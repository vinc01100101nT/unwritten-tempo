using UnityEngine;

/// <summary>Everything a unit's derived stats are computed FROM: its level, its six EFFECTIVE primaries (allocated
/// points + equipment bonuses already folded in), and its base/per-level growth curve. A plain struct so passing it
/// around costs nothing. Serializable because <see cref="UnitStatBlock.inputs"/> is BAKED into every mob prefab:
/// without this marker Unity silently dropped the block on save and every creature loaded at level 1.</summary>
[System.Serializable]
public struct UnitStatInputs
{
    public int level;
    public int str, agi, vit, intel, dex, luk;

    public int baseMaxHP, hpPerLevel;
    public int baseAttack, attackPerLevel;
    public int baseDefense, defensePerLevel;
    public int baseMaxMP, mpPerLevel;
    public int baseMagicAttack, magicAttackPerLevel;
    public int baseMagicDefense, magicDefensePerLevel;

    /// <summary>The starting curve every unit uses when nothing overrides it — the player's own base/growth values,
    /// so a mob authored with default growth sits on the same curve as a player of the same level.</summary>
    public static UnitStatInputs Default(int level) => new UnitStatInputs
    {
        level = Mathf.Max(1, level),
        str = 1, agi = 1, vit = 1, intel = 1, dex = 1, luk = 1,
        baseMaxHP = 20, hpPerLevel = 6,
        baseAttack = 4, attackPerLevel = 1,
        baseDefense = 1, defensePerLevel = 1,
        baseMaxMP = 10, mpPerLevel = 3,
        baseMagicAttack = 3, magicAttackPerLevel = 1,
        baseMagicDefense = 1, magicDefensePerLevel = 1,
    };

    public int Primary(PrimaryStat s) => s switch
    {
        PrimaryStat.STR => str,
        PrimaryStat.AGI => agi,
        PrimaryStat.VIT => vit,
        PrimaryStat.INT => intel,
        PrimaryStat.DEX => dex,
        _               => luk,
    };

    /// <summary>These inputs with a worn set's primary bonuses added. A creature's authored inputs hold only what it
    /// bought, so it goes through this before any formula — otherwise a STR helm on a creature would add nothing. The
    /// player folds its gear in while building its own inputs, so it never needs this.</summary>
    public UnitStatInputs WithGear(in EquipBonuses eb)
    {
        var e = this;
        e.str += eb.str; e.agi += eb.agi; e.vit += eb.vit;
        e.intel += eb.intel; e.dex += eb.dex; e.luk += eb.luk;
        return e;
    }
}

/// <summary>
/// THE stat formulas — pre-renewal RO, ported. Pure static math with no Unity objects and no notion of who owns the
/// numbers, so the player (<see cref="Character"/>) and every mob run the IDENTICAL arithmetic. This is the single
/// source of truth the "players and mobs are 1:1" design rests on: there is no second copy to drift.
///
/// <para>Each function takes the unit's <see cref="UnitStatInputs"/>, its aggregated equipment bonuses, and the
/// already-resolved ring percentage for that stat channel. Resolving equipment and the ring is the CALLER's job —
/// a unit with neither passes <c>default</c> and 0 and gets the unequipped, un-ringed baseline.</para>
/// </summary>
public static class UnitStatMath
{
    // Per-point contributions of each primary to the derived layer.
    public const int   AtkPerStr = 1, MatkPerInt = 1, RangedAtkPerDex = 1;
    public const int   DefPerVit = 3, MdefPerInt = 3, AtkPerDex = 2, AtkPerLuk = 4, MatkPerLuk = 4, RangedAtkPerStr = 2;  // DIVISORS
    public const float AspdPerAgi = 0.005f, CritPerLuk = 0.003f;   // pre-renewal CRIT = LUK×0.3%
    // VIT and INT scale HP/SP as a PERCENT per point (they multiply the level-driven base, not add flat);
    // DEX raises ASPD a fraction of what AGI does.
    public const float VitHpPercent = 0.02f, IntSpPercent = 0.015f, DexAspdShare = 0.25f;
    /// <summary>Hard ceiling on the attack-speed multiplier — bounds the Momentum/Deathblow equilibrium.</summary>
    public const float MaxAttackSpeedMult = 3f;
    /// <summary>Pre-renewal "crit shield": the TARGET's LUK lowers an attacker's crit chance 0.3%/pt.</summary>
    public const float CritShieldPerLuk = 0.003f;
    /// <summary>DEX at which a spell's wind-up reaches zero: castTime = baseCast × (1 − DEX/150).</summary>
    public const int   InstantCastDex = 150;
    public const int   HitPerDex = 1, FleePerAgi = 1;
    public const int   LukPerHit = 3, LukPerFlee = 5;   // DIVISORS: LUK/3 → HIT, LUK/5 → FLEE
    // RO-style damage spread: a hit rolls Min…Max. Min is a fraction of Max that DEX (physical) / INT (magic)
    // raise toward 1.0 — the relevant stat both LIFTS and STABILISES damage. A crit ignores the spread.
    public const float MinDmgFloorFraction = 0.55f;   // 0 DEX/INT → a swing can drop to 55% of max
    public const float MinFractionPerStat  = 0.012f;  // each point closes the min→max gap (~38 → min == max)
    /// <summary>Pre-renewal base crit multiplier: a crit deals 140%.</summary>
    public const float BaseCritMultiplier = 1.4f;
    /// <summary>LUK's pull on how hard a crit lands: +0.2% crit damage per point, on top of the base.</summary>
    public const float CritDamagePerLuk = 0.002f;
    // Natural regeneration is a PERCENT of the pool per second, not a flat amount, so it stays meaningful as the pool
    // grows with level. VIT owns the health side and INT the mana side — the same primary that already grows each pool.
    public const float HpRegenPercentPerVit = 0.0002f, MpRegenPercentPerInt = 0.0002f;
    /// <summary>The flat mana trickle every unit gets before INT is counted. The player's own field defaults to it and
    /// may be tuned per character; a mob reads it straight, so there is one number and not two.</summary>
    public const float BaseMpRegenPerSecond = 1.5f;
    /// <summary>Pre-renewal base-stat ceiling — a primary can't be raised past this with points.</summary>
    public const int   MaxStatValue = 99;

    /// <summary>Points to raise a stat from <paramref name="currentValue"/> to +1: <c>⌊V/10⌋ + 2</c>. The pre-renewal
    /// soft cap — each block of ten costs one more, so the last points into a stat are the dearest.</summary>
    public static int StatCost(int currentValue) => currentValue / 10 + 2;

    // ---- the stat-point ECONOMY: what a unit of a given level can afford -----------------------------------------
    // A unit's primaries are not free-typed numbers — they're an ALLOCATION out of a level-derived budget. Shared by
    // the player (who spends it through the stat screen) and every mob (whose authored spread is checked against it),
    // so a level-10 mob can't quietly carry level-50 stats and a mob and a player of equal level start equally rich.

    /// <summary>Points every unit starts with at level 1.</summary>
    public const int StartingStatPoints = 48;

    /// <summary>Status points GAINED on reaching base level <paramref name="level"/>: <c>⌊L/5⌋ + 3</c> (3 early,
    /// growing every 5 levels to keep pace with the rising <see cref="StatCost"/>).</summary>
    public static int PointsGainedAtLevel(int level) => level / 5 + 3;

    /// <summary>The TOTAL points a unit that legitimately reached <paramref name="level"/> has earned — the starting
    /// grant plus every level-up award from 2..level. This is the budget a mob's authored spread must fit inside.</summary>
    public static int PointsEarnedByLevel(int level, int startingPoints = StartingStatPoints)
    {
        int total = startingPoints;
        for (int L = 2; L <= Mathf.Max(1, level); L++) total += PointsGainedAtLevel(L);
        return total;
    }

    /// <summary>Experience needed to climb from base level <paramref name="level"/> to the next one: <c>5 + 5L</c>.
    /// The character's level curve lives HERE so the bar a player watches and the lifetime total below can never
    /// disagree about what a level costs.</summary>
    public static int XpToNextFrom(int level) => 5 + 5 * Mathf.Max(1, level);

    /// <summary>Experience needed to climb from ring level <paramref name="level"/> to the next: <c>20 + 12L</c> — a
    /// slightly slower curve than the character's. Weapon mastery levels on this same curve (<see cref="MasteryMath"/>).</summary>
    public static int RingXpToNextFrom(int level) => 20 + level * 12;

    /// <summary>The TOTAL experience a character that legitimately climbed to <paramref name="level"/> was given
    /// on the way — every rung from 1 up to it, and none of the level they are standing in. Add the experience
    /// held inside the current level to get a lifetime figure.</summary>
    public static long XpEarnedByLevel(int level)
    {
        decimal L = Mathf.Max(1, level);
        // Every rung from 1 to L-1 added up in one step. Written out rather than looped because a junk level
        // sitting in an old row would otherwise spin this billions of times while somebody waits to load in,
        // and worked out in decimal so an impossible level saturates instead of wrapping round to a negative.
        decimal total = 5m * (L - 1m) + 5m * L * (L - 1m) / 2m;
        return total > long.MaxValue ? long.MaxValue : (long)total;
    }

    /// <summary>What it cost to raise ONE stat from 1 up to <paramref name="value"/> — the running integral of
    /// <see cref="StatCost"/>.</summary>
    public static int PointsToRaise(int value)
    {
        int total = 0;
        for (int v = 1; v < value; v++) total += StatCost(v);
        return total;
    }

    /// <summary>What this whole spread cost to buy — the sum over all six primaries. Compare against
    /// <see cref="PointsEarnedByLevel"/> to know whether a unit is under-, exactly-, or over-budget.</summary>
    public static int PointsSpent(in UnitStatInputs u) =>
        PointsToRaise(u.str) + PointsToRaise(u.agi) + PointsToRaise(u.vit)
        + PointsToRaise(u.intel) + PointsToRaise(u.dex) + PointsToRaise(u.luk);

    /// <summary>The highest a single stat can be raised to with <paramref name="points"/> to spend, starting from 1.
    /// Always ≥ 1 (a stat can't go below its floor) and ≤ <see cref="MaxStatValue"/>. Authoring uses this to SNAP an
    /// over-budget entry down to what the unit can actually afford, instead of storing a spread it can't pay for.</summary>
    public static int MaxAffordableStat(int points)
    {
        int v = 1, spent = 0;
        while (v < MaxStatValue)
        {
            int c = StatCost(v);
            if (spent + c > points) break;
            spent += c; v++;
        }
        return v;
    }

    /// <summary>Spend a level's ENTIRE point budget across the six primaries in the given relative weights — the
    /// authoring counterpart of the player's stat screen, used to generate a mob that is exactly as rich as its
    /// level allows (never under-, never over-budget). Respects the rising <see cref="StatCost"/> curve and the
    /// <see cref="MaxStatValue"/> cap, so a lopsided spread genuinely costs more, just as it does for a player.
    ///
    /// <para>Greedy and deterministic: each point goes to whichever stat is furthest BELOW its target share of the
    /// spend so far, among those that can still afford their next point. Ties break in STR→LUK order.</para></summary>
    public static UnitStatInputs Allocate(int level, float wStr, float wAgi, float wVit,
                                          float wInt, float wDex, float wLuk,
                                          int startingPoints = StartingStatPoints)
    {
        var u = UnitStatInputs.Default(level);
        int budget = PointsEarnedByLevel(level, startingPoints);

        var stat = new int[6] { 1, 1, 1, 1, 1, 1 };
        var w    = new float[6] { Mathf.Max(0f, wStr), Mathf.Max(0f, wAgi), Mathf.Max(0f, wVit),
                                  Mathf.Max(0f, wInt), Mathf.Max(0f, wDex), Mathf.Max(0f, wLuk) };
        float wSum = 0f;
        for (int i = 0; i < 6; i++) wSum += w[i];
        if (wSum <= 0f) { w[0] = 1f; wSum = 1f; }

        var spent = new int[6];
        int used = 0;
        while (true)
        {
            int best = -1; float worstDeficit = float.NegativeInfinity;
            for (int i = 0; i < 6; i++)
            {
                if (w[i] <= 0f || stat[i] >= MaxStatValue) continue;
                int cost = StatCost(stat[i]);
                if (used + cost > budget) continue;
                // How far this stat is below the share of the spend its weight says it should hold.
                float deficit = w[i] / wSum - (used > 0 ? spent[i] / (float)used : 0f);
                if (deficit > worstDeficit) { worstDeficit = deficit; best = i; }
            }
            if (best < 0) break;   // nothing affordable / everything capped — the budget is as spent as it can be
            int c = StatCost(stat[best]);
            stat[best]++; spent[best] += c; used += c;
        }

        u.str = stat[0]; u.agi = stat[1]; u.vit = stat[2];
        u.intel = stat[3]; u.dex = stat[4]; u.luk = stat[5];
        return u;
    }

    /// <summary>How much <see cref="StatJumpBonus"/> grows for each ten beyond the square.</summary>
    public const int JumpStepPerTen = 3;

    /// <summary>What crossing every ten points of a primary adds to that primary's headline number, on top of what
    /// each point already gives: <c>⌊stat/10⌋² + 3 × ⌊stat/10⌋</c>. Crossing 10 adds 4 and crossing 100 adds 22, so
    /// the first jump is felt and the late ones keep growing. The headlines: STR → melee attack, DEX → ranged
    /// attack, INT → magic attack, VIT → defence, AGI → evasion, LUK → crit damage (counted in percent).</summary>
    public static int StatJumpBonus(int stat)
    {
        int tens = Mathf.Max(0, stat) / 10;
        return tens * tens + JumpStepPerTen * tens;
    }

    /// <summary>The value a stat at <paramref name="stat"/> next jumps at — the next multiple of ten above it.</summary>
    public static int NextJumpAt(int stat) => (Mathf.Max(0, stat) / 10 + 1) * 10;

    /// <summary>What reaching <paramref name="stat"/> adds through the jump alone (0 off a multiple of ten).</summary>
    public static int JumpGainAt(int stat) => StatJumpBonus(stat) - StatJumpBonus(stat - 1);

    // The flat layer × (1 + ring %). Kept private-shaped as a helper so every stat applies its channel identically.
    static int Scale(int flat, float ringPct) => Mathf.RoundToInt(flat * (1f + ringPct));

    // ---- vitals -------------------------------------------------------------------------------------------------
    public static int MaxHP(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(Mathf.RoundToInt((u.baseMaxHP + u.hpPerLevel * (u.level - 1)) * (1f + u.vit * VitHpPercent)) + eb.hp, ringPct);

    public static int MaxMP(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(Mathf.RoundToInt((u.baseMaxMP + u.mpPerLevel * (u.level - 1)) * (1f + u.intel * IntSpPercent)) + eb.mp, ringPct);

    // ---- offence ------------------------------------------------------------------------------------------------
    /// <summary>The physical attack of every weapon that is not ranged, and of bare hands: STR leads, DEX helps.</summary>
    public static int MeleeAttack(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(u.baseAttack + u.attackPerLevel * (u.level - 1) + u.str * AtkPerStr
              + StatJumpBonus(u.str) + u.dex / AtkPerDex + u.luk / AtkPerLuk + eb.atk, ringPct);

    /// <summary>The physical attack of a ranged weapon: <see cref="MeleeAttack"/> with STR and DEX swapped — DEX
    /// leads, STR helps. Base, level growth, LUK, gear and the ring feed both the same way, so the two differ only in
    /// which primary drives them.</summary>
    public static int RangedAttack(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(u.baseAttack + u.attackPerLevel * (u.level - 1) + u.dex * RangedAtkPerDex
              + StatJumpBonus(u.dex) + u.str / RangedAtkPerStr + u.luk / AtkPerLuk + eb.atk, ringPct);

    /// <summary>The physical attack a unit holding <paramref name="weapon"/> swings with — the one place the held
    /// weapon decides between the two, for normal attacks and physical skills alike.</summary>
    public static int PhysicalAttack(WeaponType weapon, in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        WeaponTypes.IsRanged(weapon) ? RangedAttack(u, eb, ringPct) : MeleeAttack(u, eb, ringPct);

    public static int MagicAttack(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(u.baseMagicAttack + u.magicAttackPerLevel * (u.level - 1) + u.intel * MatkPerInt
              + StatJumpBonus(u.intel) + u.luk / MatkPerLuk + eb.matk, ringPct);

    // ---- defence ------------------------------------------------------------------------------------------------
    public static int Defense(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(u.baseDefense + u.defensePerLevel * (u.level - 1) + u.vit / DefPerVit
              + StatJumpBonus(u.vit) + eb.def, ringPct);

    public static int MagicDefense(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(u.baseMagicDefense + u.magicDefensePerLevel * (u.level - 1) + u.intel / MdefPerInt + eb.mdef, ringPct);

    // ---- accuracy / evasion -------------------------------------------------------------------------------------
    public static int Hit(in UnitStatInputs u, float ringPct) =>
        Scale(u.level + u.dex * HitPerDex + u.luk / LukPerHit, ringPct);

    public static int Flee(in UnitStatInputs u, float ringPct) =>
        Mathf.RoundToInt((u.level + u.agi * FleePerAgi + StatJumpBonus(u.agi) + u.luk / LukPerFlee) * (1f + ringPct));

    // ---- crit / speeds ------------------------------------------------------------------------------------------
    /// <summary>The chance one swing crits against a defender with <paramref name="targetLuk"/> LUK: the attacker's
    /// crit chance minus the defender's crit shield, held inside [0, 95%]. The same sum for a player and a mob.</summary>
    public static float EffectiveCritChance(float critChance01, int targetLuk) =>
        Mathf.Clamp(critChance01 - targetLuk * CritShieldPerLuk, 0f, 0.95f);

    public static float CritChance01(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Mathf.Min(0.95f, u.luk * CritPerLuk + ringPct + eb.critPercent / 100f);

    public static float CritMultiplier(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        BaseCritMultiplier + u.luk * CritDamagePerLuk + StatJumpBonus(u.luk) / 100f
        + ringPct + eb.critDamagePercent / 100f;

    // ---- natural regeneration ------------------------------------------------------------------------------------
    // Both pools regenerate on their own, from the primary that grows them. Reported as a RATE PER SECOND so the
    // caller owns its own cadence, and derived from the pool the caller already computed rather than re-deriving it.
    public static float HPRegenPerSecond(in UnitStatInputs u, int maxHP) => maxHP * u.vit * HpRegenPercentPerVit;

    public static float MPRegenPerSecond(in UnitStatInputs u, int maxMP) => maxMP * u.intel * MpRegenPercentPerInt;

    /// <summary><paramref name="buffMult"/> is the live attack-speed buff (1 = none); the cap applies AFTER it.</summary>
    public static float AttackSpeedMult(in UnitStatInputs u, in EquipBonuses eb, float ringPct, float buffMult) =>
        Mathf.Min(MaxAttackSpeedMult,
                  (1f + (u.agi + u.dex * DexAspdShare) * AspdPerAgi + ringPct + eb.aspdPercent / 100f) * buffMult);

    /// <summary>THE cast-speed value — the multiplier a spell's base wind-up is scaled by (1 = full, 0 = instant).
    /// DEX shortens it on the pre-renewal curve (instant at <see cref="InstantCastDex"/>); the ring's CastSpeed node
    /// and the cast-speed buff divide what is left. DEX is counted ONCE, here.
    ///
    /// <para>There used to be a second "cast speed" multiplier alongside this, fed by DEX again plus the ring and the
    /// buff — and NOTHING in gameplay read it, so both of those contributions only ever moved a number on the stat
    /// panel. Collapsed into this one value so every lift on casting lands in the place the wind-up actually uses.</para>
    ///
    /// <para>Instant at 150 DEX survives every other contribution: the DEX term reaches zero and a divisor cannot lift
    /// it back off zero, so the design's ceiling holds no matter how much ring or buff is stacked on top.</para></summary>
    public static float CastTimeMult(in UnitStatInputs u, float ringPct, float buffMult) =>
        Mathf.Clamp01(1f - u.dex / (float)InstantCastDex) / Mathf.Max(0.01f, (1f + ringPct) * buffMult);

    // ---- damage spread ------------------------------------------------------------------------------------------
    /// <summary>The fraction of MAX a hit can drop to, raised toward 1 by the stabilising stat (DEX physical / INT magic).</summary>
    public static float MinFraction(int stabilisingStat) =>
        Mathf.Clamp01(MinDmgFloorFraction + stabilisingStat * MinFractionPerStat);

    public static int MinAttack(int maxAttack, int dex) =>
        Mathf.Clamp(Mathf.RoundToInt(maxAttack * MinFraction(dex)), 1, Mathf.Max(1, maxAttack));

    public static int MinMagicAttack(int maxMagicAttack, int intel) =>
        Mathf.Clamp(Mathf.RoundToInt(maxMagicAttack * MinFraction(intel)), 1, Mathf.Max(1, maxMagicAttack));
}
