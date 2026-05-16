using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 마이룸/카드게임 등 서브 씬에서 로비로 돌아가는 버튼
/// - 왼쪽 상단에 뒤로가기 버튼
/// </summary>
public class BackToLobbyButton : MonoBehaviour
{
    [SerializeField] private Button backButton;

    private void Start()
    {
        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBack);
        }
    }

    private void OnBack()
    {
        GameSceneManager.Instance?.GoToLobby();
    }
}
