using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Standard 셰이더(Built-in RP)를 쓰는 GLB 내장 머티리얼을
/// CyberpunkNeon 셰이더 머티리얼로 교체
/// GLB 머티리얼은 직접 수정 불가 → 새 머티리얼 생성 후 렌더러에 할당
/// </summary>
public class StandardToCyberpunk : EditorWindow
{
    [MenuItem("Tools/Standard → CyberpunkNeon 변환")]
    public static void ShowWindow()
    {
        GetWindow<StandardToCyberpunk>("Standard → CyberpunkNeon");
    }

    private void OnGUI()
    {
        GUILayout.Label("Standard 셰이더 → CyberpunkNeon 변환", EditorStyles.boldLabel);
        GUILayout.Space(5);

        EditorGUILayout.HelpBox(
            "GLB에 내장된 Standard 셰이더 머티리얼을\n" +
            "URP 호환 CyberpunkNeon 머티리얼로 교체합니다.\n" +
            "원본 텍스처와 색상을 최대한 유지합니다.",
            MessageType.Info);

        GUILayout.Space(10);

        GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
        if (GUILayout.Button("변환 실행!", GUILayout.Height(45)))
        {
            ConvertAll();
        }
        GUI.backgroundColor = Color.white;
    }

    private void ConvertAll()
    {
        Shader standardShader = Shader.Find("Standard");
        Shader cyberpunkShader = Shader.Find("Sevens/CyberpunkNeon");

        if (cyberpunkShader == null)
        {
            Debug.LogError("Sevens/CyberpunkNeon 셰이더를 찾을 수 없습니다!");
            return;
        }

        // 출력 폴더 확보
        string outputFolder = "Assets/sevens/Materials/Converted";
        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            AssetDatabase.CreateFolder("Assets/sevens/Materials", "Converted");
        }

        // 이미 변환된 머티리얼 캐시 (같은 원본 → 같은 변환 머티리얼 재사용)
        Dictionary<string, Material> convertedCache = new Dictionary<string, Material>();

        // 기존 변환 머티리얼 로드
        string[] existingGuids = AssetDatabase.FindAssets("t:Material", new[] { outputFolder });
        foreach (string guid in existingGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && existing.shader == cyberpunkShader)
            {
                convertedCache[existing.name] = existing;
            }
        }

        // Terrain Remaked 하위 모든 렌더러 처리
        GameObject terrain = GameObject.Find("Terrain Remaked");
        if (terrain == null)
        {
            Debug.LogError("Terrain Remaked를 찾을 수 없습니다!");
            return;
        }

        Renderer[] renderers = terrain.GetComponentsInChildren<Renderer>(true);
        int convertedCount = 0;
        int rendererCount = 0;

        foreach (Renderer rend in renderers)
        {
            if (rend == null) continue;

            Material[] mats = rend.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat == null) continue;

                // Standard 셰이더인 경우만 처리
                if (mat.shader != standardShader) continue;

                string safeName = SanitizeName(mat.name);

                // 이미 변환된 것이 있으면 재사용
                if (convertedCache.TryGetValue(safeName, out Material cached))
                {
                    mats[i] = cached;
                    changed = true;
                    continue;
                }

                // 새 CyberpunkNeon 머티리얼 생성
                Material newMat = new Material(cyberpunkShader);
                newMat.name = safeName;

                // 원본에서 텍스처, 색상 가져오기
                TransferProperties(mat, newMat);

                // 에셋으로 저장
                string savePath = $"{outputFolder}/{safeName}.mat";
                // 중복 방지
                if (AssetDatabase.LoadAssetAtPath<Material>(savePath) != null)
                {
                    savePath = $"{outputFolder}/{safeName}_{System.Guid.NewGuid().ToString().Substring(0, 4)}.mat";
                }

                AssetDatabase.CreateAsset(newMat, savePath);
                convertedCache[safeName] = newMat;

                mats[i] = newMat;
                changed = true;
                convertedCount++;

                Debug.Log($"[변환] '{mat.name}' → '{savePath}'");
            }

            if (changed)
            {
                rend.sharedMaterials = mats;
                EditorUtility.SetDirty(rend.gameObject);
                rendererCount++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[완료] {convertedCount}개 머티리얼 변환, {rendererCount}개 렌더러 업데이트");
        EditorUtility.DisplayDialog("변환 완료",
            $"Standard → CyberpunkNeon 변환:\n" +
            $"• {convertedCount}개 새 머티리얼 생성\n" +
            $"• {rendererCount}개 렌더러 업데이트\n\n" +
            $"저장 위치: {outputFolder}", "확인");
    }

    /// <summary>
    /// Standard 머티리얼의 속성을 CyberpunkNeon으로 전달
    /// </summary>
    private void TransferProperties(Material src, Material dst)
    {
        // 메인 텍스처
        if (src.HasProperty("_MainTex"))
        {
            Texture tex = src.GetTexture("_MainTex");
            if (tex != null)
                dst.SetTexture("_BaseMap", tex);

            // 텍스처 타일링/오프셋
            dst.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex"));
            dst.SetTextureOffset("_BaseMap", src.GetTextureOffset("_MainTex"));
        }

        // 베이스 색상
        Color baseColor = Color.white;
        if (src.HasProperty("_Color"))
        {
            baseColor = src.GetColor("_Color");
        }

        // 너무 어두우면 밝게 조정 (CyberpunkNeon 기본이 매우 어두움)
        float brightness = Mathf.Max(baseColor.r, baseColor.g, baseColor.b);
        if (brightness < 0.15f)
        {
            // 거의 검정이면 밝은 회색으로
            baseColor = new Color(
                Mathf.Lerp(baseColor.r, 0.5f, 0.8f),
                Mathf.Lerp(baseColor.g, 0.5f, 0.8f),
                Mathf.Lerp(baseColor.b, 0.55f, 0.8f),
                1f
            );
        }
        dst.SetColor("_BaseColor", baseColor);

        // 네온 효과 약하게 (건물이니까)
        dst.SetFloat("_NeonIntensity", 0.3f);
        dst.SetFloat("_FresnelIntensity", 0.3f);
        dst.SetFloat("_FresnelPower", 3f);
        dst.SetColor("_NeonColor1", new Color(0.5f, 0.7f, 1f, 1f));
        dst.SetColor("_NeonColor2", new Color(1f, 0.6f, 0.5f, 1f));
        dst.SetFloat("_WarmIntensity", 0.3f);
        dst.SetFloat("_PulseSpeed", 0.3f);
        dst.SetFloat("_PulseMin", 0.7f);
        dst.SetColor("_ShadowColor", new Color(0.35f, 0.3f, 0.4f, 1f));
        dst.SetFloat("_ShadowThreshold", 0.35f);
        dst.SetFloat("_FadeAmount", 0f);
    }

    private string SanitizeName(string name)
    {
        // 파일명에 쓸 수 없는 문자 제거
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Replace(" ", "_").Replace(".", "_");
    }
}
