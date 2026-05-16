using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 마을 오브젝트 배치 도구
/// - 프리팹 팔레트에서 선택 → Scene 뷰에서 클릭하면 배치
/// - 랜덤 회전/스케일 옵션
/// - 지우개 모드
/// - Undo 지원
/// </summary>
public class VillagePlacer : EditorWindow
{
    // 팔레트
    private List<GameObject> palette = new List<GameObject>();
    private int selectedIndex = -1;
    private Vector2 paletteScroll;

    // 배치 옵션
    private bool randomRotationY = true;
    private float minScale = 0.8f;
    private float maxScale = 1.2f;
    private bool snapToGrid = false;
    private bool eraserMode = false;
    private float eraserRadius = 2f;

    // 배치 부모
    private Transform placementParent;

    // 프리뷰
    private GameObject previewInstance;
    private bool isPlacing = false;

    // 카테고리 폴드아웃
    private bool showPolytope = true;
    private bool showBrokenVector = true;

    [MenuItem("Tools/Village Placer (맵 배치 도구)")]
    public static void ShowWindow()
    {
        GetWindow<VillagePlacer>("Village Placer");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        LoadPalette();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        DestroyPreview();
    }

    private void LoadPalette()
    {
        palette.Clear();

        // Polytope 프리팹
        AddPrefabsFromFolder("Assets/Polytope Studio/Lowpoly_Environments/Prefabs");
        AddPrefabsFromFolder("Assets/Polytope Studio/Lowpoly_Demos/Environment_Free/Helpers");

        // BrokenVector 나무
        AddPrefabsFromFolder("Assets/BrokenVector/LowPolyTreePack/Prefabs");
    }

    private void AddPrefabsFromFolder(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && !palette.Contains(prefab))
                palette.Add(prefab);
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Village Placer - 맵 배치 도구", EditorStyles.boldLabel);
        GUILayout.Space(5);

        // 모드 선택
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = eraserMode ? Color.white : new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("배치 모드", GUILayout.Height(30)))
            eraserMode = false;
        GUI.backgroundColor = eraserMode ? new Color(1f, 0.4f, 0.4f) : Color.white;
        if (GUILayout.Button("지우개 모드", GUILayout.Height(30)))
            eraserMode = true;
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        if (eraserMode)
        {
            eraserRadius = EditorGUILayout.Slider("지우개 범위", eraserRadius, 0.5f, 10f);
            EditorGUILayout.HelpBox("Scene 뷰에서 Shift+클릭으로 오브젝트 삭제", MessageType.Info);
        }

        GUILayout.Space(5);

        // 배치 옵션
        EditorGUILayout.LabelField("배치 옵션", EditorStyles.boldLabel);
        randomRotationY = EditorGUILayout.Toggle("Y축 랜덤 회전", randomRotationY);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("랜덤 스케일");
        minScale = EditorGUILayout.FloatField(minScale, GUILayout.Width(50));
        EditorGUILayout.LabelField("~", GUILayout.Width(15));
        maxScale = EditorGUILayout.FloatField(maxScale, GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();
        snapToGrid = EditorGUILayout.Toggle("그리드 스냅", snapToGrid);

        // 배치 부모
        placementParent = EditorGUILayout.ObjectField("배치 부모 오브젝트",
            placementParent, typeof(Transform), true) as Transform;

        GUILayout.Space(10);

        // 프리팹 팔레트
        EditorGUILayout.LabelField($"프리팹 팔레트 ({palette.Count}개)", EditorStyles.boldLabel);

        if (GUILayout.Button("팔레트 새로고침"))
            LoadPalette();

        GUILayout.Space(5);

        paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll);

        // 그리드로 표시
        int columns = Mathf.Max(1, (int)(position.width / 90));
        int index = 0;

        while (index < palette.Count)
        {
            EditorGUILayout.BeginHorizontal();
            for (int col = 0; col < columns && index < palette.Count; col++, index++)
            {
                bool isSelected = (index == selectedIndex);
                GUI.backgroundColor = isSelected ? Color.cyan : Color.white;

                EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(80), GUILayout.Height(80));

                // 프리팹 미리보기
                Texture2D preview = AssetPreview.GetAssetPreview(palette[index]);
                if (preview != null)
                {
                    if (GUILayout.Button(preview, GUILayout.Width(64), GUILayout.Height(64)))
                    {
                        selectedIndex = index;
                        eraserMode = false;
                    }
                }
                else
                {
                    if (GUILayout.Button(palette[index].name, GUILayout.Width(72), GUILayout.Height(64)))
                    {
                        selectedIndex = index;
                        eraserMode = false;
                    }
                }

                // 이름 (축약)
                string shortName = palette[index].name;
                if (shortName.Length > 12) shortName = shortName.Substring(0, 10) + "..";
                GUILayout.Label(shortName, EditorStyles.centeredGreyMiniLabel);

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndScrollView();

        // 안내
        if (selectedIndex >= 0 && selectedIndex < palette.Count)
        {
            EditorGUILayout.HelpBox(
                $"선택: {palette[selectedIndex].name}\nScene 뷰에서 Shift+클릭으로 배치",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("위에서 프리팹을 선택한 뒤 Scene 뷰에서 Shift+클릭!", MessageType.Warning);
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;

        if (eraserMode)
        {
            HandleEraser(e);
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= palette.Count) return;

        // Shift 누르고 있을 때만 배치 모드
        if (!e.shift) return;

        // 마우스 위치 → 월드 좌표 (Raycast)
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 500f))
        {
            // 프리뷰 표시
            Handles.color = new Color(0, 1, 0, 0.3f);
            Handles.DrawSolidDisc(hit.point, hit.normal, 0.5f);
            Handles.color = Color.green;
            Handles.DrawWireDisc(hit.point, Vector3.up, 0.5f);

            // 클릭으로 배치
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                PlaceObject(hit.point);
                e.Use();
            }
        }

        // Scene 뷰 갱신
        if (e.type == EventType.MouseMove)
        {
            sceneView.Repaint();
        }

        // 기본 선택 방지
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
    }

    private void PlaceObject(Vector3 position)
    {
        GameObject prefab = palette[selectedIndex];

        // 그리드 스냅
        if (snapToGrid)
        {
            VillageGrid grid = Object.FindAnyObjectByType<VillageGrid>();
            if (grid != null)
                position = grid.SnapToGrid(position);
        }

        // 인스턴스 생성
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = position;

        // 랜덤 Y 회전
        if (randomRotationY)
        {
            float angle = Random.Range(0f, 360f);
            instance.transform.rotation = Quaternion.Euler(0, angle, 0);
        }

        // 랜덤 스케일
        float scale = Random.Range(minScale, maxScale);
        instance.transform.localScale = Vector3.one * scale;

        // 부모 설정
        if (placementParent != null)
        {
            instance.transform.SetParent(placementParent);
        }

        // Undo 등록
        Undo.RegisterCreatedObjectUndo(instance, $"Place {prefab.name}");

        Debug.Log($"[Placer] {prefab.name} 배치 완료 at {position}");
    }

    private void HandleEraser(Event e)
    {
        if (!e.shift) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 500f))
        {
            // 지우개 범위 표시
            Handles.color = new Color(1, 0, 0, 0.2f);
            Handles.DrawSolidDisc(hit.point, Vector3.up, eraserRadius);
            Handles.color = Color.red;
            Handles.DrawWireDisc(hit.point, Vector3.up, eraserRadius);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                EraseObjects(hit.point);
                e.Use();
            }
        }

        if (e.type == EventType.MouseMove)
        {
            SceneView.RepaintAll();
        }

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
    }

    private void EraseObjects(Vector3 center)
    {
        Collider[] hits = Physics.OverlapSphere(center, eraserRadius);
        int count = 0;

        foreach (Collider col in hits)
        {
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(col.gameObject);
            if (root != null && root != col.gameObject.scene.GetRootGameObjects()[0])
            {
                Undo.DestroyObjectImmediate(root);
                count++;
            }
        }

        if (count > 0)
            Debug.Log($"[Placer] {count}개 오브젝트 삭제");
    }

    private void DestroyPreview()
    {
        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
        }
    }
}
