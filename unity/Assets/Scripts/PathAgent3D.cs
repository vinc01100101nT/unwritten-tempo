using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable 3D mover for the 2.5D world.
/// Routes on the XZ plane via <see cref="Pathfinder3D"/> and walks through a CharacterController
/// (so it slides along props). Mirrors PathAgent's verbs so a brain like <see cref="MonsterAI3D"/>
/// drives it identically:
///   • <see cref="MoveTo"/>     — route to a fixed point, stop on arrival.
///   • <see cref="MoveToward"/> — chase a moving target, re-routing on a cadence.
///   • <see cref="Halt"/>       — stop now.
/// Exposes <see cref="Facing"/> + <see cref="Moving"/> so <see cref="BillboardAnimator3D"/> can
/// animate from the motion. (The player keeps its own routing for now; this is for mobs.)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PathAgent3D : MonoBehaviour
{
    /// <summary>The live travel speed — written every frame by whoever drives this walker (a creature's brain from the
    /// character gears, a townsperson from the walk gear). Never typed, so never saved.</summary>
    [System.NonSerialized] public float moveSpeed = 3f;
    public float agentRadius = 0.35f;
    public float arriveRadius = 0.1f;
    /// <summary>Fall rate — READ from the world's one owner, the same constant the player's controller applies.</summary>
    public float gravity => GroundElevation.Gravity;
    [Tooltip("Seconds between re-routes while chasing a moving target.")]
    public float repathInterval = 0.35f;
    [Tooltip("Re-route early if the chase target drifts at least this far from the last route goal.")]
    public float repathThreshold = 0.6f;
    [Tooltip("A* cell budget per route. Mobs only chase a few units, so a small cap keeps a blocked " +
             "goal from burning the player's full search budget. ~2500 cells ≈ a 25×25-unit area.")]
    public int maxPathExpansions = 2500;

    [Header("Crowd avoidance (local separation so units don't stack)")]
    [Tooltip("Spread away from other units within this radius (~a body). 0 = off.")]
    public float avoidRadius = 0.8f;
    [Tooltip("How hard to push off / slide around crowders. 0 = off.")]
    public float avoidStrength = 0.9f;
    [Tooltip("Layers scanned for crowders — set to the Crowd layer by MonsterAI3D.")]
    public LayerMask avoidMask;
    [Tooltip("One unit to NOT spread away from — the chase target (player) — so melee still reaches it.")]
    public Transform avoidIgnore;
    [Tooltip("While IDLE (no active path) keep drifting apart from nearby crowd units. Mobs want this so a halted " +
             "cluster un-stacks; ambient townsfolk DON'T — with it on, a paused NPC slides away when the player " +
             "stands beside it (it's on the Crowd layer too). Off = a halted agent holds its ground (gravity only).")]
    public bool separateWhenIdle = true;

    public Vector2 Facing { get; private set; } = Vector2.down;   // cardinal (x, z)
    /// <summary>The last direction this agent travelled on the ground, in WORLD terms (x, z). Unlike
    /// <see cref="Facing"/> it depends on no camera, so it is the one a server can send to the machines watching,
    /// which each turn it into a picture for their own camera.</summary>
    public Vector2 Heading { get; private set; } = Vector2.down;
    public bool Moving { get; private set; }
    public bool Arrived => !active;

    CharacterController cc;
    readonly List<Vector2> path = new List<Vector2>();
    int wp;
    bool active, stopOnArrive;
    Vector2 lastGoal;
    float nextRepath;

    // Knockback impulse (an AoE shove, e.g. the Swordsman's Warcry). Overrides pathing while it lasts.
    Vector3 knockbackVel;
    float knockbackDecel, knockbackUntil;

    // Dash glide (a skill relocation): a fixed A→B slide that overrides pathing, twin of the player's BeginGlideTo.
    bool gliding, glideEaseIn, glideEaseOut;
    /// <summary>True while this agent is travelling a fixed A→B glide (a Dash effect) rather than pathing. The mob
    /// twin of the player's SkillGliding, so a presentation effect can last exactly one hop on either.</summary>
    public bool Gliding => gliding;
    Vector3 glideDest;
    float glideSpeed, glideTotalDist, glideEaseSeconds, glideUntil;

    static readonly Collider[] avoidBuf = new Collider[16];   // reused overlap buffer (no per-frame alloc)

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        // A CharacterController rests with its capsule bottom skinWidth ABOVE the floor. For these small
        // billboard units the capsule geometry makes the sprite's feet float by exactly that skinWidth, so
        // Unity's default 0.08 leaves mobs/NPCs "obviously elevated" while the player (own tuned controller)
        // looks planted. Shrink it so the feet sit on the ground. Reads the single owner constant so every
        // unit shares one skin-width value (see CharacterSpec.capsuleSkinWidth).
        cc.skinWidth = CharacterSpec.Active.capsuleSkinWidth;
        // THE SAME LEGS AS THE PLAYER: the one walking rule for step and slope, not whatever a baked prefab carried.
        GroundElevation.ApplyWalkRules(cc);
    }

    /// <summary>Shove this agent in <paramref name="worldDir"/> at <paramref name="speed"/>, easing out
    /// to a stop over <paramref name="seconds"/>. Cancels any current path (the caller should also stagger
    /// the brain so it doesn't immediately re-route). The slide goes through the CharacterController, so it
    /// still collides with props/terrain rather than phasing through them.</summary>
    public void Knockback(Vector3 worldDir, float speed, float seconds)
    {
        worldDir.y = 0f;
        if (worldDir.sqrMagnitude < 1e-6f || speed <= 0f || seconds <= 0f) return;
        knockbackVel = worldDir.normalized * speed;
        // hits 0 right at the end → an ease-out shove
        knockbackDecel = speed / seconds;
        knockbackUntil = Time.time + seconds;
        Halt();
        // SAID TO EVERY SCREEN WATCHING, once, as the push begins — a creature's legs are the server's, so the server
        // says it. Silent anywhere else.
        GameNetwork.AnnounceMoveOnMob(gameObject, new MoveSaid
            { kind = MoveSaid.Knockback, direction = knockbackVel, speed = speed, window = seconds });
    }

    /// <summary>Blink this agent straight to <paramref name="dest"/> — no travel. The CharacterController is toggled
    /// off across the reposition so the capsule doesn't fight it (same disable/move/enable the player's teleport uses),
    /// and the current path is dropped so the brain re-routes from where it now stands.</summary>
    public void Teleport(Vector3 dest, RelocationLook look = default)
    {
        knockbackUntil = 0f;
        Halt();
        if (cc != null) cc.enabled = false;
        transform.position = dest;
        if (cc != null) cc.enabled = true;
        GameNetwork.AnnounceMoveOnMob(gameObject, new MoveSaid { kind = MoveSaid.Blink, to = dest, look = look });
    }

    /// <summary>Glide from here to <paramref name="dest"/> at <paramref name="speed"/> world-units/second — a fixed
    /// A→B move that overrides pathing until it arrives (or its safety window lapses). The mob twin of the player's
    /// dash glide, so a Dash effect reads the same on either.</summary>
    /// <summary>A world point this unit keeps FACING for the whole glide, instead of turning toward its travel
    /// direction. Read by <see cref="Agent3DVisual"/>; null when the glide authored none. Same rule the player's
    /// controller follows, so a mob's dash-and-strike also lands looking at what it is about to hit.</summary>
    public Vector3? GlideFaceAt { get; private set; }

    public void GlideTo(Vector3 dest, float speed, bool easeIn, bool easeOut, float easeSeconds, Vector3? faceAt = null,
                        RelocationLook look = default)
    {
        Vector3 to = dest - transform.position; to.y = 0f;
        float dist = to.magnitude;
        if (dist < 0.05f || speed <= 0f) return;
        GlideFaceAt = faceAt;
        Halt();
        knockbackUntil = 0f;
        glideDest = new Vector3(dest.x, transform.position.y, dest.z);
        glideSpeed = speed;
        glideTotalDist = dist;
        glideEaseIn = easeIn; glideEaseOut = easeOut;
        glideEaseSeconds = Mathf.Max(0.05f, easeSeconds);
        // Safety window: a glide blocked by a wall must not hold the agent hostage. Generous enough that easing
        // (which slows the trip) can't clip a legitimate long dash short.
        glideUntil = Time.time + dist / speed * 3f + 0.5f;
        gliding = true;
        // SAID TO EVERY SCREEN WATCHING, once, as the trip begins — a creature's legs are the server's, so the server
        // says it. Silent anywhere else.
        GameNetwork.AnnounceMoveOnMob(gameObject, new MoveSaid
        {
            kind = MoveSaid.Glide, to = glideDest, speed = glideSpeed, easeIn = glideEaseIn, easeOut = glideEaseOut,
            easeSeconds = glideEaseSeconds, window = glideUntil - Time.time, look = look,
        });
    }

    public void MoveTo(Vector3 worldPoint)
    {
        stopOnArrive = true;
        Repath(new Vector2(worldPoint.x, worldPoint.z));
        active = true;
    }

    public void MoveToward(Vector3 movingTarget)
    {
        stopOnArrive = false;
        Vector2 g = new Vector2(movingTarget.x, movingTarget.z);
        if (!active || (g - lastGoal).sqrMagnitude > repathThreshold * repathThreshold || Time.time >= nextRepath)
            Repath(g);
        active = true;
    }

    public void Halt() { active = false; path.Clear(); wp = 0; Moving = false; }

    void Repath(Vector2 goal)
    {
        lastGoal = goal;
        nextRepath = Time.time + repathInterval;
        var from = new Vector2(transform.position.x, transform.position.z);
        // Fills `path` in place (allocation-free); false = stuck → fall back to a straight line.
        if (!Pathfinder3D.FindPath(from, goal, agentRadius, path, maxPathExpansions))
            { path.Clear(); path.Add(goal); }
        wp = 0;
    }

    void Update()
    {
        // The CharacterController is momentarily disabled while a mob is dead/fading (DeathDissolve) and during
        // respawn/teleport repositioning. Never call Move() on an inactive controller — Unity logs "Move called
        // on inactive controller" every frame per mob otherwise. Mirrors the guard in Player3DController.Update.
        // NOTE: check activeInHierarchy too — SetActive(false) (mass mob despawn on a scene/portal transition)
        // does NOT clear cc.enabled, so a final Update tick that frame would otherwise still call Move().
        if (cc == null || !cc.enabled || !cc.gameObject.activeInHierarchy) { Moving = false; return; }

        // A knockback impulse overrides pathing: slide (decelerating) until the window ends, then the
        // brain (un-staggered) resumes routing on its next tick.
        if (Time.time < knockbackUntil)
        {
            cc.Move((knockbackVel + Vector3.down * gravity) * Time.deltaTime);
            knockbackVel = GlideEase.KnockbackSlowed(knockbackVel, knockbackDecel, Time.deltaTime);
            Moving = false;
            return;
        }

        // A dash glide also overrides pathing: travel straight to the destination, then hand control back.
        if (gliding)
        {
            Vector3 toDest = glideDest - transform.position; toDest.y = 0f;
            float left = toDest.magnitude;
            if (left <= 0.05f || Time.time >= glideUntil) { gliding = false; Moving = false; GlideFaceAt = null; }
            else
            {
                Vector3 g = toDest / left;
                Facing = Facing3D.FromWorldDir(g);
                Heading = new Vector2(g.x, g.z);
                cc.Move((g * GlideSpeed(left) + Vector3.down * gravity) * Time.deltaTime);
                Moving = true;
                return;
            }
        }

        Vector3 dir = Vector3.zero;
        bool moving = false;

        if (active && wp < path.Count)
        {
            Vector3 to = WaypointDelta(wp);
            bool last = wp == path.Count - 1;
            float reach = (last && stopOnArrive) ? arriveRadius : Mathf.Max(arriveRadius, 0.12f);
            if (to.sqrMagnitude <= reach * reach)
            {
                wp++;
                if (wp >= path.Count) { if (stopOnArrive) Halt(); }   // chase: idle here until next repath
                else to = WaypointDelta(wp);
            }
            if (active && wp < path.Count && to.sqrMagnitude > 1e-4f) { dir = to.normalized; moving = true; }
        }

        // Move (with a constant downward pull to stay grounded; CharacterController slides on props).
        // Blend in crowd separation: while chasing, bend the heading; while halted, still drift apart so a
        // cluster un-stacks (units pass through each other now, so steering is what keeps them spread).
        if (moving)
        {
            dir = Steer(transform.position, dir);
            Facing = Facing3D.FromWorldDir(dir);
            if (dir.sqrMagnitude > 1e-6f) Heading = new Vector2(dir.x, dir.z);
            cc.Move((dir * moveSpeed + Vector3.down * gravity) * Time.deltaTime);
        }
        else
        {
            // Separation-only (no desired heading) — but only if this agent un-stacks while idle. Townsfolk turn
            // this off so a paused NPC isn't shoved by the player standing next to it (the idle-slide bug).
            Vector3 push = separateWhenIdle ? Steer(transform.position, Vector3.zero) : Vector3.zero;
            if (push.sqrMagnitude > 1e-4f)
                cc.Move((push * moveSpeed * 0.6f + Vector3.down * gravity) * Time.deltaTime);
            else
                cc.Move(Vector3.down * gravity * Time.deltaTime);
        }

        Moving = moving;
    }

    // The glide's speed this frame, shaped by the ease flags: ramp up over the first easeSeconds' worth of distance,
    // ramp down over the last. Distance-based (not a clock) so the shape holds however the trip is interrupted; on a
    // dash shorter than both ramps they simply overlap and the whole hop is gentle. Floored at a tenth so an eased
    // glide can never stall short of its destination.
    float GlideSpeed(float distLeft)
        => GlideEase.Speed(glideSpeed, distLeft, glideTotalDist, glideEaseIn, glideEaseOut, glideEaseSeconds);

    // Local crowd avoidance (ported from the 2D PathAgent): blend the desired heading with a radial push-off
    // from nearby units plus a tangential slide around whoever is dead ahead, so chasers fan out and surround
    // instead of stacking. One OverlapSphere per agent per frame (reused buffer, allocation-free). With a
    // zero `desired` it returns a pure separation direction (used to un-stack a halted cluster).
    Vector3 Steer(Vector3 pos, Vector3 desired)
    {
        if (avoidRadius <= 0f || avoidStrength <= 0f || avoidMask.value == 0) return desired;

        int n = Physics.OverlapSphereNonAlloc(pos, avoidRadius, avoidBuf, avoidMask);
        Vector3 separation = Vector3.zero, aheadDir = Vector3.zero;
        float nearestAhead = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            var col = avoidBuf[i];
            if (col == null) continue;
            Transform t = col.transform;
            if (t == transform) continue;                            // self
            if (avoidIgnore != null && t == avoidIgnore) continue;   // the player we're chasing

            Vector3 to = t.position - pos; to.y = 0f;
            float d = to.magnitude;
            if (d < 1e-4f) continue;
            Vector3 toDir = to / d;

            separation -= toDir * (1f - Mathf.Min(d / avoidRadius, 1f));   // push off, closer = harder

            // Remember the closest unit roughly in our way (within a ~60° cone ahead).
            if (Vector3.Dot(toDir, desired) > 0.5f && d < nearestAhead) { nearestAhead = d; aheadDir = toDir; }
        }

        Vector3 result = desired + separation * avoidStrength;

        // Slide around the blocker ahead — the deflection that turns a queue into a spread.
        if (nearestAhead < float.MaxValue)
        {
            Vector3 tangent = new Vector3(-aheadDir.z, 0f, aheadDir.x);
            if (Vector3.Dot(tangent, desired) < 0f) tangent = -tangent;
            result += tangent * avoidStrength;
        }

        result.y = 0f;
        return result.sqrMagnitude > 1e-6f ? result.normalized : desired;
    }

    Vector3 WaypointDelta(int i)
    {
        Vector3 d = new Vector3(path[i].x, transform.position.y, path[i].y) - transform.position;
        d.y = 0f;
        return d;
    }
}
