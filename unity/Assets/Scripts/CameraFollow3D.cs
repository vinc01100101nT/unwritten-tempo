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
    [Tooltip("How long the camera takes to catch up (seconds). Bigger = it trails further behind you and coasts " +
             "to a stop; 0 = locked to the player. Gated on/off by Settings ▸ Graphics ▸ Smooth camera follow.")]
    public float smoothTime = 0.35f;

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

    // The lagged point the camera frames (see FollowAnchor). Invalid until the first frame with a target, and
    // re-seeded whenever the target changes, so a new/teleported target is framed instantly instead of flown to.
    Vector3 anchorPos;
    bool anchorValid;
    Transform anchorTarget;

    /// <summary>How far the camera sits from the point it frames at the zoom the game STARTS at, read from this
    /// camera's saved framing before any scroll. <see cref="OverheadScreenLayer"/> sizes on-screen words against
    /// it, so they keep the size they had in the world at that zoom.</summary>
    public static float StartViewDepth { get; private set; }

    void Awake()
    {
        cam = GetComponent<Camera>();
        Main = this;
        StartViewDepth = new Vector2(distance, height - lookAtHeight).magnitude;
    }
    void OnDestroy() { if (Main == this) Main = null; }
    void OnEnable() { lastMouse = MousePos(); anchorValid = false; }

    /// <summary>The point the camera frames: <paramref name="targetPos"/> eased toward over
    /// <see cref="smoothTime"/> seconds, so the camera runs late behind the player and coasts to a stop.
    /// Returns the live position unchanged when the player has smoothing switched off, or on the first frame.</summary>
    Vector3 FollowAnchor(Vector3 targetPos)
    {
        float ease = GameSettings.SmoothCameraFollow ? smoothTime : 0f;
        if (!anchorValid || anchorTarget != target || ease <= 0f)
        {
            anchorPos = targetPos;
            vel = Vector3.zero;
            anchorValid = true;
            anchorTarget = target;
            return anchorPos;
        }
        anchorPos = Vector3.SmoothDamp(anchorPos, targetPos, ref vel, ease);
        return anchorPos;
    }

    /// <summary>Drop the follow delay for one frame — the camera frames the target exactly where it is now.
    /// For teleports / scene loads, so the camera doesn't fly across the map catching up.</summary>
    public void SnapToTarget() => anchorValid = false;

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

    // ---- cinematic framing (F-interact side view) --------------------------------------------------
    bool cineWant;
    Vector3 cinePos, cineLook;
    float cineBlend;          // 0 = normal follow, 1 = full cinematic pose; eased both ways
    [Tooltip("Seconds to ease into / out of a cinematic (F-interact) side view.")]
    public float cineTime = 0.5f;

    /// <summary>Frame a fixed cinematic pose (position + look-at), easing over <see cref="cineTime"/>. Call each
    /// frame to update the pose while active; <see cref="ExitCinematic"/> eases back to the normal follow.</summary>
    public void EnterCinematic(Vector3 camPos, Vector3 lookAt) { cineWant = true; cinePos = camPos; cineLook = lookAt; }
    public void ExitCinematic() { cineWant = false; }
    /// <summary>True while a cinematic is engaged or still easing back out.</summary>
    public bool Cinematic => cineWant || cineBlend > 0.001f;

    void LateUpdate()
    {
        // Undo last frame's shake so it can never accumulate into the camera's position (it's re-applied at
        // the end, on top of the freshly computed pose).
        transform.position -= shakeOffset;
        shakeOffset = Vector3.zero;

        if (!Cinematic) HandleInput();   // no zoom/orbit while a cinematic is framing
        if (target == null) return;

        Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, height, -distance);

        // What the camera is framing. The delay is applied HERE, to the point being followed — not to the
        // camera's own position. Both the camera and its look-at are then built from that one lagged point, so
        // the view direction stays exactly -offset and the camera NEVER rotates to chase the player: it simply
        // arrives late, and the player drifts off centre while moving and slides back when you stop.
        // (Lagging the camera position instead makes it swivel, because it keeps aiming at the real player.)
        Vector3 anchor = FollowAnchor(target.position);
        Vector3 followPos = anchor + offset;
        Vector3 followLook = anchor + Vector3.up * lookAtHeight;

        float targetBlend = cineWant ? 1f : 0f;
        cineBlend = Mathf.MoveTowards(cineBlend, targetBlend, Time.deltaTime / Mathf.Max(0.01f, cineTime));

        if (cineBlend <= 0.0001f)
        {
            transform.position = followPos;
            transform.LookAt(followLook);
        }
        else
        {
            // Blend the live follow pose ↔ the cinematic side view. The LOOK POINT lerps too, so LookAt eases the
            // rotation smoothly both in and out. SmoothStep gives an ease-in/out feel.
            float k = Mathf.SmoothStep(0f, 1f, cineBlend);
            transform.position = Vector3.Lerp(followPos, cinePos, k);
            transform.LookAt(Vector3.Lerp(followLook, cineLook, k));
        }

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
        float zoom = ScrollAmount();
        if (zoom != 0f)
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
        bool orbiting = RightHeld();
#if UNITY_ANDROID
        // A phone orbits with a one-finger drag, and that gesture reports its OWN travel. Taking a difference of
        // pointer positions here would fold in the jump from wherever the last finger happened to lift, so the
        // camera would snap the instant a new drag began.
        if (TouchControls.HasTouch)
        {
            delta = TouchControls.OrbitDelta;
            orbiting = delta.sqrMagnitude > 0f;
        }
#endif

        if (orbiting)
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

    // ---- input helpers -----------------------------------------------------------
    // All three route through PointerInput, the one gate for the mouse (and, on a phone, for the finger that
    // answers in its place). The backend dance and the scroll-sign normalisation live there, not here.
    static float ScrollAmount() => PointerInput.ScrollAmount;
    static Vector2 MousePos() => PointerInput.Position;
    static bool RightHeld() => PointerInput.SecondaryHeld;
}
