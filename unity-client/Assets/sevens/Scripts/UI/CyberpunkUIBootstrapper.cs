using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 사이버펑크 UI 자동 셋업 부트스트래퍼
/// - 씬 로드 시 자동으로 Canvas + 모든 사이버펑크 UI 컴포넌트 생성
/// - 기존 UI(VirtualJoystick, MobileInteractButton, DialogueUI)를 비활성화하고 새 UI로 교체
/// - DontDestroyOnLoad이 아니므로 각 씬마다 동작
///
/// 사용법: 아무 씬에서든 빈 GameObject에 이 컴포넌트를 추가하면 끝.
///         또는 [RuntimeInitializeOnLoadMethod]로 자동 실행됨
/// </summary>
public class CyberpunkUIBootstrapper : MonoBehaviour
{
    [Header("Options")]
    [SerializeField] private bool replaceJoystick = true;
    [SerializeField] private bool replaceHUD = true;
    [SerializeField] private bool replaceDialogue = true;
    [SerializeField] private bool disableOldUI = true;

    [Header("HUD Settings")]
    [SerializeField] private string locationName = "";
    [SerializeField] private bool showBackButton = true;

    private Canvas cyberpunkCanvas;

    /// <summary>
    /// 앱 시작 시 sceneLoaded 이벤트 등록 (1회)
    /// → 이후 매 씬 로드마다 OnSceneLoaded가 호출되어 부트스트래퍼 생성
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        // 첫 씬(앱 시작 직후)도 처리 + 이후 씬 전환도 처리
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        // 현재 씬에도 즉시 적용 (앱 시작 씬)
        var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        TryCreateBootstrapper(currentScene.name);
    }

    /// <summary>
    /// 매 씬 로드 시 호출 — 부트스트래퍼가 없으면 생성
    /// </summary>
    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        TryCreateBootstrapper(scene.name);
    }

    /// <summary>
    /// 조건을 확인하고 부트스트래퍼 생성
    /// </summary>
    private static void TryCreateBootstrapper(string sceneName)
    {
        // 이미 존재하면 스킵
        if (FindAnyObjectByType<CyberpunkUIBootstrapper>() != null) return;

        // 타이틀 화면에서는 CyberpunkUI 불필요 — 스킵
        if (sceneName == "TitleScreen") return;
        // 홈(마이룸)은 자체 UI(HomeController)를 쓴다
        if (sceneName == "Home") return;

        GameObject go = new GameObject("[CyberpunkUI]");
        CyberpunkUIBootstrapper boot = go.AddComponent<CyberpunkUIBootstrapper>();

        // 현재 씬 이름으로 위치 이름 자동 설정
        boot.locationName = sceneName switch
        {
            "MyRoom"   => "마이 월드",
            "Lobby"    => "로비",
            "CardGame" => "카드게임",
            _ => sceneName
        };

        // 로비/타이틀에서는 뒤로가기 버튼 숨김
        boot.showBackButton = sceneName != "Lobby";

        // 조이스틱은 MyRoom에서만 필요
        if (sceneName != "MyRoom") boot.replaceJoystick = false;
    }

    private void Start()
    {
        // 조이스틱은 MyRoom(이동 가능한 씬)에서만 필요 — 그 외 모든 씬에서 비활성화
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName != "MyRoom") replaceJoystick = false;

        SetupCanvas();

        if (disableOldUI) DisableOldUI();
        if (replaceJoystick) SetupJoystick();
        if (replaceHUD) SetupHUD();
        if (replaceDialogue) SetupDialogue();
        SetupDialogueLog();
        SetupMapEditor();
    }

    /// <summary>
    /// 전용 Canvas 생성 (기존 Canvas와 별개)
    /// </summary>
    private void SetupCanvas()
    {
        GameObject canvasObj = new GameObject("CyberpunkCanvas");
        canvasObj.transform.SetParent(transform);

        cyberpunkCanvas = canvasObj.AddComponent<Canvas>();
        cyberpunkCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        cyberpunkCanvas.sortingOrder = 100; // 기존 UI 위에 표시

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // EventSystem이 없으면 생성
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
            esObj.AddComponent<StandaloneInputModule>();
        }
    }

    /// <summary>
    /// 기존 UI 컴포넌트들을 찾아서 비활성화
    /// </summary>
    private void DisableOldUI()
    {
        // 기존 VirtualJoystick 비활성화
        VirtualJoystick oldJoystick = FindAnyObjectByType<VirtualJoystick>(FindObjectsInactive.Include);
        if (oldJoystick != null)
        {
            // 조이스틱과 그 부모(보통 조이스틱 UI 전체)를 숨김
            oldJoystick.gameObject.SetActive(false);
            Debug.Log("[CyberpunkUI] 기존 VirtualJoystick 비활성화됨");
        }

        // 기존 MobileInteractButton 비활성화
        MobileInteractButton oldInteract = FindAnyObjectByType<MobileInteractButton>(FindObjectsInactive.Include);
        if (oldInteract != null)
        {
            oldInteract.gameObject.SetActive(false);
            Debug.Log("[CyberpunkUI] 기존 MobileInteractButton 비활성화됨");
        }

        // 기존 DialogueUI 비활성화
        DialogueUI oldDialogue = FindAnyObjectByType<DialogueUI>(FindObjectsInactive.Include);
        if (oldDialogue != null)
        {
            oldDialogue.gameObject.SetActive(false);
            Debug.Log("[CyberpunkUI] 기존 DialogueUI 비활성화됨");
        }
    }

    /// <summary>
    /// 사이버펑크 조이스틱 생성
    /// </summary>
    private void SetupJoystick()
    {
        GameObject joystickObj = new GameObject("CyberpunkJoystick");
        joystickObj.transform.SetParent(cyberpunkCanvas.transform, false);
        joystickObj.AddComponent<CyberpunkJoystick>();
        Debug.Log("[CyberpunkUI] CyberpunkJoystick 생성됨");
    }

    /// <summary>
    /// 사이버펑크 HUD 생성
    /// </summary>
    private void SetupHUD()
    {
        GameObject hudObj = new GameObject("CyberpunkHUD");
        hudObj.transform.SetParent(cyberpunkCanvas.transform, false);

        // RectTransform을 캔버스 전체로 스트레치
        RectTransform hudRt = hudObj.GetComponent<RectTransform>();
        if (hudRt == null) hudRt = hudObj.AddComponent<RectTransform>();
        hudRt.anchorMin = Vector2.zero;
        hudRt.anchorMax = Vector2.one;
        hudRt.offsetMin = Vector2.zero;
        hudRt.offsetMax = Vector2.zero;

        CyberpunkHUD hud = hudObj.AddComponent<CyberpunkHUD>();
        hud.Configure(locationName, showBackButton);

        Debug.Log("[CyberpunkUI] CyberpunkHUD 생성됨");
    }

    /// <summary>
    /// 사이버펑크 대화 UI 생성
    /// </summary>
    private void SetupDialogue()
    {
        GameObject dialogueObj = new GameObject("CyberpunkDialogueUI");
        dialogueObj.transform.SetParent(cyberpunkCanvas.transform, false);

        // RectTransform을 캔버스 전체로 스트레치
        RectTransform dlgRt = dialogueObj.GetComponent<RectTransform>();
        if (dlgRt == null) dlgRt = dialogueObj.AddComponent<RectTransform>();
        dlgRt.anchorMin = Vector2.zero;
        dlgRt.anchorMax = Vector2.one;
        dlgRt.offsetMin = Vector2.zero;
        dlgRt.offsetMax = Vector2.zero;

        dialogueObj.AddComponent<CyberpunkDialogueUI>();
        Debug.Log("[CyberpunkUI] CyberpunkDialogueUI 생성됨");
    }

    private void SetupDialogueLog()
    {
        if (FindAnyObjectByType<DialogueLog>() != null) return;

        GameObject logObj = new GameObject("DialogueLog");
        logObj.transform.SetParent(transform);
        logObj.AddComponent<DialogueLog>();
        Debug.Log("[CyberpunkUI] DialogueLog 생성됨");
    }

    /// <summary>
    /// 맵 에디터 생성 (MyRoom 씬에서만)
    /// </summary>
    private void SetupMapEditor()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName != "MyRoom") return;
        if (FindAnyObjectByType<VegasMapEditor>() != null) return;

        GameObject editorObj = new GameObject("VegasMapEditor");
        editorObj.transform.SetParent(transform);
        editorObj.AddComponent<VegasMapEditor>();
        Debug.Log("[CyberpunkUI] VegasMapEditor 생성됨");
    }
}
