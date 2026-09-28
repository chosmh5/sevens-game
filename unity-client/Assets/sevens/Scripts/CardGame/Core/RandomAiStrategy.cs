using System.Collections.Generic;

/// <summary>
/// 세븐 게임의 AI 전략.
///
/// ── 설계 근거(시뮬레이션으로 검증) ────────────────────────────────
/// 이 게임은 "의무 제출 + 턴당 최대 3장 + 줄이 7로 미리 열림" 규칙이라,
/// 한 턴에 낼 수 있는 카드의 '집합'이 선택·순서와 무관하게 고정된다.
/// → 어떤 카드를 영리하게 고르는지는 승률에 영향이 없고(최대화=최소화=무작위),
///   유일하게 의미 있는 레버는 "낼 수 있는 만큼 다 내느냐"뿐이다.
///   (완벽하게 둬도 승률 상한은 공정 몫 ~25%.)
///
/// 따라서 난이도는 '더 똑똑한 선택'이 아니라 '실수(추가 장 놓침) 빈도'로 표현한다.
/// - overlookChance = 0.0  → Hard : 절대 안 놓침(완벽, 항상 최대 제출)
/// - overlookChance = 0.20 → Normal
/// - overlookChance = 0.55 → Easy : 자주 놓쳐서 사람이 이기기 쉬움
///
/// 첫 장(ctx.PlaysThisTurn == 0)은 규칙상 의무이므로 절대 놓치지 않는다.
/// 카드 선택 자체는 승률 무관이라 무작위로 한다.
/// </summary>
public class RandomAiStrategy : IAiStrategy
{
    private readonly System.Random _rng;
    private readonly float _overlookChance; // 추가 장(2·3번째)을 놓쳐 조기 종료할 확률

    /// <param name="seed">0이면 시스템 랜덤, 그 외에는 결정적 시드.</param>
    /// <param name="overlookChance">추가 장 놓침 확률(0=완벽). 첫 장에는 적용 안 됨.</param>
    public RandomAiStrategy(int seed = 0, float overlookChance = 0f)
    {
        _rng            = seed == 0 ? new System.Random() : new System.Random(seed);
        _overlookChance = overlookChance;
    }

    public Card ChooseCard(FieldManager field, IReadOnlyList<Card> hand, AiContext ctx)
    {
        if (field == null || hand == null || hand.Count == 0) return null;

        var playable = field.FilterPlayable(hand);
        if (playable.Count == 0) return null;

        // 첫 장은 의무. 추가 장에서만 일정 확률로 '놓쳐서' 턴을 일찍 끝낸다(약한 플레이 연출).
        if (ctx != null && ctx.PlaysThisTurn >= 1 && _overlookChance > 0f
            && _rng.NextDouble() < _overlookChance)
            return null;

        return playable[_rng.Next(playable.Count)];
    }
}
