using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 플레이어가 AI 대화 중인 캐릭터에게 다가가면 활성화.
/// 카메라가 두 캐릭터를 프레임에 담고, 아래쪽에 자막 HUD가 뜬다.
/// [E] 끼어들기  [Esc] 그냥 떠나기
/// </summary>
public class ConversationWatchMode : MonoBehaviour
{
    public static ConversationWatchMode Instance { get; private set; }
    public static bool IsWatching { get; private set; }

    [Header("카메라 설정")]
    [SerializeField] private float watchSideOffset  = 3.2f;  // 캐릭터 옆으로 얼마나 비껴설지
    [SerializeField] private float watchHeightOffset = 1.8f;  // 높이
    [SerializeField] private float cameraLerpSpeed  = 2.5f;   // 카메라 이동 속도
    [SerializeField] private float lineHold         = 2.2f;   // 한 대사 표시 후 다음까지 대기

    // ── 내부 상태 ─────────────────────────────────────────────────────
    private CharacterDialogue _charA;
    private CharacterDialogue _charB;

    private Camera        _mainCam;
    private Behaviour     _camController;   // 플레이어 추적 카메라 스크립트(VillageCamera 또는 CameraFollow)
    private Transform     _playerTransform;

    private Vector3    _targetCamPos;
    private Quaternion _targetCamRot;
    private bool       _camMoving;

    // ── UI ────────────────────────────────────────────────────────────
    private GameObject  _hudRoot;
    private CanvasGroup _hudGroup;
    private TMP_Text    _speakerLabel;
    private TMP_Text    _lineLabel;
    private TMP_Text    _hintLabel;

    // 미리 받아온 대사 묶음 (ConversationManager 가 보관한 것을 그대로 재생)
    private ExchangeLine[] _lines;
    private string         _playerQuestion;
    private Coroutine      _playCo;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _mainCam      = Camera.main;
        // 카메라에 실제로 붙은 추적 스크립트를 잡는다(이 프로젝트는 VillageCamera 사용).
        _camController = _mainCam != null
            ? (_mainCam.GetComponent<VillageCamera>() as Behaviour
               ?? _mainCam.GetComponent<CameraFollow>() as Behaviour)
            : null;
        BuildHUD();
        SetHUDVisible(false, instant: true);
    }

    void Update()
    {
        if (!IsWatching) return;

        // 플레이어가 움직이려 하면 연출에서 빠져나와 조작을 돌려준다 (가두지 않기).
        bool wantsMove =
            Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
            Mathf.Abs(Input.GetAxisRaw("Vertical"))   > 0.1f ||
            (CyberpunkJoystick.Instance != null && CyberpunkJoystick.Instance.IsPressed) ||
            (VirtualJoystick.Instance  != null && VirtualJoystick.Instance.IsPressed);
        if (wantsMove)
        {
            ExitWatch();
            return;
        }

        // 카메라 스무스 이동
        if (_camMoving)
        {
            _mainCam.transform.position = Vector3.Lerp(
                _mainCam.transform.position, _targetCamPos, cameraLerpSpeed * Time.deltaTime);
            _mainCam.transform.rotation = Quaternion.Slerp(
                _mainCam.transform.rotation, _targetCamRot, cameraLerpSpeed * Time.deltaTime);

            // 목표에 가까워지면 이동 완료
            if (Vector3.Distance(_mainCam.transform.position, _targetCamPos) < 0.05f)
            {
                _mainCam.transform.position = _targetCamPos;
                _mainCam.transform.rotation = _targetCamRot;
                _camMoving = false;
            }
        }

        // 카메라 목표 지점 갱신 (캐릭터가 움직이면 따라감)
        if (_charA != null && _charB != null)
            UpdateCameraTarget();

        // 끼어들기(키보드 E) — 모바일은 HUD의 끼어들기 버튼이 같은 DoInterrupt()를 호출.
        if (Input.GetKeyDown(KeyCode.E))
        {
            DoInterrupt();
            return;
        }

        // 그냥 떠나기(키보드 Esc) — 매니저의 linger 가 알아서 흩어지게 둔다
        if (Input.GetKeyDown(KeyCode.Escape))
            ExitWatch();
    }

    /// <summary>끼어들기 — 셋이 함께 대화로 인계. (키보드 E / 모바일 버튼 공용)</summary>
    public void DoInterrupt()
    {
        if (!IsWatching) return;
        var a = _charA;
        var b = _charB;
        if (_playerTransform != null && a != null && b != null)
        {
            float distA = Vector3.Distance(_playerTransform.position, a.transform.position);
            float distB = Vector3.Distance(_playerTransform.position, b.transform.position);
            if (distB < distA) (a, b) = (b, a);   // a = 플레이어가 마주하는(응답 주체) 캐릭터
        }
        ExitWatch();
        ConversationManager.Instance?.HandToPlayer();
        PlayerInteraction.Instance?.BeginGroupConversation(a, b);
    }

    // ══════════════════════════════════════════════════════════════════
    //  공개 API
    // ══════════════════════════════════════════════════════════════════

    /// <summary>두 AI 캐릭터의 대화를 감상 모드로 시작</summary>
    public void TriggerWatch(CharacterDialogue a, CharacterDialogue b, Transform player)
    {
        if (IsWatching) return;

        _charA           = a;
        _charB           = b;
        _playerTransform = player;
        IsWatching       = true;

        // 추적 카메라 비활성화 (관전 동안 직접 카메라 제어 — 안 끄면 서로 다퉈 화면이 떨린다)
        if (_camController != null) _camController.enabled = false;

        UpdateCameraTarget();
        _camMoving = true;

        SetHUDVisible(true);

        // ConversationManager 가 이미 받아둔 묶음을 그대로 재생한다(추가 서버 호출 없음).
        var mgr = ConversationManager.Instance;
        _lines          = mgr != null ? mgr.CurrentLines : null;
        _playerQuestion = mgr != null ? mgr.CurrentPlayerQuestion : null;

        if (_playCo != null) StopCoroutine(_playCo);
        _playCo = StartCoroutine(PlayLines());
    }

    /// <summary>감상 모드 종료</summary>
    public void ExitWatch()
    {
        if (!IsWatching) return;
        IsWatching = false;

        if (_playCo != null) { StopCoroutine(_playCo); _playCo = null; }

        // 추적 카메라 복원
        if (_camController != null) _camController.enabled = true;

        SetHUDVisible(false);
        _charA = _charB = null;
    }

    // ══════════════════════════════════════════════════════════════════
    //  카메라
    // ══════════════════════════════════════════════════════════════════

    void UpdateCameraTarget()
    {
        if (_charA == null || _charB == null) return;

        Vector3 posA = _charA.transform.position;
        Vector3 posB = _charB.transform.position;
        Vector3 mid  = (posA + posB) * 0.5f + Vector3.up * 1.2f;

        // 두 캐릭터를 잇는 방향에 수직인 방향으로 카메라를 비껴 세움
        Vector3 between = (posB - posA);
        between.y = 0f;
        if (between.sqrMagnitude < 0.001f) between = Vector3.forward;
        between.Normalize();

        Vector3 side = Vector3.Cross(between, Vector3.up).normalized;

        _targetCamPos = mid + side * watchSideOffset + Vector3.up * watchHeightOffset;
        _targetCamRot = Quaternion.LookRotation(mid - _targetCamPos);
    }

    // ══════════════════════════════════════════════════════════════════
    //  대화 묶음 재생
    // ══════════════════════════════════════════════════════════════════

    IEnumerator PlayLines()
    {
        // 끼어들기/떠나기는 언제든 가능. 힌트만 진행 상황에 맞춰 갱신한다.
        _hintLabel.text = "[ E ] 끼어들기    [ Esc ] 그냥 두기";

        // 대사가 아직 서버에서 생성 중이면(LinesPending) 기다렸다가 받아온다.
        // (예전엔 진입 순간 null을 한 번 복사해 자막이 영영 안 떴다.)
        var mgr = ConversationManager.Instance;
        if ((_lines == null || _lines.Length == 0) && mgr != null && mgr.LinesPending)
        {
            int dots = 0;
            while (IsWatching && mgr.LinesPending)
            {
                _speakerLabel.text = "";
                _lineLabel.text    = "대화를 듣는 중" + new string('.', dots++ % 4);
                yield return new WaitForSeconds(0.3f);
            }
            if (!IsWatching) yield break;
            _lines          = mgr.CurrentLines;
            _playerQuestion = mgr.CurrentPlayerQuestion;
        }

        if (_lines != null && _lines.Length > 0)
        {
            foreach (var l in _lines)
            {
                if (!IsWatching) yield break;
                _speakerLabel.text = l.speaker;
                yield return TypewriterEffect(_lineLabel, l.line, 0.04f);
                yield return new WaitForSeconds(lineHold);
            }
        }
        else
        {
            // 생성 실패 등으로 대사를 못 받은 경우
            _speakerLabel.text = "";
            _lineLabel.text    = "(대화를 불러오지 못했어요)";
        }

        // 마무리: 가끔 캐릭터가 플레이어를 향해 한마디 → "대답하기"로 유도
        if (!string.IsNullOrEmpty(_playerQuestion))
        {
            if (!IsWatching) yield break;
            _speakerLabel.text = (_lines != null && _lines.Length > 0)
                ? _lines[_lines.Length - 1].speaker : "";
            yield return TypewriterEffect(_lineLabel, _playerQuestion, 0.04f);
            _hintLabel.text = "[ E ] 대답하기    [ Esc ] 그냥 두기";
        }
        else
        {
            _hintLabel.text = "[ E ] 끼어들기    [ Esc ] 그냥 두기";
        }
    }

    IEnumerator TypewriterEffect(TMP_Text label, string fullText, float charDelay)
    {
        label.text = "";
        foreach (char c in fullText)
        {
            label.text += c;
            yield return new WaitForSeconds(charDelay);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  HUD
    // ══════════════════════════════════════════════════════════════════

    void SetHUDVisible(bool show, bool instant = false)
    {
        if (_hudRoot == null) return;
        _hudRoot.SetActive(true);
        StopCoroutine("FadeHUD");
        StartCoroutine(FadeHUD(show ? 1f : 0f, instant ? 0f : 0.4f));
    }

    IEnumerator FadeHUD(float target, float duration)
    {
        float start = _hudGroup.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            _hudGroup.alpha = Mathf.Lerp(start, target, duration > 0 ? t / duration : 1f);
            yield return null;
        }
        _hudGroup.alpha = target;
        if (target == 0f) _hudRoot.SetActive(false);
    }

    void BuildHUD()
    {
        // 캔버스
        var canvasGO = new GameObject("WatchModeCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 12;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        // 루트 패널 — 화면 하단
        _hudRoot = new GameObject("WatchHUD");
        _hudRoot.transform.SetParent(canvasGO.transform, false);
        var panelImg = _hudRoot.AddComponent<Image>();
        panelImg.color = new Color(0.02f, 0.02f, 0.08f, 0.85f);
        var rt               = _hudRoot.GetComponent<RectTransform>();
        rt.anchorMin         = new Vector2(0.1f, 0f);
        rt.anchorMax         = new Vector2(0.9f, 0f);
        rt.pivot             = new Vector2(0.5f, 0f);
        rt.sizeDelta         = new Vector2(0f, 220f);
        rt.anchoredPosition  = new Vector2(0f, 24f);

        _hudGroup = _hudRoot.AddComponent<CanvasGroup>();

        // 화자 이름 (좌상단)
        _speakerLabel = MakeLabel(_hudRoot.transform, "", 14f,
                                  new Color(1f, 0.85f, 0.35f), FontStyles.Bold);
        var nameRT          = _speakerLabel.GetComponent<RectTransform>();
        nameRT.anchorMin    = new Vector2(0f, 1f);
        nameRT.anchorMax    = new Vector2(0.5f, 1f);
        nameRT.pivot        = new Vector2(0f, 1f);
        nameRT.offsetMin    = new Vector2(20f, -34f);
        nameRT.offsetMax    = new Vector2(0f, -6f);
        _speakerLabel.alignment = TextAlignmentOptions.Left;

        // 대사 (버튼 자리 확보 위해 하단 여백 키움)
        _lineLabel = MakeLabel(_hudRoot.transform, "", 16f, Color.white);
        _lineLabel.enableWordWrapping = true;
        _lineLabel.alignment          = TextAlignmentOptions.TopLeft;
        // 긴 대사도 박스 안에 다 들어오도록 자동 축소 (잘림 방지)
        _lineLabel.enableAutoSizing   = true;
        _lineLabel.fontSizeMin        = 12f;
        _lineLabel.fontSizeMax        = 16f;
        var lineRT       = _lineLabel.GetComponent<RectTransform>();
        lineRT.anchorMin = new Vector2(0f, 0f);
        lineRT.anchorMax = new Vector2(1f, 1f);
        lineRT.offsetMin = new Vector2(20f, 52f);
        lineRT.offsetMax = new Vector2(-20f, -36f);

        // 힌트 (좌하단, 키보드 안내)
        _hintLabel = MakeLabel(_hudRoot.transform,
                               "키보드: E 끼어들기 / Esc 나가기",
                               11f, new Color(0.5f, 0.5f, 0.5f));
        var hintRT          = _hintLabel.GetComponent<RectTransform>();
        hintRT.anchorMin    = new Vector2(0f, 0f);
        hintRT.anchorMax    = new Vector2(0.5f, 0f);
        hintRT.pivot        = new Vector2(0f, 0f);
        hintRT.sizeDelta    = new Vector2(0f, 24f);
        hintRT.offsetMin    = new Vector2(20f, 8f);
        hintRT.offsetMax    = new Vector2(0f, 32f);
        _hintLabel.alignment = TextAlignmentOptions.Left;

        // 탭 버튼 (우하단) — 모바일에서 클릭으로 끼어들기/나가기
        MakeButton(_hudRoot.transform, "끼어들기", new Color(0.2f, 0.45f, 0.85f, 1f),
                   new Vector2(-16f, 8f), 130f, DoInterrupt);
        MakeButton(_hudRoot.transform, "나가기", new Color(0.35f, 0.35f, 0.42f, 1f),
                   new Vector2(-154f, 8f), 110f, ExitWatch);

        _hudRoot.SetActive(false);
    }

    /// <summary>하단 우측에 탭 버튼 하나 생성(앵커=우하단).</summary>
    void MakeButton(Transform parent, string text, Color bg, Vector2 anchoredPos,
                    float width, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject($"Btn_{text}");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = bg;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(width, 38f);
        rt.anchoredPosition = anchoredPos;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var lbl = MakeLabel(go.transform, text, 15f, Color.white, FontStyles.Bold);
        lbl.alignment = TextAlignmentOptions.Center;
        var lrt = lbl.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
    }

    static TMP_Text MakeLabel(Transform parent, string text, float size, Color color,
                               FontStyles style = FontStyles.Normal)
    {
        var go  = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.fontStyle = style;
        return tmp;
    }
}
