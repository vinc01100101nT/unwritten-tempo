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

    /// <summary>True when <paramref name="kind"/> is a material whose tiers merge upward. Temper Dust and Quench Oil
    /// have no tiers, so there is nothing of theirs to merge.</summary>
    public static bool Merges(MaterialKind kind) => kind == MaterialKind.Ink || kind == MaterialKind.Salt;

    // ---- the grade ladder ---------------------------------------------------------------------------------
    /// <summary>The highest grade there is. Nothing quenches past it.</summary>
    public const Quality Best = Quality.A;

    // Indexed by (int)Quality: Raw, F, E, D, C, B, A.
    static readonly int[] FoldCaps      = { 5, 10, 15, 20, 25, 30, 35 };
    static readonly float[] Lifts       = { 0f, 0f, 0.05f, 0.10f, 0.15f, 0.22f, 0.30f };
    static readonly int[] Slots         = { 0, 1, 2, 3, 4, 5, 6 };
    static readonly int[] WeaponCaps    = { 10, 20, 30, 40, 50, 60, 70 };
    static readonly float[] Speeds      = { 0.5f, 0.75f, 1f, 1.1f, 1.2f, 1.35f, 1.5f };

    static readonly float[] TrademarkLifts = { 0.05f, 0.07f, 0.10f, 0.12f, 0.15f, 0.18f, 0.20f };

    static int Index(Quality q) => Mathf.Clamp((int)q, 0, FoldCaps.Length - 1);

    // ---- the trademark ------------------------------------------------------------------------------------
    /// <summary>What a weapon's trademark adds to every main number of its skill, by the weapon's grade — +5% at
    /// Raw to +20% at A. Added to the skill's level lift, never multiplied onto it.</summary>
    public static float TrademarkLift(Quality q) => TrademarkLifts[Index(q)];

    // ---- the trademark roll: a class first, then a skill of that class -------------------------------------
    /// <summary>The share, in percent, of new weapons whose trademark is an ULTIMATE's, for a weapon type that has
    /// trademarks of both classes. Every other new weapon carries an active's or a passive's.</summary>
    public const int UltimateTrademarkPercent = 1;

    /// <summary>Whether a class draw of <paramref name="roll0To99"/> lands on an ultimate, for a weapon type with
    /// <paramref name="ultimates"/> ultimate trademarks and <paramref name="others"/> active or passive ones. A type
    /// with only one class gives that class every weapon. The second draw then picks evenly inside the class.</summary>
    public static bool TrademarkIsUltimate(int ultimates, int others, int roll0To99)
    {
        if (ultimates <= 0) return false;
        if (others <= 0) return true;
        return roll0To99 < UltimateTrademarkPercent;
    }

    /// <summary>The chance one trademark is the one a new weapon rolls: its class's share, split evenly among that
    /// class's candidates. <paramref name="ultimate"/> says which class it is in. 0 when its class has no candidate.</summary>
    public static float TrademarkChance(bool ultimate, int ultimates, int others)
    {
        int mine = ultimate ? ultimates : others;
        if (mine <= 0) return 0f;
        int theirs = ultimate ? others : ultimates;
        int share = theirs <= 0 ? 100 : ultimate ? UltimateTrademarkPercent : 100 - UltimateTrademarkPercent;
        return share / 100f / mine;
    }

    // ---- the rarity draw ----------------------------------------------------------------------------------
    // Indexed by (int)Rarity: Common, Uncommon, Rare, Epic, Legendary. Each rarity's share, in percent, of a draw
    // every rarity takes part in. Whole numbers summing to 100, so the draw is one integer draw.
    static readonly int[] RarityShares = { 50, 30, 14, 5, 1 };

    /// <summary>How many rarities there are — the length every per-rarity array below must have.</summary>
    public static int RarityCount => RarityShares.Length;

    /// <summary><paramref name="rarity"/>'s share, in percent, of a draw every rarity takes part in.</summary>
    public static int RarityShare(Rarity rarity) => RarityShares[Mathf.Clamp((int)rarity, 0, RarityShares.Length - 1)];

    /// <summary>The size of a rarity draw among only the rarities <paramref name="present"/> marks (above 0, indexed by
    /// (int)Rarity): their shares added up, so a rarity left out hands its share to the others in proportion. 0 when
    /// none is present.</summary>
    public static int RarityDrawTotal(int[] present)
    {
        int total = 0;
        for (int r = 0; r < RarityShares.Length && r < present.Length; r++)
            if (present[r] > 0) total += RarityShares[r];
        return total;
    }

    /// <summary>The rarity a draw of <paramref name="roll"/> (0 to <see cref="RarityDrawTotal"/> − 1) lands on, among
    /// the rarities <paramref name="present"/> marks.</summary>
    public static Rarity RarityAt(int[] present, int roll)
    {
        int last = 0;
        for (int r = 0; r < RarityShares.Length && r < present.Length; r++)
        {
            if (present[r] <= 0) continue;
            last = r;
            roll -= RarityShares[r];
            if (roll < 0) return (Rarity)r;
        }
        return (Rarity)last;
    }

    // What a rarity adds to the grade's lift on a piece of gear, in hundredths of the piece's value.
    static readonly int[] RarityLifts = { 0, 10, 20, 35, 50 };

    /// <summary>What <paramref name="rarity"/> adds to a piece's fold values, in hundredths: 0 at Common, 50 at
    /// Legendary. Added to the grade's lift, never multiplied onto it.</summary>
    public static int RarityLiftHundredths(Rarity rarity) => RarityLifts[Mathf.Clamp((int)rarity, 0, RarityLifts.Length - 1)];

    /// <summary>The most folds this grade allows — and, because a piece cannot be folded past its grade, also
    /// the point at which the only way further is a quench.</summary>
    public static int FoldCap(Quality q) => FoldCaps[Index(q)];

    /// <summary>What this grade adds to the piece's fold total, as a fraction (0.30 = +30%).</summary>
    public static float QualityLift(Quality q) => Lifts[Index(q)];

    /// <summary>How many stats this grade lets a piece carry.</summary>
    public static int InkSlots(Quality q) => Slots[Index(q)];

    /// <summary>The highest Weapon Mastery level a weapon of this grade lets its wielder use — and earn.</summary>
    public static int WeaponMasteryCap(Quality q) => WeaponCaps[Index(q)];

    /// <summary>How fast a weapon of this grade trains its weapon mastery (1 = par).</summary>
    public static float TrainingSpeed(Quality q) => Speeds[Index(q)];

    /// <summary>The grade counted from one: 1 at Raw, 7 at A. How many Temper Dust a fold of this grade costs, how
    /// many Quench Oil a quench of it costs, and the multiplier on what each costs in gold.</summary>
    public static int Band(Quality q) => Index(q) + 1;

    // ---- what a fold and a quench cost --------------------------------------------------------------------
    const int GoldPerFoldPerBand = 50, GoldPerQuenchPerBand = 500;
    /// <summary>Units of Temper Dust one fold of a piece of grade <paramref name="q"/> costs: 1 at Raw, 7 at A.</summary>
    public static int FoldMaterialCost(Quality q) => Band(q);
    /// <summary>Units of Quench Oil one quench of a piece of grade <paramref name="q"/> costs: 1 at Raw, 7 at A.</summary>
    public static int QuenchMaterialCost(Quality q) => Band(q);

    public static int FoldGoldCost(Quality q) => GoldPerFoldPerBand * Band(q);
    public static int QuenchGoldCost(Quality q) => GoldPerQuenchPerBand * Band(q);

    // ---- what a fold is worth on the piece ----------------------------------------------------------------
    // A weapon gains attack per fold — magic attack instead when it is a caster's weapon, which is the only
    // place a weapon TYPE changes what a fold means.
    //
    // AN ARMOUR PIECE'S MATERIAL DECIDES WHAT A FOLD BUYS, AND ITS SLOT HOW MUCH (decided 2026-09-23): metal buys
    // defence and health, leather attack speed and evasion, cloth magic defence and mana. Priced with Ink's own
    // exchange rate — 1 defence = 8 HP = 4 MP = 0.5% attack speed, and evasion at double defence, since each point
    // takes 1% off the hits that land — so a fold is worth 1.8 defence, split between two stats.
    //
    // EVERY FOLD IS WORTH 1.8 OF WHAT IT ONCE BOUGHT (decided 2026-10-07): sized so a level-1 character wearing the
    // +5 starter set — Sword, Chainmail, Iron Sabatons — beats four ungeared level-1 creatures attacking together.
    // Held in HUNDREDTHS and summed as whole numbers: 0.9, 0.45, 3.6, 7.2 and 1.8 have no exact float, and summed as
    // floats 19 of the values the folds can reach (an A +25 weapon's 58.5 among them) landed below the .5 they sit on.
    const int WeaponAttackPerFold = 180;
    const int MetalDefensePerFold = 90, MetalHPPerFold = 720;
    const int LeatherAttackSpeedPerFold = 45, LeatherEvasionPerFold = 45;
    const int ClothMagicDefensePerFold = 90, ClothMPPerFold = 360;
    // NO PIECE BUYS MOVEMENT SPEED (decided 2026-10-14): a person moves at the shared pace, and only a timed effect
    // changes it.

    /// <summary>The finished numbers one piece's folds and grade grant — what the server writes onto the copy and a
    /// player's machine only ever adds up. Attack speed is whole percent, one decimal.</summary>
    [System.Serializable]
    public struct PieceStats
    {
        public int attack, magicAttack, defense, magicDefense, hp, mp, evasion;
        public float attackSpeedPercent;
    }

    /// <summary>How much of its material's fold an armour slot takes: the full amount for body, off-hand and garment,
    /// half for head and shoes.</summary>
    public static float SlotShare(EquipSlotType slot) =>
        slot == EquipSlotType.Head || slot == EquipSlotType.Boots ? 0.5f : 1f;

    /// <summary>The piece's own numbers at <paramref name="folds"/> folds of grade <paramref name="q"/> and rarity
    /// <paramref name="rarity"/> — the finished numbers, lift included, as the server writes them onto the copy. An
    /// armour piece with no material has nothing a fold could buy. Rounds half UP, deliberately:
    /// <c>Mathf.RoundToInt</c> rounds .5 to the even number.</summary>
    public static PieceStats PieceNumbers(EquipSlotType slot, WeaponType weapon, MaterialKind material, int folds, Quality q,
                                          Rarity rarity)
    {
        var p = default(PieceStats);
        if (folds <= 0 || slot == EquipSlotType.None) return p;
        int lift = LiftHundredths(q, rarity);
        if (slot == EquipSlotType.Weapon)
        {
            int atk = FoldValue(folds * 2, WeaponAttackPerFold, lift);
            if (IsCasterWeapon(weapon)) p.magicAttack = atk;
            else p.attack = atk;
            return p;
        }
        int halves = folds * (SlotShare(slot) < 1f ? 1 : 2);
        switch (material)
        {
            case MaterialKind.Metal:
                p.defense = FoldValue(halves, MetalDefensePerFold, lift);
                p.hp = FoldValue(halves, MetalHPPerFold, lift);
                break;
            case MaterialKind.Leather:
                p.attackSpeedPercent = FoldValue(halves, LeatherAttackSpeedPerFold * 10, lift) / 10f;
                p.evasion = FoldValue(halves, LeatherEvasionPerFold, lift);
                break;
            case MaterialKind.Cloth:
                p.magicDefense = FoldValue(halves, ClothMagicDefensePerFold, lift);
                p.mp = FoldValue(halves, ClothMPPerFold, lift);
                break;
        }
        return p;
    }

    /// <summary>The grade's and the rarity's lift together, as whole hundredths of the piece's value: 100 for a Common
    /// piece at Raw or F, 130 for a Common one at A, 180 for a Legendary one at A.</summary>
    public static int LiftHundredths(Quality q, Rarity rarity) =>
        100 + Mathf.RoundToInt(QualityLift(q) * 100f) + RarityLiftHundredths(rarity);

    /// <summary>What <paramref name="halves"/> half-folds buy at <paramref name="perFoldHundredths"/> a fold, lifted by
    /// <paramref name="liftHundredths"/>, rounded half UP — all whole-number maths, so a value exactly on .5 rounds up.</summary>
    static int FoldValue(int halves, int perFoldHundredths, int liftHundredths)
    {
        const long Den = 2L * 100 * 100;
        long num = (long)halves * perFoldHundredths * liftHundredths;
        return (int)((2 * num + Den) / (2 * Den));
    }

    /// <summary>A caster's weapon gains MAGIC attack from a fold rather than attack.</summary>
    public static bool IsCasterWeapon(WeaponType w) => w == WeaponType.Staff || w == WeaponType.Wand || w == WeaponType.Scythe;

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

    /// <summary>The strongest Ink's tier — Sharp.</summary>
    public const int TopInkTier = 5;

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
    public static void AddPiece(in PieceStats piece,
                                System.Collections.Generic.List<InkStat> stats,
                                System.Collections.Generic.List<float> values, ref EquipBonuses eb)
    {
        eb.atk += piece.attack;
        eb.matk += piece.magicAttack;
        eb.def += piece.defense;
        eb.mdef += piece.magicDefense;
        eb.hp += piece.hp;
        eb.mp += piece.mp;
        eb.evasion += piece.evasion;
        eb.aspdPercent += piece.attackSpeedPercent;
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

    /// <summary>What recycling a piece of grade <paramref name="q"/> at <paramref name="folds"/> folds gives back:
    /// half, floored, of what it provably consumed, counted from what SUCCEEDED — nothing records the quenches that
    /// failed or the folds they spent, so those are not counted. Counted: for every grade climbed, one successful
    /// quench AND the full set of folds that grade's gate required before it (<see cref="ReadyToQuench"/>), each at
    /// the grade it was made from; and the folds standing on the piece now. Priced by the same two functions the
    /// spend reads (<see cref="FoldMaterialCost"/>, <see cref="QuenchMaterialCost"/>).
    ///
    /// <para>FOLDS BOUGHT AT RAW RETURN NOTHING, standing or climbed — no Temper Dust and no gold. A tutorial's first
    /// run gives its Raw folds free, so a Raw fold that paid back would pay out of nothing.</para></summary>
    public static void RecycleYield(Quality q, int folds, out int dust, out int oil, out int gold)
    {
        int spentDust = 0, spentOil = 0, spentGold = 0;
        for (int g = (int)Quality.Raw; g < (int)q; g++)
        {
            var from = (Quality)g;
            spentOil += QuenchMaterialCost(from);
            spentGold += QuenchGoldCost(from);
            if (from == Quality.Raw) continue;
            spentDust += FoldCap(from) * FoldMaterialCost(from);
            spentGold += FoldCap(from) * FoldGoldCost(from);
        }
        if (q != Quality.Raw)
        {
            spentDust += Mathf.Max(0, folds) * FoldMaterialCost(q);
            spentGold += Mathf.Max(0, folds) * FoldGoldCost(q);
        }
        dust = Mathf.FloorToInt(spentDust * RecycleShare);
        oil = Mathf.FloorToInt(spentOil * RecycleShare);
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
