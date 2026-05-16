using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 카드 한 장의 뷰 컴포넌트.
/// Kenney 보드게임 팩의 카드 스프라이트(앞면)를 단일 Image로 표시.
///
/// 외부 와이어링:
///   - SetCard(card)      : 표시할 카드 지정 → CardSpriteLibrary에서 자동으로 스프라이트 로드
///   - SetPlayable(bool)  : 현재 낼 수 있는 카드인지 시각 표시 (불가 시 디밍)
///   - OnClicked          : 클릭됐을 때 호출되는 이벤트 (HandManager가 구독)
///   - SetSelected(bool)  : 선택 상태 토글 (위로 살짝 올라옴)
///   - PlayShake()        : 잘못된 선택 시 좌우 흔들림 0.3초
/// </summary>
public class CardUI : MonoBehaviour, IPointerClickHandler
{
    [Header("Visual Refs")]
    [SerializeField] private Image       faceImage;     // 카드 앞면 (스프라이트)
    [SerializeField] private CanvasGroup canvasGroup;   // 디밍 제어

    [Header("Animation")]
    [SerializeField] private float liftY         = 25f;  // 선택 시 위로 올라가는 양
    [SerializeField] private float liftDuration  = 0.12f;
    [SerializeField] private float shakeAmp      = 8f;
    [SerializeField] private float shakeDuration = 0.3f;

    [Header("State (read-only at runtime)")]
    [SerializeField] private Card _card;
    [SerializeField] private bool _isSelected;
    [SerializeField] private bool _isPlayable = true;

    /// <summary>이 카드뷰가 클릭됐을 때 발생. 인자는 자기 자신.</summary>
    public event Action<CardUI> OnClicked;

    public Card Card        => _card;
    public bool IsSelected  => _isSelected;
    public bool IsPlayable  => _isPlayable;

    private RectTransform _rt;
    private Vector2       _basePos;
    private Coroutine     _liftCo;
    private Coroutine     _shakeCo;

    private void Awake()
    {
        _rt = (RectTransform)transform;
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    // ─────────────────────────────────────────────────────────────
    //  Data binding
    // ─────────────────────────────────────────────────────────────

    public void SetCard(Card card)
    {
        _card = card;
        Refresh();
    }

    public void SetPlayable(bool playable)
    {
        _isPlayable = playable;
        // 디밍 대신 색 톤만 살짝 어둡게 — 카드 자체는 또렷이 보이게.
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        if (faceImage   != null)
            faceImage.color = playable ? Color.white : new Color(0.78f, 0.78f, 0.78f, 1f);
    }

    private void Refresh()
    {
        if (faceImage == null) return;

        if (_card == null)
        {
            faceImage.sprite = null;
            return;
        }

        var sprite = CardSpriteLibrary.GetFace(_card);
        if (sprite == null)
        {
            // 스프라이트가 없으면 빈 흰색 박스라도 표시.
            faceImage.sprite = null;
            faceImage.color = Color.white;
        }
        else
        {
            faceImage.sprite = sprite;
            faceImage.color  = Color.white;
            faceImage.preserveAspect = true;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Selection & animations
    // ─────────────────────────────────────────────────────────────

    public void SetBasePosition(Vector2 pos)
    {
        _basePos = pos;
        _rt.anchoredPosition = _isSelected ? pos + new Vector2(0f, liftY) : pos;
    }

    public void SetSelected(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;

        if (_liftCo != null) StopCoroutine(_liftCo);
        _liftCo = StartCoroutine(LiftRoutine(selected));
    }

    private IEnumerator LiftRoutine(bool up)
    {
        Vector2 from = _rt.anchoredPosition;
        Vector2 to   = up ? _basePos + new Vector2(0f, liftY) : _basePos;
        float t = 0f;
        while (t < liftDuration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.SmoothStep(0f, 1f, t / liftDuration);
            _rt.anchoredPosition = Vector2.LerpUnclamped(from, to, a);
            yield return null;
        }
        _rt.anchoredPosition = to;
    }

    public void PlayShake()
    {
        if (_shakeCo != null) StopCoroutine(_shakeCo);
        _shakeCo = StartCoroutine(ShakeRoutine());
    }

    private IEnumerator ShakeRoutine()
    {
        Vector2 origin = _isSelected ? _basePos + new Vector2(0f, liftY) : _basePos;
        float t = 0f;
        while (t < shakeDuration)
        {
            t += Time.unscaledDeltaTime;
            float decay = 1f - (t / shakeDuration);
            float dx    = Mathf.Sin(t * 60f) * shakeAmp * decay;
            _rt.anchoredPosition = origin + new Vector2(dx, 0f);
            yield return null;
        }
        _rt.anchoredPosition = origin;
    }

    // ─────────────────────────────────────────────────────────────
    //  Click
    // ─────────────────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        OnClicked?.Invoke(this);
    }
}
