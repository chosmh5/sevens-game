using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 씬의 모든 렌더러의 머티리얼/셰이더 상태를 진단
/// </summary>
public class MaterialDiagnostic : EditorWindow
{
    [MenuItem("Tools/머티리얼 진단")]
    public static void DiagnoseAll()
    {
        Debug.Log("=== 머티리얼 전수 진단 ===");

        Renderer[] allRenderers = Object.FindObjectsByType<Renderer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 셰이더별로 그룹핑
        Dictionary<string, List<string>> shaderGroups = new Dictionary<string, List<string>>();
        int nullMats = 0;

        foreach (Renderer rend in allRenderers)
        {
            if (rend == null) continue;

            foreach (Material mat in rend.sharedMaterials)
            {
                if (mat == null)
                {
                    nullMats++;
                    continue;
                }

                string shaderName = mat.shader != null ? mat.shader.name : "NULL_SHADER";
                string entry = $"  obj='{rend.gameObject.name}' mat='{mat.name}' path='{AssetDatabase.GetAssetPath(mat)}'";

                if (!shaderGroups.ContainsKey(shaderName))
                    shaderGroups[shaderName] = new List<string>();

                // 같은 머티리얼 중복 방지
                if (!shaderGroups[shaderName].Contains(entry))
                    shaderGroups[shaderName].Add(entry);
            }
        }

        // 결과 출력
        foreach (var kvp in shaderGroups)
        {
            Debug.Log($"\n[셰이더: {kvp.Key}] — {kvp.Value.Count}개 머티리얼");
            foreach (string entry in kvp.Value)
            {
                Debug.Log(entry);
            }
        }

        if (nullMats > 0)
            Debug.LogWarning($"NULL 머티리얼 슬롯: {nullMats}개");

        Debug.Log($"=== 진단 완료 === 셰이더 종류: {shaderGroups.Count}개 ===");
    }
}
