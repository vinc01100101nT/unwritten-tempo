using UnityEngine;

/// <summary>
/// THE ONE PLACE EVERY CRAFTING NUMBER IS WORKED OUT. Plain functions over plain numbers — no component, no
/// scene, no engine call — so a fold, a quench, an Ink or a merge can be checked by calling a function rather
/// than by standing the whole game up.
///
/// <para>Every value here is the first table recorded in the V4 design (docs/2-systems/gameplay-system-v4), to be
/// tuned in play. Change it HERE: nothing else keeps a second copy.</para>
/// </summary>
public static class CraftingMath
{
    // ---- merging ------------------------------------------------------------------------------------------
    /// <summary>How many of one tier make one of the next, for the two materials that merge (Ink and Salt).
    /// Deterministic: ten always become one, and nothing can fail.</summary>
    public const int MergeCount = 10;

    /// <summary>True when <paramref name="kind"/> is a material whose tiers merge upward. A band material never
    /// merges: its band is gated by the creatures that drop it, and merging would let a starter field feed a
    /// top-grade piece.</summary>
    public static bool Merges(MaterialKind kind) => kind == MaterialKind.Ink || kind == MaterialKind.Salt;

    // ---- the grade ladder ---------------------------------------------------------------------------------
    /// <summary>The highest grade there is. Nothing quenches past it.</summary>
    public const Quality Best = Quality.A;

    // Indexed by (int)Quality: Raw, F, E, D, C, B, A.
    static readonly int[] FoldCaps      = { 5, 10, 15, 20, 25, 30, 35 };
    static readonly float[] Lifts       = { 0f, 0f, 0.05f, 0.10f, 0.15f, 0.22f, 0.30f };
    static readonly int[] Slots         = { 0, 1, 2, 3, 4, 5, 6 };
    static readonly int[] WeaponCaps    = { 10, 20, 30, 40, 50, 60, 70 };
    static readonly int[] SkillCaps     = { 4, 8, 12, 16, 20, 25, 30 };
    static readonly float[] Speeds      = { 0.5f, 0.75f, 1f, 1.1f, 1.2f, 1.35f, 1.5f };

    static readonly float[] TrademarkLifts = { 0.05f, 0.07f, 0.10f, 0.12f, 0.15f, 0.18f, 0.20f };

    static int Index(Quality q) => Mathf.Clamp((int)q, 0, FoldCaps.Length - 1);

    // ---- the trademark ------------------------------------------------------------------------------------
    /// <summary>What a weapon's trademark adds to every main number of its skill, by the weapon's grade — +5% at
    /// Raw to +20% at A. Added to the skill's practice lift, never multiplied onto it.</summary>
    public static float TrademarkLift(Quality q) => TrademarkLifts[Index(q)];

    /// <summary>Each ULTIMATE's chance, in percent, of being the trademark a forge rolls. The ordinary skills share
    /// whatever the ultimates leave.</summary>
    public const int UltimateTrademarkPercent = 5;

    /// <summary>One candidate's weight in the forge's trademark roll, out of a total of 100 × <paramref name="ordinaries"/>:
    /// an ultimate is worth <see cref="UltimateTrademarkPercent"/>%, an ordinary skill an even share of the rest.
    /// Whole numbers, so the roll is one integer draw. With no ordinary skill, or ultimates that would eat the whole
    /// roll, every candidate weighs the same.</summary>
    public static int TrademarkWeight(bool ultimate, int ordinaries, int ultimates)
    {
        if (ordinaries <= 0 || ultimates * UltimateTrademarkPercent >= 100) return 1;
        return ultimate ? UltimateTrademarkPercent * ordinaries : 100 - UltimateTrademarkPercent * ultimates;
    }

    /// <summary>The most folds this grade allows — and, because a piece cannot be folded past its grade, also
    /// the point at which the only way further is a quench.</summary>
    public static int FoldCap(Quality q) => FoldCaps[Index(q)];

    /// <summary>What this grade adds to the piece's fold total, as a fraction (0.30 = +30%).</summary>
    public static float QualityLift(Quality q) => Lifts[Index(q)];

    /// <summary>How many stats this grade lets a piece carry.</summary>
    public static int InkSlots(Quality q) => Slots[Index(q)];

    /// <summary>The highest Weapon Mastery level a weapon of this grade lets its wielder use — and earn.</summary>
    public static int WeaponMasteryCap(Quality q) => WeaponCaps[Index(q)];

    /// <summary>The highest Skill Mastery level a weapon of this grade lets its wielder use — and earn.</summary>
    public static int SkillMasteryCap(Quality q) => SkillCaps[Index(q)];

    /// <summary>How fast a weapon of this grade trains both masteries (1 = par).</summary>
    public static float TrainingSpeed(Quality q) => Speeds[Index(q)];

    /// <summary>Which material band this grade folds with: Raw folds with band 1, A with band 7. Also the
    /// multiplier on what a fold and a quench cost in gold.</summary>
    public static int Band(Quality q) => Index(q) + 1;

    // ---- what a fold and a quench cost --------------------------------------------------------------------
    const int GoldPerFoldPerBand = 50, GoldPerQuenchPerBand = 500;
    /// <summary>Units of the grade's band material one fold costs.</summary>
    public const int FoldMaterialCost = 1;
    /// <summary>Units of the grade's band material one quench costs.</summary>
    public const int QuenchMaterialCost = 10;

    public static int FoldGoldCost(Quality q) => GoldPerFoldPerBand * Band(q);
    public static int QuenchGoldCost(Quality q) => GoldPerQuenchPerBand * Band(q);

    // ---- what a fold is worth on the piece ----------------------------------------------------------------
    // A weapon gains attack per fold — magic attack instead when it is a caster's weapon, which is the only
    // place a weapon TYPE changes what a fold means. Armour's place decides which defence it gains, and a Head
    // gains one of each every second fold.
    /// <summary>The piece's own attack, magic attack, defence and magic defence at <paramref name="folds"/>
    /// folds of grade <paramref name="q"/> — the finished numbers, lift included, as the server writes them
    /// onto the copy. Rounds half UP, deliberately: <c>Mathf.RoundToInt</c> rounds .5 to the even number.</summary>
    public static void PieceNumbers(EquipSlotType slot, WeaponType weapon, int folds, Quality q,
                                    out int attack, out int magicAttack, out int defense, out int magicDefense)
    {
        attack = magicAttack = defense = magicDefense = 0;
        if (folds <= 0) return;
        float lift = 1f + QualityLift(q);
        switch (slot)
        {
            case EquipSlotType.Weapon:
                if (IsCasterWeapon(weapon)) magicAttack = Lifted(folds, lift);
                else attack = Lifted(folds, lift);
                break;
            case EquipSlotType.Armor:
            case EquipSlotType.Shield:
                defense = Lifted(folds, lift);
                break;
            case EquipSlotType.Garment:
                magicDefense = Lifted(folds, lift);
                break;
            case EquipSlotType.Head:
                defense = magicDefense = Lifted(folds / 2, lift);
                break;
        }
    }

    /// <summary>A caster's weapon gains MAGIC attack from a fold rather than attack.</summary>
    public static bool IsCasterWeapon(WeaponType w) => w == WeaponType.Staff;

    static int Lifted(int flat, float lift) => Mathf.FloorToInt(flat * lift + 0.5f);

    // ---- the quench ---------------------------------------------------------------------------------------
    // Chance in whole percent that a quench of THIS grade goes up, stays, or goes down. F is the floor: a failed
    // F stays F. A is the top and is never quenched at all.
    static readonly int[] UpPct   = { 80, 60, 50, 40, 35, 25 };
    static readonly int[] DownPct = {  0,  0, 20, 25, 30, 35 };

    /// <summary>Salt's shift toward "up", in whole percentage points, by its tier (1 Coarse, 2 Fine, 3 Pure).
    /// 0 for no Salt at all.</summary>
    public static int SaltShift(int tier) => tier <= 0 ? 0 : tier >= 3 ? 30 : tier == 2 ? 20 : 10;

    /// <summary>The floor "down" can never be pushed below by Salt, where a down outcome exists at all.</summary>
    public const int DownFloorPct = 5;

    /// <summary>The most "up" a quench can ever be, however much Salt goes in. A quench is never a certainty.</summary>
    public const int MaxUpPct = 95;

    /// <summary>True when a piece of this grade could ever be quenched. A is the top of the ladder.</summary>
    public static bool CanQuench(Quality q) => q < Best;

    /// <summary>True when a piece may be quenched RIGHT NOW: its grade is below A, and it has been folded to that
    /// grade's cap. THE FOLD CAP IS THE QUENCH GATE (V4 §2) — the folds are what a quench spends, so a piece that
    /// has not done the work cannot buy the roll.</summary>
    public static bool ReadyToQuench(Quality q, int folds) => CanQuench(q) && folds >= FoldCap(q);

    /// <summary>The three chances of one quench, in whole percent, summing to 100: up a grade, unchanged, down a
    /// grade. <paramref name="saltTier"/> shifts points into "up", taken from "same" first and then from "down",
    /// and never pushes "down" below <see cref="DownFloorPct"/> where a down outcome exists.</summary>
    public static void QuenchOdds(Quality q, int saltTier, out int up, out int same, out int down)
    {
        int i = Index(q);
        if (i >= UpPct.Length) { up = 0; same = 100; down = 0; return; }
        up = UpPct[i]; down = DownPct[i]; same = 100 - up - down;

        int shift = SaltShift(saltTier);
        int fromSame = Mathf.Min(shift, same);
        same -= fromSame; shift -= fromSame;
        if (shift > 0 && down > 0)
        {
            int fromDown = Mathf.Min(shift, Mathf.Max(0, down - DownFloorPct));
            down -= fromDown; shift -= fromDown;
        }
        up = 100 - same - down;
        // However much Salt goes in, a quench is never a certainty — the excess goes back to "unchanged".
        if (up > MaxUpPct) { same += up - MaxUpPct; up = MaxUpPct; }
    }

    /// <summary>The grade a quench lands on, given a roll of 0..99. Never moves more than one rung, and never
    /// below F once F has been reached.</summary>
    public static Quality QuenchResult(Quality q, int saltTier, int roll0To99)
    {
        QuenchOdds(q, saltTier, out int up, out _, out int down);
        if (roll0To99 < up) return (Quality)Mathf.Min((int)Best, (int)q + 1);
        if (roll0To99 >= 100 - down) return (Quality)Mathf.Max((int)Quality.F, (int)q - 1);
        return q;
    }

    // ---- Ink ----------------------------------------------------------------------------------------------
    // What one Ink writes, by stat, for tiers 1..5 (Watery, Thin, Faint, Clear, Sharp). A stat that is a
    // PERCENTAGE is stored as whole percent — Critical 0.6 means +0.6% crit chance.
    static readonly float[] Ones      = { 2, 4, 6, 8, 10 };       // Attack, Magic Attack, both defences, Accuracy, Evasion
    static readonly float[] Health    = { 16, 32, 48, 64, 80 };
    static readonly float[] Mana      = { 8, 16, 24, 32, 40 };
    static readonly float[] Crit      = { 0.6f, 1.2f, 1.8f, 2.4f, 3f };
    static readonly float[] Speed     = { 1, 2, 3, 4, 5 };        // Attack Speed, Cast Speed
    static readonly float[] Primary   = { 3, 6, 9, 12, 15 };      // the premium: 1.5× the Attack line

    /// <summary>What one Ink of <paramref name="tier"/> (1..5) writes for <paramref name="stat"/>. A percentage
    /// stat comes back as whole percent.</summary>
    public static float InkValue(InkStat stat, int tier)
    {
        int t = Mathf.Clamp(tier, 1, 5) - 1;
        switch (stat)
        {
            case InkStat.HP:          return Health[t];
            case InkStat.MP:          return Mana[t];
            // Both halves of the ring's Critical node are percentages and both are worth the same per Ink.
            case InkStat.CritChance:
            case InkStat.CritDamage:  return Crit[t];
            case InkStat.AttackSpeed:
            case InkStat.CastSpeed:   return Speed[t];
            case InkStat.STR: case InkStat.AGI: case InkStat.VIT:
            case InkStat.INT: case InkStat.DEX: case InkStat.LUK: return Primary[t];
            default:                  return Ones[t];
        }
    }

    /// <summary>FOLD ONE WRITTEN STAT INTO A GEAR TOTAL — the one place an Ink line becomes a number, so the
    /// stat sheet, the item card and the equip preview all read the same answer. Plain arithmetic over plain
    /// numbers, which is why it lives here rather than on the copy that carries the line.</summary>
    public static void AddInk(InkStat stat, float value, ref EquipBonuses eb)
    {
        switch (stat)
        {
            case InkStat.Defense:     eb.def += (int)value; break;
            case InkStat.MagicDefense: eb.mdef += (int)value; break;
            case InkStat.CritDamage:  eb.critDamagePercent += value; break;
            case InkStat.HP:          eb.hp += (int)value; break;
            case InkStat.Attack:      eb.atk += (int)value; break;
            case InkStat.Accuracy:    eb.accuracy += (int)value; break;
            case InkStat.CastSpeed:   eb.castSpeedPercent += value; break;
            case InkStat.MagicAttack: eb.matk += (int)value; break;
            case InkStat.MP:          eb.mp += (int)value; break;
            case InkStat.Evasion:     eb.evasion += (int)value; break;
            case InkStat.CritChance:  eb.critPercent += value; break;
            case InkStat.AttackSpeed: eb.aspdPercent += value; break;
            case InkStat.STR:         eb.str += (int)value; break;
            case InkStat.AGI:         eb.agi += (int)value; break;
            case InkStat.VIT:         eb.vit += (int)value; break;
            case InkStat.INT:         eb.intel += (int)value; break;
            case InkStat.DEX:         eb.dex += (int)value; break;
            case InkStat.LUK:         eb.luk += (int)value; break;
        }
    }

    /// <summary>The whole of what one crafted copy adds: the finished numbers its folds and grade are worth, and
    /// every stat written on it. THE one place a piece becomes a total.</summary>
    public static void AddPiece(int attack, int magicAttack, int defense, int magicDefense,
                                System.Collections.Generic.List<InkStat> stats,
                                System.Collections.Generic.List<float> values, ref EquipBonuses eb)
    {
        eb.atk += attack;
        eb.matk += magicAttack;
        eb.def += defense;
        eb.mdef += magicDefense;
        if (stats == null || values == null) return;
        int n = Mathf.Min(stats.Count, values.Count);
        for (int i = 0; i < n; i++) AddInk(stats[i], values[i], ref eb);
    }

    /// <summary>Which tier (1..5) of Ink wrote <paramref name="value"/> for <paramref name="stat"/>, or 0 when no tier
    /// writes that number — a value from before the table was tuned. The table is exact per tier, so the value
    /// names the tier.</summary>
    public static int InkTierOf(InkStat stat, float value)
    {
        for (int tier = 1; tier <= 5; tier++)
            if (Mathf.Approximately(InkValue(stat, tier), value)) return tier;
        return 0;
    }

    // ---- recycling ----------------------------------------------------------------------------------------
    /// <summary>The share of what a piece consumed that recycling gives back. Half, floored per material, so a
    /// single unit never comes back whole.</summary>
    public const float RecycleShare = 0.5f;

    /// <summary>What a piece of grade <paramref name="q"/> at <paramref name="folds"/> folds provably consumed,
    /// counted from what SUCCEEDED — nothing records the quenches that failed or the folds they spent, so those
    /// are not counted. Counted: the forge bill (band 1); for every rung climbed, one successful quench AND the
    /// full set of folds that grade's gate required before it (<see cref="ReadyToQuench"/>), each at the grade it
    /// was made from; and the folds standing on the piece now, at its own band. Written as units per band into
    /// <paramref name="unitsPerBand"/> (places 1..7; place 0 unused) and gold, all already halved.</summary>
    public static void RecycleYield(Quality q, int folds, int forgeUnits, int forgeGold, int[] unitsPerBand, out int gold)
    {
        System.Array.Clear(unitsPerBand, 0, unitsPerBand.Length);
        int spentGold = forgeGold;
        unitsPerBand[1] += forgeUnits;
        for (int g = (int)Quality.Raw; g < (int)q; g++)
        {
            var from = (Quality)g;
            // THE FOLDS THAT RUNG COST, not only the quench: the gate means a piece cannot have climbed without
            // first folding to that grade's cap, so those folds are as provable as the quench itself.
            unitsPerBand[Band(from)] += FoldCap(from) * FoldMaterialCost + QuenchMaterialCost;
            spentGold += FoldCap(from) * FoldGoldCost(from) + QuenchGoldCost(from);
        }
        unitsPerBand[Band(q)] += Mathf.Max(0, folds) * FoldMaterialCost;
        spentGold += Mathf.Max(0, folds) * FoldGoldCost(q);

        for (int b = 1; b < unitsPerBand.Length; b++) unitsPerBand[b] = Mathf.FloorToInt(unitsPerBand[b] * RecycleShare);
        gold = Mathf.FloorToInt(spentGold * RecycleShare);
    }

    /// <summary>How many Inks of each tier (places 1..5) a piece gives back for the stats written on it — one Ink
    /// per written stat, its tier read off the value, then halved and floored per tier.</summary>
    public static void RecycleInks(System.Collections.Generic.List<InkStat> stats,
                                   System.Collections.Generic.List<float> values, int[] perTier)
    {
        System.Array.Clear(perTier, 0, perTier.Length);
        if (stats == null || values == null) return;
        int n = Mathf.Min(stats.Count, values.Count);
        for (int i = 0; i < n; i++)
        {
            int tier = InkTierOf(stats[i], values[i]);
            if (tier > 0 && tier < perTier.Length) perTier[tier]++;
        }
        for (int t = 1; t < perTier.Length; t++) perTier[t] = Mathf.FloorToInt(perTier[t] * RecycleShare);
    }

    /// <summary>Every stat an Ink can write, in one order every reader shares. Cached, so a roll allocates nothing.</summary>
    public static readonly InkStat[] All = (InkStat[])System.Enum.GetValues(typeof(InkStat));

    /// <summary>True when this Ink stat is one of the six primaries — the rare roll.</summary>
    public static bool IsPrimary(InkStat s) => s >= InkStat.STR && s <= InkStat.LUK;

    // Roll weights, whole numbers so nothing rounds: each of the TWELVE sub-stats 80, each of the six primaries 20.
    // 12×80 + 6×20 = 1080, so a sub-stat is 7.4% and a primary 1.85% — the primaries are a ninth of the table
    // between them, which is what makes one the jackpot.
    const int RingWeight = 80, PrimaryWeight = 20, TotalWeight = 1080;

    /// <summary>The whole weight still on the table once the stats already written are taken off it — what a
    /// roll must be made against, so the ones left really do share the missing weight between them.</summary>
    public static int RemainingInkWeight(System.Collections.Generic.List<InkStat> already)
    {
        int total = TotalWeight;
        foreach (var candidate in All)
            if (already != null && already.Contains(candidate))
                total -= IsPrimary(candidate) ? PrimaryWeight : RingWeight;
        return Mathf.Max(0, total);
    }

    /// <summary>Which stat one Ink writes, given a roll made against <see cref="RemainingInkWeight"/> and the
    /// stats already on the piece (which cannot roll again — their weight is spread over the rest). Returns false
    /// when every stat is already there, which a full piece is.
    ///
    /// <para>The roll must be taken from 0 up to the REMAINING weight, not from 0 to 999 and folded: folding a
    /// 1000-wide roll into a narrower table hands the wrapped tail to whichever candidate comes first, which is
    /// not a re-spread but a thumb on the scale.</para></summary>
    public static bool RollInkStat(int rollInRemaining, System.Collections.Generic.List<InkStat> already, out InkStat stat)
    {
        stat = InkStat.Attack;
        int total = RemainingInkWeight(already);
        if (total <= 0) return false;

        int at = Mathf.Clamp(rollInRemaining, 0, total - 1);
        foreach (var candidate in All)
        {
            if (already != null && already.Contains(candidate)) continue;
            int w = IsPrimary(candidate) ? PrimaryWeight : RingWeight;
            if (at < w) { stat = candidate; return true; }
            at -= w;
        }
        return false;
    }
}
