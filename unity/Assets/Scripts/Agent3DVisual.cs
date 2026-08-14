using UnityEngine;

/// <summary>
/// The shared animation driver for any live-3D character moved by a <see cref="PathAgent3D"/> — the AI twin of
/// <see cref="Character3DVisual"/> (which reads player input instead). It owns the three things EVERY agent-driven
/// character does identically: pick Walk vs Idle from the mover, yaw the model toward travel, and seat the feet on
/// the ground.
///
/// <para>Subclasses add only the vocabulary their role has, by overriding <see cref="DriveRoleAction"/>: a
/// townsperson has none (it only ever walks or stands — <see cref="TownNPC3DVisual"/>), a mob adds attack / hurt /
/// die (<see cref="MobShadow3DVisual"/>). The locomotion, facing and grounding below are NEVER re-implemented per
/// role — that split is what let the NPC and player footstep paths drift apart before, and this is the same
/// "one function per behaviour" contract applied to the visual layer.</para>
///
/// <para>Everything routes through the same choke point (<see cref="StanceController"/>) and render sink
/// (<see cref="Character3DPose"/>) the player uses, so an AI character animates through identical machinery.
/// Runs at −100 (before StanceController's −50) so its intent is fed first. Sibling refs cached in Awake;
/// nothing here allocates.</para>
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PathAgent3D))]
public abstract class Agent3DVisual : MonoBehaviour
{
    protected PathAgent3D agent;
    protected StanceController stance;
    protected Character3DPose pose;
    protected Transform model;    // the scaled visual model child (its origin is the feet pivot)
    int groundMask;
    Vector3 lastPos;
    bool seated;   // the feet have been placed at least once; a still unit then needs no further ground ray

    protected virtual void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        stance = GetComponent<StanceController>();
        pose = GetComponent<Character3DPose>() ?? GetComponentInChildren<Character3DPose>();
        model = pose != null ? pose.transform : null;
        int gl = LayerMask.NameToLayer("Ground");
        groundMask = gl >= 0 ? (1 << gl) : 0;
        lastPos = transform.position;
    }

    /// <summary>Coming back after a freeze (or a pooled reuse) resumes with a <see cref="lastPos"/> from wherever this
    /// unit stood when it stopped — and it may have been moved a long way in between. Re-baseline both facts: without
    /// the position reset the first frame back reads one enormous phantom step, which flashes the Run stance and snaps
    /// the model's facing along the jump; without clearing <see cref="seated"/> a unit recycled to a NEW spot would
    /// think its feet were already placed and never re-seat on the ground it actually landed on.</summary>
    protected virtual void OnEnable()
    {
        lastPos = transform.position;
        seated = false;
    }

    /// <summary>Re-resolve the pose sink after the body is built at runtime (the model child doesn't exist yet when
    /// Awake runs on a mob that becomes a shadow mid-spawn).</summary>
    public void BindBody(Character3DPose builtPose)
    {
        pose = builtPose;
        model = pose != null ? pose.transform : null;
        if (stance == null) stance = GetComponent<StanceController>();
        seated = false;   // a brand-new body has never been placed on the ground, even if this unit hasn't moved
    }

    /// <summary>Drive a role-specific action stance this frame (attack / hurt / die). Return true to claim the
    /// frame — locomotion and facing are then skipped, because the role action outranks walking. Default: no
    /// role vocabulary at all.</summary>
    protected virtual bool DriveRoleAction() => false;

    /// <summary>Which locomotion gear to play at this measured ground speed. Base = always Walk, because a
    /// strolling townsperson has exactly one gear. A shadow overrides it to pick between the player's Walk and Run
    /// gears, the same two the player has.</summary>
    protected virtual StanceId LocomotionStanceFor(float groundSpeed) => StanceId.Walk;

    protected virtual void LateUpdate()
    {
        if (stance == null) return;

        // Travel this frame (LateUpdate runs after PathAgent3D's move). `moved` keeps the VERTICAL component so a
        // falling unit still re-seats; `d` drops it, because facing is a ground direction.
        Vector3 p = transform.position;
        Vector3 moved = p - lastPos;
        Vector3 d = moved; d.y = 0f;
        lastPos = p;

        if (!DriveRoleAction())
        {
            if (agent != null && agent.Moving)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                stance.DriveStance(LocomotionStanceFor(d.magnitude / dt));
                // Pass a UNIT direction, not the raw per-frame delta (~0.01 long): Character3DPose only yaws when the
                // facing's sqrMagnitude clears 1e-4, and a tiny delta sits below that gate so the model never turns.
                if (d.sqrMagnitude > 1e-8f && pose != null) pose.SetFacing(d.normalized);
            }
            else
            {
                // Fall through to the authored Idle stance (StanceController.selfDrive = false).
                stance.ClearAction();
            }
        }

        // Seat the feet: share the player's ground raycast (GroundSnap — the single owner of "how high is the floor
        // here"), then pin the model origin (feet pivot) onto the hit. The player instead seats its physics root
        // (CharacterController + jump lift) — same raycast, different rig, so only the raycast is shared.
        //
        // ONLY WHEN THE UNIT ACTUALLY MOVED (or hasn't been seated yet). A standing unit's foot height is already
        // correct and the ground doesn't move, so re-casting for it was a physics ray per idle character per frame —
        // the exact cost GroundSnap's own contract says callers must avoid ("callers gate it to MOVING units... so a
        // still crowd costs nothing"). This caller was the one not honouring it, across a town of standing NPCs.
        if (model != null && (!seated || moved.sqrMagnitude > 1e-10f)
            && GroundSnap.TryGroundY(transform.position, groundMask, out float groundY))
        {
            model.position = new Vector3(model.position.x, groundY, model.position.z);
            seated = true;
        }
    }
}
