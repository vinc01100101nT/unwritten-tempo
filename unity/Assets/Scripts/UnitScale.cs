using UnityEngine;

/// <summary>
/// Global unit-size dial for the 2.5D world. Every unit renders at <c>buildScale × multiplier</c> and moves
/// at <c>baseSpeed × multiplier</c>, so shrinking a unit also slows it proportionally — it crosses the same
/// number of body-lengths per second and keeps the same "feel" at any size.
///
/// <para>Two knobs:
///   • <see cref="Global"/>      — scales ALL units (player + mobs) together.
///   • <see cref="MobRelative"/> — an extra multiplier on MOBS only, to match their on-screen size to the
///     player, so this can default &lt; 1 when the mob art reads larger on screen than the player.</para>
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
    /// Blend factor for a melee CONTACT distance between TWO SPECIFIC fighters — the average of THEIR OWN scales. A
    /// "reach" authored at full size then shrinks as the pair shrinks, so melee stays glued to the resized bodies
    /// instead of swinging across a visible gap.
    ///
    /// <para>PASS BOTH FIGHTERS' ACTUAL SCALES. The bug this shape prevents: each side used to bake in its own guess at
    /// the blend, so a shadow mob (character-sized) and the player (also character-sized) computed 1.00 and 0.65 for
    /// the very same pairing — the shadow out-reached the player by 54% against an identical body. A blend is a fact
    /// about a PAIR; it cannot be stored on one side.</para>
    ///
    /// Cheap (two reads + a multiply) — safe to call per-frame in the hot path.
    /// </summary>
    public static float MeleeBlend(float scaleA, float scaleB) => 0.5f * (scaleA + scaleB);

    /// <summary>Size of a unit built to the shared <see cref="CharacterSpec"/> body — the player, the townsfolk, and a
    /// live-3D shadow mob. They are the same body, so they are the same size.</summary>
    public static float CharacterScale => ForPlayer;

    /// <summary>Size of an ordinary billboard mob, which is shrunk by <see cref="MobRelative"/>.</summary>
    public static float MobScale => ForMob;

    /// <summary>The blend for the COMMON case — a character against an ordinary sprite mob. Derived from
    /// <see cref="MeleeBlend"/> rather than re-typed, so it can never disagree with a pairwise check.</summary>
    public static float MeleeReachFactor => MeleeBlend(CharacterScale, MobScale);

    public static void SetGlobal(float v)      => Global      = Mathf.Clamp(v, 0.2f, 3f);
    public static void SetMobRelative(float v) => MobRelative = Mathf.Clamp(v, 0.2f, 3f);
}
