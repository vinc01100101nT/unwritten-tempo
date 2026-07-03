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
    public float moveSpeed = 3f;
    public float agentRadius = 0.35f;
    public float arriveRadius = 0.1f;
    public float gravity = 12f;
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

    public Vector2 Facing { get; private set; } = Vector2.down;   // cardinal (x, z)
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

    static readonly Collider[] avoidBuf = new Collider[16];   // reused overlap buffer (no per-frame alloc)

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        // A CharacterController rests with its capsule bottom skinWidth ABOVE the floor. For these small
        // billboard units the capsule geometry makes the sprite's feet float by exactly that skinWidth, so
        // Unity's default 0.08 leaves mobs/NPCs "obviously elevated" while the player (own tuned controller)
        // looks planted. Shrink it so the feet sit on the ground. Reads the single owner constant so every
        // unit shares one skin-width value (see Player3DController.DefaultCapsuleSkinWidth).
        cc.skinWidth = Player3DController.DefaultCapsuleSkinWidth;
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
        knockbackDecel = speed / seconds;        // hits 0 right at the end → an ease-out shove
        knockbackUntil = Time.time + seconds;
        Halt();
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
            knockbackVel = Vector3.MoveTowards(knockbackVel, Vector3.zero, knockbackDecel * Time.deltaTime);
            Moving = false;
            return;
        }

        Vector3 dir = Vector3.zero;
        bool moving = false;

        if (active && wp < path.Count)
        {
            Vector3 to = WaypointDelta(wp);
            bool last = wp == path.Count - 1;
            float reach = (last && stopOnArrive) ? arriveRadius : Mathf.Max(arriveRadius, 0.12f);
            if (to.magnitude <= reach)
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
            cc.Move((dir * moveSpeed + Vector3.down * gravity) * Time.deltaTime);
        }
        else
        {
            Vector3 push = Steer(transform.position, Vector3.zero);   // separation-only (no desired heading)
            if (push.sqrMagnitude > 1e-4f)
                cc.Move((push * moveSpeed * 0.6f + Vector3.down * gravity) * Time.deltaTime);
            else
                cc.Move(Vector3.down * gravity * Time.deltaTime);
        }

        Moving = moving;
    }

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
