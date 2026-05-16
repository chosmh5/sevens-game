using System.Collections.Generic;

/// <summary>
/// 가장 단순한 AI: 낼 수 있는 카드 중에서 무작위로 한 장 선택.
/// 사양 8단계의 4번 우선순위에 해당하는 기본 AI.
///
/// 향후 고급 전략(7 우선, 극단 숫자 우선 등)은 IAiStrategy를 새로 구현해서 교체.
/// </summary>
public class RandomAiStrategy : IAiStrategy
{
    private readonly System.Random _rng;

    /// <param name="seed">0이면 시스템 랜덤, 그 외에는 결정적 시드.</param>
    public RandomAiStrategy(int seed = 0)
    {
        _rng = seed == 0 ? new System.Random() : new System.Random(seed);
    }

    public Card ChooseCard(FieldManager field, IReadOnlyList<Card> hand)
    {
        if (field == null || hand == null || hand.Count == 0) return null;

        var playable = field.FilterPlayable(hand);
        if (playable.Count == 0) return null;

        return playable[_rng.Next(playable.Count)];
    }
}
