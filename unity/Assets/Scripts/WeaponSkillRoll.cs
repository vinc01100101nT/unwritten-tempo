using System.Collections.Generic;

/// <summary>
/// The ONE place a dropped weapon's skill kit is decided — "fate gives you the build" from the crafting design.
/// A weapon copy rolls its own kit at creation from the PUBLISHED skills of its own <see cref="WeaponType"/>
/// (see <see cref="SkillDef.published"/> / <see cref="SkillDef.weaponType"/>), so two Swords off the same
/// <see cref="Item"/> asset can carry completely different skills. The kit lives on the copy
/// (<see cref="EquipmentInstance.skillIds"/>), never on the shared asset.
///
/// <para><b>RARITY DOES NOT TOUCH THE KIT.</b> Every drop rolls the same size band and the same ultimate chance,
/// whatever tier it is. On a weapon, rarity decides how hard the kit hits (<see cref="Rarities.OutputLift"/>) and how
/// big the weapon's own attack is (<see cref="Rarities.Lifted"/>) — never what is in the kit. Do not reintroduce a
/// per-rarity kit size or ultimate chance here.</para>
///
/// <para>Actives and passives are rolled as INDEPENDENT counts, so one drop can land 3 actives and 4 passives.
/// Every number below is a tuning constant here — this file is the single source for the drop table AND the
/// kit sizes.</para>
/// </summary>
public static class WeaponSkillRoll
{
    /// <summary>How big a kit a drop rolls — THE SAME FOR EVERY DROP, whatever its rarity. Actives and passives
    /// are drawn independently between their own min/max, so the two halves don't track each other.
    ///
    /// <para><b>ActiveMax is load-bearing:</b> the hotbar is built with exactly that many numbered slots plus the
    /// ultimate slot, so lowering it leaves a slot nothing can ever fill. Change the bar in the same edit or
    /// don't lower it.</para></summary>
    public const int ActiveMin = 3, ActiveMax = 4, PassiveMin = 3, PassiveMax = 4;

    /// <summary>Percent chance that a drop carries an ultimate. One number for every drop — a weapon's ultimate
    /// is its build anchor, and which anchor you get is fate's call, not the tier's.</summary>
    public const int UltimatePct = 25;

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

    /// <summary>Roll the whole kit for a <paramref name="weapon"/> — the ultimate first, then the actives, then
    /// the passives. It takes NO rarity, deliberately: a kit is the same shape at every tier, and rarity's only
    /// effect is the lift it puts on that kit's numbers once the weapon is worn. Never returns a duplicate id,
    /// and quietly rolls a SHORT kit when the published pool can't fill it (author more skills, or publish the
    /// ones you have).</summary>
    public static List<string> RollKit(WeaponType weapon)
    {
        var kit = new List<string>(9);
        if (weapon == WeaponType.None) return kit;

        BuildPools(weapon);
        if (CombatRng.Range(0, 100) < UltimatePct) TakeRandom(ults, 1, kit);
        TakeRandom(actives, CombatRng.Range(ActiveMin, ActiveMax + 1), kit);
        TakeRandom(passives, CombatRng.Range(PassiveMin, PassiveMax + 1), kit);
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
