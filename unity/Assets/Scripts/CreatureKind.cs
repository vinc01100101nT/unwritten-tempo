using UnityEngine;

/// <summary>
/// WHICH OF THE THREE KINDS OF CREATURE this is. The kind decides how many summons it may keep out, how long it takes
/// to come back, what it drops and whether it is a 3D character. How strong it is never comes from here — that is its level and its stats, through the same formulas a player's go through.
///
/// <para>Serialized by int on every creature asset, so order is load-bearing: append, never insert.</para>
/// </summary>
public enum CreatureKind
{
    /// <summary>Fills the field. Keeps no summons.</summary>
    Normal,
    /// <summary>The stronger creatures of an area. Its summons, once it has bought the summon skill, are normals.</summary>
    Elite,
    /// <summary>The strongest creatures in the game: a 3D character, one per weapon type, levels 105 to 170. Its
    /// summons, once it has bought the summon skill, are elites.</summary>
    Shadow,
}

/// <summary>
/// THE RULES EACH KIND CARRIES, as plain functions of the kind — the one place a slot count, a summon maximum or a
/// respawn wait is written, so the spawner, the brain, the loot and the gear all read the same answer. Design:
/// docs/3-world/mob-roster.md.
/// </summary>
public static class CreatureKinds
{
    /// <summary>The skill a summoner casts. It is never published, so no forge rolls it as a trademark. Like every
    /// skill, a creature casts it only once it has bought a level in it (<see cref="Health.Mastery"/>).</summary>
    public const string SummonSkillId = "skill.summon";

    /// <summary>How far a summoned creature may stray from its summoner before it gives up its target and walks back.</summary>
    public const float SummonFollowRange = 8f;

    /// <summary>How many summons this kind keeps out at once. Zero means it does not summon, whatever it has bought.</summary>
    public static int MaxSummons(CreatureKind k) =>
        k == CreatureKind.Shadow ? 5 : k == CreatureKind.Elite ? 10 : 0;

    /// <summary>May this kind keep summons out at all? It still casts the summon skill only once it has bought it.</summary>
    public static bool Summons(CreatureKind k) => MaxSummons(k) > 0;

    /// <summary>An elite's summons are split across the three normals nearest its level at or below it, in these
    /// shares: five of the nearest, three of the next, two of the one after. Adds up to <see cref="MaxSummons"/>.</summary>
    public static readonly int[] EliteSummonShares = { 5, 3, 2 };

    /// <summary>Seconds a creature of this kind stays gone after it dies. <paramref name="roll01"/> is a die from 0 to 1,
    /// thrown by the caller, so this stays arithmetic. Normal: 5 plus up to 3. Elite: 30 minutes to 3 hours. Shadow: 4 to
    /// 6 hours.</summary>
    public static float RespawnSeconds(CreatureKind k, float roll01)
    {
        roll01 = Mathf.Clamp01(roll01);
        switch (k)
        {
            case CreatureKind.Shadow: return Mathf.Lerp(4f * 3600f, 6f * 3600f, roll01);
            case CreatureKind.Elite:  return Mathf.Lerp(30f * 60f, 3f * 3600f, roll01);
            default:                  return 5f + 3f * roll01;
        }
    }

    /// <summary>How many kills of a normal creature of the same level one kill of this kind is worth: 10 for an elite,
    /// 20 for a shadow (decided 2026-09-30 — Ragnarok's bosses pay 5 to 10 times a normal creature of their level).</summary>
    public static int ExperienceMultiplier(CreatureKind k) =>
        k == CreatureKind.Shadow ? 20 : k == CreatureKind.Elite ? 10 : 1;

    /// <summary>The experience one kill pays. The ONE place a kill's worth is decided — by the creature's kind, its
    /// level and the grade and folds of its gear (<see cref="CreaturePay"/>), never by a number typed onto the creature.
    ///
    /// <para>The gear multiplies the experience of <paramref name="fileLevel"/>, the level in the creature's FILE, only.
    /// Levels a creature gained by hitting people in this fight add their plain difference on top — letting a geared
    /// creature hit you never multiplies what its gear pays.</para></summary>
    public static int KillExperience(CreatureKind k, int level, int fileLevel, Quality gearGrade, int gearFolds)
    {
        long atFile = UnitStatMath.KillXp(fileLevel);
        long grown = System.Math.Max(0L, (long)UnitStatMath.KillXp(level) - atFile);
        long worth = (CreaturePay.Scaled(atFile, gearGrade, gearFolds) + grown) * ExperienceMultiplier(k);
        return (int)System.Math.Min(int.MaxValue, worth);
    }

    /// <summary>The experience a kill of <paramref name="def"/> pays as it spawns.</summary>
    public static int KillExperience(MonsterDef def)
        => def != null ? KillExperience(def.kind, def.level, def.level, def.gearQuality, def.gearFolds) : 0;

    /// <summary>A shadow is a 3D character; the other two kinds are flat.</summary>
    public static bool Is3D(CreatureKind k) => k == CreatureKind.Shadow;
}
