using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

/// <summary>
/// 타이틀(시작) 화면 UI
/// - 화면 중앙: 주인공 캐릭터 (3D or 이미지)
/// - 우측 패널: 마이 월드 / 세븐즈 게임 버튼
/// - Reverse:1999 스타일 연출
/// </summary>
public class TitleScreenUI : MonoBehaviour
{
    // ── 우측 버튼 패널 ──────────────────────────────────────
    [Header("Right Panel Buttons")]
    [SerializeField] private Button myWorldButton;
    [SerializeField] private Button sevensGameButton;

    // ── AI 난이도 팝업 ──────────────────────────────────────
    [Header("AI Difficulty Popup")]
    [Tooltip("'세븐즈 게임'을 누르면 표시되는 난이도 선택 팝업. 비어 있으면 바로 게임 진입.")]
    [SerializeField] private GameObject difficultyPanel;
    [Tooltip("팝업 안의 '시작' 버튼 → 선택한 난이도로 카드게임 진입.")]
    [SerializeField] private Button startGameButton;
    [Tooltip("팝업 닫기/취소 버튼(선택).")]
    [SerializeField] private Button difficultyCancelButton;
    [Tooltip("Easy / Normal / Hard 순서로 3개 버튼을 연결.")]
    [SerializeField] private Button[] difficultyButtons; // [0]=Easy [1]=Normal [2]=Hard
    [Tooltip("선택된 난이도 버튼 강조용. difficultyButtons와 같은 순서.")]
    [SerializeField] private Image[] difficultyHighlights;
    [SerializeField] private Color difficultyNormalColor   = new Color(1f, 1f, 1f, 0.20f);
    [SerializeField] private Color difficultySelectedColor = new Color(0.95f, 0.75f, 0.20f, 0.95f);

    // ── 연출 / 애니메이션 ──────────────────────────────────
    [Header("Intro Animation")]
    [SerializeField] private CanvasGroup rightPanelGroup;   // 우측 패널 페이드용
    [SerializeField] private RectTransform rightPanelRect;  // 슬라이드 인용
    [SerializeField] private float slideInDuration = 0.6f;
    [SerializeField] private float slideOffsetX    = 300f;  // 오른쪽에서 밀려옴

    // ── 배경 파티클 / 빛줄기 (선택) ──────────────────────────
    [Header("Optional FX")]
    [SerializeField] private ParticleSystem bgParticle;
    [SerializeField] private CanvasGroup   bgOverlayGroup; // 그라디언트 오버레이
    [SerializeField] private float         bgFadeDuration = 1.2f;

    // ── 캐릭터 설정 ──────────────────────────────────────────
    [Header("Character")]
    [Tooltip("씬의 CharacterRoot 오브젝트. 비워두면 이름으로 자동 검색.")]
    [SerializeField] private Transform characterRoot;

    // ── 내부 상태 ────────────────────────────────────────────
    private bool _isTransitioning;

    // ═══════════════════════════════════════════════════════
    private void Start()
    {
        // 버튼 이벤트 등록
        myWorldButton?  .onClick.AddListener(OnMyWorldClicked);
        sevensGameButton?.onClick.AddListener(OnSevensGameClicked);

        // 난이도 팝업 초기화
        SetupDifficultyPopup();

        // 텍스트 줄바꿈 방지 (TMP 기본값이 wordWrapping=true라 좁은 화면에서 깨짐)
        FixTextWrapping();

        // 캐릭터 앞면 보정:
        // VRM 정면이 +Z이고 카메라가 -Z 쪽(-4)에서 찍으므로 Y=180° 회전 필요.
        FixCharacterFacing();

        // 시작 시 우측 패널 숨기고 슬라이드 인 연출
        StartCoroutine(PlayIntro());

        // 배경 파티클 재생
        bgParticle?.Play();
    }

    /// <summary>
    /// TMP 텍스트의 자동 줄바꿈을 비활성화해 한 줄로 표시.
    /// </summary>
    private void FixTextWrapping()
    {
        // Canvas 하위의 모든 TMP 텍스트를 순회하며 wordWrapping 끄기
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        foreach (var tmp in canvas.GetComponentsInChildren<TMP_Text>(true))
        {
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
        }
    }

    /// <summary>
    /// 캐릭터가 카메라를 정면으로 바라보도록 Y=180° 보정.
    /// TitleScreenSetup으로 새로 씬을 만들면 자동 적용되지만
    /// 기존 씬에서도 런타임에 보정됩니다.
    /// </summary>
    private void FixCharacterFacing()
    {
        if (characterRoot == null)
            characterRoot = GameObject.Find("CharacterRoot")?.transform;

        if (characterRoot != null)
        {
            // 이미 180° 이면 중복 적용 방지
            float currentY = characterRoot.eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(currentY, 180f)) > 5f)
                characterRoot.rotation = Quaternion.Euler(0f, 180f, 0f);
        }
    }

    // ── 인트로 연출 ──────────────────────────────────────────
    private IEnumerator PlayIntro()
    {
        // 배경 오버레이 페이드 인
        if (bgOverlayGroup != null)
        {
            bgOverlayGroup.alpha = 0f;
            float t = 0f;
            while (t < bgFadeDuration)
            {
                t += Time.deltaTime;
                bgOverlayGroup.alpha = Mathf.Clamp01(t / bgFadeDuration);
                yield return null;
            }
        }

        // 우측 패널 슬라이드 인 + 페이드 인
        if (rightPanelRect != null && rightPanelGroup != null)
        {
            Vector2 startPos = rightPanelRect.anchoredPosition + Vector2.right * slideOffsetX;
            Vector2 endPos   = rightPanelRect.anchoredPosition;
            rightPanelRect.anchoredPosition = startPos;
            rightPanelGroup.alpha = 0f;

            float t = 0f;
            while (t < slideInDuration)
            {
                t += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, t / slideInDuration);
                rightPanelRect.anchoredPosition = Vector2.Lerp(startPos, endPos, progress);
                rightPanelGroup.alpha = progress;
                yield return null;
            }

            rightPanelRect.anchoredPosition = endPos;
            rightPanelGroup.alpha = 1f;
        }
    }

    // ── 버튼 핸들러 ──────────────────────────────────────────
    private void OnMyWorldClicked()
    {
        if (_isTransitioning) return;
        // 마이 월드 = 도시(MyRoom) 씬으로 직접 이동 — Lobby를 거치지 않음
        LoadScene(GameSceneManager.Scenes.MyRoom);
    }

    private void OnSevensGameClicked()
    {
        if (_isTransitioning) return;

        // 난이도 팝업이 있으면 먼저 띄우고, 없으면 바로 카드게임 진입(하위 호환).
        if (difficultyPanel != null)
            OpenDifficultyPanel();
        else
            LoadScene(GameSceneManager.Scenes.CardGame);
    }

    // ── AI 난이도 팝업 ────────────────────────────────────────
    private void SetupDifficultyPopup()
    {
        if (difficultyButtons != null)
        {
            for (int i = 0; i < difficultyButtons.Length; i++)
            {
                int idx = i; // 클로저 캡처 방지
                difficultyButtons[i]?.onClick.AddListener(() => SetDifficulty(idx));
            }
        }
        startGameButton?.onClick.AddListener(OnConfirmStart);
        difficultyCancelButton?.onClick.AddListener(CloseDifficultyPanel);

        if (difficultyPanel != null) difficultyPanel.SetActive(false);
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

    /// <summary>난이도 선택(0=Easy, 1=Normal, 2=Hard).</summary>
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

    /// <summary>팝업의 '시작' 버튼 → 선택된 난이도로 카드게임 진입.</summary>
    private void OnConfirmStart()
    {
        if (_isTransitioning) return;
        LoadScene(GameSceneManager.Scenes.CardGame);
    }

    private void LoadScene(string sceneName)
    {
        if (GameSceneManager.Instance != null)
        {
            GameSceneManager.Instance.LoadScene(sceneName);
        }
        else
        {
            // 폴백: GameSceneManager 없을 때 직접 로드
            StartCoroutine(FallbackLoad(sceneName));
        }
    }

    private IEnumerator FallbackLoad(string sceneName)
    {
        _isTransitioning = true;
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;
    }
}
