using System;
using System.Collections.Generic;
using System.Text;

/// <summary>하이라이트 종류.</summary>
public enum HighlightType
{
    LastCard,     // 마지막 한 장 남음
    Reach,        // 처음으로 3장 이하 (곧 끝남)
    Comeback,     // 역전 — 크게 뒤처졌던 사람이 선두(가장 적은 손패)로
    TripleCombo,  // 한 턴에 최대(3장) 제출
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
    /// <summary>LineComplete 의 문양.</summary>
    public Suit Suit;
    /// <summary>PassStreak 의 연속 패스 횟수.</summary>
    public int Count;

    public override string ToString() =>
        $"[턴 {Turn}] {Type} 주인공={Actor} 상대={Other} 강도={Intensity}" +
        (Type == HighlightType.PassStreak ? $" 연속={Count}" : "") +
        (Type == HighlightType.LineComplete ? $" {Suit.ToSymbol()}" : "");
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
///   - 3장 연속(막판 스퍼트): 한 턴에 최대 장수(3장)를 다 내서 4장 이하가 됨
///   - 막힘: 같은 사람이 2번 연속 패스(4번째에 한 번 더)
///   - 접전: 두 명 이상이 2장 이하 (한 판에 1번)
///   - 줄 완성: 한 문양이 A~K 까지 다 깔림
/// </summary>
public sealed class SevensHighlightDetector
{
    public const int ComebackDeficit = 3;    // 이만큼 뒤처졌다가 선두가 되면 '역전'
    public const int ComebackCooldown = 4;   // 역전 연출 사이 최소 턴 간격

    private readonly int _n;
    private readonly int _maxPlays;
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

    /// <summary>이번 판에서 나온 하이라이트 전부.</summary>
    public readonly List<HighlightEvent> History = new List<HighlightEvent>();

    public int Turn => _turn;
    public int Leader => _leader;

    public SevensHighlightDetector(int playerCount = 4, int maxPlaysPerTurn = 3)
    {
        _n = playerCount;
        _maxPlays = maxPlaysPerTurn;
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
        History.Clear();
    }

    /// <summary>
    /// 한 턴이 확정됐을 때 호출. played 가 비어 있으면 패스.
    /// handCounts 는 이 턴이 끝난 뒤 모든 좌석의 손패 수.
    /// </summary>
    public List<HighlightEvent> OnTurnEnded(int seat, IReadOnlyList<Card> played, int[] handCounts)
    {
        _turn++;
        var ev = new List<HighlightEvent>();
        int playedCount = played?.Count ?? 0;

        // 1) 패스 / 연속 패스(막힘)
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

        // 2) 3장 연속 — 이 규칙에선 3장을 다 내는 게 흔해서(판당 7회꼴, 초반엔 거의 매 턴),
        //    막판 스퍼트(3장을 내서 4장 이하가 됨)만 하이라이트로 본다.
        if (playedCount >= _maxPlays && handCounts[seat] <= 4)
            ev.Add(New(HighlightType.TripleCombo, seat, -1, 2));

        // 3) 줄 완성
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
                    var e = New(HighlightType.LineComplete, seat, -1, 1);
                    e.Suit = c.Suit;
                    ev.Add(e);
                }
            }
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
                ev.Add(New(HighlightType.Comeback, newLeader, _leader, 3));
                _lastComebackTurn = _turn;
            }
            _leader = newLeader;
            _maxDeficit[newLeader] = 0;
        }

        Array.Copy(handCounts, _prev, _n);
        History.AddRange(ev);
        return ev;
    }

    /// <summary>
    /// 이번 판 하이라이트를 짧은 문구 목록으로 (서버의 게임 결과 반응 프롬프트용).
    /// 예) ["플레이어 역전", "류 마지막 한 장", "진 3장 연속"]
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
            string text = who + Label(e.Type);
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
            case HighlightType.TripleCombo:  return "3장 연속";
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
