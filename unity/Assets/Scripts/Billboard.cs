using UnityEngine;

/// <summary>
/// Turns this object to face the camera every frame — the core of the 2.5D "billboard"
/// look (a flat 2D sprite standing in a 3D world, à la Ragnarok Online / Don't Starve).
/// Put it on the SpriteRenderer object that <see cref="CharacterAnimator2D"/> drives: the
/// animator keeps swapping the 4-direction walk frames while this keeps the quad turned
/// toward the camera so the sprite never looks foreshortened as the 3D world tilts.
///
/// Modes:
///  • <b>Upright</b> (default) — yaw toward the camera but stay world-vertical, so the
///    character always stands straight up. Best for a fixed RO-style tilted camera.
///  • <b>FaceCamera</b> — point fully at the camera (matches its pitch too); use if you
///    later orbit/zoom the camera freely and want sprites perfectly face-on.
///
/// If the sprite renders mirrored (left/right frames swapped) or you see its back, tick
/// <see cref="flip"/> — sprite quad winding varies, so this is the one knob that may need it.
/// </summary>
[DisallowMultipleComponent]
public class Billboard : MonoBehaviour
{
    public enum Mode { Upright, FaceCamera }

    [Tooltip("Upright = yaw to camera, stay vertical (RO-style). FaceCamera = fully face-on.")]
    public Mode mode = Mode.Upright;

    [Tooltip("Tick if the sprite looks mirrored or you see its back — flips 180° on Y.")]
    public bool flip = false;

    Transform cam;

    void OnEnable() => cam = Camera.main != null ? Camera.main.transform : null;

    void LateUpdate()
    {
        if (cam == null)
        {
            if (Camera.main == null) return;   // no camera yet (e.g. Systems not booted)
            cam = Camera.main.transform;
        }

        // Orient so the sprite's readable face turns toward the camera. Ninja Adventure sheets
        // read correctly from the -Z side, so +Z must point AWAY from the camera (otherwise the
        // left/right walk frames render mirrored — the `flip` toggle is just a fallback now).
        Vector3 dir = transform.position - cam.position;
        if (mode == Mode.Upright) dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;

        var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        if (flip) rot *= Quaternion.Euler(0f, 180f, 0f);
        transform.rotation = rot;
    }
}
