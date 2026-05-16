#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 메뉴: Tools/VRM/Force Opaque MToon Materials (Selection)
/// 메뉴: Tools/VRM/Force Opaque MToon Materials (Active Scene)
///
/// 선택한 GameObject 또는 활성 씬의 모든 MToon 머티리얼(BlendMode 0/1) 을 강제로
/// Opaque 모드로 변경한다. 셰이더는 그대로 유지(MToon) 하므로 토온 룩이 보존된다.
/// 변경된 머티리얼은 sharedMaterial 슬롯에 그대로 반영되고, 씬을 저장하면 머티리얼 에셋도
/// 갱신된다(공유 머티리얼이면 다른 씬에도 영향).
/// </summary>
public static class MToonURPFixerEditor
{
    private const string MToonShaderName = "VRM/MToon";

    [MenuItem("Tools/VRM/Force Opaque MToon Materials (Selection)")]
    public static void ForceOpaqueSelection()
    {
        if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("VRM Force Opaque", "GameObject 를 선택하세요.", "OK");
            return;
        }

        int total = 0;
        foreach (var go in Selection.gameObjects) total += ProcessHierarchy(go);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("VRM Force Opaque", $"{total} 개 머티리얼을 Opaque 로 변경했습니다.", "OK");
    }

    [MenuItem("Tools/VRM/Force Opaque MToon Materials (Active Scene)")]
    public static void ForceOpaqueActiveScene()
    {
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        int total = 0;
        foreach (var go in roots) total += ProcessHierarchy(go);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("VRM Force Opaque", $"씬 전체에서 {total} 개 머티리얼을 Opaque 로 변경했습니다.", "OK");
    }

    private static int ProcessHierarchy(GameObject root)
    {
        if (root == null) return 0;

        int changed = 0;
        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var renderer in renderers)
        {
            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat == null || mat.shader == null || mat.shader.name != MToonShaderName) continue;

                int blendMode = mat.HasProperty("_BlendMode") ? Mathf.RoundToInt(mat.GetFloat("_BlendMode")) : 0;
                if (blendMode == 2 || blendMode == 3) continue; // 투명 머티리얼은 건너뜀

                Undo.RecordObject(mat, "Force Opaque");

                if (mat.HasProperty("_BlendMode"))   mat.SetFloat("_BlendMode", 0f);
                if (mat.HasProperty("_SrcBlend"))    mat.SetFloat("_SrcBlend",  (float)BlendMode.One);
                if (mat.HasProperty("_DstBlend"))    mat.SetFloat("_DstBlend",  (float)BlendMode.Zero);
                if (mat.HasProperty("_ZWrite"))      mat.SetFloat("_ZWrite",    1f);
                if (mat.HasProperty("_AlphaToMask")) mat.SetFloat("_AlphaToMask", 0f);

                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

                if (mat.HasProperty("_Color"))
                {
                    var c = mat.GetColor("_Color");
                    c.a = 1f;
                    mat.SetColor("_Color", c);
                }

                mat.renderQueue = (int)RenderQueue.Geometry;
                mat.SetOverrideTag("RenderType", "Opaque");

                EditorUtility.SetDirty(mat);
                changed++;
            }
        }
        return changed;
    }
}
#endif
