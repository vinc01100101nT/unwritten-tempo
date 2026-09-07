using UnityEngine;

/// <summary>
/// The animation driver for ANOTHER player's body — the person standing in your town who is being played on
/// somebody else's machine. It adds nothing to <see cref="Agent3DVisual"/> but the two facts that make its body
/// different from a townsperson's: it is PLACED rather than steered, and it is TOLD which way it faces.
///
/// <para><b>Why it exists at all.</b> Without a driver, a body built by <see cref="Character3DBody"/> holds the
/// authored Idle stance forever — the builder hands the stance choke point over to a driver and there was none, so
/// every remote player slid around town in a fixed standing pose. Walking, the walk/stand decision and the
/// foot-grounding are NOT re-implemented here: they are the shared behaviour in the base, the same one townsfolk
/// and shadow mobs run, which is the whole point of putting this here instead of inside the net layer.</para>
///
/// <para><b>No mover.</b> Every other role is moved by a <see cref="PathAgent3D"/> and asks it whether it is
/// walking. This body is moved by <see cref="NetPlayer"/> blending toward the last place the server reported, so
/// it has no mover to ask and the base reads its own footfall instead.</para>
/// </summary>
[DefaultExecutionOrder(-100)]
public class RemotePlayer3DVisual : Agent3DVisual
{
    /// <summary>Whichever way the server says this person is facing — which is the direction their body root has
    /// already been turned toward, so it is read straight off it rather than kept a second time.
    ///
    /// <para>Answered even while STANDING, which is the one thing this role does differently. Facing from the
    /// travel line would mean a person who stands their ground and turns to look at you never appears to turn at
    /// all, because they never went anywhere — and standing and turning is exactly what a person does when
    /// somebody walks up to them.</para></summary>
    protected override Vector3 FacingThisFrame(Vector3 groundTravel, bool walking, Vector3 position)
        => transform.forward;

    /// <summary>The PLAYER's three gears, because this body IS a player — somebody else's, but a player. The base's
    /// single-gear answer belongs to a strolling townsperson, and using it here would show a person sprinting
    /// across town at a walk. The sprint gear is taken from what their own machine SAID rather than guessed from
    /// how fast they are going, because a hasted run is just as fast and is not a sprint.</summary>
    protected override StanceId LocomotionStanceFor(float groundSpeed) => PlayersGearFor(groundSpeed, toldSprinting);

    /// <summary>EVERYTHING THIS PERSON'S BODY IS DOING THAT IS NOT PLAIN WALKING — a jump held in the air, a dodge
    /// roll, a flinch, a body lying dead, and anything added to the game later. It is decided on their own machine,
    /// which is the only one that knows they pressed the key, so it is handed over rather than worked out here.
    ///
    /// <para><b>Why this body needs an override at all when a creature's does not.</b> A creature's brain runs on the
    /// server, so the two poses it holds — dying and flinching — arrive as things this machine can read off the
    /// creature itself: its health, and the blow the server announced. A person's brain runs on THEIR keyboard, and
    /// nothing about a jump or a roll reaches anybody else unless it is said.</para>
    ///
    /// <para>The facing is applied here rather than left to the base, because the base only turns a body on the
    /// frames it is NOT holding a pose — and a person who jumps or rolls has to keep facing the way they are
    /// facing. Two comparisons and one call; nothing is looked up and nothing allocates.</para></summary>
    protected override bool DriveRoleAction()
    {
        if (!toldHasPose) return false;

        // Death has to beat a swing that was still running when the killing blow landed, or somebody dies mid-swing
        // and finishes the swing lying down. The one-shot beat layer outranks this one, so it is cancelled rather
        // than merely out-driven — the same rule a creature's body already plays by.
        if (toldPose == StanceId.Die) stance.CancelActionOverride();

        stance.DriveStance(toldPose, toldPoseWindow, toldPoseRelease);

        if (pose != null)
        {
            Vector3 face = transform.forward; face.y = 0f;
            if (face.sqrMagnitude > 1e-8f) pose.SetFacing(face.normalized);
        }
        return true;
    }
}
