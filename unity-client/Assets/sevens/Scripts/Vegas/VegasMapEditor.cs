using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 인게임 라스베가스 맵 에디터
/// - 건물 팔레트에서 건물 선택
/// - 그리드에 터치/클릭으로 배치
/// - 기존 건물 제거
/// - 맵 저장/로드
///
/// 사용법: CyberpunkUIBootstrapper처럼 자동 생성됨
///         에디터 모드 ON/OFF는 HUD 버튼으로 토글
/// </summary>
public class VegasMapEditor : MonoBehaviour
{
    public static VegasMapEditor Instance { get; private set; }

    [Header("Editor Settings")]
    [SerializeField] private bool editorMode = false;
    [SerializeField] private VegasBuildingType selectedType = VegasBuildingType.Casino;
    [SerializeField] private int selectedFloors = 3;
    [SerializeField] private int selectedColorIndex = 0;

    // UI 요소
    private Canvas editorCanvas;
    private GameObject editorPanel;
    private GameObject palettePanel;
    private GameObject gridOverlay;
    private TextMeshProUGUI statusText;
    private Button editToggleButton;
    private TextMeshProUGUI editToggleLabel;

    // 그리드 표시
    private GameObject highlightCell;
    private Material highlightMat;
    private Material validMat;
    private Material invalidMat;

    // 상태
    private bool isEraseMode = false;
    private Vector2Int lastHighlightPos = new Vector2Int(-1, -1);
    private Camera mainCam;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        mainCam = Camera.main;
        BuildEditorUI();
        BuildGridHighlight();
        SetEditorMode(false);
    }

    // ──────────────────────────────────────────
    // UI 빌드
    // ──────────────────────────────────────────

    private void BuildEditorUI()
    {
        // 에디터 전용 캔버스
        GameObject canvasObj = new GameObject("EditorCanvas");
        canvasObj.transform.SetParent(transform);

        editorCanvas = canvasObj.AddComponent<Canvas>();
        editorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        editorCanvas.sortingOrder = 150; // HUD보다 위

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 에디터 토글 버튼 (우상단)
        BuildEditToggleButton();

        // 건물 팔레트 (하단)
        BuildPalette();

        // 상태 텍스트
        BuildStatusText();
    }

    private void BuildEditToggleButton()
    {
        GameObject btnObj = CyberpunkUITheme.CreateUIObject("EditToggle", editorCanvas.transform);
        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-16, -64);
        rt.sizeDelta = new Vector2(100, 36);

        Image bg = btnObj.AddComponent<Image>();
        bg.sprite = CyberpunkUITheme.CreatePanelSprite(32,
            new Color(0.04f, 0.04f, 0.12f, 0.9f),
            CyberpunkUITheme.NeonCyan, 2, 4);
        bg.type = Image.Type.Sliced;

        editToggleButton = btnObj.AddComponent<Button>();
        editToggleButton.targetGraphic = bg;
        editToggleButton.onClick.AddListener(ToggleEditorMode);

        // 라벨
        GameObject labelObj = CyberpunkUITheme.CreateUIObject("Label", btnObj.transform);
        RectTransform labelRt = labelObj.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        editToggleLabel = labelObj.AddComponent<TextMeshProUGUI>();
        editToggleLabel.text = "편집";
        editToggleLabel.fontSize = 14;
        editToggleLabel.color = CyberpunkUITheme.NeonCyan;
        editToggleLabel.alignment = TextAlignmentOptions.Center;
        editToggleLabel.fontStyle = FontStyles.Bold;
        editToggleLabel.raycastTarget = false;
    }

    private void BuildPalette()
    {
        // 팔레트 패널 (하단 중앙)
        palettePanel = CyberpunkUITheme.CreateUIObject("Palette", editorCanvas.transform);
        RectTransform palRt = palettePanel.GetComponent<RectTransform>();
        palRt.anchorMin = new Vector2(0.5f, 0);
        palRt.anchorMax = new Vector2(0.5f, 0);
        palRt.pivot = new Vector2(0.5f, 0);
        palRt.anchoredPosition = new Vector2(0, 10);
        palRt.sizeDelta = new Vector2(700, 100);

        // 팔레트 배경
        Image palBg = CyberpunkUITheme.ApplyPanelStyle(palettePanel,
            new Color(0.02f, 0.02f, 0.06f, 0.92f),
            new Color(CyberpunkUITheme.NeonBlue.r, CyberpunkUITheme.NeonBlue.g, CyberpunkUITheme.NeonBlue.b, 0.3f));

        // 건물 타입 버튼들
        VegasBuildingType[] types = {
            VegasBuildingType.Casino,
            VegasBuildingType.Hotel,
            VegasBuildingType.Shop,
            VegasBuildingType.Bar,
            VegasBuildingType.Restaurant,
            VegasBuildingType.Fountain,
            VegasBuildingType.Billboard
        };

        float btnW = 80f;
        float totalW = types.Length * (btnW + 8);
        float startX = -totalW / 2 + btnW / 2 + 4;

        for (int i = 0; i < types.Length; i++)
        {
            VegasBuildingType type = types[i];
            var spec = VegasBuildingConfig.GetSpec(type);
            Color[] colors = VegasBuildingConfig.GetNeonColors(type);

            GameObject btn = CyberpunkUITheme.CreateUIObject($"Btn_{type}", palettePanel.transform);
            RectTransform btnRt = btn.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.5f, 0.5f);
            btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.anchoredPosition = new Vector2(startX + i * (btnW + 8), 0);
            btnRt.sizeDelta = new Vector2(btnW, 70);

            // 버튼 배경 — 건물 컬러 힌트
            Image btnBg = btn.AddComponent<Image>();
            btnBg.sprite = CyberpunkUITheme.CreatePanelSprite(24,
                new Color(colors[0].r * 0.15f, colors[0].g * 0.15f, colors[0].b * 0.15f, 0.8f),
                colors[0], 2, 3);
            btnBg.type = Image.Type.Sliced;

            Button btnComp = btn.AddComponent<Button>();
            btnComp.targetGraphic = btnBg;

            // 클로저 캡처용 로컬 변수
            VegasBuildingType capturedType = type;
            btnComp.onClick.AddListener(() => SelectBuildingType(capturedType));

            // 아이콘 (건물 타입 이니셜)
            string icon = type switch
            {
                VegasBuildingType.Casino => "C",
                VegasBuildingType.Hotel => "H",
                VegasBuildingType.Shop => "S",
                VegasBuildingType.Bar => "B",
                VegasBuildingType.Restaurant => "R",
                VegasBuildingType.Fountain => "F",
                VegasBuildingType.Billboard => "!",
                _ => "?"
            };

            GameObject iconObj = CyberpunkUITheme.CreateUIObject("Icon", btn.transform);
            RectTransform iconRt = iconObj.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0, 0.35f);
            iconRt.anchorMax = new Vector2(1, 1);
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;
            TextMeshProUGUI iconText = iconObj.AddComponent<TextMeshProUGUI>();
            iconText.text = icon;
            iconText.fontSize = 24;
            iconText.color = colors[0];
            iconText.alignment = TextAlignmentOptions.Center;
            iconText.fontStyle = FontStyles.Bold;
            iconText.raycastTarget = false;

            // 이름
            GameObject nameObj = CyberpunkUITheme.CreateUIObject("Name", btn.transform);
            RectTransform nameRt = nameObj.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0, 0);
            nameRt.anchorMax = new Vector2(1, 0.35f);
            nameRt.offsetMin = Vector2.zero;
            nameRt.offsetMax = Vector2.zero;
            TextMeshProUGUI nameText = nameObj.AddComponent<TextMeshProUGUI>();
            nameText.text = spec.displayName;
            nameText.fontSize = 10;
            nameText.color = CyberpunkUITheme.TextSecondary;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.raycastTarget = false;
        }

        // 지우기 버튼 (마지막에 추가)
        {
            GameObject eraseBtn = CyberpunkUITheme.CreateUIObject("EraseBtn", palettePanel.transform);
            RectTransform eraseRt = eraseBtn.GetComponent<RectTransform>();
            eraseRt.anchorMin = new Vector2(0.5f, 0.5f);
            eraseRt.anchorMax = new Vector2(0.5f, 0.5f);
            eraseRt.anchoredPosition = new Vector2(startX + types.Length * (btnW + 8), 0);
            eraseRt.sizeDelta = new Vector2(btnW, 70);

            Image eraseBg = eraseBtn.AddComponent<Image>();
            eraseBg.sprite = CyberpunkUITheme.CreatePanelSprite(24,
                new Color(0.3f, 0.05f, 0.05f, 0.8f),
                new Color(1f, 0.3f, 0.3f), 2, 3);
            eraseBg.type = Image.Type.Sliced;

            Button eraseComp = eraseBtn.AddComponent<Button>();
            eraseComp.targetGraphic = eraseBg;
            eraseComp.onClick.AddListener(ToggleEraseMode);

            GameObject eraseIcon = CyberpunkUITheme.CreateUIObject("Icon", eraseBtn.transform);
            RectTransform eraseIconRt = eraseIcon.GetComponent<RectTransform>();
            eraseIconRt.anchorMin = new Vector2(0, 0.35f);
            eraseIconRt.anchorMax = new Vector2(1, 1);
            eraseIconRt.offsetMin = Vector2.zero;
            eraseIconRt.offsetMax = Vector2.zero;
            TextMeshProUGUI eraseText = eraseIcon.AddComponent<TextMeshProUGUI>();
            eraseText.text = "X";
            eraseText.fontSize = 24;
            eraseText.color = new Color(1f, 0.3f, 0.3f);
            eraseText.alignment = TextAlignmentOptions.Center;
            eraseText.fontStyle = FontStyles.Bold;
            eraseText.raycastTarget = false;

            GameObject eraseName = CyberpunkUITheme.CreateUIObject("Name", eraseBtn.transform);
            RectTransform eraseNameRt = eraseName.GetComponent<RectTransform>();
            eraseNameRt.anchorMin = new Vector2(0, 0);
            eraseNameRt.anchorMax = new Vector2(1, 0.35f);
            eraseNameRt.offsetMin = Vector2.zero;
            eraseNameRt.offsetMax = Vector2.zero;
            TextMeshProUGUI eraseNameText = eraseName.AddComponent<TextMeshProUGUI>();
            eraseNameText.text = "건물 삭제";
            eraseNameText.fontSize = 10;
            eraseNameText.color = CyberpunkUITheme.TextSecondary;
            eraseNameText.alignment = TextAlignmentOptions.Center;
            eraseNameText.raycastTarget = false;
        }

        // 저장 버튼
        {
            GameObject saveBtn = CyberpunkUITheme.CreateUIObject("SaveBtn", palettePanel.transform);
            RectTransform saveRt = saveBtn.GetComponent<RectTransform>();
            saveRt.anchorMin = new Vector2(1, 0.5f);
            saveRt.anchorMax = new Vector2(1, 0.5f);
            saveRt.pivot = new Vector2(1, 0.5f);
            saveRt.anchoredPosition = new Vector2(-8, 0);
            saveRt.sizeDelta = new Vector2(60, 30);

            Image saveBg = saveBtn.AddComponent<Image>();
            saveBg.sprite = CyberpunkUITheme.CreatePanelSprite(16,
                new Color(0.05f, 0.12f, 0.05f, 0.9f),
                new Color(0.3f, 1f, 0.5f), 1, 3);
            saveBg.type = Image.Type.Sliced;

            Button saveComp = saveBtn.AddComponent<Button>();
            saveComp.targetGraphic = saveBg;
            saveComp.onClick.AddListener(SaveMap);

            GameObject saveLabel = CyberpunkUITheme.CreateUIObject("Label", saveBtn.transform);
            RectTransform saveLabelRt = saveLabel.GetComponent<RectTransform>();
            saveLabelRt.anchorMin = Vector2.zero;
            saveLabelRt.anchorMax = Vector2.one;
            saveLabelRt.offsetMin = Vector2.zero;
            saveLabelRt.offsetMax = Vector2.zero;
            TextMeshProUGUI saveLabelText = saveLabel.AddComponent<TextMeshProUGUI>();
            saveLabelText.text = "저장";
            saveLabelText.fontSize = 12;
            saveLabelText.color = new Color(0.3f, 1f, 0.5f);
            saveLabelText.alignment = TextAlignmentOptions.Center;
            saveLabelText.fontStyle = FontStyles.Bold;
            saveLabelText.raycastTarget = false;
        }

        palettePanel.SetActive(false);
    }

    private void BuildStatusText()
    {
        GameObject statusObj = CyberpunkUITheme.CreateUIObject("Status", editorCanvas.transform);
        RectTransform statusRt = statusObj.GetComponent<RectTransform>();
        statusRt.anchorMin = new Vector2(0.5f, 1);
        statusRt.anchorMax = new Vector2(0.5f, 1);
        statusRt.pivot = new Vector2(0.5f, 1);
        statusRt.anchoredPosition = new Vector2(0, -10);
        statusRt.sizeDelta = new Vector2(400, 30);
        statusText = statusObj.AddComponent<TextMeshProUGUI>();
        statusText.text = "";
        statusText.fontSize = 14;
        statusText.color = CyberpunkUITheme.NeonCyan;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.fontStyle = FontStyles.Bold;
        statusText.raycastTarget = false;
        statusObj.SetActive(false);
    }

    // ──────────────────────────────────────────
    // 그리드 하이라이트
    // ──────────────────────────────────────────

    private void BuildGridHighlight()
    {
        highlightCell = GameObject.CreatePrimitive(PrimitiveType.Cube);
        highlightCell.name = "GridHighlight";
        Object.Destroy(highlightCell.GetComponent<Collider>());

        validMat = VegasProceduralBuilding.CreateMaterial(new Color(0, 1, 0.5f, 0.3f));
        invalidMat = VegasProceduralBuilding.CreateMaterial(new Color(1, 0, 0, 0.3f));

        highlightCell.GetComponent<Renderer>().material = validMat;
        highlightCell.SetActive(false);
    }

    // ──────────────────────────────────────────
    // 에디터 모드 제어
    // ──────────────────────────────────────────

    public void ToggleEditorMode()
    {
        SetEditorMode(!editorMode);
    }

    public void SetEditorMode(bool active)
    {
        editorMode = active;
        palettePanel.SetActive(active);
        highlightCell.SetActive(false);
        statusText.gameObject.SetActive(active);

        if (editToggleLabel != null)
        {
            editToggleLabel.text = active ? "완료" : "편집";
            editToggleLabel.color = active ? new Color(1f, 0.5f, 0.3f) : CyberpunkUITheme.NeonCyan;
        }

        if (active)
        {
            // 빈 칸이 너무 적으면 사용자에게 안내 (자동 생성 도시는 거의 다 채워져 있음)
            if (CountEmptyCells() < 4 && !isEraseMode)
            {
                isEraseMode = true; // 자연스럽게 삭제 모드부터 시작
            }
            UpdateStatusText();
        }

        // 이동 제어 비활성화/활성화
        VillagePlayerController player = FindAnyObjectByType<VillagePlayerController>();
        if (player != null)
        {
            // 에디터 모드에서는 플레이어 이동을 허용하되, 터치 입력이 UI가 아닌 곳에서만 건물 배치
        }
    }

    private void SelectBuildingType(VegasBuildingType type)
    {
        selectedType = type;
        isEraseMode = false;
        UpdateStatusText();
    }

    private void ToggleEraseMode()
    {
        isEraseMode = !isEraseMode;
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (statusText == null) return;
        int empty = CountEmptyCells();

        if (isEraseMode)
        {
            statusText.text = "삭제 모드 — 지울 건물을 터치하세요";
            statusText.color = new Color(1f, 0.3f, 0.3f);
        }
        else if (empty < 4)
        {
            // 빈 공간이 거의 없을 때 — 사용자에게 명확히 안내
            statusText.text = "빈 공간이 없어요. 먼저 'X'(삭제)로 건물을 지워주세요";
            statusText.color = new Color(1f, 0.7f, 0.2f);
        }
        else
        {
            var spec = VegasBuildingConfig.GetSpec(selectedType);
            statusText.text = $"{spec.displayName} 배치 — 초록 칸을 터치 (빈 칸 {empty})";
            statusText.color = CyberpunkUITheme.NeonCyan;
        }
    }

    /// <summary>
    /// 현재 비어 있는 셀 개수 — 편집 모드 진입 시 안내용
    /// </summary>
    private int CountEmptyCells()
    {
        var gen = VegasCityGenerator.Instance;
        if (gen == null || gen.CellTypes == null) return 0;
        int count = 0;
        var ct = gen.CellTypes;
        for (int x = 0; x < gen.GridWidth; x++)
            for (int z = 0; z < gen.GridHeight; z++)
                if (ct[x, z] == VegasCellType.Empty)
                    count++;
        return count;
    }

    private void SaveMap()
    {
        VegasCityGenerator.Instance?.SaveToPlayerPrefs();
        if (statusText != null)
        {
            statusText.text = "맵 저장 완료!";
            statusText.color = new Color(0.3f, 1f, 0.5f);
        }
    }

    // ──────────────────────────────────────────
    // 업데이트 (입력 처리)
    // ──────────────────────────────────────────

    private void Update()
    {
        if (!editorMode) return;
        if (VegasCityGenerator.Instance == null) return;

        // UI 위의 터치인지 확인
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            highlightCell.SetActive(false);
            return;
        }

        // 모바일 터치 처리
        bool isTouchDown = false;
        Vector2 inputPos = Vector2.zero;

#if UNITY_EDITOR
        inputPos = Input.mousePosition;
        isTouchDown = Input.GetMouseButtonDown(0);
        bool isHolding = Input.GetMouseButton(0);
#else
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            inputPos = touch.position;
            isTouchDown = touch.phase == TouchPhase.Began;
        }
        bool isHolding = Input.touchCount > 0;
#endif

        // 레이캐스트로 그리드 위치 찾기
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        Ray ray = mainCam.ScreenPointToRay(inputPos);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            Vector2Int gridPos = VegasCityGenerator.Instance.WorldToGrid(hitPoint);

            // 하이라이트 업데이트
            UpdateHighlight(gridPos);

            // 터치/클릭으로 배치 또는 삭제
            if (isTouchDown)
            {
                if (isEraseMode)
                {
                    VegasCityGenerator.Instance.RemoveBuilding(gridPos.x, gridPos.y);
                }
                else
                {
                    PlaceBuilding(gridPos);
                }
            }
        }
        else
        {
            highlightCell.SetActive(false);
        }
    }

    private void UpdateHighlight(Vector2Int gridPos)
    {
        var gen = VegasCityGenerator.Instance;
        float cs = gen.CellSize;

        if (isEraseMode)
        {
            // 삭제 모드: 1칸 빨간색 표시
            highlightCell.SetActive(true);
            highlightCell.transform.position = gen.GridToWorld(gridPos) + new Vector3(cs / 2, 0.1f, cs / 2);
            highlightCell.transform.localScale = new Vector3(cs, 0.2f, cs);
            highlightCell.GetComponent<Renderer>().material = invalidMat;
        }
        else
        {
            // 배치 모드: 선택된 건물 크기로 표시
            var spec = VegasBuildingConfig.GetSpec(selectedType);
            int bw = spec.minSize.x;
            int bh = spec.minSize.y;
            float w = bw * cs;
            float d = bh * cs;

            // 배치 가능 여부 확인
            bool canPlace = true;
            for (int x = gridPos.x; x < gridPos.x + bw; x++)
            {
                for (int z = gridPos.y; z < gridPos.y + bh; z++)
                {
                    if (x < 0 || x >= gen.GridWidth || z < 0 || z >= gen.GridHeight ||
                        gen.CellTypes[x, z] != VegasCellType.Empty)
                    {
                        canPlace = false;
                        break;
                    }
                }
                if (!canPlace) break;
            }

            highlightCell.SetActive(true);
            highlightCell.transform.position = gen.GridToWorld(gridPos) + new Vector3(w / 2, 0.1f, d / 2);
            highlightCell.transform.localScale = new Vector3(w, 0.2f, d);
            highlightCell.GetComponent<Renderer>().material = canPlace ? validMat : invalidMat;
        }
    }

    private void PlaceBuilding(Vector2Int gridPos)
    {
        var gen = VegasCityGenerator.Instance;
        var spec = VegasBuildingConfig.GetSpec(selectedType);

        int floors = Random.Range(spec.minFloors, spec.maxFloors + 1);

        var placement = new VegasMapData.BuildingPlacement
        {
            gridX = gridPos.x,
            gridZ = gridPos.y,
            sizeX = spec.minSize.x,
            sizeZ = spec.minSize.y,
            buildingType = selectedType,
            rotation = 0,
            floors = floors,
            colorIndex = selectedColorIndex++,
            customName = ""
        };

        if (gen.AddBuilding(placement))
        {
            Debug.Log($"[MapEditor] {spec.displayName} 배치: ({gridPos.x}, {gridPos.y})");
        }
    }
}
