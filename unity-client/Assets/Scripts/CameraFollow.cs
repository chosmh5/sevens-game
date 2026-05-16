using UnityEngine;

/// <summary>
/// 플레이어를 따라다니는 3인칭 카메라.
/// 우클릭 드래그로 수평 회전.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] float distance    = 6f;
    [SerializeField] float height      = 4f;
    [SerializeField] float smoothTime  = 0.15f;
    [SerializeField] float rotateSpeed = 120f;

    float   _yaw;
    Vector3 _vel;

    void LateUpdate()
    {
        if (target == null) return;

        if (Input.GetMouseButton(1))
            _yaw += Input.GetAxis("Mouse X") * rotateSpeed * Time.deltaTime;

        Quaternion rot     = Quaternion.Euler(0, _yaw, 0);
        Vector3    desired = target.position + rot * new Vector3(0, height, -distance);

        transform.position = Vector3.SmoothDamp(transform.position, desired, ref _vel, smoothTime);
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }

    public void SetTarget(Transform t) => target = t;
}
