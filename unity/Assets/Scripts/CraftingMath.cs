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
    /// <summary>How many of one tier make one of the next, for the two materials that merge. Deterministic, and nothing
    /// can fail. Ink merges ten into one. SALT MERGES AT ITS WORTH (decided 2026-10-14): a Fine Salt is worth 20 Coarse
    /// and a Pure 5 Fine, counted in the kills it takes to find each, so a merge costs what a find does.
    /// 0 for a tier that merges into nothing.</summary>
    public static int MergeCount(MaterialKind kind, int step)
    {
        if (kind == MaterialKind.Ink) return 10;
        if (kind == MaterialKind.Salt) return step == 1 ? 20 : step == 2 ? 5 : 0;
        return 0;
    }

    /// <summary>True when <paramref name="kind"/> is a material whose tiers merge upward. Temper Dust and Quench Oil
    /// have no tiers, so there is nothing of theirs to merge.</summary>
    public static bool Merges(MaterialKind kind) => kind == MaterialKind.Ink || kind == MaterialKind.Salt;

    // ---- the grade ladder ---------------------------------------------------------------------------------
    /// <summary>The highest grade there is. Nothing quenches past it.</summary>
    public const Quality Best = Quality.A;

    // Indexed by (int)Quality: Raw, F, E, D, C, B, A.
    // EVERY GRADE FOLDS TO +10 (decided 2026-10-14).
    static readonly int[] FoldCaps      = { 10, 10, 10, 10, 10, 10, 10 };
    static readonly int[] Slots        = { 0, 1, 2, 3, 4, 5, 6 };
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

    // What a rarity multiplies a piece's base by, in hundredths: Common 100 … Legendary 150.
    static readonly int[] RarityBases = { 100, 110, 120, 135, 150 };

    /// <summary>What <paramref name="rarity"/> multiplies a piece's base by, in hundredths: 100 at Common, 150 at
    /// Legendary.</summary>
    public static int RarityBaseHundredths(Rarity rarity) => RarityBases[Mathf.Clamp((int)rarity, 0, RarityBases.Length - 1)];

    /// <summary>The most folds this grade allows — and, because a piece cannot be folded past its grade, also
    /// the point at which the only way further is a quench.</summary>
    public static int FoldCap(Quality q) => FoldCaps[Index(q)];

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
    /// <summary>Units of Temper Dust one fold of a piece of grade <paramref name="q"/> costs: 1 at Raw, 7 at A.</summary>
    public static int FoldMaterialCost(Quality q) => Band(q);
    /// <summary>Units of Quench Oil one quench of a piece of grade <paramref name="q"/> costs: 1 at Raw, 7 at A.</summary>
    public static int QuenchMaterialCost(Quality q) => Band(q);

    // A STATION'S FEE IS PRICED IN KILLS OF INCOME AT THE GRADE'S LEVEL (decided 2026-10-14): a fold try costs 0.6 of
    // a kill, a quench try 6, a kill being worth CreatureMaterials.GoldPerLevel × the creature's level. Tenths of a kill.
    const int FoldFeeTenthsOfAKill = 6, QuenchFeeTenthsOfAKill = 60;

    /// <summary>The creature level a grade's fees are priced at: the middle of the grade's seventh of levels
    /// 1–<see cref="CreatureLoot.LadderTop"/>, rounded half up — 7, 21, 35, 50, 64, 78, 92.</summary>
    public static int GradePriceLevel(Quality q)
    {
        long num = (2L * Band(q) - 1) * CreatureLoot.LadderTop, den = 2L * CreatureLoot.Grades;
        return (int)((2 * num + den) / (2 * den));
    }

    /// <summary>What one fold try of grade <paramref name="q"/> costs in gold, paid whether it succeeds or not.</summary>
    public static int FoldGoldCost(Quality q) => FoldFeeTenthsOfAKill * CreatureMaterials.GoldPerLevel * GradePriceLevel(q) / 10;
    /// <summary>What one quench try of grade <paramref name="q"/> costs in gold, paid whether it succeeds or not.</summary>
    public static int QuenchGoldCost(Quality q) => QuenchFeeTenthsOfAKill * CreatureMaterials.GoldPerLevel * GradePriceLevel(q) / 10;

    // ---- what a piece is worth ----------------------------------------------------------------------------
    // WORTH = BASE × ROLL × RARITY × (1 + EVERY FOLD AND QUENCH PERCENTAGE, ADDED TOGETHER) (decided 2026-10-14). The
    // percentages add, never multiply each other. A Raw +0 piece is worth its base × roll × rarity.
    //
    // A WEAPON'S BASE IS 50 × ITS SWING TIME IN SECONDS, divided by (1 + the off hand's power) for a pair weapon, so
    // every weapon deals the same damage a second at equal gear. A caster's weapon gives magic attack instead — the
    // only place a weapon TYPE changes what its number means. The swing time and the off hand's power come in as
    // whole thousandths, rounded once by the caller: 1.3 as a float is 1.2999999, which truncated would cost 0.1%.
    //
    // AN ARMOUR PIECE'S MATERIAL DECIDES WHAT ITS BASE BUYS, AND ITS SLOT HOW MUCH: a body or a garment is worth one
    // sixth of a sword's base (the constant 50) on Ink's exchange rate — 1 defence = 8 HP = 4 MP = 0.5% attack speed,
    // evasion at double defence — split evenly between its material's two stats; head and shoes half. Sized so a full
    // metal set's defence at equal gear is 25% of a sword's attack. Each stat is a numerator over 12: metal 50 defence
    // + 400 HP, leather 25 attack speed + 25 evasion, cloth 50 magic defence + 200 MP.
    //
    // NO PIECE BUYS MOVEMENT SPEED (decided 2026-10-14): a person moves at the shared pace, and only a timed effect
    // changes it.
    const int WeaponBase = 50;
    const int Twelfths = 12;
    const int MetalDefense12 = 50, MetalHP12 = 400;
    const int LeatherAttackSpeed12 = 25, LeatherEvasion12 = 25;
    const int ClothMagicDefense12 = 50, ClothMP12 = 200;

    // What each fold adds at each grade, and what the quench that reached each grade adds, in percent of the base.
    static readonly int[] FoldPercents   = { 10, 20, 30, 40, 50, 60, 70 };
    static readonly int[] QuenchPercents = { 0, 100, 200, 300, 400, 500, 600 };

    /// <summary>The lowest and highest roll a dropped piece's base can take, in whole percent.</summary>
    public const int RollMin = 85, RollMax = 115;

    /// <summary>A piece's roll in percent: what it rolled, or 100 for a piece that never rolled (0).</summary>
    public static int RollOrPar(int rollPercent) => rollPercent > 0 ? rollPercent : 100;

    /// <summary>Every fold and quench percentage of a piece of grade <paramref name="q"/> at <paramref name="folds"/>
    /// folds, added together: every grade below's full folds and the quench that left it, plus this grade's folds.
    /// 4,900 at A +10.</summary>
    public static int TotalPercent(Quality q, int folds)
    {
        int at = Index(q), sum = 0;
        for (int g = 0; g < at; g++) sum += FoldCaps[g] * FoldPercents[g] + QuenchPercents[g + 1];
        return sum + Mathf.Clamp(folds, 0, FoldCaps[at]) * FoldPercents[at];
    }

    /// <summary>The finished numbers one piece is worth — what the server writes onto the copy and a player's machine
    /// only ever adds up. Attack speed is whole percent, one decimal.</summary>
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

    /// <summary>A swing time or an off hand's power as the whole thousandths <see cref="PieceNumbers"/> takes, rounded
    /// once — never truncated, since 1.3 and 1.4 as floats sit just below themselves.</summary>
    public static int Thousandths(float value) => Mathf.RoundToInt(value * 1000f);

    /// <summary>The piece's own numbers at <paramref name="folds"/> folds of grade <paramref name="q"/>, rarity
    /// <paramref name="rarity"/> and base roll <paramref name="rollPercent"/> (0 = never rolled, counted as 100) — the
    /// finished numbers, as the server writes them onto the copy. A weapon's base follows its swing time
    /// (<paramref name="swingMs"/>, whole milliseconds) and, for a pair weapon, its off hand's power
    /// (<paramref name="offHandThousandths"/>). An armour piece with no material is worth nothing. Rounds half UP,
    /// deliberately: <c>Mathf.RoundToInt</c> rounds .5 to the even number.</summary>
    public static PieceStats PieceNumbers(EquipSlotType slot, WeaponType weapon, MaterialKind material, int folds, Quality q,
                                          Rarity rarity, int rollPercent, int swingMs, int offHandThousandths)
    {
        var p = default(PieceStats);
        if (slot == EquipSlotType.None) return p;
        long scale = (long)RollOrPar(rollPercent) * RarityBaseHundredths(rarity) * (100 + TotalPercent(q, folds));
        const long Percents = 100L * 100 * 100;
        if (slot == EquipSlotType.Weapon)
        {
            long hands = WeaponTypes.IsDualWield(weapon) ? 1000 + Mathf.Max(0, offHandThousandths) : 1000;
            int ms = swingMs > 0 ? swingMs : 1000;
            int atk = HalfUp((long)WeaponBase * ms * scale, hands * Percents);
            if (IsCasterWeapon(weapon)) p.magicAttack = atk;
            else p.attack = atk;
            return p;
        }
        long den = Twelfths * (SlotShare(slot) < 1f ? 2L : 1L) * Percents;
        switch (material)
        {
            case MaterialKind.Metal:
                p.defense = HalfUp(MetalDefense12 * scale, den);
                p.hp = HalfUp(MetalHP12 * scale, den);
                break;
            case MaterialKind.Leather:
                p.attackSpeedPercent = HalfUp(LeatherAttackSpeed12 * 10L * scale, den) / 10f;
                p.evasion = HalfUp(LeatherEvasion12 * scale, den);
                break;
            case MaterialKind.Cloth:
                p.magicDefense = HalfUp(ClothMagicDefense12 * scale, den);
                p.mp = HalfUp(ClothMP12 * scale, den);
                break;
        }
        return p;
    }

    /// <summary>The piece's BASE alone — base × roll, before rarity, folds and quenches: what a tooltip's Base line shows.</summary>
    public static PieceStats BaseNumbers(EquipSlotType slot, WeaponType weapon, MaterialKind material, int rollPercent,
                                         int swingMs, int offHandThousandths) =>
        PieceNumbers(slot, weapon, material, 0, Quality.Raw, Rarity.Common, rollPercent, swingMs, offHandThousandths);

    /// <summary><paramref name="num"/> ÷ <paramref name="den"/>, rounded half UP, in whole numbers.</summary>
    static int HalfUp(long num, long den) => (int)((2 * num + den) / (2 * den));

    /// <summary>A caster's weapon gains MAGIC attack from a fold rather than attack.</summary>
    public static bool IsCasterWeapon(WeaponType w) => w == WeaponType.Staff || w == WeaponType.Wand || w == WeaponType.Scythe;

    // ---- the fold's luck ----------------------------------------------------------------------------------
    /// <summary>Folds up to this many always succeed, and a failed fold never takes a piece below it.</summary>
    public const int SafeFolds = 4;

    // The chance in whole percent that a fold from +4, +5 … +9 succeeds, by grade (Raw … A). Below +4 every fold does.
    static readonly int[,] FoldChances =
    {
        { 90, 80, 70, 60, 50, 40 },
        { 76, 68, 60, 51, 42, 34 },
        { 65, 58, 50, 43, 36, 29 },
        { 54, 48, 42, 36, 30, 24 },
        { 45, 40, 35, 30, 25, 20 },
        { 36, 32, 28, 24, 20, 16 },
        { 22, 20, 17, 15, 12, 10 },
    };

    /// <summary>The chance, in whole percent, that one fold of a grade <paramref name="q"/> piece standing at
    /// <paramref name="foldsBefore"/> succeeds: 100 below +4, then the grade's own row. 0 at the cap.</summary>
    public static int FoldChancePct(Quality q, int foldsBefore)
    {
        if (foldsBefore >= FoldCap(q)) return 0;
        if (foldsBefore < SafeFolds) return 100;
        return FoldChances[Index(q), Mathf.Clamp(foldsBefore - SafeFolds, 0, FoldChances.GetLength(1) - 1)];
    }

    /// <summary>Where a piece at <paramref name="foldsBefore"/> folds lands when a fold fails: one fold down, never below
    /// <see cref="SafeFolds"/>.</summary>
    public static int FoldFailFolds(int foldsBefore) => Mathf.Max(SafeFolds, foldsBefore - 1);

    // ---- the quench ---------------------------------------------------------------------------------------
    /// <summary>Quench chances are counted out of this many — fine enough that 6.5% × 1.25 is exact.</summary>
    public const int QuenchChanceDen = 100000;

    // The chance, out of QuenchChanceDen, that a quench of THIS grade goes up, Raw … B. A is the top and is never
    // quenched. A quench either goes up or fails; nothing stays "unchanged".
    static readonly int[] UpChances = { 40000, 30000, 18000, 11000, 6500, 3500 };

    /// <summary>The most a quench's chance can ever be, out of <see cref="QuenchChanceDen"/>, however good the Salt.</summary>
    public const int MaxUpChance = 95000;

    /// <summary>True when a piece of this grade could ever be quenched. A is the top of the ladder.</summary>
    public static bool CanQuench(Quality q) => q < Best;

    /// <summary>True when a piece may be quenched RIGHT NOW: its grade is below A, and it has been folded to that
    /// grade's cap. THE FOLD CAP IS THE QUENCH GATE (V4 §2) — the folds are what a quench spends, so a piece that
    /// has not done the work cannot buy the roll.</summary>
    public static bool ReadyToQuench(Quality q, int folds) => CanQuench(q) && folds >= FoldCap(q);

    /// <summary>The chance, out of <see cref="QuenchChanceDen"/>, that a quench of a grade <paramref name="q"/> piece goes
    /// up a grade with a Salt of <paramref name="saltTier"/> (0 none, 1 Coarse, 2 Fine, 3 Pure): Fine makes it × 1.25,
    /// Pure × 1.5, and nothing takes it past <see cref="MaxUpChance"/>. 0 for a grade that cannot be quenched.</summary>
    public static int QuenchUpChance(Quality q, int saltTier)
    {
        if (!CanQuench(q)) return 0;
        int up = UpChances[Mathf.Clamp(Index(q), 0, UpChances.Length - 1)];
        if (saltTier == 2) up = up * 5 / 4;
        else if (saltTier >= 3) up = up * 3 / 2;
        return Mathf.Min(up, MaxUpChance);
    }

    /// <summary>Where a failed quench leaves a grade <paramref name="q"/> piece, by its Salt: none drops it one grade to
    /// +0 (a Raw piece stays Raw, at +0); Coarse and Fine keep its grade at +4; Pure keeps it at +9.</summary>
    public static void QuenchFail(Quality q, int saltTier, out Quality grade, out int folds)
    {
        if (saltTier <= 0) { grade = (Quality)Mathf.Max((int)Quality.Raw, (int)q - 1); folds = 0; return; }
        grade = q;
        folds = saltTier >= 3 ? 9 : SafeFolds;
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
