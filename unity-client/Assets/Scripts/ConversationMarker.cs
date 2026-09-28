using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// CharacterDialogue와 같은 오브젝트에 붙여두면 자동으로 동작.
/// InConversation == true일 때 머리 위에 "!" 아이콘을 띄운다.
/// 카메라를 향해 항상 빌보드 처리되고, 위아래로 둥실둥실 움직인다.
/// </summary>
[RequireComponent(typeof(CharacterDialogue))]
public class ConversationMarker : MonoBehaviour
{
    [SerializeField] private float bobSpeed     = 2.2f;
    [SerializeField] private float bobAmount    = 0.12f;
    [SerializeField] private float heightOffset = 2.8f;   // 캐릭터 머리 위 높이

    private CharacterDialogue _dialogue;
    private GameObject        _markerRoot;
    private Transform         _canvasTransform;
    private CanvasGroup       _group;
    private float             _bobTime;

    // 페이드
    private float _targetAlpha;
    private const float FadeSpeed = 4f;

    void Start()
    {
        _dialogue = GetComponent<CharacterDialogue>();
        BuildMarker();
    }

    void Update()
    {
        if (_markerRoot == null) return;

        // 표시 여부: ConversationManager가 대화 중이고 이 캐릭터가 참여 중일 때
        bool shouldShow = _dialogue.InConversation;
        _targetAlpha = shouldShow ? 1f : 0f;

        // 페이드
        _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, FadeSpeed * Time.deltaTime);
        _markerRoot.SetActive(_group.alpha > 0.01f);

        if (!shouldShow) return;

        // 둥실 애니메이션
        _bobTime += Time.deltaTime * bobSpeed;
        Vector3 base3 = transform.position + Vector3.up * heightOffset;
        _canvasTransform.position = base3 + Vector3.up * Mathf.Sin(_bobTime) * bobAmount;

        // 카메라 빌보드
        if (Camera.main != null)
            _canvasTransform.LookAt(
                _canvasTransform.position + Camera.main.transform.rotation * Vector3.forward,
                Camera.main.transform.rotation * Vector3.up);
    }

    void BuildMarker()
    {
        // 월드 스페이스 캔버스
        _markerRoot = new GameObject("ConversationMarker");
        _markerRoot.transform.SetParent(null);   // 캐릭터 이동에 따라 Update에서 직접 위치 조정

        var canvas = _markerRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = _markerRoot.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(60f, 70f);
        rt.localScale = Vector3.one * 0.012f;

        _markerRoot.AddComponent<CanvasScaler>();
        _canvasTransform = _markerRoot.transform;

        _group = _markerRoot.AddComponent<CanvasGroup>();

        // 원형 배경 (주황색)
        var bg   = new GameObject("Bg");
        bg.transform.SetParent(_markerRoot.transform, false);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(1f, 0.6f, 0.1f, 0.92f);
        var bgRT        = bg.GetComponent<RectTransform>();
        bgRT.sizeDelta  = new Vector2(44f, 52f);
        bgRT.anchoredPosition = Vector2.zero;

        // "!" 텍스트
        var textGO = new GameObject("ExclamationText");
        textGO.transform.SetParent(_markerRoot.transform, false);
        var tmp         = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text        = "!";
        tmp.fontSize    = 42f;
        tmp.fontStyle   = FontStyles.Bold;
        tmp.color       = Color.white;
        tmp.alignment   = TextAlignmentOptions.Center;
        var textRT      = textGO.GetComponent<RectTransform>();
        textRT.sizeDelta     = new Vector2(60f, 60f);
        textRT.anchoredPosition = new Vector2(0f, 4f);

        _markerRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (_markerRoot != null)
            Destroy(_markerRoot);
    }
}
