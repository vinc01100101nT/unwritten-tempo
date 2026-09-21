using UnityEngine;

/// <summary>
/// THE ONE PLACE EVERY MASTERY NUMBER IS WORKED OUT — how much practice a kill is worth, what a level costs, and
/// what a level is worth once it is earned. Plain functions over plain numbers, so a whole career can be checked
/// by calling them rather than by playing one.
///
/// <para>Every value is the first table in the V4 design (docs/2-systems/gameplay-system-v4 §9), to be tuned in
/// play. Change it HERE; nothing else keeps a copy.</para>
/// </summary>
public static class MasteryMath
{
    // ---- what a level costs -------------------------------------------------------------------------------
    // WEAPON MASTERY: reaching level N costs N points, so level L in total costs L(L+1)/2 — 2,485 to reach 70.
    // SKILL MASTERY: the same shape, five times as steep in USES — 2,325 to reach 30.
    public const int SkillUsesPerLevelStep = 5;

    /// <summary>Total practice points a weapon mastery of <paramref name="level"/> has cost.</summary>
    public static float WeaponPointsFor(int level) => level <= 0 ? 0f : level * (level + 1) * 0.5f;

    /// <summary>The weapon mastery level <paramref name="points"/> of practice buys.</summary>
    public static int WeaponLevelFrom(float points)
    {
        if (points < 1f) return 0;
        // L(L+1)/2 <= points  →  L = floor((sqrt(8p+1) − 1) / 2)
        return Mathf.FloorToInt((Mathf.Sqrt(8f * points + 1f) - 1f) * 0.5f);
    }

    /// <summary>Total uses a skill mastery of <paramref name="level"/> has cost.</summary>
    public static float SkillUsesFor(int level)
        => level <= 0 ? 0f : SkillUsesPerLevelStep * level * (level + 1) * 0.5f;

    /// <summary>The skill mastery level <paramref name="uses"/> buys.</summary>
    public static int SkillLevelFrom(float uses) => WeaponLevelFrom(uses / SkillUsesPerLevelStep);

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

    // ---- what a kill is worth -----------------------------------------------------------------------------
    /// <summary>A creature far enough below you to teach nothing.</summary>
    public const int NothingBelow = 10;
    /// <summary>How far above your level a creature still counts as an even fight.</summary>
    public const int EvenAbove = 5;
    /// <summary>What each level above that adds, and the ceiling no single kill may pass.</summary>
    public const float PerLevelAbove = 0.1f, MostPerKill = 1.5f;

    /// <summary>How much of a full point of practice a fight with a creature <paramref name="victimLevel"/> is
    /// worth to a character of <paramref name="characterLevel"/>: nothing ten levels below, rising straight to a
    /// full point at their own level, a little more above it, and never past <see cref="MostPerKill"/>.</summary>
    public static float Challenge(int victimLevel, int characterLevel)
    {
        int d = victimLevel - characterLevel;
        if (d <= -NothingBelow) return 0f;
        if (d < 0) return (d + NothingBelow) / (float)NothingBelow;
        if (d <= EvenAbove) return 1f;
        return Mathf.Min(MostPerKill, 1f + (d - EvenAbove) * PerLevelAbove);
    }

    /// <summary>The practice one kill pays: the SHARE of the creature's health this weapon took off it, scaled by
    /// how challenging the fight was and by how fast the weapon in hand trains. A creature is worth one point in
    /// total however big it is, so a level makes a fight harder, never richer.</summary>
    public static float PracticeFromKill(int damageWithThisWeapon, int victimMaxHP,
                                         int victimLevel, int characterLevel, Quality weaponQuality)
    {
        if (damageWithThisWeapon <= 0 || victimMaxHP <= 0) return 0f;
        float share = Mathf.Clamp01(damageWithThisWeapon / (float)victimMaxHP);
        return share * Challenge(victimLevel, characterLevel) * CraftingMath.TrainingSpeed(weaponQuality);
    }

    /// <summary>What one completed press of a skill is worth toward that skill's mastery — one use, at the speed
    /// the weapon in hand trains.</summary>
    public static float PracticeFromUse(Quality weaponQuality) => CraftingMath.TrainingSpeed(weaponQuality);
}
