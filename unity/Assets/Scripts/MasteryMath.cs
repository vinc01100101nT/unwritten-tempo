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
    // WEAPON MASTERY: experience, earned like the character's and the ring's — every kill's experience also goes to
    // the weapon in hand — on the RING's curve (UnitStatMath.RingXpToNextFrom).
    // SKILL MASTERY: uses; reaching level N costs 5 × N uses, so 2,325 to reach 30.
    public const int SkillUsesPerLevelStep = 5;

    /// <summary>Total experience a weapon mastery of <paramref name="level"/> has cost.</summary>
    public static float WeaponXpFor(int level)
    {
        float total = 0f;
        for (int l = 0; l < level; l++) total += UnitStatMath.RingXpToNextFrom(l);
        return total;
    }

    /// <summary>The weapon mastery level <paramref name="xp"/> of experience buys.</summary>
    public static int WeaponLevelFrom(float xp)
    {
        int level = 0;
        while (xp >= UnitStatMath.RingXpToNextFrom(level)) { xp -= UnitStatMath.RingXpToNextFrom(level); level++; }
        return level;
    }

    /// <summary>How far <paramref name="xp"/> is into its current weapon mastery level, and how much experience that
    /// level takes in all — "12 of 20" on the way from level 0 to 1.</summary>
    public static void WeaponProgress(float xp, out float intoLevel, out float levelSize)
    {
        int level = WeaponLevelFrom(xp);
        intoLevel = Mathf.Max(0f, xp - WeaponXpFor(level));
        levelSize = UnitStatMath.RingXpToNextFrom(level);
    }

    /// <summary>The weapon mastery experience a kill worth <paramref name="xp"/> pays: the same experience, at the
    /// speed the weapon in hand's grade trains.</summary>
    public static float WeaponXpFromKill(int xp, Quality heldQuality)
        => Mathf.Max(0, xp) * CraftingMath.TrainingSpeed(heldQuality);

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
