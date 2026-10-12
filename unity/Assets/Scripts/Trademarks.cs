using System.Collections.Generic;

/// <summary>
/// A forged weapon's TRADEMARK: the one skill of its weapon type that the weapon empowers, rolled once at the forge.
///
/// <para>Nothing about a trademark is stored except WHICH skill it is. Whether a skill can be one, what its name is,
/// how rare it is and which of its numbers the weapon raises are all read from the skill as it is right now — so a
/// skill that gains a main number becomes a trademark, one that loses its last stops being one, and a changed value
/// is followed at once, with no second list to keep in step.</para>
///
/// <para><b>The roll is two draws.</b> First a RARITY, by its share (<see cref="CraftingMath.TrademarkRarityAt"/>) among
/// the rarities this weapon type has trademarks of; then one trademark of that rarity, every one equally likely. So
/// the rarer a trademark, the rarer the weapon — which is what an item's rarity means (<see cref="Rarities"/>).</para>
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

    // The forge's candidates and how many there are of each rarity, gathered into reused storage — a forge is rare,
    // but it needs no garbage either.
    static readonly List<SkillDef> scratch = new List<SkillDef>(24);
    static readonly int[] byRarity = new int[CraftingMath.RarityCount];

    static void Gather(WeaponType weapon)
    {
        scratch.Clear();
        System.Array.Clear(byRarity, 0, byRarity.Length);
        if (weapon == WeaponType.None) return;
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (!Rollable(s, weapon)) continue;
            scratch.Add(s);
            byRarity[(int)Rarities.OfTrademark(s)]++;
        }
    }

    /// <summary>True when the forge has at least one trademark to roll onto a new <paramref name="weapon"/>.</summary>
    public static bool AnyFor(WeaponType weapon)
    {
        Gather(weapon);
        return scratch.Count > 0;
    }

    /// <summary>The chance a forge of <paramref name="weapon"/> rolls <paramref name="def"/> as its trademark — its
    /// rarity's share of the first draw, split evenly with the other trademarks of that rarity; 0 when it cannot be
    /// rolled. Read by the wiki's trademark table; never on a hot path.</summary>
    public static float ChanceOf(SkillDef def, WeaponType weapon)
    {
        if (!Rollable(def, weapon)) return 0f;
        Gather(weapon);
        return CraftingMath.TrademarkChance(Rarities.OfTrademark(def), byRarity);
    }

    /// <summary>SERVER ONLY: roll the trademark of a new <paramref name="weapon"/> — a rarity, then a skill of that
    /// rarity, both drawn through <see cref="CombatRng"/>. The skill's id, or "" when that weapon has nothing to roll.</summary>
    public static string Roll(WeaponType weapon)
    {
        Gather(weapon);
        int total = CraftingMath.TrademarkRollTotal(byRarity);
        if (total <= 0) return "";
        var rarity = CraftingMath.TrademarkRarityAt(byRarity, CombatRng.Range(0, total));
        int nth = CombatRng.Range(0, byRarity[(int)rarity]);
        for (int i = 0; i < scratch.Count; i++)
            if (Rarities.OfTrademark(scratch[i]) == rarity && nth-- == 0) return scratch[i].id;
        return scratch[scratch.Count - 1].id;
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
