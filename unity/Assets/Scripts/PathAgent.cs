using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The "smart movement" component — attach it to ANY unit that moves (the player, a
/// monster, an ally / companion) and it walks to where it's told while routing AROUND
/// walls instead of bumping into them. Static NPCs don't move, so they don't get one.
///
/// It is a drop-in superset of <see cref="PlayerController2D"/>'s movement service — same
/// verbs, so callers don't change:
///   • <see cref="MoveTo"/>     — route to a fixed point, then stop on arrival.
///   • <see cref="MoveToward"/> — chase a (moving) target; re-routes on a cadence. The
///                                CALLER decides when to stop (e.g. in attack/leash range).
///   • <see cref="Halt"/>       — stop now.
///
/// Routing comes from <see cref="Pathfinder"/> (grid A* over the physics world). Open
/// ground is a straight line — A* only kicks in when scenery is in the way — so direct
/// moves still feel snappy. Motion is applied with Rigidbody2D.MovePosition, so units
/// still physically collide with each other and the world.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PathAgent : MonoBehaviour
{
    [Header("Motion")]
    [Tooltip("World units per second. For the player this is copied from PlayerController2D; " +
             "for monsters, from MonsterAI — so tune speed there, not here.")]
    public float moveSpeed = 4f;
    [Tooltip("Stop this close (world units) to a fixed MoveTo destination.")]
    public float arriveRadius = 0.08f;

    [Header("Routing")]
    [Tooltip("Clearance kept from walls when routing — roughly half the body's width, " +
             "minus a little so it can still slip through one-tile gaps.")]
    public float agentRadius = 0.4f;
    [Tooltip("Seconds between re-routes while chasing a moving target.")]
    public float repathInterval = 0.35f;
    [Tooltip("Re-route early (before the interval) if the target drifts at least this far " +
             "from where we last routed to.")]
    public float repathThreshold = 0.6f;
    [Tooltip("Draw the active route in the Scene view when this object is selected.")]
    public bool drawPath = true;

    [Header("Unit avoidance (soft, Dota-style)")]
    [Tooltip("Steer around OTHER moving units within this radius (0 = off). Walls are handled " +
             "by routing; this is the gentle flow-around-each-other so units don't stack.")]
    public float avoidRadius = 0.6f;
    [Tooltip("How hard to push away from crowding units, relative to the path direction.")]
    public float avoidStrength = 0.6f;
    [Tooltip("One unit to NEVER avoid — the brain sets this to the current target so melee can " +
             "still reach it (monsters ignore the player; the player ignores the unit it attacks).")]
    public Transform avoidIgnore;

    /// <summary>Last-faced cardinal direction (for anything that aims from facing).</summary>
    public Vector2 Facing { get; private set; } = Vector2.down;

    /// <summary>True when there's no active destination (idle / arrived at a fixed point).</summary>
    public bool Arrived => !active;

    Rigidbody2D rb;
    static readonly List<Collider2D> avoidBuf = new List<Collider2D>(8);
    readonly List<Vector2> waypoints = new List<Vector2>();
    int wp;                 // index of the waypoint we're currently walking toward
    bool active;            // do we have a destination?
    bool stopOnArrive;      // MoveTo = stop within arriveRadius; MoveToward = caller stops us
    Vector2 lastRouteGoal;  // the goal we last ran A* against (for the drift test)
    float nextRepath;       // earliest time we'll re-route a chase

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        // CRITICAL: a Dynamic Rigidbody2D that falls asleep silently IGNORES MovePosition,
        // which would freeze the unit mid-route in open space. We move via MovePosition (which
        // doesn't build up linearVelocity), so without this the physics engine sleeps the body
        // the moment it briefly stops — and it never wakes. Keep it awake.
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
    }

    /// <summary>Walk to a fixed ground point, routing around walls, and stop on arrival.</summary>
    public void MoveTo(Vector2 point)
    {
        stopOnArrive = true;
        Repath(point);
        active = true;
    }

    /// <summary>Head toward a (possibly moving) target, re-routing periodically. The caller
    /// is responsible for stopping (e.g. once in attack range or at follow distance).</summary>
    public void MoveToward(Vector2 movingTarget)
    {
        stopOnArrive = false;
        if (!active
            || (movingTarget - lastRouteGoal).sqrMagnitude > repathThreshold * repathThreshold
            || Time.time >= nextRepath)
            Repath(movingTarget);
        active = true;
    }

    /// <summary>Cancel any movement immediately.</summary>
    public void Halt()
    {
        active = false;
        waypoints.Clear();
        wp = 0;
        if (rb != null) rb.linearVelocity = Vector2.zero;   // kill any drift from collisions
    }

    void Repath(Vector2 goal)
    {
        lastRouteGoal = goal;
        nextRepath = Time.time + repathInterval;

        var route = Pathfinder.FindPath(rb.position, goal, agentRadius);
        waypoints.Clear();
        if (route != null && route.Count > 0) waypoints.AddRange(route);
        else waypoints.Add(goal);   // no route (walled off): fall back to a straight line
        wp = 0;
    }

    void FixedUpdate()
    {
        if (!active) return;

        Vector2 pos = rb.position;

        // Reached the end of the route?
        if (wp >= waypoints.Count) { EndOfRoute(); return; }

        Vector2 target = waypoints[wp];
        Vector2 to = target - pos;
        float dist = to.magnitude;

        bool lastWp = wp == waypoints.Count - 1;
        // Snap through intermediate corners a little early so motion stays smooth; only the
        // final point of a MoveTo uses the tight arriveRadius.
        float reach = (lastWp && stopOnArrive) ? arriveRadius : Mathf.Max(arriveRadius, 0.12f);

        if (dist <= reach)
        {
            wp++;
            if (wp >= waypoints.Count) { EndOfRoute(); return; }
            target = waypoints[wp];
            to = target - pos;
            dist = to.magnitude;
            lastWp = wp == waypoints.Count - 1;
        }

        if (dist <= 1e-4f) return;

        Vector2 dir = to / dist;

        // Local avoidance (Dota "Short Pather") — skipped on the final approach to a fixed
        // destination so we head straight in and stop cleanly on the point.
        if (!(lastWp && stopOnArrive))
            dir = Steer(pos, dir);

        float step = moveSpeed * Time.fixedDeltaTime;
        if (lastWp && stopOnArrive) step = Mathf.Min(step, dist);   // never overshoot a fixed point

        rb.MovePosition(pos + dir * step);

        Facing = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
            ? new Vector2(Mathf.Sign(dir.x), 0f)
            : new Vector2(0f, Mathf.Sign(dir.y));
    }

    void EndOfRoute()
    {
        if (stopOnArrive) Halt();                       // arrived at the fixed destination
        else if (rb != null) rb.linearVelocity = Vector2.zero;  // chase: wait here for the next repath
    }

    // Local avoidance: blend the desired heading with (1) a radial push-off to unstack, and
    // (2) a TANGENTIAL slide around whoever is directly ahead — that second part is what breaks
    // conga-lines, so chasers fan out and surround instead of queuing. Skips self and the one
    // unit we're told to ignore (our target), so melee still reaches it.
    Vector2 Steer(Vector2 pos, Vector2 desired)
    {
        if (avoidRadius <= 0f || avoidStrength <= 0f) return desired;

        var filter = new ContactFilter2D { useTriggers = false };
        int n = Physics2D.OverlapCircle(pos, avoidRadius, filter, avoidBuf);

        Vector2 separation = Vector2.zero;
        Vector2 aheadDir = Vector2.zero;
        float nearestAhead = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            var body = avoidBuf[i] != null ? avoidBuf[i].attachedRigidbody : null;
            if (body == null || body == rb || body.bodyType != RigidbodyType2D.Dynamic) continue;
            if (avoidIgnore != null && body.transform == avoidIgnore) continue;

            Vector2 to = (Vector2)body.position - pos;
            float d = to.magnitude;
            if (d < 1e-4f) continue;
            Vector2 toDir = to / d;

            separation -= toDir * (1f - Mathf.Min(d / avoidRadius, 1f));   // push off, closer = harder

            // Remember the closest unit that's roughly in our way (within a ~60° cone ahead).
            if (Vector2.Dot(toDir, desired) > 0.5f && d < nearestAhead)
            {
                nearestAhead = d;
                aheadDir = toDir;
            }
        }

        Vector2 result = desired + separation * avoidStrength;

        // Slide around the blocker ahead: steer along the tangent, toward the side we were
        // already heading. This is the deflection that turns a queue into a spread.
        if (nearestAhead < float.MaxValue)
        {
            Vector2 tangent = new Vector2(-aheadDir.y, aheadDir.x);
            if (Vector2.Dot(tangent, desired) < 0f) tangent = -tangent;
            result += tangent * avoidStrength;
        }

        return result.sqrMagnitude > 1e-6f ? result.normalized : desired;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawPath || !active || waypoints.Count == 0) return;
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
        Vector3 prev = transform.position;
        for (int i = wp; i < waypoints.Count; i++)
        {
            Vector3 p = waypoints[i];
            Gizmos.DrawLine(prev, p);
            Gizmos.DrawWireSphere(p, 0.08f);
            prev = p;
        }
    }
}
