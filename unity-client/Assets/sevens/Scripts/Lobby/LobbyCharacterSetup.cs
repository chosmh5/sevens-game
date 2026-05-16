using UnityEngine;

/// <summary>
/// 로비 3D 캐릭터 연출
/// - Idle 애니메이션 자동 재생
/// - 부드러운 호흡(상하 움직임) 효과
/// - 터치 시 리액션
/// </summary>
public class LobbyCharacterSetup : MonoBehaviour
{
    [Header("Idle Animation")]
    [SerializeField] private float breathSpeed = 1.5f;
    [SerializeField] private float breathAmount = 0.02f;

    [Header("Touch Reaction")]
    [SerializeField] private string[] reactionTriggers = { "Wave", "Nod", "Happy" };

    private Animator animator;
    private Vector3 originalPosition;
    private float breathTimer;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        originalPosition = transform.localPosition;
    }

    private void Update()
    {
        // 부드러운 호흡 효과 (위아래 미세 움직임)
        breathTimer += Time.deltaTime * breathSpeed;
        float breathOffset = Mathf.Sin(breathTimer) * breathAmount;
        transform.localPosition = originalPosition + Vector3.up * breathOffset;
    }

    /// <summary>
    /// 캐릭터 터치 시 랜덤 리액션
    /// </summary>
    public void PlayReaction()
    {
        if (animator == null || reactionTriggers.Length == 0) return;

        string trigger = reactionTriggers[Random.Range(0, reactionTriggers.Length)];
        animator.SetTrigger(trigger);
    }
}
