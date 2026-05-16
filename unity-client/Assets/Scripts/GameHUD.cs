using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    TMP_Text   _locationLabel;
    TMP_Text   _notifyLabel;
    GameObject _notifyRoot;

    void Awake() => Instance = this;
    void Start()  => BuildUI();

    // ══════════════════════════════════════════════════════════════════
    //  공개 API
    // ══════════════════════════════════════════════════════════════════

    public void SetLocation(string name)
    {
        if (_locationLabel != null)
            _locationLabel.text = name;
    }

    public void ShowNotification(string msg, float duration = 3f)
    {
        StopAllCoroutines();
        StartCoroutine(ShowNotify(msg, duration));
    }

    // ══════════════════════════════════════════════════════════════════
    //  알림 코루틴
    // ══════════════════════════════════════════════════════════════════

    IEnumerator ShowNotify(string msg, float duration)
    {
        _notifyLabel.text = msg;
        _notifyRoot.SetActive(true);
        var cg = _notifyRoot.GetComponent<CanvasGroup>();

        for (float t = 0f; t < 0.3f; t += Time.deltaTime) { cg.alpha = t / 0.3f; yield return null; }
        cg.alpha = 1f;

        yield return new WaitForSeconds(duration);

        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { cg.alpha = 1f - t / 0.4f; yield return null; }
        _notifyRoot.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  UI 구성
    // ══════════════════════════════════════════════════════════════════

    void BuildUI()
    {
        var root = NewCanvas("GameHUDCanvas", 5);

        // ── 현재 위치 (좌상단) ───────────────────────────────────────────
        var locBg = NewGO("LocationBg", root.transform);
        locBg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        var locRT             = locBg.GetComponent<RectTransform>();
        locRT.anchorMin       = new Vector2(0f, 1f);
        locRT.anchorMax       = new Vector2(0f, 1f);
        locRT.pivot           = new Vector2(0f, 1f);
        locRT.sizeDelta       = new Vector2(160f, 36f);
        locRT.anchoredPosition = new Vector2(16f, -16f);

        _locationLabel = NewTMP(locBg.transform, "내 방", 15f, Color.white);
        _locationLabel.alignment = TextAlignmentOptions.Left;
        var lblRT      = _locationLabel.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = new Vector2(10f, 4f);
        lblRT.offsetMax = new Vector2(-6f, -4f);

        // ── 알림 토스트 (상단 중앙) ──────────────────────────────────────
        _notifyRoot = NewGO("NotifyRoot", root.transform);
        _notifyRoot.AddComponent<CanvasGroup>();
        var nRT             = _notifyRoot.GetComponent<RectTransform>();
        nRT.anchorMin       = new Vector2(0.5f, 1f);
        nRT.anchorMax       = new Vector2(0.5f, 1f);
        nRT.pivot           = new Vector2(0.5f, 1f);
        nRT.sizeDelta       = new Vector2(420f, 46f);
        nRT.anchoredPosition = new Vector2(0f, -20f);

        var notifyBg = NewGO("Bg", _notifyRoot.transform);
        notifyBg.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.15f, 0.92f);
        Stretch(notifyBg.GetComponent<RectTransform>());

        _notifyLabel = NewTMP(_notifyRoot.transform, "", 15f, Color.white);
        Stretch(_notifyLabel.GetComponent<RectTransform>());

        _notifyRoot.SetActive(false);
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

    static TMP_Text NewTMP(Transform parent, string text, float size, Color color, FontStyles style = FontStyles.Normal)
    {
        var go        = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
