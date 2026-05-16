using UnityEngine;

/// <summary>
/// 마을 NPC 컨트롤러
/// - 기본 배회(Wander) AI
/// - 대화 인터랙션
/// - 플레이어 바라보기
/// </summary>
public class NPCController : MonoBehaviour, IInteractable
{
    [Header("NPC Info")]
    [SerializeField] private string npcName = "주민";
    [SerializeField] private string[] dialogueLines;

    [Header("Wander AI")]
    [SerializeField] private float wanderRadius = 5f;
    [SerializeField] private float wanderSpeed = 1.5f;
    [SerializeField] private float waitTimeMin = 2f;
    [SerializeField] private float waitTimeMax = 5f;
    [SerializeField] private float arriveThreshold = 0.3f;

    [Header("UI")]
    [SerializeField] private GameObject interactPrompt; // "E키" 안내 UI
    [SerializeField] private GameObject speechBubble;    // 말풍선 UI

    private Vector3 originPosition;
    private Vector3 wanderTarget;
    private float waitTimer;
    private bool isWaiting = true;
    private bool isTalking;

    private Animator animator;
    private VillagePlayerController talkingPlayer;
    private int currentDialogueIndex;

    private static readonly int AnimSpeed = Animator.StringToHash("Speed");

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        originPosition = transform.position;
    }

    private void Start()
    {
        SetNewWanderTarget();
        waitTimer = Random.Range(waitTimeMin, waitTimeMax);

        if (interactPrompt != null) interactPrompt.SetActive(false);
        if (speechBubble != null) speechBubble.SetActive(false);
    }

    private void Update()
    {
        if (isTalking)
        {
            // 대화 중: 플레이어를 바라봄
            if (talkingPlayer != null)
            {
                Vector3 lookDir = talkingPlayer.transform.position - transform.position;
                lookDir.y = 0;
                if (lookDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 5f * Time.deltaTime);
                }
            }

            // 화면 터치 또는 E키로 다음 대사
            bool tapInput = false;

#if UNITY_EDITOR
            tapInput = Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0);
#else
            tapInput = Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;
#endif

            if (tapInput)
            {
                AdvanceDialogue();
            }
            return;
        }

        // 배회 AI
        WanderUpdate();
    }

    #region Wander AI

    private void WanderUpdate()
    {
        if (isWaiting)
        {
            waitTimer -= Time.deltaTime;
            animator?.SetFloat(AnimSpeed, 0f, 0.1f, Time.deltaTime);

            if (waitTimer <= 0)
            {
                isWaiting = false;
                SetNewWanderTarget();
            }
            return;
        }

        // 목표 지점으로 이동
        Vector3 dir = wanderTarget - transform.position;
        dir.y = 0;

        if (dir.magnitude < arriveThreshold)
        {
            // 도착
            isWaiting = true;
            waitTimer = Random.Range(waitTimeMin, waitTimeMax);
            return;
        }

        // 이동 및 회전
        Vector3 moveDir = dir.normalized;
        transform.position += moveDir * wanderSpeed * Time.deltaTime;

        Quaternion targetRot = Quaternion.LookRotation(moveDir);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 8f * Time.deltaTime);

        animator?.SetFloat(AnimSpeed, 1f, 0.1f, Time.deltaTime);
    }

    private void SetNewWanderTarget()
    {
        Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
        wanderTarget = originPosition + new Vector3(randomCircle.x, 0, randomCircle.y);
    }

    #endregion

    #region IInteractable

    public bool CanInteract()
    {
        return !isTalking && dialogueLines != null && dialogueLines.Length > 0;
    }

    public void OnFocused()
    {
        if (interactPrompt != null)
            interactPrompt.SetActive(true);
    }

    public void OnFocusLost()
    {
        if (interactPrompt != null)
            interactPrompt.SetActive(false);
    }

    public void Interact(VillagePlayerController player)
    {
        isTalking = true;
        talkingPlayer = player;
        currentDialogueIndex = 0;

        if (interactPrompt != null) interactPrompt.SetActive(false);
        if (speechBubble != null) speechBubble.SetActive(true);

        // 대화 시작 - DialogueUI에 첫 대사 전달
        CyberpunkDialogueUI.Instance?.ShowDialogue(npcName, dialogueLines[0]);
    }

    #endregion

    #region Dialogue

    private void AdvanceDialogue()
    {
        currentDialogueIndex++;

        if (currentDialogueIndex >= dialogueLines.Length)
        {
            EndDialogue();
            return;
        }

        CyberpunkDialogueUI.Instance?.ShowDialogue(npcName, dialogueLines[currentDialogueIndex]);
    }

    private void EndDialogue()
    {
        isTalking = false;
        CyberpunkDialogueUI.Instance?.HideDialogue();

        if (speechBubble != null) speechBubble.SetActive(false);

        talkingPlayer?.EndInteraction();
        talkingPlayer = null;
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying ? originPosition : transform.position;
        Gizmos.color = new Color(0, 1, 1, 0.2f);
        Gizmos.DrawSphere(origin, wanderRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(origin, wanderRadius);
    }
}
