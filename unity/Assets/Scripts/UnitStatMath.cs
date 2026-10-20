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
    // DIVISORS, Ragnarok's: DEX÷5 helps a melee swing, STR÷5 a ranged one, LUK÷5 both
    public const int   AtkPerDex = 5, AtkPerLuk = 5, RangedAtkPerStr = 5;
    // Ragnarok: AGI cuts the time between swings 0.4% a point (DEX a quarter of that); CRIT = LUK × 0.3% + 1%
    public const float AspdPerAgi = 0.004f, CritPerLuk = 0.003f, BaseCrit = 0.01f;
    // VIT and INT scale HP/SP as a PERCENT per point (they multiply the level-driven base, not add flat);
    // DEX raises ASPD a fraction of what AGI does.
    public const float VitHpPercent = 0.01f, IntSpPercent = 0.01f, DexAspdShare = 0.25f;
    /// <summary>Ragnarok's attack-speed scale: hits a second = 50 ÷ (200 − ASPD). 190 is the most there is (5 hits a
    /// second); 0 is the least, so stacked slows can never stretch a swing past 4 seconds.</summary>
    public const int MaxAspd = 190, MinAspd = 0;
    /// <summary>The most movement-speed effects can lift a unit's pace: 250%.</summary>
    public const float MaxMoveMult = 2.5f;
    /// <summary>The most of a blow life steal can return, and the most of a blow reflect can send back.</summary>
    public const float MaxLifeSteal = 0.25f, MaxReflect = 0.5f;
    /// <summary>The range of a creature's own movement speed, in multiples of the shared pace: 0 never moves, 1 is the
    /// pace a person travels at, 2 the fastest there is.</summary>
    public const float MinCreatureMoveSpeed = 0f, MaxCreatureMoveSpeed = 2f;

    /// <summary>How fast a creature travels, in world units a second: the shared gear it is in (walk or run) times its
    /// own movement speed (held inside its range) times its live movement-speed effects (Slow, Haste). Never below 0.</summary>
    public static float CreatureTravelSpeed(float gearSpeed, float ownMoveSpeed, float moveMult) =>
        Mathf.Max(0f, gearSpeed * Mathf.Clamp(ownMoveSpeed, MinCreatureMoveSpeed, MaxCreatureMoveSpeed) * moveMult);
    /// <summary>Pre-renewal "crit shield": the TARGET's LUK lowers an attacker's crit chance 0.3%/pt.</summary>
    public const float CritShieldPerLuk = 0.002f;
    /// <summary>Ragnarok's perfect dodge: each point of the defender's LUK dodges 0.1% of ordinary attacks outright —
    /// a crit included.</summary>
    public const float PerfectDodgePerLuk = 0.001f;
    /// <summary>DEX at which a spell's wind-up reaches zero: castTime = baseCast × (1 − DEX/150).</summary>
    public const int   InstantCastDex = 150;
    public const int   HitPerDex = 1, FleePerAgi = 1;
    // RO-style damage spread: a hit rolls Min…Max. Min is a fraction of Max that DEX (physical) / INT (magic)
    // raise toward 1.0 — the relevant stat both LIFTS and STABILISES damage. A crit ignores the spread.
    public const float MinDmgFloorFraction = 0.55f;   // 0 DEX/INT → a swing can drop to 55% of max
    public const float MinFractionPerStat  = 0.012f;  // each point closes the min→max gap (~38 → min == max)
    /// <summary>A crit deals double damage; crit-damage bonuses can raise it to triple, never further.</summary>
    public const float BaseCritMultiplier = 2f, MaxCritMultiplier = 3f;
    /// <summary>LUK's pull on how hard a crit lands: +0.2% crit damage per point, on top of the base.</summary>
    public const float CritDamagePerLuk = 0.002f;
    /// <summary>Ragnarok's natural regeneration intervals (rAthena natural_healhp_interval / natural_healsp_interval):
    /// health every 6 seconds, mana every 8.</summary>
    public const float HpRegenSeconds = 6f, MpRegenSeconds = 8f;
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

    // ---- the stats chosen at CHARACTER CREATION (the hexagon) --------------------------------------------------
    // Three opposite pairs — STR/INT, AGI/LUK, VIT/DEX — each always adding up to CreationPairTotal, every stat
    // between CreationMin and CreationMax. Below 10 a point costs 2, so every legal spread costs 2 × (30 − 6) = 48:
    // exactly StartingStatPoints, which is why a new character starts with none left.

    public const int CreationMin = 1, CreationMax = 9, CreationPairTotal = 10;

    /// <summary>The stat opposite <paramref name="stat"/> on the creation hexagon, by <see cref="PrimaryStat"/> index.</summary>
    public static int CreationOpposite(int stat) => stat switch { 0 => 3, 3 => 0, 1 => 5, 5 => 1, 2 => 4, 4 => 2, _ => -1 };

    /// <summary>The even spread — every stat at half its pair's total — where the hexagon starts.</summary>
    public static int[] EvenCreationSpread()
    {
        var s = new int[6];
        for (int i = 0; i < 6; i++) s[i] = CreationPairTotal / 2;
        return s;
    }

    /// <summary>True when <paramref name="spread"/> (six values, by <see cref="PrimaryStat"/> index) is a spread the
    /// creation hexagon can make. THE one statement of the rule — the map server applies nothing that fails it.</summary>
    public static bool IsCreationSpread(int[] spread)
    {
        if (spread == null || spread.Length != 6) return false;
        for (int i = 0; i < 6; i++)
        {
            if (spread[i] < CreationMin || spread[i] > CreationMax) return false;
            if (spread[i] + spread[CreationOpposite(i)] != CreationPairTotal) return false;
        }
        return true;
    }

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

    // ---- THE EXPERIENCE CURVE — Ragnarok pre-renewal's, as one formula (decided 2026-09-30) ----------------------
    // Fitted to Ragnarok's hand-written base table for a normal class: a typical level is within 8% of it, the worst
    // (the levels Ragnarok jumps by hand — 29, 39, 49, 59, 69, 70) within 41%, and the whole of 1→99 comes to 1.01×
    // its 405 million. The last term is Ragnarok's wall from 90 to 99. A kill pays by the creature's level on the
    // curve Ragnarok's normal creatures follow (fitted the same way), so the kills a level takes grow as they do there:
    // 3 at level 1, 15 at 30, 49 at 50, 180 at 70, 706 at 90, 4,728 at 98.

    /// <summary>The highest base level — Ragnarok's. The curve past it outgrows the number a level's cost is kept in
    /// (it passes 2.1 billion at level 104).</summary>
    public const int MaxBaseLevel = 99;

    /// <summary>Experience needed to climb from base level <paramref name="level"/> to the next:
    /// <c>6.25 × L^1.23 × e^(0.098L + 0.021 × max(0, L − 90)²)</c>, rounded. Not held to the cap — the ring and the
    /// weapon masteries climb the same curve and go on past 99.</summary>
    public static double LevelCost(int level)
    {
        double L = Mathf.Max(1, level);
        double wall = System.Math.Max(0.0, L - 90.0);
        return System.Math.Round(6.25 * System.Math.Pow(L, 1.23) * System.Math.Exp(0.098 * L + 0.021 * wall * wall));
    }

    /// <summary>Experience needed to climb from base level <paramref name="level"/> to the next — <see cref="LevelCost"/>
    /// held to the cap. The character's level curve lives HERE so the bar a player watches and the lifetime total
    /// below can never disagree about what a level costs.</summary>
    public static int XpToNextFrom(int level) => (int)LevelCost(Mathf.Clamp(level, 1, MaxBaseLevel));

    /// <summary>The experience a kill of a creature of <paramref name="level"/> pays, before its kind multiplies it
    /// (<see cref="CreatureKinds.KillExperience"/>): <c>0.31 × L² × e^(0.02L)</c>, rounded, never below 2.</summary>
    public static int KillXp(int level)
    {
        double L = Mathf.Max(1, level);
        double xp = System.Math.Round(0.31 * L * L * System.Math.Exp(0.02 * L));
        return (int)System.Math.Min(int.MaxValue, System.Math.Max(2.0, xp));
    }

    /// <summary>The TOTAL experience a character that legitimately climbed to <paramref name="level"/> was given
    /// on the way — every rung from 1 up to it, and none of the level they are standing in. Add the experience
    /// held inside the current level to get a lifetime figure. Held to the cap, so a junk level in an old row
    /// costs at most 98 steps.</summary>
    public static long XpEarnedByLevel(int level)
    {
        long total = 0;
        int top = Mathf.Clamp(level, 1, MaxBaseLevel);
        for (int L = 1; L < top; L++) total += XpToNextFrom(L);
        return total;
    }

    // ---- THE THREE TRACKS: one table, one share rule, only the scale differs (decided 2026-09-30) ----------------
    // Every kill's experience is paid to the character, the ring and the weapon in hand through ShareOfKill below, and
    // each track climbs LevelCost. Nothing else about them may differ: a track that rounds, stores or climbs its own
    // way is the special case this rule forbids.

    /// <summary>The scale of every kill's experience the character is paid: all of it.</summary>
    public const double BaseShareOfExperience = 1.0;

    /// <summary>The scale the ring is paid: a third. It goes on earning after base level 99.</summary>
    public const double RingShareOfExperience = 1.0 / 3.0;

    /// <summary>Every track holds its experience to two decimals (decided 2026-09-30): <paramref name="xp"/> rounded to
    /// the nearest hundredth, a half going up. Applied after every change to any track's experience, so none of them
    /// drifts into a third decimal.</summary>
    public static double RoundExperience(double xp)
        => System.Math.Round(xp, 2, System.MidpointRounding.AwayFromZero);

    /// <summary>What one track is paid from a kill worth <paramref name="xp"/>: that experience times the track's
    /// <paramref name="scale"/>, to two decimals. THE one rule every track is paid by.</summary>
    public static double ShareOfKill(int xp, double scale)
        => RoundExperience(System.Math.Max(0, xp) * System.Math.Max(0.0, scale));

    /// <summary>The character experience a kill worth <paramref name="xp"/> pays.</summary>
    public static double BaseXpFromKill(int xp) => ShareOfKill(xp, BaseShareOfExperience);

    /// <summary>The ring experience a kill worth <paramref name="xp"/> pays.</summary>
    public static double RingXpFromKill(int xp) => ShareOfKill(xp, RingShareOfExperience);

    /// <summary>Experience needed to climb from ring level <paramref name="level"/> to the next — the SAME cost the
    /// character's own level <paramref name="level"/> has (<see cref="LevelCost"/>), with no cap: the ring keeps
    /// levelling after the character stops at 99. Past about ring 103 a level costs more than a whole number holds,
    /// and it is held there — a wall, not a wrap.</summary>
    public static int RingXpToNextFrom(int level) => (int)System.Math.Min(int.MaxValue, LevelCost(level));

    // ---- the curves before 2026-09-30 — read ONLY to carry an older save across (Character.RestoreSave) ---------

    /// <summary>The base level cost before the Ragnarok curve: <c>5 + 5L</c>.</summary>
    public static int LegacyXpToNextFrom(int level) => 5 + 5 * Mathf.Max(1, level);

    /// <summary>The ring level cost before the Ragnarok curve — and every weapon mastery's: <c>20 + 12L</c>.</summary>
    public static int LegacyRingXpToNextFrom(int level) => 20 + level * 12;

    /// <summary>How far <paramref name="xpIntoLevel"/> was through a level that cost <paramref name="oldCost"/>, carried
    /// onto a level that now costs <paramref name="newCost"/> — the same share of the way.</summary>
    public static double CarryShareOfLevel(double xpIntoLevel, int oldCost, int newCost)
    {
        if (oldCost <= 0 || newCost <= 0 || xpIntoLevel <= 0.0) return 0.0;
        double share = System.Math.Min(1.0, xpIntoLevel / oldCost);
        return RoundExperience(System.Math.Min(newCost - 0.01, share * newCost));
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
        Scale(Mathf.RoundToInt(BaseHealth(u.level) * (1f + u.vit * VitHpPercent)) + eb.hp, ringPct);

    /// <summary>A creature's health: what <see cref="MaxHP"/> gives a ×1 creature of its build, times its
    /// <paramref name="multiplier"/> (<see cref="MonsterDef.multiplier"/>), rounded half up.</summary>
    public static int CreatureMaxHP(int maxHPAtOne, float multiplier) =>
        (int)System.Math.Min(int.MaxValue, CreaturePay.Scaled(maxHPAtOne, multiplier));

    // ---- the health curve: a same-level fight lasts the same number of hits at every level ----------------------
    // THE BASE HEALTH OF EVERY LEVEL IS WORKED OUT FROM THE DAMAGE A PLAYER OF THAT LEVEL IS EXPECTED TO DO (decided
    // 2026-10-20): a ×1 creature of the reference build, in the +0 metal of its level's grade, takes ReferenceHits
    // ordinary hits — no crit, no skill, no ring — from a player of its level with the same build, holding a sword of
    // that grade folded as far through the grade as that level is (ExpectedFolds). Gear multiplies a weapon up to fifty
    // times while a level used to add only 20 health, so a higher level barely protected anything; now health follows
    // the gear ladder. Players and creatures share the curve, and it never goes down from one level to the next.
    /// <summary>How many ordinary hits a same-level ×1 creature of the reference build takes.</summary>
    public const int ReferenceHits = 5;
    /// <summary>The reference weapon's swing, in milliseconds: a sword's (weight 90 — 0.8 s plus 0.005 s a point).</summary>
    public const int ReferenceSwingMs = 1250;
    /// <summary>The highest level the curve is worked out for — the highest a player reaches, so the only levels a
    /// same-level fight exists at. Any level above it (a shadow's) reads this one.</summary>
    public const int HealthCurveTop = MaxBaseLevel;
    static int[] baseHealthCurve;

    /// <summary>The base health of <paramref name="level"/>, before VIT, gear and the ring: worked out once per level
    /// from <see cref="ReferenceHits"/> (see above) and kept.</summary>
    public static int BaseHealth(int level)
    {
        if (baseHealthCurve == null) baseHealthCurve = BuildHealthCurve();
        return baseHealthCurve[Mathf.Clamp(level, 1, HealthCurveTop)];
    }

    static int[] BuildHealthCurve()
    {
        var curve = new int[HealthCurveTop + 1];
        // Never lower than the level below: a VIT point the reference build buys would otherwise take a little base
        // health away on the level it arrives, and a level-up must never cost anybody health.
        for (int level = 1; level <= HealthCurveTop; level++)
            curve[level] = Mathf.Max(ReferenceBaseHealth(level), level > 1 ? curve[level - 1] : 0);
        curve[0] = curve[1];
        return curve;
    }

    /// <summary>The folds a player of <paramref name="level"/> is expected to have reached on gear of the grade that
    /// level wears (<see cref="CreatureLoot.WornGrade"/>): +0 at the grade's first level, rising evenly to the cap at its
    /// last, so the expected damage climbs level by level instead of only at each new grade.</summary>
    public static int ExpectedFolds(int level)
    {
        var grade = CreatureLoot.WornGrade(level);
        int first = Mathf.Max(1, level), last = first;
        while (first > 1 && CreatureLoot.WornGrade(first - 1) == grade) first--;
        while (last < CreatureLoot.LadderTop && CreatureLoot.WornGrade(last + 1) == grade) last++;
        int cap = CraftingMath.FoldCap(grade);
        if (last <= first) return cap;
        return Mathf.Clamp(Mathf.RoundToInt((float)cap * (level - first) / (last - first)), 0, cap);
    }

    /// <summary>The reference build: the one a creature holding a sword spends its points on (STR, then VIT and DEX,
    /// <see cref="CreatureLoot.BuildFor"/>).</summary>
    public static UnitStatInputs ReferenceBuild(int level) => Allocate(level, 0.45f, 0.1f, 0.25f, 0f, 0.15f, 0.05f);

    /// <summary>One ordinary hit a player of <paramref name="attackerLevel"/> lands on a ×1 creature of
    /// <paramref name="targetLevel"/>, both of the reference build: the attacker holding a sword of its level's grade
    /// at <see cref="ExpectedFolds"/>, the target in +0 metal of its own level's grade on body, shoes, garment and head.</summary>
    public static int ReferenceHit(int attackerLevel, int targetLevel)
    {
        var weapon = CraftingMath.PieceNumbers(EquipSlotType.Weapon, WeaponType.Sword, MaterialKind.Metal,
                                               ExpectedFolds(attackerLevel), CreatureLoot.WornGrade(attackerLevel), 100,
                                               ReferenceSwingMs, 0);
        var eb = default(EquipBonuses);
        eb.atk = weapon.attack;
        int attack = MeleeAttack(ReferenceBuild(attackerLevel), eb, 0f);
        ReferenceArmour(targetLevel, out _, out int hardTenths);
        float hit = Combat.Mitigated(attack, hardTenths, SoftDefense(ReferenceBuild(targetLevel)), false);
        return Mathf.Max(1, Mathf.RoundToInt(hit));
    }

    static readonly EquipSlotType[] ReferenceArmourSlots =
        { EquipSlotType.Armor, EquipSlotType.Boots, EquipSlotType.Garment, EquipSlotType.Head };

    static void ReferenceArmour(int level, out int hp, out int hardTenths)
    {
        hp = 0; hardTenths = 0;
        var grade = CreatureLoot.WornGrade(level);
        for (int i = 0; i < ReferenceArmourSlots.Length; i++)
        {
            var piece = CraftingMath.PieceNumbers(ReferenceArmourSlots[i], WeaponType.None, MaterialKind.Metal, 0, grade, 100, 0, 0);
            hp += piece.hp;
            hardTenths += piece.hardDefenseTenths;
        }
    }

    static int ReferenceBaseHealth(int level)
    {
        ReferenceArmour(level, out int armourHP, out _);
        long total = (long)ReferenceHits * ReferenceHit(level, level);
        float vitShare = 1f + ReferenceBuild(level).vit * VitHpPercent;
        // Rounded DOWN, so the finished health never passes the hits it is sized to.
        return Mathf.Max(1, Mathf.FloorToInt((total - armourHP) / vitShare));
    }

    public static int MaxMP(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(Mathf.RoundToInt((u.baseMaxMP + u.mpPerLevel * (u.level - 1)) * (1f + u.intel * IntSpPercent)) + eb.mp, ringPct);

    // ---- offence ------------------------------------------------------------------------------------------------
    /// <summary>The physical attack of every weapon that is not ranged, and of bare hands: STR leads, DEX helps.</summary>
    public static int MeleeAttack(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(u.baseAttack + u.attackPerLevel * (u.level - 1) + u.str * AtkPerStr
              + TensSquared(u.str) + u.dex / AtkPerDex + u.luk / AtkPerLuk + eb.atk, ringPct);

    /// <summary>The physical attack of a ranged weapon: <see cref="MeleeAttack"/> with STR and DEX swapped — DEX
    /// leads, STR helps. Base, level growth, LUK, gear and the ring feed both the same way, so the two differ only in
    /// which primary drives them.</summary>
    public static int RangedAttack(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(u.baseAttack + u.attackPerLevel * (u.level - 1) + u.dex * RangedAtkPerDex
              + TensSquared(u.dex) + u.str / RangedAtkPerStr + u.luk / AtkPerLuk + eb.atk, ringPct);

    /// <summary>The physical attack a unit holding <paramref name="weapon"/> swings with — the one place the held
    /// weapon (and, for a shuriken, the higher of its holder's total STR and DEX) decides between the two, for normal
    /// attacks and physical skills alike. <paramref name="u"/> must carry the TOTAL primaries, gear included.</summary>
    public static int PhysicalAttack(WeaponType weapon, in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        WeaponTypes.SwingsRanged(weapon, u.str, u.dex) ? RangedAttack(u, eb, ringPct) : MeleeAttack(u, eb, ringPct);

    public static int MagicAttack(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(u.baseMagicAttack + u.magicAttackPerLevel * (u.level - 1) + u.intel * MatkPerInt
              + Squared(u.intel / 5) + eb.matk, ringPct);

    /// <summary>(<paramref name="stat"/> ÷ 10)², whole tens — Ragnarok's bonus for the stat that leads an attack.</summary>
    public static int TensSquared(int stat) => Squared(Mathf.Max(0, stat) / 10);

    static int Squared(int n) => n * n;

    // ---- defence ------------------------------------------------------------------------------------------------
    // Ragnarok's two parts (see Combat.Mitigated). SOFT defence is the flat part, from the body alone: base, level and
    // VIT (INT for magic). HARD defence is the percentage part, in tenths of a percent, from the ring's Defence node
    // (+1 point a rank), what is worn (armour, Ink, the Bulwark word) and timed Defence effects; never below 0, and the
    // 90% ceiling is applied where a blow is taken.
    public static int SoftDefense(in UnitStatInputs u) =>
        u.baseDefense + u.defensePerLevel * (u.level - 1) + VitSoftDefense(u.vit);

    /// <summary>Ragnarok's VIT defence: VIT÷2, plus the larger of VIT×0.3 and VIT²÷150 − 1, each floored.</summary>
    public static int VitSoftDefense(int vit)
    {
        vit = Mathf.Max(0, vit);
        return vit / 2 + Mathf.Max(vit * 3 / 10, vit * vit / 150 - 1);
    }

    public static int SoftMagicDefense(in UnitStatInputs u) =>
        u.baseMagicDefense + u.magicDefensePerLevel * (u.level - 1) + u.intel;

    /// <summary><paramref name="ringPct"/> is the Defence node's value (0.01 a rank = 1 point = 10 tenths);
    /// <paramref name="timedBonus"/> is the live Defence sub-stat (0.06 = 6 points).</summary>
    public static int HardDefenseTenths(float ringPct, int wornTenths, float timedBonus) =>
        Mathf.Max(0, Mathf.RoundToInt(ringPct * 1000f) + wornTenths + Mathf.RoundToInt(timedBonus * 1000f));

    /// <summary>Hard defence as a blow will actually use it: held inside [0, 90%].</summary>
    public static int CappedHardDefenseTenths(int hardTenths) => Mathf.Clamp(hardTenths, 0, Combat.MaxHardDefenseTenths);

    // ---- accuracy / evasion -------------------------------------------------------------------------------------
    // Worn accuracy and evasion (Ink lines, and a leather piece's folds) add straight in, before the ring's lift —
    // the same place worn defence joins the defence sum above.
    public static int Hit(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Scale(u.level + u.dex * HitPerDex + eb.accuracy, ringPct);

    public static int Flee(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Mathf.RoundToInt((u.level + u.agi * FleePerAgi + eb.evasion)
                         * (1f + ringPct));

    // ---- crit / speeds ------------------------------------------------------------------------------------------
    /// <summary>The chance one swing crits against a defender with <paramref name="targetLuk"/> LUK: the attacker's
    /// crit chance minus the defender's crit shield, held inside [0, 95%]. The same sum for a player and a mob.</summary>
    /// <summary>A worn crit shield of <paramref name="percent"/> (whole percent off crit chance) expressed as the LUK it
    /// is worth to the crit roll, so it joins the one crit-shield sum below instead of opening a second one.</summary>
    public static int CritShieldAsLuk(float percent) => Mathf.RoundToInt(percent / (CritShieldPerLuk * 100f));

    public static float EffectiveCritChance(float critChance01, int targetLuk) =>
        Mathf.Clamp01(critChance01 - targetLuk * CritShieldPerLuk);

    public static float CritChance01(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Mathf.Min(1f, BaseCrit + u.luk * CritPerLuk + ringPct + eb.critPercent / 100f);

    public static float CritMultiplier(in UnitStatInputs u, in EquipBonuses eb, float ringPct) =>
        Mathf.Min(MaxCritMultiplier, BaseCritMultiplier + u.luk * CritDamagePerLuk + StatJumpBonus(u.luk) / 100f
                                     + ringPct + eb.critDamagePercent / 100f);

    // ---- natural regeneration ------------------------------------------------------------------------------------
    // RAGNAROK'S FORMULA (rAthena status_calc_regen), whole-number division as Ragnarok does it. Each is what one
    // Ragnarok interval restores; the per-second rate is that over the interval, so the shared ticker keeps its own
    // cadence and pays the same total. Whether peace doubles it is the ticker's business (WeaponDrawMath), not this.

    /// <summary>Health restored per 6 seconds: VIT/5 + MaxHP/200, the second part never below 1.</summary>
    public static int HpRegenPerInterval(int vit, int maxHP) => Mathf.Max(0, vit) / 5 + Mathf.Max(1, maxHP / 200);

    /// <summary>Mana restored per 8 seconds: 1 + INT/6 + MaxMP/100, and from 120 INT another (INT−120)/2 + 4.</summary>
    public static int MpRegenPerInterval(int intel, int maxMP)
    {
        int i = Mathf.Max(0, intel);
        int v = 1 + i / 6 + Mathf.Max(0, maxMP) / 100;
        if (i >= 120) v += (i - 120) / 2 + 4;
        return v;
    }

    public static float HPRegenPerSecond(in UnitStatInputs u, int maxHP) => HpRegenPerInterval(u.vit, maxHP) / HpRegenSeconds;

    public static float MPRegenPerSecond(in UnitStatInputs u, int maxMP) => MpRegenPerInterval(u.intel, maxMP) / MpRegenSeconds;

    /// <summary><paramref name="buffMult"/> is the live attack-speed buff (1 = none). Uncapped: the ceiling is on
    /// <see cref="Aspd"/>, the number a swing is actually timed by.</summary>
    public static float AttackSpeedMult(in UnitStatInputs u, in EquipBonuses eb, float ringPct, float buffMult) =>
        Mathf.Max(0f, (1f + (u.agi + u.dex * DexAspdShare) * AspdPerAgi + ringPct + eb.aspdPercent / 100f) * buffMult);

    /// <summary>Ragnarok's ASPD for a weapon that swings every <paramref name="baseSwingSec"/> at speed 1, sped up by
    /// <paramref name="speedMult"/>: <c>200 − 50 × base ÷ mult</c>, held inside [<see cref="MinAspd"/>,
    /// <see cref="MaxAspd"/>]. A multiplier of 0 (slows stacked to 100%) lands on the floor, never on infinity.</summary>
    public static float Aspd(float baseSwingSec, float speedMult)
    {
        if (speedMult <= 0f) return MinAspd;
        return Mathf.Clamp(200f - 50f * baseSwingSec / speedMult, MinAspd, MaxAspd);
    }

    /// <summary>Seconds between swings at <paramref name="aspd"/>: <c>(200 − ASPD) ÷ 50</c> — 0.2 s at 190, 4 s at 0.</summary>
    public static float SwingIntervalFromAspd(float aspd) => (200f - Mathf.Clamp(aspd, MinAspd, MaxAspd)) / 50f;

    /// <summary>A movement-speed effect multiplier (1 + the live bonus), held inside [0, <see cref="MaxMoveMult"/>].</summary>
    public static float MoveMult(float bonus) => Mathf.Clamp(1f + bonus, 0f, MaxMoveMult);

    public static float LifeStealFraction(float bonus) => Mathf.Clamp(bonus, 0f, MaxLifeSteal);

    public static float ReflectFraction(float bonus) => Mathf.Clamp(bonus, 0f, MaxReflect);

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

    /// <summary>Ragnarok's magic spread: the INT part of a spell runs from INT + (INT÷7)² to INT + (INT÷5)², so the low
    /// end is the high end less the difference of those two squares.</summary>
    public static int MinMagicAttack(int maxMagicAttack, int intel)
    {
        intel = Mathf.Max(0, intel);
        return Mathf.Clamp(maxMagicAttack - (Squared(intel / 5) - Squared(intel / 7)), 1, Mathf.Max(1, maxMagicAttack));
    }

    /// <summary>A defender's FLEE with <paramref name="attackers"/> creatures on it: Ragnarok takes 10% off for every
    /// attacker past the second, and 12 or more always hit.</summary>
    public static int CrowdedFlee(int flee, int attackers)
    {
        if (attackers >= 12) return int.MinValue / 4;
        int past = Mathf.Max(0, attackers - 2);
        return Mathf.RoundToInt(flee * (1f - 0.1f * past));
    }
}
