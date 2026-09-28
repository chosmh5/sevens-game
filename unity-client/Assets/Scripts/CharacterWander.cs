using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMesh 위에서 랜덤 목적지를 향해 걸어다니는 컴포넌트.
/// Animator에 bool 파라미터 "IsWalking"이 있어야 합니다.
///
/// ★ 건물 내부 등 NavMesh 바깥에 스폰되어도 자동으로 유효한 NavMesh 위치로 워프합니다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class CharacterWander : MonoBehaviour
{
    [SerializeField] private float wanderRadius  = 6f;
    [SerializeField] private float minWaitTime   = 1.5f;
    [SerializeField] private float maxWaitTime   = 4.0f;
    [SerializeField] private float moveSpeed     = 1.2f;

    [Header("갇힘 방지")]
    [Tooltip("NavMesh 스냅에 사용할 최대 검색 반경")]
    [SerializeField] private float maxSnapRadius  = 50f;

    private NavMeshAgent agent;
    private Animator     animator;
    private float        waitTimer;
    private bool         isWaiting;
    private int          _failCount;
    private float        _stuckTimer;     // 움직이지 못한 시간 추적
    private bool         _paused;          // Pause 상태 (isStopped 보다 신뢰성 있는 자체 플래그)

    static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");

    void Start()
    {
        agent    = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController == null)
            animator = null;
        agent.speed            = moveSpeed;
        agent.stoppingDistance = 0.3f;

        // 회전을 직접 제어한다. (NavMeshAgent 기본 회전은 목적지에 도착하는 순간
        // 마지막 접근 방향으로 본체를 돌려, 좁은 공간에서 매번 같은 각도로 "홱"
        // 돌아가는 것처럼 보인다. 정지 시 현재 방향을 유지하기 위해 끈다.)
        agent.updateRotation = false;

        // NavMesh 위에 없으면 유효 지점으로 워프
        EnsureOnNavMesh();
        MoveToRandom();
    }

    public void Pause()
    {
        _paused = true;
        agent.ResetPath();
        agent.isStopped = true;
        // Unity의 fake-null(파괴/미할당된 네이티브 오브젝트)은 C# ?. 가 걸러내지 못하므로
        // UnityEngine.Object 의 오버로드된 != 비교로 가드한다.
        if (animator != null) animator.SetBool(IsWalkingHash, false);
    }

    public void Resume()
    {
        _paused         = false;
        agent.isStopped = false;
        isWaiting       = false;
        _failCount      = 0;
        _stuckTimer     = 0f;
        MoveToRandom();
    }

    void Update()
    {
        // Pause 중에는 어떤 경우에도 멈춰 있게 한다.
        // (Unity 의 isStopped 는 프레임이 지나며 풀릴 수 있고, Start 의 MoveToRandom 등으로
        //  잔여 경로가 생기면 에이전트가 스스로 이동/탈출워프(ForceEscape)해버린다 → 순간이동+배회 버그)
        if (_paused)
        {
            if (agent.isOnNavMesh)
            {
                if (agent.hasPath) agent.ResetPath();
                agent.velocity = Vector3.zero;   // 회피(avoidance) 밀림·잔여 속도까지 차단해 완전히 고정
            }
            return;
        }

        if (agent.isStopped) return;

        bool moving = !agent.pathPending && agent.velocity.sqrMagnitude > 0.01f;
        if (animator != null) animator.SetBool(IsWalkingHash, moving);

        // 이동 중에만 진행 방향으로 회전. 멈추면 마지막 방향을 그대로 유지한다.
        if (moving)
        {
            Vector3 v = agent.velocity;
            v.y = 0f;
            if (v.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, Quaternion.LookRotation(v),
                    agent.angularSpeed * Time.deltaTime);
        }

        // ── 갇힘 감지: 이동 명령이 있는데 일정 시간 이상 못 움직이면 탈출 ──
        if (!isWaiting && agent.hasPath && !moving)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer > 4f) // 4초간 제자리
            {
                Debug.Log($"[CharacterWander] {gameObject.name}: 4초간 이동 불가 → 탈출 시도");
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
            if (waitTimer <= 0f)
            {
                isWaiting = false;
                MoveToRandom();
            }
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            isWaiting = true;
            waitTimer = Random.Range(minWaitTime, maxWaitTime);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  이동 목적지 선택
    // ══════════════════════════════════════════════════════════════

    void MoveToRandom()
    {
        // 여러 번 시도해서 실제 도달 가능한 목적지를 찾는다
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector3 randomDir = Random.insideUnitSphere * wanderRadius;
            randomDir += transform.position;

            if (!NavMesh.SamplePosition(randomDir, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
                continue;

            // ★ 핵심: 경로가 실제로 완전한지 검증 (partial path 거부)
            NavMeshPath path = new NavMeshPath();
            if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                agent.SetPath(path);
                _failCount = 0;
                return;
            }
        }

        // 5회 시도 모두 실패
        _failCount++;
        if (_failCount >= 3)
        {
            Debug.LogWarning($"[CharacterWander] {gameObject.name}: 경로 탐색 {_failCount}회 연속 실패 → 탈출 시도");
            ForceEscape();
            _failCount = 0;
        }
        else
        {
            isWaiting = true;
            waitTimer = 1f;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  갇힘 탈출
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 고립 영역에서 탈출: 넓은 범위에서 도달 가능한 NavMesh 지점을 찾아 워프.
    /// 단순히 가장 가까운 NavMesh 점이 아니라, 실제로 넓은 영역과
    /// 연결된 지점을 찾는다.
    /// </summary>
    void ForceEscape()
    {
        agent.ResetPath();

        float[] radii = { 10f, 20f, 35f, maxSnapRadius };
        foreach (float r in radii)
        {
            // 여러 방향으로 시도
            for (int i = 0; i < 12; i++)
            {
                // 수평면에서 균등 분포
                Vector3 dir = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * r;
                Vector3 candidate = transform.position + dir;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, r * 0.5f, NavMesh.AllAreas))
                    continue;

                // 워프 후 그 지점에서 다시 랜덤 이동이 가능한지 테스트
                // (고립 영역이 아닌지 확인)
                Vector3 testDir = Random.insideUnitSphere * wanderRadius + hit.position;
                if (!NavMesh.SamplePosition(testDir, out NavMeshHit testHit, wanderRadius, NavMesh.AllAreas))
                    continue;

                NavMeshPath testPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(hit.position, testHit.position, NavMesh.AllAreas, testPath))
                    continue;
                if (testPath.status != NavMeshPathStatus.PathComplete)
                    continue;

                // 유효한 열린 영역 발견 — 워프
                agent.Warp(hit.position);
                Debug.Log($"[CharacterWander] {gameObject.name}: 탈출 워프 성공 (반경 {r}m 방향 {i * 30}°)");
                MoveToRandom();
                return;
            }
        }

        // 최후의 수단: 월드 원점 근처 NavMesh로 워프
        if (NavMesh.SamplePosition(Vector3.zero, out NavMeshHit origin, maxSnapRadius, NavMesh.AllAreas))
        {
            agent.Warp(origin.position);
            Debug.LogWarning($"[CharacterWander] {gameObject.name}: 원점 근처로 긴급 워프");
        }
        else
        {
            Debug.LogError($"[CharacterWander] {gameObject.name}: NavMesh를 전혀 찾을 수 없음!");
        }
    }

    /// <summary>Start 시 NavMesh 위에 올려놓기</summary>
    void EnsureOnNavMesh()
    {
        if (agent.isOnNavMesh) return;

        // agent가 NavMesh 위가 아니면 강제 워프
        ForceEscape();
    }
}
