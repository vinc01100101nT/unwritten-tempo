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
    public static float MobRelative = 0.30f;

    /// <summary>Size+speed multiplier for the player.</summary>
    public static float ForPlayer => Global;
    /// <summary>Size+speed multiplier for mobs (global × mob-relative).</summary>
    public static float ForMob => Global * MobRelative;

    /// <summary>
    /// Blend factor for melee CONTACT distances between the player and a mob — the average of the two units'
    /// scales. A "reach" authored at full size (factor 1 when both are at authored scale) then shrinks as the
    /// units shrink, so melee stays glued to the resized bodies instead of swinging/biting across a visible gap.
    /// Multiply any world-space "close enough to touch" range by this so it self-corrects at any <see cref="Global"/>
    /// / <see cref="MobRelative"/> dial. Cheap (two reads + a multiply) — safe to call per-frame in the hot path.
    /// </summary>
    public static float MeleeReachFactor => 0.5f * (ForPlayer + ForMob);

    public static void SetGlobal(float v)      => Global      = Mathf.Clamp(v, 0.2f, 3f);
    public static void SetMobRelative(float v) => MobRelative = Mathf.Clamp(v, 0.2f, 3f);
}
