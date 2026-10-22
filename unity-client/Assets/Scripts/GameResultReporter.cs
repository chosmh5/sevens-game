using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 카드게임 종료 화면(순위·명장면·판 흐름 그래프·다시 하기/로비).
///
/// 결과는 HomeSession.RecordGame 으로 서버에 보고(관계 반영)만 하고, 캐릭터 반응은 여기서 띄우지 않는다.
/// 홈(마이룸)으로 돌아가면 세비-호가 방금 판에 대해 한마디 한다(HomeController → /game/return).
///
/// CardGame 씬의 아무 오브젝트에 붙이면 동작. CardGameManager가 같은 씬에 있어야 함.
/// </summary>
public class GameResultReporter : MonoBehaviour
{
    // ── 내부 ─────────────────────────────────────────────────────
    private CardGameManager _mgr;
    private GameObject      _overlayRoot;
    private CanvasGroup     _overlayGroup;
    private TMP_Text        _resultLabel;
    private TMP_Text        _recapLabel;
    private RectTransform   _rankRoot;
    private RectTransform   _chartRoot;

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
        var order = RankOrder(winner);
        int playerRank = order.IndexOf(0) + 1;
        int charRank = 0;
        for (int r = 0; r < order.Count; r++)
            if (_mgr.GetPlayerName(order[r]) == HomeSession.MainCharacter) { charRank = r + 1; break; }

        List<string> highlights = HighlightDirector.Instance != null
            ? HighlightDirector.Instance.HighlightSummary()
            : new List<string>();

        HomeSession.RecordGame(new HomeSession.GameRecord
        {
            winner      = winner.Name,
            player_rank = playerRank,
            char_rank   = charRank,
            pass_count  = _mgr.GetPassCount(0),
            highlights  = highlights.ToArray(),
        });

        _resultLabel.text = playerRank switch
        {
            1 => "승리!",
            2 => "2등",
            3 => "3등",
            _ => "패배..."
        };
        _recapLabel.text = highlights.Count > 0 ? $"명장면  {string.Join(" · ", highlights)}" : "";
        BuildRanks(order, winner);

        // 판 흐름 그래프(턴별 남은 손패)
        var det = HighlightDirector.Instance != null ? HighlightDirector.Instance.Detector : null;
        if (det != null)
        {
            det.RecordFinal(_mgr.GetHandCounts());
            GameProgressChart.Build(_chartRoot, det.CountHistory, det.History, winner.Index, _mgr.GetPlayerName);
        }
        ShowOverlay(true);
    }

    /// <summary>순위대로 좌석 번호. 승자 1등, 나머지는 남은 손패가 적은 순(같으면 좌석 순).</summary>
    List<int> RankOrder(Player winner)
    {
        int[] counts = _mgr.GetHandCounts();
        var seats = new List<int>();
        for (int i = 0; i < counts.Length; i++) seats.Add(i);
        seats.Sort((a, b) =>
        {
            if (a == b) return 0;   // 정렬이 자기 자신과 비교할 때 -1 을 주면 "일관성 없는 비교" 예외가 날 수 있다
            if (a == winner.Index) return -1;
            if (b == winner.Index) return 1;
            int c = counts[a].CompareTo(counts[b]);
            return c != 0 ? c : a.CompareTo(b);
        });
        return seats;
    }

    // ══════════════════════════════════════════════════════════════
    //  오버레이 UI
    // ══════════════════════════════════════════════════════════════

    const float RowW = 620f;   // 왼쪽 열(순위·명장면) 폭

    void ShowOverlay(bool show)
    {
        if (_overlayRoot == null) return;
        _overlayRoot.SetActive(true);
        // 문자열 StopCoroutine 은 IEnumerator 로 시작한 코루틴을 멈추지 못한다 → 핸들로 멈춘다.
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = StartCoroutine(FadeOverlay(show ? 1f : 0f, 0.4f));
    }

    Coroutine _fadeCo;

    IEnumerator FadeOverlay(float target, float duration)
    {
        float start = _overlayGroup.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _overlayGroup.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }
        _overlayGroup.alpha = target;
        if (target == 0f) _overlayRoot.SetActive(false);
        _fadeCo = null;
    }

    void BuildOverlay()
    {
        var canvasGO = new GameObject("GameResultCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        RetroCardGameSkin.SetupScaler(canvasGO.AddComponent<CanvasScaler>());
        canvasGO.AddComponent<GraphicRaycaster>();

        // 화면 전체 어둡게 + 가운데 레트로 패널
        _overlayRoot = new GameObject("ResultOverlay", typeof(RectTransform));
        _overlayRoot.transform.SetParent(canvasGO.transform, false);
        RetroTheme.Stretch((RectTransform)_overlayRoot.transform);
        var dim = _overlayRoot.AddComponent<Image>();
        dim.color = new Color(0.05f, 0.04f, 0.09f, 0.82f);
        _overlayGroup = _overlayRoot.AddComponent<CanvasGroup>();

        var panel = RetroTheme.Place(RetroTheme.Node("Panel", _overlayRoot.transform), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     Vector2.zero, new Vector2(1440f, 600f));
        RetroTheme.Panel(panel, RetroTheme.PanelColor, 14, 4, 0, 8f);

        // 제목(승리!/2등…)
        _resultLabel = RetroTheme.Label(panel, "ResultLabel", "", 55f, RetroTheme.Gold, true, TextAlignmentOptions.Left);
        RetroTheme.Place((RectTransform)_resultLabel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -22f), new Vector2(500f, 64f));
        RetroTheme.AddTextShadow(_resultLabel, -1.6f);
        var sub = RetroTheme.Label(panel, "Sub", "GAME OVER", 22f, RetroTheme.Muted, true, TextAlignmentOptions.Right);
        RetroTheme.Place((RectTransform)sub.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -38f), new Vector2(300f, 30f));

        // 왼쪽: 순위 4줄 + 명장면 / 오른쪽: 판 흐름 그래프
        _rankRoot = RetroTheme.Place(RetroTheme.Node("Ranks", panel), new Vector2(0f, 1f), new Vector2(0f, 1f),
                                     new Vector2(32f, -100f), new Vector2(RowW, 4 * 66f + 3 * 8f));
        _chartRoot = RetroTheme.Place(RetroTheme.Node("Chart", panel), new Vector2(0f, 1f), new Vector2(0f, 1f),
                                      new Vector2(32f + RowW + 28f, -100f), new Vector2(1440f - 64f - RowW - 28f, 372f));

        // 명장면
        _recapLabel = RetroTheme.Label(panel, "Recap", "", 22f, RetroTheme.Accent, false, TextAlignmentOptions.Left);
        _recapLabel.textWrappingMode = TextWrappingModes.Normal;
        _recapLabel.alignment = TextAlignmentOptions.TopLeft;
        RetroTheme.Place((RectTransform)_recapLabel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -404f), new Vector2(RowW, 170f));

        // 버튼
        var lobby = RetroTheme.MakeButton(panel, "LobbyButton", RetroTheme.BtnBlue, "로비", 33f);
        RetroTheme.Place((RectTransform)lobby.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-300f, 30f), new Vector2(170f, 72f));
        lobby.onClick.AddListener(() =>
        {
            if (GameSceneManager.Instance != null) GameSceneManager.Instance.GoToLobby();
            else UnityEngine.SceneManagement.SceneManager.LoadScene(GameSceneManager.Scenes.Lobby);
        });
        var again = RetroTheme.MakeButton(panel, "AgainButton", RetroTheme.BtnOrange, "다시 하기", 44f);
        RetroTheme.Place((RectTransform)again.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 30f), new Vector2(256f, 72f));
        again.onClick.AddListener(() =>
        {
            StopAllCoroutines();
            _fadeCo = null;
            _overlayGroup.alpha = 0f;
            _overlayRoot.SetActive(false);
            if (_mgr != null) _mgr.StartNewGame();
        });

        _overlayRoot.SetActive(false);
    }

    /// <summary>승자 1등, 나머지는 남은 장수가 적은 순. 패스 수·남은 장수 표시.</summary>
    void BuildRanks(List<int> seats, Player winner)
    {
        if (_rankRoot == null || _mgr == null) return;
        for (int i = _rankRoot.childCount - 1; i >= 0; i--) Destroy(_rankRoot.GetChild(i).gameObject);

        int[] counts = _mgr.GetHandCounts();

        for (int r = 0; r < seats.Count; r++)
        {
            int seat = seats[r];
            bool win = seat == winner.Index;
            string name = _mgr.GetPlayerName(seat);
            var row = RetroTheme.Place(RetroTheme.Node($"Rank{r + 1}", _rankRoot), new Vector2(0f, 1f), new Vector2(0f, 1f),
                                       new Vector2(0f, -r * 74f), new Vector2(RowW, 66f));
            RetroTheme.Panel(row, win ? RetroTheme.Hex("3A3320") : RetroTheme.Inset, 10, 3, 0, 5f);
            if (win)
            {
                var gr = RetroTheme.Stretch(RetroTheme.Node("WinRing", row), -6f);
                gr.SetAsFirstSibling();
                var gi = gr.gameObject.AddComponent<Image>();
                gi.sprite = RetroTheme.RingSprite(14, 4);
                gi.type = Image.Type.Sliced;
                gi.color = RetroTheme.Gold;
                gi.raycastTarget = false;
            }

            var num = RetroTheme.Label(row, "Rank", (r + 1).ToString(), 44f, win ? RetroTheme.Gold : RetroTheme.Muted, true);
            RetroTheme.Place((RectTransform)num.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(44f, 50f));

            var av = RetroTheme.Place(RetroTheme.Node("Avatar", row), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(68f, 0f), new Vector2(50f, 50f));
            RetroTheme.Panel(av, RetroTheme.SeatColor(name), 6, 3, 0, 0f);
            var ini = RetroTheme.Label(av, "Initial", RetroTheme.Initial(name), 33f, RetroTheme.Ink, true);
            RetroTheme.Stretch((RectTransform)ini.transform);

            var nm = RetroTheme.Label(row, "Name", name, 22f, RetroTheme.Text, true, TextAlignmentOptions.Left);
            RetroTheme.Place((RectTransform)nm.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(134f, 0f), new Vector2(150f, 30f));
            var ps = RetroTheme.Label(row, "Pass", $"패스 {_mgr.GetPassCount(seat)}", 22f, RetroTheme.Muted, false, TextAlignmentOptions.Left);
            RetroTheme.Place((RectTransform)ps.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(290f, 0f), new Vector2(120f, 30f));

            if (win)
            {
                var chip = RetroTheme.Chip(row, "Win", "WIN!", RetroTheme.Gold, RetroTheme.Ink, 22f);
                RetroTheme.Place((RectTransform)chip.transform.parent, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-140f, 0f), Vector2.zero);
            }

            var cnt = RetroTheme.Place(RetroTheme.Node("Count", row), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(110f, 48f));
            RetroTheme.InsetBox(cnt, RetroTheme.Hex("0F0D1F"));
            var ct = RetroTheme.Label(cnt, "Value", $"{counts[seat]}<size=22><color=#{ColorUtility.ToHtmlStringRGB(RetroTheme.Muted)}>장</color></size>", 33f, RetroTheme.Text, true);
            ct.richText = true;
            RetroTheme.Stretch((RectTransform)ct.transform);
        }
    }
}
