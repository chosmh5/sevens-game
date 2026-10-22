using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 홈(마이룸) 배경의 하이퍼팝 장식(가볍게). 배경 캔버스(캐릭터 뒤)라 세비-호·UI·터치를 가리지 않는다.
///
///  · 오른쪽 위 상태 로그(타이핑) — Log("mood : happy") 로 줄 추가
///  · 15~20초마다(그리고 Glitch() 호출 시) 0.2초 배경 글리치 — 무지개 바가 스친다
/// 동작 줄이기(HyperpopFX.ReducedMotion)면 글리치는 나오지 않는다.
/// </summary>
public class HomeBackdropFX : MonoBehaviour
{
    private static readonly Color[] Pal =
    {
        RetroTheme.Hex("FF549E"), RetroTheme.Hex("FFE04D"), RetroTheme.Hex("B8F54D"),
        RetroTheme.Hex("33E6F5"), RetroTheme.Hex("5C73FF"),
    };

    private Texture2D _rainbow;
    private readonly List<RawImage> _bars = new List<RawImage>();
    private TextMeshProUGUI _log;
    private readonly List<string> _lines = new List<string>();
    private string _typing = "";
    private float _typed;
    private float _glitchUntil, _nextGlitch;
    private int _shownChars = -1, _shownLinesVer = -1, _linesVer;   // 로그 글자를 실제로 바뀔 때만 다시 그린다
    private bool _shownCursor;
    private string _history = "";

    public static HomeBackdropFX Build(Transform bgCanvas, string characterName)
    {
        var root = RetroTheme.Stretch(RetroTheme.Node("[HyperpopBackdrop]", bgCanvas));
        var fx = root.gameObject.AddComponent<HomeBackdropFX>();
        fx.Init(root, characterName);
        return fx;
    }

    private void Init(RectTransform root, string name)
    {
        _rainbow = MakeRainbow();

        // 글리치 바
        for (int i = 0; i < 3; i++)
        {
            var r = RetroTheme.Place(RetroTheme.Node("GlitchBar" + i, root), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     Vector2.zero, new Vector2(2000f, 10f));
            var img = r.gameObject.AddComponent<RawImage>();
            img.texture = _rainbow;
            img.color = new Color(1f, 1f, 1f, 0.6f);
            img.raycastTarget = false;
            img.enabled = false;
            _bars.Add(img);
        }

        // 상태 로그
        _log = RetroTheme.Label(root, "Log", "", 18f, RetroTheme.WithAlpha(RetroTheme.Muted, 0.6f), false, TextAlignmentOptions.TopRight);
        RetroTheme.Place((RectTransform)_log.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -26f), new Vector2(340f, 130f));
        _log.textWrappingMode = TextWrappingModes.NoWrap;
        Log("room.preset loaded");
        Log($"link : {name} // online");

        _nextGlitch = Time.unscaledTime + Random.Range(15f, 20f);
    }

    /// <summary>오른쪽 위 로그에 한 줄(타이핑).</summary>
    public void Log(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (_typing.Length > 0) _lines.Add(_typing);
        _typing = "> " + line;
        _typed = 0f;
        while (_lines.Count > 3) _lines.RemoveAt(0);
        _history = _lines.Count > 0 ? string.Join("\n", _lines) + "\n" : "";
        _linesVer++;
    }

    /// <summary>배경만 0.2초 지지직(찌르기 등).</summary>
    public void Glitch()
    {
        if (HyperpopFX.ReducedMotion) return;
        _glitchUntil = Time.unscaledTime + 0.2f;
    }

    private void Update()
    {
        float t = Time.unscaledTime;
        if (!HyperpopFX.ReducedMotion && t > _nextGlitch)
        {
            Glitch();
            _nextGlitch = t + Random.Range(15f, 20f);
        }

        bool glitching = t < _glitchUntil;
        for (int i = 0; i < _bars.Count; i++)
        {
            var bar = _bars[i];
            bar.enabled = glitching;
            if (!glitching || Time.frameCount % 3 != i % 3) continue;
            var rt = bar.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, Random.Range(0.3f, 0.95f));
            rt.sizeDelta = new Vector2(2000f, Random.Range(4f, 18f));
            bar.uvRect = new Rect(Random.value, 0f, Random.Range(1f, 3f), 1f);
        }

        // 로그 타이핑 + 커서. 글자 수·커서·줄이 바뀐 프레임에만 텍스트를 다시 만든다
        // (예전엔 매 프레임 문자열을 새로 만들어 TMP 메시 재생성 + GC 가 계속 생겼다).
        if (_typed < _typing.Length) _typed += Time.unscaledDeltaTime * 30f;
        int n = Mathf.Min(_typing.Length, Mathf.FloorToInt(_typed));
        bool cursorOn = Mathf.FloorToInt(t * 2f) % 2 == 0;
        if (n == _shownChars && cursorOn == _shownCursor && _linesVer == _shownLinesVer) return;
        _shownChars = n;
        _shownCursor = cursorOn;
        _shownLinesVer = _linesVer;
        _log.text = _history + _typing.Substring(0, n) + (cursorOn ? "_" : " ");
    }

    private static Texture2D MakeRainbow()
    {
        const int n = 256;
        var tex = new Texture2D(n, 1, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color[n];
        for (int i = 0; i < n; i++)
        {
            float x = i / (float)n * Pal.Length;
            int a = Mathf.FloorToInt(x) % Pal.Length, b = (a + 1) % Pal.Length;
            px[i] = Color.Lerp(Pal[a], Pal[b], x - Mathf.Floor(x));
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private void OnDestroy()
    {
        if (_rainbow != null) Destroy(_rainbow);
    }
}
