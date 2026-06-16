using UnityEngine;

/// <summary>
/// 3D tilted follow-camera for the 2.5D world — the successor to CameraFollow2D. Keeps a fixed
/// height + pull-back offset behind the target (the player) and a constant downward pitch
/// (RO-style angle), smoothing position so the world glides rather than snaps. Rotation is
/// fixed so the tilt never changes as the player moves.
/// </summary>
[DisallowMultipleComponent]
public class CameraFollow3D : MonoBehaviour
{
    public Transform target;

    [Tooltip("World-space offset from the target: height (Y) + pull-back (-Z).")]
    public Vector3 offset = new Vector3(0f, 10f, -8f);

    [Tooltip("Constant downward pitch in degrees (the RO-style tilt).")]
    public float pitch = 52f;

    [Tooltip("Position smoothing; 0 = snap instantly.")]
    public float smoothTime = 0.12f;

    Vector3 vel;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.position + offset;
        transform.position = smoothTime > 0f
            ? Vector3.SmoothDamp(transform.position, desired, ref vel, smoothTime)
            : desired;
        transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
