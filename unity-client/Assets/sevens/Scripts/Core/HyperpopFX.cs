using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 중 하이퍼팝 연출(짧고 가장자리만) + 동작 줄이기 설정.
///
///  HyperpopFX.Pulse(intensity) : 하이라이트 순간 화면 위·아래 가장자리에 글리치 바, 양옆에 색종이가
///                                0.5~0.7초 터진다. 카드·대사창이 있는 가운데 영역은 건드리지 않는다.
///  HyperpopFX.ReducedMotion    : 켜면 펄스는 안 나오고 화면 전환은 단순 페이드가 된다(PlayerPrefs 저장).
/// </summary>
public class HyperpopFX : MonoBehaviour
{
    private const string PrefKey = "fx.reducedMotion";
    private const string ShaderPath = "Hyperpop/HyperpopTransition";

    public static bool ReducedMotion
    {
        get => PlayerPrefs.GetInt(PrefKey, 0) == 1;
        set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    private static HyperpopFX _instance;
    private static bool _failed;

    private static readonly int IdP = Shader.PropertyToID("_P");
    private static readonly int IdPhase = Shader.PropertyToID("_Phase");
    private static readonly int IdSeed = Shader.PropertyToID("_Seed");
    private static readonly int IdRes = Shader.PropertyToID("_Res");

    private Canvas _canvas;
    private Material _mat;
    private float _t = 1f, _dur = 0.6f;

    /// <summary>하이라이트 펄스. intensity 2 = 짧게, 3 이상 = 조금 길게.</summary>
    public static void Pulse(int intensity = 2)
    {
        if (ReducedMotion || intensity < 2) return;
        var fx = Get();
        if (fx == null) return;
        fx._dur = intensity >= 3 ? 0.75f : 0.55f;
        fx._t = 0f;
        fx._mat.SetFloat(IdSeed, Random.Range(0f, 50f));
        fx._canvas.enabled = true;
    }

    private static HyperpopFX Get()
    {
        if (_instance != null) return _instance;
        if (_failed) return null;
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported) { _failed = true; return null; }

        var go = new GameObject("[HyperpopFX]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<HyperpopFX>();
        _instance._mat = new Material(shader) { name = "HyperpopPulse (Instance)", hideFlags = HideFlags.DontSave };
        _instance._mat.SetFloat(IdPhase, 2f);
        _instance._canvas = go.AddComponent<Canvas>();
        _instance._canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _instance._canvas.sortingOrder = 19;   // 카드게임 UI(10) 위, 하이라이트 배너(20) 아래
        var img = RetroTheme.Stretch(RetroTheme.Node("Pulse", go.transform)).gameObject.AddComponent<RawImage>();
        img.material = _instance._mat;
        img.texture = Texture2D.whiteTexture;
        img.raycastTarget = false;             // 터치는 그대로 통과
        _instance._canvas.enabled = false;
        return _instance;
    }

    private void Update()
    {
        if (!_canvas.enabled) return;
        _t += Time.unscaledDeltaTime;
        _mat.SetFloat(IdP, Mathf.Clamp01(_t / _dur));
        _mat.SetVector(IdRes, new Vector4(Screen.width, Screen.height, 0f, 0f));
        if (_t >= _dur) _canvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
        if (_instance == this) _instance = null;
    }
}
