using UnityEngine;

/// <summary>
/// 프로시저럴 건물 생성기
/// - 큐브 프리미티브 조합으로 라스베가스 스타일 건물 생성
/// - 네온사인, 창문, LED 스트립 자동 생성
/// - 건물 타입에 따라 다른 비주얼 스타일
/// </summary>
public static class VegasProceduralBuilding
{
    // ──────────────────────────────────────────
    // 머티리얼 유틸리티 (URP 호환)
    // ──────────────────────────────────────────

    private static Material _baseMaterial;

    /// <summary>
    /// URP에서 확실하게 동작하는 기본 머티리얼 얻기
    /// Unity 프리미티브의 기본 머티리얼을 복제하는 방식 (URP 렌더파이프라인 설정 보존)
    /// </summary>
    private static Material GetBaseMaterial()
    {
        if (_baseMaterial != null) return _baseMaterial;

        // Unity 프리미티브에서 기본 머티리얼 복제 (URP 설정 포함)
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Renderer tempRenderer = temp.GetComponent<Renderer>();
        if (tempRenderer != null && tempRenderer.sharedMaterial != null)
        {
            _baseMaterial = new Material(tempRenderer.sharedMaterial);
            _baseMaterial.name = "VegasBase";
            Debug.Log($"[VegasBuilding] 기본 머티리얼 복제 완료 (셰이더: {_baseMaterial.shader.name})");
        }
        Object.DestroyImmediate(temp);

        return _baseMaterial;
    }

    /// <summary>
    /// URP 호환 머티리얼 생성 (기본 머티리얼 복제 후 색상 변경)
    /// </summary>
    public static Material CreateMaterial(Color color, float smoothness = 0.5f)
    {
        Material baseMat = GetBaseMaterial();
        Material mat = (baseMat != null) ? new Material(baseMat) : new Material(Shader.Find("Standard"));
        mat.color = color;
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", smoothness);
        return mat;
    }

    /// <summary>
    /// SafeGetShader 하위호환 — 기존 코드에서 호출하는 곳용
    /// </summary>
    public static Shader SafeGetShader()
    {
        Material baseMat = GetBaseMaterial();
        return baseMat != null ? baseMat.shader : Shader.Find("Standard");
    }

    // ──────────────────────────────────────────
    // 머티리얼 캐시 (성능)
    // ──────────────────────────────────────────

    private static Material _buildingMat;
    private static Material _windowMat;
    private static Material _roofMat;
    private static Material _roadMat;
    private static Material _sidewalkMat;
    private static Material _crosswalkMat;

    private static Material BuildingMat
    {
        get
        {
            if (_buildingMat == null)
                _buildingMat = CreateMaterial(new Color(0.65f, 0.62f, 0.78f), 0.2f); // 카툰풍 밝은 톤
            return _buildingMat;
        }
    }

    private static Material WindowMat
    {
        get
        {
            if (_windowMat == null)
            {
                // 알파 제거 — 카툰풍 불투명 + 살짝 자체 발광으로 유리 느낌
                _windowMat = CreateNeonMaterial(new Color(0.55f, 0.82f, 1f), 0.4f);
            }
            return _windowMat;
        }
    }

    private static Material RoofMat
    {
        get
        {
            if (_roofMat == null)
                _roofMat = CreateMaterial(new Color(0.35f, 0.35f, 0.45f), 0.3f);
            return _roofMat;
        }
    }

    public static Material RoadMat
    {
        get
        {
            if (_roadMat == null)
                _roadMat = CreateMaterial(new Color(0.32f, 0.32f, 0.36f), 0.1f); // 카툰풍 밝은 도로
            return _roadMat;
        }
    }

    public static Material SidewalkMat
    {
        get
        {
            if (_sidewalkMat == null)
                _sidewalkMat = CreateMaterial(new Color(0.75f, 0.72f, 0.65f), 0.1f); // 밝은 베이지
            return _sidewalkMat;
        }
    }

    public static Material CrosswalkMat
    {
        get
        {
            if (_crosswalkMat == null)
                _crosswalkMat = CreateMaterial(new Color(1f, 1f, 0.95f), 0.1f);
            return _crosswalkMat;
        }
    }

    /// <summary>
    /// 네온 발광 머티리얼 생성
    /// </summary>
    public static Material CreateNeonMaterial(Color color, float intensity = 2f)
    {
        Material mat = CreateMaterial(color, 0.8f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * intensity);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return mat;
    }

    /// <summary>
    /// 건물 머티리얼 (지정 색상)
    /// </summary>
    private static Material CreateBuildingMaterial(Color color)
    {
        return CreateMaterial(color, 0.3f);
    }

    // ──────────────────────────────────────────
    // 건물 생성
    // ──────────────────────────────────────────

    /// <summary>
    /// 건물 타입에 따라 프로시저럴 건물 생성
    /// </summary>
    public static GameObject CreateBuilding(VegasBuildingType type, Vector2Int gridSize,
        int floors, float cellSize, int colorIndex = 0)
    {
        return type switch
        {
            VegasBuildingType.Casino => CreateCasino(gridSize, floors, cellSize, colorIndex),
            VegasBuildingType.Hotel => CreateHotel(gridSize, floors, cellSize, colorIndex),
            VegasBuildingType.Shop => CreateShop(gridSize, floors, cellSize, colorIndex),
            VegasBuildingType.Bar => CreateBar(gridSize, floors, cellSize, colorIndex),
            VegasBuildingType.Restaurant => CreateShop(gridSize, floors, cellSize, colorIndex), // 상점과 유사
            VegasBuildingType.Billboard => CreateBillboard(gridSize, cellSize, colorIndex),
            VegasBuildingType.Fountain => CreateFountain(gridSize, cellSize, colorIndex),
            VegasBuildingType.ParkingLot => CreateParkingLot(gridSize, cellSize),
            _ => CreateGenericBuilding(gridSize, floors, cellSize, colorIndex)
        };
    }

    /// <summary>
    /// 카지노 — 크고 화려, 네온 간판 + 입구 장식
    /// </summary>
    private static GameObject CreateCasino(Vector2Int size, int floors, float cellSize, int colorIndex)
    {
        GameObject root = new GameObject("Casino");
        float w = size.x * cellSize;
        float d = size.y * cellSize;
        float floorH = 4.5f; // 카지노는 층고 높음
        float h = floors * floorH;
        Color[] neonColors = VegasBuildingConfig.GetNeonColors(VegasBuildingType.Casino, colorIndex);

        // 건물 색상 — 카툰풍 밝은 톤 + 네온 컬러 액센트
        Color bodyColor = new Color(
            Mathf.Clamp01(0.55f + neonColors[0].r * 0.25f),
            Mathf.Clamp01(0.55f + neonColors[0].g * 0.25f),
            Mathf.Clamp01(0.60f + neonColors[0].b * 0.25f));

        // 메인 건물 바디
        GameObject body = CreateBox(root.transform, "Body",
            new Vector3(w, h, d),
            new Vector3(w / 2, h / 2, d / 2),
            CreateBuildingMaterial(bodyColor));

        // 창문 그리드
        AddWindowGrid(root.transform, w, d, h, floorH, neonColors[2]);

        // 지붕 장식 — 네온 테두리
        float ledH = 0.3f;
        AddNeonEdge(root.transform, "RoofNeon", w, d, h, ledH, neonColors[0], 2.5f);

        // 각 층 사이 네온 라인
        for (int f = 1; f < floors; f++)
        {
            float y = f * floorH;
            AddNeonStrip(root.transform, $"FloorLine_{f}", w, d, y, 0.08f, neonColors[1], 1.5f);
        }

        // 전면 대형 간판
        float signW = w * 0.7f;
        float signH = floorH * 0.6f;
        AddNeonSign(root.transform, "MainSign",
            new Vector3(w / 2, h + signH / 2 + 0.5f, 0.1f),
            new Vector3(signW, signH, 0.3f),
            neonColors[0], 3f);

        // 입구 캐노피
        float canopyW = w * 0.4f;
        float canopyD = 2f;
        AddCanopy(root.transform, canopyW, canopyD, 3.5f, neonColors[0]);

        // 기둥 장식 (입구 양쪽)
        AddPillar(root.transform, new Vector3(w / 2 - canopyW / 2 - 0.3f, 0, -0.5f), 4f, 0.3f, neonColors[0]);
        AddPillar(root.transform, new Vector3(w / 2 + canopyW / 2 + 0.3f, 0, -0.5f), 4f, 0.3f, neonColors[0]);

        return root;
    }

    /// <summary>
    /// 호텔 — 고층, 모던, 유리 느낌
    /// </summary>
    private static GameObject CreateHotel(Vector2Int size, int floors, float cellSize, int colorIndex)
    {
        GameObject root = new GameObject("Hotel");
        float w = size.x * cellSize;
        float d = size.y * cellSize;
        float floorH = 3.2f;
        float h = floors * floorH;
        Color[] neonColors = VegasBuildingConfig.GetNeonColors(VegasBuildingType.Hotel, colorIndex);

        // 호텔 바디 — 카툰풍 밝은 파스텔 톤
        Color bodyColor = new Color(0.85f, 0.88f, 0.95f);
        CreateBox(root.transform, "Body",
            new Vector3(w, h, d),
            new Vector3(w / 2, h / 2, d / 2),
            CreateBuildingMaterial(bodyColor));

        // 유리 창문 (호텔은 큰 창)
        AddHotelWindows(root.transform, w, d, h, floorH, floors, neonColors[0]);

        // 꼭대기 네온 라인
        AddNeonEdge(root.transform, "TopNeon", w, d, h, 0.2f, neonColors[0], 1.5f);

        // 옥상 구조물
        float penthouseW = w * 0.5f;
        float penthouseD = d * 0.5f;
        float penthouseH = floorH * 1.5f;
        CreateBox(root.transform, "Penthouse",
            new Vector3(penthouseW, penthouseH, penthouseD),
            new Vector3(w / 2, h + penthouseH / 2, d / 2),
            CreateBuildingMaterial(new Color(0.1f, 0.12f, 0.18f)));
        AddNeonEdge(root.transform, "PenthouseNeon", penthouseW, penthouseD,
            h + penthouseH, 0.15f, neonColors[1], 1.2f,
            new Vector3((w - penthouseW) / 2, 0, (d - penthouseD) / 2));

        // 입구 캐노피
        AddCanopy(root.transform, w * 0.5f, 2.5f, 3.5f, neonColors[0]);

        return root;
    }

    /// <summary>
    /// 상점 — 소형, 간판 중심
    /// </summary>
    private static GameObject CreateShop(Vector2Int size, int floors, float cellSize, int colorIndex)
    {
        GameObject root = new GameObject("Shop");
        float w = size.x * cellSize;
        float d = size.y * cellSize;
        float floorH = 3.5f;
        float h = floors * floorH;
        Color[] neonColors = VegasBuildingConfig.GetNeonColors(VegasBuildingType.Shop, colorIndex);

        // 카툰풍 알록달록 — 컬러 인덱스로 다양화
        Color[] shopBodyColors = {
            new Color(1f, 0.65f, 0.4f),    // 오렌지
            new Color(1f, 0.85f, 0.4f),    // 옐로우
            new Color(0.6f, 0.85f, 1f),    // 라이트블루
            new Color(1f, 0.6f, 0.75f),    // 핑크
            new Color(0.7f, 0.95f, 0.7f),  // 라이트그린
        };
        Color bodyColor = shopBodyColors[Mathf.Abs(colorIndex) % shopBodyColors.Length];
        CreateBox(root.transform, "Body",
            new Vector3(w, h, d),
            new Vector3(w / 2, h / 2, d / 2),
            CreateBuildingMaterial(bodyColor));

        // 전면 네온 간판 (큼직하게)
        float signW = w * 0.8f;
        float signH = 1.2f;
        AddNeonSign(root.transform, "ShopSign",
            new Vector3(w / 2, h - signH / 2 - 0.3f, -0.05f),
            new Vector3(signW, signH, 0.15f),
            neonColors[0], 2f);

        // 1층 쇼윈도
        float windowY = 1.2f;
        float windowH = 2f;
        CreateBox(root.transform, "Shopfront",
            new Vector3(w * 0.85f, windowH, 0.05f),
            new Vector3(w / 2, windowY + windowH / 2, 0f),
            WindowMat);

        // 어닝
        AddCanopy(root.transform, w * 0.9f, 1.5f, h, neonColors[0], 0.08f);

        // 하단 네온 스트립
        AddNeonStrip(root.transform, "BottomNeon", w, d, 0.05f, 0.06f, neonColors[1], 1.8f);

        return root;
    }

    /// <summary>
    /// 바/클럽 — 네온 강조, 화려한 입구
    /// </summary>
    private static GameObject CreateBar(Vector2Int size, int floors, float cellSize, int colorIndex)
    {
        GameObject root = new GameObject("Bar");
        float w = size.x * cellSize;
        float d = size.y * cellSize;
        float floorH = 4f;
        float h = floors * floorH;
        Color[] neonColors = VegasBuildingConfig.GetNeonColors(VegasBuildingType.Bar, colorIndex);

        // 카툰풍 — 바/클럽도 깔끔한 어두운 톤 (완전히 어두우면 칙칙)
        Color bodyColor = new Color(0.35f, 0.28f, 0.45f);
        CreateBox(root.transform, "Body",
            new Vector3(w, h, d),
            new Vector3(w / 2, h / 2, d / 2),
            CreateBuildingMaterial(bodyColor));

        // 전면 전체를 네온으로 장식
        // 수직 네온 라인들
        int lineCount = Mathf.Max(2, (int)(w / 1.5f));
        for (int i = 0; i < lineCount; i++)
        {
            float x = (i + 0.5f) * (w / lineCount);
            CreateBox(root.transform, $"VLine_{i}",
                new Vector3(0.06f, h * 0.8f, 0.05f),
                new Vector3(x, h * 0.5f, -0.03f),
                CreateNeonMaterial(neonColors[i % neonColors.Length], 2f));
        }

        // 수평 네온 라인들
        for (int f = 0; f <= floors; f++)
        {
            float y = f * floorH;
            AddNeonStrip(root.transform, $"HLine_{f}", w, d, y, 0.08f, neonColors[f % neonColors.Length], 2f);
        }

        // 입구 네온 아치
        float archW = Mathf.Min(w * 0.5f, 3f);
        float archH = 3.5f;
        // 좌 기둥
        CreateBox(root.transform, "ArchL",
            new Vector3(0.15f, archH, 0.15f),
            new Vector3(w / 2 - archW / 2, archH / 2, -0.3f),
            CreateNeonMaterial(neonColors[0], 3f));
        // 우 기둥
        CreateBox(root.transform, "ArchR",
            new Vector3(0.15f, archH, 0.15f),
            new Vector3(w / 2 + archW / 2, archH / 2, -0.3f),
            CreateNeonMaterial(neonColors[0], 3f));
        // 상단
        CreateBox(root.transform, "ArchTop",
            new Vector3(archW + 0.15f, 0.15f, 0.15f),
            new Vector3(w / 2, archH, -0.3f),
            CreateNeonMaterial(neonColors[0], 3f));

        return root;
    }

    /// <summary>
    /// 빌보드 — 키 큰 간판
    /// </summary>
    private static GameObject CreateBillboard(Vector2Int size, float cellSize, int colorIndex)
    {
        GameObject root = new GameObject("Billboard");
        float w = size.x * cellSize;
        Color[] neonColors = VegasBuildingConfig.GetNeonColors(VegasBuildingType.Billboard, colorIndex);

        float poleH = 6f;
        float signW = w * 0.9f;
        float signH = 3f;

        // 지지대
        CreateBox(root.transform, "Pole",
            new Vector3(0.3f, poleH, 0.3f),
            new Vector3(w / 2, poleH / 2, w / 2),
            CreateBuildingMaterial(new Color(0.3f, 0.3f, 0.3f)));

        // 간판 패널
        AddNeonSign(root.transform, "Panel",
            new Vector3(w / 2, poleH + signH / 2, w / 2),
            new Vector3(signW, signH, 0.2f),
            neonColors[0], 2.5f);

        // 테두리 네온
        // 상
        CreateBox(root.transform, "BorderT",
            new Vector3(signW + 0.2f, 0.1f, 0.1f),
            new Vector3(w / 2, poleH + signH, w / 2),
            CreateNeonMaterial(neonColors[1], 2f));
        // 하
        CreateBox(root.transform, "BorderB",
            new Vector3(signW + 0.2f, 0.1f, 0.1f),
            new Vector3(w / 2, poleH, w / 2),
            CreateNeonMaterial(neonColors[1], 2f));

        return root;
    }

    /// <summary>
    /// 분수 — 원형 장식
    /// </summary>
    private static GameObject CreateFountain(Vector2Int size, float cellSize, int colorIndex)
    {
        GameObject root = new GameObject("Fountain");
        float w = size.x * cellSize;
        float r = w * 0.4f;
        Color[] neonColors = VegasBuildingConfig.GetNeonColors(VegasBuildingType.Fountain, colorIndex);

        // 분수 베이스 (원통)
        GameObject basin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        basin.name = "Basin";
        basin.transform.SetParent(root.transform);
        basin.transform.localPosition = new Vector3(w / 2, 0.4f, w / 2);
        basin.transform.localScale = new Vector3(r * 2, 0.4f, r * 2);
        basin.GetComponent<Renderer>().material = CreateBuildingMaterial(new Color(0.5f, 0.5f, 0.55f));

        // 중앙 기둥
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "Pillar";
        pillar.transform.SetParent(root.transform);
        pillar.transform.localPosition = new Vector3(w / 2, 1.5f, w / 2);
        pillar.transform.localScale = new Vector3(0.4f, 1.5f, 0.4f);
        pillar.GetComponent<Renderer>().material = CreateBuildingMaterial(new Color(0.6f, 0.6f, 0.65f));

        // 물 (반투명 실린더)
        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        water.name = "Water";
        water.transform.SetParent(root.transform);
        water.transform.localPosition = new Vector3(w / 2, 0.35f, w / 2);
        water.transform.localScale = new Vector3(r * 1.8f, 0.05f, r * 1.8f);
        Material waterMat = new Material(SafeGetShader());
        waterMat.color = new Color(0.1f, 0.4f, 0.8f, 0.5f);
        waterMat.SetFloat("_Surface", 1);
        waterMat.SetOverrideTag("RenderType", "Transparent");
        waterMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        waterMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        waterMat.renderQueue = 3000;
        water.GetComponent<Renderer>().material = waterMat;

        // 네온 링 (베이스 테두리)
        AddNeonRing(root.transform, new Vector3(w / 2, 0.82f, w / 2), r + 0.1f, neonColors[0], 1.5f);

        return root;
    }

    /// <summary>
    /// 주차장 — 바닥만 표시
    /// </summary>
    private static GameObject CreateParkingLot(Vector2Int size, float cellSize)
    {
        GameObject root = new GameObject("ParkingLot");
        float w = size.x * cellSize;
        float d = size.y * cellSize;

        // 바닥
        CreateBox(root.transform, "Floor",
            new Vector3(w, 0.05f, d),
            new Vector3(w / 2, 0.025f, d / 2),
            CreateBuildingMaterial(new Color(0.2f, 0.2f, 0.22f)));

        // 주차 라인
        int spots = (int)(w / 2.5f);
        Material lineMat = CreateBuildingMaterial(new Color(0.9f, 0.9f, 0.8f));
        for (int i = 0; i <= spots; i++)
        {
            float x = i * (w / spots);
            CreateBox(root.transform, $"Line_{i}",
                new Vector3(0.08f, 0.02f, d * 0.4f),
                new Vector3(x, 0.06f, d * 0.3f),
                lineMat);
        }

        return root;
    }

    /// <summary>
    /// 기본 건물
    /// </summary>
    private static GameObject CreateGenericBuilding(Vector2Int size, int floors, float cellSize, int colorIndex)
    {
        GameObject root = new GameObject("Building");
        float w = size.x * cellSize;
        float d = size.y * cellSize;
        float floorH = 3.5f;
        float h = floors * floorH;
        Color[] neonColors = VegasBuildingConfig.GetNeonColors(VegasBuildingType.Shop, colorIndex);

        CreateBox(root.transform, "Body",
            new Vector3(w, h, d),
            new Vector3(w / 2, h / 2, d / 2),
            BuildingMat);

        AddWindowGrid(root.transform, w, d, h, floorH, Color.white);
        AddNeonEdge(root.transform, "RoofNeon", w, d, h, 0.15f, neonColors[0], 1f);

        return root;
    }

    // ──────────────────────────────────────────
    // 헬퍼: 기본 도형
    // ──────────────────────────────────────────

    private static GameObject CreateBox(Transform parent, string name, Vector3 size, Vector3 position, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.localPosition = position;
        obj.transform.localScale = size;
        obj.GetComponent<Renderer>().material = mat;

        // 건물 바디가 아닌 장식은 콜라이더 제거 (성능)
        if (name != "Body" && name != "Floor")
        {
            Object.Destroy(obj.GetComponent<Collider>());
        }

        return obj;
    }

    // ──────────────────────────────────────────
    // 헬퍼: 장식 요소
    // ──────────────────────────────────────────

    /// <summary>
    /// 창문 그리드 (전면 + 양 측면)
    /// </summary>
    private static void AddWindowGrid(Transform parent, float w, float d, float h, float floorH, Color lightColor)
    {
        int floorsCount = Mathf.FloorToInt(h / floorH);
        float windowW = 1f;
        float windowH = 1.5f;
        float windowGap = 0.5f;
        const float OUT = 0.06f;     // 면 밖으로 돌출 거리 (z-fighting 방지)
        const float winDepth = 0.08f; // 창문 두께

        Material litWindowMat = CreateNeonMaterial(lightColor, 0.3f);

        // 전면 (z = 0 면 바깥쪽으로 돌출)
        int countX = Mathf.Max(1, (int)((w - 1f) / (windowW + windowGap)));
        for (int f = 0; f < floorsCount; f++)
        {
            float y = f * floorH + floorH * 0.4f + windowH / 2;
            for (int i = 0; i < countX; i++)
            {
                float x = (i + 0.5f) * (w / countX);
                CreateBox(parent, $"WinF_{f}_{i}",
                    new Vector3(windowW * 0.6f, windowH, winDepth),
                    new Vector3(x, y, -OUT),
                    litWindowMat);
            }
        }

        // 좌측 (x = 0 면 바깥쪽)
        int countZ = Mathf.Max(1, (int)((d - 1f) / (windowW + windowGap)));
        for (int f = 0; f < floorsCount; f++)
        {
            float y = f * floorH + floorH * 0.4f + windowH / 2;
            for (int i = 0; i < countZ; i++)
            {
                float z = (i + 0.5f) * (d / countZ);
                CreateBox(parent, $"WinL_{f}_{i}",
                    new Vector3(winDepth, windowH, windowW * 0.6f),
                    new Vector3(-OUT, y, z),
                    litWindowMat);
            }
        }

        // 우측 (x = w 면 바깥쪽)
        for (int f = 0; f < floorsCount; f++)
        {
            float y = f * floorH + floorH * 0.4f + windowH / 2;
            for (int i = 0; i < countZ; i++)
            {
                float z = (i + 0.5f) * (d / countZ);
                CreateBox(parent, $"WinR_{f}_{i}",
                    new Vector3(winDepth, windowH, windowW * 0.6f),
                    new Vector3(w + OUT, y, z),
                    litWindowMat);
            }
        }
    }

    /// <summary>
    /// 호텔용 큰 창문
    /// </summary>
    private static void AddHotelWindows(Transform parent, float w, float d, float h, float floorH, int floors, Color accentColor)
    {
        Material glassMat = WindowMat;
        Material frameMat = CreateNeonMaterial(accentColor, 0.5f);
        const float OUT = 0.06f;      // 면 밖으로 돌출
        const float glassDepth = 0.08f;

        int countX = Mathf.Max(2, (int)(w / 2.5f));
        float windowW = (w - 0.6f) / countX - 0.2f;
        float windowH = floorH * 0.7f;

        for (int f = 0; f < floors; f++)
        {
            float y = f * floorH + floorH * 0.3f + windowH / 2;
            for (int i = 0; i < countX; i++)
            {
                float x = 0.3f + (i + 0.5f) * ((w - 0.6f) / countX);
                // 유리 — 건물 면 바깥쪽으로 돌출시켜 z-fighting 회피
                CreateBox(parent, $"Glass_{f}_{i}",
                    new Vector3(windowW, windowH, glassDepth),
                    new Vector3(x, y, -OUT),
                    glassMat);
            }
        }
    }

    /// <summary>
    /// 지붕 네온 테두리 (상단 4변)
    /// </summary>
    private static void AddNeonEdge(Transform parent, string name, float w, float d, float h,
        float thickness, Color color, float intensity, Vector3 offset = default)
    {
        Material mat = CreateNeonMaterial(color, intensity);

        // 전 (z=0)
        CreateBox(parent, $"{name}_F", new Vector3(w, thickness, thickness),
            new Vector3(w / 2 + offset.x, h + offset.y, offset.z), mat);
        // 후 (z=d)
        CreateBox(parent, $"{name}_B", new Vector3(w, thickness, thickness),
            new Vector3(w / 2 + offset.x, h + offset.y, d + offset.z), mat);
        // 좌 (x=0)
        CreateBox(parent, $"{name}_L", new Vector3(thickness, thickness, d),
            new Vector3(offset.x, h + offset.y, d / 2 + offset.z), mat);
        // 우 (x=w)
        CreateBox(parent, $"{name}_R", new Vector3(thickness, thickness, d),
            new Vector3(w + offset.x, h + offset.y, d / 2 + offset.z), mat);
    }

    /// <summary>
    /// 수평 네온 스트립 (건물 전면 + 측면)
    /// </summary>
    private static void AddNeonStrip(Transform parent, string name, float w, float d, float y,
        float thickness, Color color, float intensity)
    {
        Material mat = CreateNeonMaterial(color, intensity);

        // 전면
        CreateBox(parent, $"{name}_F",
            new Vector3(w, thickness, thickness),
            new Vector3(w / 2, y, 0), mat);
        // 좌측
        CreateBox(parent, $"{name}_L",
            new Vector3(thickness, thickness, d),
            new Vector3(0, y, d / 2), mat);
        // 우측
        CreateBox(parent, $"{name}_R",
            new Vector3(thickness, thickness, d),
            new Vector3(w, y, d / 2), mat);
    }

    /// <summary>
    /// 네온 간판 패널
    /// </summary>
    private static void AddNeonSign(Transform parent, string name, Vector3 pos, Vector3 size,
        Color color, float intensity)
    {
        // 배경판
        CreateBox(parent, $"{name}_BG",
            size,
            pos,
            CreateBuildingMaterial(new Color(0.05f, 0.05f, 0.08f)));

        // 발광면 (살짝 앞에)
        CreateBox(parent, $"{name}_Glow",
            new Vector3(size.x * 0.9f, size.y * 0.8f, 0.05f),
            pos + new Vector3(0, 0, -size.z / 2 - 0.03f),
            CreateNeonMaterial(color, intensity));
    }

    /// <summary>
    /// 캐노피 (입구 차양)
    /// </summary>
    private static void AddCanopy(Transform parent, float width, float depth, float height, Color color, float thickness = 0.15f)
    {
        float parentW = 0;
        // 부모의 Body에서 폭 추출
        Transform body = parent.Find("Body");
        if (body != null) parentW = body.localScale.x;

        float cx = parentW > 0 ? parentW / 2 : width / 2;

        CreateBox(parent, "Canopy",
            new Vector3(width, thickness, depth),
            new Vector3(cx, height, -depth / 2),
            CreateBuildingMaterial(new Color(0.15f, 0.12f, 0.12f)));

        // 캐노피 하단 네온
        CreateBox(parent, "CanopyNeon",
            new Vector3(width, 0.05f, 0.05f),
            new Vector3(cx, height - thickness / 2 - 0.03f, -depth),
            CreateNeonMaterial(color, 2f));
    }

    /// <summary>
    /// 네온 기둥
    /// </summary>
    private static void AddPillar(Transform parent, Vector3 pos, float height, float radius, Color color)
    {
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "NeonPillar";
        pillar.transform.SetParent(parent);
        pillar.transform.localPosition = pos + new Vector3(0, height / 2, 0);
        pillar.transform.localScale = new Vector3(radius, height / 2, radius);
        pillar.GetComponent<Renderer>().material = CreateNeonMaterial(color, 1.5f);
        Object.Destroy(pillar.GetComponent<Collider>());
    }

    /// <summary>
    /// 네온 링 (분수용)
    /// </summary>
    private static void AddNeonRing(Transform parent, Vector3 center, float radius, Color color, float intensity)
    {
        int segments = 16;
        Material mat = CreateNeonMaterial(color, intensity);

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            float nextAngle = (i + 1) * Mathf.PI * 2 / segments;
            Vector3 p1 = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            Vector3 p2 = center + new Vector3(Mathf.Cos(nextAngle) * radius, 0, Mathf.Sin(nextAngle) * radius);
            Vector3 mid = (p1 + p2) / 2;
            float segLen = Vector3.Distance(p1, p2);

            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = $"Ring_{i}";
            seg.transform.SetParent(parent);
            seg.transform.localPosition = mid;
            seg.transform.localScale = new Vector3(segLen, 0.08f, 0.08f);
            seg.transform.LookAt(p2);
            seg.GetComponent<Renderer>().material = mat;
            Object.Destroy(seg.GetComponent<Collider>());
        }
    }

    // ──────────────────────────────────────────
    // 도로 인프라
    // ──────────────────────────────────────────

    /// <summary>
    /// 가로등 생성
    /// </summary>
    public static GameObject CreateStreetLight(Color neonColor)
    {
        GameObject root = new GameObject("StreetLight");

        // 폴
        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Pole";
        pole.transform.SetParent(root.transform);
        pole.transform.localPosition = new Vector3(0, 3f, 0);
        pole.transform.localScale = new Vector3(0.12f, 3f, 0.12f);
        pole.GetComponent<Renderer>().material = CreateBuildingMaterial(new Color(0.25f, 0.25f, 0.28f));

        // 라이트 헤드
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "Head";
        head.transform.SetParent(root.transform);
        head.transform.localPosition = new Vector3(0, 6.2f, 0);
        head.transform.localScale = new Vector3(0.8f, 0.15f, 0.4f);
        head.GetComponent<Renderer>().material = CreateNeonMaterial(neonColor, 1.5f);

        // 실제 라이트 추가
        GameObject lightObj = new GameObject("PointLight");
        lightObj.transform.SetParent(root.transform);
        lightObj.transform.localPosition = new Vector3(0, 5.8f, 0);
        Light pointLight = lightObj.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = neonColor;
        pointLight.intensity = 1.5f;
        pointLight.range = 12f;
        pointLight.lightShadowCasterMode = LightShadowCasterMode.Default;
#if UNITY_EDITOR
        pointLight.lightmapBakeType = LightmapBakeType.Mixed; // Mixed 모드
#endif

        return root;
    }
}
