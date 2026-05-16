using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// 로비 씬을 자동 생성하는 에디터 도구
/// - Lobby 씬 새로 생성
/// - 3D 캐릭터 배치 공간 + 카메라 + 조명
/// - UI 버튼 (게임하기, 마이룸, 설정)
/// - 페이드 캔버스 + GameSceneManager
/// </summary>
public class LobbySceneSetup : EditorWindow
{
    [MenuItem("Tools/Setup Lobby Scene (로비 씬 생성)")]
    public static void CreateLobbyScene()
    {
        // 새 씬 생성
        var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // === 카메라 ===
        GameObject camObj = new GameObject("Main Camera");
        Camera cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 35f; // 좁은 화각 = 캐릭터가 크게 보임
        camObj.transform.position = new Vector3(0, 1.0f, 3.5f);
        camObj.transform.rotation = Quaternion.Euler(5f, 180f, 0);
        camObj.tag = "MainCamera";
        camObj.AddComponent<AudioListener>();

        // === 조명 ===
        // 메인 조명 (따뜻한 톤)
        GameObject mainLight = new GameObject("Main Light");
        Light ml = mainLight.AddComponent<Light>();
        ml.type = LightType.Directional;
        ml.color = new Color(1f, 0.95f, 0.9f);
        ml.intensity = 1.2f;
        mainLight.transform.rotation = Quaternion.Euler(40f, -30f, 0);

        // 보조 조명 (차가운 톤, 뒤에서)
        GameObject fillLight = new GameObject("Fill Light");
        Light fl = fillLight.AddComponent<Light>();
        fl.type = LightType.Directional;
        fl.color = new Color(0.7f, 0.8f, 1f);
        fl.intensity = 0.4f;
        fillLight.transform.rotation = Quaternion.Euler(20f, 150f, 0);

        // 림 라이트 (윤곽 강조)
        GameObject rimLight = new GameObject("Rim Light");
        Light rl = rimLight.AddComponent<Light>();
        rl.type = LightType.Point;
        rl.color = new Color(0.8f, 0.9f, 1f);
        rl.intensity = 2f;
        rl.range = 5f;
        rimLight.transform.position = new Vector3(-1f, 2.5f, -1.5f);

        // === 캐릭터 자리 ===
        GameObject charSpot = new GameObject("LobbyCharacterSpot");
        charSpot.transform.position = new Vector3(0, 0, 0);
        charSpot.AddComponent<LobbyCharacterSetup>();

        // 안내 텍스트 (에디터용)
        GameObject guide = new GameObject("[여기에 캐릭터 프리팹을 자식으로 넣으세요]");
        guide.transform.SetParent(charSpot.transform);
        guide.transform.localPosition = Vector3.zero;

        // 바닥 (캐릭터 아래 그림자용)
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0, 0, 0);
        floor.transform.localScale = new Vector3(0.5f, 1, 0.5f);
        // 바닥 머티리얼은 나중에 교체
        Renderer floorRenderer = floor.GetComponent<Renderer>();
        floorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        floorRenderer.receiveShadows = true;

        // === UI Canvas ===
        GameObject canvasObj = new GameObject("LobbyCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();

        // EventSystem
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // --- 타이틀 ---
        GameObject titleObj = CreateText(canvasObj.transform, "TitleText",
            "SEVENS", 72, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -60f));
        titleObj.GetComponent<TextMeshProUGUI>().color = new Color(1, 0.95f, 0.85f);

        // --- 오른쪽 버튼 그룹 ---
        GameObject btnGroup = new GameObject("ButtonGroup");
        btnGroup.transform.SetParent(canvasObj.transform, false);
        RectTransform btnGroupRT = btnGroup.AddComponent<RectTransform>();
        btnGroupRT.anchorMin = new Vector2(1, 0.5f);
        btnGroupRT.anchorMax = new Vector2(1, 0.5f);
        btnGroupRT.pivot = new Vector2(1, 0.5f);
        btnGroupRT.anchoredPosition = new Vector2(-60, 0);
        btnGroupRT.sizeDelta = new Vector2(280, 400);

        VerticalLayoutGroup vlg = btnGroup.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 20;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // 게임하기 버튼
        GameObject gameBtn = CreateMenuButton(btnGroup.transform, "GameStartButton",
            "게임하기", new Color(0.85f, 0.55f, 0.2f, 0.95f));

        // 마이룸 버튼
        GameObject roomBtn = CreateMenuButton(btnGroup.transform, "MyRoomButton",
            "마이룸", new Color(0.3f, 0.65f, 0.85f, 0.95f));

        // 설정 버튼
        GameObject setBtn = CreateMenuButton(btnGroup.transform, "SettingsButton",
            "설정", new Color(0.5f, 0.5f, 0.55f, 0.95f));

        // --- 왼쪽 하단 유저 정보 ---
        GameObject userInfo = CreateText(canvasObj.transform, "UserInfo",
            "Lv.1  타임키퍼", 24, FontStyles.Normal,
            new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(30, 30));
        userInfo.GetComponent<TextMeshProUGUI>().color = new Color(1, 1, 1, 0.6f);
        userInfo.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.BottomLeft;

        // --- 설정 패널 (기본 숨김) ---
        GameObject settingsPanel = CreateSettingsPanel(canvasObj.transform);

        // === LobbyUI 컴포넌트 ===
        LobbyUI lobbyUI = canvasObj.AddComponent<LobbyUI>();
        SerializedObject so = new SerializedObject(lobbyUI);
        so.FindProperty("gameStartButton").objectReferenceValue = gameBtn.GetComponent<Button>();
        so.FindProperty("myRoomButton").objectReferenceValue = roomBtn.GetComponent<Button>();
        so.FindProperty("settingsButton").objectReferenceValue = setBtn.GetComponent<Button>();
        so.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
        so.FindProperty("lobbyCharacter").objectReferenceValue = charSpot.transform;
        so.ApplyModifiedProperties();

        // === 페이드 캔버스 ===
        GameObject fadeObj = new GameObject("FadeCanvas");
        Canvas fadeCvs = fadeObj.AddComponent<Canvas>();
        fadeCvs.renderMode = RenderMode.ScreenSpaceOverlay;
        fadeCvs.sortingOrder = 999;
        fadeObj.AddComponent<CanvasScaler>();

        GameObject fadeImage = new GameObject("FadeImage");
        fadeImage.transform.SetParent(fadeObj.transform, false);
        Image fi = fadeImage.AddComponent<Image>();
        fi.color = Color.black;
        RectTransform fiRT = fadeImage.GetComponent<RectTransform>();
        fiRT.anchorMin = Vector2.zero;
        fiRT.anchorMax = Vector2.one;
        fiRT.sizeDelta = Vector2.zero;

        CanvasGroup fadeCG = fadeObj.AddComponent<CanvasGroup>();
        fadeCG.alpha = 0;
        fadeCG.blocksRaycasts = false;

        // === GameSceneManager (DontDestroyOnLoad) ===
        GameObject managerObj = new GameObject("GameSceneManager");
        GameSceneManager gsm = managerObj.AddComponent<GameSceneManager>();
        SerializedObject gsmSO = new SerializedObject(gsm);
        gsmSO.FindProperty("fadeCanvasGroup").objectReferenceValue = fadeCG;
        gsmSO.ApplyModifiedProperties();

        // 씬 저장
        string scenePath = "Assets/sevens/Scenes/Lobby.unity";
        // 폴더 확인
        if (!AssetDatabase.IsValidFolder("Assets/sevens/Scenes"))
        {
            AssetDatabase.CreateFolder("Assets/sevens", "Scenes");
        }

        EditorSceneManager.SaveScene(newScene, scenePath);
        Debug.Log($"[Lobby] 로비 씬 생성 완료! 경로: {scenePath}");
        Debug.Log("[Lobby] LobbyCharacterSpot 안에 캐릭터 프리팹을 자식으로 넣어주세요!");

        // Build Settings에 씬 추가 안내
        EditorUtility.DisplayDialog("로비 씬 생성 완료!",
            "1. LobbyCharacterSpot에 캐릭터 프리팹을 자식으로 넣으세요\n" +
            "2. File → Build Settings에서 Lobby, MyRoom, CardGame 씬을 추가하세요\n" +
            "3. 바닥 머티리얼을 원하는 것으로 교체하세요",
            "확인");
    }

    private static GameObject CreateMenuButton(Transform parent, string name, string label, Color bgColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;

        // 둥근 느낌을 위해 약간의 설정
        Button btn = btnObj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(bgColor.r + 0.1f, bgColor.g + 0.1f, bgColor.b + 0.1f, 1f);
        cb.pressedColor = new Color(bgColor.r - 0.1f, bgColor.g - 0.1f, bgColor.b - 0.1f, 1f);
        btn.colors = cb;

        LayoutElement le = btnObj.AddComponent<LayoutElement>();
        le.preferredHeight = 70;
        le.preferredWidth = 260;

        // 텍스트
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(btnObj.transform, false);
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 32;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;

        return btnObj;
    }

    private static GameObject CreateText(Transform parent, string name, string text,
        int fontSize, FontStyles style, Vector2 anchorMin, Vector2 anchorMax, Vector2 position)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(400, 80);

        return obj;
    }

    private static GameObject CreateSettingsPanel(Transform parent)
    {
        GameObject panel = new GameObject("SettingsPanel");
        panel.transform.SetParent(parent, false);

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.85f);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.25f, 0.2f);
        rt.anchorMax = new Vector2(0.75f, 0.8f);
        rt.sizeDelta = Vector2.zero;

        // 제목
        GameObject title = new GameObject("Title");
        title.transform.SetParent(panel.transform, false);
        TextMeshProUGUI titleTmp = title.AddComponent<TextMeshProUGUI>();
        titleTmp.text = "설정";
        titleTmp.fontSize = 36;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Color.white;
        RectTransform titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 0.85f);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.sizeDelta = Vector2.zero;

        // BGM 슬라이더
        CreateSettingsSlider(panel.transform, "BGM", 0.65f);
        CreateSettingsSlider(panel.transform, "SFX", 0.45f);

        // 닫기 버튼
        GameObject closeBtn = new GameObject("CloseButton");
        closeBtn.transform.SetParent(panel.transform, false);
        Image closeBg = closeBtn.AddComponent<Image>();
        closeBg.color = new Color(0.6f, 0.3f, 0.3f, 0.9f);
        Button close = closeBtn.AddComponent<Button>();
        close.onClick.AddListener(() => panel.SetActive(false));

        RectTransform closeRT = closeBtn.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(0.3f, 0.05f);
        closeRT.anchorMax = new Vector2(0.7f, 0.15f);
        closeRT.sizeDelta = Vector2.zero;

        GameObject closeText = new GameObject("Text");
        closeText.transform.SetParent(closeBtn.transform, false);
        TextMeshProUGUI closeTmp = closeText.AddComponent<TextMeshProUGUI>();
        closeTmp.text = "닫기";
        closeTmp.fontSize = 26;
        closeTmp.alignment = TextAlignmentOptions.Center;
        closeTmp.color = Color.white;
        RectTransform closeTextRT = closeText.GetComponent<RectTransform>();
        closeTextRT.anchorMin = Vector2.zero;
        closeTextRT.anchorMax = Vector2.one;
        closeTextRT.sizeDelta = Vector2.zero;

        panel.SetActive(false);
        return panel;
    }

    private static void CreateSettingsSlider(Transform parent, string label, float yPosition)
    {
        GameObject row = new GameObject($"{label}Row");
        row.transform.SetParent(parent, false);
        RectTransform rowRT = row.AddComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0.1f, yPosition - 0.05f);
        rowRT.anchorMax = new Vector2(0.9f, yPosition + 0.05f);
        rowRT.sizeDelta = Vector2.zero;

        // 라벨
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        TextMeshProUGUI labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.text = label;
        labelTmp.fontSize = 24;
        labelTmp.color = Color.white;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform labelRT = labelObj.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0);
        labelRT.anchorMax = new Vector2(0.3f, 1);
        labelRT.sizeDelta = Vector2.zero;

        // 슬라이더
        GameObject sliderObj = new GameObject("Slider");
        sliderObj.transform.SetParent(row.transform, false);

        // 배경
        Image sliderBg = sliderObj.AddComponent<Image>();
        sliderBg.color = new Color(0.3f, 0.3f, 0.3f);

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.value = 0.7f;
        slider.minValue = 0;
        slider.maxValue = 1;

        RectTransform sliderRT = sliderObj.GetComponent<RectTransform>();
        sliderRT.anchorMin = new Vector2(0.35f, 0.2f);
        sliderRT.anchorMax = new Vector2(1f, 0.8f);
        sliderRT.sizeDelta = Vector2.zero;

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRT = fillArea.AddComponent<RectTransform>();
        fillAreaRT.anchorMin = Vector2.zero;
        fillAreaRT.anchorMax = Vector2.one;
        fillAreaRT.sizeDelta = Vector2.zero;

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.85f, 0.55f, 0.2f);
        RectTransform fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;

        slider.fillRect = fillRT;
    }
}
