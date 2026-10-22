using System.Collections.Generic;

/// <summary>
/// 봉쇄 전략 AI (Hard). 낼 수 있어도 '상대에게만 길을 열어 주는' 카드는 쥐고 버틴다.
///
/// ── 설계 근거(docs/block_sim.py, 15,000판) ─────────────────────────
/// 6월 시뮬레이션(ai_sim.py)은 '무작위로 멈추기'만 시험해 "다 내는 게 최선"이라고 결론 냈지만,
/// 상황을 보고 골라서 쥐면 결과가 완전히 달라진다.
///   - 봉쇄 1명 vs 다 내기 3명 → 봉쇄 승률 약 81% (공정 몫 25%)
///   - 고르기만(보류 X) 28%, 보류만(고르기 X) 34% → 둘을 합쳐야 강하다.
///   - 사람이 다 내기로 봉쇄 AI 3명을 상대하면 사람 승률 약 9%, 사람도 봉쇄하면 약 22%.
///
/// ── 쓰는 정보 ────────────────────────────────────────────────────
/// 자기 손패와 필드뿐(남의 손패는 안 본다). 7을 뺀 48장이 모두 분배되므로
/// '내 손에 없고 필드에도 없는 카드' = 반드시 상대 중 누군가의 손에 있다.
///
/// ── 규칙 ────────────────────────────────────────────────────────
/// 안전한 카드 = 내도 상대에게 새 카드를 열어 주지 않는 카드(그 방향 다음 장이 내 것이거나 A/K 끝).
///   1) 이번 턴 남은 장 수 안에 손패를 다 비울 수 있으면 비운다(승리).
///   2) 추가 장(2·3번째): 안전한 카드만 낸다. 없으면 null(쥐고 턴 종료).
///   3) 첫 장(의무): 안전한 카드 중 그 뒤로 내 카드가 길게 이어지는 것.
///      안전한 카드가 없으면 그 방향에 내 카드가 가장 많이 남은 줄을 연다(언젠가 내가 쓸 길).
/// </summary>
public class BlockadeAiStrategy : IAiStrategy
{
    private readonly System.Random _rng;

    /// <param name="seed">0이면 시스템 랜덤, 그 외에는 결정적 시드(동점일 때만 쓴다).</param>
    public BlockadeAiStrategy(int seed = 0)
    {
        _rng = seed == 0 ? new System.Random() : new System.Random(seed);
    }

    public Card ChooseCard(FieldManager field, IReadOnlyList<Card> hand, AiContext ctx)
    {
        if (field == null || hand == null || hand.Count == 0) return null;
        var playable = field.FilterPlayable(hand);
        if (playable.Count == 0) return null;

        int plays    = ctx != null ? ctx.PlaysThisTurn : 0;
        int maxPlays = ctx != null && ctx.MaxPlaysPerTurn > 0 ? ctx.MaxPlaysPerTurn : 3;

        // 1) 이번 턴에 다 비울 수 있으면 비운다.
        Card finish = FindFinishingMove(field, hand, maxPlays - plays);
        if (finish != null) return finish;

        var safe = new List<Card>();
        foreach (var c in playable)
            if (IsSafe(field, hand, c)) safe.Add(c);

        // 2) 추가 장: 안전한 카드만. 없으면 쥐고 끝낸다.
        if (plays >= 1)
            return safe.Count > 0 ? PickMax(safe, c => MyRun(field, hand, c)) : null;

        // 3) 첫 장(의무)
        if (safe.Count > 0) return PickMax(safe, c => MyRun(field, hand, c));
        return PickMax(playable, c => MyBeyond(field, hand, c));
    }

    // ─────────────────────────────────────────────────────────────
    //  판단 도우미
    // ─────────────────────────────────────────────────────────────

    /// <summary>이 카드를 내면 줄이 어느 쪽으로 자라는지(+1 = 위로 8→K, -1 = 아래로 6→A).</summary>
    private static int Dir(FieldManager field, Card c)
        => c.Number == field.GetLine(c.Suit).MaxPlaced + 1 ? 1 : -1;

    private static bool Contains(IReadOnlyList<Card> hand, Suit s, int n)
    {
        for (int i = 0; i < hand.Count; i++)
            if (hand[i].Suit == s && hand[i].Number == n) return true;
        return false;
    }

    /// <summary>내도 상대에게 새 카드를 열어 주지 않는가(다음 장이 내 것이거나 A/K 끝).</summary>
    private static bool IsSafe(FieldManager field, IReadOnlyList<Card> hand, Card c)
    {
        int next = c.Number + Dir(field, c);
        if (next < Card.MinNumber || next > Card.MaxNumber) return true;
        return Contains(hand, c.Suit, next);
    }

    /// <summary>c 다음부터 그 방향으로 내 손에 연속으로 있는 장 수.</summary>
    private static int MyRun(FieldManager field, IReadOnlyList<Card> hand, Card c)
    {
        int d = Dir(field, c), k = 0;
        for (int n = c.Number + d; n >= Card.MinNumber && n <= Card.MaxNumber && Contains(hand, c.Suit, n); n += d) k++;
        return k;
    }

    /// <summary>c 방향으로 내 손에 남은 카드 수(연속 아니어도).</summary>
    private static int MyBeyond(FieldManager field, IReadOnlyList<Card> hand, Card c)
    {
        int d = Dir(field, c), k = 0;
        for (int i = 0; i < hand.Count; i++)
            if (hand[i].Suit == c.Suit && (hand[i].Number - c.Number) * d > 0) k++;
        return k;
    }

    /// <summary>점수가 가장 높은 카드(동점이면 무작위).</summary>
    private Card PickMax(List<Card> cards, System.Func<Card, int> score)
    {
        Card best = null;
        int bestScore = int.MinValue, ties = 0;
        foreach (var c in cards)
        {
            int s = score(c);
            if (s > bestScore) { best = c; bestScore = s; ties = 1; }
            else if (s == bestScore && _rng.Next(++ties) == 0) best = c;
        }
        return best;
    }

    // ─────────────────────────────────────────────────────────────
    //  마무리 탐색 — 남은 장 수(left) 안에 손패를 다 비울 수 있나
    // ─────────────────────────────────────────────────────────────

    /// <summary>left 장 안에 손패를 다 비울 수 있으면 그 첫 카드, 아니면 null. (손패 ≤ 3장이라 탐색이 작다)</summary>
    private static Card FindFinishingMove(FieldManager field, IReadOnlyList<Card> hand, int left)
    {
        if (hand.Count > left) return null;
        var lo = new int[4];
        var hi = new int[4];
        foreach (var line in field.AllLines)
        {
            lo[(int)line.Suit] = line.MinPlaced;
            hi[(int)line.Suit] = line.MaxPlaced;
        }
        var rest = new List<Card>(hand);
        foreach (var c in hand)
        {
            if (!CanPut(lo, hi, c)) continue;
            int s = (int)c.Suit, oldLo = lo[s], oldHi = hi[s];
            Put(lo, hi, c); rest.Remove(c);
            bool ok = CanEmpty(lo, hi, rest);
            rest.Add(c); lo[s] = oldLo; hi[s] = oldHi;
            if (ok) return c;
        }
        return null;
    }

    private static bool CanPut(int[] lo, int[] hi, Card c)
    {
        int s = (int)c.Suit;
        return (c.Number == lo[s] - 1 && lo[s] > Card.MinNumber)
            || (c.Number == hi[s] + 1 && hi[s] < Card.MaxNumber);
    }

    private static void Put(int[] lo, int[] hi, Card c)
    {
        int s = (int)c.Suit;
        if (c.Number < lo[s]) lo[s] = c.Number;
        if (c.Number > hi[s]) hi[s] = c.Number;
    }

    private static bool CanEmpty(int[] lo, int[] hi, List<Card> rest)
    {
        if (rest.Count == 0) return true;
        for (int i = 0; i < rest.Count; i++)
        {
            var c = rest[i];
            if (!CanPut(lo, hi, c)) continue;
            int s = (int)c.Suit, oldLo = lo[s], oldHi = hi[s];
            Put(lo, hi, c); rest.RemoveAt(i);
            bool ok = CanEmpty(lo, hi, rest);
            rest.Insert(i, c); lo[s] = oldLo; hi[s] = oldHi;
            if (ok) return true;
        }
        return false;
    }
}
