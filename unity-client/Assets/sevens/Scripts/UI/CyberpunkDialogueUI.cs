using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// 사이버펑크 스타일 대화 UI (기존 DialogueUI 대체)
/// - 반투명 글래스 패널 + 네온 테두리
/// - 이름표: 네온 액센트 라인
/// - 스캔라인 오버레이
/// - 타이핑 효과 (커서 깜빡임 포함)
/// - 코너 장식
/// - "탭하여 계속" 안내 펄스
///
/// 사용법: 기존 DialogueUI 대신 사용.
///         Canvas에 빈 오브젝트 만들고 이 컴포넌트 추가하면 자동 빌드
/// </summary>
public class CyberpunkDialogueUI : MonoBehaviour
{
    public static CyberpunkDialogueUI Instance { get; private set; }

    [Header("Typing")]
    [SerializeField] private float typingSpeed = 0.03f;
    [SerializeField] private string cursorChar = "_";

    [Header("Layout")]
    [SerializeField] private float panelHeight = 160f;
    [SerializeField] private float panelMarginX = 24f;
    [SerializeField] private float panelMarginBottom = 24f;

    // 생성된 UI
    private GameObject dialogueRoot;
    private CanvasGroup rootCanvasGroup;
    private Image panelBg;
    private Image scanlineOverlay;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI dialogueText;
    private GameObject continuePrompt;
    private TextMeshProUGUI continueText;
    private Image nameAccentLine;

    // 상태
    private Coroutine typingCoroutine;
    private bool isTyping;
    private string fullText;
    private bool isVisible;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        BuildUI();
        HideDialogue();
    }

    // ──────────────────────────────────────────
    // UI 프로시저럴 생성
    // ──────────────────────────────────────────

    private void BuildUI()
    {
        // Root
        dialogueRoot = CyberpunkUITheme.CreateUIObject("CyberpunkDialogue", transform);
        RectTransform rootRt = dialogueRoot.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0, 0);
        rootRt.anchorMax = new Vector2(1, 0);
        rootRt.pivot = new Vector2(0.5f, 0);
        rootRt.anchoredPosition = new Vector2(0, panelMarginBottom);
        rootRt.offsetMin = new Vector2(panelMarginX, panelMarginBottom);
        rootRt.offsetMax = new Vector2(-panelMarginX, panelMarginBottom + panelHeight);

        rootCanvasGroup = dialogueRoot.AddComponent<CanvasGroup>();

        // === 메인 패널 배경 ===
        panelBg = CyberpunkUITheme.ApplyPanelStyle(dialogueRoot,
            new Color(0.02f, 0.02f, 0.06f, 0.88f),
            new Color(CyberpunkUITheme.NeonBlue.r, CyberpunkUITheme.NeonBlue.g, CyberpunkUITheme.NeonBlue.b, 0.35f));

        // === 스캔라인 오버레이 ===
        GameObject scanObj = CyberpunkUITheme.CreateUIObject("Scanlines", dialogueRoot.transform);
        RectTransform scanRt = scanObj.GetComponent<RectTransform>();
        scanRt.anchorMin = Vector2.zero;
        scanRt.anchorMax = Vector2.one;
        scanRt.offsetMin = Vector2.zero;
        scanRt.offsetMax = Vector2.zero;
        scanlineOverlay = scanObj.AddComponent<Image>();
        scanlineOverlay.sprite = CyberpunkUITheme.CreateScanlineSprite(4, 128, 0.06f, 3);
        scanlineOverlay.type = Image.Type.Tiled;
        scanlineOverlay.color = Color.white;
        scanlineOverlay.raycastTarget = false;

        // === 코너 장식 ===
        CyberpunkUITheme.AddCornerDecorations(dialogueRoot.transform,
            new Color(CyberpunkUITheme.NeonCyan.r, CyberpunkUITheme.NeonCyan.g, CyberpunkUITheme.NeonCyan.b, 0.5f),
            16f, 1.5f);

        // === 이름표 영역 ===
        GameObject nameArea = CyberpunkUITheme.CreateUIObject("NameArea", dialogueRoot.transform);
        RectTransform nameAreaRt = nameArea.GetComponent<RectTransform>();
        nameAreaRt.anchorMin = new Vector2(0, 1);
        nameAreaRt.anchorMax = new Vector2(0, 1);
        nameAreaRt.pivot = new Vector2(0, 1);
        nameAreaRt.anchoredPosition = new Vector2(16, -8);
        nameAreaRt.sizeDelta = new Vector2(180, 32);

        // 이름 배경
        Image nameBg = nameArea.AddComponent<Image>();
        nameBg.sprite = CyberpunkUITheme.CreatePanelSprite(32,
            new Color(0.04f, 0.04f, 0.12f, 0.95f),
            CyberpunkUITheme.NeonCyan, 2, 4);
        nameBg.type = Image.Type.Sliced;
        nameBg.raycastTarget = false;

        // 이름 텍스트
        GameObject nameObj = CyberpunkUITheme.CreateUIObject("NameText", nameArea.transform);
        RectTransform nameRt = nameObj.GetComponent<RectTransform>();
        nameRt.anchorMin = Vector2.zero;
        nameRt.anchorMax = Vector2.one;
        nameRt.offsetMin = new Vector2(10, 0);
        nameRt.offsetMax = new Vector2(-10, 0);
        nameText = nameObj.AddComponent<TextMeshProUGUI>();
        nameText.text = "";
        nameText.fontSize = 14;
        nameText.color = CyberpunkUITheme.NeonCyan;
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
        nameText.fontStyle = FontStyles.Bold;
        nameText.raycastTarget = false;

        // 이름 아래 액센트 라인
        GameObject accentObj = CyberpunkUITheme.CreateUIObject("AccentLine", dialogueRoot.transform);
        RectTransform accentRt = accentObj.GetComponent<RectTransform>();
        accentRt.anchorMin = new Vector2(0, 1);
        accentRt.anchorMax = new Vector2(0.4f, 1);
        accentRt.pivot = new Vector2(0, 1);
        accentRt.anchoredPosition = new Vector2(16, -42);
        accentRt.sizeDelta = new Vector2(0, 1f);
        nameAccentLine = accentObj.AddComponent<Image>();
        nameAccentLine.color = new Color(CyberpunkUITheme.NeonCyan.r, CyberpunkUITheme.NeonCyan.g, CyberpunkUITheme.NeonCyan.b, 0.25f);
        nameAccentLine.raycastTarget = false;

        // === 대사 텍스트 ===
        GameObject textObj = CyberpunkUITheme.CreateUIObject("DialogueText", dialogueRoot.transform);
        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0, 0);
        textRt.anchorMax = new Vector2(1, 1);
        textRt.offsetMin = new Vector2(20, 20);
        textRt.offsetMax = new Vector2(-20, -48);
        dialogueText = textObj.AddComponent<TextMeshProUGUI>();
        dialogueText.text = "";
        dialogueText.fontSize = 15;
        dialogueText.color = CyberpunkUITheme.TextPrimary;
        dialogueText.alignment = TextAlignmentOptions.TopLeft;
        dialogueText.lineSpacing = 8f;
        dialogueText.raycastTarget = false;

        // === "탭하여 계속" 프롬프트 ===
        continuePrompt = CyberpunkUITheme.CreateUIObject("ContinuePrompt", dialogueRoot.transform);
        RectTransform contRt = continuePrompt.GetComponent<RectTransform>();
        contRt.anchorMin = new Vector2(1, 0);
        contRt.anchorMax = new Vector2(1, 0);
        contRt.pivot = new Vector2(1, 0);
        contRt.anchoredPosition = new Vector2(-12, 6);
        contRt.sizeDelta = new Vector2(120, 20);
        continueText = continuePrompt.AddComponent<TextMeshProUGUI>();
        continueText.text = "TAP >>";
        continueText.fontSize = 11;
        continueText.color = CyberpunkUITheme.NeonBlue;
        continueText.alignment = TextAlignmentOptions.BottomRight;
        continueText.fontStyle = FontStyles.Bold;
        continueText.raycastTarget = false;
        continuePrompt.SetActive(false);
    }

    // ──────────────────────────────────────────
    // Public API (기존 DialogueUI와 동일)
    // ──────────────────────────────────────────

    public void ShowDialogue(string speakerName, string text)
    {
        isVisible = true;
        dialogueRoot.SetActive(true);
        rootCanvasGroup.alpha = 1f;

        nameText.text = speakerName;
        fullText = text;

        // 이름표 크기 자동 조절
        float nameWidth = Mathf.Max(80f, speakerName.Length * 16f + 24f);
        Transform nameArea = dialogueRoot.transform.Find("NameArea");
        if (nameArea != null)
        {
            RectTransform nameAreaRt = nameArea as RectTransform;
            if (nameAreaRt != null) nameAreaRt.sizeDelta = new Vector2(nameWidth, 32);
        }

        continuePrompt.SetActive(false);

        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(text));
    }

    public void HideDialogue()
    {
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        isVisible = false;
        dialogueRoot.SetActive(false);
    }

    public bool TrySkipTyping()
    {
        if (isTyping)
        {
            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
            dialogueText.text = fullText;
            isTyping = false;
            continuePrompt.SetActive(true);
            return true;
        }
        return false;
    }

    // ──────────────────────────────────────────
    // 타이핑 효과
    // ──────────────────────────────────────────

    private IEnumerator TypeText(string text)
    {
        isTyping = true;
        dialogueText.text = "";

        for (int i = 0; i < text.Length; i++)
        {
            // 커서 깜빡임 효과: 현재 텍스트 + 커서
            dialogueText.text = text.Substring(0, i + 1) +
                $"<color=#{ColorUtility.ToHtmlStringRGB(CyberpunkUITheme.NeonBlue)}>{cursorChar}</color>";
            yield return new WaitForSeconds(typingSpeed);
        }

        dialogueText.text = text;
        isTyping = false;
        continuePrompt.SetActive(true);
    }

    // ──────────────────────────────────────────
    // 애니메이션 업데이트
    // ──────────────────────────────────────────

    private void Update()
    {
        if (!isVisible) return;

        // "TAP ▶" 펄스
        if (continuePrompt.activeSelf && continueText != null)
        {
            float pulse = Mathf.Sin(Time.time * 4f) * 0.5f + 0.5f;
            Color c = CyberpunkUITheme.NeonBlue;
            c.a = 0.5f + pulse * 0.5f;
            continueText.color = c;
        }
    }
}
