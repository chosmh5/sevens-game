using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class ServerClient : MonoBehaviour
{
    public static ServerClient Instance { get; private set; }

    [SerializeField] private string serverUrl = "http://192.168.35.186:8000";

    /// <summary>현재 사용 중인 서버 주소 (다른 스크립트가 참조).</summary>
    public string BaseUrl => serverUrl;

    /// <summary>현재 서버 호스트(IP)만 추출. 입력창 기본값 표시용.</summary>
    public string CurrentHost
    {
        get { try { return new Uri(serverUrl).Host; } catch { return serverUrl; } }
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 서버 주소는 저장하지 않는다. 매 실행마다 사용자가 직접 입력한다.
        ServerSettingsUI.Spawn();
    }

    /// <summary>인게임에서 입력한 서버 주소를 정규화해 적용한다(저장하지 않음).</summary>
    public void SetServerAddress(string raw)
    {
        var url = NormalizeUrl(raw);
        if (!string.IsNullOrEmpty(url))
            serverUrl = url;
    }

    /// <summary>IP만 / ip:port / 전체 URL 어떤 형태로 입력해도 http URL로 정규화.</summary>
    static string NormalizeUrl(string raw)
    {
        raw = (raw ?? "").Trim().TrimEnd('/');
        if (raw.Length == 0)     return "";
        if (raw.Contains("://")) return raw;
        if (raw.Contains(":"))   return "http://" + raw;
        return $"http://{raw}:8000";
    }

    // ══════════════════════════════════════════════════════════════
    //  사용자 ID — 서버가 사람마다 대화·기억·관계를 따로 저장하도록 모든 요청에 X-User-Id 로 붙인다.
    //  - 기본값: 기기 고유 ID. 안드로이드는 ANDROID_ID 의 MD5 라서, 같은 폰·같은 서명 키로 빌드한
    //    위젯 앱(android-app)과 같은 값이 나온다 → 위젯에서 나눈 대화도 같은 기억으로 이어진다.
    //  - PC 한 대에서 여러 명을 흉내 낼 때: 실행 인자  -userId 이름   (예: SevensGame.exe -userId test2)
    //  - 기기 ID를 못 얻는 환경이면 처음 한 번 만든 랜덤 ID를 PlayerPrefs 에 저장해 계속 쓴다.
    // ══════════════════════════════════════════════════════════════

    const string UserIdPrefKey = "sevens_user_id";
    static string _userId;

    public static string UserId => _userId ??= ResolveUserId();

    static string ResolveUserId()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-userId") continue;
            var fromArg = SanitizeUserId(args[i + 1]);
            if (fromArg.Length > 0) return fromArg;
        }

        var device = SystemInfo.deviceUniqueIdentifier;
        if (!string.IsNullOrEmpty(device) && device != SystemInfo.unsupportedIdentifier)
        {
            var fromDevice = SanitizeUserId(device);
            if (fromDevice.Length > 0) return fromDevice;
        }

        var saved = SanitizeUserId(PlayerPrefs.GetString(UserIdPrefKey, ""));
        if (saved.Length == 0)
        {
            saved = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(UserIdPrefKey, saved);
            PlayerPrefs.Save();
        }
        return saved;
    }

    /// <summary>서버와 같은 규칙: 소문자 영문·숫자·-·_ 만, 최대 64자.</summary>
    static string SanitizeUserId(string raw)
    {
        var sb = new StringBuilder();
        foreach (char ch in (raw ?? "").ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_')
                sb.Append(ch);
            if (sb.Length >= 64) break;
        }
        return sb.ToString();
    }

    /// <summary>직접 UnityWebRequest 를 만드는 스크립트(HomeController 등)도 보내기 전에 이걸 불러야 한다.</summary>
    public static void ApplyUserId(UnityWebRequest req)
    {
        req.SetRequestHeader("X-User-Id", UserId);
    }

    // ══════════════════════════════════════════════════════════════
    //  공통 요청 도우미
    //  - 응답 JSON 이 깨져 있어도 코루틴이 예외로 죽지 않고 onFail 로 넘어간다(예전엔 '생각 중…'에서 멈췄다).
    //  - 실패 원인을 로그로 남긴다.
    // ══════════════════════════════════════════════════════════════

    const int LongTimeout  = 240;   // LLM 생성(노트북 CPU라 오래 걸릴 수 있음)
    const int ShortTimeout = 10;    // 저장·조회만 하는 요청

    static UnityWebRequest NewPost(string url, string jsonBody, int timeout)
    {
        var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(jsonBody == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(jsonBody));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        ApplyUserId(req);
        req.timeout = timeout;
        return req;
    }

    static UnityWebRequest NewGet(string url, int timeout)
    {
        var req = UnityWebRequest.Get(url);
        ApplyUserId(req);
        req.timeout = timeout;
        return req;
    }

    /// <summary>요청을 보내고, 성공하면 JSON 을 T 로 해석해 넘긴다. 연결 실패·해석 실패는 onFail.</summary>
    static IEnumerator Send<T>(UnityWebRequest req, Action<T> onSuccess, Action onFail) where T : class
    {
        using (req)
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[ServerClient] {req.method} {ShortUrl(req.url)} 실패: {req.error}");
                onFail?.Invoke();
                yield break;
            }

            T data = null;
            try { data = JsonUtility.FromJson<T>(req.downloadHandler.text); }
            catch (Exception e) { Debug.LogWarning($"[ServerClient] {ShortUrl(req.url)} 응답 해석 실패: {e.Message}"); }

            if (data == null) { onFail?.Invoke(); yield break; }
            onSuccess?.Invoke(data);
        }
    }

    /// <summary>결과 본문이 필요 없는 요청(저장·초기화). 성공/실패와 상관없이 onDone.</summary>
    static IEnumerator SendNoResult(UnityWebRequest req, Action onDone)
    {
        using (req)
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.Log($"[ServerClient] {req.method} {ShortUrl(req.url)} 실패: {req.error}");
        }
        onDone?.Invoke();
    }

    static string ShortUrl(string url)
    {
        int q = url.IndexOf('?');
        return q >= 0 ? url.Substring(0, q) : url;
    }

    // ══════════════════════════════════════════════════════════════
    //  API
    // ══════════════════════════════════════════════════════════════

    public IEnumerator GetCharacterLine(string characterName, Action<string> onSuccess, Action onFail = null)
    {
        string url = string.IsNullOrEmpty(characterName)
            ? $"{serverUrl}/character/line"
            : $"{serverUrl}/character/line?name={UnityWebRequest.EscapeURL(characterName)}";
        return Send<LineResponse>(NewGet(url, LongTimeout), d => onSuccess?.Invoke(d.line), onFail);
    }

    public IEnumerator GetConversationNext(Action<string, string> onSuccess, Action onFail = null)
    {
        return Send<NextLineResponse>(NewPost($"{serverUrl}/conversation/next", null, LongTimeout),
                                      d => onSuccess?.Invoke(d.speaker, d.line), onFail);
    }

    public IEnumerator SetActiveCharacters(string[] names, Action onDone = null)
    {
        var body = JsonUtility.ToJson(new SetCharactersRequest { characters = names ?? Array.Empty<string>() });
        return SendNoResult(NewPost($"{serverUrl}/characters/set", body, ShortTimeout), onDone);
    }

    public IEnumerator ClearConversation(Action onDone = null)
    {
        return SendNoResult(NewPost($"{serverUrl}/conversation/clear", null, ShortTimeout), onDone);
    }

    public IEnumerator SendUserMessage(string message, string target, Action<string> onSuccess, Action onFail = null)
    {
        // target(지금 말 거는 캐릭터)을 명시해 서버가 엉뚱한 캐릭터로 답하지 않게 한다.
        var body = $"{{\"message\":\"{EscapeJson(message)}\",\"target\":\"{EscapeJson(target)}\"}}";
        return Send<ConversationResponse>(NewPost($"{serverUrl}/conversation/user", body, LongTimeout),
                                          d => onSuccess?.Invoke(d.reply), onFail);
    }

    /// <summary>플레이어 한 마디에 여러 캐릭터가 차례로 반응한다(진짜 3자 대화).</summary>
    public IEnumerator SendGroupMessage(string message, string[] characters,
                                        Action<ExchangeLine[]> onSuccess, Action onFail = null)
    {
        var sb = new StringBuilder();
        if (characters != null)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(EscapeJson(characters[i])).Append('"');
            }
        }
        var body = $"{{\"message\":\"{EscapeJson(message)}\",\"characters\":[{sb}]}}";
        return Send<GroupReplyResult>(NewPost($"{serverUrl}/conversation/group", body, LongTimeout),
                                      d => onSuccess?.Invoke(d.replies), onFail);
    }

    /// <summary>플레이어가 제시한 대화 주제를 서버에 저장한다(이후 캐릭터들이 이 주제로 대화).</summary>
    public IEnumerator AddTopic(string text, Action onDone = null)
    {
        var body = $"{{\"text\":\"{EscapeJson(text)}\"}}";
        return SendNoResult(NewPost($"{serverUrl}/topic/add", body, ShortTimeout), onDone);
    }

    /// <summary>플레이어가 캐릭터에게 다가갔을 때 자연스러운 반응 가져오기</summary>
    public IEnumerator GetApproachGreeting(string characterName, Action<string> onSuccess, Action onFail = null)
    {
        string url = $"{serverUrl}/player/approach?name={UnityWebRequest.EscapeURL(characterName ?? "")}";
        return Send<LineResponse>(NewPost(url, null, LongTimeout), d => onSuccess?.Invoke(d.line), onFail);
    }

    /// <summary>두 캐릭터의 대화 한 묶음(turns턴)을 한 번에 받아온다.
    /// charA가 먼저 말하며, charA가 이후 플레이어 응답의 대상(active[0])이 된다.
    /// 응답에는 가끔 캐릭터가 플레이어에게 던지는 한마디(player_question)가 함께 온다.</summary>
    public IEnumerator GetConversationExchange(string charA, string charB, int turns,
                                               Action<ExchangeResult> onSuccess, Action onFail = null)
    {
        var body = $"{{\"char_a\":\"{EscapeJson(charA)}\",\"char_b\":\"{EscapeJson(charB)}\",\"turns\":{turns}}}";
        return Send<ExchangeResult>(NewPost($"{serverUrl}/conversation/exchange", body, LongTimeout), onSuccess, onFail);
    }

    /// <summary>현재 AI 대화 상태 polling (EventNotificationUI에서 사용)</summary>
    public IEnumerator GetConversationStatus(Action<ConversationStatusData> onSuccess, Action onFail = null)
    {
        return Send<ConversationStatusData>(NewGet($"{serverUrl}/conversation/status", ShortTimeout), onSuccess, onFail);
    }

    /// <summary>카드게임 결과를 서버에 보고하고 캐릭터 반응을 받아옴</summary>
    public IEnumerator ReportGameResult(string winner, int playerRank, int passCount,
                                        Action<GameResultResponse> onSuccess, Action onFail = null)
    {
        var body = $"{{\"winner\":\"{EscapeJson(winner)}\",\"player_rank\":{playerRank},\"pass_count\":{passCount}}}";
        return Send<GameResultResponse>(NewPost($"{serverUrl}/game/result", body, LongTimeout), onSuccess, onFail);
    }

    /// <summary>마이룸 종합 상태 가져오기 (관계, 이벤트 등)</summary>
    public IEnumerator GetMyRoomStatus(Action<string> onSuccess, Action onFail = null)
    {
        using var req = NewGet($"{serverUrl}/myroom/status", 15);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            onSuccess?.Invoke(req.downloadHandler.text);
        else
            onFail?.Invoke();
    }

    /// <summary>JSON 문자열 이스케이프. null 안전, 줄바꿈·탭·제어문자까지 처리(예전엔 \r·탭이 섞이면 서버가 422로 거절).</summary>
    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    [Serializable] class LineResponse         { public string speaker; public string line; }
    [Serializable] class NextLineResponse     { public string speaker; public string line; }
    [Serializable] class ConversationResponse { public string speaker; public string reply; }
    [Serializable] class SetCharactersRequest { public string[] characters; }
}

// ── AI끼리 대화 묶음 응답 (lines + 가끔 플레이어에게 던지는 질문) ──────
[Serializable]
public class ExchangeResult
{
    public ExchangeLine[] lines;
    public string         player_question;
}

// ── 그룹 대화 응답 (플레이어 한 마디에 여러 캐릭터가 차례로 답함) ──────
[Serializable]
public class GroupReplyResult
{
    public ExchangeLine[] replies;
}

// ── AI끼리 대화 묶음의 한 줄 (PlayerInteraction에서 사용) ──────────────
[Serializable]
public class ExchangeLine
{
    public string speaker;
    public string line;
}

// ── 카드게임 결과 응답 모델 ──────────────────────────────────────────
[Serializable]
public class GameResultResponse
{
    public string result;
    // JsonUtility는 Dictionary를 파싱 못 하므로, reactions는 raw JSON으로 별도 처리
}

[Serializable]
public class GameResultReaction
{
    public string character;
    public string line;
}
