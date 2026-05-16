using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 라스베가스 도시 프로시저럴 생성기
/// - 메인 스트립 + 사이드 스트리트 레이아웃
/// - 건물, 도로, 인도, 가로등 자동 배치
/// - VillageGrid와 연동
/// - VegasMapData로 저장/로드 가능
///
/// 사용법: MyRoom 씬에 빈 오브젝트 생성 후 이 컴포넌트 추가
///         또는 [RuntimeInitializeOnLoadMethod]로 자동 생성
/// </summary>
public class VegasCityGenerator : MonoBehaviour
{
    public static VegasCityGenerator Instance { get; private set; }

    [Header("Grid Settings")]
    [SerializeField] private int gridWidth = 40;
    [SerializeField] private int gridHeight = 40;
    [SerializeField] private float cellSize = 2f;

    [Header("Road Layout")]
    [SerializeField] private int mainStripColumn = 17;  // 메인 스트립 시작 컬럼
    [SerializeField] private int mainStripWidth = 6;     // 스트립 폭 (6셀 = 12m)
    [SerializeField] private int sidewalkWidth = 2;      // 인도 폭
    [SerializeField] private int crossStreetInterval = 10; // 횡단도로 간격
    [SerializeField] private int crossStreetWidth = 3;    // 횡단도로 폭

    [Header("Generation")]
    [SerializeField] private int seed = 42;
    [SerializeField] private bool autoGenerate = true;

    // 내부 상태
    private VegasCellType[,] cellTypes;
    private VegasMapData currentMapData;
    private GameObject cityRoot;
    private List<GameObject> generatedBuildings = new List<GameObject>();

    // 생성된 건물 데이터 (에디터용)
    public VegasMapData CurrentMapData => currentMapData;
    public VegasCellType[,] CellTypes => cellTypes;
    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;
    public float CellSize => cellSize;

    /// <summary>
    /// 씬 로드 이벤트 등록 — Play 시작 전(SubsystemRegistration)에 핸들러 부착해서
    /// 첫 씬은 물론, 런타임 중 SceneManager.LoadScene 으로 MyRoom에 진입할 때도
    /// VegasCityGenerator가 자동 생성되도록 한다.
    /// (AfterSceneLoad 단독은 "게임 시작 시 첫 씬"에서만 발동되므로
    ///  TitleScreen → MyRoom 전환 시 누락되는 버그를 해결.)
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterSceneHandlers()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(
        UnityEngine.SceneManagement.Scene scene,
        UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        TrySpawnInMyRoom(scene.name);
    }

    /// <summary>
    /// 게임 시작 직후, MyRoom 씬을 에디터에서 직접 Play 한 경우를 위한 보강.
    /// (sceneLoaded 핸들러가 첫 씬 로드 전에 등록돼 있으면 이건 사실상 중복이지만,
    ///  도메인 리로드 비활성화 등 환경에 따라 누락될 수 있으므로 안전망으로 둠.)
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoGenerate()
    {
        TrySpawnInMyRoom(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    private static void TrySpawnInMyRoom(string sceneName)
    {
        if (sceneName != "MyRoom") return;

        // 이미 있으면 스킵
        if (FindAnyObjectByType<VegasCityGenerator>() != null) return;

        GameObject go = new GameObject("[VegasCity]");
        go.AddComponent<VegasCityGenerator>();
        Debug.Log("[VegasCity] MyRoom 진입 — 도시 생성기 자동 부착");
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // 베가스 도시와 겹치는 기존 씬 오브젝트 정리
        DisableConflictingSceneObjects();

        // 카툰풍 밝은 데이라이트 셋업
        SetupCartoonLighting();

        if (autoGenerate)
        {
            // 기존 저장된 맵 클리어 후 새로 생성 (셰이더 수정 후 재생성 필요)
            // TODO: 안정화 후 아래 주석 해제하여 저장/로드 복원
            // string savedMap = PlayerPrefs.GetString("VegasMapData", "");
            // if (!string.IsNullOrEmpty(savedMap))
            // {
            //     LoadFromJson(savedMap);
            // }
            // else
            // {
                PlayerPrefs.DeleteKey("VegasMapData");
                GenerateDefaultCity();
            // }
        }
    }

    /// <summary>
    /// 베가스 지면/도로 위와 겹치는 씬 오브젝트(기본 Plane, 빠진 Terrain 등)
    /// 를 자동 비활성화해서 z-fighting / 시각 충돌 방지
    /// </summary>
    private void DisableConflictingSceneObjects()
    {
        // 일반 Plane: 기본적으로 (0,0,0) 위치라 베가스 지면과 충돌
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var rootObjs = scene.GetRootGameObjects();
        foreach (var root in rootObjs)
        {
            DisableConflictingRecursive(root.transform);
        }
    }

    /// <summary>
    /// 카툰풍 환경 셋업 — 밝은 푸른 하늘 + 환경광 + 디렉셔널 라이트 조정
    /// 닌텐도 스트리트패스 같은 깔끔한 데이라이트 룩
    /// </summary>
    private void SetupCartoonLighting()
    {
        // 환경광 — 그라디언트로 하늘 위/지평선/지면 색 지정
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor      = new Color(0.85f, 0.93f, 1.00f); // 푸른 하늘
        RenderSettings.ambientEquatorColor  = new Color(0.78f, 0.82f, 0.86f); // 지평선 회색-파랑
        RenderSettings.ambientGroundColor   = new Color(0.55f, 0.55f, 0.50f); // 부드러운 회색
        RenderSettings.ambientIntensity = 1.2f;

        // 안개 — 살짝 푸르스름하게(원경 빌딩 느낌)
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.78f, 0.85f, 0.95f);
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.006f;

        // 단순한 배경색 (스카이박스 없을 때 대비)
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.7f, 0.85f, 1f); // 푸른 하늘
        }

        // 메인 디렉셔널 라이트 — 사용자 지정 인스펙터 값 반영
        Light sun = null;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type == LightType.Directional) { sun = l; break; }
        }
        if (sun == null)
        {
            var sunGO = new GameObject("VegasSun");
            sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        else
        {
            sun.gameObject.name = "VegasSun";
        }
        sun.color = new Color(1f, 0.957f, 0.855f);     // 따뜻한 크림색 (인스펙터 색상)
        sun.intensity = 0.9f;                           // 인스펙터 Intensity
        sun.bounceIntensity = 0.9f;                     // Indirect Multiplier
        sun.transform.position = Vector3.zero;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        sun.shadowBias = 0.05f;
        sun.shadowNormalBias = 0.4f;
        sun.shadowNearPlane = 0.2f;

        // 빛 끊김 방지 — Shadow 거리/캐스케이드 페이드 늘리기
        QualitySettings.shadowDistance = 200f;
        QualitySettings.shadowCascades = 4;
        QualitySettings.shadowCascade4Split = new Vector3(0.06f, 0.18f, 0.45f);

        // URP Asset의 ShadowDistance, ShadowFade 도 같이 늘려서 캐스케이드 경계 부드럽게
        BumpUrpShadowDistance(200f, 0.35f);
    }

    /// <summary>
    /// 리플렉션으로 URP Asset의 shadowDistance / cascadeBorder 를 강제 설정.
    /// 패키지 참조 없이 동적으로 처리 — URP가 없으면 조용히 스킵.
    /// </summary>
    private static void BumpUrpShadowDistance(float distance, float fade)
    {
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (pipeline == null) return;
        var t = pipeline.GetType();
        if (!t.Name.Contains("Universal")) return;

        TrySetFloatProperty(pipeline, "shadowDistance", distance);
        // URP는 0~1 비율의 "shadowCascade border" (페이드 시작 위치)을 제공.
        // 0.2~0.4 가 자연스러움.
        TrySetFloatProperty(pipeline, "cascadeBorder", fade);
    }

    private static void TrySetFloatProperty(object obj, string name, float value)
    {
        var p = obj.GetType().GetProperty(name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (p != null && p.CanWrite && p.PropertyType == typeof(float))
        {
            try { p.SetValue(obj, value); } catch { /* 버전마다 setter 없는 경우 있음 */ }
        }
    }

    private void DisableConflictingRecursive(Transform t)
    {
        if (t == null) return;
        if (t.gameObject == this.gameObject) return;

        string n = t.name;

        // 정확히 "Plane"인 오브젝트(기본 Unity Plane)
        if (n == "Plane")
        {
            t.gameObject.SetActive(false);
            Debug.Log($"[VegasCity] '{n}' 비활성화 (Vegas 지면과 충돌 방지)");
        }
        // Missing Prefab 잔재 (e.g. "Terrain Remaked (Missing Prefab ...)")
        else if (n.StartsWith("Terrain") || n.Contains("Missing Prefab"))
        {
            t.gameObject.SetActive(false);
            Debug.Log($"[VegasCity] '{n}' 비활성화 (지형 충돌/missing prefab)");
        }

        for (int i = 0; i < t.childCount; i++)
        {
            DisableConflictingRecursive(t.GetChild(i));
        }
    }

    // ──────────────────────────────────────────
    // 도시 생성
    // ──────────────────────────────────────────

    /// <summary>
    /// 기본 라스베가스 도시 생성
    /// </summary>
    public void GenerateDefaultCity()
    {
        currentMapData = new VegasMapData
        {
            gridWidth = gridWidth,
            gridHeight = gridHeight,
            cellSize = cellSize,
            mapName = "Las Vegas Strip"
        };

        Random.InitState(seed);
        InitCellTypes();
        LayoutRoads();
        LayoutBuildings();
        BuildCity();
    }

    /// <summary>
    /// 맵 데이터로부터 도시 빌드
    /// </summary>
    public void BuildFromData(VegasMapData data)
    {
        currentMapData = data;
        gridWidth = data.gridWidth;
        gridHeight = data.gridHeight;
        cellSize = data.cellSize;

        InitCellTypes();
        LayoutRoads();

        // 맵 데이터의 건물들로 셀 타입 설정
        foreach (var bp in data.buildings)
        {
            for (int x = bp.gridX; x < bp.gridX + bp.sizeX; x++)
            {
                for (int z = bp.gridZ; z < bp.gridZ + bp.sizeZ; z++)
                {
                    if (x >= 0 && x < gridWidth && z >= 0 && z < gridHeight)
                        cellTypes[x, z] = VegasCellType.Building;
                }
            }
        }

        BuildCity();
    }

    /// <summary>
    /// 도시 전체 파괴 후 재생성
    /// </summary>
    public void ClearCity()
    {
        if (cityRoot != null)
            Destroy(cityRoot);

        generatedBuildings.Clear();
    }

    // ──────────────────────────────────────────
    // 셀 타입 초기화 & 도로 레이아웃
    // ──────────────────────────────────────────

    private void InitCellTypes()
    {
        cellTypes = new VegasCellType[gridWidth, gridHeight];
    }

    /// <summary>
    /// 도로 + 인도 레이아웃 설정
    /// </summary>
    private void LayoutRoads()
    {
        // === 메인 스트립 (세로 도로) ===
        int stripStart = mainStripColumn;
        int stripEnd = mainStripColumn + mainStripWidth;

        for (int z = 0; z < gridHeight; z++)
        {
            // 인도 (스트립 좌우)
            for (int sw = 0; sw < sidewalkWidth; sw++)
            {
                int leftSW = stripStart - sidewalkWidth + sw;
                int rightSW = stripEnd + sw;
                if (leftSW >= 0 && leftSW < gridWidth)
                    cellTypes[leftSW, z] = VegasCellType.Sidewalk;
                if (rightSW >= 0 && rightSW < gridWidth)
                    cellTypes[rightSW, z] = VegasCellType.Sidewalk;
            }

            // 도로
            for (int x = stripStart; x < stripEnd && x < gridWidth; x++)
            {
                cellTypes[x, z] = VegasCellType.Road;
            }
        }

        // === 횡단도로 (가로 도로) ===
        for (int roadZ = crossStreetInterval; roadZ < gridHeight; roadZ += crossStreetInterval)
        {
            for (int z = roadZ; z < roadZ + crossStreetWidth && z < gridHeight; z++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    if (cellTypes[x, z] == VegasCellType.Empty)
                        cellTypes[x, z] = VegasCellType.Road;
                    else if (cellTypes[x, z] == VegasCellType.Sidewalk)
                        cellTypes[x, z] = VegasCellType.Crosswalk;
                }

                // 횡단도로 양쪽 인도
                int aboveZ = roadZ - 1;
                int belowZ = roadZ + crossStreetWidth;
                if (aboveZ >= 0)
                {
                    for (int x = 0; x < gridWidth; x++)
                    {
                        if (cellTypes[x, aboveZ] == VegasCellType.Empty)
                            cellTypes[x, aboveZ] = VegasCellType.Sidewalk;
                    }
                }
                if (belowZ < gridHeight)
                {
                    for (int x = 0; x < gridWidth; x++)
                    {
                        if (cellTypes[x, belowZ] == VegasCellType.Empty)
                            cellTypes[x, belowZ] = VegasCellType.Sidewalk;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 비어있는 블록에 건물 자동 배치
    /// </summary>
    private void LayoutBuildings()
    {
        currentMapData.buildings.Clear();

        // 블록별로 건물 배치
        // 블록 = 도로로 둘러싸인 영역

        List<RectInt> blocks = FindEmptyBlocks();

        foreach (var block in blocks)
        {
            FillBlockWithBuildings(block);
        }
    }

    /// <summary>
    /// 빈 블록(도로/인도가 아닌 연속 영역) 찾기
    /// </summary>
    private List<RectInt> FindEmptyBlocks()
    {
        List<RectInt> blocks = new List<RectInt>();
        bool[,] visited = new bool[gridWidth, gridHeight];

        for (int z = 0; z < gridHeight; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (visited[x, z] || cellTypes[x, z] != VegasCellType.Empty)
                    continue;

                // 가능한 가장 큰 직사각형 블록 찾기
                int maxW = 0;
                int maxH = 0;

                // 폭 측정
                while (x + maxW < gridWidth && cellTypes[x + maxW, z] == VegasCellType.Empty && !visited[x + maxW, z])
                    maxW++;

                // 높이 측정
                maxH = 1;
                bool canExtend = true;
                while (canExtend && z + maxH < gridHeight)
                {
                    for (int tx = x; tx < x + maxW; tx++)
                    {
                        if (cellTypes[tx, z + maxH] != VegasCellType.Empty || visited[tx, z + maxH])
                        {
                            canExtend = false;
                            break;
                        }
                    }
                    if (canExtend) maxH++;
                }

                if (maxW >= 2 && maxH >= 2) // 최소 2x2 블록만
                {
                    blocks.Add(new RectInt(x, z, maxW, maxH));
                    for (int bx = x; bx < x + maxW; bx++)
                        for (int bz = z; bz < z + maxH; bz++)
                            visited[bx, bz] = true;
                }
                else
                {
                    // 너무 작은 블록은 스킵
                    for (int bx = x; bx < x + maxW; bx++)
                        for (int bz = z; bz < z + maxH; bz++)
                            visited[bx, bz] = true;
                }
            }
        }

        return blocks;
    }

    /// <summary>
    /// 블록 내에 건물들 채우기
    /// </summary>
    private void FillBlockWithBuildings(RectInt block)
    {
        // 스트립에 인접한 블록은 카지노/호텔 배치
        bool isStripAdjacent = IsAdjacentToStrip(block);

        int x = block.x;
        int maxX = block.x + block.width;

        while (x < maxX)
        {
            int z = block.y;
            int maxZ = block.y + block.height;

            while (z < maxZ)
            {
                // 건물 타입 결정
                VegasBuildingType type = ChooseBuildingType(isStripAdjacent);
                var spec = VegasBuildingConfig.GetSpec(type);

                // 크기 결정 (블록 내에 맞도록)
                int bw = Random.Range(spec.minSize.x, spec.maxSize.x + 1);
                int bh = Random.Range(spec.minSize.y, spec.maxSize.y + 1);
                bw = Mathf.Min(bw, maxX - x);
                bh = Mathf.Min(bh, maxZ - z);

                if (bw < spec.minSize.x || bh < spec.minSize.y)
                {
                    z++;
                    continue;
                }

                // 건물 배치 데이터 생성
                int floors = Random.Range(spec.minFloors, spec.maxFloors + 1);
                int colorIdx = Random.Range(0, 10);

                var placement = new VegasMapData.BuildingPlacement
                {
                    gridX = x,
                    gridZ = z,
                    sizeX = bw,
                    sizeZ = bh,
                    buildingType = type,
                    rotation = (Random.value > 0.5f && !isStripAdjacent) ? 90 : 0,
                    floors = floors,
                    colorIndex = colorIdx,
                    customName = ""
                };

                currentMapData.buildings.Add(placement);

                // 셀 타입 업데이트
                for (int bx = x; bx < x + bw; bx++)
                    for (int bz = z; bz < z + bh; bz++)
                        if (bx < gridWidth && bz < gridHeight)
                            cellTypes[bx, bz] = VegasCellType.Building;

                z += bh;
            }

            // 다음 열로 이동 (최소 건물 폭만큼)
            int minW = 2;
            foreach (var bp in currentMapData.buildings)
            {
                if (bp.gridX == x)
                    minW = Mathf.Max(minW, bp.sizeX);
            }
            x += minW;
        }
    }

    private bool IsAdjacentToStrip(RectInt block)
    {
        int stripLeft = mainStripColumn - sidewalkWidth;
        int stripRight = mainStripColumn + mainStripWidth + sidewalkWidth;

        return (block.x + block.width >= stripLeft && block.x <= stripRight);
    }

    private VegasBuildingType ChooseBuildingType(bool isStripAdjacent)
    {
        if (isStripAdjacent)
        {
            float r = Random.value;
            if (r < 0.35f) return VegasBuildingType.Casino;
            if (r < 0.6f) return VegasBuildingType.Hotel;
            if (r < 0.75f) return VegasBuildingType.Bar;
            if (r < 0.85f) return VegasBuildingType.Shop;
            if (r < 0.9f) return VegasBuildingType.Billboard;
            if (r < 0.95f) return VegasBuildingType.Fountain;
            return VegasBuildingType.Restaurant;
        }
        else
        {
            float r = Random.value;
            if (r < 0.25f) return VegasBuildingType.Shop;
            if (r < 0.4f) return VegasBuildingType.Restaurant;
            if (r < 0.55f) return VegasBuildingType.Bar;
            if (r < 0.65f) return VegasBuildingType.ParkingLot;
            if (r < 0.75f) return VegasBuildingType.Hotel;
            if (r < 0.85f) return VegasBuildingType.Billboard;
            return VegasBuildingType.Casino;
        }
    }

    // ──────────────────────────────────────────
    // 3D 도시 빌드
    // ──────────────────────────────────────────

    /// <summary>
    /// 셀 데이터 + 건물 데이터로 3D 도시 생성
    /// </summary>
    private void BuildCity()
    {
        ClearCity();
        cityRoot = new GameObject("CityRoot");
        cityRoot.transform.SetParent(transform);
        cityRoot.transform.localPosition = Vector3.zero;

        BuildGround();
        BuildRoads();
        BuildBuildings();
        BuildStreetLights();
        SetupPlayerSpawn();

        Debug.Log($"[VegasCity] 도시 생성 완료: 건물 {currentMapData.buildings.Count}개, 그리드 {gridWidth}x{gridHeight}");
    }

    /// <summary>
    /// 기본 지면
    /// </summary>
    private void BuildGround()
    {
        float totalW = gridWidth * cellSize;
        float totalD = gridHeight * cellSize;

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.SetParent(cityRoot.transform);
        ground.transform.localPosition = new Vector3(totalW / 2, -0.05f, totalD / 2);
        ground.transform.localScale = new Vector3(totalW + 10, 0.1f, totalD + 10);

        Material groundMat = VegasProceduralBuilding.CreateMaterial(new Color(0.12f, 0.12f, 0.14f), 0.2f);
        ground.GetComponent<Renderer>().material = groundMat;
    }

    /// <summary>
    /// 도로 + 인도 + 횡단보도 생성
    /// </summary>
    private void BuildRoads()
    {
        GameObject roadsParent = new GameObject("Roads");
        roadsParent.transform.SetParent(cityRoot.transform);

        // 셀 타입별로 바닥 타일 생성 (성능을 위해 같은 타입끼리 합치기)
        // 간단 버전: 행별로 같은 타입 연속 셀을 하나의 큐브로 합침

        for (int z = 0; z < gridHeight; z++)
        {
            int x = 0;
            while (x < gridWidth)
            {
                VegasCellType type = cellTypes[x, z];
                if (type == VegasCellType.Empty || type == VegasCellType.Building)
                {
                    x++;
                    continue;
                }

                // 같은 타입의 연속 셀 찾기
                int startX = x;
                while (x < gridWidth && cellTypes[x, z] == type)
                    x++;

                int count = x - startX;
                float segW = count * cellSize;
                float posX = startX * cellSize + segW / 2;
                float posZ = z * cellSize + cellSize / 2;

                Material mat = type switch
                {
                    VegasCellType.Road => VegasProceduralBuilding.RoadMat,
                    VegasCellType.Sidewalk => VegasProceduralBuilding.SidewalkMat,
                    VegasCellType.Crosswalk => VegasProceduralBuilding.CrosswalkMat,
                    _ => VegasProceduralBuilding.RoadMat
                };

                float height = type == VegasCellType.Sidewalk ? 0.15f : 0.02f;
                float y = height / 2;

                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = $"{type}_{startX}_{z}";
                tile.transform.SetParent(roadsParent.transform);
                tile.transform.localPosition = new Vector3(posX, y, posZ);
                tile.transform.localScale = new Vector3(segW, height, cellSize);
                tile.GetComponent<Renderer>().material = mat;
                // 도로는 Static 마킹 (라이트맵 등)
                tile.isStatic = true;
            }
        }

        // 도로 중앙선 (메인 스트립)
        float stripCenterX = (mainStripColumn + mainStripWidth / 2f) * cellSize;
        float totalZ = gridHeight * cellSize;
        GameObject centerLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
        centerLine.name = "CenterLine";
        centerLine.transform.SetParent(roadsParent.transform);
        centerLine.transform.localPosition = new Vector3(stripCenterX, 0.03f, totalZ / 2);
        centerLine.transform.localScale = new Vector3(0.15f, 0.01f, totalZ);

        Material yellowLine = VegasProceduralBuilding.CreateNeonMaterial(new Color(1f, 0.85f, 0.1f), 0.3f);
        centerLine.GetComponent<Renderer>().material = yellowLine;
    }

    /// <summary>
    /// 건물 인스턴스 생성
    /// </summary>
    private void BuildBuildings()
    {
        GameObject buildingsParent = new GameObject("Buildings");
        buildingsParent.transform.SetParent(cityRoot.transform);

        foreach (var bp in currentMapData.buildings)
        {
            Vector2Int gridSize = new Vector2Int(bp.sizeX, bp.sizeZ);
            GameObject building = VegasProceduralBuilding.CreateBuilding(
                bp.buildingType, gridSize, bp.floors, cellSize, bp.colorIndex);

            building.transform.SetParent(buildingsParent.transform);
            building.transform.localPosition = new Vector3(
                bp.gridX * cellSize,
                0,
                bp.gridZ * cellSize
            );

            if (bp.rotation != 0)
            {
                // 회전 시 피봇 보정
                float w = bp.sizeX * cellSize;
                float d = bp.sizeZ * cellSize;
                building.transform.localPosition += new Vector3(w / 2, 0, d / 2);
                building.transform.Rotate(0, bp.rotation, 0);
                building.transform.localPosition -= building.transform.rotation * new Vector3(w / 2, 0, d / 2);
            }

            generatedBuildings.Add(building);
        }
    }

    /// <summary>
    /// 가로등 배치 (스트립 양쪽 인도)
    /// </summary>
    private void BuildStreetLights()
    {
        GameObject lightsParent = new GameObject("StreetLights");
        lightsParent.transform.SetParent(cityRoot.transform);

        Color[] lightColors = {
            new Color(1f, 0.7f, 0.3f),     // 따뜻한 골드
            new Color(0.4f, 1f, 0.95f),     // 시안
            new Color(1f, 0.3f, 0.6f),      // 핑크
            new Color(0.3f, 0.7f, 1f),      // 블루
        };

        int lightInterval = 5; // 5셀마다 가로등
        int leftSW = mainStripColumn - 1;
        int rightSW = mainStripColumn + mainStripWidth;

        for (int z = 2; z < gridHeight - 2; z += lightInterval)
        {
            Color c = lightColors[z / lightInterval % lightColors.Length];

            // 좌측
            if (leftSW >= 0)
            {
                GameObject light = VegasProceduralBuilding.CreateStreetLight(c);
                light.transform.SetParent(lightsParent.transform);
                light.transform.localPosition = new Vector3(leftSW * cellSize + cellSize / 2, 0, z * cellSize);
            }

            // 우측
            if (rightSW < gridWidth)
            {
                GameObject light = VegasProceduralBuilding.CreateStreetLight(c);
                light.transform.SetParent(lightsParent.transform);
                light.transform.localPosition = new Vector3(rightSW * cellSize + cellSize / 2, 0, z * cellSize);
            }
        }
    }

    /// <summary>
    /// 플레이어 스폰 위치 설정 (스트립 중앙 인도) — 캐릭터 컨트롤러 보정 + 지면 스냅
    /// </summary>
    private void SetupPlayerSpawn()
    {
        VillagePlayerController player = FindAnyObjectByType<VillagePlayerController>();
        if (player == null) return;

        // CharacterController 보정 — VRM은 피벗이 발 위치라 center.y가 height/2여야 콜라이더 바닥이 발에 정확히 맞음
        var cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            float h = Mathf.Max(cc.height, 1.6f);
            cc.height = h;
            cc.center = new Vector3(0, h * 0.5f, 0);     // 콜라이더 바닥이 발에 위치
            cc.skinWidth = Mathf.Max(cc.skinWidth, 0.05f);
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;
            cc.minMoveDistance = 0f;                      // 매우 작은 움직임도 적용 (이동 정확도)
        }

        // 스트립 옆 인도 위 스폰 좌표
        float spawnX = (mainStripColumn - 1) * cellSize + cellSize / 2;
        float spawnZ = 5 * cellSize;

        // 지면 raycast — 인도/도로/CityGround 어느 쪽에 있든 정확히 그 위로 스냅
        Vector3 spawn = new Vector3(spawnX, 50f, spawnZ);
        if (Physics.Raycast(spawn, Vector3.down, out var hit, 100f))
        {
            spawn.y = hit.point.y + 0.02f; // skin 보정 약간
        }
        else
        {
            spawn.y = 0.2f;
        }

        // CharacterController는 enable 상태일 때 transform.position 직접 설정 못함 → 잠깐 끄고 위치 변경
        if (cc != null) cc.enabled = false;
        player.transform.position = spawn;
        if (cc != null) cc.enabled = true;

        // 낙사/맵 이탈 시 자동 리스폰 컴포넌트 부착 (없을 때만)
        if (player.GetComponent<VegasPlayerRespawner>() == null)
        {
            player.gameObject.AddComponent<VegasPlayerRespawner>();
        }

        Debug.Log($"[VegasCity] 플레이어 스폰 (지면 스냅): {spawn}");
    }

    // ──────────────────────────────────────────
    // 저장 / 로드
    // ──────────────────────────────────────────

    /// <summary>
    /// 현재 맵을 JSON으로 저장
    /// </summary>
    public void SaveToPlayerPrefs()
    {
        if (currentMapData == null) return;
        string json = JsonUtility.ToJson(currentMapData);
        PlayerPrefs.SetString("VegasMapData", json);
        PlayerPrefs.Save();
        Debug.Log("[VegasCity] 맵 저장 완료");
    }

    /// <summary>
    /// JSON에서 맵 로드
    /// </summary>
    public void LoadFromJson(string json)
    {
        try
        {
            VegasMapData data = JsonUtility.FromJson<VegasMapData>(json);
            if (data != null && data.buildings.Count > 0)
            {
                BuildFromData(data);
                Debug.Log($"[VegasCity] 맵 로드 완료: {data.mapName}, 건물 {data.buildings.Count}개");
            }
            else
            {
                Debug.LogWarning("[VegasCity] 저장된 맵 데이터 없음, 기본 생성");
                GenerateDefaultCity();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VegasCity] 맵 로드 실패: {e.Message}");
            GenerateDefaultCity();
        }
    }

    /// <summary>
    /// 건물 추가 (에디터용)
    /// </summary>
    public bool AddBuilding(VegasMapData.BuildingPlacement placement)
    {
        // 해당 위치에 건물 배치 가능한지 확인
        for (int x = placement.gridX; x < placement.gridX + placement.sizeX; x++)
        {
            for (int z = placement.gridZ; z < placement.gridZ + placement.sizeZ; z++)
            {
                if (x < 0 || x >= gridWidth || z < 0 || z >= gridHeight)
                    return false;
                if (cellTypes[x, z] != VegasCellType.Empty)
                    return false;
            }
        }

        currentMapData.buildings.Add(placement);

        // 셀 타입 업데이트
        for (int x = placement.gridX; x < placement.gridX + placement.sizeX; x++)
            for (int z = placement.gridZ; z < placement.gridZ + placement.sizeZ; z++)
                cellTypes[x, z] = VegasCellType.Building;

        // 3D 건물 생성
        GameObject building = VegasProceduralBuilding.CreateBuilding(
            placement.buildingType,
            new Vector2Int(placement.sizeX, placement.sizeZ),
            placement.floors, cellSize, placement.colorIndex);

        building.transform.SetParent(cityRoot.transform.Find("Buildings"));
        building.transform.localPosition = new Vector3(
            placement.gridX * cellSize, 0, placement.gridZ * cellSize);

        generatedBuildings.Add(building);
        return true;
    }

    /// <summary>
    /// 건물 제거 (에디터용)
    /// </summary>
    public bool RemoveBuilding(int gridX, int gridZ)
    {
        for (int i = currentMapData.buildings.Count - 1; i >= 0; i--)
        {
            var bp = currentMapData.buildings[i];
            if (gridX >= bp.gridX && gridX < bp.gridX + bp.sizeX &&
                gridZ >= bp.gridZ && gridZ < bp.gridZ + bp.sizeZ)
            {
                // 셀 해제
                for (int x = bp.gridX; x < bp.gridX + bp.sizeX; x++)
                    for (int z = bp.gridZ; z < bp.gridZ + bp.sizeZ; z++)
                        if (x >= 0 && x < gridWidth && z >= 0 && z < gridHeight)
                            cellTypes[x, z] = VegasCellType.Empty;

                currentMapData.buildings.RemoveAt(i);

                // 3D 오브젝트 제거
                if (i < generatedBuildings.Count)
                {
                    Destroy(generatedBuildings[i]);
                    generatedBuildings.RemoveAt(i);
                }

                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 월드 좌표 → 그리드 좌표
    /// </summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position;
        int x = Mathf.FloorToInt(local.x / cellSize);
        int z = Mathf.FloorToInt(local.z / cellSize);
        return new Vector2Int(
            Mathf.Clamp(x, 0, gridWidth - 1),
            Mathf.Clamp(z, 0, gridHeight - 1)
        );
    }

    /// <summary>
    /// 그리드 좌표 → 월드 좌표
    /// </summary>
    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        return transform.position + new Vector3(
            gridPos.x * cellSize,
            0,
            gridPos.y * cellSize
        );
    }
}
