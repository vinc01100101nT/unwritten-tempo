/// <summary>
/// WHAT A WORN HAT ADDS — its primary stats, its reach and its experience bonus, read off the hat's own design
/// (<see cref="Item.headgear"/>), the same on every copy. Its effect is a passive skill (<see cref="Item.wornSkillId"/>)
/// that runs through the ordinary passive door while it is worn (<see cref="Character.WearsSkill"/>).
///
/// <para>Numbers in, numbers out: the stat sheet, the item card and the server's kill pay all read these.</para>
/// </summary>
public static class Headgear
{
    /// <summary>Add what <paramref name="def"/> gives while worn into <paramref name="eb"/>. Nothing for a piece that
    /// carries no headgear numbers. Allocation-free.</summary>
    public static void AddInto(Item def, ref EquipBonuses eb)
    {
        if (def == null) return;
        var h = def.headgear;
        eb.str += h.str;
        eb.agi += h.agi;
        eb.vit += h.vit;
        eb.intel += h.intel;
        eb.dex += h.dex;
        eb.luk += h.luk;
        eb.attackReach += h.attackReach;
        eb.experiencePercent += h.experiencePercent;
    }

    /// <summary>The worn numbers that live in the sub-stat table, folded into it — basic attack reach.</summary>
    public static void FoldIntoSubStats(in EquipBonuses eb, float[] table)
    {
        table[(int)SubStat.BasicAttackRange] += eb.attackReach;
    }

    /// <summary>What a kill paying <paramref name="experience"/> pays someone wearing <paramref name="percent"/> extra
    /// experience (15 = +15%), rounded half away from zero.</summary>
    public static int ExperienceWithBonus(int experience, float percent)
        => percent <= 0f ? experience : WorldEventMath.Multiplied(experience, 1.0 + percent / 100.0);
}
