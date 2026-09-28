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
        raw = (raw ?? "").Trim();
        if (raw.Length == 0)     return "";
        if (raw.Contains("://")) return raw.TrimEnd('/');
        if (raw.Contains(":"))   return "http://" + raw;
        return $"http://{raw}:8000";
    }

    public IEnumerator GetCharacterLine(string characterName, Action<string> onSuccess, Action onFail = null)
    {
        string url = string.IsNullOrEmpty(characterName)
            ? $"{serverUrl}/character/line"
            : $"{serverUrl}/character/line?name={UnityWebRequest.EscapeURL(characterName)}";

        using var req = UnityWebRequest.Get(url);
        req.timeout = 240;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var data = JsonUtility.FromJson<LineResponse>(req.downloadHandler.text);
            onSuccess?.Invoke(data.line);
        }
        else
        {
            onFail?.Invoke();
        }
    }

    public IEnumerator GetConversationNext(Action<string, string> onSuccess, Action onFail = null)
    {
        using var req = new UnityWebRequest($"{serverUrl}/conversation/next", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Array.Empty<byte>());
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 240;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var data = JsonUtility.FromJson<NextLineResponse>(req.downloadHandler.text);
            onSuccess?.Invoke(data.speaker, data.line);
        }
        else
        {
            onFail?.Invoke();
        }
    }

    public IEnumerator SetActiveCharacters(string[] names, Action onDone = null)
    {
        var body = JsonUtility.ToJson(new SetCharactersRequest { characters = names });
        using var req = new UnityWebRequest($"{serverUrl}/characters/set", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 10;
        yield return req.SendWebRequest();
        onDone?.Invoke();
    }

    public IEnumerator ClearConversation(Action onDone = null)
    {
        using var req = new UnityWebRequest($"{serverUrl}/conversation/clear", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Array.Empty<byte>());
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 10;
        yield return req.SendWebRequest();
        onDone?.Invoke();
    }

    public IEnumerator SendUserMessage(string message, string target, Action<string> onSuccess, Action onFail = null)
    {
        // target(지금 말 거는 캐릭터)을 명시해 서버가 엉뚱한 캐릭터로 답하지 않게 한다.
        var body = $"{{\"message\":\"{EscapeJson(message)}\",\"target\":\"{EscapeJson(target)}\"}}";
        using var req = new UnityWebRequest($"{serverUrl}/conversation/user", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 240;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var data = JsonUtility.FromJson<ConversationResponse>(req.downloadHandler.text);
            onSuccess?.Invoke(data.reply);
        }
        else
        {
            onFail?.Invoke();
        }
    }

    /// <summary>플레이어 한 마디에 여러 캐릭터가 차례로 반응한다(진짜 3자 대화).</summary>
    public IEnumerator SendGroupMessage(string message, string[] characters,
                                        Action<ExchangeLine[]> onSuccess, Action onFail = null)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < characters.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(EscapeJson(characters[i])).Append('"');
        }
        var body = $"{{\"message\":\"{EscapeJson(message)}\",\"characters\":[{sb}]}}";

        using var req = new UnityWebRequest($"{serverUrl}/conversation/group", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 240;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var data = JsonUtility.FromJson<GroupReplyResult>(req.downloadHandler.text);
            onSuccess?.Invoke(data != null ? data.replies : null);
        }
        else
        {
            onFail?.Invoke();
        }
    }

    /// <summary>플레이어가 제시한 대화 주제를 서버에 저장한다(이후 캐릭터들이 이 주제로 대화).</summary>
    public IEnumerator AddTopic(string text, Action onDone = null)
    {
        var body = $"{{\"text\":\"{EscapeJson(text)}\"}}";
        using var req = new UnityWebRequest($"{serverUrl}/topic/add", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 10;
        yield return req.SendWebRequest();
        onDone?.Invoke();
    }

    /// <summary>플레이어가 캐릭터에게 다가갔을 때 자연스러운 반응 가져오기</summary>
    public IEnumerator GetApproachGreeting(string characterName, Action<string> onSuccess, Action onFail = null)
    {
        string url = $"{serverUrl}/player/approach?name={UnityWebRequest.EscapeURL(characterName)}";
        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(Array.Empty<byte>());
        req.downloadHandler = new DownloadHandlerBuffer();
        req.timeout = 240;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var data = JsonUtility.FromJson<LineResponse>(req.downloadHandler.text);
            onSuccess?.Invoke(data.line);
        }
        else
        {
            onFail?.Invoke();
        }
    }

    /// <summary>두 캐릭터의 대화 한 묶음(turns턴)을 한 번에 받아온다.
    /// charA가 먼저 말하며, charA가 이후 플레이어 응답의 대상(active[0])이 된다.
    /// 응답에는 가끔 캐릭터가 플레이어에게 던지는 한마디(player_question)가 함께 온다.</summary>
    public IEnumerator GetConversationExchange(string charA, string charB, int turns,
                                               Action<ExchangeResult> onSuccess, Action onFail = null)
    {
        var body = $"{{\"char_a\":\"{EscapeJson(charA)}\",\"char_b\":\"{EscapeJson(charB)}\",\"turns\":{turns}}}";
        using var req = new UnityWebRequest($"{serverUrl}/conversation/exchange", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 240;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var data = JsonUtility.FromJson<ExchangeResult>(req.downloadHandler.text);
            onSuccess?.Invoke(data);
        }
        else
        {
            onFail?.Invoke();
        }
    }

    /// <summary>현재 AI 대화 상태 polling (EventNotificationUI에서 사용)</summary>
    public IEnumerator GetConversationStatus(Action<ConversationStatusData> onSuccess, Action onFail = null)
    {
        using var req = UnityWebRequest.Get($"{serverUrl}/conversation/status");
        req.timeout = 10;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var data = JsonUtility.FromJson<ConversationStatusData>(req.downloadHandler.text);
            onSuccess?.Invoke(data);
        }
        else
        {
            onFail?.Invoke();
        }
    }

    /// <summary>카드게임 결과를 서버에 보고하고 캐릭터 반응을 받아옴</summary>
    public IEnumerator ReportGameResult(string winner, int playerRank, int passCount,
                                        Action<GameResultResponse> onSuccess, Action onFail = null)
    {
        var body = $"{{\"winner\":\"{EscapeJson(winner)}\",\"player_rank\":{playerRank},\"pass_count\":{passCount}}}";
        using var req = new UnityWebRequest($"{serverUrl}/game/result", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 240;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var data = JsonUtility.FromJson<GameResultResponse>(req.downloadHandler.text);
            onSuccess?.Invoke(data);
        }
        else
        {
            onFail?.Invoke();
        }
    }

    /// <summary>마이룸 종합 상태 가져오기 (관계, 이벤트 등)</summary>
    public IEnumerator GetMyRoomStatus(Action<string> onSuccess, Action onFail = null)
    {
        using var req = UnityWebRequest.Get($"{serverUrl}/myroom/status");
        req.timeout = 15;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            onSuccess?.Invoke(req.downloadHandler.text);
        else
            onFail?.Invoke();
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

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
