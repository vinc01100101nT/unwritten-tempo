using UnityEngine;

/// <summary>
/// Top-down movement SERVICE for click-to-move (Dota-style) control. It no longer
/// reads WASD — instead <see cref="PlayerCommander"/> tells it where to go:
///   • <see cref="MoveTo"/>     — walk to a fixed ground point, then stop.
///   • <see cref="MoveToward"/> — chase a moving target (the caller decides when to stop).
///   • <see cref="Halt"/>       — stop now.
///
/// Routing is now handled by a sibling <see cref="PathAgent"/>: when one is present (it's
/// auto-added in <see cref="Start"/>), these verbs delegate to it so the player walks AROUND
/// walls instead of bumping them, while <see cref="PlayerCommander"/> stays unchanged. If
/// no PathAgent exists this class still works as a plain straight-line mover (the old
/// behaviour — obstacles handled by physics, not routing), so nothing breaks.
///
/// This component also doubles as the canonical "this is the player" marker — other systems
/// (Portal, Character, MonsterAI, the spawner) locate the player by it, so keep it on the
/// player and never put it on a monster.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController2D : MonoBehaviour
{
    [Tooltip("World units per second.")]
    public float moveSpeed = 6f;

    [Tooltip("Stop this close (world units) to a fixed MoveTo destination.")]
    public float arriveRadius = 0.06f;

    /// <summary>Last-faced cardinal direction (defaults to down). Derived from actual
    /// motion; kept for anything that wants to aim from the player's facing.</summary>
    public Vector2 Facing => agent != null ? agent.Facing : facing;

    /// <summary>True when there's no active destination (idle / arrived).</summary>
    public bool Arrived => agent != null ? agent.Arrived : !hasDestination;

    Rigidbody2D rb;
    PathAgent agent;

    Vector2 facing = Vector2.down;
    Vector2 destination;
    bool hasDestination;
    bool stopOnArrive;     // MoveTo = true (stop within arriveRadius); MoveToward = false

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        agent = GetComponent<PathAgent>();
    }

    void Start()
    {
        // Give the player smart routing by default; carry our tuning onto the agent so
        // speed/arrive stay edited here (the historical home for them).
        bool added = agent == null;
        if (added) { agent = gameObject.AddComponent<PathAgent>(); agent.agentRadius = 0.4f; }
        agent.moveSpeed = moveSpeed;
        agent.arriveRadius = arriveRadius;

        // The player is DIRECTLY commanded — it must go exactly where you click, in a straight
        // line. Crowd avoidance is a MOB behaviour (so packs fan out around each other); left on
        // for the player it lets nearby mobs steer the heading, so the player visibly drifts /
        // curves as it passes a swarm. Turn it off here: player moves are now dead-straight and
        // completely unaffected by mobs (mobs already pass through us and can't push us).
        agent.avoidRadius = 0f;
    }

    /// <summary>Walk to a fixed ground point and stop on arrival.</summary>
    public void MoveTo(Vector2 point)
    {
        if (agent != null) { agent.MoveTo(point); return; }
        destination = point;
        hasDestination = true;
        stopOnArrive = true;
    }

    /// <summary>Continuously head toward a (possibly moving) target. The caller is
    /// responsible for stopping (e.g. once in attack range or at follow distance).</summary>
    public void MoveToward(Vector2 movingTarget)
    {
        if (agent != null) { agent.MoveToward(movingTarget); return; }
        destination = movingTarget;
        hasDestination = true;
        stopOnArrive = false;
    }

    /// <summary>Cancel any movement immediately.</summary>
    public void Halt()
    {
        if (agent != null) { agent.Halt(); return; }
        hasDestination = false;
        if (rb != null) rb.linearVelocity = Vector2.zero;   // kill any drift from collisions
    }

    void FixedUpdate()
    {
        if (agent != null) return;   // the PathAgent owns the stepping when present
        if (!hasDestination) return;

        Vector2 pos = rb.position;
        Vector2 to = destination - pos;
        float dist = to.magnitude;

        if (stopOnArrive && dist <= arriveRadius) { Halt(); return; }
        if (dist <= 1e-4f) return;

        Vector2 dir = to / dist;
        float step = moveSpeed * Time.fixedDeltaTime;
        if (stopOnArrive) step = Mathf.Min(step, dist);   // never overshoot a fixed point

        rb.MovePosition(pos + dir * step);

        // Remember the way we're facing (cardinal) for aiming.
        facing = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
            ? new Vector2(Mathf.Sign(dir.x), 0f)
            : new Vector2(0f, Mathf.Sign(dir.y));
    }
}
