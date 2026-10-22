using System;
using System.Collections.Generic;
using System.Text;

/// <summary>하이라이트 종류.</summary>
public enum HighlightType
{
    LastCard,     // 마지막 한 장 남음
    Reach,        // 처음으로 3장 이하 (곧 끝남)
    Comeback,     // 역전 — 크게 뒤처졌던 사람이 선두(가장 적은 손패)로. 작은 알림(강도 1)만
    Blockade,     // 플레이어가 카드를 아껴서(쥐고 안 내서) 상대를 강제로 패스시킴 — 한 판에 1번
    PassStreak,   // 같은 사람이 연속 패스(막힘)
    CloseRace,    // 접전 — 두 명 이상이 2장 이하
    LineComplete, // 한 문양 줄 완성(A~K)
}

/// <summary>감지된 하이라이트 한 건.</summary>
[Serializable]
public sealed class HighlightEvent
{
    public HighlightType Type;
    /// <summary>주인공 좌석(0 = 플레이어, 1~3 = AI). 접전처럼 특정 주인공이 없으면 -1.</summary>
    public int Actor = -1;
    /// <summary>관련된 다른 좌석(역전당한 사람 등). 없으면 -1.</summary>
    public int Other = -1;
    /// <summary>1 = 작은 알림, 2 = 캐릭터 반응, 3 = 큰 순간(연출 강하게 + AI 턴 잠깐 멈춤).</summary>
    public int Intensity;
    /// <summary>몇 번째 턴에서 나왔는지(1부터).</summary>
    public int Turn;
    /// <summary>LineComplete 의 문양 / Blockade 때 막은 카드의 문양.</summary>
    public Suit Suit;
    /// <summary>Blockade 때 막은 카드(플레이어가 쥐고 있던 카드). 그 외엔 null.</summary>
    public Card Blocker;
    /// <summary>PassStreak 의 연속 패스 횟수.</summary>
    public int Count;

    public override string ToString() =>
        $"[턴 {Turn}] {Type} 주인공={Actor} 상대={Other} 강도={Intensity}" +
        (Type == HighlightType.PassStreak ? $" 연속={Count}" : "") +
        (Type == HighlightType.LineComplete ? $" {Suit.ToSymbol()}" : "") +
        (Type == HighlightType.Blockade ? $" 막은 카드={Blocker}" : "");
}

/// <summary>
/// 세븐 카드게임의 '하이라이트'를 자동으로 찾아낸다. (Unity 의존 없음 → 시뮬레이션으로 검증 가능)
///
/// GameManager 가 매 턴이 끝날 때 OnTurnEnded(좌석, 이번 턴에 낸 카드들, 모두의 손패 수)를 넘겨주면
/// 그 턴에 생긴 하이라이트 목록을 돌려준다. 사람이 '되돌리기'를 쓸 수 있으므로
/// 카드 한 장 단위가 아니라 턴이 확정된 뒤에 판단한다.
///
/// 판단 기준
///   - 마지막 한 장: 손패가 2장 이상 → 1장
///   - 곧 끝남: 처음으로 3장 이하가 됨(좌석마다 1번)
///   - 역전: 선두(가장 적은 손패, 단독)가 바뀌었고, 새 선두가 그동안 선두보다 3장 이상 뒤처진 적이 있음
///   - 봉쇄: 플레이어가 낼 수 있는 카드를 쥔 채 턴을 마쳤고, 그 카드 바로 다음 장을 가진 상대가
///           낼 카드가 없어 패스함(= 플레이어가 그 카드를 냈다면 상대는 낼 수 있었다). 한 판에 1번.
///           손패를 넘겨줄 때만(OnTurnEnded 의 hand) 판단한다.
///   - 막힘: 같은 사람이 2번 연속 패스(4번째에 한 번 더)
///   - 접전: 두 명 이상이 2장 이하 (한 판에 1번)
///   - 줄 완성: 한 문양이 A~K 까지 다 깔림
/// </summary>
public sealed class SevensHighlightDetector
{
    public const int ComebackDeficit = 3;    // 이만큼 뒤처졌다가 선두가 되면 '역전'
    public const int ComebackCooldown = 4;   // 역전 연출 사이 최소 턴 간격

    private readonly int _n;
    private int[] _prev;
    private int[] _passStreak;
    private int[] _maxDeficit;
    private bool[] _reached;
    private readonly int[] _lineMin = new int[4];
    private readonly int[] _lineMax = new int[4];
    private readonly bool[] _lineDone = new bool[4];
    private int _leader = -1;
    private int _turn;
    private int _lastComebackTurn = -99;
    private bool _closeRaceShown;
    private bool _blockadeShown;
    // 플레이어(0번)가 지난 턴을 마칠 때 낼 수 있었는데 쥐고 있던 카드들
    private readonly List<Card> _held = new List<Card>();

    /// <summary>이번 판에서 나온 하이라이트 전부.</summary>
    public readonly List<HighlightEvent> History = new List<HighlightEvent>();

    /// <summary>
    /// 턴마다 모든 좌석의 손패 수(결과 화면 진행 그래프용). [0] = 시작, [k] = k번째 턴이 끝난 뒤.
    /// 승리로 끝난 마지막 턴은 OnTurnEnded 가 안 불리므로 RecordFinal 로 넣는다.
    /// </summary>
    public readonly List<int[]> CountHistory = new List<int[]>();
    private bool _finalRecorded;

    public int Turn => _turn;
    public int Leader => _leader;

    public SevensHighlightDetector(int playerCount = 4, int maxPlaysPerTurn = 3)
    {
        _n = playerCount;
        Reset(new int[playerCount]);
    }

    /// <summary>새 판 시작. 처음 손패 수(보통 모두 12)를 넘긴다.</summary>
    public void Reset(int[] handCounts)
    {
        _prev = (int[])handCounts.Clone();
        _passStreak = new int[_n];
        _maxDeficit = new int[_n];
        _reached = new bool[_n];
        for (int s = 0; s < 4; s++)
        {
            _lineMin[s] = Card.SevenNumber;
            _lineMax[s] = Card.SevenNumber;
            _lineDone[s] = false;
        }
        _leader = UniqueMin(handCounts);
        _turn = 0;
        _lastComebackTurn = -99;
        _closeRaceShown = false;
        _blockadeShown = false;
        _held.Clear();
        History.Clear();
        CountHistory.Clear();
        CountHistory.Add((int[])handCounts.Clone());
        _finalRecorded = false;
    }

    /// <summary>
    /// 한 턴이 확정됐을 때 호출. played 가 비어 있으면 패스.
    /// handCounts 는 이 턴이 끝난 뒤 모든 좌석의 손패 수.
    /// hand 는 이 턴을 마친 사람의 손패(선택) — 넘겨주면 '봉쇄'를 판단한다.
    /// </summary>
    public List<HighlightEvent> OnTurnEnded(int seat, IReadOnlyList<Card> played, int[] handCounts,
                                            IReadOnlyList<Card> hand = null)
    {
        _turn++;
        var ev = new List<HighlightEvent>();
        int playedCount = played?.Count ?? 0;

        // 0) 필드 갱신(줄 끝) — 봉쇄 판단에 쓰므로 먼저
        var completed = new List<Card>();
        if (played != null)
        {
            foreach (var c in played)
            {
                int s = (int)c.Suit;
                if (c.Number < _lineMin[s]) _lineMin[s] = c.Number;
                if (c.Number > _lineMax[s]) _lineMax[s] = c.Number;
                if (!_lineDone[s] && _lineMin[s] == Card.MinNumber && _lineMax[s] == Card.MaxNumber)
                {
                    _lineDone[s] = true;
                    completed.Add(c);
                }
            }
        }

        // 1) 봉쇄: 플레이어가 쥐고 있던 카드 때문에 상대가 패스
        if (hand != null)
        {
            if (seat == 0)
            {
                _held.Clear();
                foreach (var c in hand) if (CanPlace(c)) _held.Add(c);
            }
            else if (playedCount == 0 && !_blockadeShown)
            {
                Card blocker = FindBlocker(hand);
                if (blocker != null)
                {
                    _blockadeShown = true;
                    var e = New(HighlightType.Blockade, 0, seat, 2);
                    e.Blocker = blocker;
                    e.Suit = blocker.Suit;
                    ev.Add(e);
                }
            }
        }

        // 2) 패스 / 연속 패스(막힘)
        if (playedCount == 0)
        {
            _passStreak[seat]++;
            if (_passStreak[seat] == 2 || _passStreak[seat] == 4)
                ev.Add(New(HighlightType.PassStreak, seat, -1, _passStreak[seat] >= 4 ? 2 : 1, c: _passStreak[seat]));
        }
        else
        {
            _passStreak[seat] = 0;
        }

        // 3) 줄 완성
        foreach (var c in completed)
        {
            var e = New(HighlightType.LineComplete, seat, -1, 1);
            e.Suit = c.Suit;
            ev.Add(e);
        }

        // 4) 손패 이정표: 마지막 한 장 / 곧 끝남
        int before = _prev[seat], now = handCounts[seat];
        if (now == 1 && before > 1)
            ev.Add(New(HighlightType.LastCard, seat, -1, 3));
        else if (now > 1 && now <= 3 && !_reached[seat])
            ev.Add(New(HighlightType.Reach, seat, -1, 1));
        if (now <= 3) _reached[seat] = true;

        // 5) 접전: 두 명 이상이 2장 이하 (한 판에 1번)
        if (!_closeRaceShown)
        {
            int close = 0;
            for (int i = 0; i < _n; i++) if (handCounts[i] > 0 && handCounts[i] <= 2) close++;
            if (close >= 2)
            {
                _closeRaceShown = true;
                ev.Add(New(HighlightType.CloseRace, -1, -1, 2));
            }
        }

        // 6) 역전: 선두 교체 + 새 선두가 크게 뒤처졌던 적이 있음
        int min = int.MaxValue;
        for (int i = 0; i < _n; i++) if (handCounts[i] < min) min = handCounts[i];
        for (int i = 0; i < _n; i++)
        {
            int deficit = handCounts[i] - min;
            if (deficit > _maxDeficit[i]) _maxDeficit[i] = deficit;
        }
        int newLeader = UniqueMin(handCounts);
        if (newLeader != -1 && newLeader != _leader)
        {
            if (_leader != -1 && _maxDeficit[newLeader] >= ComebackDeficit
                && _turn - _lastComebackTurn >= ComebackCooldown)
            {
                ev.Add(New(HighlightType.Comeback, newLeader, _leader, 1));   // 눈에 안 띄게 작은 알림만
                _lastComebackTurn = _turn;
            }
            _leader = newLeader;
            _maxDeficit[newLeader] = 0;
        }

        Array.Copy(handCounts, _prev, _n);
        History.AddRange(ev);
        CountHistory.Add((int[])handCounts.Clone());
        return ev;
    }

    /// <summary>게임이 끝난 마지막 턴(승자가 0장)의 손패 수를 그래프 기록에 넣는다. 한 번만.</summary>
    public void RecordFinal(int[] handCounts)
    {
        if (_finalRecorded || handCounts == null) return;
        _finalRecorded = true;
        CountHistory.Add((int[])handCounts.Clone());
    }

    /// <summary>
    /// 이번 판 하이라이트를 짧은 문구 목록으로 (서버의 게임 결과 반응 프롬프트용).
    /// 예) ["플레이어 역전", "류 마지막 한 장", "플레이어가 진 봉쇄"]
    /// </summary>
    public List<string> Summary(Func<int, string> nameOf, int max = 4)
    {
        var picked = new List<HighlightEvent>(History);
        picked.Sort((a, b) => b.Intensity != a.Intensity ? b.Intensity.CompareTo(a.Intensity) : a.Turn.CompareTo(b.Turn));
        var seen = new HashSet<string>();
        var result = new List<string>();
        foreach (var e in picked)
        {
            if (e.Type == HighlightType.Reach || e.Type == HighlightType.LineComplete) continue;
            string who = e.Actor >= 0 ? nameOf(e.Actor) + " " : "";
            string text = e.Type == HighlightType.Blockade && e.Other >= 0
                ? $"{nameOf(e.Actor)}가 {nameOf(e.Other)} 봉쇄"
                : who + Label(e.Type);
            if (seen.Add(text)) result.Add(text);
            if (result.Count >= max) break;
        }
        return result;
    }

    public static string Label(HighlightType t)
    {
        switch (t)
        {
            case HighlightType.LastCard:     return "마지막 한 장";
            case HighlightType.Reach:        return "곧 끝남";
            case HighlightType.Comeback:     return "역전";
            case HighlightType.Blockade:     return "봉쇄";
            case HighlightType.PassStreak:   return "연속 패스";
            case HighlightType.CloseRace:    return "접전";
            case HighlightType.LineComplete: return "줄 완성";
            default:                         return t.ToString();
        }
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        foreach (var e in History) sb.AppendLine(e.ToString());
        return sb.ToString();
    }

    /// <summary>감지기가 아는 필드(줄 끝)로 본 '지금 낼 수 있는 카드인가'.</summary>
    private bool CanPlace(Card c)
    {
        if (c == null || !c.IsValid || c.IsSeven) return false;
        int s = (int)c.Suit;
        return (c.Number == _lineMin[s] - 1 && _lineMin[s] > Card.MinNumber)
            || (c.Number == _lineMax[s] + 1 && _lineMax[s] < Card.MaxNumber);
    }

    /// <summary>
    /// 패스한 상대의 손패를 보고, 플레이어가 쥐고 있는 카드 중 '이걸 냈으면 상대가 다음 장을 낼 수 있었던' 카드를 찾는다.
    /// </summary>
    private Card FindBlocker(IReadOnlyList<Card> victimHand)
    {
        foreach (var held in _held)
        {
            if (!CanPlace(held)) continue;   // 그새 상황이 바뀜(방어)
            int next = held.Number < Card.SevenNumber ? held.Number - 1 : held.Number + 1;
            if (next < Card.MinNumber || next > Card.MaxNumber) continue;
            foreach (var c in victimHand)
                if (c.Suit == held.Suit && c.Number == next) return held;
        }
        return null;
    }

    private HighlightEvent New(HighlightType t, int actor, int other, int intensity, int c = 0) =>
        new HighlightEvent { Type = t, Actor = actor, Other = other, Intensity = intensity, Turn = _turn, Count = c };

    private int UniqueMin(int[] counts)
    {
        int min = int.MaxValue, idx = -1, ties = 0;
        for (int i = 0; i < _n; i++)
        {
            if (counts[i] < min) { min = counts[i]; idx = i; ties = 1; }
            else if (counts[i] == min) ties++;
        }
        return ties == 1 ? idx : -1;
    }
}
