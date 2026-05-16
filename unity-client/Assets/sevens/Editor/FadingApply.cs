using UnityEngine;
using UnityEditor;
using WorldSpaceTransitions;

/// <summary>
/// 건물 카메라 페이드 시스템 설정 도구
/// CyberpunkNeon 셰이더의 디더 페이드 + BuildingFadeManager(per-object) 사용
///
/// 이전 방식(글로벌 FADE_SPHERE)과 다르게:
/// - MaterialPropertyBlock으로 개별 건물만 페이드
/// - 셰이더 키워드 오염 없음
/// - Opaque 큐 유지, 소팅 이슈 없음
/// </summary>
public class FadingApply : EditorWindow
{
    [MenuItem("Tools/건물 페이드 설정")]
    public static void ShowWindow()
    {
        GetWindow<FadingApply>("건물 페이드 설정");
    }

    private void OnGUI()
    {
        GUILayout.Label("건물 카메라 페이드 시스템", EditorStyles.boldLabel);
        GUILayout.Space(5);

        EditorGUILayout.HelpBox(
            "카메라와 플레이어 사이의 건물을 디더 패턴으로 반투명 처리합니다.\n" +
            "CyberpunkNeon 셰이더의 _FadeAlpha를 개별 Renderer마다 조절합니다.\n" +
            "글로벌 키워드를 사용하지 않아 안전합니다.",
            MessageType.Info);

        GUILayout.Space(10);

        // Fading_ 머티리얼 복원 버튼
        GUI.backgroundColor = new Color(1f, 0.6f, 0.3f);
        if (GUILayout.Button("Fading_ 머티리얼 → CyberpunkNeon 복원", GUILayout.Height(35)))
        {
            RestoreCyberpunkNeonMaterials();
        }

        GUILayout.Space(5);

        // 핑크(깨진 셰이더) 머티리얼 전체 복구
        GUI.backgroundColor = new Color(1f, 0.3f, 0.7f);
        if (GUILayout.Button("핑크(깨진 셰이더) 머티리얼 전체 복구", GUILayout.Height(35)))
        {
            FixBrokenShaderMaterials();
        }

        GUILayout.Space(10);

        // BuildingFadeManager 생성
        GUI.backgroundColor = new Color(0.4f, 1f, 0.6f);
        if (GUILayout.Button("BuildingFadeManager 씬에 추가", GUILayout.Height(35)))
        {
            SetupBuildingFadeManagerObj();
        }

        GUILayout.Space(15);

        // 글로벌 키워드 정리
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("글로벌 셰이더 키워드 리셋 (문제 해결용)", GUILayout.Height(30)))
        {
            Shader.DisableKeyword("FADE_SPHERE");
            Shader.DisableKeyword("FADE_PLANE");
            Shader.DisableKeyword("CLIP_SPHERE");
            Shader.DisableKeyword("CLIP_PLANE");
            Shader.SetGlobalFloat("_Radius", 0f);
            Shader.SetGlobalFloat("_spread", 0f);
            Debug.Log("[FadingApply] 글로벌 셰이더 키워드 리셋 완료!");
            EditorUtility.DisplayDialog("완료", "글로벌 셰이더 키워드가 리셋되었습니다.", "확인");
        }

        GUI.backgroundColor = Color.white;
    }

    private void RestoreCyberpunkNeonMaterials()
    {
        Shader cyberpunkShader = Shader.Find("Sevens/CyberpunkNeon");
        if (cyberpunkShader == null)
        {
            Debug.LogError("[FadingApply] Sevens/CyberpunkNeon 셰이더를 찾을 수 없습니다!");
            return;
        }

        GameObject terrain = GameObject.Find("Terrain Remaked");
        if (terrain == null)
        {
            Debug.LogWarning("[FadingApply] Terrain Remaked를 찾을 수 없습니다.");
            return;
        }

        int restoredCount = 0;
        Renderer[] renderers = terrain.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;

            Material[] mats = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat == null) continue;

                if (mat.name.StartsWith("Fading_"))
                {
                    string origName = mat.name.Replace("Fading_", "");
                    string origPath = $"Assets/sevens/Materials/{origName}.mat";
                    Material origMat = AssetDatabase.LoadAssetAtPath<Material>(origPath);

                    if (origMat != null)
                    {
                        mats[i] = origMat;
                        changed = true;
                        Debug.Log($"[FadingApply] 복원: {mat.name} → {origMat.name}");
                    }
                    else
                    {
                        mat.shader = cyberpunkShader;
                        EditorUtility.SetDirty(mat);
                        Debug.Log($"[FadingApply] 셰이더 변경: {mat.name} → CyberpunkNeon");
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(renderer.gameObject);
                restoredCount++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FadingApply] {restoredCount}개 건물 머티리얼 복원 완료!");
        EditorUtility.DisplayDialog("완료", $"{restoredCount}개 건물 머티리얼이 CyberpunkNeon으로 복원되었습니다.", "확인");
    }

    private void FixBrokenShaderMaterials()
    {
        Shader cyberpunkShader = Shader.Find("Sevens/CyberpunkNeon");
        if (cyberpunkShader == null)
        {
            Debug.LogError("[FadingApply] Sevens/CyberpunkNeon 셰이더를 찾을 수 없습니다!");
            return;
        }

        Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
        int fixedRenderers = 0;
        int fixedMaterials = 0;

        // 1) 씬의 모든 렌더러에서 깨진 머티리얼 찾기
        Renderer[] allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Renderer rend in allRenderers)
        {
            if (rend == null) continue;

            Material[] mats = rend.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat == null) continue;

                bool isBroken = (mat.shader == errorShader) ||
                                (mat.shader == null) ||
                                (mat.shader.name == "Hidden/InternalErrorShader");

                bool isFading = mat.name.StartsWith("Fading_");

                if (isBroken || isFading)
                {
                    // Fading_ 머티리얼이면 원본 찾기 시도
                    if (isFading)
                    {
                        string origName = mat.name.Replace("Fading_", "");
                        string origPath = $"Assets/sevens/Materials/{origName}.mat";
                        Material origMat = AssetDatabase.LoadAssetAtPath<Material>(origPath);
                        if (origMat != null)
                        {
                            mats[i] = origMat;
                            changed = true;
                            fixedMaterials++;
                            Debug.Log($"[Fix] 렌더러 '{rend.gameObject.name}': {mat.name} → {origMat.name}");
                            continue;
                        }
                    }

                    // 원본 못 찾거나 일반 깨진 머티리얼이면 셰이더만 교체
                    mat.shader = cyberpunkShader;
                    EditorUtility.SetDirty(mat);
                    fixedMaterials++;
                    changed = true;
                    Debug.Log($"[Fix] 머티리얼 '{mat.name}' 셰이더 → CyberpunkNeon");
                }
            }

            if (changed)
            {
                rend.sharedMaterials = mats;
                EditorUtility.SetDirty(rend.gameObject);
                fixedRenderers++;
            }
        }

        // 2) Assets 폴더의 Fading_ 머티리얼 에셋도 수정
        string[] fadingGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/sevens/Materials/FadingBuildings" });
        foreach (string guid in fadingGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            if (mat.shader == errorShader || mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
            {
                mat.shader = cyberpunkShader;
                EditorUtility.SetDirty(mat);
                fixedMaterials++;
                Debug.Log($"[Fix] 에셋 머티리얼 '{mat.name}' 셰이더 복구");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FadingApply] 복구 완료! 렌더러 {fixedRenderers}개, 머티리얼 {fixedMaterials}개 수정");
        EditorUtility.DisplayDialog("복구 완료",
            $"깨진 머티리얼 복구:\n• 렌더러 {fixedRenderers}개 수정\n• 머티리얼 {fixedMaterials}개 수정\n\n핑크색이 사라졌는지 확인하세요.", "확인");
    }

    private void SetupBuildingFadeManagerObj()
    {
        BuildingFadeManager existing = Object.FindAnyObjectByType<BuildingFadeManager>();
        if (existing != null)
        {
            Debug.Log("[FadingApply] BuildingFadeManager가 이미 존재합니다.");
            Selection.activeGameObject = existing.gameObject;
            EditorUtility.DisplayDialog("알림", "BuildingFadeManager가 이미 씬에 있습니다.\nInspector에서 설정을 조정하세요.", "확인");
            return;
        }

        GameObject fmObj = new GameObject("BuildingFadeManager");
        BuildingFadeManager fm = fmObj.AddComponent<BuildingFadeManager>();
        fm.fadeSpeed = 8f;
        fm.maxFadeAmount = 0.85f;
        fm.enableCameraFade = true;
        fm.playerTag = "Player";

        Undo.RegisterCreatedObjectUndo(fmObj, "Create BuildingFadeManager");
        Selection.activeGameObject = fmObj;

        Debug.Log("[FadingApply] BuildingFadeManager 생성 완료! Play 모드에서 확인하세요.");
        EditorUtility.DisplayDialog("완료", "BuildingFadeManager가 생성되었습니다.\nPlay 모드에서 카메라 뒤 건물이 자동으로 페이드됩니다.", "확인");
    }
}
