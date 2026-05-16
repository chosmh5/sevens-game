using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using System.Reflection;

/// <summary>
/// CyberpunkGlitchFeature를 URP Renderer Data에 자동 등록하고
/// 글리치 머티리얼을 생성/할당하는 에디터 도구
/// </summary>
public class GlitchFeatureRegister : EditorWindow
{
    [MenuItem("Tools/글리치 이펙트 등록")]
    public static void ShowWindow()
    {
        GetWindow<GlitchFeatureRegister>("글리치 등록");
    }

    private void OnGUI()
    {
        GUILayout.Label("CyberpunkGlitch 풀스크린 이펙트 등록", EditorStyles.boldLabel);
        GUILayout.Space(5);
        EditorGUILayout.HelpBox(
            "URP Renderer Data에 CyberpunkGlitchFeature를 추가하고\n" +
            "글리치 머티리얼을 자동 생성/할당합니다.",
            MessageType.Info);
        GUILayout.Space(10);

        GUI.backgroundColor = new Color(0.5f, 0.3f, 1f);
        if (GUILayout.Button("글리치 이펙트 등록!", GUILayout.Height(40)))
        {
            RegisterGlitchFeature();
        }
        GUI.backgroundColor = Color.white;
    }

    private static void RegisterGlitchFeature()
    {
        // 1. 글리치 머티리얼 생성
        Material glitchMat = CreateGlitchMaterial();
        if (glitchMat == null)
        {
            Debug.LogError("[GlitchRegister] 글리치 머티리얼 생성 실패!");
            return;
        }

        // 2. URP Renderer Data 찾기
        UniversalRendererData rendererData = FindRendererData();
        if (rendererData == null)
        {
            Debug.LogError("[GlitchRegister] URP Renderer Data를 찾을 수 없습니다!");
            return;
        }

        // 3. 이미 등록된 CyberpunkGlitchFeature가 있는지 확인
        foreach (var feature in rendererData.rendererFeatures)
        {
            if (feature is CyberpunkGlitchFeature)
            {
                Debug.Log("[GlitchRegister] CyberpunkGlitchFeature가 이미 등록되어 있습니다. 머티리얼만 업데이트합니다.");
                var existingFeature = feature as CyberpunkGlitchFeature;
                existingFeature.settings.glitchMaterial = glitchMat;
                EditorUtility.SetDirty(existingFeature);
                EditorUtility.SetDirty(rendererData);
                AssetDatabase.SaveAssets();
                return;
            }
        }

        // 4. CyberpunkGlitchFeature 생성 및 추가
        var glitchFeature = ScriptableObject.CreateInstance<CyberpunkGlitchFeature>();
        glitchFeature.name = "CyberpunkGlitchFeature";
        glitchFeature.settings.glitchMaterial = glitchMat;

        // Renderer Data에 Feature 추가
        // Unity 내부 API 사용 — rendererFeatures 리스트에 직접 추가
        AddRendererFeature(rendererData, glitchFeature);

        Debug.Log("[GlitchRegister] CyberpunkGlitchFeature 등록 완료! Game 뷰에서 글리치 효과를 확인하세요.");
        EditorUtility.DisplayDialog("완료!", "CyberpunkGlitchFeature가 URP Renderer에 등록되었습니다!\nPlay 모드에서 확인해보세요.", "확인");
    }

    private static Material CreateGlitchMaterial()
    {
        string matDir = "Assets/sevens/Materials";
        if (!AssetDatabase.IsValidFolder(matDir))
            AssetDatabase.CreateFolder("Assets/sevens", "Materials");

        string matPath = $"{matDir}/CyberpunkGlitchMat.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        Shader glitchShader = Shader.Find("Sevens/CyberpunkGlitch");
        if (glitchShader == null)
        {
            Debug.LogError("[GlitchRegister] Sevens/CyberpunkGlitch 셰이더를 찾을 수 없습니다!");
            return null;
        }

        if (mat == null)
        {
            mat = new Material(glitchShader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        else
        {
            mat.shader = glitchShader;
        }

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Debug.Log("[GlitchRegister] 글리치 머티리얼 생성/업데이트 완료!");
        return mat;
    }

    private static UniversalRendererData FindRendererData()
    {
        // 방법 1: 현재 활성 파이프라인에서 찾기
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null)
        {
            // Quality Settings에서 찾기
            pipeline = UnityEngine.QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        }

        if (pipeline != null)
        {
            // Reflection으로 Renderer Data 가져오기
            var prop = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                var dataList = prop.GetValue(pipeline) as ScriptableRendererData[];
                if (dataList != null && dataList.Length > 0)
                {
                    return dataList[0] as UniversalRendererData;
                }
            }

            // 대안: defaultRendererIndex
            var indexProp = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (indexProp != null)
            {
                Debug.Log("[GlitchRegister] Renderer Data를 리플렉션으로 찾는 중...");
            }
        }

        // 방법 2: 프로젝트 전체에서 UniversalRendererData 검색
        string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Debug.Log($"[GlitchRegister] Renderer Data 발견: {path}");
            return AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
        }

        // 방법 3: ScriptableRendererData로도 검색
        guids = AssetDatabase.FindAssets("t:ScriptableRendererData");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data != null)
            {
                Debug.Log($"[GlitchRegister] Renderer Data 발견 (ScriptableRendererData): {path}");
                return data;
            }
        }

        Debug.LogError("[GlitchRegister] URP Renderer Data를 찾지 못했습니다. Project Settings > Graphics에서 URP가 설정되어 있는지 확인해주세요.");
        return null;
    }

    private static void AddRendererFeature(UniversalRendererData rendererData, ScriptableRendererFeature feature)
    {
        // Feature를 서브에셋으로 저장
        AssetDatabase.AddObjectToAsset(feature, rendererData);

        // rendererFeatures 리스트에 추가
        var featuresField = typeof(ScriptableRendererData).GetField("m_RendererFeatures",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (featuresField != null)
        {
            var featuresList = featuresField.GetValue(rendererData) as System.Collections.Generic.List<ScriptableRendererFeature>;
            if (featuresList == null)
            {
                featuresList = new System.Collections.Generic.List<ScriptableRendererFeature>();
            }
            featuresList.Add(feature);
            featuresField.SetValue(rendererData, featuresList);
        }

        // m_RendererFeatureMap 업데이트 (Unity가 내부적으로 사용)
        var mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (mapField != null)
        {
            var map = mapField.GetValue(rendererData) as System.Collections.Generic.List<long>;
            if (map == null)
            {
                map = new System.Collections.Generic.List<long>();
            }
            // 고유 ID 생성
            long id = System.DateTime.Now.Ticks;
            map.Add(id);
            mapField.SetValue(rendererData, map);
        }

        EditorUtility.SetDirty(rendererData);
        EditorUtility.SetDirty(feature);
        AssetDatabase.SaveAssets();
    }
}
