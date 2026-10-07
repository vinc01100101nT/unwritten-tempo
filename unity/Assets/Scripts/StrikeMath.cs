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

    /// <summary>How much a sniper's magic shot grows for every metre to its target (decided 2026-10-06): 2%.</summary>
    public const float SniperGrowthPerMetre = 0.02f;

    /// <summary>What a sniper's magic shot is multiplied by at <paramref name="metres"/> from its target — 1 at point
    /// blank, +2% a metre.</summary>
    public static float SniperShotGrowth(float metres) => 1f + SniperGrowthPerMetre * Mathf.Max(0f, metres);

    /// <summary>The straight-line distance between two units' positions, in metres.</summary>
    public static float Metres(Vector3 from, Vector3 to) => Vector3.Distance(from, to);
}
