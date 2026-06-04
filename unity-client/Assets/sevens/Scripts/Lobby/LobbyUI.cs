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

    [Header("AI Difficulty Popup")]
    [Tooltip("'게임하기'를 누르면 표시되는 난이도 선택 팝업. 비어 있으면 바로 게임 진입.")]
    [SerializeField] private GameObject difficultyPanel;
    [Tooltip("팝업 안의 '시작' 버튼 → 선택한 난이도로 카드게임 진입.")]
    [SerializeField] private Button startGameButton;
    [Tooltip("팝업 닫기/취소 버튼(선택).")]
    [SerializeField] private Button difficultyCancelButton;
    [Tooltip("Easy / Normal / Hard 순서로 3개 버튼을 연결.")]
    [SerializeField] private Button[] difficultyButtons; // [0]=Easy [1]=Normal [2]=Hard
    [Tooltip("선택된 난이도 버튼 강조용(선택). difficultyButtons와 같은 순서.")]
    [SerializeField] private Image[] difficultyHighlights;
    [SerializeField] private Color difficultyNormalColor   = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color difficultySelectedColor = new Color(0.95f, 0.75f, 0.20f, 0.95f);

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

        SetupDifficultyButtons();

        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        startGameButton?.onClick.AddListener(OnConfirmStart);
        difficultyCancelButton?.onClick.AddListener(CloseDifficultyPanel);
    }

    // ── AI 난이도 선택 ──────────────────────────────────────────────

    private void SetupDifficultyButtons()
    {
        if (difficultyButtons == null) return;
        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            int idx = i; // 클로저 캡처 방지
            difficultyButtons[i]?.onClick.AddListener(() => SetDifficulty(idx));
        }
        RefreshDifficultyHighlight();
    }

    private void OpenDifficultyPanel()
    {
        RefreshDifficultyHighlight();
        difficultyPanel.SetActive(true);
    }

    private void CloseDifficultyPanel()
    {
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
    }

    /// <summary>팝업의 '시작' 버튼 → 선택된 난이도로 카드게임 진입.</summary>
    private void OnConfirmStart()
    {
        GameSceneManager.Instance?.GoToCardGame();
    }

    /// <summary>난이도 선택(0=Easy, 1=Normal, 2=Hard). UI 버튼 OnClick에서 직접 연결해도 됨.</summary>
    public void SetDifficulty(int difficulty)
    {
        AiDifficultySettings.Selected = (AiDifficulty)difficulty;
        RefreshDifficultyHighlight();
    }

    private void RefreshDifficultyHighlight()
    {
        if (difficultyHighlights == null) return;
        int selected = (int)AiDifficultySettings.Selected;
        for (int i = 0; i < difficultyHighlights.Length; i++)
        {
            if (difficultyHighlights[i] == null) continue;
            difficultyHighlights[i].color = (i == selected)
                ? difficultySelectedColor
                : difficultyNormalColor;
        }
    }

    private void Update()
    {
        HandleCharacterRotation();
    }

    private void OnGameStart()
    {
        // 난이도 팝업이 있으면 먼저 띄우고, 없으면 바로 게임 진입(하위 호환).
        if (difficultyPanel != null)
            OpenDifficultyPanel();
        else
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
