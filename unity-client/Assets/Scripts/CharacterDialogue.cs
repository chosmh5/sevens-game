using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterDialogue : MonoBehaviour
{
    [Header("캐릭터")]
    [SerializeField] public string characterName = "세비-호";

    [Header("말풍선 (비워두면 자동 생성)")]
    [SerializeField] private GameObject      speechBubble;
    [SerializeField] private TextMeshProUGUI dialogueText;

    [Header("타이밍")]
    [SerializeField] private float minInterval     = 6f;
    [SerializeField] private float maxInterval     = 14f;
    [SerializeField] private float displayDuration = 4f;
    [SerializeField] private float startDelay      = 2f;

    [Header("페이드")]
    [SerializeField] private float fadeDuration = 0.3f;

    public bool InConversation { get; set; }

    private CanvasGroup  bubbleGroup;
    private Transform    bubbleCanvas;

    void Start()
    {
        // 말풍선 출력 완전 비활성화 — 모든 대사는 아래 대화 박스(PlayerInteraction)로만 표시한다.
        // 혼잣말 루프(DialogueLoop)와 말풍선 생성을 둘 다 제거. 이 컴포넌트는 이제
        // 캐릭터 이름 / InConversation 상태 / 근접 탐색 용도로만 쓰인다.
        ConversationManager.Instance?.Register(this);
    }

    void LateUpdate()
    {
        if (bubbleCanvas != null && Camera.main != null)
        {
            bubbleCanvas.LookAt(
                bubbleCanvas.position + Camera.main.transform.rotation * Vector3.forward,
                Camera.main.transform.rotation * Vector3.up);
        }
    }

    void CreateSpeechBubble()
    {
        var canvasGO = new GameObject("SpeechBubble");
        canvasGO.transform.SetParent(transform, false);
        canvasGO.transform.localPosition = new Vector3(0, 2.2f, 0);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(200, 60);
        rt.localScale = Vector3.one * 0.012f;

        canvasGO.AddComponent<CanvasScaler>();
        bubbleCanvas = canvasGO.transform;

        // 배경 패널
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        var img = panelGO.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.72f);

        speechBubble = panelGO;

        // 텍스트
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(panelGO.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(8, 4);
        textRT.offsetMax = new Vector2(-8, -4);

        dialogueText = textGO.AddComponent<TextMeshProUGUI>();
        dialogueText.fontSize  = 14;
        dialogueText.alignment = TextAlignmentOptions.Center;
        dialogueText.color     = Color.white;
    }

    IEnumerator DialogueLoop()
    {
        yield return new WaitForSeconds(startDelay + Random.Range(0f, 3f));

        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));

            if (InConversation) continue;

            // 플레이어가 대화/끼어들기/관전 중이면 혼잣말 호출을 멈춘다.
            // → 7.8B를 계속 돌리는 부담을 없애 플레이어 응답 속도를 확보.
            if (PlayerInteraction.Instance != null && PlayerInteraction.Instance.IsInteracting) continue;
            if (ConversationWatchMode.IsWatching) continue;

            string line = null;
            yield return ServerClient.Instance.GetCharacterLine(
                characterName,
                onSuccess: l => line = l,
                onFail:    () => line = ""
            );

            if (!string.IsNullOrEmpty(line) && !InConversation)
                yield return ShowDialogue(line);
        }
    }

    public IEnumerator SayLine(string line)
    {
        // 말풍선 비활성화: 로그에만 남기고 화면 출력은 하지 않는다.
        DialogueLog.Instance?.AddEntry(characterName, line);
        yield break;
    }

    IEnumerator ShowDialogue(string line)
    {
        if (speechBubble == null) yield break;   // 말풍선 비활성화 상태 — 안전 차단
        DialogueLog.Instance?.AddEntry(characterName, line);
        dialogueText.text = line;
        speechBubble.SetActive(true);

        yield return Fade(0f, 1f);
        yield return new WaitForSeconds(displayDuration);
        yield return Fade(1f, 0f);

        speechBubble.SetActive(false);
    }

    IEnumerator Fade(float from, float to)
    {
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            bubbleGroup.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }
        bubbleGroup.alpha = to;
    }
}
