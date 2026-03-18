using UnityEngine;
using UnityEngine.InputSystem;

namespace MysteryRoom.Puzzle
{
    /// <summary>
    /// 마우스 우클릭을 누른 상태로 드래그하여
    /// 캐스트 퍼즐(중앙) 주위를 카메라가 공전(Orbit)하며 관찰할 수 있게 해주는 스크립트입니다.
    /// New Input System을 사용합니다.
    /// </summary>
    public class CameraOrbitController : MonoBehaviour
    {
        [Header("Orbit Settings")]
        public Transform target;           // 바라볼 중심점 (보통 CastPuzzleManager 오브젝트)
        public float distance = 5.0f;      // 타겟으로부터의 거리
        public float xSpeed = 0.5f;       // 좌우 회전 속도
        public float ySpeed = 0.5f;       // 상하 회전 속도
        
        [Header("Zoom Settings")]
        public float zoomSpeed = 2.0f;     // 줌 속도
        public float minDistance = 2.0f;   // 최소 줌 거리
        public float maxDistance = 15.0f;  // 최대 줌 거리

        [Header("Limits")]
        public float yMinLimit = -80f;     // 위아래 회전 최소각
        public float yMaxLimit = 80f;      // 위아래 회전 최대각

        private float x = 0.0f;
        private float y = 0.0f;
        private bool isOrbiting = false;

        private Vector3 focusCenter; // 실제 카메라가 바라볼 시각적 중심점

        void Start()
        {
            // 초기에 예쁜 3D 입체 투시(얼짱 각도)로 강제 정렬합니다. 
            // (에디터에 무작위로 방치된 카메라 각도로 시작하는 것을 방지)
            x = 45f;
            y = 30f;

            // 만약 타겟이 수동으로 지정되어 있지 않다면, 씬에 있는 퍼즐 매니저나 생성기를 자동으로 찾습니다.
            if (target == null)
            {
                CastPuzzleManager manager = FindObjectOfType<CastPuzzleManager>();
                if (manager != null)
                {
                    target = manager.transform;
                }
                else
                {
                    CastPuzzleGenerator gen = FindObjectOfType<CastPuzzleGenerator>();
                    if (gen != null) target = gen.transform;
                }

                if (target == null)
                {
                    Debug.LogWarning("[CameraOrbitController] 타겟으로 삼을 퍼즐을 씬에서 찾을 수 없습니다!");
                    focusCenter = Vector3.zero;
                }
                else
                {
                    focusCenter = target.position;
                }
            }
            else
            {
                focusCenter = target.position;
            }

            // 타겟(퍼즐)의 크기에 맞춰 카메라 초점 중앙값과 줌 인/아웃 거리를 시각적으로 완벽하게 포커싱합니다.
            if (target != null)
            {
                Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                    
                    // 빈 오브젝트 피벗이 아닌, 실제 퍼즐 기하학의 무게요소 중앙을 완벽한 추적 대상으로 삼음
                    focusCenter = bounds.center; 

                    // 완성된 퍼즐 크기와 현재 카메라의 시야각(FOV)을 수학적으로 계산하여 화면 가운데에 가득 차는 최적의 거리를 도출
                    float puzzleSize = bounds.size.magnitude;
                    float fov = Camera.main != null ? Camera.main.fieldOfView : 60f;
                    
                    // Screen framing calculation
                    distance = (puzzleSize * 0.6f) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
                    
                    minDistance = puzzleSize * 0.4f;
                    maxDistance = distance * 3.0f;
                    zoomSpeed = puzzleSize * 2.0f; 
                }
            }
        }

        void LateUpdate()
        {
            if (target == null || Mouse.current == null) return;

            // 마우스 우클릭 상태 체크
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                isOrbiting = true;
            }
            else if (Mouse.current.rightButton.wasReleasedThisFrame)
            {
                isOrbiting = false;
            }

            // 회전 처리
            if (isOrbiting)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                x += mouseDelta.x * xSpeed;
                y -= mouseDelta.y * ySpeed;

                y = ClampAngle(y, yMinLimit, yMaxLimit);
            }

            // 줌 처리 (마우스 휠 스크롤) - 마우스 스크롤이 환경에 따라 엄청 튀는 현상을 안정화
            float scrollRaw = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollRaw) > 0.01f)
            {
                float scrollDir = Mathf.Sign(scrollRaw); // 방향만 추출하여 일정하게 동작
                distance -= scrollDir * zoomSpeed * 0.1f;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }

            // 실제 카메라 위치와 회전값 적용 (타겟 오브젝트 피벗이 아닌 완벽한 bounds.center 기준)
            Quaternion rotation = Quaternion.Euler(y, x, 0);
            Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + focusCenter;

            transform.rotation = rotation;
            transform.position = position;
        }

        // 각도를 -360 ~ 360 사이로 안전하게 제한하는 헬퍼 함수
        private static float ClampAngle(float angle, float min, float max)
        {
            if (angle < -360F)
                angle += 360F;
            if (angle > 360F)
                angle -= 360F;
            return Mathf.Clamp(angle, min, max);
        }
    }
}
