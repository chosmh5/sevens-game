using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MyRoom 씬에 이미 배치된 VRM 캐릭터(세비-호 / 류 / 진)를 자율 행동 시스템에 연결한다.
///
/// - 누락된 지원 시스템을 생성: EventNotificationUI, PlayerInteraction, ConversationWatchMode.
///   (ConversationManager 는 씬에 이미 존재하므로 없을 때만 생성)
/// - 각 캐릭터에 CharacterWander(NavMesh 배회) + ConversationMarker("!" 표시)가
///   없으면 부착. 이미 있으면 건드리지 않는다.
///
/// 씬을 수동 편집하지 않고 런타임에 한 번 배선한다. (프로젝트의 CyberpunkUIBootstrapper 와 동일 패턴)
/// 아무것도 새로 스폰하지 않는다 — 이미 있는 아바타만 사용한다.
/// </summary>
public class MyRoomBootstrapper : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterSceneHandler()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        TryCreate(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        TryCreate(scene.name);
    }

    static void TryCreate(string sceneName)
    {
        if (sceneName != "MyRoom") return;
        if (Object.FindAnyObjectByType<MyRoomBootstrapper>() != null) return;

        var go = new GameObject("[MyRoomBootstrapper]");
        go.AddComponent<MyRoomBootstrapper>();
        Debug.Log("[MyRoomBootstrapper] MyRoom 진입 — 자율 행동 시스템 배선 시작");
    }

    void Start()
    {
        EnsureSupportSystems();
        CleanPlayerAvatar();
        var ai = WireAICharacters();

        // 테스트 편의: AI 2명을 가까이 마주보게 배치 → 진입 직후 자연스럽게 대화 시작
        if (ai.Count >= 2)
            StartCoroutine(SetupInitialMeeting(ai[0], ai[1]));
    }

    void EnsureSupportSystems()
    {
        if (ConversationManager.Instance == null)
        {
            new GameObject("[ConversationManager]").AddComponent<ConversationManager>();
            Debug.Log("[MyRoomBootstrapper] ConversationManager 생성");
        }

        if (Object.FindAnyObjectByType<EventNotificationUI>() == null)
        {
            new GameObject("[EventNotificationUI]").AddComponent<EventNotificationUI>();
            Debug.Log("[MyRoomBootstrapper] EventNotificationUI 생성");
        }

        if (ConversationWatchMode.Instance == null)
        {
            new GameObject("[ConversationWatchMode]").AddComponent<ConversationWatchMode>();
            Debug.Log("[MyRoomBootstrapper] ConversationWatchMode 생성");
        }

        if (PlayerInteraction.Instance == null)
        {
            var player = Object.FindAnyObjectByType<VillagePlayerController>();
            if (player != null)
            {
                player.gameObject.AddComponent<PlayerInteraction>();
                Debug.Log("[MyRoomBootstrapper] PlayerInteraction 생성");
            }
        }
    }

    /// <summary>
    /// 플레이어 아바타에 잘못 붙은 AI 이동 컴포넌트를 정리한다.
    /// 플레이어는 VillagePlayerController(+CharacterController)로 직접 조작하므로
    /// CharacterWander(자율 배회) / NavMeshAgent 가 있으면 조작과 충돌한다.
    /// (CharacterDialogue 유무와 무관하게 항상 검사)
    /// </summary>
    void CleanPlayerAvatar()
    {
        var player = Object.FindAnyObjectByType<VillagePlayerController>();
        if (player == null) return;
        var go = player.gameObject;

        var strayWander = go.GetComponent<CharacterWander>();
        if (strayWander != null) Destroy(strayWander);

        var strayAgent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (strayAgent != null) strayAgent.enabled = false;

        Debug.Log("[MyRoomBootstrapper] 플레이어 아바타 정리 — 자율 배회/NavMeshAgent 제거");
    }

    /// <summary>
    /// CharacterDialogue 를 가진 AI 캐릭터(세비-호 / 류 / 진)에 CharacterWander + ConversationMarker 부착.
    /// 플레이어 아바타(VillagePlayerController 보유)는 제외한다.
    /// </summary>
    List<CharacterDialogue> WireAICharacters()
    {
        var characters = Object.FindObjectsByType<CharacterDialogue>(FindObjectsSortMode.None);
        var ai = new List<CharacterDialogue>();
        foreach (var dlg in characters)
        {
            var go = dlg.gameObject;
            if (go.GetComponent<VillagePlayerController>() != null) continue; // 플레이어 제외

            // CharacterWander 는 NavMeshAgent 를 요구한다(없으면 자동 추가됨).
            if (go.GetComponent<CharacterWander>() == null)
                go.AddComponent<CharacterWander>();

            if (go.GetComponent<ConversationMarker>() == null)
                go.AddComponent<ConversationMarker>();

            ai.Add(dlg);
        }

        Debug.Log($"[MyRoomBootstrapper] AI 캐릭터 {ai.Count}명 배선 완료");
        return ai;
    }

    /// <summary>
    /// AI 두 명을 가까이(1.6m) 마주보게 배치하고, 첫 대화가 잡힐 때까지 제자리에 멈춰둔다.
    /// → ConversationManager 가 근접 판정으로 자연스럽게 대화를 시작한다.
    /// 대화 후 플레이어가 끼어들면(또는 관전 후 떠나면) 배회가 재개된다.
    /// </summary>
    IEnumerator SetupInitialMeeting(CharacterDialogue a, CharacterDialogue b)
    {
        // CharacterWander.Start(에이전트 셋업 + 초기 이동)가 먼저 돌도록 한 프레임 대기
        yield return null;

        // a 앞쪽 1.6m 지점으로 b 이동 (NavMesh 위로 스냅)
        Vector3 dir = a.transform.forward; dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
        Vector3 spot = a.transform.position + dir.normalized * 1.6f;
        if (UnityEngine.AI.NavMesh.SamplePosition(spot, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
            spot = hit.position;

        var agentB = b.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agentB != null && agentB.enabled && agentB.isOnNavMesh) agentB.Warp(spot);
        else b.transform.position = spot;

        // 서로 마주보기
        Vector3 toB = b.transform.position - a.transform.position; toB.y = 0f;
        if (toB.sqrMagnitude > 0.01f)
        {
            a.transform.rotation = Quaternion.LookRotation(toB);
            b.transform.rotation = Quaternion.LookRotation(-toB);
        }

        // 제자리 유지 (배회 정지)
        a.GetComponent<CharacterWander>()?.Pause();
        b.GetComponent<CharacterWander>()?.Pause();

        // 배치한 바로 이 두 명을 대화시킨다 — 근접 판정 우연에 맡기지 않고 확정.
        // (카메라가 비추는 쌍과 멈춰 있는 쌍이 항상 일치하도록)
        if (ConversationManager.Instance == null)
            Debug.LogError("[MyRoomBootstrapper] ConversationManager.Instance == NULL → 초기 대화 시작 실패!");
        ConversationManager.Instance?.StartConversation(a, b);

        Debug.Log($"[MyRoomBootstrapper] 초기 만남 호출 완료: {a.characterName} ↔ {b.characterName} " +
                  $"(세비-호.InConversation={b.InConversation})");
    }
}
