using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-1)]
public class ConversationManager : MonoBehaviour
{
    public static ConversationManager Instance { get; private set; }

    [SerializeField] private float conversationDistance  = 6f;   // 이 거리 안에 두 NPC가 들어오면 대화 시작
    [SerializeField] private float checkInterval         = 5f;   // 자동 짝짓기 점검 주기
    [SerializeField] private int   minTurns              = 3;    // 묶음 최소 턴
    [SerializeField] private int   maxTurns              = 6;    // 묶음 최대 턴
    [SerializeField] private float conversationCooldown  = 15f;  // 대화 종료 후 다음 대화까지 쿨다운

    private readonly List<CharacterDialogue> characters = new();
    private bool  conversationActive;
    private float lastConversationTime = -999f;

    /// <summary>현재 대화 중인 두 캐릭터 (없으면 null)</summary>
    public CharacterDialogue CurrentA { get; private set; }
    public CharacterDialogue CurrentB { get; private set; }

    /// <summary>현재 대화의 미리 받아온 대사 묶음 (관전 자막이 그대로 재생)</summary>
    public ExchangeLine[] CurrentLines { get; private set; }

    /// <summary>가끔 묶음 끝에 캐릭터가 플레이어에게 던지는 한마디(없으면 빈 문자열)</summary>
    public string CurrentPlayerQuestion { get; private set; }

    /// <summary>대사 묶음을 서버에서 받아오는 중인가(관전 자막이 준비될 때까지 기다리게).</summary>
    public bool LinesPending { get; private set; }

    /// <summary>관전/끼어들기 가능한 대화가 준비됐는가</summary>
    public bool HasReadyConversation => conversationActive && CurrentA != null && CurrentB != null;

    void Awake()
    {
        if (Instance != null) { Debug.Log("[ConversationManager] Awake — 중복 인스턴스 파괴"); Destroy(gameObject); return; }
        Instance = this;
        Debug.Log($"[ConversationManager] Awake — Instance 설정 ({gameObject.name})");
    }

    void Start()
    {
        Debug.Log($"[ConversationManager] Start — checkInterval={checkInterval}, distance={conversationDistance}, cooldown={conversationCooldown}, 등록된 캐릭터={characters.Count}");
        StartCoroutine(ConversationCheckLoop());
    }

    public void Register(CharacterDialogue c)
    {
        characters.Add(c);
        Debug.Log($"[ConversationManager] Register: {c.characterName} (총 {characters.Count}명)");
    }

    /// <summary>
    /// 특정 두 캐릭터의 대화를 즉시 시작한다. (이미 대화 중이면 무시)
    /// MyRoomBootstrapper 가 진입 시 마주보게 배치한 바로 그 쌍을 대화시키기 위해 사용한다.
    /// </summary>
    public void StartConversation(CharacterDialogue a, CharacterDialogue b)
    {
        if (a == null || b == null)
        {
            Debug.LogWarning("[ConversationManager] StartConversation 무시 — a/b null");
            return;
        }
        if (conversationActive)
        {
            Debug.LogWarning($"[ConversationManager] StartConversation({a.characterName}↔{b.characterName}) 무시 — 이미 대화 중 (현재 {(CurrentA!=null?CurrentA.characterName:"?")}↔{(CurrentB!=null?CurrentB.characterName:"?")})");
            return;
        }
        Debug.Log($"[ConversationManager] StartConversation 실행: {a.characterName} ↔ {b.characterName}");
        StartCoroutine(RunConversation(a, b));
    }

    // ══════════════════════════════════════════════════════════════════
    //  플레이어 인계 / 종료
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 플레이어가 [E]로 끼어들 때 호출 — 자동 해산 루프를 멈추고 소유권을 넘긴다.
    /// 두 캐릭터의 Pause/InConversation 은 호출자(PlayerInteraction)가 그대로 유지한다.
    /// (resume 하지 않으므로 끼어드는 순간 캐릭터가 걸어가 버리는 일이 없다.)
    /// </summary>
    public void HandToPlayer()
    {
        StopAllCoroutines();        // RunConversation(자동 해산) + 체크 루프 정지
        conversationActive = true;  // 인계된 동안에도 새 자동 대화는 막아 둔다
    }

    /// <summary>
    /// 플레이어 상호작용 종료 시 호출 — 상태를 초기화하고 쿨다운을 건 뒤 자동 점검을 재개한다.
    /// 캐릭터 Resume(배회 재개)은 호출자가 담당한다(여기서는 건드리지 않음).
    /// </summary>
    public void EndConversation()
    {
        StopAllCoroutines();
        ClearState();
        StartCoroutine(ConversationCheckLoop());
    }

    // ══════════════════════════════════════════════════════════════════
    //  자동 짝짓기
    // ══════════════════════════════════════════════════════════════════

    IEnumerator ConversationCheckLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkInterval);

            if (conversationActive) continue;
            // 플레이어가 대화/관전 중이면 새 자동 짝짓기를 멈춘다(상태 충돌·이동 방지).
            if (PlayerInteraction.Instance != null && PlayerInteraction.Instance.IsInteracting) continue;
            if (ConversationWatchMode.IsWatching) continue;
            if (Time.time - lastConversationTime < conversationCooldown) continue;
            if (characters.Count < 2) continue;

            var pair = FindNearbyPair();
            if (pair.HasValue)
            {
                Debug.Log($"[ConversationManager] 체크루프가 근접 쌍 발견 → 대화 시작: {pair.Value.a.characterName} ↔ {pair.Value.b.characterName}");
                yield return RunConversation(pair.Value.a, pair.Value.b);
            }
        }
    }

    (CharacterDialogue a, CharacterDialogue b)? FindNearbyPair()
    {
        for (int i = 0; i < characters.Count; i++)
        {
            // 플레이어 아바타는 자동 대화 대상에서 제외
            if (characters[i] == null || characters[i].GetComponent<VillagePlayerController>() != null) continue;
            for (int j = i + 1; j < characters.Count; j++)
            {
                if (characters[j] == null || characters[j].GetComponent<VillagePlayerController>() != null) continue;
                float dist = Vector3.Distance(
                    characters[i].transform.position,
                    characters[j].transform.position);
                if (dist <= conversationDistance)
                    return (characters[i], characters[j]);
            }
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════════════
    //  대화 진행
    // ══════════════════════════════════════════════════════════════════

    IEnumerator RunConversation(CharacterDialogue a, CharacterDialogue b)
    {
        Debug.Log($"[ConversationManager] RunConversation 진입: {a.characterName} ↔ {b.characterName} → InConversation=true 설정");
        conversationActive    = true;
        CurrentA = a;
        CurrentB = b;
        CurrentLines = null;
        CurrentPlayerQuestion = null;
        LinesPending = true;          // 대사 받아오는 중 — 관전 진입 시 기다리게
        a.InConversation = b.InConversation = true;

        var wanderA = a.GetComponent<CharacterWander>();
        var wanderB = b.GetComponent<CharacterWander>();
        wanderA?.Pause();
        wanderB?.Pause();
        FaceToward(a.transform, b.transform.position);
        FaceToward(b.transform, a.transform.position);

        // 대화 묶음을 한 번의 서버 왕복으로 받아온다. 받는 동안 둘은 마주보고 멈춰 기다린다.
        if (ServerClient.Instance != null)
        {
            int turns = Random.Range(minTurns, maxTurns + 1);
            yield return ServerClient.Instance.GetConversationExchange(
                a.characterName, b.characterName, turns,
                res => { CurrentLines = res?.lines; CurrentPlayerQuestion = res?.player_question; },
                ()  => { CurrentLines = null; });
        }
        LinesPending = false;         // 성공·실패 모두 받아오기 종료

        if (CurrentLines != null)
            foreach (var l in CurrentLines)
                DialogueLog.Instance?.AddEntry(l.speaker, l.line);

        Debug.Log($"[ConversationManager] 대화 준비됨: {a.characterName} ↔ {b.characterName} " +
                  $"(lines={(CurrentLines != null ? CurrentLines.Length : 0)}) — 관전/끼어들기 대기");

        // 플레이어가 관전/끼어들 때까지 두 사람은 '제자리에서' 대화 상태를 유지한다.
        // (예전엔 일정 시간 뒤 자동 해산하며 Resume → 대화 중 캐릭터가 걸어가 버리고
        //  "!"가 꺼져 1:1로만 잡히는 문제가 있었다. 그래서 자동 해산을 제거.)
        while (conversationActive)
        {
            if (CurrentA == null) yield break;   // 외부에서 종료(HandToPlayer/EndConversation)
            yield return null;
        }
    }

    void ClearState()
    {
        conversationActive    = false;
        CurrentA = CurrentB   = null;
        CurrentLines          = null;
        CurrentPlayerQuestion = null;
        LinesPending          = false;
        lastConversationTime  = Time.time;
    }

    static void FaceToward(Transform t, Vector3 target)
    {
        Vector3 dir = target - t.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f)
            t.rotation = Quaternion.LookRotation(dir);
    }
}
