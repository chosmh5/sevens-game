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
    private CardUI                _selected;

    /// <summary>현재 손패 입력 허용 여부. 내 턴이 아닐 땐 false (GameManager가 제어).</summary>
    private bool _interactable = false;

    /// <summary>현재 손패(읽기 전용).</summary>
    public IReadOnlyList<Card> Hand => _hand;

    /// <summary>현재 선택된 카드 (없으면 null).</summary>
    public Card SelectedCard => _selected != null ? _selected.Card : null;

    /// <summary>카드 제출 성공 시 발생 (제출된 카드).</summary>
    public event Action<Card> CardSubmitted;

    /// <summary>손패가 모두 소진됐을 때 발생.</summary>
    public event Action HandEmptied;

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
        _hand.Add(card);
        _hand.Sort((a, b) =>
        {
            int s = a.Suit.CompareTo(b.Suit);
            return s != 0 ? s : a.Number.CompareTo(b.Number);
        });
        RebuildUI();
        RefreshPlayableStates();
    }

    /// <summary>손패 비우기.</summary>
    public void ClearHand()
    {
        _hand.Clear();
        for (int i = _cardUIs.Count - 1; i >= 0; i--)
        {
            if (_cardUIs[i] != null)
                Destroy(_cardUIs[i].gameObject);
        }
        _cardUIs.Clear();
        _selected = null;
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
            bool playable = _interactable && fieldManager.CanPlace(ui.Card);
            ui.SetPlayable(playable);
        }
    }

    /// <summary>
    /// 손패 전체를 강제로 잠금(또는 해제). 턴당 최대 장수 도달 시 GameManager가 호출.
    /// 잠금 상태에서는 카드 클릭이 흔들림만 발생하고 선택/제출이 안 된다.
    /// </summary>
    public void SetHandLocked(bool locked)
    {
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
    }

    // ─────────────────────────────────────────────────────────────
    //  Internals
    // ─────────────────────────────────────────────────────────────

    private void RebuildUI()
    {
        for (int i = _cardUIs.Count - 1; i >= 0; i--)
        {
            if (_cardUIs[i] != null) Destroy(_cardUIs[i].gameObject);
        }
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
        if (!_interactable) return false; // 내 턴이 아니면 제출 불가(방어)
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
        Destroy(ui.gameObject);

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
