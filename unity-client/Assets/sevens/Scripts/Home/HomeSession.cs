using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 홈(마이룸) ↔ 카드게임 사이에 넘기는 값과 서버 요청.
///
/// - 홈에서 '게임'을 누르면 PrefetchGameStartLine() 으로 세비-호의 게임 시작 첫마디를 미리 요청한다.
///   서버는 마이룸에서 나눈 대화(기억)를 짚어 만든다. 카드게임의 HighlightDirector 가 시작할 때 꺼내 쓴다.
/// - 카드게임이 끝나면 RecordGame() 으로 결과를 서버에 보고(관계 반영)하고 판 내용을 기억해 둔다.
///   홈으로 돌아오면 HomeController 가 이걸 꺼내 세비-호가 그 판에 대해 한마디 하게 한다(/game/return).
/// - 요청은 씬이 바뀌어도 살아 있는 ServerClient 오브젝트에서 돌린다.
/// </summary>
public static class HomeSession
{
    public const string MainCharacter = "세비-호";
    private const string PrefLastTalk = "sevens.home.lastTalkTicks";

    /// <summary>서버 요청이 한 번이라도 성공했는지(서버 IP 입력 완료 여부 판단용).</summary>
    public static bool ServerOk { get; set; }

    /// <summary>마지막으로 확인한 세비-호 친밀도(-100~100). 씬이 바뀌어도 남아서, 게임 다녀온 뒤 변화량(+5 등)을 보여 준다.</summary>
    public static int? LastIntimacy { get; set; }

    /// <summary>방금 카드게임을 하고 돌아왔는지. GameSceneManager 가 CardGame → Home 전환 때 켠다.</summary>
    public static bool ReturnedFromGame { get; set; }

    // ── 홈에서 대화했는지(서버가 없을 때 기본 대사 고르기용) ──────────────

    public static void MarkTalked()
    {
        PlayerPrefs.SetString(PrefLastTalk, DateTime.UtcNow.Ticks.ToString());
        PlayerPrefs.Save();
    }

    public static bool TalkedRecently(float minutes = 60f)
    {
        if (!long.TryParse(PlayerPrefs.GetString(PrefLastTalk, ""), out var ticks)) return false;
        return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMinutes < minutes;
    }

    // ── 방금 끝난 판 ───────────────────────────────────────────────

    /// <summary>한 판의 결과. 서버 /game/result · /game/return 요청 본문으로도 쓴다(JsonUtility).</summary>
    [Serializable]
    public class GameRecord
    {
        public string winner;
        public int player_rank;
        public int char_rank;          // 세비-호의 순위
        public int pass_count;         // 플레이어의 패스 수
        public string[] highlights;    // 명장면 요약(하이라이트 감지기)
        public bool with_reactions;    // /game/result 가 캐릭터 반응을 만들지(홈에서 따로 받으므로 false)
        public string name;            // /game/return: 반응할 캐릭터
    }

    /// <summary>홈으로 돌아왔을 때 반응할 마지막 판(없으면 null). '다시 하기'로 여러 판 하면 마지막 판.</summary>
    public static GameRecord LastGame { get; private set; }

    /// <summary>결과 보고 중이면 true. 홈은 보고(관계 반영)가 끝난 뒤 반응을 요청한다.</summary>
    public static bool ResultPending { get; private set; }

    public static void RecordGame(GameRecord g)
    {
        if (g == null) return;
        g.with_reactions = false;
        g.name = MainCharacter;
        LastGame = g;
        var sc = ServerClient.Instance;
        if (sc == null) return;
        ResultPending = true;
        sc.StartCoroutine(PostResult(sc.BaseUrl, g));
    }

    /// <summary>마지막 판을 한 번만 꺼낸다(없으면 null).</summary>
    public static GameRecord ConsumeLastGame()
    {
        var g = LastGame;
        LastGame = null;
        return g;
    }

    private static IEnumerator PostResult(string baseUrl, GameRecord g)
    {
        using (var req = new UnityWebRequest($"{baseUrl}/game/result", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(g)));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 20;
            ServerClient.ApplyUserId(req);   // 사용자별 대화·기억 분리
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) ServerOk = true;
            else Debug.Log($"[HomeSession] 게임 결과 보고 실패 ({req.error})");
        }
        ResultPending = false;
    }

    // ── 게임 시작 첫마디 ────────────────────────────────────────────

    private static string _gameStartLine;

    /// <summary>요청 중이면 true. 카드게임 쪽은 잠깐 기다렸다가 꺼낸다.</summary>
    public static bool GameStartPending { get; private set; }

    public static void PrefetchGameStartLine()
    {
        _gameStartLine = null;
        var sc = ServerClient.Instance;
        if (sc == null || GameStartPending) return;
        GameStartPending = true;
        sc.StartCoroutine(FetchPregame(sc.BaseUrl));
    }

    /// <summary>받아 둔 첫마디를 한 번만 꺼낸다(없으면 null).</summary>
    public static string ConsumeGameStartLine()
    {
        var l = _gameStartLine;
        _gameStartLine = null;
        return string.IsNullOrWhiteSpace(l) || l.StartsWith("(") ? null : l;
    }

    [Serializable] private class LineResponse { public string speaker; public string line; public bool used_memory; }

    private static IEnumerator FetchPregame(string baseUrl)
    {
        string url = $"{baseUrl}/game/pregame?name={UnityWebRequest.EscapeURL(MainCharacter)}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 90;
            ServerClient.ApplyUserId(req);   // 사용자별 대화·기억 분리
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var data = JsonUtility.FromJson<LineResponse>(req.downloadHandler.text);
                    _gameStartLine = data?.line;
                    ServerOk = true;
                    Debug.Log($"[HomeSession] 게임 시작 첫마디(기억 반영={data?.used_memory}): {_gameStartLine}");
                }
                catch (Exception e) { Debug.LogWarning($"[HomeSession] 첫마디 해석 실패: {e.Message}"); }
            }
            else
            {
                Debug.Log($"[HomeSession] 첫마디 요청 실패 — 기본 대사 사용 ({req.error})");
            }
        }
        GameStartPending = false;
    }
}
