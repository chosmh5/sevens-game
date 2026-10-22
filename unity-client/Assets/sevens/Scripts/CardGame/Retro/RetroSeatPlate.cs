using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌석 명패(상대 3명 + 나). 이름·아바타 칸·남은 장수·패스 수·턴 강조·"곧 끝남" 배지.
/// 가로형(위 좌석·나)과 세로형(왼쪽·오른쪽 좌석) 두 가지.
/// </summary>
public class RetroSeatPlate : MonoBehaviour
{
    public const int ReachCount = 3;   // SevensHighlightDetector 의 '곧 끝남' 기준과 같음

    private bool _vertical, _isPlayer;
    private Image _panel, _turnRing, _header, _avatar;
    private TextMeshProUGUI _name, _initial, _count, _pass, _badge;
    private GameObject _badgeGO;
    private string _lastName;

    public static RetroSeatPlate Create(RectTransform host, bool vertical, bool isPlayer)
    {
        var root = RetroTheme.Node("RetroPlate", host);
        RetroTheme.Stretch(root);
        root.SetAsFirstSibling();
        var p = root.gameObject.AddComponent<RetroSeatPlate>();
        p.Build(vertical, isPlayer);
        return p;
    }

    private void Build(bool vertical, bool isPlayer)
    {
        _vertical = vertical;
        _isPlayer = isPlayer;
        var rt = (RectTransform)transform;

        var ringRT = RetroTheme.Node("TurnRing", rt);
        RetroTheme.Stretch(ringRT, -7f);
        _turnRing = ringRT.gameObject.AddComponent<Image>();
        _turnRing.sprite = RetroTheme.RingSprite(16, 5);
        _turnRing.type = Image.Type.Sliced;
        _turnRing.color = RetroTheme.Gold;
        _turnRing.raycastTarget = false;
        _turnRing.enabled = false;

        var panelRT = RetroTheme.Node("Panel", rt);
        RetroTheme.Stretch(panelRT);
        _panel = RetroTheme.Panel(panelRT, RetroTheme.PanelColor, 12, 3);

        if (vertical) BuildVertical(panelRT);
        else BuildHorizontal(panelRT);
    }

    private void BuildVertical(RectTransform p)
    {
        var headRT = RetroTheme.Node("Header", p);
        headRT.anchorMin = new Vector2(0f, 1f);
        headRT.anchorMax = new Vector2(1f, 1f);
        headRT.pivot = new Vector2(0.5f, 1f);
        headRT.offsetMin = new Vector2(3f, -39f);
        headRT.offsetMax = new Vector2(-3f, -3f);
        _header = RetroTheme.InsetBox(headRT, RetroTheme.Gold, 8);
        _name = RetroTheme.Label(headRT, "Name", "", 22f, RetroTheme.Ink, true);
        RetroTheme.Stretch((RectTransform)_name.transform);

        var avRT = RetroTheme.Place(RetroTheme.Node("Avatar", p), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                    new Vector2(0f, -48f), new Vector2(80f, 80f));
        _avatar = RetroTheme.Panel(avRT, RetroTheme.PanelColor, 8, 3, 0, 0f);
        _initial = RetroTheme.Label(avRT, "Initial", "", 44f, RetroTheme.Text, true);
        RetroTheme.Stretch((RectTransform)_initial.transform);

        var cntRT = RetroTheme.Place(RetroTheme.Node("Count", p), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                     new Vector2(0f, -138f), new Vector2(132f, 56f));
        RetroTheme.InsetBox(cntRT);
        _count = RetroTheme.Label(cntRT, "Value", "", 44f, RetroTheme.Text, true);
        RetroTheme.Stretch((RectTransform)_count.transform);
        _count.richText = true;

        _pass = RetroTheme.Label(p, "Pass", "", 22f, RetroTheme.Muted);
        RetroTheme.Place((RectTransform)_pass.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                         new Vector2(0f, -202f), new Vector2(150f, 28f));

        _badge = RetroTheme.Chip(p, "Reach", "곧 끝남", RetroTheme.Pink, RetroTheme.Ink, 22f);
        _badgeGO = _badge.transform.parent.gameObject;
        RetroTheme.Place((RectTransform)_badgeGO.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                         new Vector2(0f, -126f), Vector2.zero);
    }

    private void BuildHorizontal(RectTransform p)
    {
        float av = _isPlayer ? 66f : 62f;
        var avRT = RetroTheme.Place(RetroTheme.Node("Avatar", p), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                    new Vector2(10f, 0f), new Vector2(av, av));
        _avatar = RetroTheme.Panel(avRT, RetroTheme.Blue, 8, 3, 5, 0f);
        _initial = RetroTheme.Label(avRT, "Initial", "", 33f, RetroTheme.Ink, true);
        RetroTheme.Stretch((RectTransform)_initial.transform);

        _name = RetroTheme.Label(p, "Name", "", 22f, RetroTheme.Text, true, TextAlignmentOptions.TopLeft);
        RetroTheme.Place((RectTransform)_name.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                         new Vector2(av + 22f, -12f), new Vector2(170f, 28f));

        _badge = RetroTheme.Chip(p, "Badge", _isPlayer ? "MY TURN" : "곧 끝남",
                                 _isPlayer ? RetroTheme.Gold : RetroTheme.Pink, RetroTheme.Ink, 22f);
        _badgeGO = _badge.transform.parent.gameObject;
        var brt = (RectTransform)_badgeGO.transform;
        RetroTheme.Place(brt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(av + 22f, 10f), Vector2.zero);

        float cw = _isPlayer ? 84f : 96f;
        var cntRT = RetroTheme.Place(RetroTheme.Node("Count", p), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                                     new Vector2(-10f, 0f), new Vector2(cw, av));
        RetroTheme.InsetBox(cntRT);
        _count = RetroTheme.Label(cntRT, "Value", "", 44f, RetroTheme.Text, true);
        RetroTheme.Stretch((RectTransform)_count.transform);
        _count.richText = true;

        // 가로형: 배지 자리에 패스 수(배지가 없을 때만 보임). 내 명패는 패스 표시 없음.
        _pass = RetroTheme.Label(p, "Pass", "", 22f, RetroTheme.Muted, false, TextAlignmentOptions.BottomLeft);
        RetroTheme.Place((RectTransform)_pass.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                         new Vector2(av + 22f, 12f), new Vector2(150f, 28f));
    }

    /// <summary>표시 갱신.</summary>
    public void Refresh(string name, int count, int passes, bool active, bool myTurn = false)
    {
        if (name != _lastName)
        {
            _lastName = name;
            Color seat = RetroTheme.SeatColor(name);
            _name.text = name;
            _initial.text = RetroTheme.Initial(name);
            if (_vertical)
            {
                _header.color = seat;
                _avatar.color = RetroTheme.Darken(seat, 0.28f) + new Color(0.05f, 0.04f, 0.08f, 0f);
                _initial.color = seat;
            }
            else
            {
                _avatar.color = seat;
                _initial.color = RetroTheme.Ink;
            }
        }

        bool reach = !_isPlayer && count > 0 && count <= ReachCount;
        string countColor = reach ? "#" + ColorUtility.ToHtmlStringRGB(RetroTheme.Pink) : "#" + ColorUtility.ToHtmlStringRGB(RetroTheme.Text);
        _count.text = $"<color={countColor}>{count}</color><size=22><color=#{ColorUtility.ToHtmlStringRGB(RetroTheme.Muted)}>장</color></size>";
        _pass.text = $"패스 {passes}";
        _turnRing.enabled = active;
        bool badge = _isPlayer ? myTurn : reach;
        _badgeGO.SetActive(badge);
        if (!_vertical) _pass.gameObject.SetActive(!_isPlayer && !badge);
    }
}
