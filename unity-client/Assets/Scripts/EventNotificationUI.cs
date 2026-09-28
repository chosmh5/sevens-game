using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 화면 우하단에 AI 캐릭터 대화 상황을 실시간으로 표시하는 HUD.
/// /conversation/status 를 주기적으로 polling해서 대화 중이면 알림을 띄운다.
/// </summary>
public class EventNotificationUI : MonoBehaviour
{
    [SerializeField] private float pollInterval  = 4f;   // 서버 polling 간격(초)
    [SerializeField] private float fadeDuration  = 0.4f;
    [SerializeField] private float hideDelay     = 8f;   // 대화 없을 때 숨기기까지 대기

    // ── UI 요소 ──────────────────────────────────────────────────────────
    private GameObject  _root;
    private CanvasGroup _group;
    private TMP_Text    _headerLabel;   // "💬 세비-호 ↔ 류"
    private TMP_Text    _lineLabel;     // 최신 대사
    private TMP_Text    _dotLabel;      // 깜빡이는 점

    private float _hideTimer;
    private bool  _visible;
    private Coroutine _fadeCoroutine;
    private Coroutine _dotCoroutine;

    void Start()
    {
        BuildUI();
        SetVisible(false, instant: true);
        StartCoroutine(PollLoop());
    }

    // ══════════════════════════════════════════════════════════════════
    //  Polling
    // ══════════════════════════════════════════════════════════════════

    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(pollInterval);
            yield return ServerClient.Instance.GetConversationStatus(OnStatus);
        }
    }

    void OnStatus(ConversationStatusData data)
    {
        if (data == null || !data.is_talking || data.speakers == null || data.speakers.Length < 2)
        {
            // 대화 중 아님 → 타이머 후 숨김
            _hideTimer += pollInterval;
            if (_hideTimer > hideDelay)
                SetVisible(false);
            return;
        }

        _hideTimer = 0f;

        // 헤더: "💬 세비-호 ↔ 류"
        _headerLabel.text = "💬  " + string.Join(" ↔ ", data.speakers);

        // 최신 대사
        if (data.latest_line != null && !string.IsNullOrEmpty(data.latest_line.message))
            _lineLabel.text = $"<color=#FFDD66>{data.latest_line.speaker}</color>  {data.latest_line.message}";

        SetVisible(true);
    }

    // ══════════════════════════════════════════════════════════════════
    //  표시/숨김
    // ══════════════════════════════════════════════════════════════════

    void SetVisible(bool show, bool instant = false)
    {
        if (_visible == show) return;
        _visible = show;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(Fade(show ? 1f : 0f, instant ? 0f : fadeDuration));

        // ★ 숨길 때도 BlinkDot 코루틴을 정지해야 불필요한 연산 방지
        if (_dotCoroutine != null) { StopCoroutine(_dotCoroutine); _dotCoroutine = null; }
        if (show)
            _dotCoroutine = StartCoroutine(BlinkDot());
    }

    IEnumerator Fade(float target, float duration)
    {
        float start = _group.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            _group.alpha = Mathf.Lerp(start, target, duration > 0 ? t / duration : 1f);
            yield return null;
        }
        _group.alpha = target;
    }

    IEnumerator BlinkDot()
    {
        string[] dots = { "●", "◉", "●", "○" };
        int i = 0;
        while (true)
        {
            _dotLabel.text = dots[i % dots.Length];
            i++;
            yield return new WaitForSeconds(0.5f);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  UI 생성
    // ══════════════════════════════════════════════════════════════════

    void BuildUI()
    {
        // 캔버스
        var canvasGO = new GameObject("EventNotificationCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        // 루트 패널 — 우하단
        _root = new GameObject("NotificationPanel");
        _root.transform.SetParent(canvasGO.transform, false);
        var panelImg = _root.AddComponent<Image>();
        panelImg.color = new Color(0.04f, 0.04f, 0.10f, 0.88f);

        var rt         = _root.GetComponent<RectTransform>();
        rt.anchorMin   = new Vector2(1f, 0f);
        rt.anchorMax   = new Vector2(1f, 0f);
        rt.pivot       = new Vector2(1f, 0f);
        rt.sizeDelta   = new Vector2(360f, 76f);
        rt.anchoredPosition = new Vector2(-24f, 24f);

        // CanvasGroup (페이드용)
        _group = _root.AddComponent<CanvasGroup>();

        // 깜빡이는 점 (좌측)
        _dotLabel = MakeLabel(_root.transform, "●", 13f, new Color(0.4f, 1f, 0.6f));
        var dotRT         = _dotLabel.GetComponent<RectTransform>();
        dotRT.anchorMin   = new Vector2(0f, 0.5f);
        dotRT.anchorMax   = new Vector2(0f, 0.5f);
        dotRT.pivot       = new Vector2(0f, 0.5f);
        dotRT.sizeDelta   = new Vector2(24f, 24f);
        dotRT.anchoredPosition = new Vector2(10f, 0f);

        // 헤더 (누가 대화 중인지)
        _headerLabel = MakeLabel(_root.transform, "💬", 13f, new Color(0.9f, 0.9f, 0.9f), FontStyles.Bold);
        _headerLabel.alignment = TextAlignmentOptions.Left;
        var headerRT         = _headerLabel.GetComponent<RectTransform>();
        headerRT.anchorMin   = new Vector2(0f, 0.5f);
        headerRT.anchorMax   = new Vector2(1f, 1f);
        headerRT.offsetMin   = new Vector2(32f, 2f);
        headerRT.offsetMax   = new Vector2(-10f, -2f);

        // 최신 대사
        _lineLabel = MakeLabel(_root.transform, "", 12f, new Color(0.75f, 0.75f, 0.75f));
        _lineLabel.alignment        = TextAlignmentOptions.Left;
        _lineLabel.enableWordWrapping = true;
        var lineRT         = _lineLabel.GetComponent<RectTransform>();
        lineRT.anchorMin   = new Vector2(0f, 0f);
        lineRT.anchorMax   = new Vector2(1f, 0.5f);
        lineRT.offsetMin   = new Vector2(32f, 4f);
        lineRT.offsetMax   = new Vector2(-10f, 0f);
    }

    static TMP_Text MakeLabel(Transform parent, string text, float size, Color color,
                               FontStyles style = FontStyles.Normal)
    {
        var go  = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }
}

// ── 서버 응답 모델 ─────────────────────────────────────────────────────

[Serializable]
public class ConversationStatusData
{
    public bool          is_talking;
    public string[]      speakers;
    public LatestLine    latest_line;
    public LatestLine[]  recent_lines;
}

[Serializable]
public class LatestLine
{
    public string speaker;
    public string message;
}
