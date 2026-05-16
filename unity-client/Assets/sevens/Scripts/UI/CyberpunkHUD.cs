using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 사이버펑크 인게임 HUD 자동 생성기
/// - Canvas 하위에 추가하면 모든 HUD 요소를 코드로 생성
/// - 상단 바: 씬 이름 + 뒤로가기 버튼
/// - 우하단: 인터랙션 버튼 (말걸기 등)
/// - 모든 UI가 CyberpunkUITheme 컬러/스타일과 통일
///
/// 사용법: Canvas에 이 컴포넌트를 추가하면 자동 빌드
/// </summary>
public class CyberpunkHUD : MonoBehaviour
{
    public static CyberpunkHUD Instance { get; private set; }

    [Header("HUD Settings")]
    [SerializeField] private string locationName = "마이룸";
    [SerializeField] private bool showBackButton = true;
    [SerializeField] private bool showInteractButton = true;

    [Header("Interact Button")]
    [SerializeField] private float interactBtnSize = 80f;
    [SerializeField] private Vector2 interactBtnPos = new Vector2(-100, 140); // 우하단 기준

    // 생성된 UI 참조
    private GameObject topBar;
    private TextMeshProUGUI locationText;
    private Button backButton;
    private GameObject interactRoot;
    private Button interactButton;
    private TextMeshProUGUI interactLabel;
    private Image interactGlow;
    private CanvasGroup interactCanvasGroup;

    // 인터랙션 콜백
    private System.Action onInteractAction;
    private bool interactVisible;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        BuildHUD();
    }

    // ──────────────────────────────────────────
    // HUD 빌드
    // ──────────────────────────────────────────

    private void BuildHUD()
    {
        BuildTopBar();
        if (showInteractButton) BuildInteractButton();
    }

    /// <summary>
    /// 부트스트래퍼에서 Start 전에 설정값을 주입할 때 사용
    /// </summary>
    public void Configure(string location, bool backBtn)
    {
        locationName = location;
        showBackButton = backBtn;
    }

    /// <summary>
    /// 상단 바: 위치 이름 + 뒤로가기
    /// </summary>
    private void BuildTopBar()
    {
        // 상단 바 컨테이너
        topBar = CyberpunkUITheme.CreateUIObject("TopBar", transform);
        RectTransform rt = topBar.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, 56);

        // 배경: 상단에서 아래로 페이드되는 그라데이션
        Image barBg = topBar.AddComponent<Image>();
        barBg.color = new Color(0.02f, 0.02f, 0.06f, 0.9f);
        barBg.raycastTarget = false;

        // 하단 네온 라인
        GameObject bottomLine = CyberpunkUITheme.CreateUIObject("BottomLine", topBar.transform);
        RectTransform lineRt = bottomLine.GetComponent<RectTransform>();
        lineRt.anchorMin = new Vector2(0, 0);
        lineRt.anchorMax = new Vector2(1, 0);
        lineRt.pivot = new Vector2(0.5f, 0);
        lineRt.anchoredPosition = Vector2.zero;
        lineRt.sizeDelta = new Vector2(0, 1.5f);
        Image lineImg = bottomLine.AddComponent<Image>();
        lineImg.color = new Color(CyberpunkUITheme.NeonBlue.r, CyberpunkUITheme.NeonBlue.g, CyberpunkUITheme.NeonBlue.b, 0.4f);
        lineImg.raycastTarget = false;

        // 하단 글로우 라인
        GameObject glowLine = CyberpunkUITheme.CreateUIObject("GlowLine", topBar.transform);
        RectTransform glowRt = glowLine.GetComponent<RectTransform>();
        glowRt.anchorMin = new Vector2(0, 0);
        glowRt.anchorMax = new Vector2(1, 0);
        glowRt.pivot = new Vector2(0.5f, 0.5f);
        glowRt.anchoredPosition = new Vector2(0, -1);
        glowRt.sizeDelta = new Vector2(0, 8);
        Image glowImg = glowLine.AddComponent<Image>();
        glowImg.color = new Color(CyberpunkUITheme.NeonBlue.r, CyberpunkUITheme.NeonBlue.g, CyberpunkUITheme.NeonBlue.b, 0.08f);
        glowImg.raycastTarget = false;

        // 뒤로가기 버튼
        if (showBackButton)
        {
            GameObject backObj = CyberpunkUITheme.CreateUIObject("BackButton", topBar.transform);
            RectTransform backRt = backObj.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0, 0.5f);
            backRt.anchorMax = new Vector2(0, 0.5f);
            backRt.pivot = new Vector2(0, 0.5f);
            backRt.anchoredPosition = new Vector2(12, 0);
            backRt.sizeDelta = new Vector2(44, 44);

            Image backBg = backObj.AddComponent<Image>();
            backBg.sprite = CyberpunkUITheme.CreateCircleSprite(64,
                new Color(0.05f, 0.05f, 0.12f, 0.7f),
                new Color(CyberpunkUITheme.NeonBlue.r, CyberpunkUITheme.NeonBlue.g, CyberpunkUITheme.NeonBlue.b, 0.3f), 2);

            backButton = backObj.AddComponent<Button>();
            backButton.targetGraphic = backBg;
            backButton.onClick.AddListener(() => GameSceneManager.Instance?.GoToLobby());

            // 화살표 텍스트 (< 기호로 대체)
            GameObject arrowObj = CyberpunkUITheme.CreateUIObject("Arrow", backObj.transform);
            RectTransform arrowRt = arrowObj.GetComponent<RectTransform>();
            arrowRt.anchorMin = Vector2.zero;
            arrowRt.anchorMax = Vector2.one;
            arrowRt.offsetMin = Vector2.zero;
            arrowRt.offsetMax = Vector2.zero;
            TextMeshProUGUI arrowText = arrowObj.AddComponent<TextMeshProUGUI>();
            arrowText.text = "<";
            arrowText.fontSize = 22;
            arrowText.color = CyberpunkUITheme.NeonBlue;
            arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.raycastTarget = false;
        }

        // 위치 이름 텍스트
        GameObject nameObj = CyberpunkUITheme.CreateUIObject("LocationName", topBar.transform);
        RectTransform nameRt = nameObj.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0, 0);
        nameRt.anchorMax = new Vector2(1, 1);
        nameRt.offsetMin = new Vector2(showBackButton ? 64 : 16, 0);
        nameRt.offsetMax = new Vector2(-16, 0);
        locationText = nameObj.AddComponent<TextMeshProUGUI>();
        locationText.text = locationName;
        locationText.fontSize = 16;
        locationText.color = CyberpunkUITheme.TextPrimary;
        locationText.alignment = TextAlignmentOptions.MidlineLeft;
        locationText.fontStyle = FontStyles.Bold;
        locationText.raycastTarget = false;

        // 위치 이름 옆 작은 장식 (사이버펑크 느낌)
        GameObject decoObj = CyberpunkUITheme.CreateUIObject("Deco", topBar.transform);
        RectTransform decoRt = decoObj.GetComponent<RectTransform>();
        decoRt.anchorMin = new Vector2(0, 0.5f);
        decoRt.anchorMax = new Vector2(0, 0.5f);
        decoRt.anchoredPosition = new Vector2(showBackButton ? 58 : 10, 0);
        decoRt.sizeDelta = new Vector2(2, 20);
        Image decoImg = decoObj.AddComponent<Image>();
        decoImg.color = CyberpunkUITheme.NeonCyan;
        decoImg.raycastTarget = false;
    }

    /// <summary>
    /// 인터랙션 버튼 (우하단, 기존 MobileInteractButton 대체)
    /// </summary>
    private void BuildInteractButton()
    {
        interactRoot = CyberpunkUITheme.CreateUIObject("InteractButton", transform);
        RectTransform rt = interactRoot.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0); // 우하단
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = interactBtnPos;
        rt.sizeDelta = new Vector2(interactBtnSize + 30, interactBtnSize + 50);

        interactCanvasGroup = interactRoot.AddComponent<CanvasGroup>();
        interactCanvasGroup.alpha = 0f;
        interactRoot.SetActive(false);

        // 글로우 원
        GameObject glowObj = CyberpunkUITheme.CreateUIObject("Glow", interactRoot.transform);
        RectTransform glowRt = glowObj.GetComponent<RectTransform>();
        glowRt.anchorMin = new Vector2(0.5f, 0.55f);
        glowRt.anchorMax = new Vector2(0.5f, 0.55f);
        glowRt.sizeDelta = new Vector2(interactBtnSize + 30, interactBtnSize + 30);
        interactGlow = glowObj.AddComponent<Image>();
        interactGlow.sprite = CyberpunkUITheme.CreateGlowCircleSprite(128,
            new Color(CyberpunkUITheme.NeonCyan.r, CyberpunkUITheme.NeonCyan.g, CyberpunkUITheme.NeonCyan.b, 0.25f));
        interactGlow.raycastTarget = false;

        // 메인 원형 버튼
        GameObject btnObj = CyberpunkUITheme.CreateUIObject("Btn", interactRoot.transform);
        RectTransform btnRt = btnObj.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0.55f);
        btnRt.anchorMax = new Vector2(0.5f, 0.55f);
        btnRt.sizeDelta = new Vector2(interactBtnSize, interactBtnSize);

        Image btnBg = btnObj.AddComponent<Image>();
        btnBg.sprite = CyberpunkUITheme.CreateCircleSprite(128,
            new Color(0.04f, 0.08f, 0.16f, 0.85f),
            CyberpunkUITheme.NeonCyan, 3);

        interactButton = btnObj.AddComponent<Button>();
        interactButton.targetGraphic = btnBg;
        interactButton.onClick.AddListener(OnInteractClicked);

        // 아이콘 텍스트 (대화 아이콘 대용)
        GameObject iconObj = CyberpunkUITheme.CreateUIObject("Icon", btnObj.transform);
        RectTransform iconRt = iconObj.GetComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(8, 8);
        iconRt.offsetMax = new Vector2(-8, -8);
        TextMeshProUGUI iconText = iconObj.AddComponent<TextMeshProUGUI>();
        iconText.text = "!";
        iconText.fontSize = 32;
        iconText.color = CyberpunkUITheme.NeonCyan;
        iconText.alignment = TextAlignmentOptions.Center;
        iconText.fontStyle = FontStyles.Bold;
        iconText.raycastTarget = false;

        // 라벨
        GameObject labelObj = CyberpunkUITheme.CreateUIObject("Label", interactRoot.transform);
        RectTransform labelRt = labelObj.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0.5f, 0);
        labelRt.anchorMax = new Vector2(0.5f, 0);
        labelRt.pivot = new Vector2(0.5f, 0);
        labelRt.anchoredPosition = new Vector2(0, 0);
        labelRt.sizeDelta = new Vector2(120, 20);
        interactLabel = labelObj.AddComponent<TextMeshProUGUI>();
        interactLabel.text = "대화하기";
        interactLabel.fontSize = 12;
        interactLabel.color = CyberpunkUITheme.TextSecondary;
        interactLabel.alignment = TextAlignmentOptions.Center;
        interactLabel.raycastTarget = false;
    }

    // ──────────────────────────────────────────
    // Public API (기존 MobileInteractButton 호환)
    // ──────────────────────────────────────────

    public void ShowInteract(System.Action action, string label = "대화하기")
    {
        onInteractAction = action;
        interactVisible = true;

        if (interactRoot != null)
        {
            interactRoot.SetActive(true);
            if (interactLabel != null) interactLabel.text = label;
        }
    }

    public void HideInteract()
    {
        onInteractAction = null;
        interactVisible = false;
    }

    public void SetLocationName(string name)
    {
        if (locationText != null)
            locationText.text = name;
    }

    private void OnInteractClicked()
    {
        onInteractAction?.Invoke();
    }

    // ──────────────────────────────────────────
    // 애니메이션 업데이트
    // ──────────────────────────────────────────

    private void Update()
    {
        // 인터랙션 버튼 페이드 인/아웃
        if (interactCanvasGroup != null)
        {
            float target = interactVisible ? 1f : 0f;
            interactCanvasGroup.alpha = Mathf.MoveTowards(interactCanvasGroup.alpha, target, Time.deltaTime * 5f);

            if (!interactVisible && interactCanvasGroup.alpha <= 0.01f && interactRoot.activeSelf)
                interactRoot.SetActive(false);
        }

        // 인터랙션 버튼 펄스 글로우
        if (interactVisible && interactGlow != null)
        {
            float pulse = Mathf.Sin(Time.time * 3f) * 0.5f + 0.5f;
            Color c = interactGlow.color;
            c.a = 0.15f + pulse * 0.15f;
            interactGlow.color = c;

            // 버튼 살짝 스케일 펄스
            if (interactRoot != null)
            {
                Transform btnTransform = interactRoot.transform.Find("Btn");
                if (btnTransform != null)
                {
                    float scale = 1f + pulse * 0.03f;
                    btnTransform.localScale = new Vector3(scale, scale, 1f);
                }
            }
        }
    }
}
