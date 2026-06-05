using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 필드(중앙 카드판)의 상태와 카드 제출 유효성 검사를 담당.
/// - 4개 문양 줄(FieldLine)을 보유.
/// - 외부에서는 CanPlace / TryPlace로 카드 한 장 단위 처리.
/// - "한 턴에 3장까지" 같은 상위 규칙은 GameManager가 책임진다.
///
/// 본 사양: 4장의 7은 게임 시작 시 자동으로 깔리며 손패에 들어가지 않는다.
/// </summary>
public class FieldManager : MonoBehaviour
{
    private readonly Dictionary<Suit, FieldLine> _lines = new Dictionary<Suit, FieldLine>();

    /// <summary>카드가 필드에 놓였을 때 발생. (Card, 놓인 라인의 Suit)</summary>
    public event Action<Card, FieldLine> CardPlaced;

    /// <summary>카드가 필드에서 되돌려졌을 때(제출 취소) 발생. (Card, 라인)</summary>
    public event Action<Card, FieldLine> CardRemoved;

    /// <summary>필드가 리셋(라운드 시작)됐을 때 발생.</summary>
    public event Action FieldReset;

    private void Awake()
    {
        BuildLines();
    }

    /// <summary>
    /// 4개 줄을 만들어 둔다. 아직 7은 놓이지 않은 상태(IsOpen=false).
    /// </summary>
    private void BuildLines()
    {
        _lines.Clear();
        foreach (Suit s in Enum.GetValues(typeof(Suit)))
        {
            _lines[s] = new FieldLine(s);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    /// <summary>지정 문양의 줄을 반환.</summary>
    public FieldLine GetLine(Suit suit) => _lines[suit];

    /// <summary>모든 줄(읽기 전용 순회용).</summary>
    public IEnumerable<FieldLine> AllLines => _lines.Values;

    /// <summary>
    /// 사양: "4장의 7카드는 필드 위에 자동으로 세팅된다."
    /// 게임 시작 시 1회 호출 — 4개 줄을 모두 열고 7을 놓는다.
    /// </summary>
    public void PlaceInitialSevens()
    {
        foreach (Suit s in Enum.GetValues(typeof(Suit)))
        {
            var line = _lines[s];
            line.Reset();
            line.Place(new Card(s, Card.SevenNumber));
            CardPlaced?.Invoke(new Card(s, Card.SevenNumber), line);
        }
    }

    /// <summary>
    /// 라운드 재시작용. 모든 줄을 초기 상태(닫힘)로 되돌린다.
    /// </summary>
    public void ResetField()
    {
        foreach (var line in _lines.Values) line.Reset();
        FieldReset?.Invoke();
    }

    /// <summary>이 카드를 어떤 줄에든 둘 수 있는가?</summary>
    public bool CanPlace(Card card)
    {
        if (card == null || !card.IsValid) return false;
        return _lines[card.Suit].CanPlace(card);
    }

    /// <summary>
    /// 카드를 놓는다. 가능하면 true, 아니면 false (필드 상태 변경 없음).
    /// </summary>
    public bool TryPlace(Card card)
    {
        if (!CanPlace(card)) return false;

        var line = _lines[card.Suit];
        line.Place(card);
        CardPlaced?.Invoke(card, line);
        return true;
    }

    /// <summary>
    /// 카드 제출을 되돌린다(취소). 되돌릴 카드는 해당 줄의 현재 끝이어야 한다.
    /// 성공하면 true, CardRemoved 이벤트 발생(뷰가 슬롯을 비우도록).
    /// </summary>
    public bool UndoPlace(Card card)
    {
        if (card == null) return false;
        var line = _lines[card.Suit];
        if (!line.Undo(card)) return false;
        CardRemoved?.Invoke(card, line);
        return true;
    }

    /// <summary>
    /// 손패 중 현재 즉시 낼 수 있는 카드가 하나라도 있는가?
    /// 패스 버튼 활성/비활성 판단에 사용.
    /// </summary>
    public bool HasAnyPlayable(IEnumerable<Card> hand)
    {
        if (hand == null) return false;
        foreach (var c in hand)
            if (CanPlace(c)) return true;
        return false;
    }

    /// <summary>
    /// 손패에서 즉시 낼 수 있는 카드만 추려서 반환. (UI 강조 / AI 후보 선택용)
    /// </summary>
    public List<Card> FilterPlayable(IEnumerable<Card> hand)
    {
        var result = new List<Card>();
        if (hand == null) return result;
        foreach (var c in hand)
            if (CanPlace(c)) result.Add(c);
        return result;
    }

    // ─────────────────────────────────────────────────────────────
    //  Debug
    // ─────────────────────────────────────────────────────────────

    /// <summary>현재 필드 상태를 한 줄 문자열로 (로그용).</summary>
    public string DescribeState()
    {
        var sb = new System.Text.StringBuilder();
        foreach (Suit s in Enum.GetValues(typeof(Suit)))
        {
            sb.Append(_lines[s]);
            sb.Append("  ");
        }
        return sb.ToString().TrimEnd();
    }
}
