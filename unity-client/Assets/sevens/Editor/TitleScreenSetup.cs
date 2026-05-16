using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;
using TMPro;

/// <summary>
/// [Tools > Sevens > Setup Title Screen]
/// TitleScreen 씬을 자동으로 생성하고 UI를 구성합니다.
///
/// 구성:
///  Canvas
///    ├─ BgPanel          (전체 배경 – 어두운 그라디언트 이미지 or 단색)
///    ├─ CharacterAnchor  (중앙 3D 캐릭터를 비추는 월드스페이스 앵커 or UI 이미지 슬롯)
///    └─ RightPanel       (우측 버튼 패널)
///         ├─ MyWorldButton
///         └─ SevensGameButton
///  GameSceneManagerPrefab (또는 빈 오브젝트에 컴포넌트 추가)
///  TitleCamera
/// </summary>
public class TitleScreenSetup : EditorWindow
{
    // ── 설정 값 ──────────────────────────────────────────────
    private const string SceneName      = "TitleScreen";
    private const string ScenePath      = "Assets/sevens/Scenes/TitleScreen.unity";
    private const string ScenesBuildKey = SceneName; // Build Settings에 추가할 이름

    [MenuItem("Tools/Sevens/Setup Title Screen Scene", priority = 10)]
    public static void RunSetup()
    {
        // 1) 이미 씬이 열려있으면 경고
        if (EditorSceneManager.GetActiveScene().name == SceneName)
        {
            if (!EditorUtility.DisplayDialog("TitleScreen Setup",
                    "현재 TitleScreen 씬이 이미 열려 있습니다.\n씬을 재구성하시겠습니까?",
                    "재구성", "취소"))
                return;
        }

        // 2) 현재 씬 저장
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        // 3) 새 씬 생성 또는 기존 씬 열기
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = SceneName;

        // 4) 씬 오브젝트 구성
        BuildScene();

        // 5) 씬 저장
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);

        // 6) Build Settings에 씬 추가
        AddSceneToBuildSettings();

        EditorUtility.DisplayDialog("TitleScreen Setup",
            "✅ TitleScreen 씬이 생성되었습니다!\n\n" +
            "다음 단계:\n" +
            "1. Hierarchy의 'CharacterRoot'에 VRM 캐릭터 프리팹을 배치하세요.\n" +
            "2. TitleScreenUI 컴포넌트의 슬롯을 Inspector에서 연결하세요.\n" +
            "3. Build Settings에서 TitleScreen을 첫 번째 씬으로 이동하세요.",
            "확인");

        Debug.Log("[TitleScreen Setup] 완료! 씬 경로: " + ScenePath);
    }

    // ════════════════════════════════════════════════════════
    //  씬 오브젝트 구성
    // ════════════════════════════════════════════════════════
    private static void BuildScene()
    {
        // ── 카메라 ──────────────────────────────────────────
        var camGO = new GameObject("TitleCamera");
        var cam   = camGO.AddComponent<Camera>();
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.05f, 0.05f, 0.10f, 1f); // 짙은 남색
        cam.orthographic     = false;
        cam.fieldOfView      = 60f;
        camGO.transform.position = new Vector3(0, 1.2f, -4f);
        camGO.transform.rotation = Quaternion.Euler(5f, 0f, 0f);
        camGO.AddComponent<AudioListener>();

        // ── 라이트 ──────────────────────────────────────────
        var lightGO = new GameObject("DirectionalLight");
        var lt      = lightGO.AddComponent<Light>();
        lt.type      = LightType.Directional;
        lt.color     = new Color(0.9f, 0.85f, 1.0f);
        lt.intensity = 1.1f;
        lightGO.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        // ── GameSceneManager ────────────────────────────────
        var mgrGO = new GameObject("GameSceneManager");
        mgrGO.AddComponent<GameSceneManager>();

        // ── 캐릭터 루트 (3D) ─────────────────────────────────
        // VRM 캐릭터의 정면은 +Z. 카메라가 -Z(앞)에서 찍으므로
        // 캐릭터를 Y=180° 회전시켜 카메라를 바라보게 함.
        var charRoot = new GameObject("CharacterRoot");
        charRoot.transform.position = new Vector3(0f, 0f, 0f);
        charRoot.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        // 개발자가 여기에 VRM 캐릭터 프리팹을 자식으로 드래그 드롭
        charRoot.AddComponent<LobbyCharacterSetup>();

        // ── Canvas ──────────────────────────────────────────
        var canvasGO = new GameObject("Canvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── 배경 패널 (반투명 그라디언트) ─────────────────────
        var bgPanel = CreatePanel(canvasGO.transform, "BgOverlay",
            new Color(0.03f, 0.03f, 0.10f, 0.75f),
            Vector2.zero, Vector2.one);
        var bgGroup = bgPanel.AddComponent<CanvasGroup>();
        bgGroup.alpha = 0f; // 인트로 연출 시 페이드됨

        // ── 우측 버튼 패널 ────────────────────────────────────
        var rightPanel = CreatePanel(canvasGO.transform, "RightPanel",
            new Color(0f, 0f, 0f, 0.45f),
            new Vector2(0.72f, 0.1f), new Vector2(1f, 0.9f));

        var rightGroup = rightPanel.AddComponent<CanvasGroup>();
        rightGroup.alpha = 0f;

        var rightRect = rightPanel.GetComponent<RectTransform>();
        // Vertical Layout
        var vlg = rightPanel.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment       = TextAnchor.MiddleCenter;
        vlg.spacing              = 24f;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(32, 32, 48, 48);

        // ── 타이틀 텍스트 ─────────────────────────────────────
        var titleLabel = CreateTMPLabel(rightPanel.transform, "TitleLabel",
            "SEVENS", 52, Color.white, FontStyles.Bold);
        titleLabel.alignment = TextAlignmentOptions.Center;
        var titleLE = titleLabel.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 80f;

        // 구분선
        var divider = CreatePanel(rightPanel.transform, "Divider",
            new Color(1f, 1f, 1f, 0.2f),
            Vector2.zero, Vector2.one);
        var dividerLE = divider.AddComponent<LayoutElement>();
        dividerLE.preferredHeight = 2f;

        Spacer(rightPanel.transform, 12f);

        // ── 마이 월드 버튼 ────────────────────────────────────
        var myWorldBtn = CreateMenuButton(rightPanel.transform, "MyWorldButton",
            "마이 월드",
            "· 나만의 도시로 이동",
            new Color(0.12f, 0.18f, 0.35f, 0.92f),
            new Color(0.25f, 0.40f, 0.85f, 1f));

        Spacer(rightPanel.transform, 8f);

        // ── 세븐즈 게임 버튼 ──────────────────────────────────
        var sevensBtn = CreateMenuButton(rightPanel.transform, "SevensGameButton",
            "세븐즈 게임",
            "· 카드 게임 시작",
            new Color(0.30f, 0.10f, 0.08f, 0.92f),
            new Color(0.85f, 0.20f, 0.15f, 1f));

        Spacer(rightPanel.transform, 20f);

        // ── TitleScreenUI 컴포넌트 ────────────────────────────
        var uiScript = canvasGO.AddComponent<TitleScreenUI>();

        // SerializedObject로 private 필드 주입
        var so = new SerializedObject(uiScript);
        so.FindProperty("myWorldButton")   .objectReferenceValue = myWorldBtn.GetComponent<Button>();
        so.FindProperty("sevensGameButton").objectReferenceValue = sevensBtn.GetComponent<Button>();
        so.FindProperty("rightPanelGroup") .objectReferenceValue = rightGroup;
        so.FindProperty("rightPanelRect")  .objectReferenceValue = rightRect;
        so.FindProperty("bgOverlayGroup")  .objectReferenceValue = bgGroup;
        so.ApplyModifiedProperties();

        // ── EventSystem ──────────────────────────────────────
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        Debug.Log("[TitleScreenSetup] 씬 오브젝트 생성 완료");
    }

    // ════════════════════════════════════════════════════════
    //  헬퍼 메서드
    // ════════════════════════════════════════════════════════

    /// 앵커 기반 패널 생성
    private static GameObject CreatePanel(Transform parent, string name,
        Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go   = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img  = go.AddComponent<Image>();
        img.color = color;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin    = anchorMin;
        rect.anchorMax    = anchorMax;
        rect.offsetMin    = Vector2.zero;
        rect.offsetMax    = Vector2.zero;
        return go;
    }

    /// 메뉴 버튼 생성 (배경 이미지 + 타이틀 TMP + 서브타이틀 TMP)
    private static GameObject CreateMenuButton(Transform parent, string name,
        string title, string sub, Color bgNormal, Color bgHover)
    {
        // 버튼 루트
        var go   = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img  = go.AddComponent<Image>();
        img.color = bgNormal;

        var btn  = go.AddComponent<Button>();
        var cb   = btn.colors;
        cb.normalColor      = bgNormal;
        cb.highlightedColor = bgHover;
        cb.pressedColor     = bgHover * 0.8f;
        btn.colors          = cb;

        var le   = go.AddComponent<LayoutElement>();
        le.preferredHeight = 110f;

        var vlg  = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment        = TextAnchor.MiddleLeft;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(28, 16, 16, 16);
        vlg.spacing = 4f;

        // 제목 텍스트
        var titleTMP = CreateTMPLabel(go.transform, "Title", title, 30, Color.white, FontStyles.Bold);
        titleTMP.alignment = TextAlignmentOptions.Left;
        titleTMP.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

        // 서브 텍스트
        var subTMP = CreateTMPLabel(go.transform, "Sub", sub, 18,
            new Color(0.85f, 0.85f, 0.85f, 0.80f), FontStyles.Normal);
        subTMP.alignment = TextAlignmentOptions.Left;
        subTMP.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

        return go;
    }

    /// TextMeshPro 레이블 생성
    private static TextMeshProUGUI CreateTMPLabel(Transform parent, string name,
        string text, float fontSize, Color color, FontStyles style)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text               = text;
        tmp.fontSize           = fontSize;
        tmp.color              = color;
        tmp.fontStyle          = style;
        tmp.enableWordWrapping = false;
        tmp.overflowMode       = TextOverflowModes.Overflow;
        return tmp;
    }

    /// 빈 스페이서
    private static void Spacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        go.AddComponent<CanvasRenderer>();
    }

    /// Build Settings에 씬 추가
    private static void AddSceneToBuildSettings()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);

        // 이미 있으면 스킵
        foreach (var s in scenes)
            if (s.path == ScenePath) return;

        // TitleScreen을 맨 앞(index 0)에 삽입
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log("[TitleScreenSetup] Build Settings에 TitleScreen 추가 완료 (index 0)");
    }
}
