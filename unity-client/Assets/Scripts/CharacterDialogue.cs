using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 일정 간격으로 서버에서 대사를 받아와 말풍선으로 표시합니다.
/// speechBubble: World Space Canvas 하위 Panel
/// dialogueText: TextMeshPro 컴포넌트
/// </summary>
public class CharacterDialogue : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject      speechBubble;
    [SerializeField] private TextMeshProUGUI dialogueText;

    [Header("타이밍")]
    [SerializeField] private float minInterval     = 6f;
    [SerializeField] private float maxInterval     = 14f;
    [SerializeField] private float displayDuration = 4f;
    [SerializeField] private float startDelay      = 2f;  // 씬 시작 후 첫 대사까지 대기

    [Header("페이드")]
    [SerializeField] private float fadeDuration = 0.3f;

    private CanvasGroup bubbleGroup;

    void Start()
    {
        bubbleGroup = speechBubble.GetComponent<CanvasGroup>();
        if (bubbleGroup == null) bubbleGroup = speechBubble.AddComponent<CanvasGroup>();

        speechBubble.SetActive(false);
        StartCoroutine(DialogueLoop());
    }

    IEnumerator DialogueLoop()
    {
        yield return new WaitForSeconds(startDelay + Random.Range(0f, 3f));

        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));

            string line = null;
            yield return ServerClient.Instance.GetCharacterLine(
                onSuccess: l => line = l,
                onFail:    () => line = ""
            );

            if (!string.IsNullOrEmpty(line))
                yield return ShowDialogue(line);
        }
    }

    IEnumerator ShowDialogue(string line)
    {
        dialogueText.text = line;
        speechBubble.SetActive(true);

        // 페이드 인
        yield return Fade(0f, 1f);

        yield return new WaitForSeconds(displayDuration);

        // 페이드 아웃
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
