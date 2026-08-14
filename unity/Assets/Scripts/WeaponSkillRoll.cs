using System.Collections.Generic;

/// <summary>
/// The ONE place a dropped weapon's skill kit is decided — "fate gives you the build" from the crafting design.
/// A weapon copy rolls its own kit at creation from the PUBLISHED skills of its own <see cref="WeaponType"/>
/// (see <see cref="SkillDef.published"/> / <see cref="SkillDef.weaponType"/>), so two Swords off the same
/// <see cref="Item"/> asset can carry completely different skills. The kit lives on the copy
/// (<see cref="EquipmentInstance.skillIds"/>), never on the shared asset.
///
/// <para>Actives and passives are rolled as INDEPENDENT counts (an Uncommon can land 1 active + 2 passives), and
/// the ultimate is a Legendary-only guarantee rather than a per-drop chance. Every number below is a tuning
/// constant here — this file is the single source for the drop table AND the kit sizes.</para>
/// </summary>
public static class WeaponSkillRoll
{
    /// <summary>How big a kit each rarity rolls. Actives and passives are drawn independently between their own
    /// min/max, so the two halves of a kit don't track each other.</summary>
    public readonly struct KitSize
    {
        public readonly int ActiveMin, ActiveMax, PassiveMin, PassiveMax;
        public readonly bool Ultimate;
        public KitSize(int aMin, int aMax, int pMin, int pMax, bool ult)
        { ActiveMin = aMin; ActiveMax = aMax; PassiveMin = pMin; PassiveMax = pMax; Ultimate = ult; }
    }

    /// <summary>The rarity → kit-size table. The ultimate is EXTRA (it rides the dedicated ult slot), so a
    /// Legendary fills 4 numbered slots + the ult slot + 4 passives.</summary>
    public static KitSize SizeFor(Rarity r) => r switch
    {
        Rarity.Uncommon  => new KitSize(1, 2, 1, 2, false),
        Rarity.Rare      => new KitSize(2, 3, 2, 3, false),
        Rarity.Epic      => new KitSize(3, 4, 3, 4, false),
        Rarity.Legendary => new KitSize(4, 4, 4, 4, true),
        _                => new KitSize(1, 1, 1, 1, false),   // Common
    };

    // Drop weights in whole percent — MUST sum to 100. Legendary is pinned at 5% (design call, subject to tuning);
    // the rest are spread so a Common is the everyday drop and an Epic is a genuine event.
    const int PctCommon = 45, PctUncommon = 30, PctRare = 15, PctEpic = 5, PctLegendary = 5;

    /// <summary>Roll a drop's rarity off the weight table above (used when nothing specifies one).</summary>
    public static Rarity RollRarity()
    {
        int roll = CombatRng.Range(0, 100);
        if ((roll -= PctLegendary) < 0) return Rarity.Legendary;
        if ((roll -= PctEpic)      < 0) return Rarity.Epic;
        if ((roll -= PctRare)      < 0) return Rarity.Rare;
        if ((roll -= PctUncommon)  < 0) return Rarity.Uncommon;
        return Rarity.Common;
    }

    // Candidate buckets, rebuilt per roll and reused across rolls (a drop is a cold path, but there's no reason
    // to hand the GC three lists per kill). TakeRandom swap-removes from them, so they must be scratch.
    static readonly List<SkillDef> ults = new List<SkillDef>(8);
    static readonly List<SkillDef> actives = new List<SkillDef>(32);
    static readonly List<SkillDef> passives = new List<SkillDef>(32);

    /// <summary>Roll the whole kit for a <paramref name="weapon"/> of <paramref name="rarity"/> — the ultimate
    /// (Legendary only) first, then the actives, then the passives. Never returns a duplicate id, and quietly rolls
    /// a SHORT kit when the published pool can't fill it (author more skills, or publish the ones you have).</summary>
    public static List<string> RollKit(WeaponType weapon, Rarity rarity)
    {
        var kit = new List<string>(9);
        if (weapon == WeaponType.None) return kit;

        BuildPools(weapon);
        var size = SizeFor(rarity);
        if (size.Ultimate) TakeRandom(ults, 1, kit);
        TakeRandom(actives, CombatRng.Range(size.ActiveMin, size.ActiveMax + 1), kit);
        TakeRandom(passives, CombatRng.Range(size.PassiveMin, size.PassiveMax + 1), kit);
        return kit;
    }

    /// <summary>How many published skills this weapon type can currently roll from, split by bucket — so a GM
    /// command / editor tool can say "Sword has nothing published" instead of silently handing out an empty kit.</summary>
    public static void PoolCounts(WeaponType weapon, out int ultCount, out int activeCount, out int passiveCount)
    {
        BuildPools(weapon);
        ultCount = ults.Count; activeCount = actives.Count; passiveCount = passives.Count;
    }

    // Split every PUBLISHED skill of this weapon type into the three buckets a kit draws from. An ultimate is an
    // active by definition (a passive has no cast), so a def that is somehow both is filed as an ultimate only.
    static void BuildPools(WeaponType weapon)
    {
        ults.Clear(); actives.Clear(); passives.Clear();
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (d == null || !d.published || d.weaponType != weapon || string.IsNullOrEmpty(d.id)) continue;
            if (d.IsPassive) passives.Add(d);
            else if (d.IsUltimate) ults.Add(d);
            else actives.Add(d);
        }
    }

    // Move up to `count` random entries from `pool` into `kit` (as ids). Swap-removes what it takes, so the same
    // skill can never be drawn twice, and a pool smaller than `count` simply contributes everything it has.
    static void TakeRandom(List<SkillDef> pool, int count, List<string> kit)
    {
        for (int n = 0; n < count && pool.Count > 0; n++)
        {
            int pick = CombatRng.Range(0, pool.Count);
            kit.Add(pool[pick].id);
            pool[pick] = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
        }
    }
}
