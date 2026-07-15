using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Orbiting tilted follow-camera for the 2.5D world. Tracks the target (player) and supports:
///   • Scroll wheel  → zoom (<see cref="distance"/>).
///   • Right-drag    → orbit (<see cref="yaw"/> + <see cref="height"/>) around the target.
/// Position = target + yaw-rotated (0, height, -distance); always looks at the target.
///
/// Also owns the camera's transparency sort axis: each frame it points it along the camera's
/// horizontal forward so <see cref="CameraDepthSorter"/>'s per-sprite orders have a matching
/// tie-breaker. This is the ONE place that axis is written (camera-local, play-mode only) —
/// no per-object component mutates global GraphicsSettings.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class CameraFollow3D : MonoBehaviour
{
    public Transform target;

    [Header("Framing")]
    public float distance = 4f;        // pull-back (zoom)
    public float height = 10f;         // Y above the target (right-drag adjusts this)
    public float yaw = 0f;             // orbit angle (degrees)
    public float lookAtHeight = 0.18f;
    public float smoothTime = 0.12f;

    [Header("Limits")]
    public float minDistance = 0.4f, maxDistance = 7f;
    // minHeight lets right-drag tilt all the way down to a near-horizontal, ground-level view.
    public float minHeight = 0.1f, maxHeight = 10f;
    [Tooltip("Cap the camera's downward tilt (degrees from horizontal) so right-drag can't reach a full top-down view.")]
    public float maxPitch = 50f;

    [Header("Sensitivity")]
    public float zoomStep = 1f;             // world-units of pull-back per scroll notch
    public float dragYawSpeed = 0.25f;      // deg/pixel for right-drag X
    public float dragHeightSpeed = 0.04f;   // units/pixel for right-drag Y

    Camera cam;
    Vector3 vel;
    Vector2 lastMouse;

    void Awake() { cam = GetComponent<Camera>(); Main = this; }
    void OnDestroy() { if (Main == this) Main = null; }
    void OnEnable() => lastMouse = MousePos();

    // ---- screen shake (call CameraFollow3D.ShakeMain from anywhere, e.g. a meteor impact) ----------
    public static CameraFollow3D Main { get; private set; }
    Vector3 shakeOffset;            // the offset applied last frame (undone next frame so it can't feed back)
    float shakeMag, shakeDur, shakeLeft;

    /// <summary>Kick a screen shake on the main follow-camera: <paramref name="magnitude"/> world-units of
    /// jitter that decays to zero over <paramref name="duration"/>s. Overlapping shakes take the strongest +
    /// longest (bounded, not additive). No-op if there's no follow camera.</summary>
    public static void ShakeMain(float magnitude, float duration)
    {
        if (Main != null) Main.AddShake(magnitude, duration);
    }

    void AddShake(float magnitude, float duration)
    {
        if (magnitude <= 0f || duration <= 0f) return;
        shakeMag  = Mathf.Max(shakeMag, magnitude);
        shakeDur  = Mathf.Max(shakeDur, duration);
        shakeLeft = Mathf.Max(shakeLeft, duration);
    }

    // This frame's shake offset (camera-local screen plane), ticking the timer down. Zero (alloc-free) when idle.
    Vector3 CurrentShakeOffset()
    {
        if (shakeLeft <= 0f) return Vector3.zero;
        shakeLeft -= Time.deltaTime;
        float amp = shakeMag * (shakeDur > 1e-4f ? Mathf.Clamp01(shakeLeft / shakeDur) : 0f);   // linear decay to 0
        if (shakeLeft <= 0f) { shakeMag = 0f; shakeDur = 0f; shakeLeft = 0f; }
        return (transform.right * Random.Range(-1f, 1f) + transform.up * Random.Range(-1f, 1f)) * amp;
    }

    void LateUpdate()
    {
        // Undo last frame's shake so the follow smoothing always works from the CLEAN base position
        // (shake never feeds back into SmoothDamp → no drift/runaway).
        transform.position -= shakeOffset;
        shakeOffset = Vector3.zero;

        HandleInput();
        if (target == null) return;

        Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, height, -distance);
        Vector3 desired = target.position + offset;
        transform.position = smoothTime > 0f
            ? Vector3.SmoothDamp(transform.position, desired, ref vel, smoothTime)
            : desired;
        transform.LookAt(target.position + Vector3.up * lookAtHeight);

        shakeOffset = CurrentShakeOffset();   // re-apply on TOP of the smoothed base (after LookAt set rotation)
        transform.position += shakeOffset;

        SyncSortAxis();
    }

    // Keep the transparency tie-breaker aligned with the view's horizontal forward (matches
    // CameraDepthSorter's projection). Camera-local only — never writes GraphicsSettings.
    void SyncSortAxis()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;
        Vector3 axis = transform.forward;
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.forward;
        cam.transparencySortMode = TransparencySortMode.CustomAxis;
        cam.transparencySortAxis = axis.normalized;
    }

    void HandleInput()
    {
        if (DevSettings.Blocking) return;   // cursor is over the dev-settings panel — don't zoom/orbit while tuning

        int zoom = ScrollDir();
        if (zoom != 0)
        {
            // Dolly straight along the current sightline: scale height with distance so the pitch
            // (height : distance ratio) stays put — pure zoom, no viewing-angle drift. The vertical
            // angle is set only by right-drag-Y; zoom never changes it.
            float newDist = Mathf.Clamp(distance - zoom * zoomStep, minDistance, maxDistance);
            if (distance > 1e-4f)
                height = Mathf.Clamp(height * (newDist / distance), minHeight, maxHeight);
            distance = newDist;
        }

        Vector2 mouse = MousePos();
        Vector2 delta = mouse - lastMouse;
        lastMouse = mouse;

        if (RightHeld())
        {
            yaw += delta.x * dragYawSpeed;
            height = Mathf.Clamp(height - delta.y * dragHeightSpeed, minHeight, maxHeight);
        }

        // Cap the pitch so the view can't tilt past maxPitch toward full top-down. The look angle is
        // atan2(height, distance), so height ≤ distance·tan(maxPitch) holds it at/under the cap. Enforced every
        // frame (incl. the default framing); zoom scales height with distance so the ratio — and the cap — hold.
        float maxH = distance * Mathf.Tan(Mathf.Clamp(maxPitch, 5f, 89f) * Mathf.Deg2Rad);
        if (height > maxH) height = maxH;
    }

    // ---- input helpers (legacy Input always works; new Input System optional) ---
    // Scroll units differ per backend (legacy mouseScrollDelta vs new Input System pixels, which
    // is ~120/notch on Windows), so we take only the SIGN — one notch = one zoomStep, regardless
    // of backend or mouse driver. Predictable, and can't be "too slow" or "too fast".
    static int ScrollDir()
    {
        float s;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) s = Mouse.current.scroll.ReadValue().y;
        else s = Input.mouseScrollDelta.y;
#else
        s = Input.mouseScrollDelta.y;
#endif
        return s > 0f ? 1 : s < 0f ? -1 : 0;
    }
    static Vector2 MousePos()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
        return Input.mousePosition;
    }
    static bool RightHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.rightButton.isPressed) return true;
#endif
        return Input.GetMouseButton(1);
    }
}
