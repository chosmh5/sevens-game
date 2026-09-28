using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 플레이어가 NPC에 접근하면 [E] 힌트를 표시하고,
/// E 키 입력 시 대화 패널을 열어 서버와 주고받습니다.
/// </summary>
public class PlayerInteraction : MonoBehaviour
{
    public static PlayerInteraction Instance { get; private set; }

    [SerializeField] float   interactionRadius = 2.5f;
    [SerializeField] KeyCode interactKey       = KeyCode.E;
    [SerializeField] KeyCode closeKey          = KeyCode.Escape;

    public bool IsInteracting { get; private set; }

    // ── UI ─────────────────────────────────────────────────────────────
    GameObject     _hintRoot;
    GameObject     _dialogueRoot;
    TMP_Text       _npcNameLabel;
    TMP_Text       _npcLineLabel;
    TMP_InputField _inputField;
    Button         _sendButton;

    // 모바일 키보드가 박스를 가리지 않게 끌어올리기 위한 참조
    RectTransform  _dialoguePanelRT;
    Canvas         _dialogueCanvas;
    float          _dialoguePanelBaseY;

    // ── 상태 ───────────────────────────────────────────────────────────
    CharacterWander   _npc;
    CharacterDialogue _npcDlg;
    CharacterDialogue _otherDlg;     // 끼어든 대화의 상대 캐릭터(있을 때)
    bool              _busy;
    Coroutine         _thinkingCo;   // 응답 대기 표시("머리 쓰는 중...") 애니메이션

    // ── 대화 박스 상태 ─────────────────────────────────────────────────
    bool        _ownsManagerConversation;  // 매니저 대화를 인계받아 진행 중인가(종료 시 정리)
    bool        _pendingTopicPrompt;       // 이번 플레이어 답을 '주제'로 저장할지
    static bool _topicAsked;               // 세션 첫 1:1 대화에서 주제를 한 번 물었는가
    GameObject _inputGO;                   // 입력창 루트
    TMP_Text   _sendLabel;                 // 버튼 라벨("전송")
    TMP_Text   _boxHint;                   // 박스 우상단 힌트("[Esc] 닫기")

    void Awake() => Instance = this;

    void Start()
    {
        BuildHintUI();
        BuildDialogueUI();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Update
    // ══════════════════════════════════════════════════════════════════

    void Update()
    {
        if (IsInteracting)
        {
            AdjustForKeyboard();   // 모바일 키보드가 올라오면 박스를 위로
            if (Input.GetKeyDown(closeKey)) { EndInteraction(); return; }
            return;
        }

        // 시네마틱 관전 중에는 ConversationWatchMode 가 E/Esc 를 처리하므로
        // 여기서는 힌트도 숨기고 입력도 건드리지 않는다 (중복 처리 방지).
        if (ConversationWatchMode.IsWatching)
        {
            _hintRoot.SetActive(false);
            return;
        }

        var nearest = FindNearestNPC();
        if (nearest != null)
        {
            _hintRoot.SetActive(true);
            // 대화 중인 쌍 → 지켜보기(시네마틱), 아니면 1:1 대화. 키보드 E 또는 힌트 탭으로 실행.
            UpdateHintText(IsNPCInAIConversation(nearest) ? "지켜보기  (E·탭)" : "대화하기  (E·탭)");
            if (Input.GetKeyDown(interactKey))
                TriggerNearest();
        }
        else
        {
            _hintRoot.SetActive(false);
        }
    }

    /// <summary>가장 가까운 NPC와 상호작용. (키보드 E / 모바일 힌트 탭 공용)</summary>
    public void TriggerNearest()
    {
        if (IsInteracting || ConversationWatchMode.IsWatching) return;
        var nearest = FindNearestNPC();
        if (nearest == null) return;

        LogNearbyNPCs();
        if (IsNPCInAIConversation(nearest))
        {
            Debug.Log($"[PlayerInteraction] 지켜보기 진입 (nearest={nearest.characterName})");
            BeginWatch(nearest);
        }
        else
        {
            Debug.Log($"[PlayerInteraction] 1:1 대화 진입 (nearest={nearest.characterName})");
            BeginInteraction(nearest);
        }
    }

    /// <summary>모바일 소프트 키보드가 떠 있으면 대화 박스를 키보드 위로 끌어올린다.</summary>
    void AdjustForKeyboard()
    {
        if (_dialoguePanelRT == null) return;

        float kbCanvas = 0f;
        if (TouchScreenKeyboard.visible)
        {
            float sf = (_dialogueCanvas != null && _dialogueCanvas.scaleFactor > 0f)
                ? _dialogueCanvas.scaleFactor : 1f;
            kbCanvas = TouchScreenKeyboard.area.height / sf;   // 픽셀 → 캔버스 단위
        }

        float targetY = _dialoguePanelBaseY + kbCanvas;
        var pos = _dialoguePanelRT.anchoredPosition;
        pos.y = Mathf.Lerp(pos.y, targetY, 14f * Time.deltaTime);   // 부드럽게
        _dialoguePanelRT.anchoredPosition = pos;
    }

    // ══════════════════════════════════════════════════════════════════
    //  NPC 탐색
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 가장 가까운 NPC를 찾습니다.
    /// AI끼리 대화 중인 캐릭터도 포함 — 끼어들기 가능.
    /// </summary>
    CharacterDialogue FindNearestNPC()
    {
        float best = interactionRadius * interactionRadius;
        CharacterDialogue result = null;

        foreach (var cd in FindObjectsByType<CharacterDialogue>(FindObjectsSortMode.None))
        {
            // ★ 플레이어 아바타에 붙은 CharacterDialogue(모델을 세비-호로 공유) 제외 —
            //   이게 없으면 거리 0의 '자기 자신'이 항상 최근접으로 잡혀 늘 세비-호와 1:1이 된다.
            if (cd.GetComponent<VillagePlayerController>() != null) continue;
            float sq = (cd.transform.position - transform.position).sqrMagnitude;
            if (sq < best) { best = sq; result = cd; }
        }
        return result;
    }

    /// <summary>대상 NPC가 현재 AI끼리 대화 중인지 확인</summary>
    bool IsNPCInAIConversation(CharacterDialogue npc)
    {
        return npc.InConversation && !IsInteracting;
    }

    /// <summary>E를 누른 순간 주변 NPC 전부의 거리/대화상태를 콘솔에 덤프(진단용).</summary>
    void LogNearbyNPCs()
    {
        var sb = new System.Text.StringBuilder("[PlayerInteraction] NPC 거리 덤프 → ");
        foreach (var cd in FindObjectsByType<CharacterDialogue>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(cd.transform.position, transform.position);
            sb.Append($"{cd.characterName}(거리 {d:F1}, InConv={cd.InConversation}) ");
        }
        Debug.Log(sb.ToString());
    }

    // ══════════════════════════════════════════════════════════════════
    //  대화 시작 / 종료
    // ══════════════════════════════════════════════════════════════════

    /// <summary>AI 대화 중인 쌍 → 시네마틱 관전 모드로 진입</summary>
    void BeginWatch(CharacterDialogue npc)
    {
        // 매니저가 보관 중인 대화 쌍을 그대로 쓴다(자막으로 재생할 묶음도 매니저가 갖고 있음).
        var mgr = ConversationManager.Instance;
        CharacterDialogue a = mgr != null ? mgr.CurrentA : null;
        CharacterDialogue b = mgr != null ? mgr.CurrentB : null;

        // 매니저 상태가 비어 있으면(드문 경우) 다가간 캐릭터 + 가장 가까운 대화 상대로 폴백.
        if (a == null || b == null)
        {
            a = npc;
            b = FindConversingPartner(npc);
        }
        if (a == null || b == null)
        {
            Debug.LogWarning("[PlayerInteraction] BeginWatch: 관전할 쌍을 못 찾음 → 중단");
            return;
        }

        Debug.Log($"[PlayerInteraction] BeginWatch: {a.characterName} ↔ {b.characterName}, " +
                  $"WatchMode.Instance={(ConversationWatchMode.Instance != null ? "OK" : "NULL")}");

        _hintRoot.SetActive(false);
        ConversationWatchMode.Instance?.TriggerWatch(a, b, transform);
    }

    /// <summary>npc 외에 InConversation 중인 가장 가까운 캐릭터(폴백용).</summary>
    CharacterDialogue FindConversingPartner(CharacterDialogue npc)
    {
        CharacterDialogue best = null;
        float bestSq = float.MaxValue;
        foreach (var cd in FindObjectsByType<CharacterDialogue>(FindObjectsSortMode.None))
        {
            if (cd == npc || !cd.InConversation) continue;
            if (cd.GetComponent<VillagePlayerController>() != null) continue;
            float sq = (cd.transform.position - npc.transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = cd; }
        }
        return best;
    }

    void BeginInteraction(CharacterDialogue npc)
    {
        IsInteracting  = true;
        _npcDlg        = npc;
        _npc           = npc.GetComponent<CharacterWander>();
        npc.InConversation = true;
        _npc?.Pause();

        // 서로 마주보기
        Vector3 dir = transform.position - npc.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
        {
            npc.transform.forward = dir.normalized;
            transform.forward     = -dir.normalized;
        }

        _npcNameLabel.text = npc.characterName;
        _inputField.text   = "";
        _hintRoot.SetActive(false);
        _dialogueRoot.SetActive(true);

        // 세션 첫 1:1 대화에서는 무조건 주제부터 묻는다(LLM 대기 없이 즉시) → 답을 주제로 저장.
        if (!_topicAsked)
        {
            _topicAsked = true;
            _pendingTopicPrompt = true;
            _npcLineLabel.text = "요즘 무슨 얘기 하고 싶어요? 관심 있는 거나, 다 같이 떠들 만한 주제 있으면 던져봐요.";
            SetInput(true);   // 즉시 입력 가능
        }
        else
        {
            _npcLineLabel.text = "...";
            SetInput(false);
            StartCoroutine(FetchGreeting(false));
        }
    }

    void EndInteraction()
    {
        StopAllCoroutines();
        IsInteracting = false;
        _busy         = false;
        _pendingTopicPrompt = false;

        if (_npcDlg != null) _npcDlg.InConversation = false;
        if (_otherDlg != null)
        {
            _otherDlg.InConversation = false;
            _otherDlg.GetComponent<CharacterWander>()?.Resume();
            _otherDlg = null;
        }
        _npc?.Resume();
        _npc    = null;
        _npcDlg = null;

        // 매니저로부터 인계받은 대화였다면, 매니저 상태를 정리하고 쿨다운을 건다.
        if (_ownsManagerConversation)
        {
            ConversationManager.Instance?.EndConversation();
            _ownsManagerConversation = false;
        }

        // 다음 대화를 위해 입력창/버튼 라벨/힌트 원복
        if (_inputGO  != null) _inputGO.SetActive(true);
        if (_sendLabel != null) _sendLabel.text = "전송";
        if (_boxHint  != null) _boxHint.text = "[ Esc ] 닫기";

        // 박스 위치 원위치(키보드로 올라가 있던 것 복원)
        if (_dialoguePanelRT != null)
            _dialoguePanelRT.anchoredPosition = new Vector2(
                _dialoguePanelRT.anchoredPosition.x, _dialoguePanelBaseY);

        _dialogueRoot.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  끼어들기 — 셋이 함께 대화 (플레이어 + 두 NPC)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 시네마틱 관전에서 [E]로 끼어들 때 호출. 두 NPC를 그 자리에 고정한 채
    /// 플레이어를 향하게 하고, 입력창을 열어 셋이 함께 대화를 잇는다.
    /// a = 플레이어가 마주하는(=발화 응답 주체) 캐릭터, b = 곁의 다른 캐릭터.
    /// </summary>
    public void BeginGroupConversation(CharacterDialogue a, CharacterDialogue b)
    {
        if (a == null) return;
        Debug.Log($"[PlayerInteraction] 끼어들기 → 셋이 함께: a={a.characterName}, b={(b != null ? b.characterName : "없음")}");
        _ownsManagerConversation = true;   // 종료 시 매니저 상태 정리 필요

        IsInteracting = true;
        _npcDlg = a;
        _npc    = a.GetComponent<CharacterWander>();
        a.InConversation = true;
        _npc?.Pause();

        _otherDlg = b;
        if (b != null)
        {
            b.InConversation = true;
            b.GetComponent<CharacterWander>()?.Pause();   // ★ 상대도 떠나지 않게 고정
            FacePlayer(b);
        }
        FacePlayer(a);

        // 서버에 두 캐릭터를 활성으로 — 플레이어 발화의 응답 주체가 이 둘이 되도록.
        if (ServerClient.Instance != null && b != null)
            StartCoroutine(ServerClient.Instance.SetActiveCharacters(
                new[] { a.characterName, b.characterName }));

        // 관전하던 대화 맥락을 '즉시' 보여준다 — 추가 LLM 호출(=빈 박스/로딩 모호함) 없이.
        var mgr      = ConversationManager.Instance;
        string question = mgr != null ? mgr.CurrentPlayerQuestion : null;
        var    lines    = mgr != null ? mgr.CurrentLines          : null;

        if (!string.IsNullOrEmpty(question))
        {
            // 캐릭터가 플레이어에게 던진 질문이 있으면 그걸 띄우고, 답을 '주제'로 저장한다.
            _npcNameLabel.text = (lines != null && lines.Length > 0) ? lines[lines.Length - 1].speaker : a.characterName;
            _npcLineLabel.text = question;
            _pendingTopicPrompt = true;
        }
        else if (lines != null && lines.Length > 0)
        {
            var last = lines[lines.Length - 1];
            _npcNameLabel.text = last.speaker;
            _npcLineLabel.text = last.line + "\n\n<color=#888888>(끼어들어 말을 건네보세요)</color>";
        }
        else
        {
            _npcNameLabel.text = a.characterName;
            _npcLineLabel.text = "끼어들었다 — 무슨 얘기든 건네보세요.";
        }

        _inputField.text   = "";
        if (_inputGO  != null) _inputGO.SetActive(true);
        if (_sendLabel != null) _sendLabel.text = "전송";
        if (_boxHint  != null) _boxHint.text = "[ Esc ] 닫기";
        _hintRoot.SetActive(false);
        _dialogueRoot.SetActive(true);

        SetInput(true);   // 즉시 입력 가능 (로딩 대기 없음)
    }

    /// <summary>캐릭터가 플레이어를 바라보게 한다.</summary>
    void FacePlayer(CharacterDialogue c)
    {
        Vector3 d = transform.position - c.transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.01f)
            c.transform.forward = d.normalized;
    }

    // ══════════════════════════════════════════════════════════════════
    //  서버 통신
    // ══════════════════════════════════════════════════════════════════

    IEnumerator FetchGreeting(bool wasInConversation = false)
    {
        _busy = true;
        StartThinking();

        // 끼어들기면 대화 끊기는 텀
        if (wasInConversation)
            yield return new WaitForSeconds(0.6f);

        string result = null;
        bool   ok     = false;
        yield return ServerClient.Instance.GetApproachGreeting(
            _npcDlg.characterName,
            line => { result = line; ok = true; },
            ()   => { ok = false; }
        );

        StopThinking();
        if (ok && !string.IsNullOrEmpty(result))
        {
            _npcLineLabel.text = result;
            DialogueLog.Instance?.AddEntry(_npcDlg.characterName, result);
        }
        else
        {
            _npcLineLabel.text = "(대화를 시작할 수 없습니다)";
        }

        _busy = false;
        if (_inputGO != null) _inputGO.SetActive(true);
        if (_sendLabel != null) _sendLabel.text = "전송";
        SetInput(true);
    }

    /// <summary>하단 전송 버튼 핸들러.</summary>
    void OnPrimaryButton()
    {
        OnSendClicked();
    }

    void OnSendClicked()
    {
        string msg = _inputField.text.Trim();
        if (string.IsNullOrEmpty(msg) || _busy) return;

        _inputField.text   = "";
        _npcLineLabel.text = "...";
        SetInput(false);

        // 캐릭터가 주제를 물어본 직후의 답이면 '대화 주제'로 서버에 저장한다.
        // → 이후 다른 캐릭터들의 자동 대화에서 이 주제가 화제로 쓰인다.
        if (_pendingTopicPrompt)
        {
            _pendingTopicPrompt = false;
            if (ServerClient.Instance != null)
                StartCoroutine(ServerClient.Instance.AddTopic(msg));
        }

        // 셋이 함께 대화(상대가 있을 때)는 두 NPC가 차례로 답하고, 1:1은 한 명만 답한다.
        if (_otherDlg != null) StartCoroutine(SendGroupMessage(msg));
        else                   StartCoroutine(SendMessage(msg));
    }

    IEnumerator SendGroupMessage(string msg)
    {
        _busy = true;
        StartThinking();

        ExchangeLine[] replies = null;
        yield return ServerClient.Instance.SendGroupMessage(
            msg,
            new[] { _npcDlg.characterName, _otherDlg.characterName },
            r  => { replies = r; },
            () => { replies = null; });

        StopThinking();

        if (replies != null && replies.Length > 0)
        {
            // 두 NPC 대사를 순차로 보여준다(앞 사람 → 잠깐 → 뒷 사람).
            for (int i = 0; i < replies.Length; i++)
            {
                _npcNameLabel.text = replies[i].speaker;
                _npcLineLabel.text = replies[i].line;
                DialogueLog.Instance?.AddEntry(replies[i].speaker, replies[i].line);
                if (i < replies.Length - 1)
                    yield return new WaitForSeconds(2.2f);
            }
        }
        else
        {
            _npcLineLabel.text = "(응답을 받지 못했습니다)";
        }

        _busy = false;
        SetInput(true);
    }

    IEnumerator SendMessage(string msg)
    {
        _busy = true;
        StartThinking();

        string reply = null;
        bool   ok    = false;
        yield return ServerClient.Instance.SendUserMessage(
            msg,
            _npcDlg.characterName,                 // ★ 지금 말 거는 캐릭터를 명시
            r  => { reply = r; ok = true; },
            () => { ok = false; }
        );

        StopThinking();
        if (ok && !string.IsNullOrEmpty(reply))
        {
            _npcLineLabel.text = reply;
            DialogueLog.Instance?.AddEntry(_npcDlg.characterName, reply);
        }
        else
        {
            _npcLineLabel.text = "(응답을 받지 못했습니다)";
        }

        _busy = false;
        SetInput(true);
    }

    // ── 응답 대기 표시 ──────────────────────────────────────────────────
    void StartThinking()
    {
        StopThinking();
        _thinkingCo = StartCoroutine(ThinkingAnim());
    }

    void StopThinking()
    {
        if (_thinkingCo != null) { StopCoroutine(_thinkingCo); _thinkingCo = null; }
    }

    IEnumerator ThinkingAnim()
    {
        int i = 0;
        while (true)
        {
            _npcLineLabel.text = "머리 쓰는 중" + new string('.', i % 4);
            i++;
            yield return new WaitForSeconds(0.4f);
        }
    }

    void SetInput(bool active)
    {
        _inputField.interactable = active;
        _sendButton.interactable = active;

        // 활성화되면 입력창에 자동 포커스 — 클릭 없이 바로 타이핑할 수 있게.
        if (active)
        {
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(_inputField.gameObject);
            _inputField.ActivateInputField();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  UI 생성
    // ══════════════════════════════════════════════════════════════════

    void BuildHintUI()
    {
        _hintRoot = NewCanvas("InteractionHintCanvas", 10);

        // 배경 (탭 가능한 버튼 — 모바일에서 클릭으로 상호작용)
        var bg    = NewGO("HintBg", _hintRoot.transform);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.65f);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin        = new Vector2(0.5f, 0.12f);
        bgRT.anchorMax        = new Vector2(0.5f, 0.12f);
        bgRT.sizeDelta        = new Vector2(260, 46);
        bgRT.anchoredPosition = Vector2.zero;
        var hintBtn = bg.AddComponent<Button>();
        hintBtn.targetGraphic = bgImg;
        hintBtn.onClick.AddListener(TriggerNearest);

        // 텍스트 (동적으로 변경 가능하도록 참조 보관)
        var lbl = NewTMP(bg.transform, "대화하기  (E·탭)", 20, Color.white);
        StretchRT(lbl.GetComponent<RectTransform>(), 8, 4);
        _hintText = lbl;

        _hintRoot.SetActive(false);
    }

    void BuildDialogueUI()
    {
        // sortingOrder 120: 사이버펑크 HUD 캔버스(100)보다 위에 둬서
        // 입력창/전송 버튼 클릭이 다른 캔버스에 가로채이지 않게 한다.
        _dialogueRoot = NewCanvas("DialogueCanvas", 120);

        // ── 패널 ───────────────────────────────────────────────────────
        var panel  = NewGO("DialoguePanel", _dialogueRoot.transform);
        panel.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.12f, 0.93f);
        var panelRT         = panel.GetComponent<RectTransform>();
        panelRT.anchorMin   = new Vector2(0.5f, 0f);
        panelRT.anchorMax   = new Vector2(0.5f, 0f);
        panelRT.pivot       = new Vector2(0.5f, 0f);
        panelRT.sizeDelta   = new Vector2(700, 210);
        panelRT.anchoredPosition = new Vector2(0f, 20f);

        // 모바일 키보드 회피용 참조 보관
        _dialoguePanelRT    = panelRT;
        _dialogueCanvas     = _dialogueRoot.GetComponent<Canvas>();
        _dialoguePanelBaseY = panelRT.anchoredPosition.y;

        // ── NPC 이름 ────────────────────────────────────────────────────
        _npcNameLabel = NewTMP(panel.transform, "캐릭터", 15,
                               new Color(0.95f, 0.85f, 0.45f), FontStyles.Bold);
        var nameRT          = _npcNameLabel.GetComponent<RectTransform>();
        nameRT.anchorMin    = new Vector2(0f, 1f);
        nameRT.anchorMax    = new Vector2(1f, 1f);
        nameRT.pivot        = new Vector2(0f, 1f);
        nameRT.offsetMin    = new Vector2(14f, -36f);
        nameRT.offsetMax    = new Vector2(-14f, -4f);
        _npcNameLabel.alignment = TextAlignmentOptions.Left;

        // ── Esc 힌트 (✕ 버튼 왼쪽에 둠) ─────────────────────────────────
        var escLbl = NewTMP(panel.transform, "[ Esc ] 닫기", 11,
                            new Color(0.55f, 0.55f, 0.55f));
        _boxHint = escLbl;
        var escRT        = escLbl.GetComponent<RectTransform>();
        escRT.anchorMin  = new Vector2(1f, 1f);
        escRT.anchorMax  = new Vector2(1f, 1f);
        escRT.pivot      = new Vector2(1f, 1f);
        escRT.sizeDelta  = new Vector2(240f, 24f);
        escRT.anchoredPosition = new Vector2(-50f, -10f);
        escLbl.alignment = TextAlignmentOptions.Right;

        // ── 닫기(✕) 버튼 — 모바일에서 탭으로 닫기 ────────────────────────
        var closeGO = NewGO("CloseButton", panel.transform);
        closeGO.AddComponent<Image>().color = new Color(0.5f, 0.2f, 0.25f, 1f);
        var closeRT = closeGO.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1f, 1f);
        closeRT.anchorMax = new Vector2(1f, 1f);
        closeRT.pivot     = new Vector2(1f, 1f);
        closeRT.sizeDelta = new Vector2(34f, 34f);
        closeRT.anchoredPosition = new Vector2(-8f, -8f);
        var closeBtn = closeGO.AddComponent<Button>();
        closeBtn.onClick.AddListener(EndInteraction);
        var closeLbl = NewTMP(closeGO.transform, "✕", 18, Color.white, FontStyles.Bold);
        StretchRT(closeLbl.GetComponent<RectTransform>(), 0, 0);

        // ── NPC 대사 ────────────────────────────────────────────────────
        _npcLineLabel = NewTMP(panel.transform, "...", 14, Color.white);
        _npcLineLabel.enableWordWrapping = true;
        _npcLineLabel.alignment          = TextAlignmentOptions.TopLeft;
        var lineRT       = _npcLineLabel.GetComponent<RectTransform>();
        lineRT.anchorMin = new Vector2(0f, 0f);
        lineRT.anchorMax = new Vector2(1f, 1f);
        lineRT.offsetMin = new Vector2(14f, 56f);
        lineRT.offsetMax = new Vector2(-14f, -40f);

        // ── 입력창 ───────────────────────────────────────────────────────
        var inputGO  = NewGO("InputField", panel.transform);
        inputGO.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.22f, 1f);
        var inputRT        = inputGO.GetComponent<RectTransform>();
        inputRT.anchorMin  = new Vector2(0f, 0f);
        inputRT.anchorMax  = new Vector2(1f, 0f);
        inputRT.pivot      = new Vector2(0.5f, 0f);
        inputRT.sizeDelta  = new Vector2(-110f, 42f);
        inputRT.anchoredPosition = new Vector2(-45f, 10f);
        _inputField = BuildInputField(inputGO);
        _inputGO    = inputGO;

        // ── 전송 버튼 ────────────────────────────────────────────────────
        var btnGO  = NewGO("SendButton", panel.transform);
        btnGO.AddComponent<Image>().color = new Color(0.2f, 0.45f, 0.85f, 1f);
        var btnRT        = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin  = new Vector2(1f, 0f);
        btnRT.anchorMax  = new Vector2(1f, 0f);
        btnRT.pivot      = new Vector2(1f, 0f);
        btnRT.sizeDelta  = new Vector2(92f, 42f);
        btnRT.anchoredPosition = new Vector2(-10f, 10f);
        _sendButton = btnGO.AddComponent<Button>();
        _sendButton.onClick.AddListener(OnPrimaryButton);
        _sendLabel = NewTMP(btnGO.transform, "전송", 15, Color.white, FontStyles.Bold);
        StretchRT(_sendLabel.GetComponent<RectTransform>(), 0, 0);

        _dialogueRoot.SetActive(false);
    }

    TMP_Text _hintText;

    void UpdateHintText(string msg)
    {
        if (_hintText != null) _hintText.text = msg;
    }

    // ══════════════════════════════════════════════════════════════════
    //  UI 헬퍼
    // ══════════════════════════════════════════════════════════════════

    static GameObject NewCanvas(string name, int order)
    {
        var go = new GameObject(name);
        var c  = go.AddComponent<Canvas>();
        c.renderMode   = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = order;
        var cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        go.AddComponent<GraphicRaycaster>();
        return go;
    }

    static GameObject NewGO(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    static TMP_Text NewTMP(Transform parent, string text, float size, Color color,
                           FontStyles style = FontStyles.Normal)
    {
        var go  = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    static void StretchRT(RectTransform rt, float hPad, float vPad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(hPad, vPad);
        rt.offsetMax = new Vector2(-hPad, -vPad);
    }

    static TMP_InputField BuildInputField(GameObject go)
    {
        var field = go.AddComponent<TMP_InputField>();

        // Viewport
        var vpGO = new GameObject("TextViewport");
        vpGO.transform.SetParent(go.transform, false);
        vpGO.AddComponent<RectMask2D>();
        var vpRT = vpGO.GetComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = new Vector2(8f, 4f);
        vpRT.offsetMax = new Vector2(-8f, -4f);

        // Text
        var textGO  = new GameObject("Text");
        textGO.transform.SetParent(vpGO.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.fontSize             = 14f;
        tmp.color                = Color.white;
        tmp.enableWordWrapping   = false;
        var textRT               = textGO.GetComponent<RectTransform>();
        textRT.anchorMin         = Vector2.zero;
        textRT.anchorMax         = Vector2.one;
        textRT.offsetMin         = Vector2.zero;
        textRT.offsetMax         = Vector2.zero;

        field.textViewport  = vpRT;
        field.textComponent = tmp;
        field.lineType      = TMP_InputField.LineType.SingleLine;
        return field;
    }
}
