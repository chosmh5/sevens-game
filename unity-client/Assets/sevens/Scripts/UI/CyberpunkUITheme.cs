using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 사이버펑크 UI 테마 시스템
/// - 이미지 에셋 없이 코드만으로 모든 UI 스타일 생성
/// - 네온 글로우, 반투명 패널, 스캔라인 등 공통 스타일
/// - 다른 UI 스크립트에서 CyberpunkUITheme.XXX 로 호출
/// </summary>
public static class CyberpunkUITheme
{
    // ──────────────────────────────────────────
    // 컬러 팔레트
    // ──────────────────────────────────────────
    public static readonly Color NeonBlue = new Color(0.3f, 0.7f, 1f, 1f);
    public static readonly Color NeonPink = new Color(1f, 0.3f, 0.6f, 1f);
    public static readonly Color NeonCyan = new Color(0.4f, 1f, 0.95f, 1f);
    public static readonly Color WarmGold = new Color(1f, 0.85f, 0.5f, 1f);

    public static readonly Color PanelBg = new Color(0.03f, 0.03f, 0.08f, 0.85f);
    public static readonly Color PanelBgLight = new Color(0.06f, 0.06f, 0.14f, 0.75f);
    public static readonly Color BorderGlow = new Color(0.3f, 0.6f, 1f, 0.6f);

    public static readonly Color TextPrimary = new Color(0.9f, 0.95f, 1f, 1f);
    public static readonly Color TextSecondary = new Color(0.6f, 0.7f, 0.85f, 0.9f);
    public static readonly Color TextAccent = NeonCyan;

    public static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.5f);

    // ──────────────────────────────────────────
    // 사이즈 / 레이아웃 상수
    // ──────────────────────────────────────────
    public const float BorderWidth = 1.5f;
    public const float CornerRadius = 4f; // Sliced sprite 사용 시
    public const float GlowAlpha = 0.35f;

    // ──────────────────────────────────────────
    // 텍스처 생성 유틸리티 (에셋 불필요)
    // ──────────────────────────────────────────

    /// <summary>
    /// 단색 정사각형 텍스처 생성
    /// </summary>
    public static Texture2D CreateSolidTexture(int size, Color color)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// 네온 테두리가 있는 반투명 패널 텍스처 생성 (Sliced Sprite용)
    /// </summary>
    public static Sprite CreatePanelSprite(int size, Color bgColor, Color borderColor, int borderPx, int cornerPx)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 모서리 라운딩 체크
                bool inCorner = false;
                float cornerDist = 0;

                if (x < cornerPx && y < cornerPx)
                {
                    cornerDist = Vector2.Distance(new Vector2(x, y), new Vector2(cornerPx, cornerPx));
                    inCorner = true;
                }
                else if (x >= size - cornerPx && y < cornerPx)
                {
                    cornerDist = Vector2.Distance(new Vector2(x, y), new Vector2(size - cornerPx - 1, cornerPx));
                    inCorner = true;
                }
                else if (x < cornerPx && y >= size - cornerPx)
                {
                    cornerDist = Vector2.Distance(new Vector2(x, y), new Vector2(cornerPx, size - cornerPx - 1));
                    inCorner = true;
                }
                else if (x >= size - cornerPx && y >= size - cornerPx)
                {
                    cornerDist = Vector2.Distance(new Vector2(x, y), new Vector2(size - cornerPx - 1, size - cornerPx - 1));
                    inCorner = true;
                }

                // 코너 밖이면 투명
                if (inCorner && cornerDist > cornerPx)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                // 테두리 판정
                bool isBorder = x < borderPx || x >= size - borderPx || y < borderPx || y >= size - borderPx;
                if (inCorner && cornerDist > cornerPx - borderPx)
                    isBorder = true;

                if (isBorder)
                {
                    // 테두리 글로우: 바깥쪽은 밝게, 안쪽으로 갈수록 배경과 블렌드
                    tex.SetPixel(x, y, borderColor);
                }
                else
                {
                    tex.SetPixel(x, y, bgColor);
                }
            }
        }

        tex.Apply();

        // 9-Sliced Sprite: border 영역을 stretch 가능하게
        int sliceBorder = cornerPx + borderPx;
        return Sprite.Create(tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder));
    }

    /// <summary>
    /// 원형 스프라이트 생성 (조이스틱, 버튼용)
    /// </summary>
    public static Sprite CreateCircleSprite(int size, Color fillColor, Color borderColor, int borderPx)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        float center = size / 2f;
        float outerR = center;
        float innerR = center - borderPx;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));

                if (dist > outerR)
                {
                    tex.SetPixel(x, y, Color.clear);
                }
                else if (dist > innerR)
                {
                    // 테두리: 바깥 가장자리 안티앨리어싱
                    float edgeAlpha = Mathf.Clamp01(outerR - dist);
                    Color c = borderColor;
                    c.a *= edgeAlpha;
                    tex.SetPixel(x, y, c);
                }
                else
                {
                    tex.SetPixel(x, y, fillColor);
                }
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>
    /// 그라데이션 원형 글로우 스프라이트 (부드러운 발광 효과)
    /// </summary>
    public static Sprite CreateGlowCircleSprite(int size, Color glowColor)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        float center = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float t = Mathf.Clamp01(dist / center);
                // 부드러운 감쇠
                float alpha = Mathf.Pow(1f - t, 2.5f) * glowColor.a;
                Color c = glowColor;
                c.a = alpha;
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>
    /// 수평 스캔라인 텍스처 (오버레이용)
    /// </summary>
    public static Sprite CreateScanlineSprite(int width, int height, float lineAlpha, int lineSpacing)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;

        for (int y = 0; y < height; y++)
        {
            bool isLine = (y % lineSpacing) == 0;
            Color c = isLine ? new Color(0, 0, 0, lineAlpha) : Color.clear;
            for (int x = 0; x < width; x++)
                tex.SetPixel(x, y, c);
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
    }

    // ──────────────────────────────────────────
    // UI 컴포넌트 스타일링 헬퍼
    // ──────────────────────────────────────────

    /// <summary>
    /// Image 컴포넌트에 패널 스타일 적용
    /// </summary>
    public static Image ApplyPanelStyle(GameObject go, Color? bgColor = null, Color? borderColor = null)
    {
        Image img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();

        img.sprite = CreatePanelSprite(64,
            bgColor ?? PanelBg,
            borderColor ?? BorderGlow,
            2, 8);
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;

        return img;
    }

    /// <summary>
    /// 간단한 UI 오브젝트 생성 헬퍼
    /// </summary>
    public static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        return go;
    }

    /// <summary>
    /// 네온 글로우 테두리를 가진 아이콘 버튼 생성
    /// </summary>
    public static (GameObject root, Button button, Image icon) CreateIconButton(
        string name, Transform parent, Vector2 size, Color glowColor, string label = null)
    {
        GameObject root = CreateUIObject(name, parent);
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        // 글로우 배경
        GameObject glowObj = CreateUIObject("Glow", root.transform);
        RectTransform glowRt = glowObj.GetComponent<RectTransform>();
        glowRt.anchorMin = Vector2.zero;
        glowRt.anchorMax = Vector2.one;
        glowRt.sizeDelta = new Vector2(12, 12); // 글로우 여유분
        glowRt.anchoredPosition = Vector2.zero;
        Image glowImg = glowObj.AddComponent<Image>();
        glowImg.sprite = CreateGlowCircleSprite(128, new Color(glowColor.r, glowColor.g, glowColor.b, 0.3f));
        glowImg.raycastTarget = false;

        // 메인 버튼 배경
        Image bgImg = root.AddComponent<Image>();
        bgImg.sprite = CreateCircleSprite(128,
            new Color(PanelBg.r, PanelBg.g, PanelBg.b, 0.9f),
            glowColor, 3);

        Button btn = root.AddComponent<Button>();
        btn.targetGraphic = bgImg;

        // 아이콘 영역
        GameObject iconObj = CreateUIObject("Icon", root.transform);
        RectTransform iconRt = iconObj.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.2f, 0.2f);
        iconRt.anchorMax = new Vector2(0.8f, 0.8f);
        iconRt.offsetMin = Vector2.zero;
        iconRt.offsetMax = Vector2.zero;
        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.raycastTarget = false;

        return (root, btn, iconImg);
    }

    /// <summary>
    /// 코너 장식 라인 (패널 장식용)
    /// </summary>
    public static void AddCornerDecorations(Transform parent, Color lineColor, float length = 20f, float thickness = 1.5f)
    {
        // 좌상단 L 모양
        CreateDecoLine(parent, "CornerTL_H", new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(4, -4), new Vector2(length, thickness), lineColor);
        CreateDecoLine(parent, "CornerTL_V", new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(4, -4), new Vector2(thickness, length), lineColor);

        // 우상단
        CreateDecoLine(parent, "CornerTR_H", new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-4 - length, -4), new Vector2(length, thickness), lineColor);
        CreateDecoLine(parent, "CornerTR_V", new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-4 - thickness, -4), new Vector2(thickness, length), lineColor);

        // 좌하단
        CreateDecoLine(parent, "CornerBL_H", new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(4, 4), new Vector2(length, thickness), lineColor);
        CreateDecoLine(parent, "CornerBL_V", new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(4, 4), new Vector2(thickness, length), lineColor);

        // 우하단
        CreateDecoLine(parent, "CornerBR_H", new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(-4 - length, 4), new Vector2(length, thickness), lineColor);
        CreateDecoLine(parent, "CornerBR_V", new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(-4 - thickness, 4), new Vector2(thickness, length), lineColor);
    }

    private static void CreateDecoLine(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Color color)
    {
        GameObject line = CreateUIObject(name, parent);
        RectTransform rt = line.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = line.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }
}
