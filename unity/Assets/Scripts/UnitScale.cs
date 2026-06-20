using UnityEngine;

/// <summary>
/// Global unit-size dial for the 2.5D world. Every unit renders at <c>buildScale × multiplier</c> and moves
/// at <c>baseSpeed × multiplier</c>, so shrinking a unit also slows it proportionally — it crosses the same
/// number of body-lengths per second and keeps the same "feel" at any size.
///
/// <para>Two knobs (live-tunable in the P ▸ DevSettings panel):
///   • <see cref="Global"/>      — scales ALL units (player + mobs) together.
///   • <see cref="MobRelative"/> — an extra multiplier on MOBS only, to match their on-screen size to the
///     player (the Sscary character renders smaller than the NinjaAdventure mobs, so this defaults &lt; 1).</para>
///
/// Units read these every frame — <see cref="Player3DController"/> + <see cref="BillboardAnimator3D"/> recompute
/// their <c>baseScale</c> from a stored build scale (so the idle-breathe squash still works), and the movers
/// apply the multiplier where they read speed (<see cref="MonsterAI3D"/>, the player). No registration/events —
/// new spawns and respawns pick it up automatically because each unit recomputes from its own build values.
/// </summary>
public static class UnitScale
{
    /// <summary>Scales every unit's size + speed. 1 = authored size.</summary>
    public static float Global = 1f;
    /// <summary>Extra size+speed multiplier on mobs only (match mob height to the player). 1 = no change.</summary>
    public static float MobRelative = 0.42f;

    /// <summary>Size+speed multiplier for the player.</summary>
    public static float ForPlayer => Global;
    /// <summary>Size+speed multiplier for mobs (global × mob-relative).</summary>
    public static float ForMob => Global * MobRelative;

    public static void SetGlobal(float v)      => Global      = Mathf.Clamp(v, 0.2f, 3f);
    public static void SetMobRelative(float v) => MobRelative = Mathf.Clamp(v, 0.2f, 3f);
}
