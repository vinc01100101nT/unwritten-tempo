using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A unique, upgradeable instance of an equipment piece — what actually lives in a save /
/// the equip dictionary, as opposed to the shared <see cref="Item"/> definition. The same
/// "Iron Sword" def can back many instances that differ by their +N enhancement and inserted
/// cards. (Bag-only items — potions, materials — don't need an instance; they're just a def
/// + a count.)
/// </summary>
[System.Serializable]
public class EquipmentInstance
{
    [Tooltip("The shared item definition this is an instance of.")]
    public Item def;
    [Tooltip("+N enhancement level (refine). Scales weapon ATK / adds a touch of DEF.")]
    public int upgrade;
    [Tooltip("Inserted cards (length ≤ def.cardSlots).")]
    public List<Card> cards = new List<Card>();
    [Tooltip("THIS copy's rarity — rolled per drop, not shared with the asset. Seeded from the def for anything " +
             "that isn't a rolled weapon, so a hand-authored piece keeps the rarity it was authored with.")]
    public Rarity rarity = Rarity.Common;
    [Tooltip("THIS copy's rolled skill kit (stable SkillDef ids) — the weapon's whole identity, decided once at " +
             "creation by WeaponSkillRoll and carried in the save. Empty for anything that isn't a weapon.")]
    public List<string> skillIds = new List<string>();

    // Enhancement scaling (kept simple for v1 — mirrors the locked prototype):
    //   weapon ATK gains +3 per refine level; every piece gains +1 soft DEF per level.
    public const int AtkPerRefine = 3;
    public const int DefPerRefine = 1;

    public EquipmentInstance() { }
    public EquipmentInstance(Item def, int upgrade = 0)
    {
        this.def = def;
        this.upgrade = upgrade;
        rarity = def != null ? def.rarity : Rarity.Common;
    }

    /// <summary>Make a fresh copy of <paramref name="def"/> as a DROP: a weapon rolls its rarity (unless one is
    /// forced) and then its skill kit from the published pool of its type; anything else just inherits the def's
    /// rarity. THE single creation path for anything entering the world — loot, a GM grant, a reward — so no
    /// caller can hand out a weapon that skipped its roll.</summary>
    public static EquipmentInstance CreateRolled(Item def, Rarity? forcedRarity = null, int upgrade = 0)
    {
        var inst = new EquipmentInstance(def, upgrade);
        if (def == null) return inst;

        bool isWeapon = def.slot == EquipSlotType.Weapon && def.weaponType != WeaponType.None;
        inst.rarity = forcedRarity ?? (isWeapon ? WeaponSkillRoll.RollRarity() : def.rarity);
        if (isWeapon) inst.skillIds = WeaponSkillRoll.RollKit(def.weaponType, inst.rarity);
        return inst;
    }

    /// <summary>The rolled kit joined for the save file ("" = none). Paired with <see cref="DecodeSkills"/>.</summary>
    public string EncodeSkills() => (skillIds == null || skillIds.Count == 0) ? "" : string.Join("|", skillIds);

    /// <summary>Rebuild <see cref="skillIds"/> from a saved string, dropping ids that are no longer authored (a
    /// retired or renamed skill can't linger as a dead slot).</summary>
    public void DecodeSkills(string encoded)
    {
        if (skillIds == null) skillIds = new List<string>();
        skillIds.Clear();
        if (string.IsNullOrEmpty(encoded)) return;
        foreach (var id in encoded.Split('|'))
            if (!string.IsNullOrEmpty(id) && SkillCatalog.IsKnown(id)) skillIds.Add(id);
    }

    public Rarity Rarity => rarity;
    public int CardSlots => def != null ? def.cardSlots : 0;
    public int FilledSockets => cards != null ? cards.Count : 0;
    public bool HasFreeSocket => FilledSockets < CardSlots;

    /// <summary>Fold this instance's def + refine + card bonuses into a running
    /// <see cref="EquipBonuses"/> total. Allocation-free (no LINQ / closures) so it's
    /// safe to call from the equipment-changed path.</summary>
    public void AccumulateInto(ref EquipBonuses eb)
    {
        if (def == null) return;

        eb.hp   += def.bonusHP;
        eb.mp   += def.bonusMP;
        eb.matk += def.bonusMAtk;
        eb.mdef += def.bonusMDef;
        eb.str  += def.bonusStr;
        eb.agi  += def.bonusAgi;
        eb.vit  += def.bonusVit;
        eb.intel+= def.bonusInt;
        eb.dex  += def.bonusDex;
        eb.luk  += def.bonusLuk;
        eb.critPercent += def.bonusCritPercent;
        eb.aspdPercent += def.bonusAspdPercent;

        // ATK: flat weapon ATK + refine scaling (weapons only).
        eb.atk += def.bonusAttack + (def.slot == EquipSlotType.Weapon ? upgrade * AtkPerRefine : 0);
        // DEF: flat armor DEF + a small refine bump on every piece.
        eb.def += def.bonusDefense + upgrade * DefPerRefine;

        if (cards == null) return;
        for (int i = 0; i < cards.Count; i++)
        {
            var c = cards[i];
            if (c == null) continue;
            eb.str  += c.bonusStr;
            eb.agi  += c.bonusAgi;
            eb.vit  += c.bonusVit;
            eb.intel+= c.bonusInt;
            eb.dex  += c.bonusDex;
            eb.luk  += c.bonusLuk;
            eb.atk  += c.bonusAttack;
            eb.critPercent += c.bonusCritPercent;
            eb.aspdPercent += c.bonusAspdPercent;
        }
    }
}

/// <summary>
/// The summed equipment contribution to the stat layers — the bridge between the item model
/// and <see cref="Character"/>'s derived-stat math. A value type (no allocation), aggregated
/// once when equipment changes (cached) and recomputed on a HYPOTHETICAL set for the hover
/// "equip this → stat delta" preview. This is the structural keystone: the live panel, the
/// hover preview, and the equip-compare all feed the SAME formulas a different EquipBonuses.
/// </summary>
public struct EquipBonuses
{
    public int hp, mp, atk, def, matk, mdef;
    public int str, agi, vit, intel, dex, luk;
    /// <summary>Whole-percent additions (e.g. 5 = +5%).</summary>
    public int critPercent, aspdPercent;

    /// <summary>The flat primary-stat bonus for one stat (gear + cards).</summary>
    public int Stat(PrimaryStat s)
    {
        switch (s)
        {
            case PrimaryStat.STR: return str;
            case PrimaryStat.AGI: return agi;
            case PrimaryStat.VIT: return vit;
            case PrimaryStat.INT: return intel;
            case PrimaryStat.DEX: return dex;
            case PrimaryStat.LUK: return luk;
            default: return 0;
        }
    }

    /// <summary>Aggregate a whole equipped set into one bonus total. Allocation-free.</summary>
    public static EquipBonuses Aggregate(IEnumerable<EquipmentInstance> set)
    {
        var eb = default(EquipBonuses);
        if (set == null) return eb;
        foreach (var inst in set)
            inst?.AccumulateInto(ref eb);
        return eb;
    }
}
