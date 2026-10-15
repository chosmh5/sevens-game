using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// 카드게임 하이라이트 연출 + 캐릭터 반응.
///
/// - CardGameManager 의 턴 종료 이벤트를 SevensHighlightDetector 에 넘겨 하이라이트를 찾고,
/// - 화면 중앙 배너(강도에 따라 크기·색), 큰 순간엔 화면 번쩍임 + AI 턴 잠깐 멈춤,
/// - 상대 프로필 옆 말풍선으로 캐릭터(세비-호/류/진)가 한마디 한다.
/// - 대사는 게임 시작 때 서버(/game/reaction_bank)에서 LLM이 만든 묶음을 받아 쓰고, 없으면 기본 대사.
///
/// UI는 실행 중에 코드로 만든다(씬 수정 불필요). CardGameAddons 가 CardGame 씬에 자동으로 붙인다.
/// </summary>
public class HighlightDirector : MonoBehaviour
{
    public static HighlightDirector Instance { get; private set; }

    [Header("연출")]
    [SerializeField] private float bannerHold      = 1.1f;
    [SerializeField] private float bubbleHold      = 2.6f;
    [SerializeField] private float bigMomentHold   = 1.8f;  // 강도 3일 때 AI가 기다리는 시간
    [SerializeField] private float charCooldown    = 3.0f;  // 같은 캐릭터가 다시 말하기까지
    [SerializeField] private float globalCooldown  = 1.2f;  // 아무 캐릭터나 다시 말하기까지

    [Header("서버 대사")]
    [SerializeField] private bool  useServerLines  = true;
    [SerializeField] private float bankRetrySeconds = 20f;

    private CardGameManager _mgr;
    private SevensHighlightDetector _detector;

    private RectTransform _canvasRT;
    private RectTransform _bannerRT;
    private TMP_Text      _bannerText;
    private CanvasGroup   _bannerGroup;
    private Image         _flash;
    private Coroutine     _bannerCo;
    private readonly Dictionary<int, Bubble> _bubbles = new Dictionary<int, Bubble>();
    private readonly Dictionary<string, float> _lastSpoke = new Dictionary<string, float>();
    private float _lastAnySpoke = -99f;
    private int _rotate;

    private class Bubble
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public TMP_Text Text;
        public Coroutine Co;
    }

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _mgr = GetComponent<CardGameManager>();
        if (_mgr == null) _mgr = FindAnyObjectByType<CardGameManager>();
        if (_mgr == null)
        {
            Debug.LogWarning("[HighlightDirector] CardGameManager 없음 — 비활성화");
            enabled = false;
            return;
        }

        BuildUI();
        _mgr.GameStarted += OnGameStarted;
        _mgr.TurnEnded   += OnTurnEnded;

        // 이미 게임이 시작돼 있으면(실행 순서상 GameManager.Start 가 먼저 돈 경우) 바로 초기화
        if (_mgr.CurrentState != CardGameManager.State.Idle) OnGameStarted();

        if (useServerLines) StartCoroutine(FetchReactionBank());
    }

    private void OnDestroy()
    {
        if (_mgr != null)
        {
            _mgr.GameStarted -= OnGameStarted;
            _mgr.TurnEnded   -= OnTurnEnded;
        }
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────────────────────────
    //  게임 이벤트
    // ─────────────────────────────────────────────────────────────

    private void OnGameStarted()
    {
        _detector = new SevensHighlightDetector(Mathf.Max(_mgr.PlayerCount, 4), _mgr.MaxPlaysPerTurn);
        _detector.Reset(_mgr.GetHandCounts());
    }

    private void OnTurnEnded(Player p, IReadOnlyList<Card> played)
    {
        if (_detector == null) OnGameStarted();
        var events = _detector.OnTurnEnded(p.Index, played, _mgr.GetHandCounts());
        if (events.Count == 0) return;

        foreach (var e in events) Debug.Log($"[Highlight] {e}");

        // 한 턴에 여러 개가 나오면 가장 큰 것 하나를 연출(작은 것들은 기록만)
        HighlightEvent main = events[0];
        foreach (var e in events)
            if (e.Intensity > main.Intensity) main = e;

        ShowBanner(main);
        if (main.Intensity >= 3)
        {
            _mgr.HoldAI(bigMomentHold);
            StartCoroutine(Flash());
        }
        if (main.Intensity >= 2) React(main);
    }

    /// <summary>이번 판 하이라이트 요약(서버 결과 반응용). 예) ["플레이어 역전", "류 마지막 한 장"]</summary>
    public List<string> HighlightSummary() =>
        _detector != null ? _detector.Summary(SeatName) : new List<string>();

    // ─────────────────────────────────────────────────────────────
    //  캐릭터 반응
    // ─────────────────────────────────────────────────────────────

    private void React(HighlightEvent e)
    {
        string key = CharacterReactionLines.KeyOf(e.Type);
        if (key == null) return;

        if (e.Type == HighlightType.CloseRace)
        {
            int seat = 1 + (_rotate++ % 3);
            Say(seat, key, 0f);
            return;
        }

        if (e.Actor >= 1)
        {
            // AI 캐릭터가 주인공 → 본인이 한마디, 큰 순간이면 다른 캐릭터도 받아친다.
            Say(e.Actor, key + "_self", 0f);
            if (e.Intensity >= 3)
            {
                int rival = e.Other >= 1 && e.Other != e.Actor ? e.Other : OtherAiSeat(e.Actor);
                Say(rival, key + "_rival", 1.1f, ignoreGlobalCooldown: true);
            }
        }
        else if (e.Actor == 0)
        {
            // 플레이어가 주인공 → 관련된 캐릭터(역전당한 사람 등)나 돌아가며 한 명이 반응
            int seat = e.Other >= 1 ? e.Other : 1 + (_rotate++ % 3);
            Say(seat, key + "_player", 0.2f);
        }
    }

    private int OtherAiSeat(int notThis)
    {
        int seat = 1 + (_rotate++ % 3);
        if (seat == notThis) seat = 1 + (seat % 3);
        return seat;
    }

    private void Say(int seat, string key, float delay, bool ignoreGlobalCooldown = false)
    {
        string name = SeatName(seat);
        if (string.IsNullOrEmpty(name)) return;
        float now = Time.time + delay;
        if (_lastSpoke.TryGetValue(name, out var t) && now - t < charCooldown) return;
        if (!ignoreGlobalCooldown && now - _lastAnySpoke < globalCooldown) return;

        string line = CharacterReactionLines.Pick(name, key);
        if (string.IsNullOrEmpty(line)) return;
        _lastSpoke[name] = now;
        _lastAnySpoke = now;
        StartCoroutine(SayAfter(seat, line, delay));
    }

    private IEnumerator SayAfter(int seat, string line, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        var b = GetBubble(seat);
        if (b == null) yield break;
        PositionBubble(seat, b);
        b.Text.text = line;
        if (b.Co != null) StopCoroutine(b.Co);
        b.Co = StartCoroutine(PopBubble(b));
        var prof = _mgr.GetProfile(seat);
        if (prof != null) StartCoroutine(Punch((RectTransform)prof.transform, 1.08f, 0.25f));
    }

    private string SeatName(int seat) => seat == 0 ? "플레이어" : _mgr != null ? _mgr.GetPlayerName(seat) : "";

    // ─────────────────────────────────────────────────────────────
    //  배너 / 번쩍임
    // ─────────────────────────────────────────────────────────────

    private void ShowBanner(HighlightEvent e)
    {
        string who = e.Actor >= 1 ? SeatName(e.Actor) + " " : "";
        string text;
        switch (e.Type)
        {
            case HighlightType.LastCard:     text = who + "마지막 한 장!"; break;
            case HighlightType.Comeback:     text = e.Actor == 0 ? "역전!" : who + "역전!"; break;
            case HighlightType.TripleCombo:  text = who + "3장 몰아치기!"; break;
            case HighlightType.PassStreak:   text = who + (e.Count >= 4 ? "완전히 막혔다…" : "막혔다…"); break;
            case HighlightType.CloseRace:    text = "접전!"; break;
            case HighlightType.Reach:        text = who + $"{_mgr.GetHandCounts()[e.Actor]}장 남음"; break;
            case HighlightType.LineComplete: text = $"{e.Suit.ToSymbol()} 줄 완성"; break;
            default:                         text = SevensHighlightDetector.Label(e.Type); break;
        }

        Color color = e.Intensity >= 3 ? new Color(1f, 0.78f, 0.25f)
                    : e.Intensity == 2 ? new Color(0.45f, 0.9f, 1f)
                    : new Color(1f, 1f, 1f, 0.9f);
        float size = e.Intensity >= 3 ? 84f : e.Intensity == 2 ? 60f : 40f;

        _bannerText.text = text;
        _bannerText.color = color;
        _bannerText.fontSize = size;
        if (_bannerCo != null) StopCoroutine(_bannerCo);
        _bannerCo = StartCoroutine(PlayBanner(e.Intensity));
    }

    private IEnumerator PlayBanner(int intensity)
    {
        float hold = intensity >= 3 ? bannerHold + 0.5f : intensity == 2 ? bannerHold : bannerHold * 0.7f;
        float from = intensity >= 3 ? 1.6f : 1.25f;
        float t = 0f, dur = 0.22f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float ease = 1f - Mathf.Pow(1f - k, 3f);
            _bannerRT.localScale = Vector3.one * Mathf.Lerp(from, 1f, ease);
            _bannerGroup.alpha = k;
            yield return null;
        }
        _bannerRT.localScale = Vector3.one;
        _bannerGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(hold);
        t = 0f; dur = 0.3f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            _bannerGroup.alpha = 1f - Mathf.Clamp01(t / dur);
            yield return null;
        }
        _bannerGroup.alpha = 0f;
    }

    private IEnumerator Flash()
    {
        float t = 0f, dur = 0.45f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = t / dur;
            _flash.color = new Color(1f, 0.92f, 0.6f, 0.28f * (1f - k));
            yield return null;
        }
        _flash.color = new Color(1f, 1f, 1f, 0f);
    }

    private IEnumerator PopBubble(Bubble b)
    {
        b.Root.gameObject.SetActive(true);
        float t = 0f, dur = 0.18f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            // easeOutBack: 살짝 커졌다가 제자리
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float back = 1f + c3 * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f);
            b.Root.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, back);
            b.Group.alpha = k;
            yield return null;
        }
        b.Root.localScale = Vector3.one;
        b.Group.alpha = 1f;
        yield return new WaitForSecondsRealtime(bubbleHold);
        t = 0f; dur = 0.3f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            b.Group.alpha = 1f - Mathf.Clamp01(t / dur);
            yield return null;
        }
        b.Root.gameObject.SetActive(false);
    }

    private static IEnumerator Punch(RectTransform rt, float scale, float dur)
    {
        if (rt == null) yield break;
        Vector3 baseScale = rt.localScale;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI);
            rt.localScale = baseScale * Mathf.Lerp(1f, scale, k);
            yield return null;
        }
        rt.localScale = baseScale;
    }

    // ─────────────────────────────────────────────────────────────
    //  UI 생성
    // ─────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var canvasGO = new GameObject("HighlightCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;   // 카드게임 UI(10) 위, 결과 화면(30) 아래
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRT = (RectTransform)canvasGO.transform;

        // 화면 번쩍임
        var flashGO = new GameObject("Flash");
        flashGO.transform.SetParent(canvasGO.transform, false);
        _flash = flashGO.AddComponent<Image>();
        _flash.color = new Color(1f, 1f, 1f, 0f);
        _flash.raycastTarget = false;
        Stretch((RectTransform)flashGO.transform);

        // 배너(필드 위쪽 가운데)
        var bannerGO = new GameObject("HighlightBanner");
        bannerGO.transform.SetParent(canvasGO.transform, false);
        _bannerRT = bannerGO.AddComponent<RectTransform>();
        _bannerRT.anchorMin = _bannerRT.anchorMax = new Vector2(0.5f, 0.5f);
        _bannerRT.sizeDelta = new Vector2(1200f, 140f);
        _bannerRT.anchoredPosition = new Vector2(0f, 120f);
        _bannerGroup = bannerGO.AddComponent<CanvasGroup>();
        _bannerGroup.alpha = 0f;
        _bannerGroup.blocksRaycasts = false;
        _bannerGroup.interactable = false;
        _bannerText = bannerGO.AddComponent<TextMeshProUGUI>();
        _bannerText.alignment = TextAlignmentOptions.Center;
        _bannerText.fontStyle = FontStyles.Bold;
        _bannerText.raycastTarget = false;
        _bannerText.outlineWidth = 0.18f;
        _bannerText.outlineColor = new Color32(20, 16, 30, 255);
    }

    private Bubble GetBubble(int seat)
    {
        if (_bubbles.TryGetValue(seat, out var existing)) return existing;
        if (_mgr.GetProfile(seat) == null) return null;

        var go = new GameObject($"Bubble_{seat}");
        go.transform.SetParent(_canvasRT, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(380f, 110f);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.09f, 0.14f, 0.92f);
        bg.raycastTarget = false;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.45f, 0.9f, 1f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);
        var group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var nameGO = new GameObject("Name");
        nameGO.transform.SetParent(go.transform, false);
        var nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
        nameTMP.text = SeatName(seat);
        nameTMP.fontSize = 20f;
        nameTMP.fontStyle = FontStyles.Bold;
        nameTMP.color = new Color(0.45f, 0.9f, 1f);
        nameTMP.alignment = TextAlignmentOptions.TopLeft;
        nameTMP.raycastTarget = false;
        var nameRT = (RectTransform)nameGO.transform;
        nameRT.anchorMin = new Vector2(0f, 1f);
        nameRT.anchorMax = new Vector2(1f, 1f);
        nameRT.pivot = new Vector2(0.5f, 1f);
        nameRT.offsetMin = new Vector2(16f, -34f);
        nameRT.offsetMax = new Vector2(-16f, -8f);

        var textGO = new GameObject("Line");
        textGO.transform.SetParent(go.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 24f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 16f;
        tmp.fontSizeMax = 24f;
        tmp.raycastTarget = false;
        var textRT = (RectTransform)textGO.transform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(16f, 10f);
        textRT.offsetMax = new Vector2(-16f, -36f);

        go.SetActive(false);
        var b = new Bubble { Root = rt, Group = group, Text = tmp };
        _bubbles[seat] = b;
        return b;
    }

    /// <summary>상대 프로필의 화면 위치를 보고 말풍선을 옆(왼쪽 좌석은 오른쪽, 오른쪽 좌석은 왼쪽, 위 좌석은 아래)에 둔다.</summary>
    private void PositionBubble(int seat, Bubble b)
    {
        var prof = _mgr.GetProfile(seat);
        if (prof == null) return;
        var corners = new Vector3[4];
        ((RectTransform)prof.transform).GetWorldCorners(corners);
        // 카드게임 캔버스는 Screen Space Overlay → 월드 좌표 = 화면 좌표
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (var c in corners)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, c, null, out var local);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        Vector2 center = (min + max) * 0.5f;
        Vector2 half = (max - min) * 0.5f;
        Vector2 bubbleHalf = b.Root.sizeDelta * 0.5f;
        float canvasHalfW = _canvasRT.rect.width * 0.5f;
        const float gap = 18f;

        Vector2 pos;
        if (center.x > canvasHalfW * 0.5f)        pos = new Vector2(center.x - half.x - gap - bubbleHalf.x, center.y);   // 오른쪽 좌석
        else if (center.x < -canvasHalfW * 0.5f)  pos = new Vector2(center.x + half.x + gap + bubbleHalf.x, center.y);   // 왼쪽 좌석
        else                                      pos = new Vector2(center.x + half.x + gap + bubbleHalf.x, center.y);   // 위 좌석: 오른쪽 옆
        b.Root.anchoredPosition = pos;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ─────────────────────────────────────────────────────────────
    //  서버 대사 묶음
    // ─────────────────────────────────────────────────────────────

    [Serializable] private class BankLine { public string character; public string key; public string line; }
    [Serializable] private class BankResponse { public BankLine[] lines; public string[] pending; }

    private IEnumerator FetchReactionBank()
    {
        if (ServerClient.Instance == null) yield break;   // 서버 설정이 없으면 기본 대사만
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using (var req = UnityWebRequest.Get($"{ServerClient.Instance.BaseUrl}/game/reaction_bank"))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[HighlightDirector] 반응 대사 서버 연결 실패 — 기본 대사 사용 ({req.error})");
                    yield break;
                }
                BankResponse data = null;
                try { data = JsonUtility.FromJson<BankResponse>(req.downloadHandler.text); }
                catch (Exception e) { Debug.LogWarning($"[HighlightDirector] 반응 대사 해석 실패: {e.Message}"); }
                if (data?.lines != null)
                    foreach (var l in data.lines) CharacterReactionLines.AddServerLine(l.character, l.key, l.line);
                Debug.Log($"[HighlightDirector] 서버 반응 대사 {CharacterReactionLines.ServerLineCount}개");
                // 서버가 아직 만드는 중인 캐릭터가 있으면 조금 뒤 다시 받아 온다.
                if (data?.pending == null || data.pending.Length == 0) yield break;
            }
            yield return new WaitForSeconds(bankRetrySeconds);
        }
    }
}

/// <summary>
/// CardGame 씬이 열리면 HighlightDirector 와 GameResultReporter 를 자동으로 붙인다(씬 수정 불필요).
/// </summary>
public static class CardGameAddons
{
    private static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (!_hooked)
        {
            _hooked = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => Attach();
        }
        Attach();
    }

    private static void Attach()
    {
        var mgr = UnityEngine.Object.FindAnyObjectByType<CardGameManager>();
        if (mgr == null) return;
        if (UnityEngine.Object.FindAnyObjectByType<HighlightDirector>() == null)
            mgr.gameObject.AddComponent<HighlightDirector>();
        if (UnityEngine.Object.FindAnyObjectByType<GameResultReporter>() == null)
            mgr.gameObject.AddComponent<GameResultReporter>();
    }
}
