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

    [Header("AI Difficulty")]
    [Tooltip("Easy / Normal / Hard 순서로 3개 버튼을 연결. 비워도 동작(코드 API로 설정 가능).")]
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
        GameSceneManager.Instance?.GoToCardGame();
    }

    private void OnMyRoom()
    {
        GameSceneManager.Instance?.GoToMyRoom();
    }

    private void OnSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(!settingsPanel.