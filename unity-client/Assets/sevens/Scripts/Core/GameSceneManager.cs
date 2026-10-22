using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 씬 전환 매니저 (싱글톤)
/// - 하이퍼팝 화면 전환(HyperpopTransition). 셰이더를 못 쓰면 예전 페이드 인/아웃
/// - 씬 이름 상수 관리
/// - 화면 방향: 홈(마이룸)은 세로, 카드게임 등 나머지는 가로. 페이드 아웃 중에 돌린다.
///
/// 흐름: Home(마이룸, 첫 화면) ⇄ CardGame. 로비는 없앴고 GoToLobby/GoToMyRoom 은 홈으로 간다.
/// </summary>
public class GameSceneManager : MonoBehaviour
{
    public static GameSceneManager Instance { get; private set; }

    // 씬 이름 상수
    public static class Scenes
    {
        public const string Home     = "Home";        // 앱 첫 화면 = 마이룸(세비-호와 대화)
        public const string Title    = "TitleScreen"; // (사용 안 함 — 예전 시작 화면)
        public const string Lobby    = "Lobby";       // (사용 안 함 — 홈으로 대체)
        public const string MyRoom   = "MyRoom";      // (예전 3D 도시 마이룸)
        public const string CardGame = "CardGame";
    }

    [Header("Fade")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeDuration = 0.5f;

    private bool isTransitioning;
    private HyperpopTransition _fx;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 씬에 페이드 캔버스가 연결돼 있지 않으면(코드로 생성된 경우) 직접 만든다.
        if (fadeCanvasGroup == null) fadeCanvasGroup = CreateFadeCanvas();

        _fx = HyperpopTransition.Create(transform);

        // 시작: 덮인 화면에서 걷히며 들어온다(하이퍼팝이 안 되면 페이드인)
        if (_fx != null)
        {
            if (fadeCanvasGroup != null) { fadeCanvasGroup.alpha = 0f; fadeCanvasGroup.blocksRaycasts = false; }
            _fx.CoverNow(LabelFor(SceneManager.GetActiveScene().name));
            StartCoroutine(_fx.PlayIn());
        }
        else if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 1f;
            StartCoroutine(Fade(0f));
        }
    }

    /// <summary>전환 덮개에 잠깐 뜨는 목적지 단어.</summary>
    public static string LabelFor(string sceneName)
    {
        switch (sceneName)
        {
            case Scenes.Home:     return "MY ROOM";
            case Scenes.CardGame: return "LET'S PLAY";
            default:              return "";
        }
    }

    /// <summary>
    /// 씬 전환 (페이드 효과)
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (isTransitioning) return;
        // 로비는 없앴다 → 홈으로
        if (sceneName == Scenes.Lobby) sceneName = Scenes.Home;
        StartCoroutine(TransitionTo(sceneName));
    }

    /// <summary>홈(마이룸)으로</summary>
    public void GoToHome() => LoadScene(Scenes.Home);

    /// <summary>예전 '로비로' 버튼들 — 이제 홈으로 간다.</summary>
    public void GoToLobby() => GoToHome();

    /// <summary>마이룸 = 홈</summary>
    public void GoToMyRoom() => GoToHome();

    /// <summary>
    /// 카드게임으로
    /// </summary>
    public void GoToCardGame() => LoadScene(Scenes.CardGame);

    /// <summary>씬별 화면 방향. 홈만 세로.</summary>
    public static ScreenOrientation OrientationFor(string sceneName) =>
        sceneName == Scenes.Home ? ScreenOrientation.Portrait : ScreenOrientation.LandscapeLeft;

    private IEnumerator TransitionTo(string sceneName)
    {
        isTransitioning = true;
        string from = SceneManager.GetActiveScene().name;

        // 지금 화면 → 덮개
        if (_fx != null) yield return StartCoroutine(_fx.PlayOut(LabelFor(sceneName)));
        else yield return StartCoroutine(Fade(1f));

        // 화면이 가려진 동안 방향 전환
        yield return StartCoroutine(RotateFor(sceneName));

        if (sceneName == Scenes.Home && from == Scenes.CardGame)
            HomeSession.ReturnedFromGame = true;

        // 씬 로드
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone)
        {
            yield return null;
        }

        // 덮개 → 새 화면
        if (_fx != null) yield return StartCoroutine(_fx.PlayIn());
        else yield return StartCoroutine(Fade(0f));

        isTransitioning = false;
    }

    private static IEnumerator RotateFor(string sceneName)
    {
        var want = OrientationFor(sceneName);
        if (Screen.orientation == want) yield break;
        Screen.orientation = want;
        if (!Application.isMobilePlatform) yield break;   // 에디터/PC 는 화면이 안 돈다

        // 기기가 실제로 돌 때까지(최대 1.5초) 기다린다 → 새 씬 UI가 바뀐 해상도로 잡힘
        bool portrait = want == ScreenOrientation.Portrait;
        float t = 0f;
        while (t < 1.5f && (Screen.height > Screen.width) != portrait)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return null;
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

    private CanvasGroup CreateFadeCanvas()
    {
        var go = new GameObject("FadeCanvas");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        go.AddComponent<GraphicRaycaster>();
        var cg = go.AddComponent<CanvasGroup>();

        var img = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(go.transform, false);
        img.color = RetroTheme.Ink;   // 카드게임과 같은 잉크색
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return cg;
    }
}
