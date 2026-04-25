using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class ServerClient : MonoBehaviour
{
    public static ServerClient Instance { get; private set; }

    [SerializeField] private string serverUrl = "http://192.168.35.35:8000";

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public IEnumerator GetCharacterLine(string characterName, Action<string> onSuccess, Action onFail = null)
    {
        string url = string.IsNullOrEmpty(characterName)
            ? $"{serverUrl}/character/line"
            : $"{serverUrl}/character/line?name={UnityWebRequest.EscapeURL(characterName)}";

        using var req = UnityWebRequest.Get(url);
        req.timeout = 30;
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
        req.timeout = 60;
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

    public IEnumerator SendUserMessage(string message, Action<string> onSuccess, Action onFail = null)
    {
        var body = $"{{\"message\":\"{EscapeJson(message)}\"}}";
        using var req = new UnityWebRequest($"{serverUrl}/conversation/user", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 60;
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

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    [Serializable] class LineResponse         { public string speaker; public string line; }
    [Serializable] class NextLineResponse     { public string speaker; public string line; }
    [Serializable] class ConversationResponse { public string speaker; public string reply; }
    [Serializable] class SetCharactersRequest { public string[] characters; }
}
