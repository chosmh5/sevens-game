using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 모바일 가상 조이스틱
/// - 화면 왼쪽 터치로 작동
/// - 드래그 거리에 비례한 입력값
/// - 놓으면 자동 복귀
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static VirtualJoystick Instance { get; private set; }

    [Header("References")]
    [SerializeField] private RectTransform background;  // 조이스틱 배경
    [SerializeField] private RectTransform handle;       // 조이스틱 핸들(썸)

    [Header("Settings")]
    [SerializeField] private float handleRange = 1f;     // 핸들 이동 범위 (배경 반지름 대비)
    [SerializeField] private float deadZone = 0.1f;      // 데드존

    private Canvas canvas;
    private Camera uiCamera;
    private Vector2 inputValue = Vector2.zero;

    /// <summary>조이스틱 입력값 (-1 ~ 1)</summary>
    public Vector2 InputValue => inputValue;

    /// <summary>조이스틱이 눌려있는지</summary>
    public bool IsPressed { get; private set; }

    private void Awake()
    {
        Instance = this;
        canvas = GetComponentInParent<Canvas>();

        if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
            uiCamera = canvas.worldCamera;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsPressed = true;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        // 터치 위치를 배경 기준 로컬 좌표로 변환
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background, eventData.position, uiCamera, out localPoint);

        // 배경 크기 기준 정규화 (-1 ~ 1)
        Vector2 bgSize = background.sizeDelta;
        Vector2 normalized = new Vector2(
            localPoint.x / (bgSize.x * 0.5f),
            localPoint.y / (bgSize.y * 0.5f)
        );

        // 범위 제한
        inputValue = normalized.magnitude > 1f ? normalized.normalized : normalized;

        // 데드존 처리
        if (inputValue.magnitude < deadZone)
            inputValue = Vector2.zero;

        // 핸들 위치 업데이트
        handle.anchoredPosition = inputValue * (bgSize.x * 0.5f) * handleRange;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsPressed = false;
        inputValue = Vector2.zero;
        handle.anchoredPosition = Vector2.zero;
    }
}
