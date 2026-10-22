using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 하이퍼팝 화면 전환. GameSceneManager 가 페이드 대신 쓴다.
///
///  OUT : 지금 화면을 캡처해 글리치로 부수며 덮개로 덮는다(0.38초).
///  덮개 : 잉크(또는 흰) 화면 + 목적지 단어. 이 동안 화면 회전·씬 로드.
///  IN  : 덮개가 블록/바/창 모양으로 걷히며 새 씬이 드러난다(0.5초).
///
/// 스타일 4가지(Blocks · Melt · Bars · Tiles)를 번갈아 쓴다. 전체 길이는 예전 페이드(0.5+0.5초)와 비슷하다.
/// Resources/HyperpopPhotos 에 사진을 넣으면 블록·타일 안에 사진 조각이 섞인다(없으면 게임 화면 조각).
/// 동작 줄이기(HyperpopFX.ReducedMotion)가 켜져 있으면 단순 페이드로 동작한다.
/// </summary>
public class HyperpopTransition : MonoBehaviour
{
    public enum Style { Blocks = 0, Melt = 1, Bars = 2, Tiles = 3, Fade = 4 }

    private const string ShaderPath = "Hyperpop/HyperpopTransition";
    private const string PhotoFolder = "HyperpopPhotos";

    [SerializeField] private float outDuration = 0.38f;
    [SerializeField] private float inDuration = 0.5f;
    [Tooltip("덮개(단어)가 최소 이만큼은 보이게(초)")]
    [SerializeField] private float minHold = 0.18f;
    [Tooltip("-1 = 번갈아, 0~4 = 해당 스타일 고정(테스트용)")]
    [SerializeField] private int forceStyle = -1;

    private static readonly int IdP = Shader.PropertyToID("_P");
    private static readonly int IdPhase = Shader.PropertyToID("_Phase");
    private static readonly int IdStyle = Shader.PropertyToID("_Style");
    private static readonly int IdSeed = Shader.PropertyToID("_Seed");
    private static readonly int IdRes = Shader.PropertyToID("_Res");
    private static readonly int IdPhoto0 = Shader.PropertyToID("_Photo0");
    private static readonly int IdPhoto1 = Shader.PropertyToID("_Photo1");
    private static readonly int IdPhoto0ST = Shader.PropertyToID("_Photo0ST");
    private static readonly int IdPhoto1ST = Shader.PropertyToID("_Photo1ST");
    private static readonly int IdPhotoCount = Shader.PropertyToID("_PhotoCount");

    private Canvas _canvas;
    private RawImage _view;
    private Material _mat;
    private Texture2D _snap;
    private Texture2D[] _photos;
    private RectTransform _labelRoot;
    private TextMeshProUGUI[] _labels;
    private float _labelShownAt = -1f;
    private float _coverTime;
    private Style _style;
    private int _lastStyle = -1;

    public bool Busy { get; private set; }

    /// <summary>셰이더를 못 쓰면 null(→ 호출 쪽이 예전 페이드를 쓴다).</summary>
    public static HyperpopTransition Create(Transform parent)
    {
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("[Hyperpop] 전환 셰이더를 못 찾았거나 지원되지 않아 기본 페이드를 씁니다.");
            return null;
        }
        var go = new GameObject("[HyperpopTransition]");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<HyperpopTransition>();
        t.Build(shader);
        return t;
    }

    private void Build(Shader shader)
    {
        _mat = new Material(shader) { name = "HyperpopTransition (Instance)", hideFlags = HideFlags.DontSave };
        _photos = Resources.LoadAll<Texture2D>(PhotoFolder);

        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1000;
        gameObject.AddComponent<GraphicRaycaster>();

        _view = RetroTheme.Stretch(RetroTheme.Node("Screen", transform)).gameObject.AddComponent<RawImage>();
        _view.material = _mat;
        _view.texture = Texture2D.whiteTexture;
        _view.raycastTarget = true;   // 전환 중 터치 차단

        _labelRoot = RetroTheme.Stretch(RetroTheme.Node("Label", transform));
        _labels = new TextMeshProUGUI[3];
        for (int i = 0; i < _labels.Length; i++)
            _labels[i] = RetroTheme.Label(_labelRoot, i == 0 ? "Word" : "Echo" + i, "", 60f, Color.white, true);

        SetVisible(false);
    }

    // ── 공개 ───────────────────────────────────────────────────────

    /// <summary>지금 화면 → 덮개. 끝나면 화면이 완전히 가려져 있다.</summary>
    public IEnumerator PlayOut(string label)
    {
        Busy = true;
        _style = PickStyle();

        yield return new WaitForEndOfFrame();
        ReleaseSnap();
        _snap = ScreenCapture.CaptureScreenshotAsTexture();
        _view.texture = _snap != null ? (Texture)_snap : Texture2D.whiteTexture;

        Begin(0f, label);
        float t = 0f;
        while (t < outDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / outDuration);
            SetP(p);
            if (p > 0.8f) ShowLabel();
            UpdateLabel(1f);
            yield return null;
        }
        SetP(1f);
        ShowLabel();
        _coverTime = Time.unscaledTime;
    }

    /// <summary>캡처 없이 바로 덮인 상태로(앱 시작 시).</summary>
    public void CoverNow(string label)
    {
        Busy = true;
        _style = PickStyle();
        ReleaseSnap();
        _view.texture = Texture2D.whiteTexture;
        Begin(0f, label);
        SetP(1f);
        ShowLabel();
        _coverTime = Time.unscaledTime;
    }

    /// <summary>덮개 → 새 화면.</summary>
    public IEnumerator PlayIn()
    {
        float wait = minHold - (Time.unscaledTime - _coverTime);
        while (wait > 0f)
        {
            wait -= Time.unscaledDeltaTime;
            UpdateLabel(1f);
            yield return null;
        }

        ReleaseSnap();
        _view.texture = Texture2D.whiteTexture;
        PreparePhotos();   // 화면이 돌았을 수 있으니 사진 맞춤을 다시
        _mat.SetFloat(IdPhase, 1f);
        _mat.SetFloat(IdSeed, Random.Range(0f, 50f));

        float t = 0f;
        while (t < inDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / inDuration);
            SetP(p);
            UpdateLabel(1f - Mathf.Clamp01(p / 0.25f));
            yield return null;
        }
        SetVisible(false);
        Busy = false;
    }

    // ── 내부 ───────────────────────────────────────────────────────

    private Style PickStyle()
    {
        if (HyperpopFX.ReducedMotion) return Style.Fade;
        if (forceStyle >= 0 && forceStyle <= 4) return (Style)forceStyle;
        int s;
        do s = Random.Range(0, 4); while (s == _lastStyle);
        _lastStyle = s;
        return (Style)s;
    }

    private void Begin(float phase, string label)
    {
        _mat.SetFloat(IdStyle, (float)_style);
        _mat.SetFloat(IdPhase, phase);
        _mat.SetFloat(IdSeed, Random.Range(0f, 50f));
        PreparePhotos();
        SetP(0f);
        SetVisible(true);

        // Melt 는 흰 덮개 → 잉크 글씨, 나머지는 잉크 덮개 → 밝은 글씨
        Color c = _style == Style.Melt ? RetroTheme.Ink : RetroTheme.Text;
        for (int i = 0; i < _labels.Length; i++)
        {
            _labels[i].text = label ?? "";
            _labels[i].color = RetroTheme.WithAlpha(c, i == 0 ? 1f : i == 1 ? 0.35f : 0.14f);
        }
        _labelShownAt = -1f;
        UpdateLabel(0f);
    }

    private void ShowLabel()
    {
        if (_labelShownAt < 0f) _labelShownAt = Time.unscaledTime;
    }

    /// <summary>단어 + 아래로 겹친 잔상 두 줄. 나타날 때 빠르게 타이핑되고 잠깐 흔들린다.</summary>
    private void UpdateLabel(float alpha)
    {
        bool shown = _labelShownAt >= 0f && !string.IsNullOrEmpty(_labels[0].text);
        _labelRoot.gameObject.SetActive(shown && alpha > 0.01f);
        if (!_labelRoot.gameObject.activeSelf) return;

        float age = Time.unscaledTime - _labelShownAt;
        float size = Mathf.Min(Screen.width, Screen.height) * 0.085f;
        int len = _labels[0].text.Length;
        int visible = Mathf.Clamp(Mathf.CeilToInt(age / 0.16f * len), 1, len);
        float jitter = age < 0.12f && !HyperpopFX.ReducedMotion ? Random.Range(-1f, 1f) * size * 0.08f : 0f;

        for (int i = 0; i < _labels.Length; i++)
        {
            var l = _labels[i];
            l.fontSize = size;
            l.maxVisibleCharacters = visible;
            l.alpha = (i == 0 ? 1f : i == 1 ? 0.35f : 0.14f) * alpha;
            ((RectTransform)l.transform).anchoredPosition = new Vector2(i == 0 ? jitter : -jitter * 0.5f, (1 - i) * size * 0.92f);
        }
    }

    private void SetP(float p)
    {
        _mat.SetFloat(IdP, p);
        _mat.SetVector(IdRes, new Vector4(Screen.width, Screen.height, 0f, 0f));
    }

    private void SetVisible(bool on)
    {
        _canvas.enabled = on;
        if (!on) _labelRoot.gameObject.SetActive(false);
    }

    private void PreparePhotos()
    {
        int n = _photos != null ? _photos.Length : 0;
        _mat.SetFloat(IdPhotoCount, Mathf.Min(n, 2));
        if (n == 0) return;
        int a = Random.Range(0, n);
        int b = n > 1 ? (a + Random.Range(1, n)) % n : a;
        SetPhoto(IdPhoto0, IdPhoto0ST, _photos[a]);
        SetPhoto(IdPhoto1, IdPhoto1ST, _photos[b]);
    }

    /// <summary>사진을 화면에 '가득 채우기'로 맞추고, 남는 쪽은 무작위로 비켜 잘라 쓴다.</summary>
    private void SetPhoto(int texId, int stId, Texture2D tex)
    {
        _mat.SetTexture(texId, tex);
        float sa = Screen.width / (float)Mathf.Max(1, Screen.height);
        float pa = tex.width / (float)Mathf.Max(1, tex.height);
        Vector2 scale = pa > sa ? new Vector2(sa / pa, 1f) : new Vector2(1f, pa / sa);
        Vector2 off = new Vector2((1f - scale.x) * Random.value, (1f - scale.y) * Random.value);
        _mat.SetVector(stId, new Vector4(scale.x, scale.y, off.x, off.y));
    }

    private void ReleaseSnap()
    {
        if (_snap == null) return;
        if (_view != null && _view.texture == _snap) _view.texture = Texture2D.whiteTexture;
        Destroy(_snap);
        _snap = null;
    }

    private void OnDestroy()
    {
        ReleaseSnap();
        if (_mat != null) Destroy(_mat);
    }
}
