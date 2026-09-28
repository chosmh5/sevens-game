using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DialogueLog : MonoBehaviour
{
    public static DialogueLog Instance { get; private set; }

    [SerializeField] private int maxEntries = 100;

    private readonly List<(string speaker, string line)> entries = new();

    // UI refs
    private Canvas canvas;
    private GameObject collapsedBar;
    private TMP_Text previewLabel;
    private GameObject expandedPanel;
    private RectTransform contentRT;
    private ScrollRect scrollRect;
    private bool isOpen;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        DisableEventNotificationUI();
        BuildUI();
    }

    void DisableEventNotificationUI()
    {
        var existing = Object.FindAnyObjectByType<EventNotificationUI>();
        if (existing != null)
            existing.gameObject.SetActive(false);
    }

    public void AddEntry(string speaker, string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        entries.Add((speaker, line));
        if (entries.Count > maxEntries)
            entries.RemoveAt(0);

        UpdatePreview(speaker, line);

        if (isOpen)
            AppendEntryUI(speaker, line);
    }

    private void UpdatePreview(string speaker, string line)
    {
        if (previewLabel == null) return;
        string hex = ColorUtility.ToHtmlStringRGB(CyberpunkUITheme.NeonCyan);
        previewLabel.text = $"<color=#{hex}>{speaker}</color>  {line}";
    }

    private void Toggle()
    {
        isOpen = !isOpen;
        collapsedBar.SetActive(!isOpen);
        expandedPanel.SetActive(isOpen);

        if (isOpen)
            RebuildContent();
    }

    private void RebuildContent()
    {
        foreach (Transform child in contentRT)
            Destroy(child.gameObject);

        foreach (var (speaker, line) in entries)
            AppendEntryUI(speaker, line);

        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private void AppendEntryUI(string speaker, string line)
    {
        var go = new GameObject("Entry");
        go.transform.SetParent(contentRT, false);

        go.AddComponent<RectTransform>();

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 5, 5);
        layout.spacing = 2;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // speaker
        var nameGO = new GameObject("Speaker");
        nameGO.transform.SetParent(go.transform, false);
        var nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
        nameTMP.text = speaker;
        nameTMP.fontSize = 11;
        nameTMP.color = CyberpunkUITheme.NeonCyan;
        nameTMP.fontStyle = FontStyles.Bold;
        nameTMP.raycastTarget = false;
        var nameLE = nameGO.AddComponent<LayoutElement>();
        nameLE.preferredHeight = 16;

        // line
        var lineGO = new GameObject("Line");
        lineGO.transform.SetParent(go.transform, false);
        var lineTMP = lineGO.AddComponent<TextMeshProUGUI>();
        lineTMP.text = line;
        lineTMP.fontSize = 12;
        lineTMP.color = CyberpunkUITheme.TextPrimary;
        lineTMP.enableWordWrapping = true;
        lineTMP.raycastTarget = false;

        // separator
        var sepGO = new GameObject("Sep");
        sepGO.transform.SetParent(go.transform, false);
        sepGO.AddComponent<Image>().color = new Color(
            CyberpunkUITheme.NeonBlue.r, CyberpunkUITheme.NeonBlue.g,
            CyberpunkUITheme.NeonBlue.b, 0.1f);
        sepGO.GetComponent<Image>().raycastTarget = false;
        var sepLE = sepGO.AddComponent<LayoutElement>();
        sepLE.preferredHeight = 1;

        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    // ── UI Build ───────────────────────────────────────

    void BuildUI()
    {
        // canvas (sortingOrder 9 — EventNotificationUI와 동일)
        var canvasGO = new GameObject("DialogueLogCanvas");
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        BuildCollapsedBar(canvasGO.transform);
        BuildExpandedPanel(canvasGO.transform);
        expandedPanel.SetActive(false);
    }

    void BuildCollapsedBar(Transform parent)
    {
        collapsedBar = new GameObject("CollapsedBar");
        collapsedBar.transform.SetParent(parent, false);

        var bg = collapsedBar.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.04f, 0.10f, 0.88f);

        var rt = collapsedBar.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(360f, 76f);
        rt.anchoredPosition = new Vector2(-24f, 24f);

        var btn = collapsedBar.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(Toggle);

        // header
        var headerGO = new GameObject("Header");
        headerGO.transform.SetParent(collapsedBar.transform, false);
        headerGO.AddComponent<RectTransform>();
        var headerTMP = headerGO.AddComponent<TextMeshProUGUI>();
        headerTMP.text = "대화 로그";
        headerTMP.fontSize = 13;
        headerTMP.color = CyberpunkUITheme.NeonCyan;
        headerTMP.fontStyle = FontStyles.Bold;
        headerTMP.alignment = TextAlignmentOptions.Left;
        headerTMP.raycastTarget = false;
        var headerRT = headerGO.GetComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0f, 0.5f);
        headerRT.anchorMax = new Vector2(1f, 1f);
        headerRT.offsetMin = new Vector2(12f, 2f);
        headerRT.offsetMax = new Vector2(-10f, -4f);

        // preview line
        var previewGO = new GameObject("Preview");
        previewGO.transform.SetParent(collapsedBar.transform, false);
        previewGO.AddComponent<RectTransform>();
        previewLabel = previewGO.AddComponent<TextMeshProUGUI>();
        previewLabel.text = "<color=#999>아직 대화가 없습니다</color>";
        previewLabel.fontSize = 11;
        previewLabel.color = new Color(0.7f, 0.7f, 0.7f);
        previewLabel.alignment = TextAlignmentOptions.Left;
        previewLabel.enableWordWrapping = true;
        previewLabel.overflowMode = TextOverflowModes.Ellipsis;
        previewLabel.maxVisibleLines = 1;
        previewLabel.raycastTarget = false;
        var previewRT = previewGO.GetComponent<RectTransform>();
        previewRT.anchorMin = new Vector2(0f, 0f);
        previewRT.anchorMax = new Vector2(1f, 0.5f);
        previewRT.offsetMin = new Vector2(12f, 4f);
        previewRT.offsetMax = new Vector2(-10f, 0f);

        // expand hint
        var hintGO = new GameObject("Hint");
        hintGO.transform.SetParent(collapsedBar.transform, false);
        hintGO.AddComponent<RectTransform>();
        var hintTMP = hintGO.AddComponent<TextMeshProUGUI>();
        hintTMP.text = "▲";
        hintTMP.fontSize = 12;
        hintTMP.color = new Color(CyberpunkUITheme.NeonBlue.r, CyberpunkUITheme.NeonBlue.g,
                                   CyberpunkUITheme.NeonBlue.b, 0.5f);
        hintTMP.alignment = TextAlignmentOptions.Center;
        hintTMP.raycastTarget = false;
        var hintRT = hintGO.GetComponent<RectTransform>();
        hintRT.anchorMin = new Vector2(1f, 0.5f);
        hintRT.anchorMax = new Vector2(1f, 0.5f);
        hintRT.pivot = new Vector2(1f, 0.5f);
        hintRT.sizeDelta = new Vector2(24f, 24f);
        hintRT.anchoredPosition = new Vector2(-4f, 0f);
    }

    void BuildExpandedPanel(Transform parent)
    {
        expandedPanel = new GameObject("ExpandedPanel");
        expandedPanel.transform.SetParent(parent, false);

        CyberpunkUITheme.ApplyPanelStyle(expandedPanel,
            new Color(0.02f, 0.02f, 0.06f, 0.93f),
            new Color(CyberpunkUITheme.NeonBlue.r, CyberpunkUITheme.NeonBlue.g,
                      CyberpunkUITheme.NeonBlue.b, 0.3f));

        var rt = expandedPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(380f, 480f);
        rt.anchoredPosition = new Vector2(-24f, 24f);

        CyberpunkUITheme.AddCornerDecorations(expandedPanel.transform,
            new Color(CyberpunkUITheme.NeonCyan.r, CyberpunkUITheme.NeonCyan.g,
                      CyberpunkUITheme.NeonCyan.b, 0.35f), 10f, 1f);

        // title bar
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(expandedPanel.transform, false);
        titleGO.AddComponent<RectTransform>();
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "DIALOGUE LOG";
        titleTMP.fontSize = 14;
        titleTMP.color = CyberpunkUITheme.NeonCyan;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.raycastTarget = false;
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1);
        titleRT.anchoredPosition = Vector2.zero;
        titleRT.sizeDelta = new Vector2(0, 36);

        // close button
        var closeGO = new GameObject("Close");
        closeGO.transform.SetParent(expandedPanel.transform, false);
        var closeBg = closeGO.AddComponent<Image>();
        closeBg.color = Color.clear;
        var closeBtn = closeGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        closeBtn.onClick.AddListener(Toggle);
        var closeRT = closeGO.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1, 1);
        closeRT.anchorMax = new Vector2(1, 1);
        closeRT.pivot = new Vector2(1, 1);
        closeRT.sizeDelta = new Vector2(36, 36);
        closeRT.anchoredPosition = Vector2.zero;
        var closeLabelGO = new GameObject("X");
        closeLabelGO.transform.SetParent(closeGO.transform, false);
        var closeTMP = closeLabelGO.AddComponent<TextMeshProUGUI>();
        closeTMP.text = "▼";
        closeTMP.fontSize = 14;
        closeTMP.color = CyberpunkUITheme.TextSecondary;
        closeTMP.alignment = TextAlignmentOptions.Center;
        closeTMP.raycastTarget = false;
        var closeLabelRT = closeLabelGO.GetComponent<RectTransform>();
        closeLabelRT.anchorMin = Vector2.zero;
        closeLabelRT.anchorMax = Vector2.one;
        closeLabelRT.offsetMin = Vector2.zero;
        closeLabelRT.offsetMax = Vector2.zero;

        // accent line
        var accentGO = new GameObject("Accent");
        accentGO.transform.SetParent(expandedPanel.transform, false);
        var accentImg = accentGO.AddComponent<Image>();
        accentImg.color = new Color(CyberpunkUITheme.NeonCyan.r, CyberpunkUITheme.NeonCyan.g,
                                     CyberpunkUITheme.NeonCyan.b, 0.2f);
        accentImg.raycastTarget = false;
        var accentRT = accentGO.GetComponent<RectTransform>();
        accentRT.anchorMin = new Vector2(0.05f, 1);
        accentRT.anchorMax = new Vector2(0.95f, 1);
        accentRT.pivot = new Vector2(0.5f, 1);
        accentRT.anchoredPosition = new Vector2(0, -36);
        accentRT.sizeDelta = new Vector2(0, 1);

        // scroll view
        var scrollGO = new GameObject("Scroll");
        scrollGO.transform.SetParent(expandedPanel.transform, false);
        var scrollRT = scrollGO.AddComponent<RectTransform>();
        scrollRT.anchorMin = Vector2.zero;
        scrollRT.anchorMax = Vector2.one;
        scrollRT.offsetMin = new Vector2(4, 8);
        scrollRT.offsetMax = new Vector2(-4, -40);

        scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30;
        scrollGO.AddComponent<RectMask2D>();

        // content
        var contentGO = CyberpunkUITheme.CreateUIObject("Content", scrollGO.transform);
        contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0, 1);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = Vector2.zero;

        var contentLayout = contentGO.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 0;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;

        var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = contentRT;
        scrollRect.viewport = scrollRT;
    }
}
