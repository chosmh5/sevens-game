using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.IO;

// Force recompile
/// <summary>
/// 사이버펑크 + 힐링 비주얼 원클릭 세팅
/// - 포스트프로세싱 볼륨 (Bloom, Color Grading, Chromatic Aberration, Vignette)
/// - 네온 조명 (파랑/핑크 포인트 라이트)
/// - 사이버펑크 네온 머티리얼
/// - 안개 설정
/// </summary>
public class CyberpunkSetup : EditorWindow
{
    private bool setupPostProcessing = true;
    private bool setupNeonLights = true;
    private bool setupFog = true;
    private bool createNeonMaterials = true;
    private bool adjustCamera = true;

    // 색상 커스텀
    private Color neonBlue = new Color(0.2f, 0.5f, 1.0f);
    private Color neonPink = new Color(1.0f, 0.2f, 0.5f);
    private Color warmAmbient = new Color(1.0f, 0.85f, 0.6f);
    private float bloomIntensity = 0.8f;
    private float bloomThreshold = 1.2f;

    [MenuItem("Tools/사이버펑크 비주얼 세팅")]
    public static void ShowWindow()
    {
        GetWindow<CyberpunkSetup>("사이버펑크 세팅");
    }

    private void OnGUI()
    {
        GUILayout.Label("사이버펑크 + 힐링 비주얼 세팅", EditorStyles.boldLabel);
        GUILayout.Space(5);
        EditorGUILayout.HelpBox(
            "청색+적색 네온 글리치 + 따뜻한 힐링 색감\n" +
            "Bloom, Color Grading, 네온 조명을 한번에 세팅합니다.",
            MessageType.Info);

        GUILayout.Space(10);

        // 옵션
        EditorGUILayout.LabelField("적용 항목", EditorStyles.boldLabel);
        setupPostProcessing = EditorGUILayout.Toggle("포스트프로세싱 볼륨", setupPostProcessing);
        setupNeonLights = EditorGUILayout.Toggle("네온 조명 (파랑/핑크)", setupNeonLights);
        setupFog = EditorGUILayout.Toggle("사이버펑크 안개", setupFog);
        createNeonMaterials = EditorGUILayout.Toggle("네온 머티리얼 생성", createNeonMaterials);
        adjustCamera = EditorGUILayout.Toggle("카메라 포스트프로세싱 활성화", adjustCamera);

        GUILayout.Space(10);

        // 색상 설정
        EditorGUILayout.LabelField("색상 설정", EditorStyles.boldLabel);
        neonBlue = EditorGUILayout.ColorField("네온 블루", neonBlue);
        neonPink = EditorGUILayout.ColorField("네온 핑크", neonPink);
        warmAmbient = EditorGUILayout.ColorField("따뜻한 앰비언트", warmAmbient);
        bloomIntensity = EditorGUILayout.Slider("Bloom 강도", bloomIntensity, 0, 5);
        bloomThreshold = EditorGUILayout.Slider("Bloom 임계값", bloomThreshold, 0, 2);

        GUILayout.Space(15);

        GUI.backgroundColor = new Color(0.3f, 0.6f, 1.0f);
        if (GUILayout.Button("사이버펑크 비주얼 적용!", GUILayout.Height(45)))
        {
            ApplyCyberpunkVisuals();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);
        if (GUILayout.Button("네온 머티리얼만 생성", GUILayout.Height(30)))
        {
            CreateNeonMaterials();
        }
        GUI.backgroundColor = Color.white;
    }

    private void ApplyCyberpunkVisuals()
    {
        int count = 0;

        if (setupPostProcessing) { SetupPostProcessingVolume(); count++; }
        if (setupNeonLights) { SetupNeonLights(); count++; }
        if (setupFog) { SetupFog(); count++; }
        if (createNeonMaterials) { CreateNeonMaterials(); count++; }
        if (adjustCamera) { EnableCameraPostProcessing(); count++; }

        // 씬 라이팅 조정
        SetupAmbientLighting();

        Debug.Log($"[CyberpunkSetup] {count}개 항목 적용 완료!");
        EditorUtility.DisplayDialog("완료!", "사이버펑크 비주얼이 적용되었습니다.\nGame 뷰에서 확인해보세요!", "확인");
    }

    private void SetupPostProcessingVolume()
    {
        // 기존 볼륨 찾기 또는 생성
        Volume volume = Object.FindAnyObjectByType<Volume>();
        GameObject volumeObj;

        if (volume == null)
        {
            volumeObj = new GameObject("PostProcess Volume");
            volume = volumeObj.AddComponent<Volume>();
            volume.isGlobal = true;
            Undo.RegisterCreatedObjectUndo(volumeObj, "Create PostProcess Volume");
        }
        else
        {
            volumeObj = volume.gameObject;
        }

        // 볼륨 프로파일 생성
        string profileDir = "Assets/sevens/Settings";
        if (!AssetDatabase.IsValidFolder(profileDir))
        {
            AssetDatabase.CreateFolder("Assets/sevens", "Settings");
        }

        string profilePath = $"{profileDir}/CyberpunkPostProcess.asset";
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);

        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }
        else
        {
            // 기존 프로파일 초기화
            profile.components.Clear();
        }

        // === Bloom ===
        var bloom = profile.Add<Bloom>();
        bloom.active = true;
        bloom.threshold.Override(bloomThreshold);
        bloom.intensity.Override(bloomIntensity);
        bloom.scatter.Override(0.7f);
        bloom.tint.Override(new Color(0.8f, 0.85f, 1.0f)); // 약간 푸른 블룸

        // === Color Adjustments (Color Grading) ===
        var colorAdj = profile.Add<ColorAdjustments>();
        colorAdj.active = true;
        colorAdj.postExposure.Override(0.3f);
        colorAdj.contrast.Override(15f);
        colorAdj.saturation.Override(10f);
        colorAdj.colorFilter.Override(new Color(0.95f, 0.92f, 0.98f)); // 약간 보라+따뜻

        // === Lift Gamma Gain (세밀한 색감) ===
        var lgg = profile.Add<LiftGammaGain>();
        lgg.active = true;
        // 그림자에 보라 (사이버펑크)
        lgg.lift.Override(new Vector4(0.95f, 0.9f, 1.05f, 0f));
        // 중간톤에 따뜻함 (힐링)
        lgg.gamma.Override(new Vector4(1.02f, 1.0f, 0.98f, 0f));
        // 하이라이트에 네온 블루
        lgg.gain.Override(new Vector4(0.95f, 0.98f, 1.08f, 0f));

        // === Chromatic Aberration ===
        var chromatic = profile.Add<ChromaticAberration>();
        chromatic.active = true;
        chromatic.intensity.Override(0.15f);

        // === Vignette ===
        var vignette = profile.Add<Vignette>();
        vignette.active = true;
        vignette.intensity.Override(0.3f);
        vignette.color.Override(new Color(0.1f, 0.05f, 0.15f)); // 보라빛 비네트

        // === Tonemapping ===
        var tonemap = profile.Add<Tonemapping>();
        tonemap.active = true;
        tonemap.mode.Override(TonemappingMode.ACES);

        // 프로파일 저장
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        volume.profile = profile;
        EditorUtility.SetDirty(volume);

        Debug.Log("[CyberpunkSetup] 포스트프로세싱 볼륨 세팅 완료!");
    }

    private void SetupNeonLights()
    {
        // 네온 조명 부모
        GameObject lightsParent = GameObject.Find("NeonLights");
        if (lightsParent == null)
        {
            lightsParent = new GameObject("NeonLights");
            Undo.RegisterCreatedObjectUndo(lightsParent, "Create Neon Lights");
        }

        // 기존 자식 라이트 제거
        for (int i = lightsParent.transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(lightsParent.transform.GetChild(i).gameObject);

        // 파란 네온 라이트들
        CreateNeonLight(lightsParent.transform, "NeonBlue_1", new Vector3(-8, 6, -5), neonBlue * 2, 20, 3);
        CreateNeonLight(lightsParent.transform, "NeonBlue_2", new Vector3(12, 4, 8), neonBlue * 1.5f, 18, 2.5f);
        CreateNeonLight(lightsParent.transform, "NeonBlue_3", new Vector3(0, 8, -15), neonBlue * 1.8f, 25, 2);

        // 핑크 네온 라이트들
        CreateNeonLight(lightsParent.transform, "NeonPink_1", new Vector3(6, 5, -3), neonPink * 2, 15, 2.5f);
        CreateNeonLight(lightsParent.transform, "NeonPink_2", new Vector3(-10, 3, 10), neonPink * 1.5f, 20, 2);

        // 따뜻한 포인트 라이트 (힐링)
        CreateNeonLight(lightsParent.transform, "WarmLight_1", new Vector3(0, 3, 0), warmAmbient * 1.5f, 30, 1.5f);
        CreateNeonLight(lightsParent.transform, "WarmLight_2", new Vector3(-5, 2, 5), warmAmbient, 20, 1.2f);

        Debug.Log("[CyberpunkSetup] 네온 조명 7개 설치 완료!");
    }

    private void CreateNeonLight(Transform parent, string name, Vector3 pos, Color color, float range, float intensity)
    {
        GameObject lightObj = new GameObject(name);
        lightObj.transform.SetParent(parent);
        lightObj.transform.position = pos;

        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None; // 모바일 성능
    }

    private void SetupFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.015f;
        // 사이버펑크 보라+파랑 안개
        RenderSettings.fogColor = new Color(0.06f, 0.04f, 0.12f);

        Debug.Log("[CyberpunkSetup] 사이버펑크 안개 설정 완료!");
    }

    private void CreateNeonMaterials()
    {
        string matDir = "Assets/sevens/Materials";
        if (!AssetDatabase.IsValidFolder(matDir))
        {
            AssetDatabase.CreateFolder("Assets/sevens", "Materials");
        }

        Shader neonShader = Shader.Find("Sevens/CyberpunkNeon");
        if (neonShader == null)
        {
            Debug.LogError("[CyberpunkSetup] Sevens/CyberpunkNeon 셰이더를 찾을 수 없습니다!");
            return;
        }

        // 건물용 네온 머티리얼
        CreateMaterial(matDir, "CyberpunkBuilding_Blue", neonShader,
            new Color(0.03f, 0.03f, 0.08f), neonBlue, new Color(0.5f, 0.2f, 0.8f), 2.0f);

        CreateMaterial(matDir, "CyberpunkBuilding_Pink", neonShader,
            new Color(0.05f, 0.02f, 0.05f), neonPink, new Color(0.3f, 0.5f, 1.0f), 1.8f);

        CreateMaterial(matDir, "CyberpunkBuilding_Warm", neonShader,
            new Color(0.08f, 0.06f, 0.04f), warmAmbient, neonBlue, 1.0f);

        CreateMaterial(matDir, "CyberpunkGround", neonShader,
            new Color(0.04f, 0.04f, 0.06f), new Color(0.1f, 0.2f, 0.4f), neonPink, 0.5f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[CyberpunkSetup] 네온 머티리얼 4개 생성 완료!");
    }

    private void CreateMaterial(string dir, string name, Shader shader,
        Color baseColor, Color neon1, Color neon2, float intensity)
    {
        string path = $"{dir}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = shader;
        }

        mat.SetColor("_BaseColor", baseColor);
        mat.SetColor("_NeonColor1", neon1);
        mat.SetColor("_NeonColor2", neon2);
        mat.SetFloat("_NeonIntensity", intensity);
        mat.SetFloat("_FresnelPower", 2.5f);
        mat.SetFloat("_FresnelIntensity", 1.5f);
        mat.SetColor("_WarmColor", warmAmbient);
        mat.SetFloat("_WarmIntensity", 0.15f);
        mat.SetFloat("_PulseSpeed", 1.0f);

        EditorUtility.SetDirty(mat);
    }

    private void EnableCameraPostProcessing()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            if (cams.Length > 0) cam = cams[0];
        }

        if (cam == null)
        {
            Debug.LogWarning("[CyberpunkSetup] 카메라를 찾을 수 없습니다.");
            return;
        }

        var urpCamData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (urpCamData == null)
            urpCamData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();

        urpCamData.renderPostProcessing = true;
        // 안티앨리어싱
        urpCamData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        EditorUtility.SetDirty(cam);
        EditorUtility.SetDirty(urpCamData);

        Debug.Log("[CyberpunkSetup] 카메라 포스트프로세싱 활성화 완료!");
    }

    private void SetupAmbientLighting()
    {
        // 앰비언트 라이팅 - 사이버펑크 어두운 보라 + 힐링 따뜻함
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.08f, 0.06f, 0.15f);      // 하늘: 보라
        RenderSettings.ambientEquatorColor = new Color(0.12f, 0.08f, 0.18f);   // 수평: 짙은 보라
        RenderSettings.ambientGroundColor = new Color(0.15f, 0.1f, 0.08f);     // 바닥: 따뜻한 갈색

        // Directional Light 조정
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light l in lights)
        {
            if (l.type == LightType.Directional)
            {
                l.color = new Color(0.9f, 0.85f, 0.95f); // 약간 보라빛 달빛
                l.intensity = 0.6f; // 낮은 강도 (야간 느낌)
                l.transform.rotation = Quaternion.Euler(35, -30, 0);
                EditorUtility.SetDirty(l);
                break;
            }
        }

        Debug.Log("[CyberpunkSetup] 앰비언트 라이팅 설정 완료!");
    }
}
