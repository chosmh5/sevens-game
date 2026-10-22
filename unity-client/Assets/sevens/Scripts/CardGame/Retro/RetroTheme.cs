using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

/// <summary>
/// 카드게임 레트로 스킨의 공용 팔레트·폰트·스프라이트·UI 생성 도우미.
/// 디자인 기준 해상도 1560×720(가로 모바일), 최소 글자 22px.
///
/// 폰트: Resources/Fonts/Galmuri11(.ttf), Galmuri11-Bold(.ttf) 를 런타임에 TMP 동적 폰트로 만든다.
/// 스프라이트: 둥근 모서리·검은 외곽선·아래쪽 음영을 텍스처로 구워 9-slice 로 쓴다(이미지 에셋 불필요).
/// </summary>
public static class RetroTheme
{
    /// <summary>false 로 두면 카드·필드가 예전(Kenney 스프라이트) 모양으로 돌아간다.</summary>
    public static bool Enabled = true;

    public static readonly Vector2 ReferenceResolution = new Vector2(1560f, 720f);

    // ── 팔레트 ───────────────────────────────────────────────────
    public static readonly Color Ink       = Hex("0D0B18");   // 외곽선·그림자
    public static readonly Color Bg        = Hex("1A1730");
    public static readonly Color BgStripe  = Hex("1D1A36");
    public static readonly Color PanelColor = Hex("2C2945");
    public static readonly Color Inset     = Hex("15132A");
    public static readonly Color Felt      = Hex("1F3A3F");
    public static readonly Color FeltRim   = Hex("2A4E52");
    public static readonly Color Text      = Hex("F4EFE3");
    public static readonly Color Muted     = Hex("A8A3C7");
    public static readonly Color Faint     = Hex("6E6A92");
    public static readonly Color CardFace  = Hex("F4EFE3");
    public static readonly Color Accent    = Hex("45D7E6");
    public static readonly Color Gold      = Hex("F5B83D");
    public static readonly Color Pink      = Hex("F2547D");
    public static readonly Color Cyan      = Hex("3FC6D6");
    public static readonly Color Blue      = Hex("4F8EF7");
    public static readonly Color BtnOrange = Hex("D9631A");
    public static readonly Color BtnBlue   = Hex("2F6FDB");
    public static readonly Color BtnRed    = Hex("C9372F");

    /// <summary>4색 덱: ♠ 남색, ♥ 빨강, ♦ 주황, ♣ 초록.</summary>
    public static Color SuitInk(Suit s)
    {
        switch (s)
        {
            case Suit.Spade:   return Hex("2B3A67");
            case Suit.Heart:   return Hex("D23A44");
            case Suit.Diamond: return Hex("D9631A");
            default:           return Hex("2E7D4F");
        }
    }

    /// <summary>어두운 배경 위에 쓰는 밝은 문양 색.</summary>
    public static Color SuitLight(Suit s)
    {
        switch (s)
        {
            case Suit.Spade:   return Hex("8FA6E8");
            case Suit.Heart:   return Hex("FF7A82");
            case Suit.Diamond: return Hex("FFA25A");
            default:           return Hex("6FD69A");
        }
    }

    /// <summary>좌석 캐릭터 색. 이름으로 찾는다(세비-호/류/진, 그 외는 플레이어 파랑).</summary>
    public static Color SeatColor(string name)
    {
        switch (name)
        {
            case "세비-호": return Pink;
            case "류":     return Cyan;
            case "진":     return Gold;
            default:       return Blue;
        }
    }

    public static string Initial(string name) =>
        string.IsNullOrEmpty(name) ? "?" : (name == "플레이어" ? "나" : name.Substring(0, 1));

    public static string RankLabel(int n)
    {
        switch (n)
        {
            case 1:  return "A";
            case 11: return "J";
            case 12: return "Q";
            case 13: return "K";
            default: return n.ToString();
        }
    }

    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }

    public static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // ── 폰트 ─────────────────────────────────────────────────────
    private static TMP_FontAsset _regular, _bold;
    private static bool _fontTried;

    public static TMP_FontAsset Regular { get { LoadFonts(); return _regular; } }
    public static TMP_FontAsset Bold    { get { LoadFonts(); return _bold ?? _regular; } }

    private static void LoadFonts()
    {
        if (_fontTried) return;
        _fontTried = true;
        _regular = CreateDynamic("Fonts/Galmuri11");
        _bold    = CreateDynamic("Fonts/Galmuri11-Bold");

        // 굵은 글꼴에 없는 글자는 보통 굵기 → 기본 TMP 폰트(NotoSansKR) 순으로 대체.
        var fallbackDefault = TMP_Settings.defaultFontAsset;
        if (_regular != null && fallbackDefault != null && fallbackDefault != _regular)
            _regular.fallbackFontAssetTable = new List<TMP_FontAsset> { fallbackDefault };
        if (_bold != null && _regular != null)
        {
            var list = new List<TMP_FontAsset> { _regular };
            if (fallbackDefault != null) list.Add(fallbackDefault);
            _bold.fallbackFontAssetTable = list;
        }
        if (_regular == null)
        {
            Debug.LogWarning("[RetroTheme] Resources/Fonts/Galmuri11 폰트가 없어 기본 TMP 폰트를 씁니다.");
            _regular = fallbackDefault;
        }
    }

    private static TMP_FontAsset CreateDynamic(string resourcePath)
    {
        var font = Resources.Load<Font>(resourcePath);
        if (font == null) return null;
        // 도트 폰트라 11의 배수(22·33·44)로 쓸 때 가장 또렷하다. 44pt 로 샘플링.
        var fa = TMP_FontAsset.CreateFontAsset(font, 44, 5, GlyphRenderMode.SDFAA, 1024, 1024,
                                               AtlasPopulationMode.Dynamic, true);
        if (fa != null)
        {
            fa.name = font.name + " (Retro Dynamic)";
            // 빌드에서 셰이더가 빠지지 않도록 기본 TMP 폰트와 같은 셰이더를 쓴다(언더레이 지원).
            var def = TMP_Settings.defaultFontAsset;
            if (def != null && def.material != null && fa.material != null)
                fa.material.shader = def.material.shader;
        }
        return fa;
    }

    // ── 스프라이트 ───────────────────────────────────────────────
    private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

    /// <summary>
    /// 흰 바탕 + 검은 외곽선(border px) + 아래쪽 음영 띠(bevel px, 회색) 둥근 사각형.
    /// Image.color 로 칠하면 바탕만 색이 바뀌고 외곽선은 검정으로 남는다.
    /// </summary>
    public static Sprite PanelSprite(int radius, int border, int bevel = 0)
    {
        string key = $"panel_{radius}_{border}_{bevel}";
        if (_sprites.TryGetValue(key, out var cached)) return cached;

        int slice = radius + border + bevel + 2;
        int size = slice * 2 + 4;
        var tex = NewTexture(size, size);
        var px = new Color32[size * size];
        Color32 ink = Ink, white = Color.white, shade = new Color(0.68f, 0.68f, 0.68f, 1f), clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = RoundedDistance(x + 0.5f, y + 0.5f, size, size, radius + border);
            Color32 c;
            if (d > 0f) c = clear;
            else if (d > -border) c = ink;
            else if (bevel > 0 && y < border + bevel) c = shade;
            else c = white;
            px[y * size + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                               SpriteMeshType.FullRect, new Vector4(slice, slice, slice, slice));
        _sprites[key] = sp;
        return sp;
    }

    /// <summary>외곽선만 있는 둥근 사각형(테두리 = 흰색, 안쪽 투명). 강조 링·점선 대용.</summary>
    public static Sprite RingSprite(int radius, int thickness)
    {
        string key = $"ring_{radius}_{thickness}";
        if (_sprites.TryGetValue(key, out var cached)) return cached;
        int slice = radius + thickness + 2;
        int size = slice * 2 + 4;
        var tex = NewTexture(size, size);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = RoundedDistance(x + 0.5f, y + 0.5f, size, size, radius + thickness);
            px[y * size + x] = (d <= 0f && d > -thickness) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
        }
        tex.SetPixels32(px);
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                               SpriteMeshType.FullRect, new Vector4(slice, slice, slice, slice));
        _sprites[key] = sp;
        return sp;
    }

    /// <summary>사선 줄무늬 배경 타일(이음매 없음).</summary>
    public static Sprite StripeTile()
    {
        const string key = "stripes";
        if (_sprites.TryGetValue(key, out var cached)) return cached;
        const int n = 72;
        var tex = NewTexture(n, n);
        tex.wrapMode = TextureWrapMode.Repeat;
        var px = new Color32[n * n];
        Color32 a = Bg, b = BgStripe;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
            px[y * n + x] = ((x + y) % n) < n / 2 ? a : b;
        tex.SetPixels32(px);
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        _sprites[key] = sp;
        return sp;
    }

    /// <summary>CRT 주사선 타일(4px 주기, 위 2px 어둡게).</summary>
    public static Sprite ScanlineTile()
    {
        const string key = "scan";
        if (_sprites.TryGetValue(key, out var cached)) return cached;
        var tex = NewTexture(4, 4);
        tex.wrapMode = TextureWrapMode.Repeat;
        var px = new Color32[16];
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            px[y * 4 + x] = y >= 2 ? new Color32(0, 0, 0, 41) : new Color32(0, 0, 0, 0);
        tex.SetPixels32(px);
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        _sprites[key] = sp;
        return sp;
    }

    /// <summary>가장자리로 갈수록 어두워지는 비네트(검정, 알파만).</summary>
    public static Sprite VignetteSprite()
    {
        const string key = "vignette";
        if (_sprites.TryGetValue(key, out var cached)) return cached;
        const int w = 256, h = 128;
        var tex = NewTexture(w, h);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx * 0.85f + dy * dy * 0.85f);
            float a = Mathf.Clamp01((r - 0.55f) / 0.75f);
            px[y * w + x] = new Color32(0, 0, 0, (byte)(a * a * 190));
        }
        tex.SetPixels32(px);
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        _sprites[key] = sp;
        return sp;
    }

    private static Texture2D NewTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.DontSave;
        return tex;
    }

    /// <summary>둥근 사각형 SDF(음수 = 안쪽).</summary>
    private static float RoundedDistance(float x, float y, int w, int h, float r)
    {
        float hx = w * 0.5f, hy = h * 0.5f;
        float qx = Mathf.Abs(x - hx) - (hx - r);
        float qy = Mathf.Abs(y - hy) - (hy - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    // ── UI 생성 도우미 ───────────────────────────────────────────
    public static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>앵커·피벗·위치·크기를 한 번에.</summary>
    public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static RectTransform Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        return rt;
    }

    /// <summary>레트로 패널(검은 외곽선 + 딱딱한 그림자).</summary>
    public static Image Panel(RectTransform rt, Color fill, int radius = 10, int border = 3, int bevel = 0, float shadow = 6f)
    {
        var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        img.sprite = PanelSprite(radius, border, bevel);
        img.type = Image.Type.Sliced;
        img.color = fill;
        img.raycastTarget = false;
        if (shadow > 0f)
        {
            var sh = rt.gameObject.GetComponent<Shadow>() ?? rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = Ink;
            sh.effectDistance = new Vector2(0f, -shadow);
            sh.useGraphicAlpha = true;
        }
        return img;
    }

    /// <summary>외곽선 없는 어두운 홈(숫자 칸 등).</summary>
    public static Image InsetBox(RectTransform rt, Color? fill = null, int radius = 8)
    {
        var img = rt.gameObject.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        img.sprite = PanelSprite(radius, 0);
        img.type = Image.Type.Sliced;
        img.color = fill ?? Inset;
        img.raycastTarget = false;
        return img;
    }

    public static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color,
                                        bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var rt = Node(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        Style(t, size, color, bold, align);
        t.text = text;
        return t;
    }

    public static void Style(TMP_Text t, float size, Color color, bool bold = false,
                             TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var f = bold ? Bold : Regular;
        if (f != null) t.font = f;
        t.enableAutoSizing = false;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = FontStyles.Normal;   // 굵기는 Bold 폰트로(가짜 볼드 X)
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        t.characterSpacing = 0f;
    }

    /// <summary>
    /// 레트로 버튼: 채움색 + 검은 외곽선 + 아래쪽 음영 + 딱딱한 그림자.
    /// 비활성일 때 어둡게(ColorBlock).
    /// </summary>
    public static Button StyleButton(Button btn, Color fill, int radius = 10)
    {
        var img = btn.GetComponent<Image>() ?? btn.gameObject.AddComponent<Image>();
        img.sprite = PanelSprite(radius, 3, 6);
        img.type = Image.Type.Sliced;
        img.color = Color.white;
        img.raycastTarget = true;
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.ColorTint;
        var cb = ColorBlock.defaultColorBlock;
        cb.normalColor      = fill;
        cb.highlightedColor = Color.Lerp(fill, Color.white, 0.12f);
        cb.pressedColor     = Darken(fill, 0.8f);
        cb.selectedColor    = fill;
        cb.disabledColor    = Color.Lerp(Darken(fill, 0.45f), PanelColor, 0.5f);
        cb.colorMultiplier  = 1f;
        cb.fadeDuration     = 0.06f;
        btn.colors = cb;
        var sh = btn.GetComponent<Shadow>() ?? btn.gameObject.AddComponent<Shadow>();
        sh.effectColor = Ink;
        sh.effectDistance = new Vector2(0f, -6f);
        return btn;
    }

    public static Button MakeButton(Transform parent, string name, Color fill, string text, float fontSize)
    {
        var rt = Node(name, parent);
        rt.gameObject.AddComponent<Image>();
        var btn = rt.gameObject.AddComponent<Button>();
        StyleButton(btn, fill);
        if (!string.IsNullOrEmpty(text))
        {
            var label = Label(rt, "Label", text, fontSize, Text, true);
            Stretch((RectTransform)label.transform);
            AddTextShadow(label);
        }
        return btn;
    }

    /// <summary>글자 아래 딱딱한 그림자(언더레이).</summary>
    public static void AddTextShadow(TMP_Text t, float offsetY = -1.2f)
    {
        var mat = t.fontMaterial; // 인스턴스 머티리얼
        mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        mat.SetColor(ShaderUtilities.ID_UnderlayColor, Ink);
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, offsetY);
        mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.2f);
    }

    /// <summary>작은 색 칩(이름표·배지).</summary>
    public static TextMeshProUGUI Chip(Transform parent, string name, string text, Color fill, Color textColor,
                                       float fontSize = 22f, bool outlined = true)
    {
        var rt = Node(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = PanelSprite(4, outlined ? 2 : 0);
        img.type = Image.Type.Sliced;
        img.color = fill;
        img.raycastTarget = false;
        var fitter = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        fitter.padding = new RectOffset(10, 10, 2, 2);
        fitter.childAlignment = TextAnchor.MiddleCenter;
        fitter.childControlWidth = fitter.childControlHeight = true;
        fitter.childForceExpandWidth = fitter.childForceExpandHeight = false;
        var csf = rt.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var t = Label(rt, "Text", text, fontSize, textColor, true);
        return t;
    }
}
