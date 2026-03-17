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
        public bool isSolved = false; // 퍼즐에서 완전히 분리되었는지 여부

        [Header("Unlock Condition")]
        public float unlockDistance = 1.5f; // 분리되기 위해 중심에서부터 떨어져야 하는 거리

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

            // 여러 자식 큐브로 이루어진 테트리스 형태를 위해 자식들의 모든 렌더러에 재질 적용
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            if (shader != null && renderers.Length > 0)
            {
                Material mat = new Material(shader);
                
                // 실제 주물/금속 퍼즐(Cast Puzzle) 느낌을 위해 채도를 대폭 낮춥니다 (0.0 ~ 0.15 극저채도)
                float hue = Random.Range(0f, 1f);
                float saturation = Random.Range(0.0f, 0.15f); // 쇠, 흑철, 은 느낌
                float value = Random.Range(0.3f, 0.8f); // 흑철처럼 어둡거나 은처럼 밝은 명도 모두 포함
                mat.color = Color.HSVToRGB(hue, saturation, value);
                
                // 금속성(Metallic)과 매끄러움(Smoothness)을 극대화하여 실제 금속 고리/블럭 느낌 제공
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 1.0f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
                else if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.85f);
                
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
            if (isSolved || rb == null) return;

            if (isDragging && Mouse.current != null)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                
                // 마우스 이동량이 있을 때만 물리 이동 연산 수행
                if (mouseDelta.sqrMagnitude > 0.01f)
                {
                    // 카메라 이동 벡터
                    Vector3 rawMoveDir = mainCam.transform.right * mouseDelta.x + mainCam.transform.up * mouseDelta.y;
                    
                    // 대각선 이동으로 인한 블록 틈새 끼임(Overlap)을 완벽히 방지하려면, 
                    // Soma Cube 특성상 직각인 X, Y, Z 세 축 중 가장 마우스 이동이 큰 단일 축으로만(Snap) 움직여야 합니다.
                    Vector3 absDir = new Vector3(Mathf.Abs(rawMoveDir.x), Mathf.Abs(rawMoveDir.y), Mathf.Abs(rawMoveDir.z));
                    Vector3 moveDir = Vector3.zero;
                    
                    if (absDir.x > absDir.y && absDir.x > absDir.z) moveDir = new Vector3(Mathf.Sign(rawMoveDir.x), 0, 0);
                    else if (absDir.y > absDir.x && absDir.y > absDir.z) moveDir = new Vector3(0, Mathf.Sign(rawMoveDir.y), 0);
                    else moveDir = new Vector3(0, 0, Mathf.Sign(rawMoveDir.z));

                    // 직접 velocity를 덮어씌우면 벽(다른 조각)에 눌러붙어 파고들기 때문에,
                    // AddForce 모델을 사용하여 유니티 물리엔진이 자연스럽게 반발력을 처리하게 둡니다.
                    Vector3 targetVelocity = moveDir * 5.0f;
                    Vector3 velocityDifference = targetVelocity - rb.linearVelocity;
                    rb.AddForce(velocityDifference * 20f, ForceMode.Acceleration);
                }
                else
                {
                    // 마우스 멈췄을 때 끼임 없이 멈추기 위해 부드러운 감속 처리
                    rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, 15f * Time.fixedDeltaTime);
                }
            }
            else
            {
                rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, 15f * Time.fixedDeltaTime); // 드래그 안할 때는 감속
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
                        isDragging = true;
                        if (rb != null) rb.isKinematic = false; // 드래그 시작 시 물리 기반 이동을 위해 Kinematic 해제
                    }
                }
            }
            // 마우스 버튼 뗄 때 드래그 종료
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (isDragging)
                {
                    isDragging = false;
                    if (rb != null)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.isKinematic = true; // 드래그 종료 시 다시 굳건한 벽 역할을 위해 Kinematic 활성화
                    }
                }
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


    }
}
