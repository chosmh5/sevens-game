using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 마이룸 관계 대시보드 — 오른쪽 슬라이드 패널.
/// /myroom/status API를 호출하여 캐릭터별 친밀도/신뢰도/감정 + 이벤트 로그 표시.
/// 토글 버튼으로 열고 닫을 수 있어 3D 도시 탐험과 공존 가능.
/// [RuntimeInitializeOnLoadMethod]로 MyRoom 씬 진입 시 자동 생성.
/// </summary>
public class MyRoomManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float refreshInterval = 10f;  // 관계도가 호감도 변화를 곧 반영하도록 짧게

    // ── 색상 ─────────────────────────────────────────────────────
    static readonly Color PANEL_BG   = new Color(0.06f, 0.06f, 0.14f, 0.88f);
    static readonly Color CARD_BG    = new Color(0.09f, 0.13f, 0.24f, 0.92f);
    static readonly Color ACCENT     = new Color(0.91f, 0.27f, 0.38f, 1f);
    static readonly Color GOLD       = new Color(1f, 0.85f, 0.35f, 1f);
    static readonly Color BAR_TRUST  = new Color(0.55f, 0.90f, 0.55f, 1f);  // 관계도 '친함' 초록
    static readonly Color TOGGLE_BG  = new Color(0.08f, 0.08f, 0.18f, 0.85f);

    // ── 패널 설정 ────────────────────────────────────────────────
    const float PANEL_WIDTH_RATIO = 0.32f; // 화면 폭의 32%
    const float SLIDE_DURATION = 0.3f;

    // ── UI 참조 ──────────────────────────────────────────────────
    private Canvas          _canvas;
    private RectTransform   _panelRT;
    private TMP_Text        _eventLogText;
    private TMP_Text        _topicText;
    private TMP_Text        _titleText;
    private CanvasGroup     _panelGroup;
    private GameObject      _toggleBtnGO;
    private TMP_Text        _toggleBtnText;
    private bool            _isOpen;
    private Coroutine       _slideCoroutine;

    // ── 자동 생성 ────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterSceneHandler()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        TryCreate(SceneManager.GetActiveScene().name);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryCreate(scene.name);
    }

    static void TryCreate(string sceneName)
    {
        if (sceneName != "MyRoom") return;
        if (FindAnyObjectByType<MyRoomManager>() != null) return;

        var go = new GameObject("[MyRoomManager]");
        go.AddComponent<MyRoomManager>();
        Debug.Log("[MyRoomManager] 마이룸 대시보드 자동 생성");
    }

    void Start()
    {
        BuildUI();
        StartCoroutine(InitialGraphLayout());
        StartCoroutine(LoadAndRefresh());
    }

    // 서버 데이터 도착 전, 레이아웃이 잡힌 다음 프레임에 노드/엣지를 제자리에 배치한다.
    IEnumerator InitialGraphLayout()
    {
        yield return null;
        UpdateGraph(new Dictionary<string, RelationData>(), new Dictionary<string, RelationData>());
    }

    IEnumerator LoadAndRefresh()
    {
        while (true)
        {
            yield return FetchStatus();
            yield return new WaitForSeconds(refreshInterval);
        }
    }

    IEnumerator FetchStatus()
    {
        if (ServerClient.Instance == null)
        {
            _titleText.text = "관계 대시보드 <size=14><color=#999>(서버 대기...)</color></size>";
            yield break;
        }

        string rawJson = null;
        yield return ServerClient.Instance.GetMyRoomStatus(
            json => rawJson = json,
            () => rawJson = null
        );

        if (rawJson == null)
        {
            _titleText.text = "관계 대시보드 <size=14><color=#999>(연결 실패)</color></size>";
            yield break;
        }

        _titleText.text = "관계 대시보드";
        ParseAndDisplay(rawJson);
    }

    // ══════════════════════════════════════════════════════════════
    //  토글
    // ══════════════════════════════════════════════════════════════

    public void TogglePanel()
    {
        _isOpen = !_isOpen;
        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(SlidePanel(_isOpen));
        _toggleBtnText.text = _isOpen ? ">" : "<";
    }

    IEnumerator SlidePanel(bool open)
    {
        float from = _panelRT.anchorMin.x;
        float to = open ? (1f - PANEL_WIDTH_RATIO) : 1f;
        float t = 0f;

        while (t < SLIDE_DURATION)
        {
            t += Time.deltaTime;
            float ratio = t / SLIDE_DURATION;
            float ease = 1f - Mathf.Pow(1f - ratio, 3f); // ease-out cubic
            float x = Mathf.Lerp(from, to, ease);
            _panelRT.anchorMin = new Vector2(x, 0f);
            // ★ 오른쪽 anchor도 함께 이동 — 열렸을 때 화면 오른쪽 끝(1.0)에 딱 맞아 잘리지 않게.
            _panelRT.anchorMax = new Vector2(x + PANEL_WIDTH_RATIO, 1f);
            _panelGroup.alpha = open ? ease : (1f - ease * 0.3f);
            yield return null;
        }

        _panelRT.anchorMin = new Vector2(to, 0f);
        _panelRT.anchorMax = new Vector2(to + PANEL_WIDTH_RATIO, 1f);
        _panelGroup.alpha = open ? 1f : 0.7f;

        // 토글 버튼 위치 업데이트
        UpdateToggleButtonPosition();
    }

    void UpdateToggleButtonPosition()
    {
        var btnRT = _toggleBtnGO.GetComponent<RectTransform>();
        float panelLeft = _panelRT.anchorMin.x;
        btnRT.anchorMin = new Vector2(panelLeft, 0.45f);
        btnRT.anchorMax = new Vector2(panelLeft, 0.55f);
        btnRT.pivot = new Vector2(1f, 0.5f);
        btnRT.sizeDelta = new Vector2(36f, 60f);
        btnRT.anchoredPosition = Vector2.zero;
    }

    // ══════════════════════════════════════════════════════════════
    //  JSON 파싱 & 표시
    // ══════════════════════════════════════════════════════════════

    void ParseAndDisplay(string json)
    {
        var playerRels = ParseRelationBlock(json, "player_relations");
        var charRels   = ParseRelationBlock(json, "char_relations");
        UpdateGraph(playerRels, charRels);

        // 주제 박스 (제목은 섹션이 따로 가지므로 본문만 채운다)
        var topics = ParseTopics(json);
        if (topics.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in topics)
                sb.AppendLine($"• {t}");
            _topicText.text = sb.ToString().TrimEnd();
        }
        else
        {
            _topicText.text = "<color=#999999>아직 저장된 주제가 없습니다.</color>";
        }

        // 이벤트 박스 (제목은 섹션이 따로 가지므로 본문만 채운다)
        var events = ParseRecentEvents(json);
        if (events.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ev in events)
            {
                string icon = ev.type == "card_game" ? "♠" : "💬";
                sb.AppendLine($"{icon} {ev.situation}");
            }
            _eventLogText.text = sb.ToString().TrimEnd();
        }
        else
        {
            _eventLogText.text = "<color=#999999>아직 이벤트가 없습니다.</color>";
        }
    }

    List<string> ParseTopics(string json)
    {
        var list = new List<string>();
        try
        {
            int idx = json.IndexOf("\"topics\"");
            if (idx < 0) return list;

            int arrStart = json.IndexOf('[', idx);
            if (arrStart < 0) return list;
            // topics 객체에는 중첩 배열이 없으므로 첫 ']' 가 곧 배열의 끝이다.
            int arrEnd = json.IndexOf(']', arrStart);
            if (arrEnd < 0) return list;

            string block = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            var pat = new Regex("\\{([^}]+)\\}");
            foreach (Match m in pat.Matches(block))
            {
                string text = ExtractString(m.Groups[1].Value, "text");
                if (!string.IsNullOrEmpty(text)) list.Add(text);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MyRoomManager] topics 파싱 실패: {e.Message}");
        }
        // 최신이 위로 오도록 뒤집는다
        list.Reverse();
        return list;
    }

    // ── 수동 JSON 파싱 (JsonUtility Dict 미지원) ─────────────────

    struct RelationData
    {
        public int intimacy;
        public int trust;
        public string emotion;
    }

    /// <summary>"player_relations" 또는 "char_relations" 블록을 파싱.
    /// 키 → RelationData. char_relations 의 키는 "A|B" 형태.</summary>
    Dictionary<string, RelationData> ParseRelationBlock(string json, string blockKey)
    {
        var dict = new Dictionary<string, RelationData>();
        try
        {
            int idx = json.IndexOf($"\"{blockKey}\"");
            if (idx < 0) return dict;

            int objStart = json.IndexOf('{', idx);
            if (objStart < 0) return dict;

            int objEnd = FindMatchingBrace(json, objStart);
            if (objEnd < 0) return dict;

            string block = json.Substring(objStart + 1, objEnd - objStart - 1);

            var charPattern = new Regex("\"([^\"]+)\"\\s*:\\s*\\{([^}]+)\\}");
            foreach (Match m in charPattern.Matches(block))
            {
                string charName = m.Groups[1].Value;
                string inner = m.Groups[2].Value;

                int intimacy = ExtractInt(inner, "intimacy");
                int trust = ExtractInt(inner, "trust");
                string emotion = ExtractString(inner, "emotion");

                dict[charName] = new RelationData
                {
                    intimacy = intimacy,
                    trust = trust,
                    emotion = emotion ?? "neutral"
                };
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MyRoomManager] {blockKey} 파싱 실패: {e.Message}");
        }
        return dict;
    }

    struct EventData
    {
        public string situation;
        public string type;
    }

    List<EventData> ParseRecentEvents(string json)
    {
        var list = new List<EventData>();
        try
        {
            int idx = json.IndexOf("\"recent_events\"");
            if (idx < 0) return list;

            int arrStart = json.IndexOf('[', idx);
            if (arrStart < 0) return list;

            int arrEnd = json.IndexOf(']', arrStart);
            if (arrEnd < 0) return list;

            string block = json.Substring(arrStart + 1, arrEnd - arrStart - 1);

            var evPattern = new Regex("\\{([^}]+)\\}");
            foreach (Match m in evPattern.Matches(block))
            {
                string inner = m.Groups[1].Value;
                string situation = ExtractString(inner, "situation");
                string type = ExtractString(inner, "type");
                if (!string.IsNullOrEmpty(situation))
                    list.Add(new EventData { situation = situation, type = type ?? "conversation" });
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MyRoomManager] recent_events 파싱 실패: {e.Message}");
        }
        return list;
    }

    int FindMatchingBrace(string s, int openPos)
    {
        int depth = 0;
        bool inStr = false;
        for (int i = openPos; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\'))
                inStr = !inStr;
            if (inStr) continue;
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    int ExtractInt(string s, string key)
    {
        var m = Regex.Match(s, $"\"{key}\"\\s*:\\s*(-?\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    string ExtractString(string s, string key)
    {
        var m = Regex.Match(s, $"\"{key}\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ══════════════════════════════════════════════════════════════
    //  UI 구축 — 오른쪽 슬라이드 패널
    // ══════════════════════════════════════════════════════════════

    void BuildUI()
    {
        // 캔버스
        var canvasGO = new GameObject("MyRoomDashboardCanvas");
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 15;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── 슬라이드 패널 (오른쪽, 초기 화면 밖) ──────────────────
        var panelGO = new GameObject("DashboardPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = PANEL_BG;
        _panelRT = panelGO.GetComponent<RectTransform>();
        _panelRT.anchorMin = new Vector2(1f, 0f); // 화면 밖 (닫힘)
        _panelRT.anchorMax = new Vector2(1f + PANEL_WIDTH_RATIO, 1f);
        _panelRT.offsetMin = Vector2.zero;
        _panelRT.offsetMax = Vector2.zero;

        _panelGroup = panelGO.AddComponent<CanvasGroup>();
        _panelGroup.alpha = 0.7f;

        // ── 토글 버튼 (패널 왼쪽 가장자리) ──────────────────────
        _toggleBtnGO = new GameObject("ToggleBtn");
        _toggleBtnGO.transform.SetParent(canvasGO.transform, false);
        var tbImg = _toggleBtnGO.AddComponent<Image>();
        tbImg.color = TOGGLE_BG;
        var tbRT = _toggleBtnGO.GetComponent<RectTransform>();
        tbRT.anchorMin = new Vector2(1f, 0.45f);
        tbRT.anchorMax = new Vector2(1f, 0.55f);
        tbRT.pivot = new Vector2(1f, 0.5f);
        tbRT.sizeDelta = new Vector2(36f, 60f);
        tbRT.anchoredPosition = Vector2.zero;
        var tbBtn = _toggleBtnGO.AddComponent<Button>();
        tbBtn.targetGraphic = tbImg;
        tbBtn.onClick.AddListener(TogglePanel);
        var arrowGO = CreateText(_toggleBtnGO.transform, "Arrow", "<", 22f, GOLD, FontStyles.Bold);
        StretchGO(arrowGO);
        _toggleBtnText = arrowGO.GetComponent<TMP_Text>();

        // ── 패널 내부 콘텐츠 ──────────────────────────────────────

        // 타이틀
        var titleGO = CreateText(panelGO.transform, "Title", "관계 대시보드", 22f, GOLD, FontStyles.Bold);
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 0.93f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.offsetMin = new Vector2(16f, 0f);
        titleRT.offsetMax = new Vector2(-16f, -6f);
        _titleText = titleGO.GetComponent<TMP_Text>();
        _titleText.alignment = TextAlignmentOptions.Left;

        // 관계도 영역 (호감도 미터기 대신 노드-엣지 그래프)
        BuildRelationshipGraph(panelGO.transform);

        // 하단: 주제 박스(위) + 이벤트 박스(아래) — 세로 스크롤로 드래그해 볼 수 있다.
        BuildBottomScroll(panelGO.transform);
    }

    // ══════════════════════════════════════════════════════════════
    //  관계도 (플레이어 + 3 NPC 노드, 6개 엣지)
    // ══════════════════════════════════════════════════════════════

    class GNode { public string name; public Vector2 norm; public RectTransform rt; }
    class GEdge { public string a; public string b; public RectTransform line; public TMP_Text label; public RectTransform labelRT; }

    RectTransform   _graphAreaRT;
    List<GNode>     _gnodes = new();
    List<GEdge>     _gedges = new();

    void BuildRelationshipGraph(Transform parent)
    {
        var areaGO = new GameObject("RelationshipGraph");
        areaGO.transform.SetParent(parent, false);
        _graphAreaRT = areaGO.AddComponent<RectTransform>();
        _graphAreaRT.anchorMin = new Vector2(0.03f, 0.35f);
        _graphAreaRT.anchorMax = new Vector2(0.97f, 0.92f);
        _graphAreaRT.offsetMin = Vector2.zero;
        _graphAreaRT.offsetMax = Vector2.zero;

        // 노드 위치(영역 내 정규화 좌표). 플레이어 아래, 3 NPC가 위쪽 다이아몬드.
        _gnodes.Add(new GNode { name = "플레이어", norm = new Vector2(0.50f, 0.14f) });
        _gnodes.Add(new GNode { name = "세비-호",  norm = new Vector2(0.24f, 0.58f) });
        _gnodes.Add(new GNode { name = "진",      norm = new Vector2(0.50f, 0.88f) });
        _gnodes.Add(new GNode { name = "류",      norm = new Vector2(0.76f, 0.58f) });

        // 엣지(모든 쌍 6개) — 노드보다 먼저 만들어 뒤에 깔리게 한다.
        for (int i = 0; i < _gnodes.Count; i++)
            for (int j = i + 1; j < _gnodes.Count; j++)
                _gedges.Add(BuildEdge(areaGO.transform, _gnodes[i].name, _gnodes[j].name));

        // 노드(엣지 위에)
        foreach (var n in _gnodes)
            n.rt = BuildNode(areaGO.transform, n.name);
    }

    GEdge BuildEdge(Transform parent, string a, string b)
    {
        var lineGO = new GameObject($"Edge_{a}_{b}");
        lineGO.transform.SetParent(parent, false);
        var img = lineGO.AddComponent<Image>();
        img.color = new Color(0.45f, 0.45f, 0.5f, 1f);
        var rt = lineGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);

        var labelGO = CreateText(parent, $"EdgeLabel_{a}_{b}", "잘 모르는 사이", 10f, Color.white, FontStyles.Bold);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0.5f, 0.5f);
        lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.pivot     = new Vector2(0.5f, 0.5f);
        lrt.sizeDelta = new Vector2(110f, 18f);

        return new GEdge { a = a, b = b, line = rt, label = labelGO.GetComponent<TMP_Text>(), labelRT = lrt };
    }

    RectTransform BuildNode(Transform parent, string name)
    {
        bool isPlayer = name == "플레이어";
        var nodeGO = new GameObject($"Node_{name}");
        nodeGO.transform.SetParent(parent, false);
        var img = nodeGO.AddComponent<Image>();
        img.color = isPlayer ? GOLD : new Color(0.16f, 0.22f, 0.36f, 1f);
        var rt = nodeGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(74f, 34f);

        var label = CreateText(nodeGO.transform, "Name", name, 13f,
                               isPlayer ? Color.black : Color.white, FontStyles.Bold);
        StretchGO(label);
        return rt;
    }

    /// <summary>관계 데이터로 노드 위치 + 엣지 색/두께/수치를 갱신한다.</summary>
    void UpdateGraph(Dictionary<string, RelationData> playerRels,
                     Dictionary<string, RelationData> charRels)
    {
        if (_graphAreaRT == null) return;
        Rect r = _graphAreaRT.rect;
        float w = r.width, h = r.height;
        if (w < 1f || h < 1f) return;   // 아직 레이아웃 전

        Vector2 Pos(Vector2 norm) => new Vector2((norm.x - 0.5f) * w, (norm.y - 0.5f) * h);

        // 노드 배치
        foreach (var n in _gnodes)
            if (n.rt != null) n.rt.anchoredPosition = Pos(n.norm);

        // 엣지 갱신
        foreach (var e in _gedges)
        {
            var na = _gnodes.Find(x => x.name == e.a);
            var nb = _gnodes.Find(x => x.name == e.b);
            if (na == null || nb == null) continue;

            Vector2 pa = Pos(na.norm);
            Vector2 pb = Pos(nb.norm);
            Vector2 mid = (pa + pb) * 0.5f;
            Vector2 dir = pb - pa;
            float dist = dir.magnitude;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            // 데이터가 없으면(서버 미연결/첫 상태) 친밀도 0으로 간주 → 기본 "잘 모르는 사이".
            int intimacy = LookupIntimacy(e.a, e.b, playerRels, charRels, out bool known);
            int shown = known ? intimacy : 0;
            float thickness = Mathf.Lerp(2.5f, 9f, Mathf.Abs(shown) / 100f);

            e.line.sizeDelta        = new Vector2(dist, thickness);
            e.line.anchoredPosition = mid;
            e.line.localEulerAngles = new Vector3(0f, 0f, angle);
            e.line.GetComponent<Image>().color = IntimacyColor(shown);

            e.labelRT.anchoredPosition = mid + new Vector2(0f, 10f);
            e.label.text  = IntimacyLabel(shown);
            e.label.color = IntimacyColor(shown);
        }
    }

    int LookupIntimacy(string a, string b,
                       Dictionary<string, RelationData> playerRels,
                       Dictionary<string, RelationData> charRels, out bool known)
    {
        known = false;
        // 플레이어가 낀 엣지 → player_relations (캐릭터 이름 키)
        if (a == "플레이어" || b == "플레이어")
        {
            string other = a == "플레이어" ? b : a;
            if (playerRels.TryGetValue(other, out var rd)) { known = true; return rd.intimacy; }
            return 0;
        }
        // NPC↔NPC → char_relations ("A|B" 키, 순서 모르니 둘 다 시도)
        if (charRels.TryGetValue($"{a}|{b}", out var r1)) { known = true; return r1.intimacy; }
        if (charRels.TryGetValue($"{b}|{a}", out var r2)) { known = true; return r2.intimacy; }
        return 0;
    }

    Color IntimacyColor(int intimacy)
    {
        var gray = new Color(0.5f, 0.5f, 0.55f, 1f);
        if (intimacy >= 10)
            return Color.Lerp(gray, BAR_TRUST, Mathf.InverseLerp(10f, 60f, intimacy)); // 친함=초록
        if (intimacy <= -10)
            return Color.Lerp(gray, ACCENT, Mathf.InverseLerp(-10f, -60f, intimacy));  // 나쁨=빨강
        return gray;                                                                    // 무관심=회색
    }

    /// <summary>친밀도를 관계 상태 텍스트로. 기본(0 근처)은 "잘 모르는 사이".</summary>
    string IntimacyLabel(int intimacy)
    {
        if (intimacy >= 50)  return "매우 친함";
        if (intimacy >= 15)  return "친해지는 중";
        if (intimacy <= -50) return "싫어함";
        if (intimacy <= -15) return "껄끄러움";
        return "잘 모르는 사이";
    }

    // ══════════════════════════════════════════════════════════════
    //  하단 스크롤 영역 (주제 + 이벤트)
    // ══════════════════════════════════════════════════════════════

    void BuildBottomScroll(Transform parent)
    {
        // 스크롤 루트 (기존 이벤트 영역과 동일한 하단 자리)
        var scrollGO = new GameObject("BottomScroll");
        scrollGO.transform.SetParent(parent, false);
        var scrollRT = scrollGO.AddComponent<RectTransform>();
        scrollRT.anchorMin = new Vector2(0.03f, 0.03f);
        scrollRT.anchorMax = new Vector2(0.97f, 0.33f);
        scrollRT.offsetMin = Vector2.zero;
        scrollRT.offsetMax = Vector2.zero;

        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal       = false;
        scroll.vertical         = true;
        scroll.movementType     = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        // 뷰포트 (마스크) — 영역 밖은 잘라낸다
        var viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var vpImg = viewportGO.AddComponent<Image>();
        vpImg.color = new Color(0.04f, 0.05f, 0.10f, 0.55f);
        vpImg.raycastTarget = true;                  // 드래그 입력을 받기 위해 필요
        viewportGO.AddComponent<RectMask2D>();
        var vpRT = viewportGO.GetComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero;
        vpRT.offsetMax = Vector2.zero;
        scroll.viewport = vpRT;

        // 콘텐츠 (위에서부터 쌓이는 세로 레이아웃, 내용만큼 높이 자동)
        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot     = new Vector2(0.5f, 1f);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;

        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 8f;
        vlg.childAlignment        = TextAnchor.UpperLeft;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var fitter = contentGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRT;

        // 주제 박스(위) — 기존 이벤트 박스 자리에
        _topicText = BuildScrollSection(contentGO.transform, "TopicSection",
            "<color=#FFD95A>대화 주제</color>", "<color=#999999>아직 저장된 주제가 없습니다.</color>");

        // 이벤트 박스(아래로 내림)
        _eventLogText = BuildScrollSection(contentGO.transform, "EventSection",
            "<color=#E94560>최근 소식</color>", "<color=#999999>아직 이벤트가 없습니다.</color>");
    }

    /// <summary>제목+본문을 담은 박스를 만들고, 본문 TMP를 반환한다(내용만큼 세로로 늘어남).</summary>
    TMP_Text BuildScrollSection(Transform parent, string name, string title, string bodyPlaceholder)
    {
        var panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        panel.AddComponent<RectTransform>();
        var img = panel.AddComponent<Image>();
        img.color = CARD_BG;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.spacing = 4f;
        vlg.childAlignment        = TextAnchor.UpperLeft;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var titleGO = CreateText(panel.transform, "Title", title, 14f, Color.white, FontStyles.Bold);
        var tTmp = titleGO.GetComponent<TMP_Text>();
        tTmp.alignment        = TextAlignmentOptions.TopLeft;
        tTmp.enableWordWrapping = true;

        var bodyGO = CreateText(panel.transform, "Body", bodyPlaceholder, 13f, Color.white, FontStyles.Normal);
        var bTmp = bodyGO.GetComponent<TMP_Text>();
        bTmp.alignment        = TextAlignmentOptions.TopLeft;
        bTmp.enableWordWrapping = true;

        return bTmp;
    }

    // ── 유틸 ─────────────────────────────────────────────────────

    GameObject CreateText(Transform parent, string name, string text,
                          float fontSize, Color color, FontStyles style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        return go;
    }

    void StretchGO(GameObject go, float padding = 0f)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }
}
