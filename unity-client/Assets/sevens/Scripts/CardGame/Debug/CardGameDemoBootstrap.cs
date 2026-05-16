using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CardGame 씬 즉시 체험용 부트스트랩.
/// Play 시 자동으로:
///   1) 4장의 7을 필드에 깐다 (PlaceInitialSevens)
///   2) 7을 제외한 48장 중 12장을 셔플해서 내 손에 준다 (sevens 배분 사양과 동일)
///
/// 본 컴포넌트는 정식 GameManager가 들어오기 전까지의 임시 진입점.
/// </summary>
public class CardGameDemoBootstrap : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private FieldManager fieldManager;
    [SerializeField] private HandManager  handManager;

    [Header("Setup")]
    [SerializeField] private int  initialHandSize = 12;
    [SerializeField] private int  randomSeed      = 0; // 0이면 매번 다른 결과
    [SerializeField] private bool runOnStart      = true;

    private void Start()
    {
        if (runOnStart) Begin();
    }

    [ContextMenu("▶ Begin Demo Round")]
    public void Begin()
    {
        if (fieldManager == null || handManager == null)
        {
            Debug.LogError("[Demo] FieldManager / HandManager 참조가 비어있습니다.");
            return;
        }

        fieldManager.ResetField();
        fieldManager.PlaceInitialSevens();

        var deck = BuildDeckExcludingSevens();
        Shuffle(deck, randomSeed);

        // 첫 N장만 내 손에 (실제 4인 분배는 GameManager에서)
        var myHand = new List<Card>(initialHandSize);
        for (int i = 0; i < initialHandSize && i < deck.Count; i++) myHand.Add(deck[i]);

        // 정렬: 보기 좋게 문양 → 숫자 순
        myHand.Sort((a, b) =>
        {
            int s = a.Suit.CompareTo(b.Suit);
            return s != 0 ? s : a.Number.CompareTo(b.Number);
        });

        handManager.SetInitialHand(myHand);

        Debug.Log("[Demo] 라운드 시작. 필드: " + fieldManager.DescribeState());
        Debug.Log("[Demo] 내 손: " + JoinCards(myHand));
    }

    [ContextMenu("Refresh Playable States")]
    public void Refresh()
    {
        handManager?.RefreshPlayableStates();
        // 패스 버튼 갱신은 CardGameManager가 담당 (RefreshPassButton 제거됨).
    }

    // ─────────────────────────────────────────────────────────────

    private static List<Card> BuildDeckExcludingSevens()
    {
        var deck = new List<Card>(48);
        foreach (Suit s in Enum.GetValues(typeof(Suit)))
        {
            for (int n = 1; n <= 13; n++)
            {
                if (n == Card.SevenNumber) continue; // 7 제외
                deck.Add(new Card(s, n));
            }
        }
        return deck;
    }

    private static void Shuffle<T>(IList<T> list, int seed)
    {
        var rng = seed == 0 ? new System.Random() : new System.Random(seed);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static string JoinCards(IList<Card> cards)
    {
        if (cards == null || cards.Count == 0) return "(empty)";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < cards.Count; i++)
        {
            sb.Append(cards[i]);
            if (i < cards.Count - 1) sb.Append(' ');
        }
        return sb.ToString();
    }
}
