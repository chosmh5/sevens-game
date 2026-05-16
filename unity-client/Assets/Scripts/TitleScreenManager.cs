using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TitleScreenManager : MonoBehaviour
{
    public static TitleScreenManager Instance { get; private set; }
    public static bool IsActive => Instance != null && Instance._active;

    bool _active = true;
    GameObject _root;

    void Awake() => Instance = this;
    void Start()  => BuildUI();

    // ══════════════════════════════════════════════════════════════════
    //  UI 구성
    // ══════════════════════════════════════════════════════════════════

    void BuildUI()
    {
        _root = NewCanvas("TitleCanvas", 100);

        // 전체 화면 배경
        var bg = NewGO("Background", _root.transform);
        bg.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.10f, 1f);
        Stretch(bg.GetComponent<RectTransform>());

        // 게임 타이틀
        var title = NewTMP(_root.transform, "SEVENS", 80f, new Color(0.4f, 0.8f, 1f), FontStyles.Bold);
        Place(title.GetComponent<RectTransform>(), new Vector2(0f, 100f), new Vector2(800f, 120f));

        // 부제목
        var sub = NewTMP(_root.transform, "AI 캐릭터와 함께하는 소셜 게임", 22f, new Color(0.55f, 0.65f, 0.78f));
        Place(sub.GetComponent<RectTransform>(), new Vector2(0f, 30f), new Vector2(600f, 40f));

        MakeButton("게임 시작", new Vector2(0f, -70f),  new Color(0.2f, 0.45f, 0.85f), OnStartClicked);
        MakeButton("종료",     new Vector2(0f, -140f), new Color(0.22f, 0.22f, 0.28f), OnQuitClicked);
    }

    void MakeButton(string label, Vector2 pos, Color color, System.Action onClick)
    {
        var go = NewGO(label + "Btn", _root.transform);
        go.AddComponent<Image>().color = color;
        Place(go.GetComponent<RectTransform>(), pos, new Vector2(220f, 54f));
        go.AddComponent<Button>().onClick.AddListener(() => onClick());

        var lbl = NewTMP(go.transform, label, 20f, Color.white, FontStyles.Bold);
        Stretch(lbl.GetComponent<RectTransform>());
    }

    // ══════════════════════════════════════════════════════════════════
    //  버튼 핸들러
    // ══════════════════════════════════════════════════════════════════

    void OnStartClicked() => StartCoroutine(FadeOut());

    void OnQuitClicked()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    IEnumerator FadeOut()
    {
        var cg = _root.AddComponent<CanvasGroup>();
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            cg.alpha = 1f - t / 0.5f;
            yield return null;
        }
        _active = false;
        _root.SetActive(false);
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

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
    }
}
