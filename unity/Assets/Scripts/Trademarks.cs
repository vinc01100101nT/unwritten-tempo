using System.Collections.Generic;

/// <summary>
/// A forged weapon's TRADEMARK: the one skill of its weapon type that the weapon empowers, rolled once at the forge.
///
/// <para>Nothing about a trademark is stored except WHICH skill it is. Whether a skill can be one, what its name is,
/// and which of its numbers the weapon raises are all read from the skill as it is right now — so a skill that gains
/// a main number becomes a trademark, one that loses its last stops being one, and a changed value is followed at
/// once, with no second list to keep in step.</para>
/// </summary>
public static class Trademarks
{
    /// <summary>True when <paramref name="def"/> can be a weapon's trademark: it belongs to a weapon, and at least one
    /// of its effects has a main number (<see cref="EffectScaling.HasMainNumber"/>).</summary>
    public static bool Qualifies(SkillDef def) =>
        def != null && def.weaponType != WeaponType.None && EffectScaling.HasMainNumber(def);

    /// <summary>True when the forge may roll <paramref name="def"/> onto a new <paramref name="weapon"/>: it
    /// qualifies, belongs to that weapon, and is PUBLISHED — publishing is what lets a skill be rolled onto a weapon.</summary>
    public static bool Rollable(SkillDef def, WeaponType weapon) =>
        def != null && def.published && def.weaponType == weapon && Qualifies(def);

    // The forge's candidates, gathered into one reused list — a forge is rare, but it needs no garbage either.
    static readonly List<SkillDef> scratch = new List<SkillDef>(24);

    static void Gather(WeaponType weapon, out int ordinaries, out int ultimates)
    {
        scratch.Clear();
        ordinaries = ultimates = 0;
        if (weapon == WeaponType.None) return;
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (!Rollable(s, weapon)) continue;
            scratch.Add(s);
            if (s.IsUltimate) ultimates++; else ordinaries++;
        }
    }

    /// <summary>The size of the forge's trademark roll for <paramref name="weapon"/> — draw a whole number below it
    /// and hand it to <see cref="Pick"/>. 0 when that weapon has nothing to roll.</summary>
    public static int RollTotal(WeaponType weapon)
    {
        Gather(weapon, out int ord, out int ult);
        int total = 0;
        for (int i = 0; i < scratch.Count; i++)
            total += CraftingMath.TrademarkWeight(scratch[i].IsUltimate, ord, ult);
        return total;
    }

    /// <summary>The skill id a draw of <paramref name="roll"/> (0 to <see cref="RollTotal"/> − 1) lands on, or "" when
    /// the weapon has nothing to roll.</summary>
    public static string Pick(WeaponType weapon, int roll)
    {
        Gather(weapon, out int ord, out int ult);
        for (int i = 0; i < scratch.Count; i++)
        {
            roll -= CraftingMath.TrademarkWeight(scratch[i].IsUltimate, ord, ult);
            if (roll < 0) return scratch[i].id;
        }
        return scratch.Count > 0 ? scratch[scratch.Count - 1].id : "";
    }

    /// <summary>The trademark skill of <paramref name="weapon"/> as it stands now, or null — none rolled, the skill
    /// gone from the game, or no longer a skill with a main number.</summary>
    public static SkillDef SkillOf(EquipmentInstance weapon)
    {
        if (weapon == null || string.IsNullOrEmpty(weapon.trademarkSkillId)) return null;
        var def = SkillCatalog.Get(weapon.trademarkSkillId);
        return Qualifies(def) ? def : null;
    }

    /// <summary>What <paramref name="weapon"/>'s trademark adds to <paramref name="skillId"/>'s main numbers — the
    /// finished lift the server wrote on the copy, and 0 for any other skill.</summary>
    public static bool Empowers(EquipmentInstance weapon, string skillId)
    {
        var def = SkillOf(weapon);
        return def != null && def.id == skillId;
    }

    public static float LiftOn(EquipmentInstance weapon, string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return 0f;
        var def = SkillOf(weapon);
        return def != null && def.id == skillId ? weapon.trademarkLift : 0f;
    }
}
