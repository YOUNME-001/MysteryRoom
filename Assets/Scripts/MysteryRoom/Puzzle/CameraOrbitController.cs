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

        void Start()
        {
            Vector3 angles = transform.eulerAngles;
            x = angles.y;
            y = angles.x;

            // 만약 타겟이 수동으로 안 지정되어 있다면 자동으로 생성기 중심 찾기
            if (target == null)
            {
                if (CastPuzzleGenerator.Instance != null)
                {
                    target = CastPuzzleGenerator.Instance.transform;
                }
                else
                {
                    Debug.LogWarning("[CameraOrbitController] 타겟 오브젝트를 찾지 못했습니다! 인스펙터에 타겟을 할당하거나 게임씬에 CastPuzzleGenerator가 있어야 합니다.");
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

            // 줌 처리 (마우스 휠 스크롤)
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                distance -= scroll * zoomSpeed * 0.001f;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }

            // 실제 카메라 위치와 회전값 적용
            Quaternion rotation = Quaternion.Euler(y, x, 0);
            Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + target.position;

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
