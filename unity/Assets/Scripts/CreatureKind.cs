using UnityEngine;

/// <summary>
/// WHICH OF THE THREE KINDS OF CREATURE this is. The kind decides how many extra skill slots it carries, whether it
/// summons, what it wears, how long it takes to come back, what it drops and whether it is a 3D character. How strong
/// it is never comes from here — that is its level and its stats, through the same formulas a player's go through.
///
/// <para>Serialized by int on every creature asset, so order is load-bearing: append, never insert.</para>
/// </summary>
public enum CreatureKind
{
    /// <summary>Fills the field. One extra skill slot, unforged gear.</summary>
    Normal,
    /// <summary>The stronger creatures of an area. Two extra skill slots, summons normals.</summary>
    Elite,
    /// <summary>The strongest creatures in the game: a 3D character, one per weapon type, levels 105 to 160. Four extra
    /// skill slots, the highest-grade gear there is, and summons elites.</summary>
    Shadow,
}

/// <summary>
/// THE RULES EACH KIND CARRIES, as plain functions of the kind — the one place a slot count, a summon maximum or a
/// respawn wait is written, so the spawner, the brain, the loot and the gear all read the same answer. Design:
/// docs/3-world/mob-roster.md.
/// </summary>
public static class CreatureKinds
{
    /// <summary>The skill every summoner casts. It is never published, so no creature rolls it as an extra skill and no
    /// forge rolls it as a trademark; a summoner is handed it in a slot of its own (see <see cref="Summons"/>).</summary>
    public const string SummonSkillId = "skill.summon";

    /// <summary>How far a summoned creature may stray from its summoner before it gives up its target and walks back.</summary>
    public const float SummonFollowRange = 8f;

    /// <summary>The most extra skill slots any kind has — what a list sized once for every kind needs.</summary>
    public const int MostExtraSkillSlots = 4;

    /// <summary>Extra skill slots — skills of its own, apart from what its weapon's mastery has opened. The summon skill
    /// is NOT one of these; it has a slot of its own.</summary>
    public static int ExtraSkillSlots(CreatureKind k) =>
        k == CreatureKind.Shadow ? 4 : k == CreatureKind.Elite ? 2 : 1;

    /// <summary>How many summons this kind keeps out at once. Zero means it does not summon.</summary>
    public static int MaxSummons(CreatureKind k) =>
        k == CreatureKind.Shadow ? 5 : k == CreatureKind.Elite ? 10 : 0;

    /// <summary>Does this kind summon at all?</summary>
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

    /// <summary>A shadow is a 3D character; the other two kinds are flat.</summary>
    public static bool Is3D(CreatureKind k) => k == CreatureKind.Shadow;

    /// <summary>Does this kind pick up random pieces for the places nothing was authored for? A shadow wears exactly what
    /// it was given — most of the twelve have no weapon until their weapon's skills exist.</summary>
    public static bool WearsRandomGear(CreatureKind k) => k != CreatureKind.Shadow;

    /// <summary>Does this kind wear its gear at the best a forge can make it — the top grade, folded to its cap, every stat
    /// slot written with the strongest Ink, and a trademark on the weapon? Otherwise every piece is Raw and unfolded.</summary>
    public static bool WearsBestGear(CreatureKind k) => k == CreatureKind.Shadow;
}
