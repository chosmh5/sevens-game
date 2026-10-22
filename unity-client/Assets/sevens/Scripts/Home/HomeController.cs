using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRM;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 홈(마이룸) — 앱의 첫 화면. 미연시처럼 세비-호 한 명을 세로 화면 가운데 세워 두고 대화한다.
///
/// - 세로 화면. 오른쪽 버튼(게임)으로 카드게임에 들어가면 가로로 돌아간다(GameSceneManager).
/// - 여기서 나눈 대화는 서버가 캐릭터별 '기억'으로 남긴다 → 카드게임의 시작 첫마디·하이라이트 반응이 달라진다.
/// - 카드게임을 하고 돌아오면 세비-호가 방금 판(순위·명장면)에 대해 한마디 한다(/game/return).
/// - 씬에는 이 컴포넌트만 있으면 된다. 카메라·조명·배경·UI는 실행 중에 코드로 만든다.
///   씬 이름이 Home 이면 [Home] 오브젝트가 없어도 자동으로 생긴다.
/// </summary>
public class HomeController : MonoBehaviour
{
    public const string SceneName = "Home";
    private const string DefaultPrefabPath = "Assets/seven.prefab";

    [Header("캐릭터")]
    [Tooltip("세비-호 VRM 프리팹(Assets/seven.prefab). Tools ▸ Sevens ▸ 홈 씬 만들기가 넣어 준다.")]
    [SerializeField] private GameObject characterPrefab;
    [SerializeField] private string characterName = HomeSession.MainCharacter;

    [Header("톡 건드리기 — AI 반응")]
    [Tooltip("클릭할 때 AI 대사를 요청할 확률(나머지는 기본 대사만)")]
    [SerializeField, Range(0f, 1f)] private float aiPokeChance = 0.35f;
    [Tooltip("AI 대사 요청 사이 최소 간격(초) — 노트북 CPU 부담 줄이기")]
    [SerializeField] private float aiPokeCooldown = 15f;

    [Header("화면 구도")]
    [SerializeField] private float cameraFov = 24f;
    [Tooltip("머리 꼭대기 위 여백(m)")]
    [SerializeField] private float headroom = 0.12f;
    [Tooltip("엉덩이(Hips)에서 아래로 몇 m까지 보여 줄지 — 클수록 전신에 가깝다")]
    [SerializeField] private float belowHips = 0.50f;

    [Header("대사")]
    [SerializeField] private float typeSpeed = 28f;   // 초당 글자 수

    // ── 기본 대사(서버가 없거나 기다리는 동안) — 세비-호 말투 ───────────────
    private static readonly string[] GreetFirst =
    {
        "왔어요? 기다린 건 아니에요. 그냥 여기 있었던 거죠.",
        "오늘도 한 판 하러 온 거죠? 각오는 됐어요?",
        "늦었네요. …뭐, 상관없지만요.",
    };
    private static readonly string[] GreetAfterGame =
    {
        "수고했어요. 다음 판도 제가 가져갈 거지만요.",
        "방금 판, 꽤 볼만했어요. 아주 조금요.",
    };
    // 방금 판 결과별 기본 대사(서버가 없거나 응답이 실패했을 때)
    private static readonly string[] AfterPlayerWin =
    {
        "…이번 판은 당신이 가져갔네요. 이번만이에요.",
        "축하해요. 아주 조금은 인정할게요.",
    };
    private static readonly string[] AfterSelfWin =
    {
        "봤죠? 1등은 역시 제 자리예요.",
        "말했잖아요. 이기는 건 저라고요.",
    };
    private static readonly string[] AfterBlockaded =
    {
        "아까 그 카드, 일부러 쥐고 있던 거 다 알아요. 치사해요.",
        "저를 막고 좋았어요? 다음엔 제가 막을 거예요.",
    };
    private static readonly string[] PokeLines =
    {
        "…뭐예요, 갑자기.",
        "자꾸 그러면 다음 판에 안 봐줘요.",
        "할 말 있으면 말로 하세요.",
        "흥, 관심 있으면 솔직하게 말해요.",
    };
    private static readonly string[] PlayLines =
    {
        "한 판 하자고요? 좋아요. 후회하지 마요.",
        "드디어 할 마음이 생겼네요.",
        "좋아요. 이번에도 제가 1등이에요.",
    };
    private const string OfflineLine = "(서버랑 연결이 안 돼요. 오른쪽 위 '서버' 버튼에서 IP를 확인해 주세요.)";

    // ── 런타임 ─────────────────────────────────────────────────────
    private Camera _cam;
    private GameObject _character;
    private VnCharacterPose _pose;
    private VnGesturePlayer _gestures;
    private HomeBackdropFX _backdrop;
    private string _pendingPlayerMsg;  // 다음 대사 반응에 쓸 플레이어의 말
    private bool _waved;               // 이번 방문 인사 손짓

    private CanvasGroup _hudGroup;
    private TextMeshProUGUI _relationText;
    private TextMeshProUGUI _relationDelta;   // 명패 오른쪽에 잠깐 뜨는 +2 / -1
    private RectTransform _relationPlate;
    private Coroutine _relWatchCo, _deltaCo;
    private TextMeshProUGUI _lineText;
    private TextMeshProUGUI _nextMark;
    private TMP_InputField _input;
    private Button _sendBtn;
    private RectTransform _playBtnRT;
    private GameObject _restoreHud;
    private GameObject _difficultyPanel;
    private GameObject _memoryPanel;
    private TextMeshProUGUI _memoryText;

    private Coroutine _typeCo, _thinkCo;
    private string _fullLine = "";
    private bool _busy;               // 서버 응답 기다리는 중
    private bool _playerSpoke;        // 이번 방문에 플레이어가 말을 걸었는지
    private int _replyCount;

    // 톡 건드리기
    private int _pokeCount;            // 짧은 간격으로 연달아 건드린 횟수
    private float _lastPokeTime = -99f;
    private float _lastAiPokeTime = -99f;
    private float _lastSayTime;
    private bool _aiPokePending;       // AI 반응 요청 중
    private string _aiPokeReady;       // 받아 놨지만 아직 못 보여 준 AI 반응(다음 클릭 때 바로)
    private int _chatSerial;           // 플레이어가 말을 걸 때마다 +1 — 그 뒤 도착한 반응은 버린다

    // ══════════════════════════════════════════════════════════════════
    //  자동 생성
    // ══════════════════════════════════════════════════════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => TryCreate(s);

    private static void TryCreate(Scene s)
    {
        if (s.name != SceneName) return;
        if (FindAnyObjectByType<HomeController>() != null) return;
        new GameObject("[Home]").AddComponent<HomeController>();
    }

    // ══════════════════════════════════════════════════════════════════
    //  수명
    // ══════════════════════════════════════════════════════════════════

    private void Awake()
    {
        Screen.orientation = ScreenOrientation.Portrait;
        EnsureServices();
    }

    private void Start()
    {
        BuildStage();
        SpawnCharacter();
        BuildUI();
        FrameCamera();

        ServerSettingsUI.Connected += OnServerConnected;

        bool back = HomeSession.ReturnedFromGame;
        HomeSession.ReturnedFromGame = false;
        var game = back ? HomeSession.ConsumeLastGame() : null;
        if (game != null)
        {
            // 방금 끝난 판에 대한 반응(서버가 없으면 결과별 기본 대사)
            if (HomeSession.ServerOk || HomeSession.ResultPending) StartCoroutine(FetchGameReturn(game));
            else Greet(AfterGameLine(game));
        }
        else if (HomeSession.ServerOk)
        {
            // 서버가 이미 연결돼 있으면(게임 하고 돌아온 경우 등) 기억·결과를 반영한 인사를 받아 온다.
            StartCoroutine(FetchGreeting(back ? Pick(GreetAfterGame) : null));
            StartCoroutine(FetchRelation());
        }
        else
        {
            Greet(Pick(back ? GreetAfterGame : GreetFirst));
        }
    }

    private void OnDestroy()
    {
        ServerSettingsUI.Connected -= OnServerConnected;
    }

    private void OnServerConnected()
    {
        HomeSession.ServerOk = true;
        StartCoroutine(FetchRelation());
        if (!_playerSpoke && !_busy) StartCoroutine(FetchGreeting(null));
    }

    private void Update()
    {
        // 안드로이드 뒤로가기 → 열린 창 닫기
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_difficultyPanel != null && _difficultyPanel.activeSelf) _difficultyPanel.SetActive(false);
            else if (_memoryPanel != null && _memoryPanel.activeSelf) _memoryPanel.SetActive(false);
            else if (_restoreHud != null && _restoreHud.activeSelf) ShowHud(true);
        }

        // 캐릭터를 톡 치면 반응
        if (Input.GetMouseButtonDown(0) && !PointerOverUI() && IsOnCharacter(Input.mousePosition))
            Poke();

        // 게임 버튼 숨쉬기
        if (_playBtnRT != null)
            _playBtnRT.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 2.4f) * 0.035f);
    }

    // ══════════════════════════════════════════════════════════════════
    //  준비: 서비스 / 무대 / 캐릭터
    // ══════════════════════════════════════════════════════════════════

    private static void EnsureServices()
    {
        if (GameSceneManager.Instance == null)
            new GameObject("GameSceneManager").AddComponent<GameSceneManager>();
        if (ServerClient.Instance == null)
            new GameObject("ServerClient").AddComponent<ServerClient>();   // Awake 에서 서버 IP 입력창도 띄운다
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private void BuildStage()
    {
        _cam = Camera.main;
        if (_cam == null)
        {
            var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
            _cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
        }
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = RetroTheme.Bg;
        _cam.fieldOfView = cameraFov;
        _cam.nearClipPlane = 0.05f;
        _cam.farClipPlane = 50f;

        if (FindAnyObjectByType<Light>() == null)
        {
            var lightGO = new GameObject("Key Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = new Color(1f, 0.96f, 0.92f);
            light.shadows = LightShadows.None;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.78f, 0.74f, 0.74f);
    }

    private void SpawnCharacter()
    {
        var prefab = characterPrefab;
#if UNITY_EDITOR
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
#endif
        if (prefab == null)
        {
            Debug.LogError("[Home] 캐릭터 프리팹이 없습니다. Tools ▸ Sevens ▸ 홈 씬 만들기를 실행하거나 [Home] 의 Character Prefab 에 seven.prefab 을 넣어 주세요.");
            return;
        }

        _character = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        _character.name = characterName;
        MToonURPFixer.FixHierarchy(_character);   // URP 반투명 보정(MToon 유지, 속성만 수정)

        // 혹시 붙어 있는 배회/대화 컴포넌트는 홈에선 쓰지 않는다.
        foreach (var mb in _character.GetComponentsInChildren<MonoBehaviour>())
            if (mb is CharacterWander || mb is CharacterDialogue || mb is ConversationMarker) mb.enabled = false;

        _pose = _character.AddComponent<VnCharacterPose>();
        _pose.SetLookTarget(_cam.transform);
        _gestures = _character.AddComponent<VnGesturePlayer>();   // 감정에 따라 이따금 하는 동작
    }

    /// <summary>머리 위부터 허벅지쯤까지 세로 화면에 꽉 차게 카메라를 놓는다.</summary>
    private void FrameCamera()
    {
        Vector3 fwd = _pose != null && _pose.Ready ? _pose.Forward : Vector3.forward;
        float top = 1.6f, bottom = 0.4f;
        if (_pose != null && _pose.Ready && _pose.Head != null)
        {
            top = _pose.Head.position.y + 0.17f + headroom;
            bottom = _pose.Hips.position.y - belowHips;
        }
        float h = top - bottom;
        float d = (h * 0.5f) / Mathf.Tan(cameraFov * 0.5f * Mathf.Deg2Rad);
        Vector3 center = new Vector3(_character != null ? _character.transform.position.x : 0f,
                                     (top + bottom) * 0.5f,
                                     _character != null ? _character.transform.position.z : 0f);
        _cam.transform.position = center + fwd * d;
        _cam.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);

        // 조명: 카메라 쪽 위에서 살짝 비스듬히
        var light = FindAnyObjectByType<Light>();
        if (light != null && light.type == LightType.Directional)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            light.transform.rotation = Quaternion.LookRotation(-fwd - Vector3.up * 0.55f + right * 0.35f);
        }

        // 배경은 캐릭터 뒤에
        var bg = GameObject.Find("[HomeBackground]");
        if (bg != null) bg.GetComponent<Canvas>().planeDistance = d + 3f;
    }

    // ══════════════════════════════════════════════════════════════════
    //  UI — 카드게임과 같은 레트로 스킨(RetroTheme)
    //  Galmuri 도트 폰트 · 검은 외곽선 패널 + 딱딱한 그림자 · 남색 줄무늬 배경 · 주사선/비네트.
    //  기준 해상도 720×1560 = 카드게임(1560×720)을 세로로 세운 것 → 글자·패널 픽셀 크기가 같다.
    // ══════════════════════════════════════════════════════════════════

    private static readonly Vector2 PortraitReference =
        new Vector2(RetroTheme.ReferenceResolution.y, RetroTheme.ReferenceResolution.x);

    private Color SeatColor => RetroTheme.SeatColor(characterName);
    private static string HexOf(Color c) => ColorUtility.ToHtmlStringRGB(c);

    private readonly List<Button> _difficultyButtons = new List<Button>();
    private readonly List<TextMeshProUGUI> _difficultyLabels = new List<TextMeshProUGUI>();

    private static void SetupScaler(CanvasScaler s)
    {
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = PortraitReference;
        s.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        s.referencePixelsPerUnit = 100f;
    }

    private void BuildUI()
    {
        // ── 배경(캐릭터 뒤): 카드게임과 같은 줄무늬 + 캐릭터 색 후광 ──
        var bgGO = new GameObject("[HomeBackground]");
        var bgCanvas = bgGO.AddComponent<Canvas>();
        bgCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        bgCanvas.worldCamera = _cam;
        bgCanvas.planeDistance = 10f;
        bgCanvas.sortingOrder = -100;
        SetupScaler(bgGO.AddComponent<CanvasScaler>());

        var stripes = RetroTheme.Stretch(RetroTheme.Node("Stripes", bgGO.transform)).gameObject.AddComponent<Image>();
        stripes.sprite = RetroTheme.StripeTile();
        stripes.type = Image.Type.Tiled;
        stripes.raycastTarget = false;

        var glow = RetroTheme.Place(RetroTheme.Node("Glow", bgGO.transform), new Vector2(0.5f, 0.66f),
                                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 1000f));
        var gi = glow.gameObject.AddComponent<Image>();
        gi.sprite = HomeUI.GlowSprite();
        gi.color = RetroTheme.WithAlpha(SeatColor, 0.20f);
        gi.raycastTarget = false;

        // 하이퍼팝 장식(상태 로그·가끔 배경 글리치) — 배경 캔버스라 캐릭터·UI 뒤
        _backdrop = HomeBackdropFX.Build(bgGO.transform, characterName);

        // ── 화면 UI ──
        var canvasGO = new GameObject("[HomeUI]");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        SetupScaler(canvasGO.AddComponent<CanvasScaler>());
        canvasGO.AddComponent<GraphicRaycaster>();

        var safe = RetroTheme.Node("SafeArea", canvasGO.transform);
        ApplySafeArea(safe);

        // 숨길 수 있는 HUD 묶음
        var hud = RetroTheme.Stretch(RetroTheme.Node("HUD", safe));
        _hudGroup = hud.gameObject.AddComponent<CanvasGroup>();

        BuildRelationPlate(hud);
        BuildDialogueBox(hud);
        BuildInputBar(hud);
        BuildSideRail(hud);

        // HUD 숨김 상태에서 아무 데나 누르면 복귀
        var restore = RetroTheme.Stretch(RetroTheme.Node("RestoreHud", safe));
        restore.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        restore.gameObject.AddComponent<Button>().onClick.AddListener(() => ShowHud(true));
        _restoreHud = restore.gameObject;
        _restoreHud.SetActive(false);

        BuildDifficultyPanel(safe);
        BuildMemoryPanel(safe);
        BuildCrt();
    }

    private static void ApplySafeArea(RectTransform rt)
    {
        Rect sa = Screen.safeArea;
        if (Screen.width <= 0 || Screen.height <= 0) { RetroTheme.Stretch(rt); return; }
        rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
        rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>가장자리 비네트(맨 위, 터치 통과). 카드게임의 주사선은 캐릭터를 가려서 홈에선 쓰지 않는다.</summary>
    private static void BuildCrt()
    {
        var go = new GameObject("[HomeCRT]");
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 40;
        SetupScaler(go.AddComponent<CanvasScaler>());
        // 주사선(가로줄)은 캐릭터를 가려서 홈에선 뺀다 — 비네트만
        var vi = RetroTheme.Stretch(RetroTheme.Node("Vignette", go.transform)).gameObject.AddComponent<Image>();
        vi.sprite = RetroTheme.VignetteSprite();
        vi.raycastTarget = false;
    }

    /// <summary>왼쪽 위 — 카드게임 좌석 명패와 같은 모양(아바타 칸 + 이름 + 호감도).</summary>
    private void BuildRelationPlate(Transform parent)
    {
        var plate = RetroTheme.Place(RetroTheme.Node("RelationPlate", parent), new Vector2(0f, 1f), new Vector2(0f, 1f),
                                     new Vector2(20f, -20f), new Vector2(340f, 84f));
        RetroTheme.Panel(plate, RetroTheme.PanelColor, 12, 3);

        var av = RetroTheme.Place(RetroTheme.Node("Avatar", plate), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                  new Vector2(12f, 0f), new Vector2(60f, 60f));
        RetroTheme.Panel(av, SeatColor, 8, 3, 5, 0f);
        var ini = RetroTheme.Label(av, "Initial", RetroTheme.Initial(characterName), 33f, RetroTheme.Ink, true);
        RetroTheme.Stretch((RectTransform)ini.transform);

        var name = RetroTheme.Label(plate, "Name", characterName, 22f, RetroTheme.Text, true, TextAlignmentOptions.TopLeft);
        RetroTheme.Place((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                         new Vector2(86f, -12f), new Vector2(240f, 28f));
        RetroTheme.AddTextShadow(name);

        _relationText = RetroTheme.Label(plate, "Relation", "", 22f, RetroTheme.Muted, false, TextAlignmentOptions.BottomLeft);
        RetroTheme.Place((RectTransform)_relationText.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                         new Vector2(86f, 12f), new Vector2(240f, 28f));
        _relationText.richText = true;
        _relationText.text = $"<color=#{HexOf(RetroTheme.Faint)}>♥♥♥♥♥</color>";

        _relationPlate = plate;
        _relationDelta = RetroTheme.Label(plate, "Delta", "", 30f, RetroTheme.Pink, true, TextAlignmentOptions.Left);
        RetroTheme.Place((RectTransform)_relationDelta.transform, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                         new Vector2(12f, 0f), new Vector2(120f, 40f));
        RetroTheme.AddTextShadow(_relationDelta);
        _relationDelta.alpha = 0f;
    }

    /// <summary>아래 대사창 — 카드게임 말풍선과 같은 RPG 대화창(검은 바탕 + 밝은 테두리 + 캐릭터 색 이름표 + ▼).</summary>
    private void BuildDialogueBox(Transform parent)
    {
        var box = RetroTheme.Node("DialogueBox", parent);
        box.anchorMin = new Vector2(0f, 0f);
        box.anchorMax = new Vector2(1f, 0f);
        box.pivot = new Vector2(0.5f, 0f);
        box.offsetMin = new Vector2(20f, 132f);
        box.offsetMax = new Vector2(-20f, 132f + 236f);

        var bg = box.gameObject.AddComponent<Image>();
        bg.sprite = RetroTheme.PanelSprite(8, 0);
        bg.type = Image.Type.Sliced;
        bg.color = RetroTheme.WithAlpha(RetroTheme.Ink, 0.94f);
        var sh = box.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.5f);
        sh.effectDistance = new Vector2(0f, -6f);
        var tapSkip = box.gameObject.AddComponent<Button>();
        tapSkip.transition = Selectable.Transition.None;
        tapSkip.onClick.AddListener(SkipTyping);

        var ring = RetroTheme.Stretch(RetroTheme.Node("Border", box)).gameObject.AddComponent<Image>();
        ring.sprite = RetroTheme.RingSprite(8, 4);
        ring.type = Image.Type.Sliced;
        ring.color = RetroTheme.Text;
        ring.raycastTarget = false;

        var chip = RetroTheme.Chip(box, "Name", characterName, SeatColor, RetroTheme.Ink, 22f, false);
        RetroTheme.Place((RectTransform)chip.transform.parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                         new Vector2(16f, -14f), Vector2.zero);

        _lineText = RetroTheme.Label(box, "Line", "", 33f, RetroTheme.Text, false, TextAlignmentOptions.TopLeft);
        _lineText.textWrappingMode = TextWrappingModes.Normal;
        _lineText.richText = true;
        _lineText.lineSpacing = 8f;
        var lrt = (RectTransform)_lineText.transform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(24f, 24f);
        lrt.offsetMax = new Vector2(-24f, -60f);

        _nextMark = RetroTheme.Label(box, "Next", "▼", 22f, RetroTheme.Gold, true);
        RetroTheme.Place((RectTransform)_nextMark.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                         new Vector2(-18f, 14f), new Vector2(30f, 30f));
        _nextMark.gameObject.SetActive(false);
    }

    private void BuildInputBar(Transform parent)
    {
        var bar = RetroTheme.Node("InputBar", parent);
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.offsetMin = new Vector2(20f, 40f);
        bar.offsetMax = new Vector2(-20f, 40f + 72f);

        var field = RetroTheme.Stretch(RetroTheme.Node("Field", bar));
        field.offsetMax = new Vector2(-132f, 0f);
        var fieldImg = RetroTheme.Panel(field, RetroTheme.Inset, 10, 3, 0, 0f);
        fieldImg.raycastTarget = true;

        var area = RetroTheme.Stretch(RetroTheme.Node("TextArea", field));
        area.offsetMin = new Vector2(18f, 8f);
        area.offsetMax = new Vector2(-18f, -8f);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = RetroTheme.Label(area, "Placeholder", $"{characterName}에게 말 걸기…", 22f, RetroTheme.Faint, false,
                                  TextAlignmentOptions.Left);
        RetroTheme.Stretch((RectTransform)ph.transform);
        var txt = RetroTheme.Label(area, "Text", "", 22f, RetroTheme.Text, false, TextAlignmentOptions.Left);
        RetroTheme.Stretch((RectTransform)txt.transform);
        txt.raycastTarget = true;

        _input = field.gameObject.AddComponent<TMP_InputField>();
        _input.targetGraphic = fieldImg;
        _input.textViewport = area;
        _input.textComponent = txt;
        _input.placeholder = ph;
        _input.lineType = TMP_InputField.LineType.SingleLine;
        _input.characterLimit = 120;
        _input.caretColor = RetroTheme.Text;
        _input.customCaretColor = true;
        _input.selectionColor = RetroTheme.WithAlpha(RetroTheme.Accent, 0.4f);
        _input.onSubmit.AddListener(_ => OnSend());

        _sendBtn = RetroTheme.MakeButton(bar, "Send", RetroTheme.BtnBlue, "전송", 22f);
        RetroTheme.Place((RectTransform)_sendBtn.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                         new Vector2(0f, 3f), new Vector2(116f, 66f));
        _sendBtn.onClick.AddListener(OnSend);
    }

    /// <summary>오른쪽 버튼 — 카드게임 버튼과 같은 색(주황 = 진행, 파랑 = 보조).</summary>
    private void BuildSideRail(Transform parent)
    {
        var rail = RetroTheme.Place(RetroTheme.Node("SideRail", parent), new Vector2(1f, 0f), new Vector2(1f, 0f),
                                    new Vector2(-20f, 400f), new Vector2(150f, 330f));

        var play = RetroTheme.MakeButton(rail, "Play", RetroTheme.BtnOrange, "게임\n시작", 33f);
        _playBtnRT = RetroTheme.Place((RectTransform)play.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                      new Vector2(0f, 6f), new Vector2(150f, 150f));
        play.onClick.AddListener(OpenDifficulty);

        var memory = RetroTheme.MakeButton(rail, "Memory", RetroTheme.BtnBlue, "기억", 22f);
        RetroTheme.Place((RectTransform)memory.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                         new Vector2(0f, 180f), new Vector2(150f, 64f));
        memory.onClick.AddListener(OpenMemory);

        var hide = RetroTheme.MakeButton(rail, "Hide", RetroTheme.PanelColor, "감상", 22f);
        RetroTheme.Place((RectTransform)hide.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                         new Vector2(0f, 262f), new Vector2(150f, 64f));
        hide.onClick.AddListener(() => ShowHud(false));
    }

    /// <summary>어두운 막 + 가운데 레트로 패널. 막을 누르면 닫히고, 패널을 누르면 안 닫힌다.</summary>
    private RectTransform Modal(Transform parent, string name, Vector2 size, out GameObject root)
    {
        var dim = RetroTheme.Stretch(RetroTheme.Node(name, parent));
        var dimImg = dim.gameObject.AddComponent<Image>();
        dimImg.color = RetroTheme.WithAlpha(RetroTheme.Ink, 0.72f);
        var go = dim.gameObject;
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => go.SetActive(false));
        root = go;

        var card = RetroTheme.Place(RetroTheme.Node("Card", dim), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                    new Vector2(0f, 40f), size);
        RetroTheme.Panel(card, RetroTheme.PanelColor, 12, 3).raycastTarget = true;
        card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
        return card;
    }

    private TextMeshProUGUI Title(RectTransform card, string text, float y)
    {
        var t = RetroTheme.Label(card, "Title", text, 33f, RetroTheme.Gold, true);
        RetroTheme.Place((RectTransform)t.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                         new Vector2(0f, y), new Vector2(card.sizeDelta.x - 40f, 44f));
        RetroTheme.AddTextShadow(t);
        return t;
    }

    private TextMeshProUGUI Sub(RectTransform card, string text, float y)
    {
        var t = RetroTheme.Label(card, "Sub", text, 22f, RetroTheme.Muted);
        RetroTheme.Place((RectTransform)t.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                         new Vector2(0f, y), new Vector2(card.sizeDelta.x - 40f, 30f));
        return t;
    }

    private void BuildDifficultyPanel(Transform parent)
    {
        var card = Modal(parent, "DifficultyPanel", new Vector2(600f, 520f), out _difficultyPanel);
        Title(card, "얼마나 진심으로 할까요?", -32f);
        Sub(card, "세븐 · 4인전   세비-호 · 류 · 진", -84f);

        string[] labels = { "살살", "보통", "진심" };
        string[] notes  = { "EASY", "NORMAL", "HARD" };
        _difficultyButtons.Clear();
        _difficultyLabels.Clear();
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var b = RetroTheme.MakeButton(card, $"Diff{i}", RetroTheme.Inset, "", 22f);
            RetroTheme.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Vector2((i - 1) * 186f, -138f), new Vector2(170f, 140f));
            b.onClick.AddListener(() => SelectDifficulty(idx));
            var lbl = RetroTheme.Label(b.transform, "Label", $"{labels[i]}\n<size=22>{notes[i]}</size>", 33f,
                                       RetroTheme.Text, true);
            lbl.richText = true;
            lbl.lineSpacing = 10f;
            RetroTheme.Stretch((RectTransform)lbl.transform);
            _difficultyButtons.Add(b);
            _difficultyLabels.Add(lbl);
        }

        var start = RetroTheme.MakeButton(card, "Start", RetroTheme.BtnOrange, "시작", 33f);
        RetroTheme.Place((RectTransform)start.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                         new Vector2(0f, 92f), new Vector2(360f, 84f));
        start.onClick.AddListener(StartGame);

        var cancel = RetroTheme.Place(RetroTheme.Node("Cancel", card), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                      new Vector2(0f, 26f), new Vector2(220f, 44f));
        cancel.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        cancel.gameObject.AddComponent<Button>().onClick.AddListener(() => _difficultyPanel.SetActive(false));
        var cl = RetroTheme.Label(cancel, "Label", "다음에", 22f, RetroTheme.Muted);
        RetroTheme.Stretch((RectTransform)cl.transform);

        _difficultyPanel.SetActive(false);
    }

    private void BuildMemoryPanel(Transform parent)
    {
        var card = Modal(parent, "MemoryPanel", new Vector2(620f, 880f), out _memoryPanel);
        Title(card, $"{characterName}의 기억", -32f);
        Sub(card, "여기서 나눈 이야기는 카드게임에서 꺼내요.", -84f);

        var inset = RetroTheme.Stretch(RetroTheme.Node("List", card));
        inset.offsetMin = new Vector2(24f, 128f);
        inset.offsetMax = new Vector2(-24f, -128f);
        RetroTheme.InsetBox(inset, null, 10);

        _memoryText = RetroTheme.Label(inset, "Text", "", 22f, RetroTheme.Text, false, TextAlignmentOptions.TopLeft);
        _memoryText.textWrappingMode = TextWrappingModes.Normal;
        _memoryText.overflowMode = TextOverflowModes.Ellipsis;
        _memoryText.richText = true;
        _memoryText.lineSpacing = 10f;
        RetroTheme.Stretch((RectTransform)_memoryText.transform, 20f);

        var close = RetroTheme.MakeButton(card, "Close", RetroTheme.BtnBlue, "닫기", 33f);
        RetroTheme.Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                         new Vector2(-120f, 30f), new Vector2(300f, 76f));
        close.onClick.AddListener(() => _memoryPanel.SetActive(false));

        var clear = RetroTheme.MakeButton(card, "Clear", RetroTheme.BtnRed, "잊게 하기", 22f);
        RetroTheme.Place((RectTransform)clear.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                         new Vector2(160f, 30f), new Vector2(200f, 76f));
        clear.onClick.AddListener(() => StartCoroutine(ClearMemory()));

        _memoryPanel.SetActive(false);
    }

    private void ShowHud(bool show)
    {
        _hudGroup.alpha = show ? 1f : 0f;
        _hudGroup.interactable = show;
        _hudGroup.blocksRaycasts = show;
        _restoreHud.SetActive(!show);
    }

    // ══════════════════════════════════════════════════════════════════
    //  대사 표시
    // ══════════════════════════════════════════════════════════════════

    private void Say(string line, BlendShapePreset expr = BlendShapePreset.Neutral)
    {
        StopThinking();
        _lastSayTime = Time.unscaledTime;
        _fullLine = line ?? "";
        if (_typeCo != null) StopCoroutine(_typeCo);
        _typeCo = StartCoroutine(TypeLine(_fullLine));
        if (expr != BlendShapePreset.Neutral)
        {
            if (_pose != null) _pose.Express(expr, 2.6f, 0.65f);
        }
        else if (_gestures != null && _gestures.enabled)
        {
            _gestures.React(_fullLine, _pendingPlayerMsg);   // 대사 내용·플레이어 말에 맞는 동작 + 표정
        }
        else
        {
            expr = GuessExpression(_fullLine);
            if (expr != BlendShapePreset.Neutral && _pose != null) _pose.Express(expr, 2.6f, 0.65f);
        }
        _pendingPlayerMsg = null;
    }

    /// <summary>방문 인사 — 대사 + (이번 방문 처음이면) 손 흔들기.</summary>
    private void Greet(string line)
    {
        Say(line);
        if (_waved) return;
        _waved = true;
        StartCoroutine(PlaySoon(VnGesture.Wave, 0.3f));
    }

    private IEnumerator PlaySoon(VnGesture g, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_gestures != null) _gestures.Play(g);
    }

    private IEnumerator TypeLine(string line)
    {
        _nextMark.gameObject.SetActive(false);
        _lineText.text = line;
        _lineText.maxVisibleCharacters = 0;
        _lineText.ForceMeshUpdate();
        int total = _lineText.textInfo.characterCount;
        if (_pose != null) _pose.Talk(total / typeSpeed + 0.1f);
        float shown = 0f;
        while (shown < total)
        {
            shown += Time.unscaledDeltaTime * typeSpeed;
            _lineText.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
            yield return null;
        }
        _lineText.maxVisibleCharacters = 99999;
        _nextMark.gameObject.SetActive(true);
        _typeCo = null;
    }

    private void SkipTyping()
    {
        if (_typeCo == null) return;
        StopCoroutine(_typeCo);
        _typeCo = null;
        _lineText.maxVisibleCharacters = 99999;
        _nextMark.gameObject.SetActive(true);
        if (_pose != null) _pose.StopTalking();
    }

    private void StartThinking(string playerLine)
    {
        if (_typeCo != null) { StopCoroutine(_typeCo); _typeCo = null; }
        StopThinking();
        _thinkCo = StartCoroutine(Thinking(playerLine));
    }

    private void StopThinking()
    {
        if (_thinkCo != null) StopCoroutine(_thinkCo);
        _thinkCo = null;
    }

    private IEnumerator Thinking(string playerLine)
    {
        _nextMark.gameObject.SetActive(false);
        _lineText.maxVisibleCharacters = 99999;
        string head = string.IsNullOrEmpty(playerLine) ? "" : $"<size=22><color=#{HexOf(RetroTheme.Muted)}>나: {Escape(playerLine)}</color></size>\n";
        int n = 0;
        while (true)
        {
            _lineText.text = head + new string('·', 1 + (n++ % 3));
            yield return new WaitForSecondsRealtime(0.4f);
        }
    }

    private static string Escape(string s) => (s ?? "").Replace("<", "&lt;").Replace(">", "&gt;");

    private static BlendShapePreset GuessExpression(string line)
    {
        if (string.IsNullOrEmpty(line)) return BlendShapePreset.Neutral;
        if (Regex.IsMatch(line, "흥|어림없|짜증|싫어|안 봐|건방")) return BlendShapePreset.Angry;
        if (Regex.IsMatch(line, "미안|아쉽|속상|졌")) return BlendShapePreset.Sorrow;
        if (Regex.IsMatch(line, "좋아요|재밌|기뻐|역시|이겼|ㅎ|!")) return BlendShapePreset.Fun;
        return BlendShapePreset.Neutral;
    }

    private static string Pick(string[] arr) => arr[UnityEngine.Random.Range(0, arr.Length)];

    // ══════════════════════════════════════════════════════════════════
    //  상호작용
    // ══════════════════════════════════════════════════════════════════

    private void OnSend()
    {
        if (_busy || _input == null) return;
        string msg = (_input.text ?? "").Trim();
        if (msg.Length == 0) return;
        _input.text = "";
        _busy = true;
        _playerSpoke = true;
        _chatSerial++;
        _aiPokeReady = null;   // 대화로 맥락이 바뀌었으니 묵은 반응은 버린다
        _pendingPlayerMsg = msg;
        HomeSession.MarkTalked();
        StartThinking(msg);

        if (ServerClient.Instance == null)
        {
            _busy = false;
            Say(OfflineLine);
            return;
        }
        StartCoroutine(ServerClient.Instance.SendUserMessage(msg, characterName,
            reply =>
            {
                _busy = false;
                HomeSession.ServerOk = true;
                Say(string.IsNullOrWhiteSpace(reply) ? "……" : reply);
                _replyCount++;
                WatchRelation();   // 호감도 판정은 답장 뒤 서버에서 따로 돈다 → 바뀔 때까지 잠깐 지켜본다
            },
            () =>
            {
                _busy = false;
                Say(OfflineLine);
            }));
    }

    private void Poke()
    {
        if (_busy || _pose == null) return;
        float now = Time.unscaledTime;
        _pokeCount = now - _lastPokeTime < 8f ? _pokeCount + 1 : 1;
        _lastPokeTime = now;

        _pose.Hop();
        if (_backdrop != null) { _backdrop.Glitch(); _backdrop.Log("poke detected"); }
        if (_gestures != null) _gestures.Play(UnityEngine.Random.value < 0.6f ? VnGesture.LookAway : VnGesture.ArmsCrossed, -1f, false);

        // 전에 받아 두고 못 보여 준 AI 반응이 있으면 바로 쓴다(기다림 없음).
        if (!string.IsNullOrEmpty(_aiPokeReady))
        {
            string ready = _aiPokeReady;
            _aiPokeReady = null;
            Say(ready, GuessPokeExpression(ready));
            return;
        }

        // 일단 기본 대사 → 가끔 뒤에서 AI 반응을 받아 이어서 말한다(로딩 티 안 나게).
        Say(Pick(PokeLines), BlendShapePreset.Angry);
        if (!_aiPokePending && HomeSession.ServerOk && BaseUrl != null
            && now - _lastAiPokeTime > aiPokeCooldown && UnityEngine.Random.value < aiPokeChance)
            StartCoroutine(FetchPokeLine(_pokeCount));
    }

    private static BlendShapePreset GuessPokeExpression(string line)
    {
        var e = GuessExpression(line);
        return e == BlendShapePreset.Neutral ? BlendShapePreset.Angry : e;
    }

    /// <summary>
    /// 톡 건드림에 대한 AI 반응. 기본 대사가 다 나오고 한 박자 쉰 뒤 이어서 말한다.
    /// 그새 플레이어가 말을 걸었으면 버리고, 너무 늦게 왔거나 창이 열려 있으면 다음 클릭 때 쓴다.
    /// </summary>
    private IEnumerator FetchPokeLine(int count)
    {
        _aiPokePending = true;
        _lastAiPokeTime = Time.unscaledTime;
        int serial = _chatSerial;
        float pokedAt = _lastPokeTime;
        string line = null;

        string url = $"{BaseUrl}/character/poke?name={UnityWebRequest.EscapeURL(characterName)}&count={count}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 30;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                try { line = JsonUtility.FromJson<LineResponse>(req.downloadHandler.text)?.line; } catch { }
            }
        }
        _aiPokePending = false;
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("(")) yield break;

        // 기본 대사 타이핑이 끝나고 한 박자 쉴 때까지 기다린다(그새 또 건드리면 그 대사 뒤로).
        while (_typeCo != null || Time.unscaledTime - _lastSayTime < 1.0f)
        {
            if (_chatSerial != serial || _busy) yield break;
            yield return null;
        }
        if (_chatSerial != serial || _busy) yield break;

        // 건드린 지 오래됐고 그 뒤로 안 건드렸으면 뜬금없으니 다음 클릭 때 쓴다.
        bool late = Time.unscaledTime - pokedAt > 10f && Time.unscaledTime - _lastPokeTime > 4f;
        bool covered = (_difficultyPanel != null && _difficultyPanel.activeSelf)
                    || (_memoryPanel != null && _memoryPanel.activeSelf)
                    || (_restoreHud != null && _restoreHud.activeSelf)
                    || _thinkCo != null;
        if (late || covered) { _aiPokeReady = line; yield break; }

        Say(line);   // 내용에 맞는 동작·표정은 VnGesturePlayer 가 고른다
    }

    private bool IsOnCharacter(Vector2 screenPos)
    {
        if (_character == null || _restoreHud.activeSelf) return false;
        return screenPos.x > Screen.width * 0.22f && screenPos.x < Screen.width * 0.78f
            && screenPos.y > Screen.height * 0.36f;   // 대사창·입력창 위쪽
    }

    private static bool PointerOverUI()
    {
        var es = EventSystem.current;
        if (es == null) return false;
        if (Input.touchCount > 0) return es.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
        return es.IsPointerOverGameObject();
    }

    private void OpenDifficulty()
    {
        _memoryPanel.SetActive(false);
        _difficultyPanel.SetActive(true);
        SelectDifficulty((int)AiDifficultySettings.Selected);
        if (!_busy)
        {
            Say(Pick(PlayLines), BlendShapePreset.Fun);
            if (_gestures != null) _gestures.Play(VnGesture.HandsOnHips, -1f, false);
        }
        // 고르는 동안 세비-호의 게임 시작 첫마디(기억 반영)를 미리 받아 둔다.
        HomeSession.PrefetchGameStartLine();
    }

    private void SelectDifficulty(int idx)
    {
        AiDifficultySettings.Selected = (AiDifficulty)idx;
        for (int i = 0; i < _difficultyButtons.Count; i++)
        {
            RetroTheme.StyleButton(_difficultyButtons[i], i == idx ? RetroTheme.Gold : RetroTheme.Inset);
            _difficultyLabels[i].color = i == idx ? RetroTheme.Ink : RetroTheme.Text;
        }
    }

    private void StartGame()
    {
        _difficultyPanel.SetActive(false);
        if (GameSceneManager.Instance != null) GameSceneManager.Instance.GoToCardGame();
        else SceneManager.LoadScene(GameSceneManager.Scenes.CardGame);
    }

    private void OpenMemory()
    {
        _difficultyPanel.SetActive(false);
        _memoryPanel.SetActive(true);
        _memoryText.text = $"<color=#{HexOf(RetroTheme.Muted)}>불러오는 중…</color>";
        StartCoroutine(FetchMemory());
    }

    // ══════════════════════════════════════════════════════════════════
    //  서버
    // ══════════════════════════════════════════════════════════════════

    [Serializable] private class LineResponse { public string speaker; public string line; }
    [Serializable] private class MemoryLine { public string player; public string reply; }
    [Serializable] private class MemoryResponse { public string name; public string[] notes; public MemoryLine[] recent; }

    private string BaseUrl => ServerClient.Instance != null ? ServerClient.Instance.BaseUrl : null;

    /// <summary>다가갔을 때 첫마디(기억·방금 게임 결과 반영). 기다리는 동안 placeholder 를 먼저 보여 준다.</summary>
    private IEnumerator FetchGreeting(string placeholder)
    {
        if (BaseUrl == null) { Greet(Pick(GreetFirst)); yield break; }
        if (!string.IsNullOrEmpty(placeholder)) Greet(placeholder);
        else StartThinking(null);

        string url = $"{BaseUrl}/player/approach?name={UnityWebRequest.EscapeURL(characterName)}";
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 60;
            yield return req.SendWebRequest();
            if (_playerSpoke || _busy) { StopThinking(); yield break; }   // 그새 플레이어가 먼저 말함
            string line = null;
            if (req.result == UnityWebRequest.Result.Success)
            {
                try { line = JsonUtility.FromJson<LineResponse>(req.downloadHandler.text)?.line; } catch { }
            }
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("(")) Greet(line);
            else if (string.IsNullOrEmpty(placeholder)) Greet(Pick(GreetFirst));
        }
    }

    /// <summary>방금 끝난 판의 결과별 기본 대사.</summary>
    private string AfterGameLine(HomeSession.GameRecord g)
    {
        if (g.highlights != null)
            foreach (var h in g.highlights)
                if (h.Contains("봉쇄") && h.Contains(characterName)) return Pick(AfterBlockaded);
        if (g.player_rank == 1) return Pick(AfterPlayerWin);
        if (g.char_rank == 1) return Pick(AfterSelfWin);
        return Pick(GreetAfterGame);
    }

    /// <summary>
    /// 카드게임에서 돌아왔을 때 — 세비-호가 방금 판(순위·명장면)에 대해 한마디.
    /// 결과 보고(관계 반영)가 끝나길 잠깐 기다렸다가 요청한다. 실패하면 결과별 기본 대사.
    /// </summary>
    private IEnumerator FetchGameReturn(HomeSession.GameRecord g)
    {
        StartThinking(null);
        float waited = 0f;
        while (HomeSession.ResultPending && waited < 8f) { waited += Time.unscaledDeltaTime; yield return null; }
        StartCoroutine(FetchRelation());
        if (_playerSpoke || _busy) yield break;
        if (BaseUrl == null) { StopThinking(); Greet(AfterGameLine(g)); yield break; }

        g.name = characterName;
        using (var req = new UnityWebRequest($"{BaseUrl}/game/return", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(g)));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 60;
            yield return req.SendWebRequest();
            if (_playerSpoke || _busy) yield break;   // 그새 플레이어가 먼저 말함(그쪽 응답이 이어서 나온다)
            string line = null;
            if (req.result == UnityWebRequest.Result.Success)
            {
                HomeSession.ServerOk = true;
                try { line = JsonUtility.FromJson<LineResponse>(req.downloadHandler.text)?.line; } catch { }
            }
            StopThinking();
            Greet(!string.IsNullOrWhiteSpace(line) && !line.StartsWith("(") ? line : AfterGameLine(g));
        }
    }

    private static readonly Regex IntimacyRe = new Regex("\"intimacy\"\\s*:\\s*(-?\\d+)");
    private static readonly Regex EmotionRe = new Regex("\"emotion\"\\s*:\\s*\"(\\w+)\"");

    private IEnumerator FetchRelation()
    {
        if (BaseUrl == null) yield break;
        using (var req = UnityWebRequest.Get($"{BaseUrl}/myroom/status"))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;
            string json = req.downloadHandler.text;
            // player_relations 안에서 이 캐릭터 블록의 intimacy
            int at = json.IndexOf("\"player_relations\"", StringComparison.Ordinal);
            int me = at >= 0 ? json.IndexOf($"\"{characterName}\"", at, StringComparison.Ordinal) : -1;
            if (me < 0) yield break;
            var m = IntimacyRe.Match(json, me);
            if (!m.Success) yield break;
            int intimacy = int.Parse(m.Groups[1].Value);
            var em = EmotionRe.Match(json, me);
            if (em.Success && _gestures != null) _gestures.SetMood(em.Groups[1].Value);   // 기분 → 자주 하는 동작

            int? prev = HomeSession.LastIntimacy;
            HomeSession.LastIntimacy = intimacy;
            ShowRelation(intimacy);
            if (prev.HasValue && prev.Value != intimacy) ShowRelationDelta(intimacy - prev.Value);
        }
    }

    /// <summary>명패: 하트 5칸만(단계 구간 -100~-60 / -20 / 20 / 60 / 100 → 1~5칸).</summary>
    private void ShowRelation(int intimacy)
    {
        int hearts = intimacy >= 60 ? 5 : intimacy >= 20 ? 4 : intimacy >= -20 ? 3 : intimacy >= -60 ? 2 : 1;
        _relationText.text = $"<color=#{HexOf(RetroTheme.Pink)}>{new string('♥', hearts)}</color>" +
                             $"<color=#{HexOf(RetroTheme.Faint)}>{new string('♥', 5 - hearts)}</color>";
    }

    /// <summary>명패 오른쪽에 +2 / -1 을 잠깐 띄운다(위로 살짝 떠오르며 사라짐).</summary>
    private void ShowRelationDelta(int delta)
    {
        if (_relationDelta == null || delta == 0) return;
        if (_deltaCo != null) StopCoroutine(_deltaCo);
        _deltaCo = StartCoroutine(RelationDeltaAnim(delta));
    }

    private IEnumerator RelationDeltaAnim(int delta)
    {
        _relationDelta.text = delta > 0 ? $"♥+{delta}" : $"{delta}";
        _relationDelta.color = delta > 0 ? RetroTheme.Pink : RetroTheme.Cyan;
        var rt = (RectTransform)_relationDelta.transform;
        const float total = 2.2f;
        for (float t = 0f; t < total; t += Time.unscaledDeltaTime)
        {
            float k = t / total;
            _relationDelta.alpha = k < 0.12f ? k / 0.12f : k > 0.7f ? 1f - (k - 0.7f) / 0.3f : 1f;
            rt.anchoredPosition = new Vector2(12f, Mathf.Lerp(-6f, 14f, k));
            if (_relationPlate != null)   // 명패가 톡 튀었다 돌아온다
                _relationPlate.localScale = Vector3.one * (1f + 0.06f * Mathf.Max(0f, 1f - t / 0.25f) * Mathf.Sin(t / 0.25f * Mathf.PI));
            yield return null;
        }
        _relationDelta.alpha = 0f;
        if (_relationPlate != null) _relationPlate.localScale = Vector3.one;
        _deltaCo = null;
    }

    /// <summary>답장 뒤 서버의 호감도 판정이 끝날 때까지 몇 초 간격으로 확인(바뀌면 바로 멈춤).</summary>
    private void WatchRelation()
    {
        if (_relWatchCo != null) StopCoroutine(_relWatchCo);
        _relWatchCo = StartCoroutine(WatchRelationCo());
    }

    private IEnumerator WatchRelationCo()
    {
        int? before = HomeSession.LastIntimacy;
        for (int i = 0; i < 12; i++)   // 최대 약 36초(CPU 판정이 밀려도)
        {
            yield return new WaitForSecondsRealtime(3f);
            if (_busy) continue;       // 다음 답장 기다리는 중엔 서버를 괴롭히지 않는다
            yield return FetchRelation();
            if (HomeSession.LastIntimacy != before) break;
        }
        _relWatchCo = null;
    }

    private IEnumerator FetchMemory()
    {
        if (BaseUrl == null) { _memoryText.text = OfflineLine; yield break; }
        string url = $"{BaseUrl}/memory/{UnityWebRequest.EscapeURL(characterName)}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { _memoryText.text = OfflineLine; yield break; }
            MemoryResponse data = null;
            try { data = JsonUtility.FromJson<MemoryResponse>(req.downloadHandler.text); } catch { }
            var sb = new System.Text.StringBuilder();
            if (data?.notes != null && data.notes.Length > 0)
            {
                foreach (var n in data.notes)
                    sb.Append($"<color=#{HexOf(RetroTheme.Pink)}>♥</color> ").Append(Escape(n)).Append('\n');
            }
            else sb.Append($"<color=#{HexOf(RetroTheme.Muted)}>아직 기억해 둔 이야기가 없어요.\n말을 걸면 기억해요.</color>\n");

            if (data?.recent != null && data.recent.Length > 0)
            {
                sb.Append($"\n<color=#{HexOf(RetroTheme.Gold)}>최근 대화</color>\n");
                int from = Mathf.Max(0, data.recent.Length - 3);
                for (int i = from; i < data.recent.Length; i++)
                    sb.Append($"<color=#{HexOf(RetroTheme.Muted)}>나: {Escape(data.recent[i].player)}\n" +
                              $"{characterName}: {Escape(data.recent[i].reply)}</color>\n");
            }
            _memoryText.text = sb.ToString();
        }
    }

    private IEnumerator ClearMemory()
    {
        if (BaseUrl == null) yield break;
        string url = $"{BaseUrl}/memory/clear?name={UnityWebRequest.EscapeURL(characterName)}";
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 10;
            yield return req.SendWebRequest();
        }
        StartCoroutine(FetchMemory());
        Say("…방금 뭐라고 했었죠? 기억 안 나요.", BlendShapePreset.Sorrow);
        if (_gestures != null) _gestures.Play(VnGesture.ThinkChin, -1f, false);
    }
}
