using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 프리팹에 콜라이더를 자동으로 추가하는 에디터 도구
/// - MeshRenderer 기반으로 적절한 콜라이더 자동 생성
/// - 나무: CapsuleCollider (줄기만), 바위/건물: BoxCollider
/// - Static 플래그도 같이 설정
/// </summary>
public class AutoColliderSetup : EditorWindow
{
    private enum ColliderType { Auto, Box, Capsule, Sphere, MeshCollider }

    private ColliderType colliderType = ColliderType.Auto;
    private bool setStatic = true;
    private bool addToChildren = false;
    private string searchFolder = "Assets";
    private Vector2 scrollPos;
    private List<string> resultLog = new List<string>();

    [MenuItem("Tools/Auto Collider Setup")]
    public static void ShowWindow()
    {
        GetWindow<AutoColliderSetup>("Auto Collider");
    }

    private void OnGUI()
    {
        GUILayout.Label("프리팹 자동 콜라이더 설정", EditorStyles.boldLabel);
        GUILayout.Space(10);

        searchFolder = EditorGUILayout.TextField("검색 폴더", searchFolder);
        colliderType = (ColliderType)EditorGUILayout.EnumPopup("콜라이더 타입", colliderType);
        setStatic = EditorGUILayout.Toggle("Static 설정", setStatic);

        GUILayout.Space(5);
        EditorGUILayout.HelpBox(
            "Auto 모드: 이름에 Tree→CapsuleCollider(줄기), Rock/Shrub→BoxCollider, 기타→BoxCollider",
            MessageType.Info);

        GUILayout.Space(10);

        // 빠른 버튼들
        GUILayout.Label("빠른 실행", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Polytope 환경 에셋", GUILayout.Height(35)))
        {
            searchFolder = "Assets/Polytope Studio";
            ProcessFolder();
        }
        if (GUILayout.Button("BrokenVector 나무", GUILayout.Height(35)))
        {
            searchFolder = "Assets/BrokenVector";
            ProcessFolder();
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);

        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("선택 폴더 전체 처리", GUILayout.Height(40)))
        {
            ProcessFolder();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(5);

        if (GUILayout.Button("선택한 오브젝트에만 적용 (Scene/Project)"))
        {
            ProcessSelected();
        }

        // 결과 로그
        if (resultLog.Count > 0)
        {
            GUILayout.Space(10);
            GUILayout.Label("결과:", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
            foreach (string log in resultLog)
            {
                GUILayout.Label(log, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void ProcessFolder()
    {
        resultLog.Clear();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { searchFolder });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            // 프리팹 편집 모드
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

            bool modified = AddColliderTo(root, prefab.name);

            if (setStatic)
            {
                SetStaticRecursive(root);
            }

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                count++;
                resultLog.Add($"  + {prefab.name}");
            }

            PrefabUtility.UnloadPrefabContents(root);
        }

        resultLog.Insert(0, $"총 {count}개 프리팹에 콜라이더 추가 완료!");
        Debug.Log($"[AutoCollider] {count}개 프리팹 처리 완료 ({searchFolder})");
        AssetDatabase.SaveAssets();
    }

    private void ProcessSelected()
    {
        resultLog.Clear();
        int count = 0;

        foreach (GameObject obj in Selection.gameObjects)
        {
            if (AddColliderTo(obj, obj.name))
            {
                if (setStatic) SetStaticRecursive(obj);
                EditorUtility.SetDirty(obj);
                count++;
                resultLog.Add($"  + {obj.name}");
            }
        }

        resultLog.Insert(0, $"선택한 {count}개 오브젝트에 콜라이더 추가!");
    }

    private bool AddColliderTo(GameObject obj, string name)
    {
        // 이미 콜라이더가 있으면 스킵
        if (obj.GetComponentInChildren<Collider>() != null)
            return false;

        // 렌더러의 전체 바운드 계산
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        Bounds totalBounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
        {
            totalBounds.Encapsulate(r.bounds);
        }

        // 콜라이더 타입 결정
        ColliderType type = colliderType;
        if (type == ColliderType.Auto)
        {
            type = DetectBestCollider(name, totalBounds);
        }

        // 루트에 콜라이더 추가
        switch (type)
        {
            case ColliderType.Capsule:
                AddTreeCapsuleCollider(obj, totalBounds);
                break;
            case ColliderType.Sphere:
                SphereCollider sc = obj.AddComponent<SphereCollider>();
                sc.center = obj.transform.InverseTransformPoint(totalBounds.center);
                sc.radius = Mathf.Max(totalBounds.extents.x, totalBounds.extents.z);
                break;
            case ColliderType.MeshCollider:
                MeshFilter mf = obj.GetComponentInChildren<MeshFilter>();
                if (mf != null)
                {
                    MeshCollider mc = obj.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                }
                break;
            default: // Box
                BoxCollider bc = obj.AddComponent<BoxCollider>();
                bc.center = obj.transform.InverseTransformPoint(totalBounds.center);
                bc.size = totalBounds.size;
                break;
        }

        return true;
    }

    private ColliderType DetectBestCollider(string name, Bounds bounds)
    {
        string lower = name.ToLower();

        // 나무류 → 줄기만 감싸는 캡슐
        if (lower.Contains("tree") || lower.Contains("pine") || lower.Contains("fruit"))
            return ColliderType.Capsule;

        // 바위, 관목 → 박스
        if (lower.Contains("rock") || lower.Contains("shrub") || lower.Contains("bush"))
            return ColliderType.Box;

        // 버섯, 꽃, 풀 → 작으니까 박스
        if (lower.Contains("mushroom") || lower.Contains("flower") || lower.Contains("grass") || lower.Contains("poppy"))
            return ColliderType.Box;

        // 울타리 → 박스
        if (lower.Contains("fence"))
            return ColliderType.Box;

        // 세로로 긴 오브젝트 → 캡슐
        if (bounds.size.y > bounds.size.x * 2f)
            return ColliderType.Capsule;

        return ColliderType.Box;
    }

    /// <summary>
    /// 나무용: 줄기 부분만 감싸는 얇은 CapsuleCollider
    /// (잎 부분은 통과 가능하게)
    /// </summary>
    private void AddTreeCapsuleCollider(GameObject obj, Bounds totalBounds)
    {
        CapsuleCollider cc = obj.AddComponent<CapsuleCollider>();
        Vector3 localCenter = obj.transform.InverseTransformPoint(totalBounds.center);

        // 줄기 = 전체 높이의 하단 40%, 폭은 전체의 20%
        float trunkHeight = totalBounds.size.y * 0.4f;
        float trunkRadius = Mathf.Max(totalBounds.extents.x, totalBounds.extents.z) * 0.2f;
        trunkRadius = Mathf.Max(trunkRadius, 0.15f); // 최소 반지름

        cc.center = new Vector3(localCenter.x, trunkHeight * 0.5f, localCenter.z);
        cc.height = trunkHeight;
        cc.radius = trunkRadius;
        cc.direction = 1; // Y축
    }

    private void SetStaticRecursive(GameObject obj)
    {
        obj.isStatic = true;
        foreach (Transform child in obj.transform)
        {
            SetStaticRecursive(child.gameObject);
        }
    }
}
