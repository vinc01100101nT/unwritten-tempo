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
    [Tooltip("How many times this copy has been FOLDED (+N). A fold is the piece's one unit of work: it raises the " +
             "piece's own attack or defence, and a quench spends every fold it has.")]
    public int upgrade;
    [Tooltip("Inserted cards (length <= def.cardSlots).")]
    public List<Card> cards = new List<Card>();
    [Tooltip("What this copy came out of its last quench as. A freshly forged piece is Raw and has never been " +
             "quenched; F to A is the ladder above it.")]
    public Quality quality = Quality.Raw;

    [Tooltip("The stats written onto this copy with Ink, in the order they were written — the LAST one is the one a " +
             "failed quench removes. Parallel to Ink Values.")]
    public List<InkStat> inkStats = new List<InkStat>();
    [Tooltip("What each written stat is worth. A percentage stat is whole percent. Parallel to Ink Stats.")]
    public List<float> inkValues = new List<float>();

    // THE SERVER'S FINISHED ANSWER for what this copy's folds and quality are worth, written whenever it is forged,
    // folded or quenched (Recompute). A player's machine never works these out — it adds what it was sent.
    [Tooltip("Finished attack this copy grants (server-written).")]
    public int pieceAttack;
    [Tooltip("Finished magic attack this copy grants (server-written).")]
    public int pieceMagicAttack;
    [Tooltip("Finished defence this copy grants (server-written).")]
    public int pieceDefense;
    [Tooltip("Finished magic defence this copy grants (server-written).")]
    public int pieceMagicDefense;

    public EquipmentInstance() { }
    public EquipmentInstance(Item def, int upgrade = 0)
    {
        this.def = def;
        this.upgrade = upgrade;
        Recompute();
    }

    /// <summary>Work the piece's numbers out ONLY where that is allowed — on the server, and in the editor's own
    /// tools, which have no server to ask. A player's machine leaves them exactly as they arrived.</summary>
    void RecomputeIfMine()
    {
        if (GameServer.IsServer || !Application.isPlaying) Recompute();
    }

    /// <summary>A fresh copy of <paramref name="def"/> as it ENTERS THE WORLD: Raw, unfolded, with nothing written
    /// on it. THE single creation path for anything that enters the world — a grant, a reward, what a creature
    /// spawns wearing — so no caller can hand out a copy in a state a forge could not have produced.</summary>
    public static EquipmentInstance CreateFresh(Item def) => new EquipmentInstance(def);

    /// <summary>Work out what this copy's folds and quality are worth and write the answer onto it. THE SERVER
    /// calls this after forging, folding or quenching, and a player's machine never does: the finished numbers
    /// cross in the same text the grade does, and that side only copies them in.</summary>
    public void Recompute()
    {
        if (def == null) { pieceAttack = pieceMagicAttack = pieceDefense = pieceMagicDefense = 0; return; }
        CraftingMath.PieceNumbers(def.slot, def.weaponType, upgrade, quality,
                                  out pieceAttack, out pieceMagicAttack, out pieceDefense, out pieceMagicDefense);
    }

    /// <summary>How many stats this copy's grade lets it carry.</summary>
    public int InkSlots => CraftingMath.InkSlots(quality);

    /// <summary>Write one stat onto this copy, at <paramref name="slot"/>. A slot already holding a stat is
    /// replaced; the first empty slot is filled. Refused when the grade has no such slot, or when the stat is
    /// already somewhere on the piece.</summary>
    public bool WriteInk(int slot, InkStat stat, float value)
    {
        EnsureInkLists();
        if (slot < 0 || slot >= InkSlots) return false;
        for (int i = 0; i < inkStats.Count; i++)
            if (inkStats[i] == stat && i != slot) return false;

        // A SLOT PAST THE END IS THE FIRST EMPTY ONE. The written stats are a list, not a grid with holes in it:
        // padding the gap with copies of the stat being written would put the same stat on the piece several
        // times, five of them worth nothing, and read as a full piece.
        if (slot > inkStats.Count) slot = inkStats.Count;
        if (slot == inkStats.Count) { inkStats.Add(stat); inkValues.Add(value); return true; }
        inkStats[slot] = stat;
        inkValues[slot] = value;
        return true;
    }

    /// <summary>Drop the LAST stat written on this copy — what a quench that lowers the grade takes away.</summary>
    public void DropLastInk()
    {
        EnsureInkLists();
        if (inkStats.Count == 0) return;
        inkStats.RemoveAt(inkStats.Count - 1);
        inkValues.RemoveAt(inkValues.Count - 1);
    }

    /// <summary>Cut the written stats back to what this copy's grade allows, dropping the last ones first.</summary>
    public void TrimInkToSlots()
    {
        EnsureInkLists();
        while (inkStats.Count > InkSlots) DropLastInk();
    }

    void EnsureInkLists()
    {
        if (inkStats == null) inkStats = new List<InkStat>();
        if (inkValues == null) inkValues = new List<float>();
        while (inkValues.Count < inkStats.Count) inkValues.Add(0f);
        while (inkStats.Count < inkValues.Count) inkValues.RemoveAt(inkValues.Count - 1);
    }

    /// <summary>This copy's grade, THE FINISHED NUMBERS the server worked out for it, and its written stats,
    /// joined for the save and the wire: "grade;attack,magic attack,defence,magic defence;stat:value,stat:value".
    /// Paired with <see cref="DecodeRoll"/>. Kept in the same slot the armour roll used, so the save and the wire
    /// need no new array — and carrying the finished numbers here is what lets a player's machine draw them
    /// without working any of them out.</summary>
    public string EncodeRoll()
    {
        EnsureInkLists();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append(((int)quality).ToString(ci)).Append(';')
          .Append(pieceAttack.ToString(ci)).Append(',').Append(pieceMagicAttack.ToString(ci)).Append(',')
          .Append(pieceDefense.ToString(ci)).Append(',').Append(pieceMagicDefense.ToString(ci)).Append(';');
        for (int i = 0; i < inkStats.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(((int)inkStats[i]).ToString(ci)).Append(':').Append(inkValues[i].ToString("0.###", ci));
        }
        return sb.ToString();
    }

    /// <summary>Rebuild the grade and the written stats from a saved string. Anything unreadable — "", a save
    /// written before crafting existed, a damaged entry — leaves the copy Raw with nothing written on it, which
    /// is exactly what a piece that has never been to a forge is.</summary>
    public void DecodeRoll(string encoded)
    {
        quality = Quality.Raw;
        pieceAttack = pieceMagicAttack = pieceDefense = pieceMagicDefense = 0;
        EnsureInkLists();
        inkStats.Clear();
        inkValues.Clear();
        if (!string.IsNullOrEmpty(encoded))
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var ints = System.Globalization.NumberStyles.Integer;
            var parts = encoded.Split(';');
            if (int.TryParse(parts[0], ints, ci, out int q))
                quality = (Quality)Mathf.Clamp(q, (int)Quality.Raw, (int)Quality.A);

            // The finished numbers, exactly as the server wrote them. A save written before they travelled has
            // no such part; the server works them out again below, and nothing else ever does.
            if (parts.Length > 1)
            {
                var nums = parts[1].Split(',');
                if (nums.Length == 4)
                {
                    int.TryParse(nums[0], ints, ci, out pieceAttack);
                    int.TryParse(nums[1], ints, ci, out pieceMagicAttack);
                    int.TryParse(nums[2], ints, ci, out pieceDefense);
                    int.TryParse(nums[3], ints, ci, out pieceMagicDefense);
                }
            }

            if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2]))
                foreach (var pair in parts[2].Split(','))
                {
                    var bits = pair.Split(':');
                    if (bits.Length != 2) continue;
                    if (!int.TryParse(bits[0], ints, ci, out int s)) continue;
                    if (!float.TryParse(bits[1], System.Globalization.NumberStyles.Float, ci, out float v)) continue;
                    // EVERY stat the enum names, whatever its number. A ceiling typed here once stopped at LUK
                    // and silently threw away Magic Defence and Crit Damage — the two that sit after it.
                    if (!System.Enum.IsDefined(typeof(InkStat), s)) continue;
                    inkStats.Add((InkStat)s);
                    inkValues.Add(v);
                }
            TrimInkToSlots();
        }
        RecomputeIfMine();
    }

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

    /// <summary>What THIS copy itself adds, before its cards — the one place a copy's own numbers are read, by the
    /// stat sheet, the equip preview, the tooltip and the inventory column alike. Two things and no more: the
    /// finished attack / magic attack / defence / magic defence the server worked out from its folds and its grade,
    /// and whatever Ink has written on it. The item's own bonus fields are not read: a crafted piece IS its work.
    /// Allocation-free.</summary>
    public void AddPieceInto(ref EquipBonuses eb)
    {
        if (def == null) return;
        CraftingMath.AddPiece(pieceAttack, pieceMagicAttack, pieceDefense, pieceMagicDefense,
                              inkStats, inkValues, ref eb);
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
    /// <summary>Flat additions to the accuracy and evasion numbers.</summary>
    public int accuracy, evasion;
    /// <summary>Whole-percent additions (e.g. 5 = +5%). Fractional, because one Ink writes +0.6% crit.</summary>
    public float critPercent, aspdPercent, castSpeedPercent;
    /// <summary>Whole-percent addition to how hard a crit lands, the other half of the ring's Critical node.</summary>
    public float critDamagePercent;

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
