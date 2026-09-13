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

    /// <summary>How many main stats a copy keeps, in this order: STR, AGI, VIT, INT, DEX, LUK.</summary>
    public const int MainStatCount = 6;
    [Tooltip("TRUE once THIS copy's armour numbers were rolled (ArmourRoll). False on anything that never rolled — a " +
             "weapon, the ring, or a piece saved before rolls existed — which then adds its item's own flat bonuses.")]
    public bool rolled;
    [Tooltip("THIS copy's rolled defence, before rarity.")]
    public int rolledDefense;
    [Tooltip("THIS copy's rolled magic defence, before rarity.")]
    public int rolledMagicDefense;
    [Tooltip("THIS copy's rolled main stats, before rarity's flat extra, in the order STR, AGI, VIT, INT, DEX, LUK. " +
             "0 = not carried.")]
    public int[] rolledStats = new int[MainStatCount];

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

    /// <summary>Make a fresh copy of <paramref name="def"/> as it ENTERS THE WORLD: a weapon rolls its rarity (unless
    /// one is forced) and then its skill kit from the published pool of its type; a Head, Armor, Garment or Shield
    /// rolls its rarity the same way and then its defence, magic defence and main stats (<see cref="ArmourRoll"/>);
    /// anything else just inherits the def's rarity. THE single creation path for anything entering the world — loot,
    /// a GM grant, a reward, what a creature spawns wearing — so no caller can hand out a copy that skipped its roll.</summary>
    public static EquipmentInstance CreateRolled(Item def, Rarity? forcedRarity = null, int upgrade = 0)
    {
        var inst = new EquipmentInstance(def, upgrade);
        if (def == null) return inst;

        bool isWeapon = def.slot == EquipSlotType.Weapon && def.weaponType != WeaponType.None;
        bool isArmour = ArmourRoll.IsRolledSlot(def.slot);
        inst.rarity = forcedRarity ?? (isWeapon || isArmour ? WeaponSkillRoll.RollRarity() : def.rarity);
        if (isWeapon) inst.skillIds = WeaponSkillRoll.RollKit(def.weaponType);
        if (isArmour) ArmourRoll.Roll(inst);
        return inst;
    }

    /// <summary>The rolled-stats array always has its six places — a copy built by a serializer can arrive without it.</summary>
    public void EnsureStatArray()
    {
        if (rolledStats == null || rolledStats.Length != MainStatCount) rolledStats = new int[MainStatCount];
    }

    /// <summary>This copy's rolled armour numbers joined for the save and the wire: "defence,magic defence,STR,AGI,VIT,INT,
    /// DEX,LUK". "" = this copy never rolled. Paired with <see cref="DecodeRoll"/>.</summary>
    public string EncodeRoll()
    {
        if (!rolled) return "";
        EnsureStatArray();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append(rolledDefense.ToString(ci)).Append(',').Append(rolledMagicDefense.ToString(ci));
        for (int i = 0; i < MainStatCount; i++) sb.Append(',').Append(rolledStats[i].ToString(ci));
        return sb.ToString();
    }

    /// <summary>Rebuild the rolled armour numbers from a saved string. Anything that is not exactly eight whole numbers
    /// — "", a save written before rolls existed, a damaged entry — leaves the copy UNROLLED, so it adds its item's own
    /// flat bonuses and is never mistaken for a copy that rolled zeroes.</summary>
    public void DecodeRoll(string encoded)
    {
        rolled = false;
        rolledDefense = 0;
        rolledMagicDefense = 0;
        EnsureStatArray();
        System.Array.Clear(rolledStats, 0, rolledStats.Length);
        if (string.IsNullOrEmpty(encoded)) return;

        var parts = encoded.Split(',');
        if (parts.Length != 2 + MainStatCount) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var ns = System.Globalization.NumberStyles.Integer;
        var values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], ns, ci, out values[i])) return;

        rolledDefense = values[0];
        rolledMagicDefense = values[1];
        for (int i = 0; i < MainStatCount; i++) rolledStats[i] = values[2 + i];
        rolled = true;
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

        AddPieceInto(ref eb);

        // Refine: a weapon's ATK scaling (weapons only), and a small DEF bump on every piece. Never lifted by rarity.
        if (def.slot == EquipSlotType.Weapon) eb.atk += upgrade * AtkPerRefine;
        eb.def += upgrade * DefPerRefine;

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

    /// <summary>What THIS copy itself adds, before refine and cards — the one place a copy's own numbers are worked out,
    /// read by the stat sheet, the equip preview, the tooltip and the inventory column alike.
    /// <list type="bullet">
    /// <item>A ROLLED piece of armour: its rolled defence and magic defence lifted by rarity, and each rolled main stat
    /// plus rarity's flat extra. Its item's flat bonus fields are not read.</item>
    /// <item>A weapon: its item's bonuses, with attack and magic attack lifted by rarity.</item>
    /// <item>Anything else, and a copy that never rolled: its item's flat bonuses, exactly as authored.</item>
    /// </list>
    /// Allocation-free.</summary>
    public void AddPieceInto(ref EquipBonuses eb)
    {
        if (def == null) return;

        if (rolled && ArmourRoll.IsRolledSlot(def.slot))
        {
            EnsureStatArray();
            eb.def  += Rarities.Lifted(rolledDefense, rarity);
            eb.mdef += Rarities.Lifted(rolledMagicDefense, rarity);
            int extra = Rarities.StatExtra(rarity);
            eb.str   += WithExtra(rolledStats[0], extra);
            eb.agi   += WithExtra(rolledStats[1], extra);
            eb.vit   += WithExtra(rolledStats[2], extra);
            eb.intel += WithExtra(rolledStats[3], extra);
            eb.dex   += WithExtra(rolledStats[4], extra);
            eb.luk   += WithExtra(rolledStats[5], extra);
            return;
        }

        bool weapon = def.slot == EquipSlotType.Weapon;
        eb.hp   += def.bonusHP;
        eb.mp   += def.bonusMP;
        eb.matk += weapon ? Rarities.Lifted(def.bonusMAtk, rarity) : def.bonusMAtk;
        eb.mdef += def.bonusMDef;
        eb.str  += def.bonusStr;
        eb.agi  += def.bonusAgi;
        eb.vit  += def.bonusVit;
        eb.intel+= def.bonusInt;
        eb.dex  += def.bonusDex;
        eb.luk  += def.bonusLuk;
        eb.critPercent += def.bonusCritPercent;
        eb.aspdPercent += def.bonusAspdPercent;
        eb.atk  += weapon ? Rarities.Lifted(def.bonusAttack, rarity) : def.bonusAttack;
        eb.def  += def.bonusDefense;
    }

    // A rolled main stat the copy does not carry (0) stays 0 — rarity's extra is only added to a stat that is there.
    static int WithExtra(int rolledValue, int extra) => rolledValue > 0 ? rolledValue + extra : 0;
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
