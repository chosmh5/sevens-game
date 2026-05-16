using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 모바일 인터랙션 버튼
/// - 인터랙션 가능할 때만 표시
/// - VillagePlayerController와 연동
/// </summary>
public class MobileInteractButton : MonoBehaviour
{
    public static MobileInteractButton Instance { get; private set; }

    [SerializeField] private Button button;
    [SerializeField] private GameObject buttonRoot; // 전체 버튼 오브젝트

    private System.Action onClickAction;

    private void Awake()
    {
        Instance = this;
        button.onClick.AddListener(OnButtonClicked);
        Hide();
    }

    public void Show(System.Action action)
    {
        onClickAction = action;
        buttonRoot.SetActive(true);
    }

    public void Hide()
    {
        onClickAction = null;
        buttonRoot.SetActive(false);
    }

    private void OnButtonClicked()
    {
        onClickAction?.Invoke();
    }
}
