using UnityEngine;

/// <summary>How much a skill strike's damage multiplier GROWS by — the arithmetic behind
/// <see cref="SkillEffect.damageMultPerPointGivenUp"/> and <see cref="SkillEffect.damageMultPerMetre"/>. Plain numbers
/// in, plain numbers out, so it is pinned by the game-maths checks without standing the game up.</summary>
public static class StrikeMath
{
    /// <summary>What a strike's multiplier grows by: <paramref name="perPoint"/> for every point of MP or HP given up,
    /// plus <paramref name="perMetre"/> for every metre to the unit struck. Nothing given up and no distance = 0.</summary>
    public static float Growth(float perPoint, int givenUp, float perMetre, float metres) =>
        perPoint * Mathf.Max(0, givenUp) + perMetre * Mathf.Max(0f, metres);

    /// <summary>A release-early skill let go the instant its wind-up starts (decided 2026-10-23): 0.30× its damage.</summary>
    public const float ReleaseScaleAtStart = 0.30f;
    /// <summary>A release-early skill whose wind-up filled completely (decided 2026-10-23): 1.50× its damage.</summary>
    public const float ReleaseScaleAtFull = 1.50f;

    /// <summary>What a release-early skill's damage is multiplied by when let go at <paramref name="releasedAt"/> of a
    /// wind-up that began at <paramref name="startedAt"/> and lasts <paramref name="windupSeconds"/>: evenly from
    /// <see cref="ReleaseScaleAtStart"/> to <see cref="ReleaseScaleAtFull"/> across the wind-up, held at either end.</summary>
    public static float ReleaseScale(float startedAt, float releasedAt, float windupSeconds)
    {
        if (windupSeconds <= 0f) return ReleaseScaleAtFull;
        float filled = Mathf.Clamp01((releasedAt - startedAt) / windupSeconds);
        return Mathf.Lerp(ReleaseScaleAtStart, ReleaseScaleAtFull, filled);
    }

    /// <summary>How much a sniper's magic shot grows for every metre to its target (decided 2026-10-06): 2%.</summary>
    public const float SniperGrowthPerMetre = 0.02f;

    /// <summary>What a sniper's magic shot is multiplied by at <paramref name="metres"/> from its target — 1 at point
    /// blank, +2% a metre.</summary>
    public static float SniperShotGrowth(float metres) => 1f + SniperGrowthPerMetre * Mathf.Max(0f, metres);

    /// <summary>The most points of hit chance above 100 that a strike's accuracy growth counts (decided 2026-10-21):
    /// 100, so the growth has a ceiling like every other power source.</summary>
    public const int MaxAccuracyOverPoints = 100;

    /// <summary>How many points of hit chance above 100 an attacker has against a defender — the accuracy an ordinary
    /// attack would WASTE, because a hit chance is held at 100%. <paramref name="rawHitPoints"/> is the unclamped
    /// HitBase + HIT − FLEE (<see cref="Combat.RawHitPoints"/>). Counted at most <see cref="MaxAccuracyOverPoints"/>.</summary>
    public static int AccuracyOverPoints(int rawHitPoints) =>
        Mathf.Clamp(rawHitPoints - 100, 0, MaxAccuracyOverPoints);

    /// <summary>A strike's extra crit chance: <paramref name="always"/>, plus <paramref name="vsSlowed"/> when the unit
    /// struck moves slower than its normal pace (<paramref name="targetMoveMult"/> below 1). Never negative.</summary>
    public static float CritBonus(float always, float vsSlowed, float targetMoveMult) =>
        Mathf.Max(0f, always) + (targetMoveMult < 1f ? Mathf.Max(0f, vsSlowed) : 0f);

    /// <summary>How many points of movement-speed bonus a caster has right now, as a percent (a ×1.45 pace = 45),
    /// moving or standing still (the user, 2026-10-24). Never negative: a slowed caster gets nothing, not a penalty.
    /// It stops by itself at the speed cap (<see cref="UnitStatMath.MaxMoveMult"/>).</summary>
    public static float SpeedBonusPoints(float moveMult) => Mathf.Max(0f, moveMult - 1f) * 100f;

    /// <summary>The straight-line distance between two units' positions, in metres.</summary>
    public static float Metres(Vector3 from, Vector3 to) => Vector3.Distance(from, to);
}
