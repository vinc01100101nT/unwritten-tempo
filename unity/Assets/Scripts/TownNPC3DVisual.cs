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
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PathAgent3D))]
public class TownNPC3DVisual : MonoBehaviour
{
    PathAgent3D agent;
    StanceController stance;
    Character3DPose pose;
    Vector3 lastPos;

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        stance = GetComponent<StanceController>();
        pose = GetComponent<Character3DPose>() ?? GetComponentInChildren<Character3DPose>();
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
    }
}
