using UnityEngine;

/// <summary>
/// Global unit-size dial for the 2.5D world, plus the melee-contact blend between two bodies.
///
/// <para>Two knobs:
///   • <see cref="Global"/>      — scales the player's size + speed, and the character height every creature's size
///     is a multiple of (<see cref="MobSizes"/>).
///   • <see cref="MobRelative"/> — an extra SPEED multiplier on billboard mobs only. It no longer sizes anything: a
///     creature's size is its own (<see cref="MobSizes"/>). It is also the stand-in body (<see cref="MobScale"/>)
///     for a melee blend that has no target to measure against.</para>
///
/// The player reads these every frame (<see cref="Player3DController"/> recomputes its <c>baseScale</c> from a stored
/// build scale), and the movers apply the multiplier where they read speed (<see cref="MonsterAI3D"/>, the player).
/// </summary>
public static class UnitScale
{
    /// <summary>Scales every unit's size + speed. 1 = authored size.</summary>
    public static float Global = 1f;
    /// <summary>Extra SPEED multiplier on billboard mobs only. 1 = no change.</summary>
    public static float MobRelative = 0.30f;

    /// <summary>Size+speed multiplier for the player.</summary>
    public static float ForPlayer => Global;
    /// <summary>Speed multiplier for billboard mobs (global × mob-relative).</summary>
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

    /// <summary>The stand-in body for a melee blend with NO target to measure against (a ground-aimed search, the
    /// editor gizmo, a body with no brain) — the value every sprite mob had before sizes, so those searches did not
    /// widen. Every blend against a real creature reads that creature's size instead.</summary>
    public static float MobScale => ForMob;

    /// <summary>The blend of a character against the stand-in body (<see cref="MobScale"/>), for callers with no target.
    /// Derived from <see cref="MeleeBlend"/> rather than re-typed, so it can never disagree with a pairwise check.</summary>
    public static float MeleeReachFactor => MeleeBlend(CharacterScale, MobScale);

    public static void SetGlobal(float v)      => Global      = Mathf.Clamp(v, 0.2f, 3f);
    public static void SetMobRelative(float v) => MobRelative = Mathf.Clamp(v, 0.2f, 3f);
}
