using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상대 프로필 박스 UI.
/// - 이름과 남은 카드 수 표시
/// - 현재 턴일 때 배경색 강조
///
/// GameManager가 매 턴 시작 시 SetTurnActive(true)를 호출하고,
/// 카드 수가 변할 때 SetCardCount(n)을 호출한다.
/// </summary>
public class OpponentProfile : MonoBehaviour
{
    [Header("UI Refs")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text cardCountText;
    [SerializeField] private Image    backgroundImage;

    [Header("Colors")]
    [SerializeField] private Color normalColor = new Color(0.22f, 0.24f, 0.30f, 0f);
    [SerializeField] private Color activeColor = new Color(0.95f, 0.75f, 0.20f, 0.95f);

    private Player _player;
    public Player Player => _player;

    /// <summary>이 프로필이 표시할 Player 데이터를 바인딩.</summary>
    public void Bind(Player player)
    {
        _player = player;
        if (nameText != null) nameText.text = player != null ? player.Name : "";
        SetCardCount(player != null ? player.Hand.Count : 0);
        SetTurnActive(false);
    }

    /// <summary>남은 카드 수 갱신.</summary>
    public void SetCardCount(int count)
    {
        if (cardCountText != null) cardCountText.text = $"{count}장";
    }

    /// <summary>이 플레이어가 현재 턴인지 시각 표시.</summary>
    public void SetTurnActive(bool active)
    {
        if (backgroundImage != null)
            backgroundImage.color = active ? activeColor : normalColor;
    }
}
