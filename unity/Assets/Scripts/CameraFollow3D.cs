using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Orbiting tilted follow-camera for the 2.5D world. Tracks the target (player) and supports:
///   • Scroll wheel         → zoom (<see cref="distance"/>).
///   • Q / E or right-drag  → orbit (<see cref="yaw"/> + <see cref="height"/>) around the target.
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
    public float distance = 9f;        // pull-back (zoom)
    public float height = 10f;         // Y above the target (right-drag adjusts this)
    public float yaw = 0f;             // orbit angle (degrees)
    public float lookAtHeight = 0.6f;  // aim a little above the target's feet
    public float smoothTime = 0.12f;

    [Header("Limits")]
    public float minDistance = 4f, maxDistance = 22f;
    public float minHeight = 2f, maxHeight = 26f;

    [Header("Sensitivity")]
    public float zoomStep = 2f;             // world-units of pull-back per scroll notch
    public float keyOrbitSpeed = 90f;       // deg/sec for Q / E
    public float dragYawSpeed = 0.25f;      // deg/pixel for right-drag X
    public float dragHeightSpeed = 0.04f;   // units/pixel for right-drag Y

    Camera cam;
    Vector3 vel;
    Vector2 lastMouse;

    void Awake() => cam = GetComponent<Camera>();
    void OnEnable() => lastMouse = MousePos();

    void LateUpdate()
    {
        HandleInput();
        if (target == null) return;

        Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, height, -distance);
        Vector3 desired = target.position + offset;
        transform.position = smoothTime > 0f
            ? Vector3.SmoothDamp(transform.position, desired, ref vel, smoothTime)
            : desired;
        transform.LookAt(target.position + Vector3.up * lookAtHeight);

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

        if (KeyHeldQ()) yaw -= keyOrbitSpeed * Time.deltaTime;
        if (KeyHeldE()) yaw += keyOrbitSpeed * Time.deltaTime;

        Vector2 mouse = MousePos();
        Vector2 delta = mouse - lastMouse;
        lastMouse = mouse;

        if (RightHeld())
        {
            yaw += delta.x * dragYawSpeed;
            height = Mathf.Clamp(height - delta.y * dragHeightSpeed, minHeight, maxHeight);
        }
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
    static bool KeyHeldQ()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.qKey.isPressed) return true;
#endif
        return Input.GetKey(KeyCode.Q);
    }
    static bool KeyHeldE()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.eKey.isPressed) return true;
#endif
        return Input.GetKey(KeyCode.E);
    }
}
