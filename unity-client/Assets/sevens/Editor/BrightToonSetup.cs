using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 밝은 아침 톤 그래픽 설정 도구
/// 사이버펑크 네온 → 깨끗하고 밝은 카툰 스타일로 전환
/// - 밝은 하늘 + 부드러운 태양광
/// - Bloom/글리치 비활성화 또는 최소화
/// - 안개를 밝은 색으로
/// - CyberpunkNeon 셰이더의 네온 효과 약화
/// - 네온 조명 제거
/// </summary>
public class BrightToonSetup : EditorWindow
{
    private bool adjustPostProcessing = true;
    private bool adjustLighting = true;
    private bool adjustFog = true;
    private bool removeNeonLights = true;
    private bool adjustMaterials = true;
    private bool disableGlitch = true;
    private bool applyRetrowaveSkybox = true;
    private int skyboxPreset = 0; // 0=Orange, 1=VHS, 2=Vapor

    [MenuItem("Tools/밝은 아침 스타일 적용")]
    public static void ShowWindow()
    {
        GetWindow<BrightToonSetup>("밝은 아침 스타일");
    }

    private void OnGUI()
    {
        GUILayout.Label("밝은 아침 툰 그래픽", EditorStyles.boldLabel);
        GUILayout.Space(5);

        EditorGUILayout.HelpBox(
            "사이버펑크 네온 스타일을 밝고 깨끗한 아침 카툰 스타일로 전환합니다.\n" +
            "네온 조명 제거, 밝은 하늘, 따뜻한 햇살, 깨끗한 포스트프로세싱.",
            MessageType.Info);

        GUILayout.Space(10);

        adjustPostProcessing = EditorGUILayout.Toggle("포스트프로세싱 (밝게)", adjustPostProcessing);
        adjustLighting = EditorGUILayout.Toggle("라이팅 (아침 햇살)", adjustLighting);
        adjustFog = EditorGUILayout.Toggle("안개 (밝은 안개)", adjustFog);
        removeNeonLights = EditorGUILayout.Toggle("네온 조명 제거", removeNeonLights);
        adjustMaterials = EditorGUILayout.Toggle("머티리얼 네온 약화", adjustMaterials);
        disableGlitch = EditorGUILayout.Toggle("글리치 효과 비활성화", disableGlitch);

        GUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        applyRetrowaveSkybox = EditorGUILayout.Toggle("RETROWAVE 스카이박스", applyRetrowaveSkybox);
        if (applyRetrowaveSkybox)
        {
            string[] presetNames = { "Orange (따뜻한 석양)", "VHS (보라 레트로)", "Vapor (네온 핑크)" };
            skyboxPreset = EditorGUILayout.Popup("스카이박스 프리셋", skyboxPreset, presetNames);
        }

        GUILayout.Space(15);

        GUI.backgroundColor = new Color(0.5f, 0.9f, 1f);
        if (GUILayout.Button("밝은 아침 스타일 적용!", GUILayout.Height(45)))
        {
            ApplyBrightToon();
        }
        GUI.backgroundColor = Color.white;
    }

    private void ApplyBrightToon()
    {
        if (adjustPostProcessing) SetupBrightPostProcessing();
        if (adjustLighting) SetupMorningLighting();
        if (adjustFog) SetupBrightFog();
        if (removeNeonLights) RemoveNeonLights();
        if (adjustMaterials) AdjustMaterialsForBright();
        if (disableGlitch) DisableGlitchEffect();
        if (applyRetrowaveSkybox) ApplyRetrowaveSkybox();

        EditorUtility.DisplayDialog("완료!",
            "밝은 아침 스타일이 적용되었습니다.\nGame 뷰에서 확인해보세요!", "확인");
    }

    private void SetupBrightPostProcessing()
    {
        Volume volume = Object.FindAnyObjectByType<Volume>();
        if (volume == null || volume.profile == null)
        {
            Debug.LogWarning("[BrightToon] PostProcess Volume을 찾을 수 없습니다.");
            return;
        }

        VolumeProfile profile = volume.profile;

        // === Bloom: 매우 약하게 (자연스러운 글로우만) ===
        if (profile.TryGet<Bloom>(out var bloom))
        {
            bloom.active = true;
            bloom.threshold.Override(2.0f);   // 높은 threshold로 글로우 최소화
            bloom.intensity.Override(0.2f);
            bloom.scatter.Override(0.3f);
            bloom.tint.Override(new Color(1f, 0.98f, 0.95f)); // 따뜻한 화이트
        }

        // === Color Adjustments: 밝고 따뜻하게 ===
        if (profile.TryGet<ColorAdjustments>(out var colorAdj))
        {
            colorAdj.postExposure.Override(0.15f);      // 살짝만 밝게 (과노출 방지)
            colorAdj.contrast.Override(15f);              // 약간의 대비
            colorAdj.saturation.Override(20f);            // 채도 올림 (카툰 느낌)
            colorAdj.colorFilter.Override(new Color(1f, 0.99f, 0.97f)); // 따뜻한 화이트
        }

        // === Vignette: 거의 없음 ===
        if (profile.TryGet<Vignette>(out var vignette))
        {
            vignette.active = true;
            vignette.intensity.Override(0.1f);
            vignette.color.Override(new Color(0.3f, 0.3f, 0.4f));
        }

        // === Lift Gamma Gain: 밝고 따뜻하게 ===
        if (profile.TryGet<LiftGammaGain>(out var lgg))
        {
            lgg.lift.Override(new Vector4(0f, 0f, 0f, 0.05f));     // 그림자 살짝 밝게
            lgg.gamma.Override(new Vector4(0.02f, 0.01f, 0f, 0f)); // 중간톤 약간 따뜻
            lgg.gain.Override(new Vector4(0f, 0f, 0f, 0f));        // 하이라이트 그대로
        }

        // === Chromatic Aberration: 없음 ===
        if (profile.TryGet<ChromaticAberration>(out var ca))
        {
            ca.active = false;
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[BrightToon] 포스트프로세싱 적용 완료 (밝은 아침)");
    }

    private void SetupMorningLighting()
    {
        // === Directional Light: 아침 햇살 ===
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light l in lights)
        {
            if (l.type == LightType.Directional)
            {
                l.intensity = 0.7f;  // 과노출 방지 (캐릭터 하얗게 뜨지 않게)
                l.color = new Color(1f, 0.96f, 0.88f); // 따뜻한 아침 햇살
                l.transform.rotation = Quaternion.Euler(35f, -30f, 0f); // 아침 각도

                // 그림자 부드럽게
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.4f;

                EditorUtility.SetDirty(l);
                Debug.Log("[BrightToon] Directional Light 아침 햇살 설정");
                break;
            }
        }

        // === 앰비언트 라이팅: 밝고 부드러운 하늘 ===
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.45f, 0.55f, 0.7f);     // 하늘색 (약간 억제)
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.6f, 0.55f);  // 자연스러운 중간
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.25f, 0.2f);   // 따뜻한 갈색

        // === Skybox 색상 ===
        RenderSettings.ambientIntensity = 0.9f;  // 앰비언트 과다 방지

        Debug.Log("[BrightToon] 라이팅 설정 완료 (아침)");
    }

    private void SetupBrightFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.008f;
        RenderSettings.fogColor = new Color(0.75f, 0.85f, 0.95f); // 밝은 하늘빛 안개

        Debug.Log("[BrightToon] 안개 설정 완료 (밝은 안개)");
    }

    private void RemoveNeonLights()
    {
        int removed = 0;

        // NeonLights 그룹 찾기
        GameObject neonLights = GameObject.Find("NeonLights");
        if (neonLights != null)
        {
            Undo.DestroyObjectImmediate(neonLights);
            removed++;
            Debug.Log("[BrightToon] NeonLights 그룹 삭제");
        }

        // WarmStreetLights 색상 변경 (제거 대신 밝은 색으로)
        GameObject warmLights = GameObject.Find("WarmStreetLights");
        if (warmLights != null)
        {
            Light[] streetLights = warmLights.GetComponentsInChildren<Light>();
            foreach (Light sl in streetLights)
            {
                sl.color = new Color(1f, 0.95f, 0.8f); // 따뜻한 백색
                sl.intensity = 0.5f;
                EditorUtility.SetDirty(sl);
            }
            Debug.Log($"[BrightToon] WarmStreetLights {streetLights.Length}개 밝은 색으로 변경");
        }

        // 나머지 네온 컬러 포인트 라이트 제거
        Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light l in allLights)
        {
            if (l.type == LightType.Point)
            {
                // 파랑/핑크 네온 계열 라이트 감지
                Color c = l.color;
                bool isNeon = (c.b > 0.7f && c.r < 0.4f) ||    // 파란 네온
                              (c.r > 0.7f && c.b > 0.3f && c.g < 0.3f); // 핑크 네온
                if (isNeon)
                {
                    Undo.DestroyObjectImmediate(l.gameObject);
                    removed++;
                }
            }
        }

        Debug.Log($"[BrightToon] {removed}개 네온 라이트 제거/변경 완료");
    }

    private void AdjustMaterialsForBright()
    {
        // CyberpunkNeon 셰이더를 사용하는 모든 머티리얼의 네온 효과 약화
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/sevens/Materials" });
        int adjusted = 0;

        Shader cyberpunkShader = Shader.Find("Sevens/CyberpunkNeon");
        if (cyberpunkShader == null) return;

        foreach (string guid in matGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader != cyberpunkShader) continue;

            // 네온 강도 크게 줄이기
            if (mat.HasProperty("_NeonIntensity"))
                mat.SetFloat("_NeonIntensity", 0.3f);  // 2.0 → 0.3

            // 프레넬 엣지 글로우 줄이기
            if (mat.HasProperty("_FresnelIntensity"))
                mat.SetFloat("_FresnelIntensity", 0.3f); // 1.5 → 0.3

            // 네온 색상을 부드럽게 변경
            if (mat.HasProperty("_NeonColor1"))
                mat.SetColor("_NeonColor1", new Color(0.6f, 0.8f, 1f, 1f)); // 부드러운 하늘색
            if (mat.HasProperty("_NeonColor2"))
                mat.SetColor("_NeonColor2", new Color(1f, 0.7f, 0.6f, 1f)); // 부드러운 살구색

            // 따뜻한 앰비언트 올리기
            if (mat.HasProperty("_WarmIntensity"))
                mat.SetFloat("_WarmIntensity", 0.3f);  // 0.15 → 0.3

            // 펄스 속도 줄이기 (차분하게)
            if (mat.HasProperty("_PulseSpeed"))
                mat.SetFloat("_PulseSpeed", 0.3f);  // 1.0 → 0.3

            // 그림자 색을 따뜻하게
            if (mat.HasProperty("_ShadowColor"))
                mat.SetColor("_ShadowColor", new Color(0.4f, 0.35f, 0.45f, 1f)); // 따뜻한 보라 그림자
            if (mat.HasProperty("_ShadowThreshold"))
                mat.SetFloat("_ShadowThreshold", 0.35f);

            // 베이스 색상 밝게
            if (mat.HasProperty("_BaseColor"))
            {
                Color baseColor = mat.GetColor("_BaseColor");
                // 어두운 베이스 색을 밝게 조정
                float brightness = Mathf.Max(baseColor.r, baseColor.g, baseColor.b);
                if (brightness < 0.3f)
                {
                    mat.SetColor("_BaseColor", new Color(
                        Mathf.Lerp(baseColor.r, 0.6f, 0.7f),
                        Mathf.Lerp(baseColor.g, 0.6f, 0.7f),
                        Mathf.Lerp(baseColor.b, 0.65f, 0.7f),
                        1f
                    ));
                }
            }

            EditorUtility.SetDirty(mat);
            adjusted++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[BrightToon] {adjusted}개 머티리얼 밝은 톤으로 조정");
    }

    private void ApplyRetrowaveSkybox()
    {
        string[] skyboxPaths = {
            "Assets/Suggo Creations/RETROWAVE SKIES Lite/Skybox Materials/Orange_Skybox.mat",
            "Assets/Suggo Creations/RETROWAVE SKIES Lite/Skybox Materials/VHS_Skybox.mat",
            "Assets/Suggo Creations/RETROWAVE SKIES Lite/Skybox Materials/Vapor_Skybox.mat"
        };

        string selectedPath = skyboxPaths[skyboxPreset];
        Material skyboxMat = AssetDatabase.LoadAssetAtPath<Material>(selectedPath);

        if (skyboxMat == null)
        {
            Debug.LogWarning($"[BrightToon] 스카이박스를 찾을 수 없습니다: {selectedPath}");
            return;
        }

        // 스카이박스 적용
        RenderSettings.skybox = skyboxMat;

        // 핵심: 앰비언트를 Trilight로 유지 → 스카이박스가 어두워도 씬은 밝게
        // (SetupMorningLighting에서 이미 Trilight로 설정했으므로 여기선 확인만)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

        // 스카이박스 프리셋별로 안개 색상을 살짝 맞춤
        switch (skyboxPreset)
        {
            case 0: // Orange
                RenderSettings.fogColor = new Color(0.85f, 0.7f, 0.55f);
                break;
            case 1: // VHS
                RenderSettings.fogColor = new Color(0.65f, 0.6f, 0.8f);
                break;
            case 2: // Vapor
                RenderSettings.fogColor = new Color(0.75f, 0.6f, 0.75f);
                break;
        }

        // Reflection 업데이트
        DynamicGI.UpdateEnvironment();

        string[] presetNames = { "Orange", "VHS", "Vapor" };
        Debug.Log($"[BrightToon] RETROWAVE SKIES '{presetNames[skyboxPreset]}' 스카이박스 적용 완료 (앰비언트 Trilight 유지)");
    }

    private void DisableGlitchEffect()
    {
        // URP Renderer Data에서 CyberpunkGlitchFeature 비활성화
        // Renderer Feature는 직접 접근이 어려우므로 머티리얼 파라미터로 제어
        var glitchFeature = FindGlitchFeature();
        if (glitchFeature != null)
        {
            glitchFeature.SetActive(false);
            Debug.Log("[BrightToon] CyberpunkGlitch Feature 비활성화");
        }
        else
        {
            Debug.Log("[BrightToon] CyberpunkGlitch Feature를 찾지 못했습니다. URP Renderer에서 수동으로 비활성화해주세요.");
        }
    }

    private ScriptableRendererFeature FindGlitchFeature()
    {
        // URP Asset에서 Renderer Data 찾기
        var urpAsset = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
        if (urpAsset == null) return null;

        // Renderer list 접근 (리플렉션 필요할 수 있음)
        var rendererDataList = urpAsset.GetType()
            .GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (rendererDataList == null) return null;

        var dataArray = rendererDataList.GetValue(urpAsset) as ScriptableRendererData[];
        if (dataArray == null) return null;

        foreach (var data in dataArray)
        {
            if (data == null) continue;
            foreach (var feature in data.rendererFeatures)
            {
                if (feature != null && feature.GetType().Name == "CyberpunkGlitchFeature")
                {
                    EditorUtility.SetDirty(data);
                    return feature;
                }
            }
        }

        return null;
    }
}
