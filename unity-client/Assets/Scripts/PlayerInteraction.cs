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

    // ── 상태 ───────────────────────────────────────────────────────────
    CharacterWander   _npc;
    CharacterDialogue _npcDlg;
    bool              _busy;

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
            if (Input.GetKeyDown(closeKey)) EndInteraction();
            return;
        }

        var nearest = FindNearestNPC();
        _hintRoot.SetActive(nearest != null);
        if (nearest != null && Input.GetKeyDown(interactKey))
            BeginInteraction(nearest);
    }

    // ══════════════════════════════════════════════════════════════════
    //  NPC 탐색
    // ══════════════════════════════════════════════════════════════════

    CharacterDialogue FindNearestNPC()
    {
        float best = interactionRadius * interactionRadius;
        CharacterDialogue result = null;

        foreach (var cd in FindObjectsByType<CharacterDialogue>(FindObjectsSortMode.None))
        {
            if (cd.InConversation) continue;
            float sq = (cd.transform.position - transform.position).sqrMagnitude;
            if (sq < best) { best = sq; result = cd; }
        }
        return result;
    }

    // ══════════════════════════════════════════════════════════════════
    //  대화 시작 / 종료
    // ══════════════════════════════════════════════════════════════════

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
        _npcLineLabel.text = "...";
        _inputField.text   = "";
        SetInput(false);
        _hintRoot.SetActive(false);
        _dialogueRoot.SetActive(true);

        StartCoroutine(FetchGreeting());
    }

    void EndInteraction()
    {
        StopAllCoroutines();
        IsInteracting = false;
        _busy         = false;

        if (_npcDlg != null) _npcDlg.InConversation = false;
        _npc?.Resume();
        _npc    = null;
        _npcDlg = null;

        _dialogueRoot.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  서버 통신
    // ══════════════════════════════════════════════════════════════════

    IEnumerator FetchGreeting()
    {
        _busy = true;
        yield return ServerClient.Instance.GetCharacterLine(
            _npcDlg.characterName,
            line => _npcLineLabel.text = line,
            ()   => _npcLineLabel.text = "(대화를 시작할 수 없습니다)"
        );
        _busy = false;
        SetInput(true);
    }

    void OnSendClicked()
    {
        string msg = _inputField.text.Trim();
        if (string.IsNullOrEmpty(msg) || _busy) return;

        _inputField.text   = "";
        _npcLineLabel.text = "...";
        SetInput(false);
        StartCoroutine(SendMessage(msg));
    }

    IEnumerator SendMessage(string msg)
    {
        _busy = true;
        yield return ServerClient.Instance.SendUserMessage(
            msg,
            reply => _npcLineLabel.text = reply,
            ()    => _npcLineLabel.text = "(응답을 받지 못했습니다)"
        );
        _busy = false;
        SetInput(true);
    }

    void SetInput(bool active)
    {
        _inputField.interactable = active;
        _sendButton.interactable = active;
    }

    // ══════════════════════════════════════════════════════════════════
    //  UI 생성
    // ══════════════════════════════════════════════════════════════════

    void BuildHintUI()
    {
        _hintRoot = NewCanvas("InteractionHintCanvas", 10);

        // 배경
        var bg    = NewGO("HintBg", _hintRoot.transform);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.65f);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin        = new Vector2(0.5f, 0.12f);
        bgRT.anchorMax        = new Vector2(0.5f, 0.12f);
        bgRT.sizeDelta        = new Vector2(260, 46);
        bgRT.anchoredPosition = Vector2.zero;

        // 텍스트
        var lbl = NewTMP(bg.transform, "[ E ]  대화하기", 20, Color.white);
        StretchRT(lbl.GetComponent<RectTransform>(), 8, 4);

        _hintRoot.SetActive(false);
    }

    void BuildDialogueUI()
    {
        _dialogueRoot = NewCanvas("DialogueCanvas", 11);

        // ── 패널 ───────────────────────────────────────────────────────
        var panel  = NewGO("DialoguePanel", _dialogueRoot.transform);
        panel.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.12f, 0.93f);
        var panelRT         = panel.GetComponent<RectTransform>();
        panelRT.anchorMin   = new Vector2(0.5f, 0f);
        panelRT.anchorMax   = new Vector2(0.5f, 0f);
        panelRT.pivot       = new Vector2(0.5f, 0f);
        panelRT.sizeDelta   = new Vector2(700, 210);
        panelRT.anchoredPosition = new Vector2(0f, 20f);

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

        // ── Esc 힌트 ────────────────────────────────────────────────────
        var escLbl = NewTMP(panel.transform, "[ Esc ] 닫기", 11,
                            new Color(0.55f, 0.55f, 0.55f));
        var escRT        = escLbl.GetComponent<RectTransform>();
        escRT.anchorMin  = new Vector2(1f, 1f);
        escRT.anchorMax  = new Vector2(1f, 1f);
        escRT.pivot      = new Vector2(1f, 1f);
        escRT.sizeDelta  = new Vector2(100f, 24f);
        escRT.anchoredPosition = new Vector2(-10f, -8f);

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
        _sendButton.onClick.AddListener(OnSendClicked);
        var btnLbl = NewTMP(btnGO.transform, "전송", 15, Color.white, FontStyles.Bold);
        StretchRT(btnLbl.GetComponent<RectTransform>(), 0, 0);

        _dialogueRoot.SetActive(false);
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
