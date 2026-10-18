using System.Collections.Generic;

/// <summary>
/// A weapon's TRADEMARK: the one skill of its weapon type that the weapon empowers, rolled once when the copy is made.
///
/// <para>Nothing about a trademark is stored except WHICH skill it is. Whether a skill can be one, what its name is and
/// which of its numbers the weapon raises are all read from the skill as it is right now — so a skill that gains a
/// main number becomes a trademark, one that loses its last stops being one, and a changed value is followed at once,
/// with no second list to keep in step.</para>
///
/// <para><b>The roll is two draws, and has nothing to do with the weapon's rarity.</b> First a CLASS: an ultimate's on
/// <see cref="CraftingMath.UltimateTrademarkPercent"/> percent of weapons, an active's or a passive's on the rest — or
/// every weapon to the one class a weapon type has (<see cref="CraftingMath.TrademarkIsUltimate"/>). Then one skill of
/// that class, every one equally likely.</para>
/// </summary>
public static class Trademarks
{
    /// <summary>True when <paramref name="def"/> can be a weapon's trademark: it belongs to a weapon, and at least one
    /// of its effects has a main number (<see cref="EffectScaling.HasMainNumber"/>).</summary>
    public static bool Qualifies(SkillDef def) =>
        def != null && def.weaponType != WeaponType.None && EffectScaling.HasMainNumber(def);

    /// <summary>True when <paramref name="def"/> may be rolled onto a new <paramref name="weapon"/>: it qualifies,
    /// belongs to that weapon, and is PUBLISHED — publishing is what lets a skill be rolled onto a weapon.</summary>
    public static bool Rollable(SkillDef def, WeaponType weapon) =>
        def != null && def.published && def.weaponType == weapon && Qualifies(def);

    // Each weapon type's candidates, split into its two classes — gathered the first time that type is asked about and
    // kept, because every new weapon asks and a search through every skill per weapon would be a search through every
    // skill. Forgotten whenever the skill catalog is (SkillCatalog.Reload).
    sealed class Pool
    {
        public readonly List<SkillDef> ultimates = new List<SkillDef>();
        public readonly List<SkillDef> others = new List<SkillDef>();
    }
    static readonly Dictionary<WeaponType, Pool> pools = new Dictionary<WeaponType, Pool>();

    static Pool PoolFor(WeaponType weapon)
    {
        if (pools.TryGetValue(weapon, out var pool)) return pool;
        pool = new Pool();
        if (weapon != WeaponType.None)
        {
            var all = SkillCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (!Rollable(s, weapon)) continue;
                (s.IsUltimate ? pool.ultimates : pool.others).Add(s);
            }
        }
        pools[weapon] = pool;
        return pool;
    }

    /// <summary>Forget every weapon type's candidates, so a skill authored or changed since is seen. The skill catalog
    /// calls this whenever it is reloaded.</summary>
    public static void Forget() => pools.Clear();

    /// <summary>True when there is at least one trademark to roll onto a new <paramref name="weapon"/>.</summary>
    public static bool AnyFor(WeaponType weapon)
    {
        var pool = PoolFor(weapon);
        return pool.ultimates.Count + pool.others.Count > 0;
    }

    /// <summary>The chance a new <paramref name="weapon"/> rolls <paramref name="def"/> as its trademark — its class's
    /// share, split evenly with the other trademarks of that class; 0 when it cannot be rolled. Read by the wiki's
    /// trademark table; never on a hot path.</summary>
    public static float ChanceOf(SkillDef def, WeaponType weapon)
    {
        if (!Rollable(def, weapon)) return 0f;
        var pool = PoolFor(weapon);
        return CraftingMath.TrademarkChance(def.IsUltimate, pool.ultimates.Count, pool.others.Count);
    }

    /// <summary>SERVER ONLY: roll the trademark of a new <paramref name="weapon"/> — a class, then a skill of that
    /// class, both drawn through <see cref="CombatRng"/>. The skill's id, or "" when that weapon has nothing to roll.</summary>
    public static string Roll(WeaponType weapon)
    {
        var pool = PoolFor(weapon);
        if (pool.ultimates.Count + pool.others.Count == 0) return "";
        bool ultimate = CraftingMath.TrademarkIsUltimate(pool.ultimates.Count, pool.others.Count, CombatRng.Range(0, 100));
        var list = ultimate ? pool.ultimates : pool.others;
        return list[CombatRng.Range(0, list.Count)].id;
    }

    /// <summary>The trademark skill of <paramref name="weapon"/> as it stands now, or null — none rolled, the skill
    /// gone from the game, or no longer a skill with a main number.</summary>
    public static SkillDef SkillOf(EquipmentInstance weapon) => weapon == null ? null : SkillOf(weapon.trademarkSkillId);

    /// <summary>The trademark skill named <paramref name="skillId"/>, or null — none, gone, or no longer a trademark.</summary>
    public static SkillDef SkillOf(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return null;
        var def = SkillCatalog.Get(skillId);
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
