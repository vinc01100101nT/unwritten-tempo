using UnityEngine;

/// <summary>
/// WHICH OF THE THREE KINDS OF CREATURE this is. The kind decides how many summons it may keep out, how long it takes
/// to come back and whether it is a 3D character. How strong it is and what it pays never come from here — that is its
/// level, its stats and its typed multiplier (<see cref="MonsterDef.multiplier"/>), the same for every kind.
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
    /// <summary>The skill a summoner casts. It is never published, so no weapon rolls it as a trademark. Like every
    /// skill, a creature casts it only once it has bought a level in it (<see cref="Health.Mastery"/>).</summary>
    public const string SummonSkillId = "skill.summon";

    /// <summary>How far a summoned creature may stray from its summoner before it gives up its target and walks back.</summary>
    public const float SummonFollowRange = 8f;

    /// <summary>How many summons this kind keeps out at once. Zero means it does not summon, whatever it has bought.</summary>
    public static int MaxSummons(CreatureKind k) =>
        k == CreatureKind.Shadow ? 5 : k == CreatureKind.Elite ? 10 : 0;

    /// <summary>May this kind keep summons out at all? It still casts the summon skill only once it has bought it.</summary>
    public static bool Summons(CreatureKind k) => MaxSummons(k) > 0;

    /// <summary>An elite's summons are split across the three creatures it calls nearest its level at or below it, in
    /// these shares: five of the nearest, three of the next, two of the one after. Adds up to <see cref="MaxSummons"/>.</summary>
    public static readonly int[] EliteSummonShares = { 5, 3, 2 };

    /// <summary>The multipliers an elite calls from, least and most: ×5 to ×10, the Tainted and Blighted the dungeons
    /// it guards are filled with (decided 2026-10-20).</summary>
    public const float EliteCallsFrom = 5f, EliteCallsUpTo = 10f;

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

    /// <summary>How many times harder a creature of <paramref name="multiplier"/> hits than a ×1 creature of the same
    /// level, stats and gear: the cube root of the health it multiplies, as Ragnarok's bosses do (×30 about 3.1×, ×150
    /// about 5.3×). Applied to attack and magic attack only; every other stat is a ×1 creature's.</summary>
    public static float AttackMultiplier(float multiplier) => Mathf.Pow(CreaturePay.Clamp(multiplier), 1f / 3f);

    /// <summary>The experience one kill pays. The ONE place a kill's worth is decided — by the creature's level and its
    /// multiplier (<see cref="CreaturePay"/>), whatever its kind.
    ///
    /// <para>The experience of <paramref name="fileLevel"/>, the level in the creature's FILE, and the plain difference
    /// of any levels it gained by hitting people in this fight, together times the multiplier.</para></summary>
    public static int KillExperience(int level, int fileLevel, float multiplier)
    {
        long atFile = UnitStatMath.KillXp(fileLevel);
        long grown = System.Math.Max(0L, (long)UnitStatMath.KillXp(level) - atFile);
        return (int)System.Math.Min(int.MaxValue, CreaturePay.Scaled(atFile + grown, multiplier));
    }

    /// <summary>The experience a kill of <paramref name="def"/> pays as it spawns.</summary>
    public static int KillExperience(MonsterDef def)
        => def != null ? KillExperience(def.level, def.level, def.multiplier) : 0;

    /// <summary>A shadow is a 3D character; the other two kinds are flat.</summary>
    public static bool Is3D(CreatureKind k) => k == CreatureKind.Shadow;
}
