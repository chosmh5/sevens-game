using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// 씬 전환 매니저 (싱글톤)
/// - 페이드 인/아웃 전환
/// - 씬 이름 상수 관리
/// - 로딩 화면 지원
/// </summary>
public class GameSceneManager : MonoBehaviour
{
    public static GameSceneManager Instance { get; private set; }

    // 씬 이름 상수
    public static class Scenes
    {
        public const string Title    = "TitleScreen"; // ← 신규: 게임 시작 화면
        public const string Lobby    = "Lobby";
        public const string MyRoom   = "MyRoom";
        public const string CardGame = "CardGame";
    }

    [Header("Fade")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeDuration = 0.5f;

    private bool isTransitioning;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 시작 시 페이드인
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 1f;
            StartCoroutine(Fade(0f));
        }
    }

    /// <summary>
    /// 씬 전환 (페이드 효과)
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (isTransitioning) return;
        StartCoroutine(TransitionTo(sceneName));
    }

    /// <summary>
    /// 로비로 돌아가기
    /// </summary>
    public void GoToLobby() => LoadScene(Scenes.Lobby);

    /// <summary>
    /// 마이룸으로
    /// </summary>
    public void GoToMyRoom() => LoadScene(Scenes.MyRoom);

    /// <summary>
    /// 카드게임으로
    /// </summary>
    public void GoToCardGame() => LoadScene(Scenes.CardGame);

    private IEnumerator TransitionTo(string sceneName)
    {
        isTransitioning = true;

        // 페이드 아웃 (어두워짐)
        yield return StartCoroutine(Fade(1f));

        // 씬 로드
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone)
        {
            yield return null;
        }

        // 페이드 인 (밝아짐)
        yield return StartCoroutine(Fade(0f));

        isTransitioning = false;
    }

    private IEnumerator Fade(float targetAlpha)
    {
        if (fadeCanvasGroup == null) yield break;

        float startAlpha = fadeCanvasGroup.alpha;
        float elapsed = 0f;

        // 페이드 중 터치 차단
        fadeCanvasGroup.blocksRaycasts = true;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / fadeDuration);
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;

        // 완전히 투명하면 터치 통과
        fadeCanvasGroup.blocksRaycasts = (targetAlpha > 0.01f);
    }
}
