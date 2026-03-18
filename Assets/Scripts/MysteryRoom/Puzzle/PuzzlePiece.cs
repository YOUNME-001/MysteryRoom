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
        [HideInInspector] public float puzzleTotalSize = 1.0f; // 마우스 이동 속도 보정용

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

            // [프리팹 베이킹 검증] 이 조각이 이미 한 번 동적으로 생성되어 테두리 렌더링 세팅이 끝난 프리팹인지 확인합니다.
            bool isAlreadyBaked = transform.Find("SolidEdgeFrames") != null || GetComponentsInChildren<Renderer>().Length == 0;

            if (!isAlreadyBaked)
            {
                // 아직 렌더링된 적 없는 날것의 큐브 덩어리일 경우, 예쁜 투명 재질과 테두리를 입히는 시각화 과정을 진행합니다.
                Renderer[] renderers = GetComponentsInChildren<Renderer>();
                
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("HDRP/Lit");
                if (shader == null) shader = Shader.Find("Standard");

                if (shader != null && renderers.Length > 0)
                {
                    Material mat = new Material(shader);
                    
                    // 안쪽 조각이 명확하게 들여다보이도록 맑고 투명한 유리(Glass) 느낌으로 무작위 색상 부여
                    float hue = Random.Range(0f, 1f);
                    float saturation = Random.Range(0.1f, 0.5f);  // 은은한 틴트(Tint) 색감
                    float value = Random.Range(0.8f, 1.0f); // 밝고 영롱하게
                    Color baseColor = Color.HSVToRGB(hue, saturation, value);
                    baseColor.a = 0.2f; // 20% 불투명도 적용 (매우 투명하게 내부가 다 보임)
                    
                    if (mat.HasProperty("_Color")) mat.color = baseColor;
                    else if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
                    
                    // 빛이 많이 맺히도록 메탈릭은 살짝 덜어내고 매끄러움(Smoothness)을 극대화 (유리 표면 느낌)
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.2f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.95f);
                    else if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.95f);
                    
                    // 페이드아웃 효과를 위해 Transparent(투명 렌더링) 모드 설정 파라미터 완벽 활성화
                    mat.SetFloat("_Mode", 3); // Standard Shader의 Transparent mode
                    mat.SetFloat("_Surface", 1); // URP의 Transparent mode (0:Opaque, 1:Transparent)
                    
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); // URP Keyword
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                    foreach (Renderer rend in renderers)
                    {
                        rend.material = mat;
                    }

                    // 뒷 배경이나 뒤 조각의 테두리가 확실하게 보이도록 각 큐브마다 단단한 불투명 3D 테두리를 만들어 줍니다.
                    Material edgeMat = new Material(shader);
                    // 테두리는 몸통 색상과 같지만 매우 진하고 완전 불투명하게 (Alpha = 1.0) 설정
                    Color edgeColor = Color.HSVToRGB(hue, saturation, value * 0.7f); 
                    edgeColor.a = 1.0f; // 테두리는 불투명

                    if (edgeMat.HasProperty("_Color")) edgeMat.color = edgeColor;
                    else if (edgeMat.HasProperty("_BaseColor")) edgeMat.SetColor("_BaseColor", edgeColor);

                    if (edgeMat.HasProperty("_Metallic")) edgeMat.SetFloat("_Metallic", 0.8f);
                    if (edgeMat.HasProperty("_Smoothness")) edgeMat.SetFloat("_Smoothness", 0.5f);

                    DrawSolidEdges(renderers, edgeMat);
                }
            }
        }

        private void DrawSolidEdges(Renderer[] renderers, Material edgeMat)
        {
            // 구버전 및 중복 생성된 테두리들 싹 청소하기
            foreach (Renderer r in renderers)
            {
                Transform old = r.transform.Find("SolidEdgeFrames");
                if (old != null) DestroyImmediate(old.gameObject);
            }
            Transform rootOld = transform.Find("SolidEdgeFrames");
            if (rootOld != null) DestroyImmediate(rootOld.gameObject);

            GameObject edgeContainer = new GameObject("SolidEdgeFrames");
            edgeContainer.transform.SetParent(this.transform);
            edgeContainer.transform.localPosition = Vector3.zero;
            edgeContainer.transform.localRotation = Quaternion.identity;
            edgeContainer.transform.localScale = Vector3.one;

            // 블록 로컬 좌표를 정수로 관리 (크기를 2배로 계산하여 0.5 단위를 1단위로 만듦)
            HashSet<Vector3Int> voxels = new HashSet<Vector3Int>();
            foreach (Renderer rend in renderers)
            {
                // Edge를 구성할 대상은 기본 큐브(렌더러)만 해당
                if (rend.name.StartsWith("CastPiece") || rend.name.StartsWith("SolidEdge")) continue;
                Vector3 p = rend.transform.localPosition;
                voxels.Add(new Vector3Int(Mathf.RoundToInt(p.x * 2f), Mathf.RoundToInt(p.y * 2f), Mathf.RoundToInt(p.z * 2f)));
            }

            HashSet<Vector3Int> processedEdges = new HashSet<Vector3Int>();
            List<Vector3Int> edgesX = new List<Vector3Int>();
            List<Vector3Int> edgesY = new List<Vector3Int>();
            List<Vector3Int> edgesZ = new List<Vector3Int>();

            // 각 큐브마다 12개의 모서리 좌표를 탐색
            foreach (Vector3Int v in voxels)
            {
                Vector3Int[] edgeCenters = new Vector3Int[] {
                    new Vector3Int(v.x, v.y-1, v.z-1), new Vector3Int(v.x, v.y+1, v.z-1), new Vector3Int(v.x, v.y-1, v.z+1), new Vector3Int(v.x, v.y+1, v.z+1), // X평행
                    new Vector3Int(v.x-1, v.y, v.z-1), new Vector3Int(v.x+1, v.y, v.z-1), new Vector3Int(v.x-1, v.y, v.z+1), new Vector3Int(v.x+1, v.y, v.z+1), // Y평행
                    new Vector3Int(v.x-1, v.y-1, v.z), new Vector3Int(v.x+1, v.y-1, v.z), new Vector3Int(v.x-1, v.y+1, v.z), new Vector3Int(v.x+1, v.y+1, v.z)  // Z평행
                };

                for (int i = 0; i < 12; i++)
                {
                    Vector3Int edge = edgeCenters[i];
                    if (processedEdges.Contains(edge)) continue;
                    processedEdges.Add(edge);

                    int axis = (i < 4) ? 0 : ((i < 8) ? 1 : 2);
                    bool v0 = false, v1 = false, v2 = false, v3 = false;

                    // 이 모서리를 공유하는 4방향의 복셀 칸이 내 몸통에 얼마나 있는지 확인
                    if (axis == 0) { // X
                        v0 = voxels.Contains(new Vector3Int(edge.x, edge.y-1, edge.z-1));
                        v1 = voxels.Contains(new Vector3Int(edge.x, edge.y+1, edge.z-1));
                        v2 = voxels.Contains(new Vector3Int(edge.x, edge.y+1, edge.z+1));
                        v3 = voxels.Contains(new Vector3Int(edge.x, edge.y-1, edge.z+1));
                    } else if (axis == 1) { // Y
                        v0 = voxels.Contains(new Vector3Int(edge.x-1, edge.y, edge.z-1));
                        v1 = voxels.Contains(new Vector3Int(edge.x+1, edge.y, edge.z-1));
                        v2 = voxels.Contains(new Vector3Int(edge.x+1, edge.y, edge.z+1));
                        v3 = voxels.Contains(new Vector3Int(edge.x-1, edge.y, edge.z+1));
                    } else { // Z
                        v0 = voxels.Contains(new Vector3Int(edge.x-1, edge.y-1, edge.z));
                        v1 = voxels.Contains(new Vector3Int(edge.x+1, edge.y-1, edge.z));
                        v2 = voxels.Contains(new Vector3Int(edge.x+1, edge.y+1, edge.z));
                        v3 = voxels.Contains(new Vector3Int(edge.x-1, edge.y+1, edge.z));
                    }

                    int count = (v0?1:0) + (v1?1:0) + (v2?1:0) + (v3?1:0);
                    bool shouldDraw = false;

                    // 1개(바깥 뾰족 모서리)이거나 3개(안쪽 접히는 모서리)면 그린다.
                    if (count == 1 || count == 3) shouldDraw = true;
                    else if (count == 2) {
                        // 2개일 때, 서로 대각선으로 맞닿아 있으면(계단식) 꺾이는 모서리이므로 그린다. (평평한 면이면 안 그림)
                        if ((v0 && v2) || (v1 && v3)) shouldDraw = true;
                    }

                    if (shouldDraw) {
                        if (axis == 0) edgesX.Add(edge);
                        else if (axis == 1) edgesY.Add(edge);
                        else edgesZ.Add(edge);
                    }
                }
            }

            float t = 0.04f;
            float l = 1.0f + t; // 모서리끼리 겹치는 곳 보완
            void SpawnLine(Vector3Int center2, Vector3 scale) {
                GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(line.GetComponent<Collider>());
                line.transform.SetParent(edgeContainer.transform);
                line.transform.localPosition = new Vector3(center2.x * 0.5f, center2.y * 0.5f, center2.z * 0.5f);
                line.transform.localRotation = Quaternion.identity;
                line.transform.localScale = scale;
                line.GetComponent<Renderer>().sharedMaterial = edgeMat;
            }

            foreach (var e in edgesX) SpawnLine(e, new Vector3(l, t, t));
            foreach (var e in edgesY) SpawnLine(e, new Vector3(t, l, t));
            foreach (var e in edgesZ) SpawnLine(e, new Vector3(t, t, l));
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
                    // 큐브의 전체 크기(puzzleTotalSize)에 비례해서 드래그 속도를 자연스럽게 자동 스케일링
                    float currentDragSpeed = 5.0f * puzzleTotalSize; 
                    Vector3 targetVelocity = moveDir * currentDragSpeed;

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

            // 프리팹 코어가 월드 맵 어디에 위치하든 무관하게(World Origin 강제 배제),
            // 조각이 부모(=퍼즐 조립 중심)의 원점에서 일정 거리 이상 완전히 빠져나왔는지 확인합니다.
            if (transform.localPosition.magnitude > unlockDistance)
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

            // 전체 퍼즐 생성기(매니저)에 이 조각이 풀렸음을 보고함
            CastPuzzleManager manager = GetComponentInParent<CastPuzzleManager>();
            if (manager != null)
            {
                manager.ReportPieceSolved();
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
