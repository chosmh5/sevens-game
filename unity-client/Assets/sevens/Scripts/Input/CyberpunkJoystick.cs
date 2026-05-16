using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 사이버펑크 스타일 가상 조이스틱
/// - 기존 VirtualJoystick 기능 완전 호환 (InputValue, IsPressed)
/// - 네온 글로우 배경 + 홀로그램 핸들
/// - 코드로 모든 UI를 프로시저럴 생성 (이미지 에셋 불필요)
/// - 조이스틱 조작 시 글로우 반응 애니메이션
///
/// 사용법: 기존 VirtualJoystick 대신 이 컴포넌트를 Canvas 하위에 배치
///         빈 GameObject에 추가하면 자동으로 UI 생성됨
/// </summary>
public class CyberpunkJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static CyberpunkJoystick Instance { get; private set; }

    [Header("Joystick Settings")]
    [SerializeField] private float handleRange = 1f;
    [SerializeField] private float deadZone = 0.1f;

    [Header("Cyberpunk Style")]
    [SerializeField] private float bgSize = 180f;
    [SerializeField] private float handleSize = 70f;
    [SerializeField] private Color baseGlowColor = new Color(0.3f, 0.7f, 1f, 0.4f);
    [SerializeField] private Color activeGlowColor = new Color(0.4f, 1f, 0.95f, 0.6f);
    [SerializeField] private float pulseSpeed = 2f;

    [Header("Position")]
    [SerializeField] private Vector2 screenPosition = new Vector2(140, 140); // 좌하단 기준

    // 생성된 UI 참조
    private RectTransform background;
    private RectTransform handle;
    private Image bgImage;
    private Image bgGlowImage;
    private Image handleImage;
    private Image handleGlowImage;
    private Image ringImage;

    // 입력 상태
    private Canvas canvas;
    private Camera uiCamera;
    private Vector2 inputValue = Vector2.zero;

    public Vector2 InputValue => inputValue;
    public bool IsPressed { get; private set; }

    private void Awake()
    {
        Instance = this;
        canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
            uiCamera = canvas.worldCamera;

        BuildUI();
    }

    // ──────────────────────────────────────────
    // UI 프로시저럴 생성
    // ──────────────────────────────────────────

    private void BuildUI()
    {
        // Root 설정
        RectTransform root = GetComponent<RectTransform>();
        if (root == null) root = gameObject.AddComponent<RectTransform>();
        root.anchorMin = new Vector2(0, 0); // 좌하단
        root.anchorMax = new Vector2(0, 0);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = screenPosition;
        root.sizeDelta = new Vector2(bgSize + 40, bgSize + 40); // 글로우 여유

        // === 외곽 글로우 ===
        GameObject glowObj = CyberpunkUITheme.CreateUIObject("BgGlow", transform);
        RectTransform glowRt = glowObj.GetComponent<RectTransform>();
        glowRt.anchorMin = new Vector2(0.5f, 0.5f);
        glowRt.anchorMax = new Vector2(0.5f, 0.5f);
        glowRt.sizeDelta = new Vector2(bgSize + 40, bgSize + 40);
        bgGlowImage = glowObj.AddComponent<Image>();
        bgGlowImage.sprite = CyberpunkUITheme.CreateGlowCircleSprite(256,
            new Color(baseGlowColor.r, baseGlowColor.g, baseGlowColor.b, 0.15f));
        bgGlowImage.raycastTarget = false;

        // === 배경 원 (반투명 + 테두리) ===
        GameObject bgObj = CyberpunkUITheme.CreateUIObject("Background", transform);
        background = bgObj.GetComponent<RectTransform>();
        background.anchorMin = new Vector2(0.5f, 0.5f);
        background.anchorMax = new Vector2(0.5f, 0.5f);
        background.sizeDelta = new Vector2(bgSize, bgSize);
        bgImage = bgObj.AddComponent<Image>();
        bgImage.sprite = CyberpunkUITheme.CreateCircleSprite(256,
            new Color(0.02f, 0.03f, 0.08f, 0.5f),
            new Color(baseGlowColor.r, baseGlowColor.g, baseGlowColor.b, 0.4f), 3);
        bgImage.raycastTarget = true; // 터치 수신

        // === 십자 가이드 링 (장식) ===
        GameObject ringObj = CyberpunkUITheme.CreateUIObject("Ring", bgObj.transform);
        RectTransform ringRt = ringObj.GetComponent<RectTransform>();
        ringRt.anchorMin = new Vector2(0.5f, 0.5f);
        ringRt.anchorMax = new Vector2(0.5f, 0.5f);
        ringRt.sizeDelta = new Vector2(bgSize * 0.55f, bgSize * 0.55f);
        ringImage = ringObj.AddComponent<Image>();
        ringImage.sprite = CyberpunkUITheme.CreateCircleSprite(128,
            Color.clear,
            new Color(baseGlowColor.r, baseGlowColor.g, baseGlowColor.b, 0.15f), 2);
        ringImage.raycastTarget = false;

        // === 핸들 글로우 ===
        GameObject handleGlowObj = CyberpunkUITheme.CreateUIObject("HandleGlow", bgObj.transform);
        RectTransform hgRt = handleGlowObj.GetComponent<RectTransform>();
        hgRt.anchorMin = new Vector2(0.5f, 0.5f);
        hgRt.anchorMax = new Vector2(0.5f, 0.5f);
        hgRt.sizeDelta = new Vector2(handleSize + 30, handleSize + 30);
        handleGlowImage = handleGlowObj.AddComponent<Image>();
        handleGlowImage.sprite = CyberpunkUITheme.CreateGlowCircleSprite(128,
            new Color(baseGlowColor.r, baseGlowColor.g, baseGlowColor.b, 0.25f));
        handleGlowImage.raycastTarget = false;

        // === 핸들 (조이스틱 썸) ===
        GameObject handleObj = CyberpunkUITheme.CreateUIObject("Handle", bgObj.transform);
        handle = handleObj.GetComponent<RectTransform>();
        handle.anchorMin = new Vector2(0.5f, 0.5f);
        handle.anchorMax = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(handleSize, handleSize);
        handleImage = handleObj.AddComponent<Image>();
        handleImage.sprite = CyberpunkUITheme.CreateCircleSprite(128,
            new Color(0.15f, 0.25f, 0.45f, 0.7f),
            new Color(baseGlowColor.r, baseGlowColor.g, baseGlowColor.b, 0.8f), 3);
        handleImage.raycastTarget = false;
    }

    // ──────────────────────────────────────────
    // 입력 처리 (기존 VirtualJoystick과 동일)
    // ──────────────────────────────────────────

    public void OnPointerDown(PointerEventData eventData)
    {
        IsPressed = true;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background, eventData.position, uiCamera, out localPoint);

        Vector2 bgSize2 = background.sizeDelta;
        Vector2 normalized = new Vector2(
            localPoint.x / (bgSize2.x * 0.5f),
            localPoint.y / (bgSize2.y * 0.5f)
        );

        inputValue = normalized.magnitude > 1f ? normalized.normalized : normalized;

        if (inputValue.magnitude < deadZone)
            inputValue = Vector2.zero;

        // 핸들 + 글로우 위치 업데이트
        Vector2 handlePos = inputValue * (bgSize2.x * 0.5f) * handleRange;
        handle.anchoredPosition = handlePos;

        // 핸들 글로우도 따라감
        Transform hgTransform = handle.parent.Find("HandleGlow");
        if (hgTransform != null)
        {
            RectTransform hgRt = hgTransform as RectTransform;
            if (hgRt != null) hgRt.anchoredPosition = handlePos;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsPressed = false;
        inputValue = Vector2.zero;
        handle.anchoredPosition = Vector2.zero;

        Transform hgTransform = handle.parent.Find("HandleGlow");
        if (hgTransform != null)
        {
            RectTransform hgRt = hgTransform as RectTransform;
            if (hgRt != null) hgRt.anchoredPosition = Vector2.zero;
        }
    }

    // ──────────────────────────────────────────
    // 비주얼 업데이트 (글로우 펄스, 색상 반응)
    // ──────────────────────────────────────────

    private void Update()
    {
        float pulse = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f;

        if (IsPressed)
        {
            // 입력 중: 밝은 시안 글로우 + 세기 연동
            float intensity = Mathf.Clamp01(inputValue.magnitude);
            Color glow = Color.Lerp(baseGlowColor, activeGlowColor, intensity);
            glow.a = 0.2f + intensity * 0.3f;

            if (bgGlowImage != null) bgGlowImage.color = new Color(glow.r, glow.g, glow.b, glow.a + pulse * 0.05f);
            if (handleGlowImage != null) handleGlowImage.color = new Color(glow.r, glow.g, glow.b, 0.3f + intensity * 0.3f);
            if (handleImage != null)
            {
                Color hc = handleImage.color;
                hc.a = 0.8f + intensity * 0.2f;
                handleImage.color = hc;
            }
        }
        else
        {
            // 대기 중: 은은한 펄스
            float idleAlpha = 0.1f + pulse * 0.06f;
            if (bgGlowImage != null) bgGlowImage.color = new Color(baseGlowColor.r, baseGlowColor.g, baseGlowColor.b, idleAlpha);
            if (handleGlowImage != null) handleGlowImage.color = new Color(baseGlowColor.r, baseGlowColor.g, baseGlowColor.b, 0.15f + pulse * 0.05f);
            if (handleImage != null)
            {
                Color hc = handleImage.color;
                hc.a = 0.6f + pulse * 0.1f;
                handleImage.color = hc;
            }
        }
    }
}
