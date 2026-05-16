#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 메뉴: Tools/VRM/Dump Character Renderers (Selection)
///
/// 선택한 GameObject 와 모든 자식의 Renderer / Material / Shader 정보를 콘솔에 덤프한다.
/// MToon 머티리얼 변환이 캐릭터에 제대로 작동하지 않을 때, 정확히 어떤 메시가 어떤 셰이더를
/// 쓰는지 한 번에 확인하는 진단 도구.
/// </summary>
public static class MToonURPFixerDiagnose
{
    [MenuItem("Tools/VRM/Dump Character Renderers (Selection)")]
    public static void Dump()
    {
        var targets = Selection.gameObjects;
        if (targets == null || targets.Length == 0)
        {
            EditorUtility.DisplayDialog("Dump", "Hierarchy 에서 캐릭터 GameObject 를 선택하세요.", "OK");
            return;
        }

        var sb = new StringBuilder();
        foreach (var root in targets)
        {
            sb.AppendLine($"\n══════ {root.name} ══════");
            DumpHierarchy(root.transform, 0, sb);
        }
        Debug.Log(sb.ToString());
    }

    private static void DumpHierarchy(Transform t, int depth, StringBuilder sb)
    {
        string indent = new string(' ', depth * 2);
        var rends = t.GetComponents<Renderer>();
        if (rends.Length > 0)
        {
            foreach (var r in rends)
            {
                sb.AppendLine($"{indent}● {t.name} [{r.GetType().Name}, active={t.gameObject.activeInHierarchy}]");
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) { sb.AppendLine($"{indent}  └─ slot{i}: (null)"); continue; }
                    string shader = m.shader != null ? m.shader.name : "(null shader)";
                    int blendMode = m.HasProperty("_BlendMode") ? Mathf.RoundToInt(m.GetFloat("_BlendMode")) : -1;
                    float alpha = m.HasProperty("_Color") ? m.GetColor("_Color").a : -1f;
                    sb.AppendLine($"{indent}  └─ slot{i}: '{m.name}' shader={shader} BM={blendMode} a={alpha} RQ={m.renderQueue}");
                }
            }
        }
        else
        {
            sb.AppendLine($"{indent}○ {t.name} (no Renderer, active={t.gameObject.activeInHierarchy})");
        }
        foreach (Transform child in t)
            DumpHierarchy(child, depth + 1, sb);
    }
}
#endif
