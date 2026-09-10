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
    /// <summary>Everything only THIS body knows, which is very little: the server has already turned its root to
    /// the direction it says this person is facing, so that is read straight off the root rather than kept a second
    /// time — and answered even while STANDING, which is the one thing this kind does differently. Facing from the
    /// travel line would mean a person who stands their ground and turns to look at you never appears to turn at
    /// all, because they never went anywhere, and standing and turning is exactly what a person does when somebody
    /// walks up to them.
    ///
    /// <para>Everything else — the pose, the gear, the flinch, the death, the turn — is the shared
    /// <see cref="CharacterBrain"/>, run once on the machine that actually knows and arriving here as its verdict.
    /// This type used to carry its own copy of three of those rules.</para></summary>
    protected override void DescribeSelf(ref CharacterBrain.Intent it) => it.facing = transform.forward;
}
