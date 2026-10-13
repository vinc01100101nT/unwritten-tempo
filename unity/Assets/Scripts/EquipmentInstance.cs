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

    [Tooltip("This one copy's own id, unique among every piece in the game — what a hotbar slot names so it equips " +
             "exactly this piece and not another of the same kind. Given by the server the first time the copy is " +
             "saved or sent (EnsureCopyId); empty until then.")]
    public string copyId = "";

    /// <summary>SERVER: give this copy its id if it has none yet. Called wherever a copy leaves the server — saved or
    /// sent — so every piece anybody can see has one, and no creature's gear pays for one it never needs. A player's
    /// machine never makes one: it only ever wears the id the server sent.</summary>
    public void EnsureCopyId()
    {
        if (!string.IsNullOrEmpty(copyId)) return;
        if (!GameServer.IsServer && Application.isPlaying) return;
        copyId = System.Guid.NewGuid().ToString("N");
    }
    [Tooltip("How many times this copy has been FOLDED (+N). A fold is the piece's one unit of work: it raises the " +
             "piece's own attack or defence, and a quench spends every fold it has.")]
    public int upgrade;
    [Tooltip("Inserted cards (length <= def.cardSlots).")]
    public List<Card> cards = new List<Card>();
    [Tooltip("What this copy came out of its last quench as. A new piece is Raw and has never been quenched; F to A " +
             "is the ladder above it.")]
    public Quality quality = Quality.Raw;

    [Tooltip("The stats written onto this copy with Ink, in the order they were written — the LAST one is the one a " +
             "failed quench removes. Parallel to Ink Values.")]
    public List<InkStat> inkStats = new List<InkStat>();
    [Tooltip("What each written stat is worth. A percentage stat is whole percent. Parallel to Ink Stats.")]
    public List<float> inkValues = new List<float>();

    // THE SERVER'S FINISHED ANSWER for what this copy's folds, grade and rarity are worth, written whenever it is
    // made, folded or quenched (Recompute). A player's machine never works these out — it adds what it was sent.
    [Tooltip("Finished numbers this copy's folds, grade and rarity grant (server-written): attack or magic attack on a weapon; " +
             "on armour, what its material buys — defence and HP (metal), attack speed and evasion (leather), magic " +
             "defence and MP (cloth) — and on shoes, movement speed as well.")]
    public CraftingMath.PieceStats piece;

    [Tooltip("The id of this copy's TRADEMARK skill, rolled once when the copy is made and never changed. Empty = none. Its " +
             "name and the numbers it raises are read from that skill, never stored here.")]
    public string trademarkSkillId = "";
    [Tooltip("Finished lift this copy's trademark adds to its skill's main numbers (server-written, from the grade).")]
    public float trademarkLift;

    [Tooltip("An armour copy's WORD, rolled once when the copy is made from its slot's words and never changed. None = none.")]
    public ArmourWord word = ArmourWord.None;
    [Tooltip("What this copy's word is worth (server-written, from the grade) — see ArmourWords.Amount for its unit.")]
    public float wordAmount;

    [Tooltip("How rare this copy is. The server decides it once, when the copy is made, and it never changes: a piece a " +
             "creature drops rolls it by the creature's kind, every other piece is Common. It lifts what the folds are worth.")]
    public Rarity rarity = Rarity.Common;

    public EquipmentInstance() { }
    public EquipmentInstance(Item def, int upgrade = 0)
    {
        this.def = def;
        this.upgrade = upgrade;
        Recompute();
    }

    /// <summary>THE ONE WAY a copy becomes the four values a save, a message or a kept sale holds for it beside its item
    /// id: its folds, its cards, its roll text (grade, written stats, trademark, word, rarity) and its own id. Gives the copy
    /// its id first if it has none, as everything that lets a copy leave the server must.</summary>
    public void ToSaved(out int folds, out string cardsText, out string roll, out string id)
    {
        EnsureCopyId();
        folds = upgrade;
        cardsText = ItemCatalog.EncodeCards(cards);
        roll = EncodeRoll();
        id = copyId;
    }

    /// <summary>THE ONE WAY those four values become a copy again. <paramref name="cardsText"/> null = none were
    /// written, <paramref name="roll"/> null = a save older than v12, which carries no grade, so the piece loads Raw
    /// with nothing written on it. Loading is not a creation: nothing is rolled here.</summary>
    public static EquipmentInstance FromSaved(Item def, int folds, string cardsText, string roll, string id)
    {
        var inst = new EquipmentInstance(def, folds);
        if (cardsText != null) ItemCatalog.DecodeCards(cardsText, inst.cards);
        if (roll != null) inst.DecodeRoll(roll);
        else inst.Recompute();
        inst.copyId = id ?? "";
        return inst;
    }

    /// <summary>Work the piece's numbers out ONLY where that is allowed — on the server, and in the editor's own
    /// tools, which have no server to ask. A player's machine leaves them exactly as they arrived.</summary>
    void RecomputeIfMine()
    {
        if (GameServer.IsServer || !Application.isPlaying) Recompute();
    }

    /// <summary>A fresh copy of <paramref name="def"/> as it ENTERS THE WORLD: Raw, unfolded, no Ink, Common — and its
    /// one random thing already rolled, a weapon's trademark or an armour piece's word. THE rolling door every new
    /// piece comes through — a grant, a reward, what a creature spawns wearing, what a creature drops
    /// (<see cref="CreateDropped"/>). Server only: the roll goes through CombatRng. Loading a saved or sent copy is not
    /// a creation and never comes here.</summary>
    public static EquipmentInstance CreateFresh(Item def)
    {
        var made = new EquipmentInstance(def);
        if (def == null) return made;
        if (def.slot == EquipSlotType.Weapon) made.trademarkSkillId = Trademarks.Roll(def.weaponType);
        else made.RollWord();
        made.Recompute();
        return made;
    }

    /// <summary>A NEW CHARACTER'S PIECE (<see cref="StarterKit.Worn"/>): Raw, unfolded, Common, and made with NO trademark
    /// and NO word — never through the rolling door, so making a character again can never roll its starting weapon's
    /// trademark again.</summary>
    public static EquipmentInstance CreateStarter(Item def) => new EquipmentInstance(def);

    /// <summary>THE PIECE A KILL DROPS: a fresh copy of <paramref name="def"/> exactly as <see cref="CreateFresh"/>
    /// makes one — Raw, unfolded, its own trademark or word rolled — at <paramref name="rarity"/>, the one thing a
    /// dropped piece rolls that no other piece does. Server only.</summary>
    public static EquipmentInstance CreateDropped(Item def, Rarity rarity)
    {
        var made = CreateFresh(def);
        made.rarity = rarity;
        made.Recompute();
        return made;
    }

    // Reused: the stats already written while one piece is being filled in. A piece is filled once per creature life.
    static readonly List<InkStat> inkScratch = new List<InkStat>(6);

    /// <summary>A copy of <paramref name="def"/> AT AN AUTHORED STATE: grade <paramref name="quality"/>, folded
    /// <paramref name="folds"/> times (never past that grade's cap), every stat slot the grade carries written with
    /// Ink of <paramref name="inkTier"/> — each stat rolled exactly as a station rolls one — and the trademark or
    /// word <see cref="CreateFresh"/> rolled, at any grade. Common, like everything <see cref="CreateFresh"/> makes.
    /// WHAT A CREATURE WEARS, from the three settings on its own catalog entry. Server only: every roll goes through
    /// CombatRng.</summary>
    public static EquipmentInstance CreateAt(Item def, Quality quality, int folds, int inkTier)
    {
        var made = CreateFresh(def);
        if (def == null) return made;
        made.quality = quality;
        made.upgrade = Mathf.Clamp(folds, 0, CraftingMath.FoldCap(quality));
        inkTier = Mathf.Clamp(inkTier, 1, CraftingMath.TopInkTier);
        inkScratch.Clear();
        for (int slot = 0; slot < made.InkSlots; slot++)
        {
            int weight = CraftingMath.RemainingInkWeight(inkScratch);
            if (weight <= 0 || !CraftingMath.RollInkStat(CombatRng.Range(0, weight), inkScratch, out var stat)) break;
            made.WriteInk(slot, stat, CraftingMath.InkValue(stat, inkTier));
            inkScratch.Add(stat);
        }
        inkScratch.Clear();
        // The trademark or the word was rolled by CreateFresh, at every grade — a Raw piece carries one too.
        made.Recompute();
        return made;
    }

    /// <summary>Work out what this copy's folds, grade and rarity are worth and write the answer onto it. THE SERVER
    /// calls this after making, folding or quenching a copy, and a player's machine never does: the finished numbers
    /// cross in the same text the grade does, and that side only copies them in.</summary>
    public void Recompute()
    {
        trademarkLift = string.IsNullOrEmpty(trademarkSkillId) ? 0f : CraftingMath.TrademarkLift(quality);
        piece = def == null ? default : CraftingMath.PieceNumbers(def.slot, def.weaponType, def.madeOf, upgrade, quality, rarity);
        wordAmount = word == ArmourWord.None ? 0f : ArmourWords.Amount(word, quality);
    }

    /// <summary>Roll this ARMOUR copy's word — every word of its slot equally likely, through the one random source.
    /// THE SERVER calls this once, when the copy is made; a weapon or an unworn item rolls nothing.</summary>
    public void RollWord()
    {
        // A piece made of nothing is worn for its look alone, and carries nothing.
        if (def == null || def.madeOf == MaterialKind.None) return;
        var words = ArmourWords.For(def.slot);
        word = words.Length == 0 ? ArmourWord.None : ArmourWords.Pick(def.slot, CombatRng.Range(0, words.Length));
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

    /// <summary>This copy's grade, THE FINISHED NUMBERS the server worked out for it, its written stats, its
    /// trademark, its word and its rarity, joined for the save and the wire — always six parts, a missing trademark or
    /// word written as empty text so every part keeps its place:
    /// "grade;attack,magic attack,defence,magic defence,HP,MP,evasion,attack speed,movement speed;stat:value,stat:value;trademark skill id:finished lift;word:finished amount;rarity".
    /// Paired with <see cref="DecodeRoll"/>. Kept in the same slot the armour roll used, so the save and the wire
    /// need no new array — and carrying the finished numbers here is what lets a player's machine draw them
    /// without working any of them out.</summary>
    public string EncodeRoll()
    {
        EnsureInkLists();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append(((int)quality).ToString(ci)).Append(';')
          .Append(piece.attack.ToString(ci)).Append(',').Append(piece.magicAttack.ToString(ci)).Append(',')
          .Append(piece.defense.ToString(ci)).Append(',').Append(piece.magicDefense.ToString(ci)).Append(',')
          .Append(piece.hp.ToString(ci)).Append(',').Append(piece.mp.ToString(ci)).Append(',')
          .Append(piece.evasion.ToString(ci)).Append(',').Append(piece.attackSpeedPercent.ToString("0.#", ci)).Append(',')
          .Append(piece.moveSpeedPercent.ToString("0.#", ci)).Append(';');
        for (int i = 0; i < inkStats.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(((int)inkStats[i]).ToString(ci)).Append(':').Append(inkValues[i].ToString("0.###", ci));
        }
        sb.Append(';');
        if (!string.IsNullOrEmpty(trademarkSkillId))
            sb.Append(trademarkSkillId).Append(':').Append(trademarkLift.ToString("0.####", ci));
        // The armour word, BY NAME, and the finished amount the server wrote for it.
        sb.Append(';');
        if (word != ArmourWord.None)
            sb.Append(word.ToString()).Append(':').Append(wordAmount.ToString("0.###", ci));
        sb.Append(';').Append(((int)rarity).ToString(ci));
        return sb.ToString();
    }

    /// <summary>Rebuild the grade, the written stats, the trademark, the word and the rarity from a saved string.
    /// Anything unreadable — "", a save written before crafting existed, a damaged entry — leaves the copy Raw and
    /// Common with nothing written on it, which is exactly what a piece nobody has worked on is.</summary>
    public void DecodeRoll(string encoded)
    {
        ReadRoll(encoded);
        RecomputeIfMine();
    }

    /// <summary>The reading half of <see cref="DecodeRoll"/>, with no engine call in it — every part laid onto the
    /// copy exactly as written, and nothing worked out again — so the offline checks can read a roll text back.</summary>
    public void ReadRoll(string encoded)
    {
        quality = Quality.Raw;
        rarity = Rarity.Common;
        piece = default;
        trademarkSkillId = "";
        trademarkLift = 0f;
        word = ArmourWord.None;
        wordAmount = 0f;
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
                // Four numbers from a save written before armour's material decided its folds; eight before shoes'
                // folds bought movement speed; nine since. The server works a piece's numbers out again on load
                // either way (RecomputeIfMine, below).
                var nums = parts[1].Split(',');
                if (nums.Length == 4 || nums.Length >= 8)
                {
                    int.TryParse(nums[0], ints, ci, out piece.attack);
                    int.TryParse(nums[1], ints, ci, out piece.magicAttack);
                    int.TryParse(nums[2], ints, ci, out piece.defense);
                    int.TryParse(nums[3], ints, ci, out piece.magicDefense);
                }
                if (nums.Length >= 8)
                {
                    int.TryParse(nums[4], ints, ci, out piece.hp);
                    int.TryParse(nums[5], ints, ci, out piece.mp);
                    int.TryParse(nums[6], ints, ci, out piece.evasion);
                    float.TryParse(nums[7], System.Globalization.NumberStyles.Float, ci, out piece.attackSpeedPercent);
                }
                if (nums.Length >= 9)
                    float.TryParse(nums[8], System.Globalization.NumberStyles.Float, ci, out piece.moveSpeedPercent);
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

            // The trademark: which skill, and the finished lift the server wrote for it. A save written before
            // trademarks existed has no such part — that copy simply has none.
            if (parts.Length > 3 && !string.IsNullOrEmpty(parts[3]))
            {
                int colon = parts[3].LastIndexOf(':');
                trademarkSkillId = colon > 0 ? parts[3].Substring(0, colon) : parts[3];
                if (colon > 0)
                    float.TryParse(parts[3].Substring(colon + 1), System.Globalization.NumberStyles.Float, ci,
                                   out trademarkLift);
            }

            // The armour word. A save written before words existed has no such part — that copy simply has none. A
            // name the game no longer has is dropped rather than guessed at.
            if (parts.Length > 4 && !string.IsNullOrEmpty(parts[4]))
            {
                var bits = parts[4].Split(':');
                if (System.Enum.TryParse(bits[0], out ArmourWord w) && System.Enum.IsDefined(typeof(ArmourWord), w))
                {
                    word = w;
                    if (bits.Length > 1)
                        float.TryParse(bits[1], System.Globalization.NumberStyles.Float, ci, out wordAmount);
                }
            }

            // The rarity, read before the numbers are worked out again below because it lifts them. A text with no
            // such part is a Common piece.
            if (parts.Length > 5 && int.TryParse(parts[5], ints, ci, out int r)
                && r >= (int)Rarity.Common && r <= (int)Rarity.Legendary)
                rarity = (Rarity)r;
        }
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
    /// finished attack / magic attack / defence / magic defence the server worked out from its folds, grade and rarity,
    /// and whatever Ink has written on it. The item's own bonus fields are not read: a crafted piece IS its work.
    /// Allocation-free.</summary>
    public void AddPieceInto(ref EquipBonuses eb)
    {
        if (def == null) return;
        CraftingMath.AddPiece(piece, inkStats, inkValues, ref eb);
        ArmourWords.AddWord(word, wordAmount, ref eb);
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
    /// <summary>What the worn pieces' rolled WORDS add (see <see cref="ArmourWords"/>): whole percent, except MP per
    /// second (Wellspring) and HP per second (Wayfarer). Read through the unit's state store, which folds them
    /// into the same sub-stat table a timed buff moves — so every reader of those stats gets worn words for free.</summary>
    public float wordCritShieldPercent, wordDisableCutPercent, wordMPRegen, wordDamageCutPercent, wordLifeStealPercent,
                 wordDamagePercent, wordStaminaCutPercent, wordShoveCutPercent, wordHPRegen;
    /// <summary>Whole-percent movement speed the worn shoes' folds bought (see <see cref="CraftingMath.PieceNumbers"/>),
    /// read through the same sub-stat table as the words above.</summary>
    public float moveSpeedPercent;
    /// <summary>Whole-percent cut to damage of one element, from a garment's ward word.</summary>
    public float wardFirePercent, wardWaterPercent, wardEarthPercent, wardWindPercent;

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
