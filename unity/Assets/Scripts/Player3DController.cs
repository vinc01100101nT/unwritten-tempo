using System.Collections.Generic;
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
[RequireComponent(typeof(SpriteRenderer), typeof(CharacterController))]
public class Player3DController : MonoBehaviour
{
    [Header("Walk frames (column = facing), filled by Build 2.5D Spike")]
    public Sprite[] down, up, left, right;

    public float moveSpeed = 4f;
    public float framesPerSecond = 8f;
    [Tooltip("Stop this close to a click target.")]
    public float arriveDistance = 0.12f;
    [Tooltip("Clearance kept from props when routing (Phase 3D-C) — about the CharacterController radius.")]
    public float agentRadius = 0.35f;
    [Tooltip("Constant downward pull (units/s) that keeps the CharacterController grounded.")]
    public float gravity = 12f;

    SpriteRenderer sr;
    CharacterController controller;
    Camera cam;
    Vector2 facing = Vector2.down;
    float timer; int frame;
    readonly List<Vector2> path = new List<Vector2>();   // XZ waypoints from Pathfinder3D
    int wp; bool routing;
    float groundY;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        controller = GetComponent<CharacterController>();
        groundY = transform.position.y;   // sprite height; ground-raycast targets snap to XZ
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;

        // WASD takes priority and cancels any routed destination.
        Vector3 move = ReadWasd();
        if (move.sqrMagnitude > 0.001f) { routing = false; path.Clear(); move.Normalize(); }
        else if (TryReadClick(out var p)) StartRoute(p);   // click → A* route around props (3D-C)

        if (routing) move = FollowRoute();

        bool moving = move.sqrMagnitude > 0.001f;
        if (moving)
            facing = Mathf.Abs(move.x) > Mathf.Abs(move.z) ? new Vector2(Mathf.Sign(move.x), 0f)
                                                           : new Vector2(0f, Mathf.Sign(move.z));

        // Move via CharacterController (Phase 3D-B) so we slide along props instead of through
        // them; the downward term keeps us pinned to the flat ground.
        Vector3 motion = move * moveSpeed + Vector3.down * gravity;
        controller.Move(motion * Time.deltaTime);

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

    // Phase 3D-C: ask Pathfinder3D for a route (around props) to the clicked point.
    void StartRoute(Vector3 goal)
    {
        var from = new Vector2(transform.position.x, transform.position.z);
        var to = new Vector2(goal.x, goal.z);
        var route = Pathfinder3D.FindPath(from, to, agentRadius);
        path.Clear();
        if (route != null) path.AddRange(route); else path.Add(to);   // truly stuck → straight line
        wp = 0;
        routing = path.Count > 0;
    }

    // Unit step direction toward the current waypoint (zero when the route ends).
    Vector3 FollowRoute()
    {
        if (wp >= path.Count) { routing = false; return Vector3.zero; }
        Vector3 to = WaypointDelta();
        if (to.magnitude <= arriveDistance)
        {
            wp++;
            if (wp >= path.Count) { routing = false; return Vector3.zero; }
            to = WaypointDelta();
        }
        return to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.zero;
    }

    Vector3 WaypointDelta()
    {
        Vector2 c = path[wp];
        Vector3 d = new Vector3(c.x, transform.position.y, c.y) - transform.position;
        d.y = 0f;
        return d;
    }

    void OnDrawGizmosSelected()
    {
        if (!routing || path.Count == 0) return;
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
        Vector3 prev = transform.position;
        for (int i = wp; i < path.Count; i++)
        {
            Vector3 p = new Vector3(path[i].x, transform.position.y, path[i].y);
            Gizmos.DrawLine(prev, p);
            Gizmos.DrawWireSphere(p, 0.1f);
            prev = p;
        }
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
