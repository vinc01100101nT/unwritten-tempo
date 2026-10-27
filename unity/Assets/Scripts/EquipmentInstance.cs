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
             "piece's own attack or defence. A fold from +4 can fail; a quench is allowed only at +10.")]
    public int upgrade;
    [Tooltip("Inserted cards (length <= cardSlots).")]
    public List<Card> cards = new List<Card>();
    [Tooltip("How many card slots this copy has, decided when it is made (a creature's worn piece rolls them) or added " +
             "at the Smith, never above the slot's cap: a weapon 4, an armour piece 2.")]
    public int cardSlots;
    [Tooltip("What this copy came out of its last quench as. A new piece is Raw and has never been quenched; F to A " +
             "is the ladder above it.")]
    public Quality quality = Quality.Raw;

    [Tooltip("The stats written onto this copy with Ink, in the order they were written — the LAST one is the one a " +
             "failed quench removes. Parallel to Ink Tiers and Ink Values.")]
    public List<InkStat> inkStats = new List<InkStat>();
    [Tooltip("Which Ink wrote each stat, 1 (Watery) to 5 (Sharp). Parallel to Ink Stats.")]
    public List<int> inkTiers = new List<int>();
    [Tooltip("What each written stat is worth (server-written, from its Ink and this copy's grade). A percentage stat " +
             "is whole percent. Parallel to Ink Stats.")]
    public List<float> inkValues = new List<float>();
    [Tooltip("What this copy is worth (server-written): exactly what recycling it gives back — its gold, and its Temper " +
             "Dust, Quench Oil and Inks at their own values. What a shop prices it at.")]
    public int worth;

    // THE SERVER'S FINISHED ANSWER for what this copy's folds and grade are worth, written whenever it is
    // made, folded or quenched (Recompute). A player's machine never works these out — it adds what it was sent.
    [Tooltip("Finished numbers this copy's folds and grade grant (server-written): attack or magic attack on a weapon; " +
             "on armour, what its material buys — hard defence and HP (metal), attack speed and evasion (leather), hard " +
             "magic defence and MP (cloth).")]
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

    [Tooltip("What this copy's base rolled, in whole percent (85 to 115), when a creature dropped it — rolled once and " +
             "never changed. 0 = never rolled: what a creature wears, a starter, shop, reward or GM piece, counted as 100.")]
    public int basePercent;
    [Tooltip("Finished numbers of this copy's base alone — base × roll, before folds and quenches " +
             "(server-written): what the tooltip's Base line shows.")]
    public CraftingMath.PieceStats baseStats;

    public EquipmentInstance() { }
    public EquipmentInstance(Item def, int upgrade = 0)
    {
        this.def = def;
        this.upgrade = upgrade;
        Recompute();
    }

    /// <summary>THE ONE WAY a copy becomes the four values a save, a message or a kept sale holds for it beside its item
    /// id: its folds, its cards, its roll text (grade, written stats, trademark, word, base) and its own id. Gives the copy
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
    /// piece comes through — a grant, a reward, what a creature spawns wearing (<see cref="CreateWorn"/>). What a
    /// creature drops is a copy of what it wore (<see cref="CloneAsNewCopy"/>). Server only: the roll goes through CombatRng. Loading a saved or sent copy is not
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

    /// <summary>A NEW CHARACTER'S PIECE (<see cref="StarterKit.Worn"/>): Raw, folded as the kit says, and made with NO trademark
    /// and NO word — never through the rolling door, so making a character again can never roll its starting weapon's
    /// trademark again.</summary>
    public static EquipmentInstance CreateStarter(Item def, int folds)
    {
        return new EquipmentInstance(def, Mathf.Clamp(folds, 0, CraftingMath.FoldCap(Quality.Raw)));
    }

    /// <summary>WHAT A CREATURE WEARS, rolled each life: <see cref="CreateAt"/> at grade <paramref name="quality"/> and
    /// Ink <paramref name="inkTier"/>, with its folds, card slots and base roll (85% to 115%) drawn by the creature's
    /// <paramref name="kind"/>. Whatever it drops is a copy of this, so a creature fights with exactly what it drops.
    /// Server only: every roll goes through CombatRng.</summary>
    public static EquipmentInstance CreateWorn(Item def, CreatureKind kind, Quality quality, int inkTier)
    {
        // server-math: worn gear is rolled on the server only (UnitEquipment.SeatStartingGear)
        int folds = CreatureLoot.RollWornFolds(kind, CombatRng.Range(0, 1000));
        var made = CreateAt(def, quality, folds, inkTier);
        if (def == null) return made;
        // server-math: worn gear is rolled on the server only, and the roll is the piece's own base
        made.basePercent = CombatRng.Range(CraftingMath.RollMin, CraftingMath.RollMax + 1);
        made.cardSlots = CreatureLoot.RollWornCardSlots(def.slot, kind, CombatRng.Range(0, 100));
        made.Recompute();
        return made;
    }

    /// <summary>A NEW COPY that is identical to this one — grade, folds, Ink lines, trademark, word, base roll, card
    /// slots and the finished numbers — with its own id and no cards. What a creature drops: a copy of what it wore.</summary>
    public EquipmentInstance CloneAsNewCopy()
    {
        EnsureInkLists();
        var made = new EquipmentInstance
        {
            def = def, upgrade = upgrade, quality = quality, piece = piece, baseStats = baseStats,
            trademarkSkillId = trademarkSkillId, trademarkLift = trademarkLift, word = word, wordAmount = wordAmount,
            basePercent = basePercent, cardSlots = cardSlots, worth = worth,
        };
        made.inkStats.AddRange(inkStats);
        made.inkTiers.AddRange(inkTiers);
        made.inkValues.AddRange(inkValues);
        return made;
    }

    // Reused: the stats already written while one piece is being filled in. A piece is filled once per creature life.
    static readonly List<InkStat> inkScratch = new List<InkStat>(6);

    /// <summary>A copy of <paramref name="def"/> AT AN AUTHORED STATE: grade <paramref name="quality"/>, folded
    /// <paramref name="folds"/> times (never past that grade's cap), every stat slot the grade carries written with
    /// Ink of <paramref name="inkTier"/> — each stat rolled exactly as a station rolls one — and the trademark or
    /// word <see cref="CreateFresh"/> rolled, at any grade.
    /// WHAT A CREATURE WEARS, from the three settings on its own catalog entry. Server only: every roll goes through
    /// CombatRng.</summary>
    public static EquipmentInstance CreateAt(Item def, Quality quality, int folds, int inkTier)
    {
        var made = CreateFresh(def);
        if (def == null) return made;
        made.quality = quality;
        made.upgrade = Mathf.Clamp(folds, 0, CraftingMath.FoldCap(quality));
        // tier 0 writes no Ink at all
        inkTier = Mathf.Clamp(inkTier, 0, CraftingMath.TopInkTier);
        inkScratch.Clear();
        for (int slot = 0; inkTier > 0 && slot < made.InkSlots; slot++)
        {
            int weight = CraftingMath.RemainingInkWeight(inkScratch);
            if (weight <= 0 || !CraftingMath.RollInkStat(CombatRng.Range(0, weight), inkScratch, out var stat)) break;
            made.WriteInk(slot, stat, inkTier);
            inkScratch.Add(stat);
        }
        inkScratch.Clear();
        // The trademark or the word was rolled by CreateFresh, at every grade — a Raw piece carries one too.
        made.Recompute();
        return made;
    }

    /// <summary>Work out what this copy's folds, grade and Ink lines are worth and write the answer onto it. THE SERVER
    /// calls this after making, folding or quenching a copy, and a player's machine never does: the finished numbers
    /// cross in the same text the grade does, and that side only copies them in.</summary>
    public void Recompute()
    {
        trademarkLift = string.IsNullOrEmpty(trademarkSkillId) ? 0f : CraftingMath.TrademarkLift(quality);
        if (def == null) { piece = default; baseStats = default; }
        else
        {
            piece = NumbersAt(upgrade, quality);
            Swing(def, out int ms, out int offHand);
            baseStats = CraftingMath.BaseNumbers(def.slot, def.weaponType, def.madeOf, basePercent, ms, offHand);
        }
        wordAmount = word == ArmourWord.None ? 0f : ArmourWords.Amount(word, quality);
        // every Ink line is worth its Ink at THIS grade, so a quench rescales what is written
        EnsureInkLists();
        for (int i = 0; i < inkStats.Count; i++) inkValues[i] = CraftingMath.InkValue(inkStats[i], quality, inkTiers[i]);
        worth = def != null ? WorthNow() : 0;
    }

    // Reused while a worth is worked out — the server works one copy out at a time.
    static readonly int[] worthInks = new int[CraftingMath.TopInkTier + 1];

    // What recycling this copy gives back, priced at each thing's own value.
    int WorthNow()
    {
        CraftingMath.RecycleYield(quality, upgrade, out int dust, out int oil, out int gold);
        CraftingMath.RecycleInks(inkTiers, worthInks);
        var dustItem = MaterialCatalog.TemperDust;
        var oilItem = MaterialCatalog.QuenchOil;
        long total = CraftingMath.WorthOf(gold, dust, dustItem != null ? dustItem.value : 0, oil, oilItem != null ? oilItem.value : 0);
        for (int t = 1; t < worthInks.Length; t++)
        {
            var ink = MaterialCatalog.Ink(t);
            total += (long)worthInks[t] * (ink != null ? ink.value : 0);
        }
        return (int)System.Math.Min(int.MaxValue, total);
    }

    /// <summary>What this copy would be worth at <paramref name="folds"/> folds of grade <paramref name="q"/> — its own
    /// base and roll kept. The one function both its own numbers and a station's "after this press" preview read.
    /// Server and editor only, like <see cref="Recompute"/>.</summary>
    public CraftingMath.PieceStats NumbersAt(int folds, Quality q)
    {
        if (def == null) return default;
        Swing(def, out int ms, out int offHand);
        return CraftingMath.PieceNumbers(def.slot, def.weaponType, def.madeOf, folds, q, basePercent, ms, offHand);
    }

    // A weapon's swing time and its off hand's power, as the whole thousandths a piece's worth is worked out from.
    static void Swing(Item def, out int ms, out int offHand)
    {
        var spec = CharacterSpec.Active;
        ms = CraftingMath.Thousandths(UnitOffense.SwingSeconds(def.weaponType, def.weight));
        offHand = spec != null ? CraftingMath.Thousandths(spec.offHandPower) : 0;
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

    /// <summary>Write one stat onto this copy with Ink of <paramref name="tier"/> (1..5), at <paramref name="slot"/>. A
    /// slot already holding a stat is replaced (and may take the same stat again); the first empty slot is filled.
    /// Refused when the grade has no such slot, or when the stat is already in another slot. Server only: the value
    /// is worked out here, from the Ink and this copy's grade.</summary>
    public bool WriteInk(int slot, InkStat stat, int tier)
    {
        tier = Mathf.Clamp(tier, 1, CraftingMath.TopInkTier);
        float value = CraftingMath.InkValue(stat, quality, tier);
        EnsureInkLists();
        if (slot < 0 || slot >= InkSlots) return false;
        for (int i = 0; i < inkStats.Count; i++)
            if (inkStats[i] == stat && i != slot) return false;

        // A SLOT PAST THE END IS THE FIRST EMPTY ONE. The written stats are a list, not a grid with holes in it:
        // padding the gap with copies of the stat being written would put the same stat on the piece several
        // times, five of them worth nothing, and read as a full piece.
        if (slot > inkStats.Count) slot = inkStats.Count;
        if (slot == inkStats.Count) { inkStats.Add(stat); inkTiers.Add(tier); inkValues.Add(value); return true; }
        inkStats[slot] = stat;
        inkTiers[slot] = tier;
        inkValues[slot] = value;
        return true;
    }

    /// <summary>Drop the LAST stat written on this copy — what a quench that lowers the grade takes away.</summary>
    public void DropLastInk()
    {
        EnsureInkLists();
        if (inkStats.Count == 0) return;
        inkStats.RemoveAt(inkStats.Count - 1);
        inkTiers.RemoveAt(inkTiers.Count - 1);
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
        if (inkTiers == null) inkTiers = new List<int>();
        if (inkValues == null) inkValues = new List<float>();
        while (inkTiers.Count < inkStats.Count) inkTiers.Add(1);
        while (inkTiers.Count > inkStats.Count) inkTiers.RemoveAt(inkTiers.Count - 1);
        while (inkValues.Count < inkStats.Count) inkValues.Add(0f);
        while (inkValues.Count > inkStats.Count) inkValues.RemoveAt(inkValues.Count - 1);
    }

    /// <summary>This copy's grade, THE FINISHED NUMBERS the server worked out for it, its written stats, its
    /// trademark, its word, its base and its card slots, joined for the save and the wire — always seven parts, a missing
    /// trademark or word written as empty text so every part keeps its place:
    /// "grade;attack,magic attack,hard defence,hard magic defence,HP,MP,evasion,attack speed;stat:tier:value,…;trademark skill id:finished lift;word:finished amount;base roll:base attack,…,base attack speed;card slots:worth".
    /// Hard defence is in tenths of a percent.
    /// Paired with <see cref="DecodeRoll"/>. Kept in the same slot the armour roll used, so the save and the wire
    /// need no new array — and carrying the finished numbers here is what lets a player's machine draw them
    /// without working any of them out.</summary>
    public string EncodeRoll()
    {
        EnsureInkLists();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append(((int)quality).ToString(ci)).Append(';');
        AppendNumbers(sb, piece, ci);
        sb.Append(';');
        for (int i = 0; i < inkStats.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(((int)inkStats[i]).ToString(ci)).Append(':').Append(inkTiers[i].ToString(ci)).Append(':')
              .Append(inkValues[i].ToString("0.###", ci));
        }
        sb.Append(';');
        if (!string.IsNullOrEmpty(trademarkSkillId))
            sb.Append(trademarkSkillId).Append(':').Append(trademarkLift.ToString("0.####", ci));
        // The armour word, BY NAME, and the finished amount the server wrote for it.
        sb.Append(';');
        if (word != ArmourWord.None)
            sb.Append(word.ToString()).Append(':').Append(wordAmount.ToString("0.###", ci));
        // The base's roll and the finished base numbers the tooltip's Base line copies.
        sb.Append(';').Append(basePercent.ToString(ci)).Append(':');
        AppendNumbers(sb, baseStats, ci);
        sb.Append(';').Append(cardSlots.ToString(ci)).Append(':').Append(worth.ToString(ci));
        return sb.ToString();
    }

    static void AppendNumbers(System.Text.StringBuilder sb, in CraftingMath.PieceStats p, System.IFormatProvider ci)
    {
        sb.Append(p.attack.ToString(ci)).Append(',').Append(p.magicAttack.ToString(ci)).Append(',')
          .Append(p.hardDefenseTenths.ToString(ci)).Append(',').Append(p.hardMagicDefenseTenths.ToString(ci)).Append(',')
          .Append(p.hp.ToString(ci)).Append(',').Append(p.mp.ToString(ci)).Append(',')
          .Append(p.evasion.ToString(ci)).Append(',').Append(p.attackSpeedPercent.ToString("0.#", ci));
    }

    static void ReadNumbers(string text, ref CraftingMath.PieceStats p)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var ints = System.Globalization.NumberStyles.Integer;
        var nums = text.Split(',');
        if (nums.Length < 8) return;
        int.TryParse(nums[0], ints, ci, out p.attack);
        int.TryParse(nums[1], ints, ci, out p.magicAttack);
        int.TryParse(nums[2], ints, ci, out p.hardDefenseTenths);
        int.TryParse(nums[3], ints, ci, out p.hardMagicDefenseTenths);
        int.TryParse(nums[4], ints, ci, out p.hp);
        int.TryParse(nums[5], ints, ci, out p.mp);
        int.TryParse(nums[6], ints, ci, out p.evasion);
        float.TryParse(nums[7], System.Globalization.NumberStyles.Float, ci, out p.attackSpeedPercent);
    }

    /// <summary>Rebuild the grade, the written stats, the trademark, the word and the base from a saved string.
    /// Anything unreadable — "" or a damaged entry — leaves the copy Raw with nothing written on it, which is exactly
    /// what a piece nobody has worked on is.</summary>
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
        basePercent = 0;
        cardSlots = 0;
        worth = 0;
        baseStats = default;
        piece = default;
        trademarkSkillId = "";
        trademarkLift = 0f;
        word = ArmourWord.None;
        wordAmount = 0f;
        EnsureInkLists();
        inkStats.Clear();
        inkTiers.Clear();
        inkValues.Clear();
        if (!string.IsNullOrEmpty(encoded))
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var ints = System.Globalization.NumberStyles.Integer;
            var parts = encoded.Split(';');
            if (int.TryParse(parts[0], ints, ci, out int q))
                quality = (Quality)Mathf.Clamp(q, (int)Quality.Raw, (int)Quality.A);

            // The finished numbers, exactly as the server wrote them.
            if (parts.Length > 1) ReadNumbers(parts[1], ref piece);

            if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2]))
                foreach (var pair in parts[2].Split(','))
                {
                    var bits = pair.Split(':');
                    if (bits.Length != 3) continue;
                    if (!int.TryParse(bits[0], ints, ci, out int s)) continue;
                    if (!int.TryParse(bits[1], ints, ci, out int t)) continue;
                    if (!float.TryParse(bits[2], System.Globalization.NumberStyles.Float, ci, out float v)) continue;
                    // EVERY stat the enum names, whatever its number. A ceiling typed here once stopped at LUK
                    // and silently threw away Magic Defence and Crit Damage — the two that sit after it.
                    if (!System.Enum.IsDefined(typeof(InkStat), s)) continue;
                    inkStats.Add((InkStat)s);
                    inkTiers.Add(Mathf.Clamp(t, 1, CraftingMath.TopInkTier));
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

            // The base's roll and its finished numbers. A text with no such part never rolled: 0, counted as 100.
            if (parts.Length > 5)
            {
                int colon = parts[5].IndexOf(':');
                string pct = colon >= 0 ? parts[5].Substring(0, colon) : parts[5];
                if (int.TryParse(pct, ints, ci, out int roll)
                    && (roll == 0 || (roll >= CraftingMath.RollMin && roll <= CraftingMath.RollMax)))
                    basePercent = roll;
                if (colon >= 0) ReadNumbers(parts[5].Substring(colon + 1), ref baseStats);
            }

            // Card slots, then the finished worth. A text with no such part has no slots and is worth nothing.
            if (parts.Length > 6)
            {
                var bits = parts[6].Split(':');
                if (int.TryParse(bits[0], ints, ci, out int slots))
                    cardSlots = Mathf.Clamp(slots, 0, def != null ? CraftingMath.CardSlotCap(def.slot) : 4);
                if (bits.Length > 1) int.TryParse(bits[1], ints, ci, out worth);
            }
        }
    }

    public int CardSlots => cardSlots;
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
            if (cards[i] != null) cards[i].AddInto(ref eb);
    }

    /// <summary>What THIS copy itself adds, before its cards — the one place a copy's own numbers are read, by the
    /// stat sheet, the equip preview, the tooltip and the inventory column alike. Two things and no more: the
    /// finished attack / magic attack / hard defence / hard magic defence the server worked out from its folds and grade,
    /// and whatever Ink has written on it. The item's own bonus fields are not read: a crafted piece IS its work.
    /// Allocation-free.</summary>
    public void AddPieceInto(ref EquipBonuses eb)
    {
        if (def == null) return;
        CraftingMath.AddPiece(piece, inkStats, inkValues, ref eb);
        ArmourWords.AddWord(word, wordAmount, ref eb);
        Headgear.AddInto(def, ref eb);
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
    public int hp, mp, atk, matk;
    /// <summary>Worn HARD defence (the percentage part, see <see cref="Combat.Mitigated"/>) in tenths of a percent:
    /// armour, Ink lines and the Bulwark word. 60 = 6.0%.</summary>
    public int hardDefTenths, hardMdefTenths;
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
    public float wordCritShieldPercent, wordDisableCutPercent, wordMPRegen, wordLifeStealPercent,
                 wordDamagePercent, wordStaminaCutPercent, wordShoveCutPercent, wordHPRegen;
    /// <summary>Whole-percent cut to damage of one element, from a garment's ward word.</summary>
    public float wardFirePercent, wardWaterPercent, wardEarthPercent, wardWindPercent;
    /// <summary>What worn headgear adds (<see cref="Headgear"/>): world units of basic attack reach, and whole percent
    /// more experience from every kill.</summary>
    public float attackReach, experiencePercent;

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
