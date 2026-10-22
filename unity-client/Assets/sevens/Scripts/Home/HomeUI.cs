using UnityEngine;

/// <summary>
/// 홈 화면 전용 텍스처. 패널·버튼·폰트·색은 카드게임과 같은 RetroTheme 를 쓴다.
/// </summary>
public static class HomeUI
{
    private static Sprite _glow;

    /// <summary>가운데가 밝고 바깥으로 사라지는 원형 후광(흰색, 알파만). Image.color 로 칠한다.</summary>
    public static Sprite GlowSprite()
    {
        if (_glow != null) return _glow;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        _glow = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return _glow;
    }
}
