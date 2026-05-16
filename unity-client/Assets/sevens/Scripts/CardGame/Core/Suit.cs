/// <summary>
/// 트럼프 카드 4가지 문양.
/// 정수 순서(0~3)는 UI 라인 정렬 등에 그대로 사용해도 무방.
/// </summary>
public enum Suit
{
    Spade   = 0, // ♠
    Heart   = 1, // ♥
    Diamond = 2, // ♦
    Club    = 3  // ♣
}

/// <summary>
/// Suit 관련 헬퍼.
/// </summary>
public static class SuitExtensions
{
    /// <summary>
    /// 문양을 유니코드 심볼 문자열로 변환 (디버그/로그 가독성용).
    /// </summary>
    public static string ToSymbol(this Suit suit)
    {
        switch (suit)
        {
            case Suit.Spade:   return "♠"; // ♠
            case Suit.Heart:   return "♥"; // ♥
            case Suit.Diamond: return "♦"; // ♦
            case Suit.Club:    return "♣"; // ♣
            default:           return "?";
        }
    }
}
