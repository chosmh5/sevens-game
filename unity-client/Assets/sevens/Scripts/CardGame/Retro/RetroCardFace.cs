using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 레트로 카드 앞면(스프라이트 없이 UI로 그림): 크림색 바탕 + 검은 외곽선 + 4색 문양.
/// 손패 카드(CardUI)와 필드 칸(RetroFieldView) 양쪽에서 쓴다.
/// </summary>
public class RetroCardFace : MonoBehaviour
{
    public enum Mode { Face, FaceSeven, TargetSelected, TargetMine, TargetOpen, Empty }

    private RectTransform _rt;
    private Image _ring, _bg, _dim;
    private TextMeshProUGUI _rank, _suitSmall, _suitBig, _center, _arrow;
    private bool _isHand;
    private Card _card;
    private Mode _mode;

    public Card Card => _card;

    /// <summary>parent 를 꽉 채우는 카드 앞면을 만든다.</summary>
    public static RetroCardFace Create(RectTransform parent, bool isHand)
    {
        var root = RetroTheme.Node("RetroFace", parent);
        RetroTheme.Stretch(root);
        var f = root.gameObject.AddComponent<RetroCardFace>();
        f.Build(isHand);
        return f;
    }

    private void Build(bool isHand)
    {
        _isHand = isHand;
        _rt = (RectTransform)transform;
        float h = isHand ? 144f : 70f;

        // 강조 링(카드 바깥으로 살짝 나옴)
        // 손패: 카드 바깥 링(바탕 뒤). 필드: 칸 안쪽 테두리(바탕 위) — 아래에서 순서 조정.
        var ringRT = RetroTheme.Node("Ring", _rt);
        RetroTheme.Stretch(ringRT, isHand ? -7f : 0f);
        _ring = ringRT.gameObject.AddComponent<Image>();
        _ring.sprite = RetroTheme.RingSprite(isHand ? 14 : 6, isHand ? 5 : 3);
        _ring.type = Image.Type.Sliced;
        _ring.raycastTarget = false;
        _ring.enabled = false;

        var bgRT = RetroTheme.Node("Bg", _rt);
        RetroTheme.Stretch(bgRT);
        _bg = bgRT.gameObject.AddComponent<Image>();
        _bg.sprite = RetroTheme.PanelSprite(isHand ? 10 : 6, isHand ? 3 : 2);
        _bg.type = Image.Type.Sliced;
        _bg.color = RetroTheme.CardFace;
        _bg.raycastTarget = isHand;   // 손패는 클릭 받기(부모 CardUI 로 전달)
        var sh = bgRT.gameObject.AddComponent<Shadow>();
        sh.effectColor = RetroTheme.WithAlpha(RetroTheme.Ink, 0.55f);
        sh.effectDistance = new Vector2(0f, isHand ? -6f : -4f);
        if (!isHand) ringRT.SetAsLastSibling();

        // 왼쪽 위 숫자 + 작은 문양
        _rank = RetroTheme.Label(_rt, "Rank", "", isHand ? 33f : 22f, Color.black, true, TextAlignmentOptions.Top);
        RetroTheme.Place((RectTransform)_rank.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                         isHand ? new Vector2(6f, -6f) : new Vector2(3f, -3f),
                         isHand ? new Vector2(40f, 36f) : new Vector2(30f, 24f));
        _suitSmall = RetroTheme.Label(_rt, "SuitSmall", "", 22f, Color.black, false, TextAlignmentOptions.Top);
        RetroTheme.Place((RectTransform)_suitSmall.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                         new Vector2(6f, -42f), new Vector2(40f, 26f));
        _suitSmall.gameObject.SetActive(isHand);

        // 오른쪽 아래 큰 문양
        _suitBig = RetroTheme.Label(_rt, "SuitBig", "", isHand ? 55f : 26f, Color.black, false, TextAlignmentOptions.BottomRight);
        RetroTheme.Place((RectTransform)_suitBig.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                         isHand ? new Vector2(-8f, 10f) : new Vector2(-3f, 2f),
                         isHand ? new Vector2(70f, 64f) : new Vector2(34f, 32f));

        // 빈칸·목표칸용 가운데 글자 + ▼
        _center = RetroTheme.Label(_rt, "Center", "", 22f, RetroTheme.Muted, true);
        RetroTheme.Stretch((RectTransform)_center.transform);
        _arrow = RetroTheme.Label(_rt, "Arrow", "▼", 22f, RetroTheme.Ink, true, TextAlignmentOptions.Top);
        RetroTheme.Place((RectTransform)_arrow.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                         new Vector2(0f, -4f), new Vector2(40f, 24f));

        // 낼 수 없는 카드 어둡게
        var dimRT = RetroTheme.Node("Dim", _rt);
        RetroTheme.Stretch(dimRT);
        _dim = dimRT.gameObject.AddComponent<Image>();
        _dim.sprite = RetroTheme.PanelSprite(isHand ? 10 : 6, 0);
        _dim.type = Image.Type.Sliced;
        _dim.color = new Color(0.05f, 0.04f, 0.1f, 0.5f);
        _dim.raycastTarget = false;
        _dim.enabled = false;

        SetMode(Mode.Empty, null, 1);
    }

    /// <summary>손패용: 카드 지정.</summary>
    public void SetCard(Card card) => SetMode(Mode.Face, card, card != null ? card.Number : 1);

    /// <summary>
    /// 표시 모드. number 는 빈칸/목표칸에서 보여줄 숫자(필드 열 번호).
    /// </summary>
    public void SetMode(Mode mode, Card card, int number)
    {
        _mode = mode;
        _card = card;
        bool face = mode == Mode.Face || mode == Mode.FaceSeven;

        _rank.gameObject.SetActive(face);
        _suitBig.gameObject.SetActive(face);
        _suitSmall.gameObject.SetActive(face && _isHand);
        _center.gameObject.SetActive(!face);
        _arrow.gameObject.SetActive(mode == Mode.TargetSelected);
        _bg.enabled = true;

        if (face && card != null)
        {
            Color ink = RetroTheme.SuitInk(card.Suit);
            string sym = card.Suit.ToSymbol();
            _rank.text = RetroTheme.RankLabel(card.Number);
            _suitSmall.text = sym;
            _suitBig.text = sym;
            _rank.color = _suitSmall.color = _suitBig.color = ink;
            _bg.sprite = RetroTheme.PanelSprite(_isHand ? 10 : 6, _isHand ? 3 : 2);
            _bg.color = RetroTheme.CardFace;
            SetRing(mode == Mode.FaceSeven, RetroTheme.Gold);
            return;
        }

        string rk = RetroTheme.RankLabel(number);
        string symText = card != null ? card.Suit.ToSymbol() : "";
        SetRing(false, Color.clear);
        switch (mode)
        {
            case Mode.TargetSelected:
                _bg.sprite = RetroTheme.PanelSprite(6, 2);
                _bg.color = RetroTheme.Accent;
                _center.text = rk + symText;
                _center.color = RetroTheme.Ink;
                ((RectTransform)_center.transform).anchoredPosition = new Vector2(0f, -8f);
                break;
            case Mode.TargetMine:
                _bg.sprite = RetroTheme.PanelSprite(6, 0);
                _bg.color = RetroTheme.WithAlpha(RetroTheme.Accent, 0.14f);
                SetRing(true, RetroTheme.Accent);
                _center.text = rk + symText;
                _center.color = RetroTheme.Accent;
                ((RectTransform)_center.transform).anchoredPosition = Vector2.zero;
                break;
            case Mode.TargetOpen:
                _bg.sprite = RetroTheme.PanelSprite(6, 0);
                _bg.color = new Color(0f, 0f, 0f, 0f);
                SetRing(true, RetroTheme.Faint);
                _center.text = rk;
                _center.color = RetroTheme.Muted;
                ((RectTransform)_center.transform).anchoredPosition = Vector2.zero;
                break;
            default: // Empty
                _bg.sprite = RetroTheme.PanelSprite(6, 0);
                _bg.color = new Color(0.05f, 0.04f, 0.1f, 0.45f);
                _center.text = rk;
                _center.color = RetroTheme.Hex("4A5E66");
                ((RectTransform)_center.transform).anchoredPosition = Vector2.zero;
                break;
        }
    }

    /// <summary>손패 상태: 낼 수 있으면 하늘색 링, 선택이면 굵은 링, 아니면 어둡게.</summary>
    public void SetHandState(bool playable, bool selected)
    {
        _dim.enabled = !playable;
        if (selected)
        {
            _ring.sprite = RetroTheme.RingSprite(14, 7);
            RetroTheme.Stretch((RectTransform)_ring.transform, -9f);
            SetRing(true, RetroTheme.Accent);
        }
        else
        {
            _ring.sprite = RetroTheme.RingSprite(14, 5);
            RetroTheme.Stretch((RectTransform)_ring.transform, -7f);
            SetRing(playable, RetroTheme.Accent);
        }
    }

    private void SetRing(bool on, Color c)
    {
        _ring.enabled = on;
        _ring.color = c;
    }
}
