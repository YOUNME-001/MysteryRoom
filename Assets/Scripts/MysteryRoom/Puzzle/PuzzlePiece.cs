using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

namespace MysteryRoom.Puzzle
{
    /// <summary>
    /// 개별 캐스트 퍼즐 조각의 상태와 상호작용을 관리하는 클래스입니다.
    /// New Input System을 사용하여 마우스 드래그를 통해 조각을 회전시키고 지정된 탈출 각도에 도달하면 분리됩니다.
    /// </summary>
    public class PuzzlePiece : MonoBehaviour
    {
        public int pieceID;
        public bool isLocked = true; // 부모 조각에 의해 묶여있는지 여부
        public bool isSolved = false; // 퍼즐에서 완전히 분리되었는지 여부

        [Header("Unlock Condition")]
        public float unlockDistance = 4.0f; // 분리되기 위해 중심에서부터 떨어져야 하는 거리

        public PuzzlePiece parentPiece; // 이 조각이 의존하고 있는 부모 조각
        public List<PuzzlePiece> dependentPieces = new List<PuzzlePiece>(); // 내가 풀려야 풀리는 자식 조각들

        private Camera mainCam;
        private bool isDragging = false;
        private Rigidbody rb;

        void Start()
        {
            mainCam = Camera.main;
            
            // 기존에 Rigidbody가 프리팹에 이미 붙어있다면 가져오고, 없다면 새로 추가
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }

            // 시작할 때는 무조건 물리적 튕김 분리(Pop)를 방지하기 위해 Kinematic으로 일단 묶어둠 (Unlock 시 해제됨)
            rb.useGravity = false;
            rb.isKinematic = true; 
            rb.constraints = RigidbodyConstraints.FreezeRotation; // 회전 금지
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // 조각들끼리 부드럽게 미끄러지도록 콜라이더를 살짝 축소
            // (동적으로 생성될 때만 축소하고, 이미 프리팹으로 구워져 축소된 경우 중복 축소 방지)
            BoxCollider[] colliders = GetComponentsInChildren<BoxCollider>();
            foreach (BoxCollider col in colliders)
            {
                if (col.size.x >= 0.99f) // 최초 생성시에만 0.95로 줄임 (프리팹 로드 시 중복 축소 방지)
                {
                    col.size = Vector3.one * 0.95f; 
                }
            }

            // 첫 번째 조각(루트)처럼 생성 단계에서 이미 Unlock() 지시를 받았다면 다시 Kinematic을 꺼줌
            if (!isLocked)
            {
                rb.isKinematic = false;
            }

            // 여러 자식 큐브로 이루어진 테트리스 형태를 위해 자식들의 모든 렌더러에 재질 적용
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            if (shader != null && renderers.Length > 0)
            {
                Material mat = new Material(shader);
                mat.color = new Color(Random.value, Random.value, Random.value);
                
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.8f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.7f);
                else if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.7f);
                
                // 페이드아웃 효과를 위해 Transparent(투명 렌더링) 모드 설정 파라미터 활성화
                mat.SetFloat("_Mode", 3); // Standard Shader의 Transparent mode
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;

                foreach (Renderer rend in renderers)
                {
                    rend.material = mat;
                }
            }
        }

        void Update()
        {
            if (isSolved) return;

            HandleMouseInput();
            CheckUnlockCondition();
        }

        void FixedUpdate()
        {
            if (isSolved || rb == null || isLocked) return;

            if (isDragging && Mouse.current != null)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                
                // 마우스 이동량이 있을 때만 물리 이동 연산 수행
                if (mouseDelta.sqrMagnitude > 0.01f)
                {
                    // 카메라가 바라보는 방향을 기준으로 좌우/상하 이동 벡터 계산
                    Vector3 moveDir = mainCam.transform.right * mouseDelta.x + mainCam.transform.up * mouseDelta.y;
                    
                    // 위치를 강제로 이동시키지 않고 velocity를 변경하여 콜라이더끼리 자연스럽게 막히도록 처리
                    rb.linearVelocity = moveDir * 5.0f; 
                }
                else
                {
                    rb.linearVelocity = Vector3.zero;
                }
            }
            else
            {
                rb.linearVelocity = Vector3.zero; // 드래그 안할 때는 물리 감속
            }
        }

        private void HandleMouseInput()
        {
            if (Mouse.current == null) return;

            // 마우스 클릭 시 Raycast로 조각 선택
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Ray ray = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    // 클릭된 물체의 부모 중에 내가 있는지 확인 (이제 자식 큐브들이 클릭되기 때문에)
                    PuzzlePiece clickedPiece = hit.transform.GetComponentInParent<PuzzlePiece>();
                    if (clickedPiece == this)
                    {
                        if (isLocked)
                        {
                            Debug.Log($"Piece {pieceID} is locked by another piece!");
                            return;
                        }
                        isDragging = true;
                    }
                }
            }
            // 마우스 버튼 뗄 때 드래그 종료
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;
            }
        }

        private void CheckUnlockCondition()
        {
            if (isSolved) return;

            // 조각이 중앙부(0,0,0)에서 일정 거리 이상 완전히 빠져나왔는지 확인
            if (transform.position.magnitude > unlockDistance)
            {
                SolvePiece();
            }
        }

        private void SolvePiece()
        {
            isSolved = true;
            Debug.Log($"Piece {pieceID} Solved!");

            // 떨어져 나간 조각은 이제 큐브의 간섭을 받지 않도록 처리 가능 (충돌 무시)
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            // 만약 나한테 종속된 자식 조각이 있었다면, 자체적으로 락을 풀어줌 (Generator 의존도 제거)
            foreach (var piece in dependentPieces)
            {
                if (piece != null)
                {
                    piece.Unlock();
                }
            }

            // 서서히 사라지는 연출 시작
            StartCoroutine(FadeOutAndDestroyRoutine());
        }

        private IEnumerator FadeOutAndDestroyRoutine()
        {
            float duration = 1.0f; // 페이드 아웃에 걸리는 시간 (초)
            float elapsed = 0f;

            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            
            // 모든 렌더러의 초기 색상 수집
            Dictionary<Renderer, Color> initialColors = new Dictionary<Renderer, Color>();
            foreach (Renderer rend in renderers)
            {
                if (rend.material.HasProperty("_Color"))
                {
                    initialColors[rend] = rend.material.color;
                }
                else if (rend.material.HasProperty("_BaseColor")) // URP/HDRP
                {
                    initialColors[rend] = rend.material.GetColor("_BaseColor");
                }
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = elapsed / duration;
                float alpha = Mathf.Lerp(1f, 0f, normalizedTime);

                foreach (Renderer rend in renderers)
                {
                    if (initialColors.TryGetValue(rend, out Color initColor))
                    {
                        Color newColor = new Color(initColor.r, initColor.g, initColor.b, alpha);
                        if (rend.material.HasProperty("_Color"))
                        {
                            rend.material.color = newColor;
                        }
                        else if (rend.material.HasProperty("_BaseColor"))
                        {
                            rend.material.SetColor("_BaseColor", newColor);
                        }
                    }
                }
                yield return null;
            }

            // 완전히 투명해지면 오브젝트 파괴
            Destroy(gameObject);
        }

        public void Unlock()
        {
            isLocked = false;
            if (rb != null)
            {
                rb.isKinematic = false;
            }
        }
    }
}
