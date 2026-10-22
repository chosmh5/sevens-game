using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자식 RectTransform들을 부채꼴(Fan) 형태로 배치하는 컴포넌트.
///
/// 기본 정책:
/// - 카드 N장이 자식으로 있을 때, 호(arc) 위에 균등 분포.
/// - 손패가 많아질수록 각 카드 사이 각도는 동일하게 유지하되 totalAngle 상한으로 클램프.
/// - 각 카드는 호의 접선 방향으로 약간 기울어진다 (자연스러운 부채꼴).
///
/// CardUI와 연동: 자식에 CardUI가 있으면 SetBasePosition을 호출해
/// 선택/셰이크 애니메이션 origin을 갱신.
/// </summary>
[ExecuteAlways]
public class FanLayout : MonoBehaviour
{
    [Header("Geometry")]
    [Tooltip("호의 반지름(px). 클수록 카드들이 더 평평해진다.")]
    [SerializeField] private float radius = 600f;

    [Tooltip("카드 한 장당 회전 각도(도). 카드 사이 간격을 결정.")]
    [SerializeField] private float anglePerCard = 8f;

    [Tooltip("최대 펼침 각도(도). 카드가 많을 때 이 값을 초과하지 않도록 자동 압축.")]
    [SerializeField] private float maxTotalAngle = 60f;

    [Tooltip("부채꼴이 향하는 기준 각도(0 = 위쪽). 보통 0으로 둠.")]
    [SerializeField] private float centerAngleOffset = 0f;

    [Tooltip("자식 카드를 호의 접선 방향으로 회전시킬지 여부.")]
    [SerializeField] private bool rotateChildren = true;

    [Tooltip("부채꼴 중심을 자식 영역에서 얼마나 아래로 내릴지(px). 카드가 위로 휘게.")]
    [SerializeField] private float pivotYOffset = -300f;

    // ─────────────────────────────────────────────────────────────

    private readonly List<RectTransform> _cached = new List<RectTransform>();

    private void OnEnable()  { ApplyLayout(); }
    private void OnTransformChildrenChanged() { ApplyLayout(); }

#if UNITY_EDITOR
    private void OnValidate() { ApplyLayout(); }
#endif

    /// <summary>런타임에 부채꼴 모양을 바꾼다(레트로 스킨이 손패 영역에 맞춰 호출).</summary>
    public void Configure(float newRadius, float newAnglePerCard, float newMaxTotalAngle, float newPivotYOffset)
    {
        radius = newRadius;
        anglePerCard = newAnglePerCard;
        maxTotalAngle = newMaxTotalAngle;
        pivotYOffset = newPivotYOffset;
        ApplyLayout();
    }

    /// <summary>외부에서 강제로 재배치 (카드 추가/제거 후 호출).</summary>
    public void ApplyLayout()
    {
        CollectChildren();
        int n = _cached.Count;
        if (n == 0) return;

        // 카드 사이 각도 결정 (총합이 maxTotalAngle을 넘지 않게 압축)
        float step      = anglePerCard;
        float total     = step * (n - 1);
        if (total > maxTotalAngle && n > 1)
        {
            step  = maxTotalAngle / (n - 1);
            total = maxTotalAngle;
        }

        float startAngle = -total * 0.5f + centerAngleOffset;

        for (int i = 0; i < n; i++)
        {
            var rt = _cached[i];
            float angleDeg = startAngle + step * i;
            float angleRad = angleDeg * Mathf.Deg2Rad;

            // 부채꼴 중심은 (0, pivotYOffset). 각도 0 = 위쪽으로 펼침.
            float x = Mathf.Sin(angleRad) * radius;
            float y = Mathf.Cos(angleRad) * radius + pivotYOffset;

            Vector2 pos = new Vector2(x, y);
            rt.anchoredPosition = pos;

            if (rotateChildren)
                rt.localRotation = Quaternion.Euler(0, 0, -angleDeg);
            else
                rt.localRotation = Quaternion.identity;

            // CardUI에 base position 갱신 알림 (선택 lift / shake 기준점)
            var cu = rt.GetComponent<CardUI>();
            if (cu != null) cu.SetBasePosition(pos);
        }
    }

    private void CollectChildren()
    {
        _cached.Clear();
        for (int i = 0; i < transform.childCount; i++)
        {
            var t = transform.GetChild(i);
            if (!t.gameObject.activeSelf) continue;
            if (t is RectTransform rt) _cached.Add(rt);
        }
    }
}
