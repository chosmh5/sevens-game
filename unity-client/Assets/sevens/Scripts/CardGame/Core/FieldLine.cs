using System;

/// <summary>
/// 필드의 한 줄(특정 문양 하나).
/// 7이 놓이면 IsOpen=true가 되고, MinPlaced/MaxPlaced=7로 초기화.
/// 이후 위(8,9,…,K) / 아래(6,5,…,A) 방향으로만 1씩 확장된다.
/// </summary>
[Serializable]
public class FieldLine
{
    public Suit Suit { get; private set; }

    /// <summary>현재 놓인 카드 중 가장 작은 숫자. IsOpen=false면 의미 없음.</summary>
    public int MinPlaced { get; private set; }

    /// <summary>현재 놓인 카드 중 가장 큰 숫자. IsOpen=false면 의미 없음.</summary>
    public int MaxPlaced { get; private set; }

    /// <summary>이 줄이 열렸는가? (해당 문양 7이 필드에 나왔는지)</summary>
    public bool IsOpen { get; private set; }

    public FieldLine(Suit suit)
    {
        Suit      = suit;
        IsOpen    = false;
        MinPlaced = Card.SevenNumber;
        MaxPlaced = Card.SevenNumber;
    }

    /// <summary>
    /// 이 카드가 이 줄에 놓일 수 있는가?
    /// - 줄이 닫혀 있으면 7만 가능.
    /// - 줄이 열려 있으면 MinPlaced-1 또는 MaxPlaced+1만 가능.
    /// - 단, MinPlaced=1(A) 또는 MaxPlaced=13(K)까지 가면 그 방향은 더 이상 확장 불가.
    /// </summary>
    public bool CanPlace(Card card)
    {
        if (card == null) return false;
        if (card.Suit != Suit) return false;
        if (!card.IsValid)     return false;

        if (!IsOpen)
        {
            // 줄이 닫힌 상태에서는 7만 가능.
            return card.IsSeven;
        }

        // 열린 상태 — 양 끝에 한 칸씩만 확장 허용.
        bool extendLow  = card.Number == MinPlaced - 1 && MinPlaced > Card.MinNumber;
        bool extendHigh = card.Number == MaxPlaced + 1 && MaxPlaced < Card.MaxNumber;
        return extendLow || extendHigh;
    }

    /// <summary>
    /// 카드를 실제로 놓는다. (호출 전 CanPlace 통과를 가정)
    /// 7이면 줄을 연다. 그 외에는 Min/Max를 갱신.
    /// </summary>
    public void Place(Card card)
    {
        if (!CanPlace(card))
            throw new InvalidOperationException(
                $"[FieldLine:{Suit.ToSymbol()}] {card}는 현재 이 줄에 놓을 수 없습니다. " +
                $"(IsOpen={IsOpen}, Min={MinPlaced}, Max={MaxPlaced})");

        if (card.IsSeven)
        {
            IsOpen    = true;
            MinPlaced = Card.SevenNumber;
            MaxPlaced = Card.SevenNumber;
            return;
        }

        if (card.Number < MinPlaced) MinPlaced = card.Number;
        if (card.Number > MaxPlaced) MaxPlaced = card.Number;
    }

    /// <summary>줄 상태 리셋. 라운드 재시작 시 사용.</summary>
    public void Reset()
    {
        IsOpen    = false;
        MinPlaced = Card.SevenNumber;
        MaxPlaced = Card.SevenNumber;
    }

    public override string ToString()
        => IsOpen
            ? $"{Suit.ToSymbol()} [{MinPlaced}..{MaxPlaced}]"
            : $"{Suit.ToSymbol()} (closed)";
}
