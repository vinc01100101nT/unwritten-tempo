/// <summary>
/// A creature's combat numbers AS IT SPAWNS, worked out from its catalog entry — the one place this is done, read by the
/// Mob Table and by <c>/mobinfo</c>, so the two can never show different numbers for the same creature.
///
/// <para>It reads the SAME functions the game runs (<see cref="UnitStatMath"/>) through the entry's own stat inputs, so
/// what it gives is what the creature will actually have, not a parallel estimate. Worn gear and the ring are folded in
/// the way a spawned creature folds them in: one piece per slot (the last one authored for a slot wins, as
/// <see cref="UnitEquipment"/> seats them), gear primaries added to the inputs, the weapon held picking melee or ranged
/// attack, the ring's percentages, and the weapon mastery on the weapon held. No sub-stat buffs: this is a creature at rest.
/// Its worn pieces' written stats are left out too — which stats Ink lands on is rolled fresh every life, so nothing
/// could show them without inventing a roll.</para>
/// </summary>
public static class MonsterDefStats
{
    public enum Stat { HP, Atk, Def, HardDef, Flee }

    // Scratch reused by every call: the Mob Table asks four times per row per draw and twice per comparison while
    // sorting, so nothing in here may allocate. Every caller runs on the main thread.
    static readonly Item[] wornScratch = new Item[LargestSlot() + 1];
    static readonly EquipmentInstance pieceScratch = new EquipmentInstance();
    static readonly PassiveRanks ringScratch = new PassiveRanks();

    static int LargestSlot()
    {
        int max = 0;
        foreach (int v in System.Enum.GetValues(typeof(EquipSlotType))) if (v > max) max = v;
        return max;
    }

    // What the creature wears, added up at ITS OWN grade and folds, and the weapon type it holds.
    static void Worn(MonsterDef def, out EquipBonuses gear, out WeaponType held)
    {
        System.Array.Clear(wornScratch, 0, wornScratch.Length);
        if (def.startingGear != null)
            foreach (var item in def.startingGear)
                if (item != null && item.slot != EquipSlotType.None) wornScratch[(int)item.slot] = item;

        gear = default;
        foreach (var item in wornScratch)
        {
            if (item == null) continue;
            pieceScratch.def = item;
            pieceScratch.cards.Clear();
            // A creature wears what it was given at ITS OWN grade and folds, so the piece's attack or defence counts
            // here exactly as it will in the world.
            pieceScratch.quality = def.gearQuality;
            pieceScratch.upgrade = UnityEngine.Mathf.Clamp(def.FileFolds, 0, CraftingMath.FoldCap(def.gearQuality));
            pieceScratch.Recompute();
            pieceScratch.AccumulateInto(ref gear);
        }
        pieceScratch.def = null;
        var weapon = wornScratch[(int)EquipSlotType.Weapon];
        held = weapon != null ? weapon.weaponType : WeaponType.None;
    }

    /// <summary><paramref name="which"/> of <paramref name="def"/>'s combat numbers, as it spawns.</summary>
    /// <summary>A creature's defence as the sheet writes it: hard defence as a percentage, then soft — "6.0% + 12".</summary>
    public static string DefenseLabel(MonsterDef def) =>
        (Derived(def, Stat.HardDef) / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "% + "
        + Derived(def, Stat.Def);

    public static int Derived(MonsterDef def, Stat which)
    {
        Worn(def, out var gear, out var held);

        bool hasRanks = def.ringRankStats != null && def.ringRankValues != null && def.ringRankStats.Length > 0;
        if (hasRanks) ringScratch.LoadState(def.ringRankStats, def.ringRankValues, def.ringRankStats[0], true, 0);
        // Its weapon mastery lifts the same stat channels the ring does, by the player's rule (UnitStatBlock.Lift).
        int mastery = UnityEngine.Mathf.Min(def.MasteryOnWeapon, CraftingMath.WeaponMasteryCap(def.gearQuality));
        float Ring(StatId stat) => (hasRanks ? ringScratch.Percent(stat) : 0f)
            + (held != WeaponType.None && WeaponMasteryStats.Raises(held, stat) ? MasteryMath.WeaponPercent(mastery) : 0f);

        var u = def.StatInputs.WithGear(gear);
        switch (which)
        {
            case Stat.HP:   return UnitStatMath.MaxHP(u, gear, Ring(StatId.HP)) * CreatureKinds.HealthMultiplier(def.kind, def.level);
            case Stat.Atk:  return UnityEngine.Mathf.RoundToInt(UnitStatMath.PhysicalAttack(held, u, gear, Ring(StatId.Atk))
                                                    * CreatureKinds.AttackMultiplier(def.kind, def.level));
            case Stat.Def:  return UnitStatMath.SoftDefense(u);
            case Stat.HardDef: return UnitStatMath.CappedHardDefenseTenths(UnitStatMath.HardDefenseTenths(
                                   hasRanks ? ringScratch.Percent(StatId.Def) : 0f, gear.hardDefTenths, 0f));
            default:        return UnitStatMath.Flee(u, gear, Ring(StatId.Evasion));
        }
    }
}
