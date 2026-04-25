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
        if (speechBubble == null)
            CreateSpeechBubble();

        bubbleGroup = speechBubble.GetComponent<CanvasGroup>();
        if (bubbleGroup == null) bubbleGroup = speechBubble.AddComponent<CanvasGroup>();

        speechBubble.SetActive(false);

        ConversationManager.Instance?.Register(this);
        StartCoroutine(DialogueLoop());
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
        yield return ShowDialogue(line);
    }

    IEnumerator ShowDialogue(string line)
    {
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
