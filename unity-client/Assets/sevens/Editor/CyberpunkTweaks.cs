using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 추가 비주얼 조정:
/// 1. 전체 밝기 낮추기 (포스트프로세싱 + 앰비언트)
/// 2. Plane 삭제
/// 3. 도시 내 오브젝트 충돌판정 추가
/// </summary>
public class CyberpunkTweaks : EditorWindow
{
    [MenuItem("Tools/사이버펑크 추가 조정")]
    public static void ShowWindow()
    {
        GetWindow<CyberpunkTweaks>("추가 조정");
    }

    private void OnGUI()
    {
        GUILayout.Label("사이버펑크 추가 비주얼 조정", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 1. 밝기 낮추기
        EditorGUILayout.LabelField("1. 전체 밝기 낮추기", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("포스트프로세싱 노출을 낮추고 앰비언트/디렉셔널 라이트를 어둡게 합니다.", MessageType.Info);
        GUI.backgroundColor = new Color(0.3f, 0.3f, 0.5f);
        if (GUILayout.Button("밝기 낮추기", GUILayout.Height(35)))
        {
            LowerBrightness();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        // 2. Plane 삭제
        EditorGUILayout.LabelField("2. Plane 오브젝트 삭제", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("루트에 있는 Plane 오브젝트를 삭제합니다.", MessageType.Info);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("Plane 삭제", GUILayout.Height(35)))
        {
            DeletePlane();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        // 3. 충돌판정
        EditorGUILayout.LabelField("3. 도시 오브젝트 충돌판정 추가", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("나무, 가로등 등 CityGen/Terrain 하위 오브젝트에 MeshCollider를 추가합니다.", MessageType.Info);
        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
        if (GUILayout.Button("충돌판정 추가", GUILayout.Height(35)))
        {
            AddColliders();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        // 4. Bloom/반짝거림 줄이기
        EditorGUILayout.LabelField("4. 캐릭터 반짝거림 줄이기", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Bloom 임계값을 높이고 강도를 낮춰서 캐릭터의 과도한 반짝거림을 줄입니다.", MessageType.Info);
        GUI.backgroundColor = new Color(1f, 0.9f, 0.5f);
        if (GUILayout.Button("반짝거림 줄이기 (Bloom 조정)", GUILayout.Height(35)))
        {
            ReduceBloom();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(15);

        // 전부
        GUI.backgroundColor = new Color(0.8f, 0.4f, 1f);
        if (GUILayout.Button("전부 한번에 적용!", GUILayout.Height(45)))
        {
            LowerBrightness();
            DeletePlane();
            AddColliders();
            EditorUtility.DisplayDialog("완료!", "모든 조정이 적용되었습니다!", "확인");
        }
        GUI.backgroundColor = Color.white;
    }

    private void LowerBrightness()
    {
        // 1. 포스트프로세싱 볼륨에서 노출 낮추기
        Volume volume = Object.FindAnyObjectByType<Volume>();
        if (volume != null && volume.profile != null)
        {
            // Color Adjustments 수정
            if (volume.profile.TryGet<ColorAdjustments>(out var colorAdj))
            {
                colorAdj.postExposure.Override(-0.3f); // 기존 0.3 → -0.3 (상당히 어둡게)
                colorAdj.contrast.Override(20f); // 대비 살짝 올려서 깊이감
                Debug.Log("[CyberpunkTweaks] 포스트프로세싱 노출 낮춤: -0.3");
            }

            // Bloom도 약간 낮추기
            if (volume.profile.TryGet<Bloom>(out var bloom))
            {
                bloom.intensity.Override(1.0f); // 기존 1.5 → 1.0
                Debug.Log("[CyberpunkTweaks] Bloom 강도 낮춤: 1.0");
            }

            // Vignette 강화 (어두운 느낌)
            if (volume.profile.TryGet<Vignette>(out var vignette))
            {
                vignette.intensity.Override(0.4f); // 기존 0.3 → 0.4
                Debug.Log("[CyberpunkTweaks] Vignette 강화: 0.4");
            }

            EditorUtility.SetDirty(volume.profile);
            AssetDatabase.SaveAssets();
        }
        else
        {
            Debug.LogWarning("[CyberpunkTweaks] PostProcess Volume을 찾을 수 없습니다.");
        }

        // 2. Directional Light 약하게
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light l in lights)
        {
            if (l.type == LightType.Directional)
            {
                l.intensity = 0.35f; // 기존 0.6 → 0.35
                l.color = new Color(0.85f, 0.8f, 0.9f); // 더 차갑고 어두운 달빛
                EditorUtility.SetDirty(l);
                Debug.Log("[CyberpunkTweaks] Directional Light 강도 낮춤: 0.35");
                break;
            }
        }

        // 3. 앰비언트 더 어둡게
        RenderSettings.ambientSkyColor = new Color(0.04f, 0.03f, 0.08f);
        RenderSettings.ambientEquatorColor = new Color(0.06f, 0.04f, 0.1f);
        RenderSettings.ambientGroundColor = new Color(0.08f, 0.06f, 0.04f);

        // 4. 안개 더 짙게
        RenderSettings.fogDensity = 0.02f; // 기존 0.015 → 0.02

        // 5. 네온/가로등 라이트들도 약간 낮추기
        foreach (Light l in lights)
        {
            if (l.type == LightType.Point)
            {
                l.intensity *= 0.7f; // 30% 감소
                EditorUtility.SetDirty(l);
            }
        }

        Debug.Log("[CyberpunkTweaks] 전체 밝기 낮추기 완료!");
    }

    private void ReduceBloom()
    {
        Volume volume = Object.FindAnyObjectByType<Volume>();
        if (volume != null && volume.profile != null)
        {
            if (volume.profile.TryGet<Bloom>(out var bloom))
            {
                bloom.threshold.Override(1.2f);   // 높은 임계값 → 밝은 것만 블룸
                bloom.intensity.Override(0.6f);    // 약한 강도
                bloom.scatter.Override(0.5f);      // 덜 퍼지게
                Debug.Log("[CyberpunkTweaks] Bloom 조정: threshold=1.2, intensity=0.6, scatter=0.5");
            }
            EditorUtility.SetDirty(volume.profile);
            AssetDatabase.SaveAssets();
        }
        else
        {
            Debug.LogWarning("[CyberpunkTweaks] PostProcess Volume을 찾을 수 없습니다.");
        }
        EditorUtility.DisplayDialog("완료", "Bloom 설정이 조정되었습니다.\n캐릭터 반짝거림이 줄어들었을 것입니다.", "확인");
    }

    private void DeletePlane()
    {
        // 루트에 있는 Plane 찾기
        GameObject plane = GameObject.Find("Plane");
        if (plane != null)
        {
            Undo.DestroyObjectImmediate(plane);
            Debug.Log("[CyberpunkTweaks] Plane 삭제 완료!");
        }
        else
        {
            Debug.Log("[CyberpunkTweaks] Plane을 찾지 못했습니다. 이미 삭제되었을 수 있습니다.");
        }
    }

    private void AddColliders()
    {
        int added = 0;

        // Terrain Remaked 하위 모든 오브젝트 (CityGen Buildings 포함)
        GameObject terrain = GameObject.Find("Terrain Remaked");
        if (terrain == null)
        {
            Debug.LogWarning("[CyberpunkTweaks] Terrain Remaked를 찾을 수 없습니다.");
            return;
        }

        // MeshFilter가 있지만 Collider가 없는 오브젝트에 MeshCollider 추가
        MeshFilter[] meshFilters = terrain.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf == null || mf.sharedMesh == null) continue;

            GameObject obj = mf.gameObject;

            // 이미 Collider가 있으면 스킵
            Collider existingCollider = obj.GetComponent<Collider>();
            if (existingCollider != null) continue;

            // 메시가 너무 작으면 스킵 (데코레이션 등)
            Bounds meshBounds = mf.sharedMesh.bounds;
            float meshSize = Mathf.Max(meshBounds.size.x, meshBounds.size.y, meshBounds.size.z);
            if (meshSize < 0.1f) continue;

            // MeshCollider 추가
            MeshCollider mc = obj.AddComponent<MeshCollider>();
            mc.convex = false; // 정확한 충돌을 위해 non-convex

            // 버텍스가 256개 이하인 작은 메시는 convex로 (성능)
            if (mf.sharedMesh.vertexCount <= 256)
            {
                mc.convex = true;
            }

            EditorUtility.SetDirty(obj);
            added++;
        }

        Debug.Log($"[CyberpunkTweaks] {added}개 오브젝트에 MeshCollider 추가 완료!");
    }
}
