using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMesh 위에서 랜덤 목적지를 향해 걸어다니는 컴포넌트.
/// Animator에 bool 파라미터 "IsWalking"이 있어야 합니다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class CharacterWander : MonoBehaviour
{
    [SerializeField] private float wanderRadius  = 6f;
    [SerializeField] private float minWaitTime   = 1.5f;
    [SerializeField] private float maxWaitTime   = 4.0f;
    [SerializeField] private float moveSpeed     = 1.2f;

    private NavMeshAgent agent;
    private Animator     animator;
    private float        waitTimer;
    private bool         isWaiting;

    static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");

    void Start()
    {
        agent    = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController == null)
            animator = null;
        agent.speed     = moveSpeed;
        agent.stoppingDistance = 0.3f;
        MoveToRandom();
    }

    public void Pause()
    {
        agent.ResetPath();
        agent.isStopped = true;
        animator?.SetBool(IsWalkingHash, false);
    }

    public void Resume()
    {
        agent.isStopped = false;
        isWaiting = false;
        MoveToRandom();
    }

    void Update()
    {
        if (agent.isStopped) return;

        bool moving = !agent.pathPending && agent.velocity.sqrMagnitude > 0.01f;
        animator?.SetBool(IsWalkingHash, moving);

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

    void MoveToRandom()
    {
        // 현재 위치 기준 wanderRadius 안에서 NavMesh 위 임의 지점 탐색
        Vector3 randomDir = Random.insideUnitSphere * wanderRadius;
        randomDir += transform.position;

        if (NavMesh.SamplePosition(randomDir, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
        else
            // 실패하면 잠깐 대기 후 재시도
            waitTimer = 0.5f;
    }
}
