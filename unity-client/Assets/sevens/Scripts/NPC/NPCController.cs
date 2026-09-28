using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 마을 NPC 컨트롤러
/// - NavMeshAgent 기반 배회(Wander) AI
/// - 대화 인터랙션
/// - 플레이어 바라보기
/// - NavMesh 바깥 스폰 시 자동 탈출
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
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

    [Header("갇힘 방지")]
    [SerializeField] private float maxSnapRadius = 50f;

    [Header("UI")]
    [SerializeField] private GameObject interactPrompt; // "E키" 안내 UI
    [SerializeField] private GameObject speechBubble;    // 말풍선 UI

    private NavMeshAgent agent;
    private float waitTimer;
    private bool isWaiting = true;
    private bool isTalking;
    private int  _failCount;
    private float _stuckTimer;

    private Animator animator;
    private VillagePlayerController talkingPlayer;
    private int currentDialogueIndex;

    private bool _initialized;

    private static readonly int AnimSpeed = Animator.StringToHash("Speed");

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        agent = GetComponent<NavMeshAgent>();
    }

    private void OnEnable()
    {
        VegasCityGenerator.OnNavMeshReady += OnNavMeshBaked;
    }

    private void OnDisable()
    {
        VegasCityGenerator.OnNavMeshReady -= OnNavMeshBaked;
    }

    private void Start()
    {
        agent.speed = wanderSpeed;
        agent.stoppingDistance = arriveThreshold;
        agent.updateRotation = true;

        if (interactPrompt != null) interactPrompt.SetActive(false);
        if (speechBubble != null) speechBubble.SetActive(false);

        // NavMesh가 이미 존재하면 바로 초기화, 아니면 OnNavMeshReady 이벤트 대기
        TryInitialize();
    }

    /// <summary>
    /// NavMesh 런타임 베이크 완료 콜백
    /// </summary>
    private void OnNavMeshBaked()
    {
        Debug.Log($"[NPCController] {npcName}: NavMesh 베이크 완료 신호 수신");
        TryInitialize();
    }

    /// <summary>
    /// NavMesh가 준비되어 있으면 에이전트를 NavMesh 위로 배치하고 배회 시작.
    /// NavMesh가 아직 없으면 아무것도 하지 않음 (이벤트 대기).
    /// </summary>
    private void TryInitialize()
    {
        if (_initialized) return;

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.vertices.Length == 0)
        {
            Debug.Log($"[NPCController] {npcName}: NavMesh 아직 없음 — 베이크 대기 중");
            return;
        }

        Debug.Log($"[NPCController] {npcName}: NavMesh 발견 (삼각형={tri.indices.Length / 3}) → 초기화 시작");

        EnsureOnNavMesh();
        waitTimer = Random.Range(waitTimeMin, waitTimeMax);
        _initialized = true;
    }

    private void Update()
    {
        if (isTalking)
        {
            // 대화 중: 에이전트 정지 + 플레이어를 바라봄
            if (agent.hasPath) agent.ResetPath();

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

            animator?.SetFloat(AnimSpeed, 0f, 0.1f, Time.deltaTime);

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

        // 초기화 전이면 배회하지 않음
        if (!_initialized) return;

        // 배회 AI
        WanderUpdate();
    }

    #region NavMesh Wander AI

    private void WanderUpdate()
    {
        if (!agent.isOnNavMesh)
        {
            EnsureOnNavMesh();
            return;
        }

        bool moving = !agent.pathPending && agent.velocity.sqrMagnitude > 0.01f;
        animator?.SetFloat(AnimSpeed, moving ? 1f : 0f, 0.1f, Time.deltaTime);

        // 갇힘 감지: 이동 명령이 있는데 일정 시간 이상 못 움직이면 탈출
        if (!isWaiting && agent.hasPath && !moving)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer > 4f)
            {
                Debug.Log($"[NPCController] {npcName}: 4초간 이동 불가 → 탈출 시도");
                ForceEscape();
                _stuckTimer = 0f;
                return;
            }
        }
        else
        {
            _stuckTimer = 0f;
        }

        if (isWaiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0)
            {
                isWaiting = false;
                MoveToRandom();
            }
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            isWaiting = true;
            waitTimer = Random.Range(waitTimeMin, waitTimeMax);
        }
    }

    private void MoveToRandom()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector3 randomDir = Random.insideUnitSphere * wanderRadius;
            randomDir += transform.position;

            if (!NavMesh.SamplePosition(randomDir, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
                continue;

            NavMeshPath path = new NavMeshPath();
            if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                agent.SetPath(path);
                _failCount = 0;
                return;
            }
        }

        _failCount++;
        if (_failCount >= 3)
        {
            Debug.LogWarning($"[NPCController] {npcName}: 경로 탐색 {_failCount}회 연속 실패 → 탈출 시도");
            ForceEscape();
            _failCount = 0;
        }
        else
        {
            isWaiting = true;
            waitTimer = 1f;
        }
    }

    #endregion

    #region NavMesh 탈출

    private void ForceEscape()
    {
        if (agent.isOnNavMesh) agent.ResetPath();

        float[] radii = { 10f, 20f, 35f, maxSnapRadius };
        foreach (float r in radii)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * r;
                Vector3 candidate = transform.position + dir;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, r * 0.5f, NavMesh.AllAreas))
                    continue;

                // 워프 후 그 지점에서 다시 이동 가능한지 테스트
                Vector3 testDir = Random.insideUnitSphere * wanderRadius + hit.position;
                if (!NavMesh.SamplePosition(testDir, out NavMeshHit testHit, wanderRadius, NavMesh.AllAreas))
                    continue;

                NavMeshPath testPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(hit.position, testHit.position, NavMesh.AllAreas, testPath))
                    continue;
                if (testPath.status != NavMeshPathStatus.PathComplete)
                    continue;

                agent.Warp(hit.position);
                Debug.Log($"[NPCController] {npcName}: 탈출 워프 성공 (반경 {r}m 방향 {i * 30}°)");
                isWaiting = false;
                MoveToRandom();
                return;
            }
        }

        // 최후의 수단: 원점 근처 NavMesh로 워프
        if (NavMesh.SamplePosition(Vector3.zero, out NavMeshHit origin, maxSnapRadius, NavMesh.AllAreas))
        {
            agent.Warp(origin.position);
            Debug.LogWarning($"[NPCController] {npcName}: 원점 근처로 긴급 워프");
        }
        else
        {
            Debug.LogError($"[NPCController] {npcName}: NavMesh를 전혀 찾을 수 없음!");
        }
    }

    private void EnsureOnNavMesh()
    {
        if (agent.isOnNavMesh) return;

        // 가장 가까운 NavMesh 지점으로 워프 시도
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, maxSnapRadius, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
            Debug.Log($"[NPCController] {npcName}: NavMesh 위로 스냅 워프");
        }
        else
        {
            ForceEscape();
        }
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
        Vector3 origin = transform.position;
        Gizmos.color = new Color(0, 1, 1, 0.2f);
        Gizmos.DrawSphere(origin, wanderRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(origin, wanderRadius);
    }
}
