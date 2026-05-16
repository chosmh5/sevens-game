using UnityEngine;

/// <summary>
/// Suit + Number → Sprite 매핑.
/// 에셋 위치: Assets/sevens/Resources/Cards/cardSuitNumber.png (Kenney 보드게임 팩 명명 규칙)
///
/// 사용: var sprite = CardSpriteLibrary.GetFace(card);
///       var back   = CardSpriteLibrary.GetBack();
///
/// 시작 시 한 번 LoadAll()을 호출하면 모든 스프라이트가 캐시된다.
/// 안 부르면 첫 요청 시 자동 로드.
/// </summary>
public static class CardSpriteLibrary
{
    private const string ResourceFolder = "Cards";

    // [suit][number-1] = sprite. 7도 표시 가능해야 하므로 1~13 모두 보유.
    private static Sprite[,] _faces;
    private static Sprite    _back;
    private static bool      _loaded;

    /// <summary>모든 카드 스프라이트를 미리 로드.</summary>
    public static void LoadAll()
    {
        if (_loaded) return;

        _faces = new Sprite[4, 13];
        foreach (var suit in System.Enum.GetValues(typeof(Suit)))
        {
            var s = (Suit)suit;
            for (int n = 1; n <= 13; n++)
            {
                string name = BuildName(s, n);
                var sprite = Resources.Load<Sprite>($"{ResourceFolder}/{name}");
                if (sprite == null)
                {
                    Debug.LogWarning($"[CardSpriteLibrary] 스프라이트 없음: {name} (경로 {ResourceFolder}/{name})");
                }
                _faces[(int)s, n - 1] = sprite;
            }
        }

        _back = Resources.Load<Sprite>($"{ResourceFolder}/cardBack_blue1");
        if (_back == null)
            Debug.LogWarning("[CardSpriteLibrary] 뒷면 스프라이트(cardBack_blue1) 없음");

        _loaded = true;
    }

    /// <summary>해당 카드의 앞면 스프라이트. 없으면 null.</summary>
    public static Sprite GetFace(Card card)
    {
        if (card == null) return null;
        if (!_loaded) LoadAll();
        if (card.Number < 1 || card.Number > 13) return null;
        return _faces[(int)card.Suit, card.Number - 1];
    }

    /// <summary>카드 뒷면 스프라이트.</summary>
    public static Sprite GetBack()
    {
        if (!_loaded) LoadAll();
        return _back;
    }

    /// <summary>
    /// Kenney 카드 명명 규칙으로 파일 이름 생성.
    /// 예) Spade 1  → "cardSpadesA"
    ///     Heart 10 → "cardHearts10"
    ///     Diamond 11→ "cardDiamondsJ"
    /// </summary>
    private static string BuildName(Suit suit, int number)
    {
        string suitStr;
        switch (suit)
        {
            case Suit.Spade:   suitStr = "Spades";   break;
            case Suit.Heart:   suitStr = "Hearts";   break;
            case Suit.Diamond: suitStr = "Diamonds"; break;
            case Suit.Club:    suitStr = "Clubs";    break;
            default:           suitStr = "Spades";   break;
        }

        string numStr;
        switch (number)
        {
            case 1:  numStr = "A"; break;
            case 11: numStr = "J"; break;
            case 12: numStr = "Q"; break;
            case 13: numStr = "K"; break;
            default: numStr = number.ToString(); break;
        }

        return $"card{suitStr}{numStr}";
    }
}
