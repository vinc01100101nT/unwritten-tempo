using UnityEngine;

/// <summary>
/// The townsfolk animation driver: a live-3D character that only ever WALKS or STANDS. It adds nothing to
/// <see cref="Agent3DVisual"/> — walking, facing and foot-grounding are the shared behaviour every agent-driven
/// character performs identically, and a wandering townsperson has no further vocabulary (no attack/jump/dash/
/// pickup/hurt). The role that DOES add one is the 3D shadow mob (<see cref="MobShadow3DVisual"/>).
///
/// <para>Kept as its own type rather than collapsed into the base because <see cref="TownNPC"/> resolves it by type
/// to freeze it when far, and because "townsfolk visual" is the thing an author adds to a townsperson. Behaviour
/// lives in the base — do NOT re-implement locomotion here.</para>
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PathAgent3D))]
public class TownNPC3DVisual : Agent3DVisual
{
}
