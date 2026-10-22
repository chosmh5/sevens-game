using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 레트로 필드: 위쪽 정보줄(TURN·난이도·턴 순서·이번 턴 제출 수) + 문양 4줄 × 13칸.
/// 칸 상태: 놓인 카드 / 7(금테) / 내가 고른 카드가 들어갈 칸(▼) / 내 손패로 채울 수 있는 칸 / 열린 칸 / 빈칸.
/// FieldManager·HandManager·CardGameManager 이벤트로만 갱신(매 프레임 폴링 없음).
/// </summary>
public class RetroFieldView : MonoBehaviour
{
    public const float Width = 980f, Height = 378f;
    private const float PadX = 20f, HeaderH = 42f, RowH = 70f, RowGap = 8f, SlotW = 62f, SlotGap = 5f, LabelW = 54f;

    private static readonly Suit[] Order = { Suit.Spade, Suit.Heart, Suit.Diamond, Suit.Club };

    private FieldManager _field;
    private HandManager _hand;
    private CardGameManager _mgr;

    private readonly RetroCardFace[,] _slots = new RetroCardFace[4, 13];
    private readonly TextMeshProUGUI[] _rowCount = new TextMeshProUGUI[4];

    private TextMeshProUGUI _turnNum, _diff, _playsText;
    private readonly List<Image> _orderChips = new List<Image>();
    private readonly List<TextMeshProUGUI> _orderTexts = new List<TextMeshProUGUI>();
    private readonly Image[] _pips = new Image[3];

    public static RetroFieldView Create(RectTransform parent, FieldManager field, HandManager hand, CardGameManager mgr)
    {
        var rt = RetroTheme.Node("RetroField", parent);
        var v = rt.gameObject.AddComponent<RetroFieldView>();
        v._field = field;
        v._hand = hand;
        v._mgr = mgr;
        v.Build();
        v.Subscribe();
        return v;
    }

    private void Build()
    {
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(Width, Height);
        var bg = RetroTheme.Panel(rt, new Color(0.05f, 0.04f, 0.09f, 0.55f), 14, 3, 0, 0f);

        BuildHeader(rt);

        for (int r = 0; r < 4; r++)
        {
            float top = 10f + HeaderH + RowGap + r * (RowH + RowGap);
            var lab = RetroTheme.Place(RetroTheme.Node($"Label_{Order[r]}", rt), new Vector2(0f, 1f), new Vector2(0f, 1f),
                                       new Vector2(PadX, -top), new Vector2(LabelW, RowH));
            RetroTheme.InsetBox(lab);
            var sym = RetroTheme.Label(lab, "Sym", Order[r].ToSymbol(), 33f, RetroTheme.SuitLight(Order[r]), false, TextAlignmentOptions.Center);
            RetroTheme.Place((RectTransform)sym.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(LabelW, 36f));
            _rowCount[r] = RetroTheme.Label(lab, "Count", "", 22f, RetroTheme.Muted);
            RetroTheme.Place((RectTransform)_rowCount[r].transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(LabelW, 26f));

            for (int n = 1; n <= 13; n++)
            {
                float x = PadX + LabelW + 10f + (n - 1) * (SlotW + SlotGap);
                var slot = RetroTheme.Place(RetroTheme.Node($"Slot_{Order[r]}_{n}", rt), new Vector2(0f, 1f), new Vector2(0f, 1f),
                                            new Vector2(x, -top), new Vector2(SlotW, RowH));
                _slots[r, n - 1] = RetroCardFace.Create(slot, false);
            }
        }
    }

    private void BuildHeader(RectTransform rt)
    {
        // 왼쪽: TURN n + 난이도
        var left = RetroTheme.Place(RetroTheme.Node("TurnBox", rt), new Vector2(0f, 1f), new Vector2(0f, 1f),
                                    new Vector2(PadX, -10f), new Vector2(124f, 40f));
        RetroTheme.InsetBox(left);
        var tl = RetroTheme.Label(left, "Label", "TURN", 22f, RetroTheme.Muted, false, TextAlignmentOptions.Left);
        RetroTheme.Place((RectTransform)tl.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(60f, 30f));
        _turnNum = RetroTheme.Label(left, "Num", "1", 33f, RetroTheme.Text, true, TextAlignmentOptions.Right);
        RetroTheme.Place((RectTransform)_turnNum.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(54f, 36f));

        var diff = RetroTheme.Place(RetroTheme.Node("Difficulty", rt), new Vector2(0f, 1f), new Vector2(0f, 1f),
                                    new Vector2(PadX + 132f, -10f), new Vector2(104f, 40f));
        RetroTheme.InsetBox(diff);
        _diff = RetroTheme.Label(diff, "Text", "", 22f, RetroTheme.Gold, true);
        RetroTheme.Stretch((RectTransform)_diff.transform);

        // 가운데: 턴 순서
        var order = RetroTheme.Place(RetroTheme.Node("TurnOrder", rt), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                     new Vector2(-10f, -10f), new Vector2(380f, 40f));
        var hl = order.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6f;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        for (int i = 0; i < 4; i++)
        {
            if (i > 0)
            {
                var arrow = RetroTheme.Label(order, "Arrow", "▶", 22f, RetroTheme.Faint);
                arrow.fontSize = 16f;
            }
            var chip = RetroTheme.Chip(order, $"Seat{i}", "", RetroTheme.Inset, RetroTheme.Muted, 22f);
            _orderTexts.Add(chip);
            _orderChips.Add(chip.transform.parent.GetComponent<Image>());
        }

        // 오른쪽: 이번 턴 ■□□ k/3
        var right = RetroTheme.Place(RetroTheme.Node("Plays", rt), new Vector2(1f, 1f), new Vector2(1f, 1f),
                                     new Vector2(-PadX, -10f), new Vector2(214f, 40f));
        RetroTheme.InsetBox(right);
        var pl = RetroTheme.Label(right, "Label", "이번 턴", 22f, RetroTheme.Muted, false, TextAlignmentOptions.Left);
        RetroTheme.Place((RectTransform)pl.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(80f, 30f));
        for (int i = 0; i < 3; i++)
        {
            var pip = RetroTheme.Place(RetroTheme.Node($"Pip{i}", right), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                       new Vector2(96f + i * 20f, 0f), new Vector2(16f, 16f));
            _pips[i] = pip.gameObject.AddComponent<Image>();
            _pips[i].sprite = RetroTheme.PanelSprite(0, 2);
            _pips[i].type = Image.Type.Sliced;
            _pips[i].raycastTarget = false;
        }
        _playsText = RetroTheme.Label(right, "Count", "0/3", 33f, RetroTheme.Text, true, TextAlignmentOptions.Right);
        RetroTheme.Place((RectTransform)_playsText.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(60f, 36f));
    }

    // ─────────────────────────────────────────────────────────────
    private void Subscribe()
    {
        if (_field != null)
        {
            _field.CardPlaced  += OnPlaced;
            _field.CardRemoved += OnFieldChanged;
            _field.FieldReset  += RefreshSlots;
        }
        if (_hand != null) _hand.SelectionChanged += RefreshSlots;
        if (_mgr != null)
        {
            _mgr.TurnStateChanged += RefreshSlots;
            _mgr.GameStarted      += RefreshSlots;
        }
    }

    private void OnDestroy()
    {
        if (_field != null)
        {
            _field.CardPlaced  -= OnPlaced;
            _field.CardRemoved -= OnFieldChanged;
            _field.FieldReset  -= RefreshSlots;
        }
        if (_hand != null) _hand.SelectionChanged -= RefreshSlots;
        if (_mgr != null)
        {
            _mgr.TurnStateChanged -= RefreshSlots;
            _mgr.GameStarted      -= RefreshSlots;
        }
    }

    private void OnPlaced(Card card, FieldLine line)
    {
        RefreshSlots();
        if (card == null || !isActiveAndEnabled) return;
        int r = System.Array.IndexOf(Order, card.Suit);
        if (r < 0 || card.Number < 1 || card.Number > 13) return;
        var t = _slots[r, card.Number - 1].transform.parent;
        StartCoroutine(Punch(t, 1.18f, 0.2f));
    }

    private void OnFieldChanged(Card card, FieldLine line) => RefreshSlots();

    /// <summary>모든 칸 상태를 다시 계산.</summary>
    public void RefreshSlots()
    {
        if (_field == null) return;
        bool myTurn = _mgr != null && _mgr.CurrentState == CardGameManager.State.PlayerTurn
                      && _mgr.PlaysThisTurn < _mgr.MaxPlaysPerTurn;
        IReadOnlyList<Card> hand = _hand != null ? _hand.Hand : null;
        Card selected = _hand != null ? _hand.SelectedCard : null;

        for (int r = 0; r < 4; r++)
        {
            var line = _field.GetLine(Order[r]);
            int placedCount = 0;
            for (int n = 1; n <= 13; n++)
            {
                var face = _slots[r, n - 1];
                var card = new Card(Order[r], n);
                bool placed = line.IsOpen && n >= line.MinPlaced && n <= line.MaxPlaced;
                if (placed)
                {
                    placedCount++;
                    face.SetMode(n == Card.SevenNumber ? RetroCardFace.Mode.FaceSeven : RetroCardFace.Mode.Face, card, n);
                    continue;
                }
                bool target = _field.CanPlace(card);
                if (!target) { face.SetMode(RetroCardFace.Mode.Empty, null, n); continue; }

                bool inHand = false;
                if (myTurn && hand != null)
                    for (int i = 0; i < hand.Count; i++) if (hand[i].Equals(card)) { inHand = true; break; }

                if (inHand && selected != null && selected.Equals(card))
                    face.SetMode(RetroCardFace.Mode.TargetSelected, card, n);
                else if (inHand)
                    face.SetMode(RetroCardFace.Mode.TargetMine, card, n);
                else
                    face.SetMode(RetroCardFace.Mode.TargetOpen, null, n);
            }
            _rowCount[r].text = $"{placedCount}/13";
        }
    }

    /// <summary>정보줄 갱신(스킨이 호출).</summary>
    public void SetHeader(int turn, string difficulty, IReadOnlyList<string> seatNames, int currentSeat, int plays, int maxPlays)
    {
        _turnNum.text = turn.ToString();
        _diff.text = difficulty;
        for (int i = 0; i < _orderTexts.Count; i++)
        {
            string n = seatNames != null && i < seatNames.Count ? seatNames[i] : "";
            _orderTexts[i].text = i == 0 ? "나" : n;
            bool cur = i == currentSeat;
            _orderChips[i].color = cur ? RetroTheme.Gold : RetroTheme.Inset;
            _orderChips[i].sprite = RetroTheme.PanelSprite(4, cur ? 2 : 0);
            _orderTexts[i].color = cur ? RetroTheme.Ink : RetroTheme.Muted;
        }
        for (int i = 0; i < _pips.Length; i++)
        {
            _pips[i].gameObject.SetActive(i < maxPlays);
            _pips[i].color = i < plays ? RetroTheme.Accent : RetroTheme.PanelColor;
        }
        _playsText.text = $"{plays}/{maxPlays}";
    }

    private static IEnumerator Punch(Transform t, float scale, float dur)
    {
        float e = 0f;
        while (e < dur)
        {
            e += Time.unscaledDeltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(e / dur) * Mathf.PI);
            t.localScale = Vector3.one * Mathf.Lerp(1f, scale, k);
            yield return null;
        }
        t.localScale = Vector3.one;
    }
}
