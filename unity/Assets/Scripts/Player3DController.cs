using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The real 2.5D player (Phase 3D-A): click a point on the 3D ground to walk there (your Dota
/// style — raycast from the camera), with WASD as a direct fallback, plus 4-direction billboard
/// animation from the Ninja Adventure sheet. Movement is DIRECT for now; obstacle routing comes
/// in Phase 3D-C (the existing grid A*, adapted to XZ). Pair with <see cref="Billboard"/>; the
/// frame arrays are filled by Build25DSpike. Supersedes the throwaway SpikeActor.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Player3DController : MonoBehaviour
{
    [Header("Walk frames (column = facing), filled by Build 2.5D Spike")]
    public Sprite[] down, up, left, right;

    public float moveSpeed = 4f;
    public float framesPerSecond = 8f;
    [Tooltip("Stop this close to a click target.")]
    public float arriveDistance = 0.12f;

    SpriteRenderer sr;
    Camera cam;
    Vector2 facing = Vector2.down;
    float timer; int frame;
    bool hasTarget; Vector3 target;
    float groundY;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        groundY = transform.position.y;   // keep the sprite at this height while moving on XZ
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;

        // WASD takes priority and cancels any click destination.
        Vector3 move = ReadWasd();
        if (move.sqrMagnitude > 0.001f) { hasTarget = false; move.Normalize(); }
        else if (TryReadClick(out var p)) { target = p; hasTarget = true; }

        // Steer toward a click destination until we arrive.
        if (hasTarget)
        {
            Vector3 to = target - transform.position; to.y = 0f;
            if (to.magnitude <= arriveDistance) { hasTarget = false; move = Vector3.zero; }
            else move = to.normalized;
        }

        bool moving = move.sqrMagnitude > 0.001f;
        if (moving)
        {
            transform.position += move * (moveSpeed * Time.deltaTime);
            var pos = transform.position; pos.y = groundY; transform.position = pos;
            facing = Mathf.Abs(move.x) > Mathf.Abs(move.z) ? new Vector2(Mathf.Sign(move.x), 0f)
                                                           : new Vector2(0f, Mathf.Sign(move.z));
        }
        Animate(moving);
    }

    Vector3 ReadWasd()
    {
        float h = 0f, v = 0f;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  h -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    v += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  v -= 1f;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        h = Input.GetAxisRaw("Horizontal");
        v = Input.GetAxisRaw("Vertical");
#endif
        return new Vector3(h, 0f, v);
    }

    bool TryReadClick(out Vector3 point)
    {
        point = default;
        if (cam == null) return false;

        bool clicked = false; Vector2 mouse = default;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m != null)
        {
            clicked = m.rightButton.wasPressedThisFrame || m.leftButton.wasPressedThisFrame;
            mouse = m.position.ReadValue();
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        clicked = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(0);
        mouse = Input.mousePosition;
#endif
        if (!clicked) return false;

        var ray = cam.ScreenPointToRay(mouse);
        if (Physics.Raycast(ray, out var hit, 500f)) { point = hit.point; return true; }
        // Fallback: intersect the flat ground plane at y = groundY.
        if (Mathf.Abs(ray.direction.y) > 1e-4f)
        {
            float t = (groundY - ray.origin.y) / ray.direction.y;
            if (t > 0f) { point = ray.GetPoint(t); return true; }
        }
        return false;
    }

    void Animate(bool moving)
    {
        var clip = ClipFor(facing);
        if (clip == null || clip.Length == 0) return;
        if (moving)
        {
            timer += Time.deltaTime;
            float ft = 1f / Mathf.Max(framesPerSecond, 0.01f);
            while (timer >= ft) { timer -= ft; frame = (frame + 1) % clip.Length; }
        }
        else { frame = 0; timer = 0f; }
        sr.sprite = clip[Mathf.Min(frame, clip.Length - 1)];
    }

    Sprite[] ClipFor(Vector2 f)
    {
        if (f.y > 0f) return up;     // +Z away from camera
        if (f.y < 0f) return down;   // -Z toward camera
        if (f.x < 0f) return left;
        return right;
    }
}
