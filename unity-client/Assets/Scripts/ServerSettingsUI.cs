using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// 게임 실행 시 서버 IP를 직접 입력받는 UI.
/// 입력값은 저장하지 않으므로 매 실행마다 다시 입력해야 한다.
/// ServerClient가 Awake에서 Spawn()으로 생성한다.
/// </summary>
public class ServerSettingsUI : MonoBehaviour
{
    static ServerSettingsUI _instance;

    /// <summary>서버 연결 확인에 성공하면 호출(홈 화면이 인사·관계를 다시 불러온다).</summary>
    public static event System.Action Connected;

    GameObject     _panel;
    TMP_InputField _input;
    TMP_Text       _status;

    public static void Spawn()
    {
        if (_instance != null) return;
        var go = new GameObject("ServerSettingsUI");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ServerSettingsUI>();
    }

    void Start()
    {
        EnsureEventSystem();
        Build();
        Open();   // 매 실행마다 자동으로 열어 입력을 유도
    }

    // ══════════════════════════════════════════════════════════
    //  동작
    // ══════════════════════════════════════════════════════════

    void Open()
    {
        if (_panel != null) _panel.SetActive(true);
        if (_input != null && ServerClient.Instance != null)
            _input.text = ServerClient.Instance.CurrentHost;
        if (_status != null) _status.text = "";
    }

    void OnConnect()
    {
        if (ServerClient.Instance == null) return;

        var raw = _input != null ? _input.text : "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            _status.text = "<color=#FF6666>IP를 입력하세요.</color>";
            return;
        }

        ServerClient.Instance.SetServerAddress(raw);
        StopAllCoroutines();
        StartCoroutine(TestConnection());
    }

    IEnumerator TestConnection()
    {
        _status.text = "연결 확인 중...";
        using var req = UnityWebRequest.Get(ServerClient.Instance.BaseUrl + "/conversation/status");
        ServerClient.ApplyUserId(req);
        req.timeout = 5;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            _status.text = "<color=#66FF66>✅ 연결 성공</color>";
            Connected?.Invoke();
            yield return new WaitForSeconds(0.8f);
            if (_panel != null) _panel.SetActive(false);
        }
        else
        {
            _status.text =
                $"<color=#FF6666>❌ 연결 실패\n{ServerClient.Instance.BaseUrl}\n서버 실행 / IP / 방화벽 확인</color>";
        }
    }

    // ══════════════════════════════════════════════════════════
    //  UI 생성 (코드로 빌드 — 씬/에디터 수작업 불필요)
    // ══════════════════════════════════════════════════════════

    void Build()
    {
        var canvasGO = new GameObject("Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;   // 다른 모든 UI보다 위에
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // 다시 열기 버튼 (항상 보임, 우상단)
        var reopen = MakeButton(canvasGO.transform, "⚙ 서버", Open,
                                new Color(0.25f, 0.25f, 0.3f, 0.9f));
        var rRT = reopen.GetComponent<RectTransform>();
        rRT.anchorMin = rRT.anchorMax = rRT.pivot = new Vector2(1f, 1f);
        rRT.anchoredPosition = new Vector2(-20f, -20f);
        rRT.sizeDelta        = new Vector2(180f, 70f);

        // 어두운 배경(클릭 차단) + 박스
        _panel = new GameObject("Panel");
        _panel.transform.SetParent(canvasGO.transform, false);
        var dim = _panel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.85f);
        StretchFull(_panel.GetComponent<RectTransform>());

        var box = new GameObject("Box");
        box.transform.SetParent(_panel.transform, false);
        var boxImg = box.AddComponent<Image>();
        boxImg.color = new Color(0.12f, 0.12f, 0.16f, 1f);
        var boxRT = box.GetComponent<RectTransform>();
        boxRT.anchorMin = boxRT.anchorMax = boxRT.pivot = new Vector2(0.5f, 0.5f);
        boxRT.sizeDelta = new Vector2(820f, 560f);

        // 제목
        var title = MakeLabel(box.transform, "서버 IP 입력", 44f,
                              new Color(1f, 0.85f, 0.35f), FontStyles.Bold);
        Place(title.rectTransform, top: -30f, height: 70f);

        // 안내
        var hint = MakeLabel(box.transform,
            "PC에서 실행 중인 서버의 IP를 입력하세요.\n(폰과 PC가 같은 Wi-Fi에 연결되어 있어야 함)",
            26f, new Color(0.8f, 0.8f, 0.8f));
        Place(hint.rectTransform, top: -120f, height: 90f);

        // 입력창
        _input = MakeInputField(box.transform,
            ServerClient.Instance != null ? ServerClient.Instance.CurrentHost : "");
        var iRT = _input.GetComponent<RectTransform>();
        iRT.anchorMin = iRT.anchorMax = iRT.pivot = new Vector2(0.5f, 0.5f);
        iRT.anchoredPosition = new Vector2(0f, 40f);
        iRT.sizeDelta        = new Vector2(680f, 100f);

        // 상태 텍스트
        _status = MakeLabel(box.transform, "", 26f, Color.white);
        var sRT = _status.rectTransform;
        sRT.anchorMin = sRT.anchorMax = sRT.pivot = new Vector2(0.5f, 0.5f);
        sRT.anchoredPosition = new Vector2(0f, -70f);
        sRT.sizeDelta        = new Vector2(760f, 110f);

        // 연결 버튼
        var connect = MakeButton(box.transform, "연결", OnConnect,
                                 new Color(0.2f, 0.45f, 0.85f, 1f));
        var cRT = connect.GetComponent<RectTransform>();
        cRT.anchorMin = cRT.anchorMax = cRT.pivot = new Vector2(0.5f, 0f);
        cRT.anchoredPosition = new Vector2(0f, 50f);
        cRT.sizeDelta        = new Vector2(680f, 100f);

        // 내 사용자 ID(서버는 이 ID별로 대화·기억을 따로 저장) — 위젯과 기억이 안 이어질 때 비교용
        var uid = ServerClient.UserId;
        var idLabel = MakeLabel(box.transform, $"내 ID: {(uid.Length > 12 ? uid.Substring(0, 12) + "…" : uid)}",
                                20f, new Color(0.55f, 0.55f, 0.6f));
        var idRT = idLabel.rectTransform;
        idRT.anchorMin = idRT.anchorMax = idRT.pivot = new Vector2(0.5f, 0f);
        idRT.anchoredPosition = new Vector2(0f, 10f);
        idRT.sizeDelta        = new Vector2(760f, 34f);
    }

    // ── 헬퍼 ───────────────────────────────────────────────────

    /// <summary>박스 상단 기준으로 가로 꽉 채워 배치.</summary>
    static void Place(RectTransform rt, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(30f, 0f);
        rt.offsetMax = new Vector2(-30f, 0f);
        rt.anchoredPosition = new Vector2(0f, top);
        rt.sizeDelta        = new Vector2(rt.sizeDelta.x, height);
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TMP_Text MakeLabel(Transform parent, string text, float size, Color color,
                              FontStyles style = FontStyles.Normal)
    {
        var go  = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text              = text;
        tmp.fontSize          = size;
        tmp.color             = color;
        tmp.fontStyle         = style;
        tmp.alignment         = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        return tmp;
    }

    static GameObject MakeButton(Transform parent, string label,
                                 UnityEngine.Events.UnityAction onClick, Color color)
    {
        var go  = new GameObject("Button");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        var txt = MakeLabel(go.transform, label, 34f, Color.white, FontStyles.Bold);
        StretchFull(txt.rectTransform);
        return go;
    }

    static TMP_InputField MakeInputField(Transform parent, string initial)
    {
        var go  = new GameObject("InputField");
        go.transform.SetParent(parent, false);
        var bg = go.AddComponent<Image>();
        bg.color  = Color.white;
        var field = go.AddComponent<TMP_InputField>();

        // 텍스트 영역(뷰포트)
        var area = new GameObject("Text Area");
        area.transform.SetParent(go.transform, false);
        area.AddComponent<RectMask2D>();
        var areaRT = area.GetComponent<RectTransform>();
        areaRT.anchorMin = Vector2.zero;
        areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(20f, 8f);
        areaRT.offsetMax = new Vector2(-20f, -8f);

        // placeholder
        var ph = new GameObject("Placeholder").AddComponent<TextMeshProUGUI>();
        ph.transform.SetParent(area.transform, false);
        ph.text      = "예: 192.168.0.10";
        ph.fontSize  = 38f;
        ph.color     = new Color(0.5f, 0.5f, 0.5f, 0.8f);
        ph.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFull(ph.rectTransform);

        // 실제 텍스트
        var txt = new GameObject("Text").AddComponent<TextMeshProUGUI>();
        txt.transform.SetParent(area.transform, false);
        txt.fontSize  = 38f;
        txt.color     = Color.black;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFull(txt.rectTransform);

        field.textViewport  = areaRT;
        field.textComponent = txt;
        field.placeholder   = ph;
        field.lineType      = TMP_InputField.LineType.SingleLine;
        field.contentType   = TMP_InputField.ContentType.Standard;
        field.keyboardType  = TouchScreenKeyboardType.URL;   // 숫자·점 입력 쉬운 모바일 키보드
        field.text          = initial;
        return field;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
        DontDestroyOnLoad(es);
    }
}
