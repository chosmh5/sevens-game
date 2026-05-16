using UnityEngine;

/// <summary>
/// 마을 캐릭터 이동 컨트롤러 (모바일 + 에디터 겸용)
/// - 모바일: VirtualJoystick으로 이동
/// - 에디터: WASD 키보드로 이동
/// - 카메라 고정 각도 기준 이동 방향
/// - MobileInteractButton 연동
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class VillagePlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float runSpeed = 7f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float gravity = -15f;

    [Header("Interaction")]
    [SerializeField] private float interactionRange = 2.5f;
    [SerializeField] private LayerMask interactableLayer;

    [Header("Run Threshold (Joystick)")]
    [SerializeField] private float runThreshold = 0.85f; // 조이스틱 이 이상 밀면 달리기

    private CharacterController controller;
    private Animator animator;
    private Vector3 velocity;
    private VegasPlayerRespawner respawner; // 공중 리스폰 입력 잠금 연동 (없을 수도 있음)

    // 인터랙션
    private IInteractable currentInteractable;
    private bool isInteracting;

    // 애니메이터 해시
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimInteract = Animator.StringToHash("Interact");

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        respawner = GetComponent<VegasPlayerRespawner>();
    }

    private void Update()
    {
        if (isInteracting) return;

        HandleMovement();
        HandleInteraction();
    }

    private void HandleMovement()
    {
        // respawner는 런타임에 동적으로 추가될 수 있어 lazily 재조회
        if (respawner == null) respawner = GetComponent<VegasPlayerRespawner>();

        // 공중 리스폰 직후엔 수평 입력 무시 (중력은 그대로 적용 → 자연 낙하 + 착지 후 자동 해제)
        bool inputLocked = respawner != null && respawner.IsInputLocked();

        Vector2 input = inputLocked ? Vector2.zero : GetInput();
        float inputMagnitude = input.magnitude;

        if (inputMagnitude >= 0.1f)
        {
            // 카메라 yaw 기준 이동 방향 계산
            float cameraYaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
            float targetAngle = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg + cameraYaw;

            // 부드러운 회전
            float smoothAngle = Mathf.LerpAngle(
                transform.eulerAngles.y,
                targetAngle,
                rotationSpeed * Time.deltaTime
            );
            transform.rotation = Quaternion.Euler(0, smoothAngle, 0);

            // 조이스틱 많이 밀면 달리기
            bool isRunning = inputMagnitude > runThreshold;
            float currentSpeed = isRunning ? runSpeed : moveSpeed;

            Vector3 moveDir = Quaternion.Euler(0, targetAngle, 0) * Vector3.forward;
            controller.Move(moveDir * currentSpeed * Time.deltaTime);
        }

        // 중력 (입력 잠금과 무관하게 항상 적용)
        if (controller.isGrounded && velocity.y < 0)
            velocity.y = -2f;

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // 애니메이션
        if (animator != null)
        {
            float animSpeed = inputMagnitude > runThreshold ? inputMagnitude * 2f : inputMagnitude;
            animator.SetFloat(AnimSpeed, animSpeed, 0.15f, Time.deltaTime);
        }
    }

    /// <summary>
    /// 조이스틱 또는 키보드 입력을 통합 반환
    /// </summary>
    private Vector2 GetInput()
    {
        // 사이버펑크 조이스틱 우선
        if (CyberpunkJoystick.Instance != null && CyberpunkJoystick.Instance.IsPressed)
        {
            return CyberpunkJoystick.Instance.InputValue;
        }

        // 레거시 조이스틱 폴백
        if (VirtualJoystick.Instance != null && VirtualJoystick.Instance.IsPressed)
        {
            return VirtualJoystick.Instance.InputValue;
        }

        // 에디터 키보드 폴백
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector2 kb = new Vector2(h, v);
        return kb.magnitude > 1f ? kb.normalized : kb;
    }

    private void HandleInteraction()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, interactionRange, interactableLayer);
        IInteractable nearest = null;
        float nearestDist = float.MaxValue;

        foreach (Collider hit in hits)
        {
            IInteractable interactable = hit.GetComponent<IInteractable>();
            if (interactable != null && interactable.CanInteract())
            {
                float dist = Vector3.Distance(transform.position, hit.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = interactable;
                }
            }
        }

        // 대상 변경 시
        if (nearest != currentInteractable)
        {
            currentInteractable?.OnFocusLost();
            currentInteractable = nearest;
            currentInteractable?.OnFocused();

            // 사이버펑크 HUD 인터랙션 버튼 표시/숨김
            if (currentInteractable != null)
            {
                CyberpunkHUD.Instance?.ShowInteract(() => DoInteract());
                MobileInteractButton.Instance?.Show(() => DoInteract()); // 레거시 폴백
            }
            else
            {
                CyberpunkHUD.Instance?.HideInteract();
                MobileInteractButton.Instance?.Hide(); // 레거시 폴백
            }
        }

        // 에디터에서 E키 폴백
#if UNITY_EDITOR
        if (currentInteractable != null && Input.GetKeyDown(KeyCode.E))
        {
            DoInteract();
        }
#endif
    }

    private void DoInteract()
    {
        if (currentInteractable == null) return;

        isInteracting = true;
        animator?.SetTrigger(AnimInteract);
        currentInteractable.Interact(this);
        CyberpunkHUD.Instance?.HideInteract();
        MobileInteractButton.Instance?.Hide();
    }

    public void EndInteraction()
    {
        isInteracting = false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}
