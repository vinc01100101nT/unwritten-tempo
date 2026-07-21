using UnityEngine;

/// <summary>
/// The NPC twin of <see cref="Character3DVisual"/>: drives a live-3D Quaternius character's animation from its
/// <see cref="PathAgent3D"/> motion instead of from player input. Moving → Walk, still → Idle, and yaw the model
/// toward its travel direction. It has NONE of the player's action vocabulary (no attack/jump/dash/pickup/hurt) —
/// a wandering townsperson only ever walks or stands.
///
/// <para>Reuse, not reinvention: it feeds the SAME choke point (<see cref="StanceController"/>) and the SAME render
/// sink (<see cref="Character3DPose"/>) the player uses, so a town character animates through identical machinery —
/// only the (tiny) decision differs. Move speed + gait cadence are the PLAYER's own values (read once at spawn by
/// <see cref="AmbientLife"/>, single source of truth), so a townsperson strolls exactly like the player walking.
/// Runs at −100 (before StanceController −50) so its intent is fed first, matching <see cref="Character3DVisual"/>.
/// All sibling refs are cached in Awake; nothing here allocates.</para>
///
/// <para>FOOT GROUNDING — shares the player's ground RAYCAST (<see cref="GroundSnap.TryGroundY"/>), the single
/// owner of "how high is the floor here". The SEATING then differs by rig, and correctly so: the player seats its
/// physics ROOT, offsetting for the CharacterController's skin width plus any jump/fall lift (see
/// <see cref="Character3DVisual.GroundedPos"/>); a townsperson has no CharacterController and never jumps, so it
/// simply pins the model's ORIGIN — the Quaternius pivot, authored at the feet — straight onto the ground hit.
/// One raycast per simulated NPC per frame, zero allocation; far NPCs don't run this (their
/// <see cref="TownNPC"/> LOD disables this driver).</para>
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PathAgent3D))]
public class TownNPC3DVisual : MonoBehaviour
{
    PathAgent3D agent;
    StanceController stance;
    Character3DPose pose;
    Transform model;              // the scaled visual model child (its origin is the feet pivot)
    int groundMask;
    Vector3 lastPos;

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        stance = GetComponent<StanceController>();
        pose = GetComponent<Character3DPose>() ?? GetComponentInChildren<Character3DPose>();
        model = pose != null ? pose.transform : null;
        int gl = LayerMask.NameToLayer("Ground");
        groundMask = gl >= 0 ? (1 << gl) : 0;
        lastPos = transform.position;
    }

    void LateUpdate()
    {
        if (stance == null) return;

        // Horizontal travel this frame (LateUpdate runs after PathAgent3D's move) → the facing direction.
        Vector3 p = transform.position;
        Vector3 d = p - lastPos; d.y = 0f;
        lastPos = p;

        if (agent != null && agent.Moving)
        {
            stance.DriveStance(StanceId.Walk);
            // Pass a UNIT direction, not the raw per-frame delta (~0.01 long): Character3DPose only yaws when the
            // facing's sqrMagnitude clears 1e-4, and a tiny delta sits below that gate so the model never turns.
            if (d.sqrMagnitude > 1e-8f && pose != null) pose.SetFacing(d.normalized);
        }
        else
        {
            // Fall through to the authored Idle stance (StanceController.selfDrive = false).
            stance.ClearAction();
        }

        // Seat the feet: share the player's ground raycast (GroundSnap), then pin the model origin (feet pivot) onto
        // the hit. The player instead seats its physics root (CharacterController + jump lift) — same raycast, different
        // rig, so only the raycast is shared, not the seating.
        if (model != null && GroundSnap.TryGroundY(transform.position, groundMask, out float groundY))
            model.position = new Vector3(model.position.x, groundY, model.position.z);
    }
}
