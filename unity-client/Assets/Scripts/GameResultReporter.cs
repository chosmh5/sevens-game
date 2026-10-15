using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 카드게임 종료 시 결과를 서버에 보고하고,
/// 캐릭터들의 반응을 화면 중앙에 연출하는 스크립트.
///
/// CardGame 씬의 아무 오브젝트에 붙이면 동작.
/// CardGameManager가 같은 씬에 있어야 함.
/// </summary>
public class GameResultReporter : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float resultDisplayTime = 4f;

    // ── 내부 ─────────────────────────────────────────────────────
    private CardGameManager _mgr;
    private GameObject      _overlayRoot;
    private CanvasGroup     _overlayGroup;
    private TMP_Text        _resultLabel;
    private TMP_Text        _reactionLabel;

    void Start()
    {
        _mgr = FindAnyObjectByType<CardGameManager>();
        if (_mgr != null)
            _mgr.GameWon += OnGameWon;

        BuildOverlay();
    }

    void OnDestroy()
    {
        if (_mgr != null)
            _mgr.GameWon -= OnGameWon;
    }

    // ══════════════════════════════════════════════════════════════
    //  게임 종료 처리
    // ══════════════════════════════════════════════════════════════

    void OnGameWon(Player winner)
    {
        // 순위: 승자 1등, 나머지는 남은 손패가 적은 순
        int[] counts = _mgr.GetHandCounts();
        int playerRank = 1;
        if (winner.IsAI)
        {
            playerRank = 2;
            for (int i = 1; i < counts.Length; i++)
                if (i != winner.Index && counts[i] < counts[0]) playerRank++;
        }
        int passCount = _mgr.GetPassCount(0);
        List<string> highlights = HighlightDirector.Instance != null
            ? HighlightDirector.Instance.HighlightSummary()
            : new List<string>();

        StartCoroutine(ReportAndShow(winner.Name, playerRank, passCount, highlights));
    }

    IEnumerator ReportAndShow(string winnerName, int playerRank, int passCount, List<string> highlights)
    {
        // 결과 화면 즉시 표시
        string rankText = playerRank switch
        {
            1 => "승리!",
            2 => "2등",
            3 => "3등",
            _ => "패배..."
        };
        _resultLabel.text   = rankText;
        string recap = highlights != null && highlights.Count > 0
            ? $"<color=#9FE8FF>명장면: {string.Join(" · ", highlights)}</color>\n\n"
            : "";
        _reactionLabel.text = recap + "캐릭터 반응 확인 중...";
        ShowOverlay(true);

        // 서버에 결과 보고
        GameResultResponse response = null;
        string rawJson = null;

        yield return ReportWithRawJson(winnerName, playerRank, passCount, highlights,
            (resp, json) => { response = resp; rawJson = json; });

        if (response != null && rawJson != null)
        {
            // reactions 딕셔너리를 수동 파싱 (JsonUtility는 Dict 미지원)
            var reactions = ParseReactions(rawJson);
            if (reactions.Count > 0)
            {
                // 각 캐릭터 반응을 순서대로 표시
                yield return ShowReactions(reactions, recap);
            }
            else
            {
                _reactionLabel.text = response.result;
                yield return new WaitForSeconds(resultDisplayTime);
            }
        }
        else
        {
            _reactionLabel.text = "(서버 연결 실패)";
            yield return new WaitForSeconds(2f);
        }

        ShowOverlay(false);
    }

    /// <summary>서버 호출 + raw JSON도 함께 받아오는 래퍼</summary>
    IEnumerator ReportWithRawJson(string winner, int rank, int passCount, List<string> highlights,
                                   Action<GameResultResponse, string> callback)
    {
        var hl = new System.Text.StringBuilder("[");
        if (highlights != null)
            for (int i = 0; i < highlights.Count; i++)
                hl.Append(i > 0 ? "," : "").Append('"').Append(EscapeJson(highlights[i])).Append('"');
        hl.Append(']');
        var body = $"{{\"winner\":\"{EscapeJson(winner)}\",\"player_rank\":{rank},\"pass_count\":{passCount},\"highlights\":{hl}}}";
        using var req = new UnityEngine.Networking.UnityWebRequest(
            $"{GetServerUrl()}/game/result", "POST");
        req.uploadHandler   = new UnityEngine.Networking.UploadHandlerRaw(
            System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 30;
        yield return req.SendWebRequest();

        if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            string json = req.downloadHandler.text;
            var data = JsonUtility.FromJson<GameResultResponse>(json);
            callback?.Invoke(data, json);
        }
        else
        {
            callback?.Invoke(null, null);
        }
    }

    static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    string GetServerUrl()
    {
        // ServerClient가 들고 있는 현재 서버 주소를 그대로 사용
        // (인게임에서 입력한 IP가 즉시 반영됨)
        if (ServerClient.Instance != null)
            return ServerClient.Instance.BaseUrl;
        return "http://192.168.35.186:8000";
    }

    // ══════════════════════════════════════════════════════════════
    //  반응 표시 연출
    // ══════════════════════════════════════════════════════════════

    IEnumerator ShowReactions(List<GameResultReaction> reactions, string recap = "")
    {
        foreach (var r in reactions)
        {
            _reactionLabel.text = $"{recap}<color=#FFDD66>{r.character}</color>\n{r.line}";
            yield return new WaitForSeconds(resultDisplayTime);
        }
    }

    /// <summary>서버 응답 JSON에서 reactions 딕셔너리를 수동 파싱</summary>
    List<GameResultReaction> ParseReactions(string json)
    {
        var list = new List<GameResultReaction>();
        try
        {
            // "reactions": {"세비-호": "...", "류": "..."} 형태를 간단히 파싱
            int idx = json.IndexOf("\"reactions\"");
            if (idx < 0) return list;

            int braceStart = json.IndexOf('{', idx);
            if (braceStart < 0) return list;

            int braceEnd = json.IndexOf('}', braceStart);
            if (braceEnd < 0) return list;

            string inner = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
            // "세비-호": "대사", "류": "대사" 형태를 분리
            // 쉼표로 분리하되 따옴표 안의 쉼표는 무시해야 함
            var pairs = SplitJsonPairs(inner);
            foreach (var pair in pairs)
            {
                int colon = pair.IndexOf(':');
                if (colon < 0) continue;

                string key = pair.Substring(0, colon).Trim().Trim('"');
                string val = pair.Substring(colon + 1).Trim().Trim('"');
                // 이스케이프 복원
                val = val.Replace("\\\"", "\"").Replace("\\n", "\n");

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                    list.Add(new GameResultReaction { character = key, line = val });
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameResultReporter] reactions 파싱 실패: {e.Message}");
        }
        return list;
    }

    /// <summary>JSON 오브젝트 내부의 key:value 쌍을 분리 (따옴표 안 쉼표 무시)</summary>
    List<string> SplitJsonPairs(string s)
    {
        var result = new List<string>();
        bool inQuote = false;
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\'))
                inQuote = !inQuote;
            if (c == ',' && !inQuote)
            {
                result.Add(s.Substring(start, i - start));
                start = i + 1;
            }
        }
        if (start < s.Length)
            result.Add(s.Substring(start));
        return result;
    }

    // ══════════════════════════════════════════════════════════════
    //  오버레이 UI
    // ══════════════════════════════════════════════════════════════

    void ShowOverlay(bool show)
    {
        if (_overlayRoot == null) return;
        _overlayRoot.SetActive(true);
        StopCoroutine("FadeOverlay");
        StartCoroutine(FadeOverlay(show ? 1f : 0f, 0.4f));
    }

    IEnumerator FadeOverlay(float target, float duration)
    {
        float start = _overlayGroup.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            _overlayGroup.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }
        _overlayGroup.alpha = target;
        if (target == 0f) _overlayRoot.SetActive(false);
    }

    void BuildOverlay()
    {
        var canvasGO = new GameObject("GameResultCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        // 반투명 배경
        _overlayRoot = new GameObject("ResultOverlay");
        _overlayRoot.transform.SetParent(canvasGO.transform, false);
        var bgImg = _overlayRoot.AddComponent<Image>();
        bgImg.color = new Color(0.02f, 0.02f, 0.08f, 0.88f);
        var bgRT        = _overlayRoot.GetComponent<RectTransform>();
        bgRT.anchorMin  = new Vector2(0.2f, 0.25f);
        bgRT.anchorMax  = new Vector2(0.8f, 0.75f);
        bgRT.offsetMin  = Vector2.zero;
        bgRT.offsetMax  = Vector2.zero;

        _overlayGroup = _overlayRoot.AddComponent<CanvasGroup>();

        // 결과 텍스트 (상단)
        var resultGO = new GameObject("ResultLabel");
        resultGO.transform.SetParent(_overlayRoot.transform, false);
        _resultLabel = resultGO.AddComponent<TextMeshProUGUI>();
        _resultLabel.fontSize  = 42f;
        _resultLabel.fontStyle = FontStyles.Bold;
        _resultLabel.color     = new Color(1f, 0.85f, 0.35f);
        _resultLabel.alignment = TextAlignmentOptions.Center;
        var resRT        = resultGO.GetComponent<RectTransform>();
        resRT.anchorMin  = new Vector2(0f, 0.6f);
        resRT.anchorMax  = new Vector2(1f, 0.95f);
        resRT.offsetMin  = new Vector2(20f, 0f);
        resRT.offsetMax  = new Vector2(-20f, 0f);

        // 캐릭터 반응 (하단)
        var reactGO = new GameObject("ReactionLabel");
        reactGO.transform.SetParent(_overlayRoot.transform, false);
        _reactionLabel = reactGO.AddComponent<TextMeshProUGUI>();
        _reactionLabel.fontSize          = 18f;
        _reactionLabel.color             = Color.white;
        _reactionLabel.alignment         = TextAlignmentOptions.Center;
        _reactionLabel.enableWordWrapping = true;
        var reactRT        = reactGO.GetComponent<RectTransform>();
        reactRT.anchorMin  = new Vector2(0f, 0.05f);
        reactRT.anchorMax  = new Vector2(1f, 0.55f);
        reactRT.offsetMin  = new Vector2(30f, 0f);
        reactRT.offsetMax  = new Vector2(-30f, 0f);

        _overlayRoot.SetActive(false);
    }
}
