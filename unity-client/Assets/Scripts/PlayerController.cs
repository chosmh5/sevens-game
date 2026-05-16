using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] float moveSpeed    = 3f;
    [SerializeField] float rotateSpeed  = 360f;

    CharacterController _cc;
    Animator            _anim;

    static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");

    void Start()
    {
        _cc   = GetComponent<CharacterController>();
        _anim = GetComponent<Animator>();
        if (_anim != null && _anim.runtimeAnimatorController == null) _anim = null;
    }

    void Update()
    {
        if (TitleScreenManager.IsActive) return;
        if (PlayerInteraction.Instance != null && PlayerInteraction.Instance.IsInteracting) return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 camForward = Vector3.forward;
        Vector3 camRight   = Vector3.right;
        if (Camera.main != null)
        {
            camForward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            camRight   = Vector3.ProjectOnPlane(Camera.main.transform.right,   Vector3.up).normalized;
        }

        Vector3 move = camForward * v + camRight * h;
        if (move.sqrMagnitude > 1f) move.Normalize();

        bool isMoving = move.sqrMagnitude > 0.01f;
        if (isMoving)
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(move),
                rotateSpeed * Time.deltaTime);

        // gravity + horizontal move combined
        _cc.Move((move * moveSpeed + Vector3.down * 2f) * Time.deltaTime);
        _anim?.SetBool(IsWalkingHash, isMoving);
    }
}
