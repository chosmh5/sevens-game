using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// CardGame 씬 UI 를 레트로 디자인(1560×720 기준)으로 다시 배치·스타일링한다.
/// 씬 파일은 건드리지 않고, 씬이 열릴 때 CardGameManager 에 자동으로 붙는다(CardGameAddons 와 같은 방식).
///
/// - Canvas 스케일러: 1560×720, Expand(어떤 비율에서도 전체 레이아웃이 보이게)
/// - SafeArea 루트: 노치·홈 인디케이터를 피해 좌우 최소 32px, 아래 최소 16px
/// - 배경(사선 줄무늬 + 펠트 테이블), 좌석 명패 4개, 필드, 손패, 버튼, CRT 효과
/// </summary>
public class RetroCardGameSkin : MonoBehaviour
{
    [Tooltip("CRT 주사선·비네트 효과")]
    [SerializeField] private bool crtEffect = true;
    [Tooltip("낼 카드가 없을 때 자동으로 패스")]
    [SerializeField] private float autoPassDelay = 0.8f;

    private const string AutoPassPref = "sevens.autoPass";

    private CardGameManager _mgr;
    private FieldManager _field;
    private HandManager _hand;
    private Canvas _canvas;
    private RectTransform _safe;
    private RetroFieldView _fieldView;
    private RetroSeatPlate _myPlate;
    private readonly RetroSeatPlate[] _oppPlates = new RetroSeatPlate[3];
    private readonly OpponentProfile[] _profiles = new OpponentProfile[3];
    private Button _passButton, _undoButton, _menuButton;
    private Image _autoPassBox;
    private TextMeshProUGUI _autoPassText, _toast;
    private bool _autoPass;
    private int _turn;
    private int _aiPlays;
    private Coroutine _autoPassCo, _toastCo;
    private float _menuArmedUntil;
    private Rect _lastSafe;
    private Vector2Int _lastScreen;

    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _mgr = GetComponent<CardGameManager>();
        if (_mgr == null) _mgr = FindAnyObjectByType<CardGameManager>();
        _field = FindAnyObjectByType<FieldManager>();
        _hand = FindAnyObjectByType<HandManager>();
        if (_mgr == null || _field == null || _hand == null)
        {
            Debug.LogWarning("[RetroSkin] CardGameManager/FieldManager/HandManager 를 찾지 못해 스킨을 적용하지 않습니다.");
            enabled = false;
            return;
        }
        _canvas = FindMainCanvas();
        if (_canvas == null)
        {
            Debug.LogWarning("[RetroSkin] 카드게임 Canvas 없음");
            enabled = false;
            return;
        }
        _autoPass = PlayerPrefs.GetInt(AutoPassPref, 1) == 1;

        SetupScaler(_canvas.GetComponent<CanvasScaler>());
        BuildBackground();
        BuildSafeRoot();
        Relayout();
        BuildCrt();

        _mgr.GameStarted      += OnGameStarted;
        _mgr.TurnStarted      += OnTurnStarted;
        _mgr.PlayerPlayedCard += OnPlayed;
        _mgr.PlayerPassed     += OnPassed;
        _mgr.TurnStateChanged += RefreshHud;
        _hand.SelectionChanged += RefreshHud;
    }

    private IEnumerator Start()
    {
        // CanvasScaler 가 새 기준 해상도로 한 번 갱신된 뒤 SafeArea 를 다시 계산
        yield return null;
        ApplySafeArea();
        RefreshHud();
    }

    private void OnDestroy()
    {
        if (_mgr != null)
        {
            _mgr.GameStarted      -= OnGameStarted;
            _mgr.TurnStarted      -= OnTurnStarted;
            _mgr.PlayerPlayedCard -= OnPlayed;
            _mgr.PlayerPassed     -= OnPassed;
            _mgr.TurnStateChanged -= RefreshHud;
        }
        if (_hand != null) _hand.SelectionChanged -= RefreshHud;
        for (int i = 0; i < _profiles.Length; i++)
            if (_profiles[i] != null) _profiles[i].Changed -= RefreshHud;
    }

    private void Update()
    {
        // 화면 회전·창 크기 변경 시 SafeArea 다시 계산(값이 바뀔 때만).
        if (Screen.safeArea != _lastSafe || Screen.width != _lastScreen.x || Screen.height != _lastScreen.y)
            ApplySafeArea();
    }

    private Canvas FindMainCanvas()
    {
        var fieldArea = GameObject.Find("FieldArea");
        if (fieldArea != null)
        {
            var c = fieldArea.GetComponentInParent<Canvas>();
            if (c != null) return c.rootCanvas;
        }
        var anyProfile = FindAnyObjectByType<OpponentProfile>();
        return anyProfile != null ? anyProfile.GetComponentInParent<Canvas>().rootCanvas : null;
    }

    public static void SetupScaler(CanvasScaler scaler)
    {
        if (scaler == null) return;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = RetroTheme.ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referencePixelsPerUnit = 100f;
    }

    // ─────────────────────────────────────────────────────────────
    //  배경·SafeArea·CRT
    // ─────────────────────────────────────────────────────────────
    private void BuildBackground()
    {
        var root = (RectTransform)_canvas.transform;
        var bg = RetroTheme.Stretch(RetroTheme.Node("RetroBackground", root));
        bg.SetAsFirstSibling();
        var stripes = bg.gameObject.AddComponent<Image>();
        stripes.sprite = RetroTheme.StripeTile();
        stripes.type = Image.Type.Tiled;
        stripes.raycastTarget = false;

        // 펠트 테이블(위가 둥근 큰 판)
        var felt = RetroTheme.Node("Table", bg);
        felt.anchorMin = new Vector2(0.5f, 0f);
        felt.anchorMax = new Vector2(0.5f, 1f);
        felt.pivot = new Vector2(0.5f, 1f);
        felt.sizeDelta = new Vector2(1260f, 0f);
        felt.offsetMin = new Vector2(-630f, -120f);
        felt.offsetMax = new Vector2(630f, -56f);
        var feltImg = felt.gameObject.AddComponent<Image>();
        feltImg.sprite = RetroTheme.PanelSprite(120, 6);
        feltImg.type = Image.Type.Sliced;
        feltImg.color = RetroTheme.Felt;
        feltImg.raycastTarget = false;
        var rim = RetroTheme.Node("Rim", felt);
        RetroTheme.Stretch(rim, 9f);
        var rimImg = rim.gameObject.AddComponent<Image>();
        rimImg.sprite = RetroTheme.RingSprite(110, 4);
        rimImg.type = Image.Type.Sliced;
        rimImg.color = RetroTheme.FeltRim;
        rimImg.raycastTarget = false;
    }

    private void BuildSafeRoot()
    {
        _safe = RetroTheme.Node("RetroSafeArea", _canvas.transform);
        _safe.SetSiblingIndex(1); // 배경 바로 위
        ApplySafeArea();
    }

    private void ApplySafeArea()
    {
        if (_safe == null) return;
        _lastSafe = Screen.safeArea;
        _lastScreen = new Vector2Int(Screen.width, Screen.height);
        var canvasRT = (RectTransform)_canvas.transform;
        float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
        Vector2 size = canvasRT.rect.size;
        if (size.x <= 1f) size = RetroTheme.ReferenceResolution;
        Rect s = Screen.safeArea;
        float left   = s.xMin / sw * size.x;
        float right  = (sw - s.xMax) / sw * size.x;
        float bottom = s.yMin / sh * size.y;
        float top    = (sh - s.yMax) / sh * size.y;
        _safe.anchorMin = Vector2.zero;
        _safe.anchorMax = Vector2.one;
        _safe.pivot = new Vector2(0.5f, 0.5f);
        _safe.offsetMin = new Vector2(Mathf.Max(left, 32f), Mathf.Max(bottom, 16f));
        _safe.offsetMax = new Vector2(-Mathf.Max(right, 32f), -Mathf.Max(top, 8f));
    }

    private void BuildCrt()
    {
        if (!crtEffect) return;
        var go = new GameObject("RetroCRTCanvas");
        go.transform.SetParent(transform, false);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 40; // 결과 화면(30) 위
        SetupScaler(go.AddComponent<CanvasScaler>());

        var scan = RetroTheme.Stretch(RetroTheme.Node("Scanlines", go.transform));
        var si = scan.gameObject.AddComponent<Image>();
        si.sprite = RetroTheme.ScanlineTile();
        si.type = Image.Type.Tiled;
        si.raycastTarget = false;

        var vig = RetroTheme.Stretch(RetroTheme.Node("Vignette", go.transform));
        var vi = vig.gameObject.AddComponent<Image>();
        vi.sprite = RetroTheme.VignetteSprite();
        vi.raycastTarget = false;
    }

    // ─────────────────────────────────────────────────────────────
    //  배치
    // ─────────────────────────────────────────────────────────────
    private void Relayout()
    {
        var root = (RectTransform)_canvas.transform;

        // 기존 필드(스프라이트 칸)는 숨기고 레트로 필드로 대체
        var fieldArea = root.Find("FieldArea");
        if (fieldArea != null) fieldArea.gameObject.SetActive(false);
        _fieldView = RetroFieldView.Create(_safe, _field, _hand, _mgr);
        RetroTheme.Place((RectTransform)_fieldView.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                         new Vector2(0f, -98f), new Vector2(RetroFieldView.Width, RetroFieldView.Height));

        // 손패
        var handArea = root.Find("HandArea") as RectTransform;
        if (handArea != null)
        {
            handArea.SetParent(_safe, false);
            RetroTheme.Place(handArea, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 104f), new Vector2(760f, 160f));
            var fan = handArea.GetComponent<FanLayout>();
            if (fan != null) fan.Configure(1700f, 2.9f, 21.8f, -1700f);
        }

        // 상대 명패: 오른쪽(1)·위(2)·왼쪽(3)
        string[] names = { "RightProfile", "TopProfile", "LeftProfile" };
        for (int i = 0; i < 3; i++)
        {
            var t = root.Find(names[i]) as RectTransform;
            if (t == null) continue;
            var prof = t.GetComponent<OpponentProfile>();
            t.SetParent(_safe, false);
            t.localScale = Vector3.one;
            if (i == 1) RetroTheme.Place(t, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(380f, 82f));
            else if (i == 0) RetroTheme.Place(t, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -136f), new Vector2(160f, 236f));
            else RetroTheme.Place(t, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -136f), new Vector2(160f, 236f));

            // 기존 배경·텍스트는 숨김(OpponentProfile 은 계속 값을 갱신)
            var bg = t.GetComponent<Image>();
            if (bg != null) bg.enabled = false;
            for (int c = 0; c < t.childCount; c++) t.GetChild(c).gameObject.SetActive(false);

            _oppPlates[i] = RetroSeatPlate.Create(t, i != 1, false);
            if (prof != null)
            {
                _profiles[i] = prof;
                prof.Changed += RefreshHud;
            }
        }

        // 내 명패
        var mine = RetroTheme.Place(RetroTheme.Node("MyPlate", _safe), new Vector2(0f, 0f), new Vector2(0f, 0f),
                                    new Vector2(0f, 8f), new Vector2(300f, 90f));
        _myPlate = RetroSeatPlate.Create(mine, false, true);

        // 메뉴(왼쪽 위): 두 번 누르면 로비로
        _menuButton = RetroTheme.MakeButton(_safe, "MenuButton", RetroTheme.BtnRed, "", 22f);
        RetroTheme.Place((RectTransform)_menuButton.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -12f), new Vector2(72f, 66f));
        for (int b = 0; b < 3; b++)
        {
            var bar = RetroTheme.Place(RetroTheme.Node($"Bar{b}", _menuButton.transform), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                       new Vector2(0f, 12f - b * 11f), new Vector2(30f, 5f));
            var bi = bar.gameObject.AddComponent<Image>();
            bi.color = RetroTheme.Text;
            bi.raycastTarget = false;
        }
        _menuButton.onClick.AddListener(OnMenu);

        // 턴 종료/패스 버튼(기존 버튼 재사용 — GameManager 가 라벨·활성 상태를 관리)
        var pass = root.Find("PassButton") as RectTransform;
        if (pass != null)
        {
            pass.SetParent(_safe, false);
            RetroTheme.Place(pass, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 8f), new Vector2(320f, 80f));
            _passButton = pass.GetComponent<Button>();
            if (_passButton != null) RetroTheme.StyleButton(_passButton, RetroTheme.BtnOrange, 12);
            var label = pass.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                RetroTheme.Style(label, 44f, RetroTheme.Text, true);
                RetroTheme.AddTextShadow(label);
                RetroTheme.Stretch((RectTransform)label.transform);
            }
        }

        // 되돌리기(씬에 없어서 새로 만들고 GameManager 에 연결)
        _undoButton = RetroTheme.MakeButton(_safe, "UndoButton", RetroTheme.BtnBlue, "↶ 되돌리기", 22f);
        RetroTheme.Place((RectTransform)_undoButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 102f), new Vector2(170f, 66f));
        _mgr.BindUndoButton(_undoButton);

        // 자동 패스 토글
        var ap = RetroTheme.Node("AutoPass", _safe);
        RetroTheme.Place(ap, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-182f, 111f), new Vector2(138f, 48f));
        var apImg = ap.gameObject.AddComponent<Image>();
        apImg.sprite = RetroTheme.PanelSprite(8, 3);
        apImg.type = Image.Type.Sliced;
        apImg.color = RetroTheme.Inset;
        var apBtn = ap.gameObject.AddComponent<Button>();
        apBtn.targetGraphic = apImg;
        apBtn.onClick.AddListener(ToggleAutoPass);
        var box = RetroTheme.Place(RetroTheme.Node("Box", ap), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(18f, 18f));
        _autoPassBox = box.gameObject.AddComponent<Image>();
        _autoPassBox.sprite = RetroTheme.PanelSprite(0, 2);
        _autoPassBox.type = Image.Type.Sliced;
        _autoPassBox.raycastTarget = false;
        _autoPassText = RetroTheme.Label(ap, "Label", "자동 패스", 22f, RetroTheme.Accent, false, TextAlignmentOptions.Left);
        RetroTheme.Place((RectTransform)_autoPassText.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(96f, 30f));
        RefreshAutoPassVisual();

        // 안내 토스트(메뉴 확인 등)
        var toastRT = RetroTheme.Place(RetroTheme.Node("Toast", _safe), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                       new Vector2(0f, -500f), new Vector2(560f, 48f));
        RetroTheme.InsetBox(toastRT, RetroTheme.WithAlpha(RetroTheme.Ink, 0.92f));
        _toast = RetroTheme.Label(toastRT, "Text", "", 22f, RetroTheme.Text, false);
        RetroTheme.Stretch((RectTransform)_toast.transform);
        toastRT.gameObject.SetActive(false);

        // 기존 텍스트 폰트도 통일
        foreach (var t in _canvas.GetComponentsInChildren<TMP_Text>(true))
            if (t.font != RetroTheme.Regular && t.font != RetroTheme.Bold) t.font = RetroTheme.Regular;
    }

    // ─────────────────────────────────────────────────────────────
    //  게임 이벤트
    // ─────────────────────────────────────────────────────────────
    private void OnGameStarted()
    {
        _turn = 0;
        _aiPlays = 0;
        RefreshHud();
    }

    private void OnTurnStarted(Player p)
    {
        _turn++;
        _aiPlays = 0;
        RefreshHud();
        if (_autoPassCo != null) StopCoroutine(_autoPassCo);
        if (p != null && !p.IsAI && _autoPass) _autoPassCo = StartCoroutine(AutoPassRoutine());
    }

    private void OnPlayed(Player p, Card c)
    {
        if (p != null && p.IsAI) _aiPlays++;
        RefreshHud();
    }

    private void OnPassed(Player p) => RefreshHud();

    private IEnumerator AutoPassRoutine()
    {
        yield return new WaitForSeconds(autoPassDelay);
        if (!_autoPass || _mgr.CurrentState != CardGameManager.State.PlayerTurn) yield break;
        if (_mgr.PlaysThisTurn > 0 || _field.HasAnyPlayable(_hand.Hand)) yield break;
        if (_passButton != null && _passButton.interactable)
        {
            ShowToast("낼 카드가 없어 자동으로 패스");
            _passButton.onClick.Invoke();
        }
    }

    private void RefreshHud()
    {
        if (_mgr == null) return;
        var cur = _mgr.CurrentPlayer;
        int curSeat = cur != null ? cur.Index : -1;
        bool myTurn = _mgr.CurrentState == CardGameManager.State.PlayerTurn;

        int[] counts = _mgr.GetHandCounts();
        string[] names = new string[4];
        for (int i = 0; i < 4; i++) names[i] = _mgr.GetPlayerName(i);

        int plays = myTurn ? _mgr.PlaysThisTurn : (curSeat > 0 ? _aiPlays : 0);
        if (_fieldView != null)
            _fieldView.SetHeader(Mathf.Max(1, _turn), DifficultyLabel(), names, curSeat, plays, _mgr.MaxPlaysPerTurn);

        for (int i = 0; i < 3; i++)
        {
            int seat = i + 1;
            if (_oppPlates[i] == null) continue;
            int cnt = seat < counts.Length ? counts[seat] : 0;
            _oppPlates[i].Refresh(names[seat], cnt, _mgr.GetPassCount(seat), curSeat == seat);
        }
        if (_myPlate != null)
            _myPlate.Refresh(string.IsNullOrEmpty(names[0]) ? "플레이어" : names[0],
                             counts.Length > 0 ? counts[0] : 0, _mgr.GetPassCount(0), myTurn, myTurn);
    }

    private static string DifficultyLabel()
    {
        switch (AiDifficultySettings.Selected)
        {
            case AiDifficulty.Easy: return "EASY";
            case AiDifficulty.Hard: return "HARD";
            default:                return "NORMAL";
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  버튼
    // ─────────────────────────────────────────────────────────────
    private void ToggleAutoPass()
    {
        _autoPass = !_autoPass;
        PlayerPrefs.SetInt(AutoPassPref, _autoPass ? 1 : 0);
        RefreshAutoPassVisual();
        if (_autoPass && _mgr.CurrentState == CardGameManager.State.PlayerTurn)
        {
            if (_autoPassCo != null) StopCoroutine(_autoPassCo);
            _autoPassCo = StartCoroutine(AutoPassRoutine());
        }
    }

    private void RefreshAutoPassVisual()
    {
        if (_autoPassBox == null) return;
        _autoPassBox.color = _autoPass ? RetroTheme.Accent : RetroTheme.PanelColor;
        _autoPassText.color = _autoPass ? RetroTheme.Accent : RetroTheme.Muted;
        var img = _autoPassBox.transform.parent.GetComponent<Image>();
        if (img != null) img.color = _autoPass ? RetroTheme.Inset : RetroTheme.Darken(RetroTheme.Inset, 0.8f);
    }

    private void OnMenu()
    {
        if (Time.unscaledTime > _menuArmedUntil)
        {
            _menuArmedUntil = Time.unscaledTime + 2f;
            ShowToast("한 번 더 누르면 로비로 나가요");
            return;
        }
        if (GameSceneManager.Instance != null) GameSceneManager.Instance.GoToLobby();
        else SceneManager.LoadScene(GameSceneManager.Scenes.Lobby);
    }

    private void ShowToast(string msg)
    {
        if (_toast == null) return;
        if (_toastCo != null) StopCoroutine(_toastCo);
        _toastCo = StartCoroutine(ToastRoutine(msg));
    }

    private IEnumerator ToastRoutine(string msg)
    {
        var box = _toast.transform.parent.gameObject;
        _toast.text = msg;
        box.SetActive(true);
        yield return new WaitForSecondsRealtime(1.8f);
        box.SetActive(false);
    }
}

/// <summary>CardGame 씬이 열리면 레트로 스킨을 자동으로 붙인다.</summary>
public static class RetroCardGameSkinBootstrap
{
    private static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (!_hooked)
        {
            _hooked = true;
            SceneManager.sceneLoaded += (scene, mode) => Attach();
        }
        Attach();
    }

    private static void Attach()
    {
        if (!RetroTheme.Enabled) return;
        var mgr = Object.FindAnyObjectByType<CardGameManager>();
        if (mgr == null || mgr.GetComponent<RetroCardGameSkin>() != null) return;
        mgr.gameObject.AddComponent<RetroCardGameSkin>();
    }
}
