using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 UI 컨트롤러
/// - 게임하기 / 마이룸 / 설정 버튼
/// - 3D 캐릭터 회전 터치
/// - 상단 유저 정보
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button gameStartButton;
    [SerializeField] private Button myRoomButton;
    [SerializeField] private Button settingsButton;

    [Header("Settings Panel")]
    [SerializeField] private GameObject settingsPanel;

    [Header("3D Character")]
    [SerializeField] private Transform lobbyCharacter; // 로비에 서 있는 3D 캐릭터
    [SerializeField] private float dragRotateSpeed = 0.3f;

    private bool isDraggingCharacter;
    private float lastTouchX;

    private void Start()
    {
        // 버튼 연결
        gameStartButton?.onClick.AddListener(OnGameStart);
        myRoomButton?.onClick.AddListener(OnMyRoom);
        settingsButton?.onClick.AddListener(OnSettings);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    private void Update()
    {
        HandleCharacterRotation();
    }

    private void OnGameStart()
    {
        GameSceneManager.Instance?.GoToCardGame();
    }

    private void OnMyRoom()
    {
        GameSceneManager.Instance?.GoToMyRoom();
    }

    private void OnSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    /// <summary>
    /// 화면 중앙(캐릭터 영역)을 드래그하면 캐릭터 회전
    /// </summary>
    private void HandleCharacterRotation()
    {
        if (lobbyCharacter == null) return;

#if UNITY_EDITOR
        // 에디터: 마우스
        if (Input.GetMouseButtonDown(0) && IsInCharacterArea(Input.mousePosition))
        {
            isDraggingCharacter = true;
            lastTouchX = Input.mousePosition.x;
        }
        if (Input.GetMouseButtonUp(0))
            isDraggingCharacter = false;

        if (isDraggingCharacter)
        {
            float delta = Input.mousePosition.x - lastTouchX;
            lobbyCharacter.Rotate(0, -delta * dragRotateSpeed, 0);
            lastTouchX = Input.mousePosition.x;
        }
#else
        // 모바일: 터치
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began && IsInCharacterArea(touch.position))
            {
                isDraggingCharacter = true;
                lastTouchX = touch.position.x;
            }
            else if (touch.phase == TouchPhase.Moved && isDraggingCharacter)
            {
                float delta = touch.position.x - lastTouchX;
                lobbyCharacter.Rotate(0, -delta * dragRotateSpeed, 0);
                lastTouchX = touch.position.x;
            }
            else if (touch.phase == TouchPhase.Ended)
            {
                isDraggingCharacter = false;
            }
        }
#endif
    }

    /// <summary>
    /// 화면 중앙 영역인지 판단 (좌우 UI 영역 제외)
    /// </summary>
    private bool IsInCharacterArea(Vector2 screenPos)
    {
        float screenWidth = Screen.width;
        // 화면의 중앙 60% 영역
        return screenPos.x > screenWidth * 0.2f && screenPos.x < screenWidth * 0.8f;
    }
}
