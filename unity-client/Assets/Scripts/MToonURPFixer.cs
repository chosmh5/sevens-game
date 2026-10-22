using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// URP 환경에서 VRM/MToon 캐릭터가 반투명하게 보이는 문제를 완화한다.
///
/// ▣ 제약
///   MToon 셰이더는 VRM 표준이라 절대 교체하지 않는다.
///   따라서 머티리얼 인스턴스의 속성/키워드만 in-place 로 수정.
///
/// ▣ 정책
///   - 얼굴 디테일 (Eye/Brow/Eyelash/Eyeline/Mouth): 원본 유지
///   - 원래 Cutout(BlendMode 1) 이던 머티리얼(헤어·옷 등): Cutout 유지(원래 _Cutoff), 아웃라인만 끔
///       → 알파로 잘려야 할 부분(헤어 안쪽 색, 옷단)이 Opaque 로 칠해져 비쳐 보이던 문제 방지
///   - 그 외 MToon 머티리얼:
///       · BlendMode = 0 (Opaque), ZWrite = 1, blend = One/Zero
///       · _ALPHATEST_ON / _ALPHABLEND_ON / _ALPHAPREMULTIPLY_ON 키워드 제거
///       · _Cutoff = 0, _AlphaToMask = 0
///       · _Color.a = 1
///       · Outline 비활성화 (MTOON_OUTLINE_* 키워드 + _OutlineWidth=0)
///       · RenderQueue = Geometry
/// </summary>
public static class MToonURPFixer
{
    private const string MToonShaderName = "VRM/MToon";

    public static bool Verbose = true;

    private static readonly string[] KeepTransparentKeywords =
    {
        "EyeIris", "EyeHighlight", "EyeWhite",
        "Brow", "Eyelash", "Eyeline",
        "Mouth", "Tongue",
    };

    public static int FixHierarchy(GameObject root)
    {
        if (root == null) return 0;

        int converted = 0, skipped = 0, nonMToon = 0;
        var report = new StringBuilder();
        if (Verbose) report.AppendLine($"[MToonURPFixer] === {root.name} (MToon 유지, 속성만 수정) ===");

        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);

        foreach (var renderer in renderers)
        {
            var sharedMats = renderer.sharedMaterials;
            var newMats    = new Material[sharedMats.Length];
            bool anyChanged = false;

            for (int i = 0; i < sharedMats.Length; i++)
            {
                var mat = sharedMats[i];
                if (mat == null) { newMats[i] = mat; continue; }

                string shaderName = mat.shader != null ? mat.shader.name : "(null)";
                int blendMode = mat.HasProperty("_BlendMode") ? Mathf.RoundToInt(mat.GetFloat("_BlendMode")) : -1;

                if (shaderName != MToonShaderName)
                {
                    newMats[i] = mat;
                    nonMToon++;
                    if (Verbose) report.AppendLine($"  · [non-MToon] {renderer.name}/{mat.name} ({shaderName})");
                    continue;
                }

                if (NameContainsAny(mat.name, KeepTransparentKeywords))
                {
                    newMats[i] = mat;
                    skipped++;
                    if (Verbose) report.AppendLine($"  · [skip 얼굴] {renderer.name}/{mat.name} BM={blendMode}");
                    continue;
                }

                // 머티리얼 인스턴스 생성(원본 에셋 보호) + MToon 셰이더 유지 + 속성만 변경
                bool cutout = blendMode == 1;
                var inst = new Material(mat) { name = mat.name + (cutout ? " (Cutout)" : " (Opaque)") };
                if (cutout)
                {
                    float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
                    ForceCutoff(inst, cutoff > 0.01f ? cutoff : 0.5f);
                }
                else ForceOpaque(inst);
                DisableOutline(inst);
                newMats[i] = inst;
                anyChanged = true;
                converted++;
                if (Verbose) report.AppendLine($"  · [{(cutout ? "Cutout" : "Opaque")}+NoOutline] {renderer.name}/{mat.name} BM={blendMode}→{(cutout ? 1 : 0)}");
            }

            if (anyChanged) renderer.materials = newMats;
        }

        if (Verbose)
        {
            report.AppendLine($"[MToonURPFixer] 요약: 변환 {converted}, 스킵 {skipped}, 비MToon {nonMToon}");
            Debug.Log(report.ToString());
        }
        else if (converted > 0)
        {
            Debug.Log($"[MToonURPFixer] {root.name}: {converted}개 변환");
        }

        return converted;
    }

    private static bool NameContainsAny(string name, string[] keywords)
    {
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < keywords.Length; i++)
            if (name.IndexOf(keywords[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static void ForceOpaque(Material m)
    {
        if (m.HasProperty("_BlendMode"))   m.SetFloat("_BlendMode", 0f);
        if (m.HasProperty("_SrcBlend"))    m.SetFloat("_SrcBlend",  (float)BlendMode.One);
        if (m.HasProperty("_DstBlend"))    m.SetFloat("_DstBlend",  (float)BlendMode.Zero);
        if (m.HasProperty("_ZWrite"))      m.SetFloat("_ZWrite",    1f);
        if (m.HasProperty("_AlphaToMask")) m.SetFloat("_AlphaToMask", 0f);
        if (m.HasProperty("_Cutoff"))      m.SetFloat("_Cutoff",    0f);

        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        if (m.HasProperty("_Color"))
        {
            var c = m.GetColor("_Color");
            c.a = 1f;
            m.SetColor("_Color", c);
        }

        m.renderQueue = (int)RenderQueue.Geometry;
        m.SetOverrideTag("RenderType", "Opaque");
    }

    /// <summary>
    /// FixHierarchy() 이후에 호출. 이미 Opaque 로 바뀐 MToon 머티리얼 중
    /// 얼굴 디테일을 제외한 모든 머티리얼을 Cutoff 로 재설정한다.
    /// transparentKeywords 에 해당하는 머티리얼은 Cutoff 대신 Transparent 로 설정한다.
    /// </summary>
    public static int ApplyMyRoomOverrides(GameObject root, string[] transparentKeywords = null, float cutoffThreshold = 0.5f)
    {
        if (root == null) return 0;

        int cutoffCount = 0, transparentCount = 0;
        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);

        foreach (var renderer in renderers)
        {
            var mats = renderer.materials; // 인스턴스 배열
            bool anyChanged = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;

                string shaderName = mat.shader != null ? mat.shader.name : "";
                if (shaderName != MToonShaderName) continue;

                // 얼굴 디테일은 건드리지 않음
                if (NameContainsAny(mat.name, KeepTransparentKeywords)) continue;

                if (transparentKeywords != null && NameContainsAny(mat.name, transparentKeywords))
                {
                    // Tops 등 지정된 머티리얼 → Transparent
                    ForceTransparent(mat);
                    anyChanged = true;
                    transparentCount++;
                    if (Verbose)
                        Debug.Log($"[MToonURPFixer] [Transparent] {renderer.name}/{mat.name}");
                }
                else
                {
                    // 나머지 body 머티리얼 → Cutoff
                    ForceCutoff(mat, cutoffThreshold);
                    anyChanged = true;
                    cutoffCount++;
                    if (Verbose)
                        Debug.Log($"[MToonURPFixer] [Cutoff] {renderer.name}/{mat.name} → Cutoff({cutoffThreshold})");
                }
            }

            if (anyChanged) renderer.materials = mats;
        }

        int total = cutoffCount + transparentCount;
        if (total > 0)
            Debug.Log($"[MToonURPFixer] MyRoom 오버라이드 요약: Cutoff {cutoffCount}개, Transparent {transparentCount}개");

        return total;
    }

    /// <summary>
    /// MToon 머티리얼을 Cutoff(TransparentCutout / AlphaTest) 모드로 설정한다.
    /// BlendMode=1, _ALPHATEST_ON 키워드 활성화, RenderQueue=AlphaTest.
    /// </summary>
    private static void ForceCutoff(Material m, float cutoff = 0.5f)
    {
        if (m.HasProperty("_BlendMode"))   m.SetFloat("_BlendMode", 1f); // 1 = Cutout
        if (m.HasProperty("_SrcBlend"))    m.SetFloat("_SrcBlend",  (float)BlendMode.One);
        if (m.HasProperty("_DstBlend"))    m.SetFloat("_DstBlend",  (float)BlendMode.Zero);
        if (m.HasProperty("_ZWrite"))      m.SetFloat("_ZWrite",    1f);
        if (m.HasProperty("_AlphaToMask")) m.SetFloat("_AlphaToMask", 1f);
        if (m.HasProperty("_Cutoff"))      m.SetFloat("_Cutoff",    cutoff);

        m.EnableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        if (m.HasProperty("_Color"))
        {
            var c = m.GetColor("_Color");
            c.a = 1f;
            m.SetColor("_Color", c);
        }

        m.renderQueue = (int)RenderQueue.AlphaTest; // 2450
        m.SetOverrideTag("RenderType", "TransparentCutout");
    }

    /// <summary>
    /// MToon 머티리얼을 Transparent 모드로 설정한다.
    /// BlendMode=2, _ALPHABLEND_ON 키워드 활성화, RenderQueue=Transparent.
    /// </summary>
    private static void ForceTransparent(Material m)
    {
        if (m.HasProperty("_BlendMode"))   m.SetFloat("_BlendMode", 2f); // 2 = Transparent
        if (m.HasProperty("_SrcBlend"))    m.SetFloat("_SrcBlend",  (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend"))    m.SetFloat("_DstBlend",  (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite"))      m.SetFloat("_ZWrite",    0f);
        if (m.HasProperty("_AlphaToMask")) m.SetFloat("_AlphaToMask", 0f);

        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        m.renderQueue = (int)RenderQueue.Transparent; // 3000
        m.SetOverrideTag("RenderType", "Transparent");
    }

    private static void DisableOutline(Material m)
    {
        if (m.HasProperty("_OutlineWidth"))       m.SetFloat("_OutlineWidth", 0f);
        if (m.HasProperty("_OutlineWidthMode"))   m.SetFloat("_OutlineWidthMode", 0f);
        if (m.HasProperty("_OutlineColorMode"))   m.SetFloat("_OutlineColorMode", 0f);
        if (m.HasProperty("_OutlineLightingMix")) m.SetFloat("_OutlineLightingMix", 0f);

        m.DisableKeyword("MTOON_OUTLINE_WIDTH_WORLD");
        m.DisableKeyword("MTOON_OUTLINE_WIDTH_SCREEN");
        m.DisableKeyword("MTOON_OUTLINE_COLOR_FIXED");
        m.DisableKeyword("MTOON_OUTLINE_COLOR_MIXED");
    }
}
