using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 사이버펑크 머티리얼 적용 + 바닥 확장 + UI 정리
/// </summary>
public class CyberpunkApply : EditorWindow
{
    [MenuItem("Tools/사이버펑크 적용 도구")]
    public static void ShowWindow()
    {
        GetWindow<CyberpunkApply>("사이버펑크 적용");
    }

    private void OnGUI()
    {
        GUILayout.Label("사이버펑크 적용 도구", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 1. 건물 네온 머티리얼
        EditorGUILayout.LabelField("1. 건물 네온 머티리얼 적용", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("CityGen Buildings 하위 건물에 네온 머티리얼을 랜덤 적용합니다.", MessageType.Info);
        GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
        if (GUILayout.Button("건물에 네온 머티리얼 적용", GUILayout.Height(35)))
        {
            ApplyNeonToBuildings();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        // 2. 따뜻한 조명 추가
        EditorGUILayout.LabelField("2. 따뜻한 조명 보강", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("가로등/창문 느낌의 따뜻한 포인트 라이트를 추가합니다.", MessageType.Info);
        GUI.backgroundColor = new Color(1f, 0.8f, 0.4f);
        if (GUILayout.Button("따뜻한 가로등 조명 추가", GUILayout.Height(35)))
        {
            AddWarmStreetLights();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        // 3. 바닥 확장
        EditorGUILayout.LabelField("3. 바닥 충돌 판정 확장", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("기존 Plane을 도시 맵 전체를 덮도록 확장합니다.", MessageType.Info);
        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
        if (GUILayout.Button("Plane 바닥 확장 (도시 전체)", GUILayout.Height(35)))
        {
            ExpandFloor();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        // 4. New Text 제거
        EditorGUILayout.LabelField("4. 'New Text' UI 제거", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("화면 중앙에 떠 있는 'New Text' 오브젝트를 찾아 제거합니다.", MessageType.Info);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("New Text 찾아서 제거", GUILayout.Height(35)))
        {
            RemoveNewText();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(15);

        // 전체 적용
        GUI.backgroundColor = new Color(0.8f, 0.4f, 1f);
        if (GUILayout.Button("전부 한번에 적용!", GUILayout.Height(45)))
        {
            ApplyNeonToBuildings();
            AddWarmStreetLights();
            ExpandFloor();
            RemoveNewText();
            EditorUtility.DisplayDialog("완료!", "모든 항목이 적용되었습니다!", "확인");
        }
        GUI.backgroundColor = Color.white;
    }

    private void ApplyNeonToBuildings()
    {
        // CityGen Buildings 찾기
        GameObject cityGen = GameObject.Find("CityGen Buildings");
        if (cityGen == null)
        {
            // Terrain Remaked 하위에서 찾기
            GameObject terrain = GameObject.Find("Terrain Remaked");
            if (terrain != null)
            {
                Transform found = terrain.transform.Find("CityGen Buildings");
                if (found != null) cityGen = found.gameObject;
            }
        }

        if (cityGen == null)
        {
            Debug.LogWarning("[CyberpunkApply] CityGen Buildings를 찾을 수 없습니다. 수동으로 건물 오브젝트를 선택해주세요.");
            return;
        }

        // 네온 머티리얼 로드
        Material matBlue = AssetDatabase.LoadAssetAtPath<Material>("Assets/sevens/Materials/CyberpunkBuilding_Blue.mat");
        Material matPink = AssetDatabase.LoadAssetAtPath<Material>("Assets/sevens/Materials/CyberpunkBuilding_Pink.mat");
        Material matWarm = AssetDatabase.LoadAssetAtPath<Material>("Assets/sevens/Materials/CyberpunkBuilding_Warm.mat");
        Material matGround = AssetDatabase.LoadAssetAtPath<Material>("Assets/sevens/Materials/CyberpunkGround.mat");

        if (matBlue == null || matPink == null)
        {
            Debug.LogError("[CyberpunkApply] 네온 머티리얼을 찾을 수 없습니다. 먼저 '사이버펑크 비주얼 세팅'에서 머티리얼을 생성해주세요.");
            return;
        }

        Material[] buildingMats = { matBlue, matPink, matWarm };
        int count = 0;

        Renderer[] renderers = cityGen.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            // 바닥/도로 구분
            string name = r.gameObject.name.ToLower();
            bool isGround = name.Contains("road") || name.Contains("ground") || name.Contains("floor")
                         || name.Contains("sidewalk") || name.Contains("pavement") || name.Contains("plane");

            if (isGround)
            {
                // 바닥은 어둡게
                Material[] mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = matGround;
                r.sharedMaterials = mats;
            }
            else
            {
                // 건물은 랜덤 네온 배정 (건물마다 일관된 색상)
                int hash = r.gameObject.GetInstanceID();
                Material chosen = buildingMats[Mathf.Abs(hash) % buildingMats.Length];

                Material[] mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = chosen;
                r.sharedMaterials = mats;
            }

            EditorUtility.SetDirty(r);
            count++;
        }

        Debug.Log($"[CyberpunkApply] 건물 {count}개 렌더러에 네온 머티리얼 적용 완료!");
    }

    private void AddWarmStreetLights()
    {
        // 기존 가로등 그룹 제거
        GameObject existing = GameObject.Find("WarmStreetLights");
        if (existing != null) DestroyImmediate(existing);

        GameObject parent = new GameObject("WarmStreetLights");
        Undo.RegisterCreatedObjectUndo(parent, "Create Warm Street Lights");

        Color warmYellow = new Color(1f, 0.85f, 0.5f);
        Color warmOrange = new Color(1f, 0.7f, 0.4f);
        Color warmPink = new Color(1f, 0.75f, 0.8f);

        // 바닥 근처에 따뜻한 조명 배치 (가로등 느낌)
        Vector3[] positions = {
            new Vector3(0, 3, 0),
            new Vector3(8, 3, 5),
            new Vector3(-8, 3, -5),
            new Vector3(15, 3, -8),
            new Vector3(-12, 3, 10),
            new Vector3(5, 3, -12),
            new Vector3(-5, 3, 15),
            new Vector3(20, 3, 0),
            new Vector3(-18, 3, -10),
            new Vector3(0, 3, 20),
            // 높은 위치 (건물 창문 느낌)
            new Vector3(10, 8, 3),
            new Vector3(-6, 10, -8),
            new Vector3(3, 12, 12),
            new Vector3(-15, 6, 5),
        };

        Color[] colors = { warmYellow, warmOrange, warmYellow, warmPink, warmOrange,
                          warmYellow, warmPink, warmOrange, warmYellow, warmPink,
                          warmOrange, warmYellow, warmPink, warmOrange };

        for (int i = 0; i < positions.Length; i++)
        {
            GameObject lightObj = new GameObject($"WarmLight_{i:D2}");
            lightObj.transform.SetParent(parent.transform);
            lightObj.transform.position = positions[i];

            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colors[i % colors.Length];
            light.range = 15f;
            light.intensity = 1.8f;
            light.shadows = LightShadows.None;
        }

        Debug.Log($"[CyberpunkApply] 따뜻한 가로등 조명 {positions.Length}개 추가 완료!");
    }

    private void ExpandFloor()
    {
        // 기존 Plane 찾기 (루트에 있는 것)
        GameObject plane = GameObject.Find("Plane");
        if (plane == null)
        {
            Debug.LogWarning("[CyberpunkApply] Plane을 찾을 수 없습니다.");
            return;
        }

        // Terrain Remaked의 범위 확인
        GameObject terrain = GameObject.Find("Terrain Remaked");
        Bounds mapBounds = new Bounds(Vector3.zero, Vector3.zero);

        if (terrain != null)
        {
            Renderer[] renderers = terrain.GetComponentsInChildren<Renderer>(true);
            bool first = true;
            foreach (Renderer r in renderers)
            {
                if (first)
                {
                    mapBounds = r.bounds;
                    first = false;
                }
                else
                {
                    mapBounds.Encapsulate(r.bounds);
                }
            }
        }

        if (mapBounds.size == Vector3.zero)
        {
            // 기본 크기로 확장
            mapBounds = new Bounds(Vector3.zero, new Vector3(100, 0, 100));
        }

        // Plane 위치/크기 조정
        // Unity Plane은 기본 10x10 유닛 (Scale 1 = 10m)
        float sizeX = mapBounds.size.x * 1.2f; // 20% 여유
        float sizeZ = mapBounds.size.z * 1.2f;
        float scaleX = sizeX / 10f;
        float scaleZ = sizeZ / 10f;

        plane.transform.position = new Vector3(mapBounds.center.x, -0.05f, mapBounds.center.z);
        plane.transform.localScale = new Vector3(scaleX, 1, scaleZ);

        // 콜라이더 갱신
        MeshCollider mc = plane.GetComponent<MeshCollider>();
        if (mc == null) mc = plane.AddComponent<MeshCollider>();

        // 바닥 머티리얼도 사이버펑크로
        Material groundMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/sevens/Materials/CyberpunkGround.mat");
        Renderer planeRenderer = plane.GetComponent<Renderer>();
        if (planeRenderer != null && groundMat != null)
        {
            planeRenderer.sharedMaterial = groundMat;
            EditorUtility.SetDirty(planeRenderer);
        }

        EditorUtility.SetDirty(plane);

        Debug.Log($"[CyberpunkApply] Plane 확장 완료! 범위: {mapBounds.size}, Scale: ({scaleX:F1}, 1, {scaleZ:F1}), 중심: {mapBounds.center}");
    }

    private void RemoveNewText()
    {
        int removed = 0;

        // "New Text" 이름의 모든 오브젝트 찾기
        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (GameObject obj in allObjects)
        {
            if (obj.name == "New Text" || obj.name == "New text" || obj.name == "new text")
            {
                Debug.Log($"[CyberpunkApply] '{obj.name}' 발견 (부모: {(obj.transform.parent != null ? obj.transform.parent.name : "없음")}) - 제거");
                Undo.DestroyObjectImmediate(obj);
                removed++;
            }
        }

        // TMPro Text 중 "New Text" 내용인 것도 찾기
        TMPro.TextMeshProUGUI[] tmpTexts = Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None);
        foreach (var tmp in tmpTexts)
        {
            if (tmp != null && tmp.text != null && tmp.text.Trim() == "New Text")
            {
                Debug.Log($"[CyberpunkApply] TMP 'New Text' 발견 (오브젝트: {tmp.gameObject.name}) - 제거");
                Undo.DestroyObjectImmediate(tmp.gameObject);
                removed++;
            }
        }

        // 일반 UnityEngine.UI.Text도 확인
        UnityEngine.UI.Text[] uiTexts = Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None);
        foreach (var txt in uiTexts)
        {
            if (txt != null && txt.text != null && txt.text.Trim() == "New Text")
            {
                Debug.Log($"[CyberpunkApply] UI Text 'New Text' 발견 (오브젝트: {txt.gameObject.name}) - 제거");
                Undo.DestroyObjectImmediate(txt.gameObject);
                removed++;
            }
        }

        if (removed > 0)
            Debug.Log($"[CyberpunkApply] 총 {removed}개 'New Text' 제거 완료!");
        else
            Debug.Log("[CyberpunkApply] 'New Text'를 찾지 못했습니다. Hierarchy에서 직접 확인해주세요.");
    }
}
