using UnityEngine;

/// <summary>
/// 오픈월드 RPG 스타일 숄더뷰 카메라
/// - 캐릭터 뒤 + 약간 위에서 바라보는 3인칭
/// - 마우스/터치 드래그로 카메라 회전
/// - 스크롤/핀치로 줌인아웃
/// - 장애물(건물) 충돌 시 카메라 당김
/// - SmoothDamp으로 부드러운 추적
/// </summary>
public class VillageCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Shoulder View")]
    [Tooltip("캐릭터로부터의 기본 거리")]
    [SerializeField] private float distance = 3.0f;
    [Tooltip("캐릭터 위 높이 오프셋")]
    [SerializeField] private float heightOffset = 0f;
    [Tooltip("카메라가 바라보는 높이 (캐릭터 기준)")]
    [SerializeField] private float lookAtHeight = 0.8f;

    [Header("Rotation")]
    [Tooltip("수평 회전 감도")]
    [SerializeField] private float horizontalSensitivity = 3f;
    [Tooltip("수직 회전 감도")]
    [SerializeField] private float verticalSensitivity = 2f;
    [Tooltip("최소 수직 각도 (아래 보기)")]
    [SerializeField] private float minPitch = -10f;
    [Tooltip("최대 수직 각도 (위 보기)")]
    [SerializeField] private float maxPitch = 60f;
    [Tooltip("터치 회전 감도")]
    [SerializeField] private float touchSensitivity = 0.3f;

    [Header("Zoom")]
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 12f;
    [SerializeField] private float scrollSensitivity = 2f;
    [SerializeField] private float pinchSensitivity = 0.02f;

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.1f;
    [SerializeField] private float rotationSmoothSpeed = 10f;
    [SerializeField] private float zoomSmoothTime = 0.15f;

    [Header("Collision")]
    [Tooltip("카메라 충돌 검사 레이어")]
    [SerializeField] private LayerMask collisionLayers = ~0;
    [Tooltip("벽에서 카메라를 떨어뜨리는 거리")]
    [SerializeField] private float collisionPadding = 0.3f;
    [Tooltip("카메라 충돌 구체 반경")]
    [SerializeField] private float collisionRadius = 0.2f;

    // 현재 상태
    private float currentYaw = 0f;
    private float currentPitch = 5f;   // 초기 각도: 거의 수평 (어깨 높이)
    private float currentDistance;
    private float targetDistance;

    // Smoothing
    private Vector3 positionVelocity;
    private float zoomVelocity;

    // Touch 처리
    private int rotationTouchId = -1;
    private Vector2 lastTouchPos;
    private bool isDragging = false;

    void Start()
    {
        currentDistance = distance;
        targetDistance = distance;

        if (target != null)
        {
            // 초기 yaw를 캐릭터의 정면 방향으로 설정
            currentYaw = target.eulerAngles.y;
        }

        // 즉시 위치 세팅 (시작 시 점프 방지)
        if (target != null)
        {
            UpdateCameraImmediate();
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        HandleInput();

        // 줌 부드럽게
        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref zoomVelocity, zoomSmoothTime);

        // 카메라 위치 계산
        Vector3 lookAtPoint = target.position + Vector3.up * lookAtHeight;

        // 구면 좌표로 카메라 위치 계산
        float radPitch = currentPitch * Mathf.Deg2Rad;
        float radYaw = currentYaw * Mathf.Deg2Rad;

        Vector3 cameraOffset = new Vector3(
            Mathf.Sin(radYaw) * Mathf.Cos(radPitch),
            Mathf.Sin(radPitch),
            -Mathf.Cos(radYaw) * Mathf.Cos(radPitch) // 마이너스: 캐릭터 뒤쪽
        ) * currentDistance;

        // 높이 오프셋 추가
        cameraOffset.y += heightOffset;

        Vector3 desiredPosition = lookAtPoint + cameraOffset;

        // 장애물 충돌 처리
        desiredPosition = HandleCollision(lookAtPoint, desiredPosition);

        // 부드러운 위치 이동
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref positionVelocity,
            positionSmoothTime
        );

        // 캐릭터를 바라봄
        Quaternion desiredRotation = Quaternion.LookRotation(lookAtPoint - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, Time.deltaTime * rotationSmoothSpeed);
    }

    void HandleInput()
    {
        // === 모바일 터치 ===
        if (Input.touchCount > 0)
        {
            HandleTouchInput();
            return; // 터치 있으면 마우스 입력 무시
        }

        // === PC 마우스 ===
        HandleMouseInput();
    }

    void HandleMouseInput()
    {
        // 우클릭 드래그로 회전
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            currentYaw += mouseX * horizontalSensitivity;
            currentPitch -= mouseY * verticalSensitivity;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
        }

        // 스크롤 줌
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            targetDistance -= scroll * scrollSensitivity;
            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
        }
    }

    void HandleTouchInput()
    {
        // 2손가락 핀치 줌
        if (Input.touchCount == 2)
        {
            rotationTouchId = -1;
            isDragging = false;

            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);

            float prevDist = ((t0.position - t0.deltaPosition) - (t1.position - t1.deltaPosition)).magnitude;
            float currDist = (t0.position - t1.position).magnitude;
            float delta = prevDist - currDist;

            targetDistance += delta * pinchSensitivity;
            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
            return;
        }

        // 1손가락 드래그 회전 (화면 오른쪽 절반만)
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                // 화면 오른쪽 절반에서만 카메라 회전 (왼쪽은 조이스틱)
                if (touch.position.x > Screen.width * 0.35f)
                {
                    rotationTouchId = touch.fingerId;
                    lastTouchPos = touch.position;
                    isDragging = true;
                }
            }
            else if (isDragging && touch.fingerId == rotationTouchId)
            {
                if (touch.phase == TouchPhase.Moved)
                {
                    Vector2 delta = touch.position - lastTouchPos;

                    currentYaw += delta.x * touchSensitivity;
                    currentPitch -= delta.y * touchSensitivity * 0.6f;
                    currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

                    lastTouchPos = touch.position;
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    rotationTouchId = -1;
                    isDragging = false;
                }
            }
        }
    }

    /// <summary>
    /// 카메라와 캐릭터 사이의 장애물 충돌 처리
    /// 건물 등이 가리면 카메라를 앞으로 당김
    /// </summary>
    Vector3 HandleCollision(Vector3 lookAt, Vector3 desiredPos)
    {
        Vector3 direction = desiredPos - lookAt;
        float desiredDistance = direction.magnitude;

        RaycastHit hit;
        if (Physics.SphereCast(lookAt, collisionRadius, direction.normalized,
            out hit, desiredDistance, collisionLayers))
        {
            // 플레이어 자체는 무시
            if (target != null && (hit.transform == target || hit.transform.IsChildOf(target)))
            {
                return desiredPos;
            }

            // 충돌 지점 바로 앞으로 카메라 이동
            float safeDistance = Mathf.Max(hit.distance - collisionPadding, 0.5f);
            return lookAt + direction.normalized * safeDistance;
        }

        return desiredPos;
    }

    /// <summary>
    /// 즉시 카메라 위치 설정 (초기화용)
    /// </summary>
    void UpdateCameraImmediate()
    {
        Vector3 lookAtPoint = target.position + Vector3.up * lookAtHeight;

        float radPitch = currentPitch * Mathf.Deg2Rad;
        float radYaw = currentYaw * Mathf.Deg2Rad;

        Vector3 cameraOffset = new Vector3(
            Mathf.Sin(radYaw) * Mathf.Cos(radPitch),
            Mathf.Sin(radPitch),
            -Mathf.Cos(radYaw) * Mathf.Cos(radPitch)
        ) * currentDistance;

        cameraOffset.y += heightOffset;

        transform.position = lookAtPoint + cameraOffset;
        transform.LookAt(lookAtPoint);
    }

    // === Public API ===

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
        {
            currentYaw = target.eulerAngles.y;
            UpdateCameraImmediate();
        }
    }

    public void SetAngle(float pitch, float yaw)
    {
        currentPitch = pitch;
        currentYaw = yaw;
    }

    /// <summary>
    /// 카메라 yaw를 가져옴 (캐릭터 이동 방향에 사용)
    /// </summary>
    public float GetYaw()
    {
        return currentYaw;
    }
}
