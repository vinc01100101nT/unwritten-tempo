using UnityEngine;

/// <summary>
/// THE ONE PLACE EVERY MASTERY NUMBER IS WORKED OUT — what a kill pays a weapon mastery, what a level costs, and
/// what a level is worth once it is earned. Plain functions over plain numbers, so a whole career can be checked
/// by calling them rather than by playing one.
///
/// <para>Every value is the first table in the V4 design (docs/2-systems/gameplay-system-v4 §9), to be tuned in
/// play. Change it HERE; nothing else keeps a copy.</para>
/// </summary>
public static class MasteryMath
{
    // ---- what a level costs -------------------------------------------------------------------------------
    // WEAPON MASTERY: experience, earned like the character's and the ring's — TWO THIRDS of every kill's experience
    // goes to the weapon in hand (decided 2026-09-30), times its grade's speed — on the CHARACTER's own curve: mastery
    // M to M+1 costs what base level M+1 does (UnitStatMath.LevelCost), so mastery 0 stands where base level 1 does.
    // It goes on earning after the character stops at 99.
    // SKILL MASTERY: uses; reaching level N costs 5 × N uses, so 2,325 to reach 30.
    public const int SkillUsesPerLevelStep = 5;

    /// <summary>The scale of every kill's experience the weapon in hand is paid: two thirds, before its grade's speed.</summary>
    public const double WeaponShareOfExperience = 2.0 / 3.0;

    // No weapon grade lets mastery past 70, so nothing ever asks past it — the loops below are bounded by this, not
    // by what a corrupt save claims.
    const int HighestMasteryAsked = 79;

    /// <summary>Total experience a weapon mastery of <paramref name="level"/> has cost.</summary>
    public static double WeaponXpFor(int level)
        => level <= 0 ? 0.0 : level <= HighestMasteryAsked + 1 ? WeaponTotals[level] : CostOf(level);

    static double CostOf(int level)
    {
        double total = 0.0;
        for (int l = 1; l <= level; l++) total += UnitStatMath.LevelCost(l);
        return total;
    }

    // Every mastery level's total, worked out once: a weapon's level is read whenever its stats are, and walking the
    // curve's powers each time would be dozens of them per read.
    static readonly double[] WeaponTotals = BuildWeaponTotals();

    static double[] BuildWeaponTotals()
    {
        var totals = new double[HighestMasteryAsked + 2];
        for (int l = 1; l < totals.Length; l++) totals[l] = CostOf(l);
        return totals;
    }

    /// <summary>The weapon mastery level <paramref name="xp"/> of experience buys.</summary>
    public static int WeaponLevelFrom(double xp)
    {
        int level = 0;
        while (level < HighestMasteryAsked && xp >= WeaponTotals[level + 1]) level++;
        return level;
    }

    /// <summary>How far <paramref name="xp"/> is into its current weapon mastery level, and how much experience that
    /// level takes in all.</summary>
    public static void WeaponProgress(double xp, out double intoLevel, out double levelSize)
    {
        int level = WeaponLevelFrom(xp);
        double floor = WeaponXpFor(level);
        intoLevel = UnitStatMath.RoundExperience(System.Math.Max(0.0, xp - floor));
        levelSize = WeaponXpFor(level + 1) - floor;
    }

    /// <summary>A weapon's mastery experience saved on the curve before 2026-09-30 (<c>20 + 12L</c> per level, from 0),
    /// carried onto today's curve at the same level and the same share of the way through it. Read only by
    /// <see cref="Character.RestoreSave"/> for an older save.</summary>
    public static double CarryLegacyWeaponXp(double legacyXp)
    {
        if (legacyXp <= 0.0) return 0.0;
        int level = 0;
        double left = legacyXp;
        while (level < HighestMasteryAsked && left >= UnitStatMath.LegacyRingXpToNextFrom(level))
        {
            left -= UnitStatMath.LegacyRingXpToNextFrom(level);
            level++;
        }
        double share = System.Math.Min(1.0, left / UnitStatMath.LegacyRingXpToNextFrom(level));
        double floor = WeaponXpFor(level);
        return UnitStatMath.RoundExperience(floor + share * (WeaponXpFor(level + 1) - floor));
    }

    /// <summary>The weapon mastery experience a kill worth <paramref name="xp"/> pays: two thirds of it, at the speed the
    /// weapon in hand's grade trains — paid by the same rule as the character and the ring
    /// (<see cref="UnitStatMath.ShareOfKill"/>).</summary>
    public static double WeaponXpFromKill(int xp, Quality heldQuality)
        => UnitStatMath.ShareOfKill(xp, WeaponShareOfExperience * CraftingMath.TrainingSpeed(heldQuality));

    /// <summary>Total uses a skill mastery of <paramref name="level"/> has cost.</summary>
    public static float SkillUsesFor(int level)
        => level <= 0 ? 0f : SkillUsesPerLevelStep * level * (level + 1) * 0.5f;

    /// <summary>The skill mastery level <paramref name="uses"/> buys.</summary>
    public static int SkillLevelFrom(float uses)
    {
        float steps = uses / SkillUsesPerLevelStep;
        if (steps < 1f) return 0;
        // L(L+1)/2 <= steps  →  L = floor((sqrt(8s+1) − 1) / 2)
        return Mathf.FloorToInt((Mathf.Sqrt(8f * steps + 1f) - 1f) * 0.5f);
    }

    // ---- what a level is worth ----------------------------------------------------------------------------
    /// <summary>What ONE weapon mastery level adds to each of that weapon's three stats — +0.5%, so a level-70
    /// mastery is +35% on each.</summary>
    public const float WeaponPercentPerLevel = 0.005f;

    /// <summary>What ONE skill mastery level adds to that skill's output — +1%, so level 30 is +30%.</summary>
    public const float SkillPercentPerLevel = 0.01f;

    /// <summary>The most any mastery may lift a skill's output, whichever mastery it comes from.</summary>
    public const float MaxSkillLift = 0.30f;

    /// <summary>The percentage a weapon mastery of <paramref name="level"/> adds to each of its stats.</summary>
    public static float WeaponPercent(int level) => Mathf.Max(0, level) * WeaponPercentPerLevel;

    /// <summary>The lift a skill mastery of <paramref name="level"/> puts on that skill's output.</summary>
    public static float SkillLift(int level) => Mathf.Min(MaxSkillLift, Mathf.Max(0, level) * SkillPercentPerLevel);

    /// <summary>What a PASSIVE's output is lifted by. A passive cannot be used on purpose, so it earns no skill
    /// mastery of its own and rides the weapon's instead — half a percent a level, to the same ceiling.</summary>
    public static float PassiveLift(int weaponMasteryLevel)
        => Mathf.Min(MaxSkillLift, Mathf.Max(0, weaponMasteryLevel) * WeaponPercentPerLevel);

    /// <summary>What one completed press of a skill is worth toward that skill's mastery — one use, at the speed
    /// the weapon in hand trains.</summary>
    public static float PracticeFromUse(Quality weaponQuality) => CraftingMath.TrainingSpeed(weaponQuality);
}
