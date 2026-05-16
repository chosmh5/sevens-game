using System;

/// <summary>
/// 한 장의 트럼프 카드.
/// Number = 1(A) ~ 13(K). 7은 '오프닝' 카드.
/// </summary>
[Serializable]
public class Card : IEquatable<Card>
{
    /// <summary>최소 숫자 (A).</summary>
    public const int MinNumber = 1;
    /// <summary>최대 숫자 (K).</summary>
    public const int MaxNumber = 13;
    /// <summary>오프닝 카드 숫자(7).</summary>
    public const int SevenNumber = 7;

    public Suit Suit;
    public int  Number;

    public Card() { }

    public Card(Suit suit, int number)
    {
        Suit   = suit;
        Number = number;
    }

    /// <summary>7 카드인가?</summary>
    public bool IsSeven => Number == SevenNumber;

    /// <summary>유효한 숫자 범위인가? (1~13)</summary>
    public bool IsValid => Number >= MinNumber && Number <= MaxNumber;

    public bool Equals(Card other)
    {
        if (other is null) return false;
        return Suit == other.Suit && Number == other.Number;
    }

    public override bool Equals(object obj) => obj is Card c && Equals(c);

    public override int GetHashCode() => ((int)Suit * 16) ^ Number;

    /// <summary>
    /// 디버그/로그용 표시: 예) "♠7", "♥10", "♦A", "♣K"
    /// </summary>
    public override string ToString()
    {
        string n;
        switch (Number)
        {
            case 1:  n = "A"; break;
            case 11: n = "J"; break;
            case 12: n = "Q"; break;
            case 13: n = "K"; break;
            default: n = Number.ToString(); break;
        }
        return Suit.ToSymbol() + n;
    }
}
