using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어의 손패를 관리.
/// - List&lt;Card&gt; 보유
/// - CardUI 프리팹을 자식으로 스폰
/// - 카드 선택 1장 토글 (다른 카드 선택 시 이전 선택 해제)
/// - 더블탭 또는 외부 호출(SubmitSelected)로 필드에 제출
/// - 손패가 변할 때마다 FieldManager를 보고 각 카드의 Playable 상태를 갱신
///
/// 사양상 본 컴포넌트는 "내 손패"용 — 상대 AI 손패는 별도 클래스에서 처리.
/// </summary>
public class HandManager : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CardUI       cardPrefab;
    [SerializeField] private RectTransform handArea;     // 카드 부모 (FanLayout이 부착됨)
    [SerializeField] private FanLayout    fanLayout;     // 보통 handArea와 같은 GameObject
    [SerializeField] private FieldManager fieldManager;  // 유효성 검사 의뢰

    [Header("Behavior")]
    [Tooltip("선택된 카드를 한 번 더 클릭하면 즉시 제출")]
    [SerializeField] private bool tapSelectedToSubmit = true;

    [Tooltip("같은 카드를 다시 클릭하면 선택 해제(토글)")]
    [SerializeField] private bool toggleOnReclick = true;

    // ─────────────────────────────────────────────────────────────

    private readonly List<Card>   _hand     = new List<Card>();
    private readonly List<CardUI> _cardUIs  = new List<CardUI>();
    private CardUI                _selectedUI;
    /// <summary>선택 카드. 바뀔 때마다 SelectionChanged 발생.</summary>
    private CardUI _selected
    {
        get => _selectedUI;
        set
        {
            if (_selectedUI == value) return;
            _selectedUI = value;
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>현재 손패 입력 허용 여부. 내 턴이 아닐 땐 false (GameManager가 제어).</summary>
    private bool _interactable = false;

    /// <summary>턴당 최대 장수에 도달해 손패가 잠겼는지. 잠긴 동안엔 다른 곳에서 가용성을 다시 그려도 낼 수 없다.</summary>
    private bool _locked = false;

    /// <summary>현재 손패(읽기 전용).</summary>
    public IReadOnlyList<Card> Hand => _hand;

    /// <summary>현재 선택된 카드 (없으면 null).</summary>
    public Card SelectedCard => _selected != null ? _selected.Card : null;

    /// <summary>카드 제출 성공 시 발생 (제출된 카드).</summary>
    public event Action<Card> CardSubmitted;

    /// <summary>손패가 모두 소진됐을 때 발생.</summary>
    public event Action HandEmptied;

    /// <summary>선택 카드가 바뀔 때 발생(필드의 '들어갈 칸' 표시 갱신용).</summary>
    public event Action SelectionChanged;

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    /// <summary>손패를 초기 카드 리스트로 설정. 기존 UI는 모두 제거 후 재생성.</summary>
    public void SetInitialHand(IEnumerable<Card> cards)
    {
        ClearHand();
        if (cards == null) return;
        foreach (var c in cards) _hand.Add(c);
        RebuildUI();
        RefreshPlayableStates();
    }

    /// <summary>카드 한 장 추가 (드로우 등).</summary>
    public void AddCard(Card card)
    {
        if (card == null) return;
        _hand.Add(card);
        SpawnCardUI(card);
        fanLayout?.ApplyLayout();
        RefreshPlayableStates();
    }

    /// <summary>
    /// 제출 취소로 카드를 손패에 되돌린다. 정렬 위치에 다시 꽂고 UI를 재구성한다.
    /// </summary>
    public void ReturnCard(Card card)
    {
        if (card == null) return;
        _locked = false;   // 한 장 되돌렸으니 다시 낼 수 있다

        // 정렬 위치를 찾아 그 자리에 카드 한 장만 다시 꽂는다(예전엔 손패 UI 전체를 지우고 다시 만들었다).
        int idx = 0;
        while (idx < _hand.Count && CompareCards(_hand[idx], card) <= 0) idx++;
        _hand.Insert(idx, card);

        var ui = SpawnCardUI(card);
        if (ui != null)
        {
            _cardUIs.Remove(ui);
            _cardUIs.Insert(Mathf.Min(idx, _cardUIs.Count), ui);
            ui.transform.SetSiblingIndex(SiblingIndexFor(idx));
        }
        fanLayout?.ApplyLayout();
        RefreshPlayableStates();
    }

    private static int CompareCards(Card a, Card b)
    {
        int s = a.Suit.CompareTo(b.Suit);
        return s != 0 ? s : a.Number.CompareTo(b.Number);
    }

    /// <summary>손패 idx 번째 카드가 handArea 에서 몇 번째 자식이어야 하는지. 새 카드는 맨 끝에 생기므로
    /// 바로 다음 카드의 자리로 옮기면 그 앞에 끼워진다(다음 카드가 없으면 맨 끝 그대로).</summary>
    private int SiblingIndexFor(int idx)
    {
        if (idx + 1 < _cardUIs.Count && _cardUIs[idx + 1] != null)
            return _cardUIs[idx + 1].transform.GetSiblingIndex();
        return handArea != null ? handArea.childCount - 1 : 0;
    }

    /// <summary>손패 비우기.</summary>
    public void ClearHand()
    {
        _hand.Clear();
        for (int i = _cardUIs.Count - 1; i >= 0; i--)
            DestroyCardUI(_cardUIs[i]);
        _cardUIs.Clear();
        _selected = null;
        _locked = false;
    }

    /// <summary>
    /// 카드 UI 제거. Destroy 는 프레임 끝에 일어나서 그 사이 부채꼴 배치가 사라질 카드까지 세므로,
    /// 먼저 비활성화해 배치에서 바로 빠지게 한다.
    /// </summary>
    private void DestroyCardUI(CardUI ui)
    {
        if (ui == null) return;
        ui.OnClicked -= HandleCardClicked;
        ui.gameObject.SetActive(false);
        Destroy(ui.gameObject);
    }

    /// <summary>선택된 카드를 필드에 제출. 성공이면 true.</summary>
    public bool SubmitSelected()
    {
        if (_selected == null) return false;
        return SubmitCard(_selected);
    }

    /// <summary>
    /// 손패 입력 허용 여부 설정. 내 턴 시작 시 true, AI 턴/게임오버 시 false.
    /// false면 카드 클릭이 무시되고(상대 턴 중 제출 버그 방지), 모든 카드가 비활성 표시된다.
    /// </summary>
    public void SetInteractable(bool value)
    {
        _interactable = value;
        _locked = false;   // 턴이 바뀌면 잠금도 풀린다
        if (!value && _selected != null)
        {
            _selected.SetSelected(false);
            _selected = null;
        }
        RefreshPlayableStates();
    }

    /// <summary>외부에서 필드 상태가 바뀐 뒤 호출해 손패 가용성을 다시 그리도록.</summary>
    public void RefreshPlayableStates()
    {
        if (fieldManager == null) return;
        for (int i = 0; i < _cardUIs.Count; i++)
        {
            var ui = _cardUIs[i];
            if (ui == null) continue;
            // 내 턴이 아니면 전부 비활성 표시(클릭도 차단됨).
            bool playable = _interactable && !_locked && fieldManager.CanPlace(ui.Card);
            ui.SetPlayable(playable);
        }
    }

    /// <summary>
    /// 손패 전체를 강제로 잠금(또는 해제). 턴당 최대 장수 도달 시 GameManager가 호출.
    /// 잠금 상태에서는 카드 클릭이 흔들림만 발생하고 선택/제출이 안 된다.
    /// </summary>
    public void SetHandLocked(bool locked)
    {
        _locked = locked;
        for (int i = 0; i < _cardUIs.Count; i++)
        {
            var ui = _cardUIs[i];
            if (ui == null) continue;
            if (locked) ui.SetPlayable(false);
        }
        if (locked && _selected != null)
        {
            _selected.SetSelected(false);
            _selected = null;
        }
        if (!locked) RefreshPlayableStates();
    }

    // ─────────────────────────────────────────────────────────────
    //  Internals
    // ─────────────────────────────────────────────────────────────

    private void RebuildUI()
    {
        for (int i = _cardUIs.Count - 1; i >= 0; i--)
            DestroyCardUI(_cardUIs[i]);
        _cardUIs.Clear();
        _selected = null;

        foreach (var c in _hand) SpawnCardUI(c);
        fanLayout?.ApplyLayout();
    }

    private CardUI SpawnCardUI(Card card)
    {
        if (cardPrefab == null || handArea == null) return null;
        var ui = Instantiate(cardPrefab, handArea);
        ui.SetCard(card);
        ui.OnClicked += HandleCardClicked;
        _cardUIs.Add(ui);
        return ui;
    }

    private void HandleCardClicked(CardUI ui)
    {
        if (ui == null) return;

        // 내 턴이 아니면 입력 무시 (상대 턴 중 카드 제출 버그 방지).
        if (!_interactable) return;
        if (_locked) { ui.PlayShake(); return; }   // 이번 턴 최대 장수를 이미 냄

        // 같은 카드를 다시 클릭한 경우
        if (_selected == ui)
        {
            if (tapSelectedToSubmit)
            {
                // 더블탭 효과: 제출 시도
                if (ui.IsPlayable)
                {
                    SubmitCard(ui);
                    return;
                }
                ui.PlayShake();
                return;
            }
            if (toggleOnReclick)
            {
                ui.SetSelected(false);
                _selected = null;
                return;
            }
            return;
        }

        // 새 카드 선택
        if (!ui.IsPlayable)
        {
            ui.PlayShake();
            return;
        }

        if (_selected != null) _selected.SetSelected(false);
        _selected = ui;
        ui.SetSelected(true);
    }

    private bool SubmitCard(CardUI ui)
    {
        if (ui == null || ui.Card == null) return false;
        if (!_interactable || _locked) return false; // 내 턴이 아니거나 잠겼으면 제출 불가(방어)
        if (fieldManager == null)
        {
            Debug.LogWarning("[HandManager] FieldManager가 연결되지 않았습니다.");
            return false;
        }

        Card card = ui.Card;
        if (!fieldManager.TryPlace(card))
        {
            ui.PlayShake();
            return false;
        }

        // 손에서 제거 + UI 파괴
        _hand.Remove(card);
        _cardUIs.Remove(ui);
        if (_selected == ui) _selected = null;
        DestroyCardUI(ui);

        fanLayout?.ApplyLayout();
        RefreshPlayableStates();

        CardSubmitted?.Invoke(card);
        if (_hand.Count == 0) HandEmptied?.Invoke();

        return true;
    }

    private void OnDestroy()
    {
        // 이벤트 해제 (방어적)
        for (int i = 0; i < _cardUIs.Count; i++)
        {
            if (_cardUIs[i] != null)
                _cardUIs[i].OnClicked -= HandleCardClicked;
        }
    }
}
