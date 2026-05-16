using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 필드 한 줄(한 문양)의 시각화.
/// 13개 슬롯(A..K) 각각에 Image를 두고, 해당 슬롯에 카드가 놓이면 스프라이트로 채운다.
/// FieldManager의 CardPlaced/FieldReset 이벤트를 구독해 실시간 갱신.
///
/// 구성: 슬롯 13개 = index 0..12 (각각 카드 숫자 1..13에 대응).
/// </summary>
public class FieldLineView : MonoBehaviour
{
    [SerializeField] private Suit         suit;
    [SerializeField] private FieldManager fieldManager;
    [SerializeField] private Image[]      slots = new Image[13];

    [Header("Visuals")]
    [Tooltip("비어있는 슬롯의 배경색 (안내용 placeholder).")]
    [SerializeField] private Color emptySlotColor = new Color(1, 1, 1, 0.05f);
    [Tooltip("카드가 놓인 슬롯의 색상 (보통 white).")]
    [SerializeField] private Color filledSlotColor = Color.white;

    private bool _subscribed;

    private void Awake()
    {
        ClearAll();
        TrySubscribe();
    }

    private void Start()
    {
        // FieldManager가 늦게 셋업될 수도 있으니 Start에서 한 번 더.
        TrySubscribe();
        // 이미 놓여있는 7 등도 즉시 동기화.
        SyncFromCurrentState();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    /// <summary>외부에서 FieldManager 참조를 늦게 주입할 때 사용.</summary>
    public void Bind(FieldManager fm, Suit s)
    {
        Unsubscribe();
        fieldManager = fm;
        suit = s;
        TrySubscribe();
        ClearAll();
        SyncFromCurrentState();
    }

    // ─────────────────────────────────────────────────────────────

    private void TrySubscribe()
    {
        if (_subscribed || fieldManager == null) return;
        fieldManager.CardPlaced += OnCardPlaced;
        fieldManager.FieldReset += OnFieldReset;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || fieldManager == null) return;
        fieldManager.CardPlaced -= OnCardPlaced;
        fieldManager.FieldReset -= OnFieldReset;
        _subscribed = false;
    }

    private void OnCardPlaced(Card card, FieldLine line)
    {
        if (card == null || card.Suit != suit) return;
        Fill(card);
    }

    private void OnFieldReset()
    {
        ClearAll();
    }

    private void SyncFromCurrentState()
    {
        if (fieldManager == null) return;
        var line = fieldManager.GetLine(suit);
        if (!line.IsOpen) return;
        for (int n = line.MinPlaced; n <= line.MaxPlaced; n++)
        {
            Fill(new Card(suit, n));
        }
    }

    private void Fill(Card card)
    {
        int idx = card.Number - 1;
        if (idx < 0 || idx >= slots.Length) return;
        var img = slots[idx];
        if (img == null) return;

        var sprite = CardSpriteLibrary.GetFace(card);
        img.sprite = sprite;
        img.color  = filledSlotColor;
        img.preserveAspect = true;
    }

    private void ClearAll()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            slots[i].sprite = null;
            slots[i].color  = emptySlotColor;
        }
    }
}
