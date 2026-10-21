// The game's arithmetic, pinned. Run by tools/check-game-math.sh, outside Unity, in about a second.
//
// WHAT EARNS A CHECK HERE: a function where a WRONG ANSWER IS SILENT — a plausible-looking number nobody
// notices for a week. Not things that would obviously break on screen the moment you pressed play; those you
// would see anyway, and testing them is the waste this file exists to avoid.
//
// PREFER THE GENERAL FORM. "The element table matches its list's real size" survives an eleventh element being
// added; "the table is 10 by 10" does not, and is the check that quietly stops covering the thing it names.
// Write a specific number only where a specific number was PROMISED — the 95% accuracy ceiling, the jump at ten.
// Those are promises, and a promise has to be stated to be checked.
using System;
using System.Collections.Generic;
using System.Reflection;

static class Checks
{
    static int failures;

    // Every failure names the GAME file it is about, followed by a colon. That is not decoration: the
    // end-of-turn guard keeps only fault lines naming a file the session actually edited, and a failure that
    // names only itself is filtered into silence and reported as good news.
    static void Fail(string gameFile, string what)
    {
        Console.WriteLine("  WRONG  " + gameFile + ": " + what);
        failures++;
    }

    static void That(bool ok, string gameFile, string what)
    {
        if (!ok) Fail(gameFile, what);
    }

    const string StatMath = "unity/Assets/Scripts/UnitStatMath.cs";
    const string CombatF  = "unity/Assets/Scripts/Combat.cs";
    const string ElementF = "unity/Assets/Scripts/Element.cs";
    const string RingF    = "unity/Assets/Scripts/SkillRing.cs";
    const string CraftF   = "unity/Assets/Scripts/CraftingMath.cs";
    const string MasteryF = "unity/Assets/Scripts/MasteryMath.cs";
    const string StatesF  = "unity/Assets/Scripts/UnitStates.cs";
    const string StatusF  = "unity/Assets/Scripts/StatusController.cs";

    // ---- the experience curve: Ragnarok's, one formula, three tracks -----------------------------------
    // The running total is what a save's lifetime experience is worked out from, so a wrong one silently rewrites
    // real progress on load. The pinned figures are the curve itself (decided 2026-09-30): moving any constant in
    // it moves one of them. The Ragnarok figures are its own hand-written table, which the curve must stay near.
    static void ExperienceCurve()
    {
        long running = 0;
        for (int level = 1; level <= UnitStatMath.MaxBaseLevel; level++)
        {
            long closed = UnitStatMath.XpEarnedByLevel(level);
            if (closed != running)
            {
                Fail(StatMath, "experience earned by level " + level + " is " + closed +
                               ", but adding up each level's cost gives " + running);
                return;   // one report, not a hundred
            }
            running += UnitStatMath.XpToNextFrom(level);
        }
        That(UnitStatMath.XpEarnedByLevel(1) == 0, StatMath, "level 1 should have earned no experience");
        That(UnitStatMath.XpEarnedByLevel(0) == 0, StatMath, "level 0 should have earned no experience");
        That(UnitStatMath.XpEarnedByLevel(int.MaxValue) == UnitStatMath.XpEarnedByLevel(UnitStatMath.MaxBaseLevel), StatMath,
             "an absurd level should read as the cap, never run past it or go negative");

        // THE CURVE ITSELF, pinned.
        That(UnitStatMath.XpToNextFrom(1) == 7, StatMath, "level 1 no longer costs 7: " + UnitStatMath.XpToNextFrom(1));
        That(UnitStatMath.XpToNextFrom(30) == 7755, StatMath, "level 30 no longer costs 7,755: " + UnitStatMath.XpToNextFrom(30));
        That(UnitStatMath.XpToNextFrom(50) == 103194, StatMath, "level 50 no longer costs 103,194: " + UnitStatMath.XpToNextFrom(50));
        That(UnitStatMath.XpToNextFrom(98) == 99941089, StatMath, "level 98 no longer costs 99,941,089: " + UnitStatMath.XpToNextFrom(98));
        That(UnitStatMath.XpEarnedByLevel(99) == 409648693, StatMath,
             "1 to 99 no longer costs 409,648,693 in all: " + UnitStatMath.XpEarnedByLevel(99));

        // AND STILL RAGNAROK'S: within 45% of its own table at every level pinned here.
        int[] roLevel = { 10, 30, 50, 70, 90, 98 };
        long[] roCost = { 320, 7995, 115254, 1473746, 9738720, 99999998 };
        for (int i = 0; i < roLevel.Length; i++)
        {
            double off = UnitStatMath.XpToNextFrom(roLevel[i]) / (double)roCost[i] - 1.0;
            That(Math.Abs(off) <= 0.45, StatMath, "level " + roLevel[i] + " is " + (off * 100).ToString("0") +
                                                  "% off Ragnarok's " + roCost[i]);
        }

        // WHAT A KILL PAYS, by level, and by kind on top.
        That(UnitStatMath.KillXp(1) == 2, StatMath, "a level-1 kill no longer pays 2: " + UnitStatMath.KillXp(1));
        That(UnitStatMath.KillXp(30) == 508, StatMath, "a level-30 kill no longer pays 508: " + UnitStatMath.KillXp(30));
        That(UnitStatMath.KillXp(90) == 15191, StatMath, "a level-90 kill no longer pays 15,191: " + UnitStatMath.KillXp(90));
        That(CreatureKinds.KillExperience(50, 50, 1f) == UnitStatMath.KillXp(50), StatMath,
             "a x1 creature does not pay its level's amount");
        That(CreatureKinds.KillExperience(50, 50, 30f) == 30 * UnitStatMath.KillXp(50), StatMath,
             "a x30 creature does not pay thirty times its level's amount");
        That(CreatureKinds.KillExperience(150, 150, 122f) == 122L * UnitStatMath.KillXp(150), StatMath,
             "a x122 creature does not pay 122 times its level's amount");

        // THE RING IS PAID A THIRD OF EVERY KILL, on the character's own curve and with no cap: a third of what a
        // climb to 99 earns stands the ring at 93, and it keeps climbing after.
        // ONE RULE PAYS ALL THREE TRACKS — only the scale differs, and every track keeps two decimals the same way.
        for (int xp = 0; xp <= 3000; xp += 7)
        {
            That(UnitStatMath.BaseXpFromKill(xp) == UnitStatMath.ShareOfKill(xp, UnitStatMath.BaseShareOfExperience), StatMath,
                 "the character is not paid by the shared rule for a kill worth " + xp);
            That(UnitStatMath.RingXpFromKill(xp) == UnitStatMath.ShareOfKill(xp, UnitStatMath.RingShareOfExperience), StatMath,
                 "the ring is not paid by the shared rule for a kill worth " + xp);
            That(MasteryMath.WeaponXpFromKill(xp, Quality.E) == UnitStatMath.ShareOfKill(xp, MasteryMath.WeaponShareOfExperience), StatMath,
                 "a par weapon is not paid by the shared rule for a kill worth " + xp);
        }
        That(UnitStatMath.BaseXpFromKill(2) == 2.0 && UnitStatMath.RingXpFromKill(2) == 0.67
             && MasteryMath.WeaponXpFromKill(2, Quality.E) == 1.33, StatMath,
             "a kill worth 2 no longer pays 2.00 / 0.67 / 1.33 to the character, the ring and a par weapon");
        That(UnitStatMath.RingXpFromKill(1) == 0.33, StatMath, "a third of 1 is not 0.33");
        That(UnitStatMath.RoundExperience(0.125) == 0.13, StatMath, "a half hundredth does not round up");
        That(UnitStatMath.RingXpFromKill(300) == 100.0, StatMath, "a kill worth 300 does not pay the ring 100");
        for (int ring = 1; ring <= 99; ring++)
            That(UnitStatMath.RingXpToNextFrom(ring) == UnitStatMath.XpToNextFrom(ring), StatMath,
                 "ring " + ring + " no longer costs what base level " + ring + " does");
        That(UnitStatMath.RingXpToNextFrom(120) == int.MaxValue, StatMath, "a ring level past a whole number's reach wraps");
        long left = UnitStatMath.XpEarnedByLevel(99) / 3;
        int reached = 1;
        while (left >= UnitStatMath.RingXpToNextFrom(reached)) { left -= UnitStatMath.RingXpToNextFrom(reached); reached++; }
        That(reached == 93, StatMath, "a third of a climb to base 99 buys ring " + reached + ", not 93");
    }

    // ---- the stat-point budget ------------------------------------------------------------------------
    // A creature built over budget is "a bit harder than expected" for weeks; one built under is "this area
    // feels flat". Neither says anything on screen.
    static void StatPoints()
    {
        for (int value = 1; value < UnitStatMath.MaxStatValue; value++)
        {
            int cost = UnitStatMath.PointsToRaise(value);
            int back = UnitStatMath.MaxAffordableStat(cost);
            if (back != value)
            {
                Fail(StatMath, "raising a stat to " + value + " costs " + cost +
                               " points, but " + cost + " points only affords " + back);
                return;
            }
        }
        // The cost of the next point never falls as a stat grows — a ladder that dips somewhere would make one
        // stat quietly cheaper than its neighbours at that value.
        int previous = 0;
        for (int value = 0; value < UnitStatMath.MaxStatValue; value++)
        {
            int cost = UnitStatMath.StatCost(value);
            That(cost >= previous, StatMath, "the cost of a point DROPS at value " + value);
            previous = cost;
        }
    }

    // ---- accuracy and the two halves of defence -------------------------------------------------------
    static void AccuracyAndDefence()
    {
        // The window is a promise: hopeless accuracy still lands sometimes, and a sitting duck still dodges
        // sometimes, so MISS always appears. The two ends are written as LITERALS on purpose. Reading them
        // from the code instead would make this check self-referential — move the ceiling and the assertion
        // moves with it, so it can never fail. (Found exactly that way: the prover raised the ceiling to 105%
        // and this check said nothing, because it was comparing the code against itself.)
        That(Math.Abs(Combat.MinHitChance - 0.05f) < 0.0001f, CombatF,
             "the accuracy FLOOR is no longer 5%: " + Combat.MinHitChance);
        That(Math.Abs(Combat.MaxHitChance - 1f) < 0.0001f, CombatF,
             "the accuracy CEILING is no longer 100%: " + Combat.MaxHitChance);
        for (int hit = -500; hit <= 500; hit += 25)
            for (int flee = 0; flee <= 500; flee += 25)
            {
                float chance = Combat.HitChance01(hit, flee);
                That(chance >= 0.05f - 0.0001f && chance <= 1f + 0.0001f, CombatF,
                     "hit chance left its window at hit " + hit + " against evasion " + flee + ": " + chance);
            }

        // RAGNAROK'S TWO-PART DEFENCE (decided 2026-10-18): hard defence takes a percentage, soft defence a flat amount
        // after it, and a crit skips both. Hard defence is held inside [0, 90%]. Literals, so a moved cap fails here.
        That(Combat.MaxHardDefenseTenths == 900, CombatF, "hard defence should stop at 90%, not " + Combat.MaxHardDefenseTenths);
        for (int hard = 0; hard <= 2000; hard += 100)
            for (int soft = 0; soft <= 40; soft += 10)
                That(Math.Abs(Combat.Mitigated(100f, hard, soft, true) - 100f) < 0.001f, CombatF,
                     "a critical hit was reduced by defence (" + hard + " tenths, " + soft + " soft)");
        That(Math.Abs(Combat.Mitigated(100f, 0, 20, false) - 80f) < 0.001f, CombatF, "100 against 20 soft defence should leave 80");
        That(Math.Abs(Combat.Mitigated(100f, 500, 0, false) - 50f) < 0.001f, CombatF, "100 against 50% hard defence should leave 50");
        That(Math.Abs(Combat.Mitigated(100f, 450, 12, false) - 43f) < 0.001f, CombatF, "100 against 45% + 12 should leave 43");
        That(Math.Abs(Combat.Mitigated(100f, 2000, 0, false) - 10f) < 0.001f, CombatF, "hard defence past 90% should still leave 10 of 100");
        That(Math.Abs(Combat.Mitigated(100f, -300, 0, false) - 100f) < 0.001f, CombatF, "negative hard defence should never add damage");
        // Timed defence counts as points (0.06 = 6 points = 60 tenths) and never takes hard defence below 0.
        That(UnitStatMath.HardDefenseTenths(0.33f, 450, 0.06f) == 840, StatMath, "33 ring ranks + 45% worn + a 6-point buff should be 840 tenths");
        That(UnitStatMath.HardDefenseTenths(0f, 20, -0.06f) == 0, StatMath, "a defence debuff took hard defence below 0");
        That(UnitStatMath.CappedHardDefenseTenths(1200) == 900, StatMath, "the sheet's hard defence passed 90%");

        // RAGNAROK'S FORMULAS (adopted 2026-10-18): hit = HIT + 80 − FLEE; HIT = level + DEX; FLEE = level + AGI; FLEE
        // loses 10% per attacker past the second and 12 always hit; perfect dodge is 0.1% a LUK; crit = 1% + LUK × 0.3%,
        // and each target LUK takes 0.2% off; VIT gives HP +1% a point; VIT defence = VIT÷2 + max(VIT×0.3, VIT²÷150 − 1);
        // magic attack spreads from INT + (INT÷7)² to INT + (INT÷5)²; a leading stat adds (stat÷10)².
        That(Combat.HitBase == 80 && Math.Abs(Combat.HitChance01(300, 330) - 0.5f) < 0.0001f, CombatF, "HIT 300 vs FLEE 330 should hit 50%");
        var plain = UnitStatInputs.Default(50); plain.dex = 40; plain.agi = 30; plain.luk = 60;
        That(UnitStatMath.Hit(plain, default(EquipBonuses), 0f) == 90, StatMath, "HIT should be level + DEX, no LUK");
        That(UnitStatMath.Flee(plain, default(EquipBonuses), 0f) == 80, StatMath, "FLEE should be level + AGI, no LUK, no step bonus");
        That(UnitStatMath.CrowdedFlee(200, 2) == 200 && UnitStatMath.CrowdedFlee(200, 4) == 160 && UnitStatMath.CrowdedFlee(200, 11) == 20
             && Combat.HitChance01(0, UnitStatMath.CrowdedFlee(900, 12)) >= 1f, StatMath, "the crowd penalty on FLEE is off");
        That(Math.Abs(UnitStatMath.PerfectDodgePerLuk - 0.001f) < 1e-7f && Math.Abs(UnitStatMath.CritShieldPerLuk - 0.002f) < 1e-7f,
             StatMath, "perfect dodge should be 0.1% a LUK and the crit shield 0.2% a LUK");
        That(Math.Abs(UnitStatMath.CritChance01(plain, default(EquipBonuses), 0f) - (0.01f + 60 * 0.003f)) < 1e-5f, StatMath, "crit should be 1% + LUK × 0.3%");
        That(UnitStatMath.VitSoftDefense(99) == 49 + 64 && UnitStatMath.VitSoftDefense(30) == 15 + 9 && UnitStatMath.VitSoftDefense(0) == 0,
             StatMath, "VIT defence should be VIT÷2 + max(VIT×0.3, VIT²÷150 − 1)");
        That(UnitStatMath.TensSquared(99) == 81 && UnitStatMath.TensSquared(9) == 0, StatMath, "a leading stat should add (stat÷10)²");
        That(UnitStatMath.MinMagicAttack(1000, 99) == 1000 - (19 * 19 - 14 * 14), StatMath, "magic spread should be (INT÷5)² − (INT÷7)²");
        var vit = UnitStatInputs.Default(1); vit.vit = 50;
        That(UnitStatMath.MaxHP(vit, default(EquipBonuses), 0f) == UnityEngine.Mathf.RoundToInt(UnitStatMath.BaseHealth(1) * 1.5f), StatMath,
             "VIT should give +1% max HP a point (level 1's base health × 1.5)");

        // A crit's chance can reach 100% (no lower ceiling), held inside [0, 100%] whatever LUK either side has.
        for (int luk = 0; luk <= 1000; luk += 50)
            foreach (float chance in new[] { 0f, 0.3f, 0.95f, 5f })
            {
                float c = UnitStatMath.EffectiveCritChance(chance, luk);
                That(c >= 0f && c <= 1f + 0.0001f, StatMath,
                     "crit chance " + chance + " against " + luk + " LUK left [0, 100%]: " + c);
            }
        That(Math.Abs(UnitStatMath.EffectiveCritChance(1f, 0) - 1f) < 0.0001f, StatMath, "a crit chance of 100% should stay 100%");
        // A crit deals double; crit-damage bonuses stop at triple. Ragnarok's attack speed: 190 at most, 0 at least.
        That(UnitStatMath.BaseCritMultiplier == 2f && UnitStatMath.MaxCritMultiplier == 3f, StatMath, "a crit should be 2x, at most 3x");
        That(UnitStatMath.MaxAspd == 190 && UnitStatMath.MinAspd == 0, StatMath, "ASPD should run 0 to 190");
        That(Math.Abs(UnitStatMath.Aspd(1f, 1f) - 150f) < 0.001f, StatMath, "a 1 s weapon at speed 1 should be ASPD 150");
        That(Math.Abs(UnitStatMath.Aspd(1f, 100f) - 190f) < 0.001f, StatMath, "ASPD passed 190");
        That(UnitStatMath.Aspd(0.8f, 0f) == 0f, StatMath, "attack speed slowed to nothing should land on ASPD 0, not infinity");
        That(Math.Abs(UnitStatMath.SwingIntervalFromAspd(190f) - 0.2f) < 0.0001f, StatMath, "ASPD 190 should swing every 0.2 s");
        That(Math.Abs(UnitStatMath.SwingIntervalFromAspd(0f) - 4f) < 0.0001f, StatMath, "ASPD 0 should swing every 4 s");
        That(Math.Abs(UnitOffense.AttackInterval(0.8f, 0f) - 4f) < 0.0001f, StatMath, "a unit slowed to nothing must still swing every 4 s");
        // Movement-speed effects stop at 250%; life steal at 25% of a blow; reflect at 50%.
        That(UnitStatMath.MoveMult(5f) == 2.5f && UnitStatMath.MoveMult(-3f) == 0.3f, StatMath, "movement speed kept inside [30%, 250%]");
        That(UnitStatMath.CreatureTravelSpeed(3f, 0f, UnitStatMath.MoveMult(-3f)) == 0f
             && UnitStatMath.CreatureTravelSpeed(3f, 0f, UnitStatMath.MoveMult(1f)) == 0f, StatMath,
             "a creature whose own speed is 0 must never move, whatever slows or speed-ups it carries");
        That(UnitStatMath.LifeStealFraction(0.9f) == 0.25f && UnitStatMath.ReflectFraction(0.9f) == 0.5f, StatMath,
             "life steal should stop at 25% and reflect at 50%");
        // The multiplier: health times it, attack the cube root, never below x1.
        That(UnitStatMath.CreatureMaxHP(2000, 1f) == 2000 && UnitStatMath.CreatureMaxHP(2000, 30f) == 60000
             && UnitStatMath.CreatureMaxHP(2000, 1.5f) == 3000 && UnitStatMath.CreatureMaxHP(2000, 0.2f) == 2000,
             StatMath, "a creature's health should be its x1 health times its multiplier, never below x1");
        That(Math.Abs(CreatureKinds.AttackMultiplier(30f) - 3.107f) < 0.001f, StatMath, "a x30 creature should hit about 3.107x");
        That(CreatureKinds.AttackMultiplier(1f) == 1f && CreatureKinds.AttackMultiplier(0f) == 1f, StatMath, "a x1 creature should hit 1x");
        // The look a multiplier wears: none to x2, the dark enchant's level 1 from x3, 2 from x10, 3 from x30, the smoke
        // from x90 (decided 2026-10-20).
        That(DifficultyLook.LookOf(1f) == 0 && DifficultyLook.LookOf(2.9f) == 0 && DifficultyLook.LookOf(3f) == 1
             && DifficultyLook.LookOf(9.9f) == 1 && DifficultyLook.LookOf(10f) == 2 && DifficultyLook.LookOf(29f) == 2
             && DifficultyLook.LookOf(30f) == 3 && DifficultyLook.LookOf(89f) == 3 && DifficultyLook.LookOf(90f) == 4
             && DifficultyLook.LookOf(150f) == 4, StatMath, "a multiplier's look should step at x3, x10, x30 and x90");
        // THE HEALTH CURVE: a x1 creature of the reference build takes ReferenceHits ordinary hits from a player of its
        // level with the same build, at every level the curve covers — never fewer, and never more than a quarter hit
        // past them (the curve never goes down, so the first level of a grade, whose new armour would have let its own
        // figure dip, keeps the one below it) — and no level-up ever lowers base health.
        That(UnitStatMath.ReferenceHits == 5, StatMath, "a same-level fight should last 5 ordinary hits");
        for (int level = 1; level <= UnitStatMath.HealthCurveTop; level++)
        {
            int hp = ReferenceCreatureHP(level), hit = UnitStatMath.ReferenceHit(level, level);
            double hits = hp / (double)hit;
            That(hits > UnitStatMath.ReferenceHits - 1 && hits <= UnitStatMath.ReferenceHits + 0.25, StatMath,
                 "a level-" + level + " x1 creature (" + hp + " health) takes " + hits.ToString("0.00") + " hits of " + hit
                 + " from a level-" + level + " player, not " + UnitStatMath.ReferenceHits);
            That(level == 1 || UnitStatMath.BaseHealth(level) >= UnitStatMath.BaseHealth(level - 1), StatMath,
                 "base health goes down from level " + (level - 1) + " to " + level);
        }
        That(UnitStatMath.ExpectedFolds(1) == 0 && UnitStatMath.ExpectedFolds(14) == 10 && UnitStatMath.ExpectedFolds(15) == 0
             && UnitStatMath.ExpectedFolds(50) == 5 && UnitStatMath.ExpectedFolds(99) == 10 && UnitStatMath.ExpectedFolds(150) == 10,
             StatMath, "expected folds should climb +0 to +10 through each grade's levels");
    }

    // ---- a strike that grows ------------------------------------------------------------------------------
    // Decided 2026-10-06: a strike's damage multiplier grows by a set amount per point of MP or HP the caster gave up
    // (the all-in blows), and by a set amount per metre to its target. Both 0 must mean no growth at all, because that
    // is what every strike authored before them carries.
    const string StrikeF = "unity/Assets/Scripts/StrikeMath.cs";

    static void StrikeGrowthRules()
    {
        That(StrikeMath.Growth(0f, 500, 0f, 30f) == 0f, StrikeF, "a strike with both growth numbers at 0 grew");
        That(Math.Abs(StrikeMath.Growth(0.1f, 250, 0f, 0f) - 25f) < 0.001f, StrikeF, "0.1 per point with 250 given up should add 25");
        That(Math.Abs(StrikeMath.Growth(0.05f, 0, 0f, 0f)) < 0.001f, StrikeF, "nothing given up should add nothing");
        That(Math.Abs(StrikeMath.Growth(0f, 0, 0.05f, 20f) - 1f) < 0.001f, StrikeF, "0.05 per metre at 20 metres should add 1");
        That(Math.Abs(StrikeMath.Growth(0.1f, 10, 0.05f, 20f) - 2f) < 0.001f, StrikeF, "the two growths should add together");
        That(StrikeMath.Growth(0.1f, -5, 0.05f, -3f) == 0f, StrikeF, "a negative amount given up or distance should add nothing");
        // A lobbed arrow (Rainbow, 2026-10-21): real throw physics on level ground.
        That(Math.Abs(Ballistics.FlightSeconds(11f, 45f, 9.81f) - 1.4976f) < 0.001f, StrikeF, "an 11 m lob at 45 degrees should fly about 1.5 s");
        That(Ballistics.FlightSeconds(4f, 45f, 9.81f) < Ballistics.FlightSeconds(11f, 45f, 9.81f), StrikeF, "a shorter lob should land sooner");
        That(Math.Abs(Ballistics.ApexHeight(11f, 45f) - 2.75f) < 0.001f, StrikeF, "an 11 m lob at 45 degrees should peak 2.75 m up");
        That(Ballistics.PointAt(UnityEngine.Vector3.zero, new UnityEngine.Vector3(10f, 0f, 0f), 2f, 1f) == new UnityEngine.Vector3(10f, 0f, 0f), StrikeF, "a lob should end exactly on its landing");
        That(Math.Abs(Ballistics.PointAt(UnityEngine.Vector3.zero, new UnityEngine.Vector3(10f, 0f, 0f), 2f, 0.5f).y - 2f) < 0.001f, StrikeF, "a lob should peak half way");
        // Accuracy above 100 (Thunderhead, decided 2026-10-21): counted 0..100, never negative.
        That(StrikeMath.AccuracyOverPoints(160) == 60, StrikeF, "160 raw hit points should count 60 over");
        That(StrikeMath.AccuracyOverPoints(90) == 0, StrikeF, "a hit chance under 100 should count nothing");
        That(StrikeMath.AccuracyOverPoints(400) == 100, StrikeF, "the accuracy overflow should stop at 100 points");
        // Speed bonus while moving (Sunbreak): percent points of bonus, 0 standing still or slowed.
        That(Math.Abs(StrikeMath.SpeedBonusPoints(1.45f, true) - 45f) < 0.01f, StrikeF, "a x1.45 pace on the move should count 45");
        That(StrikeMath.SpeedBonusPoints(1.45f, false) == 0f, StrikeF, "standing still should count nothing");
        That(StrikeMath.SpeedBonusPoints(0.7f, true) == 0f, StrikeF, "a slowed caster should count nothing, not a penalty");
        // The sniper (decided 2026-10-06): its ordinary attack is a magic shot that grows 2% a metre, and it is the only
        // weapon whose magic can crit.
        That(Math.Abs(StrikeMath.SniperShotGrowth(0f) - 1f) < 0.001f, StrikeF, "a point-blank sniper shot should be x1");
        That(Math.Abs(StrikeMath.SniperShotGrowth(7f) - 1.14f) < 0.001f, StrikeF, "a sniper shot at 7 metres should be x1.14");
        That(Math.Abs(StrikeMath.SniperShotGrowth(-2f) - 1f) < 0.001f, StrikeF, "a negative distance should not shrink a shot");
        foreach (WeaponType w in Enum.GetValues(typeof(WeaponType)))
        {
            That(WeaponTypes.MagicCanCrit(w) == (w == WeaponType.Firearm), StrikeF, w + ": only the sniper's magic may crit");
            That(WeaponTypes.ShootsMagic(w) == (w == WeaponType.Firearm), StrikeF, w + ": only the sniper shoots magic");
        }
    }

    // ---- things that fly -------------------------------------------------------------------------------------
    // Decided 2026-10-06: a thrown shuriken makes 3 jumps, each within 2.5 of the last enemy hit and worth 75% of the
    // hit before it; everything flies at 14 a second; a buff adds whole jumps to a chain that already bounces and never
    // makes one bounce that does not; no chain passes the jump cap.
    const string FlightF = "unity/Assets/Scripts/FlightMath.cs";

    static void FlightRules()
    {
        That(FlightMath.ThrowJumps == 3, FlightF, "a thrown shuriken should make 3 jumps");
        That(Math.Abs(FlightMath.ThrowJumpRange - 2.5f) < 0.001f, FlightF, "a jump should reach 2.5");
        That(Math.Abs(FlightMath.ThrowDamageKept - 0.75f) < 0.001f, FlightF, "a jump should keep 75%");
        That(Math.Abs(FlightMath.Speed - 7f) < 0.001f, FlightF, "everything should fly at 7 a second");
        That(Math.Abs(FlightMath.HopScale(0.75f, 0) - 1f) < 0.001f, FlightF, "the first hit should be worth 100%");
        That(Math.Abs(FlightMath.HopScale(0.75f, 1) - 0.75f) < 0.001f, FlightF, "the first jump should be worth 75%");
        That(Math.Abs(FlightMath.HopScale(0.75f, 3) - 0.421875f) < 0.0001f, FlightF, "the third jump should be worth 42.2%");
        That(Math.Abs(FlightMath.LegSeconds(UnityEngine.Vector3.zero, new UnityEngine.Vector3(7f, 0f, 0f)) - 1f) < 0.001f,
             FlightF, "7 units should take one second");
        That(FlightMath.TotalJumps(3, 0f) == 3, FlightF, "3 jumps with no buff should stay 3");
        That(FlightMath.TotalJumps(3, 2f) == 5, FlightF, "a +2 buff should make 3 jumps 5");
        That(FlightMath.TotalJumps(3, 1.6f) == 4, FlightF, "a buff adds WHOLE jumps — +1.6 adds 1");
        That(FlightMath.TotalJumps(0, 5f) == 0, FlightF, "a buff made an arrow bounce");
        That(FlightMath.TotalJumps(3, 100f) == FlightMath.MaxJumps, FlightF, "a chain passed the jump cap");
    }

    // ---- a dash's trip -------------------------------------------------------------------------------
    // Decided 2026-10-16: a dash's strike lands when the dash arrives, so how long the trip takes is the wait. An
    // un-eased glide is its length over its speed; easing only ever slows it.
    const string GlideF = "unity/Assets/Scripts/GlideEase.cs";

    static void GlideTripSeconds()
    {
        That(Math.Abs(GlideEase.Seconds(20f, 10f, false, false, 0.3f) - 0.5f) < 0.0001f, GlideF,
             "10 metres at 20 a second should take half a second");
        That(GlideEase.Seconds(20f, 0f, false, false, 0.3f) == 0f && GlideEase.Seconds(0f, 10f, true, true, 0.3f) == 0f,
             GlideF, "no trip should take no time");
        float eased = GlideEase.Seconds(20f, 10f, true, true, 0.3f);
        That(eased > 0.5f && eased < 0.5f / 0.2f, GlideF,
             "an eased 10-metre glide at 20 should take longer than half a second and less than its slowest pace, took " + eased);
    }

    // ---- armour words --------------------------------------------------------------------------------
    // Decided 2026-09-23: each armour slot rolls one of ITS OWN words, and no word lives on two slots, so a character
    // can never wear the same word twice. A wrong answer here is silent — a word that does nothing reads as a word.
    const string WordsF = "unity/Assets/Scripts/ArmourWords.cs";

    static void ArmourWordRules()
    {
        var seen = new Dictionary<ArmourWord, EquipSlotType>();
        foreach (EquipSlotType slot in Enum.GetValues(typeof(EquipSlotType)))
            foreach (var w in ArmourWords.For(slot))
            {
                That(!seen.ContainsKey(w), WordsF, w + " can roll on two slots");
                seen[w] = slot;
            }
        foreach (ArmourWord w in Enum.GetValues(typeof(ArmourWord)))
        {
            if (w == ArmourWord.None) continue;
            That(seen.ContainsKey(w), WordsF, w + " belongs to no slot, so it can never roll");
            That(ArmourWords.Amount(w, Quality.Raw) > 0f, WordsF, w + " is worth nothing at Raw");
            That(ArmourWords.Amount(w, Quality.A) > ArmourWords.Amount(w, Quality.Raw), WordsF, w + " does not grow with grade");
        }
        That(ArmourWords.For(EquipSlotType.Weapon).Length == 0, WordsF, "a weapon can roll an armour word");

        // The agreed first numbers, as literals.
        That(Math.Abs(ArmourWords.Amount(ArmourWord.Bulwark, Quality.A) - 6f) < 0.001f, WordsF, "Bulwark at A should be 6 points of hard defence");
        var bulwark = default(EquipBonuses);
        ArmourWords.AddWord(ArmourWord.Bulwark, 6f, ref bulwark);
        That(bulwark.hardDefTenths == 60 && bulwark.hardMdefTenths == 60, WordsF, "Bulwark 6 should be 60 tenths of hard defence and magic defence");
        That(Math.Abs(ArmourWords.Amount(ArmourWord.Emberward, Quality.A) - 15f) < 0.001f, WordsF, "a ward at A should be 15%");
        // Anchored and Wayfarer, decided 2026-10-06: 5% to 25% shorter shoves, +1 to +5 HP a second.
        That(Math.Abs(ArmourWords.Amount(ArmourWord.Anchored, Quality.Raw) - 5f) < 0.001f, WordsF, "Anchored at Raw should be 5%");
        That(Math.Abs(ArmourWords.Amount(ArmourWord.Anchored, Quality.A) - 25f) < 0.001f, WordsF, "Anchored at A should be 25%");
        That(Math.Abs(ArmourWords.Amount(ArmourWord.Wayfarer, Quality.Raw) - 1f) < 0.001f, WordsF, "Wayfarer at Raw should be 1 HP a second");
        That(Math.Abs(ArmourWords.Amount(ArmourWord.Wayfarer, Quality.A) - 5f) < 0.001f, WordsF, "Wayfarer at A should be 5 HP a second");
        var shoes = new EquipBonuses();
        ArmourWords.AddWord(ArmourWord.Wayfarer, 3f, ref shoes);
        var wornTable = new float[Enum.GetValues(typeof(SubStat)).Length];
        ArmourWords.FoldIntoSubStats(shoes, wornTable);
        That(Math.Abs(wornTable[(int)SubStat.HPRegen] - 3f) < 0.001f, WordsF, "a worn Wayfarer does not reach HP regeneration");
        That(Math.Abs(ArmourWords.CutBy(10f, 0.25f) - 7.5f) < 0.001f, WordsF, "a 25% Anchored should leave a shove at 75% of its speed");

        // Ironblood's percentage reaches the crit roll as the LUK it is worth: 7.5% at Ragnarok's 0.2% a point is 37.5,
        // rounded to 38 LUK.
        That(UnitStatMath.CritShieldAsLuk(7.5f) == 38, StatMath, "7.5% crit shield should be worth 38 LUK");
        // A quarter cut takes a quarter off; a ward cuts damage but never touches immunity or a heal.
        That(Math.Abs(ArmourWords.CutBy(8f, 0.25f) - 6f) < 0.001f, WordsF, "a 25% cut of 8 should be 6");
        That(Math.Abs(Elements.Warded(1.5f, 0.1f) - 1.35f) < 0.001f, ElementF, "a 10% ward on 150% should leave 135%");
        That(Elements.Warded(0f, 0.5f) == 0f && Elements.Warded(-0.5f, 0.5f) == -0.5f, ElementF,
             "a ward must never change an immune or a healing answer");
        // A worn word lands in the sub-stat table in that stat's own units, and worn shoes buy no movement speed.
        var table = new float[SubStats.All.Length];
        var eb = default(EquipBonuses);
        CraftingMath.AddPiece(CraftingMath.PieceNumbers(EquipSlotType.Boots, WeaponType.None, MaterialKind.Metal, 5, Quality.Raw, 0, 0, 0),
                              null, null, ref eb);
        ArmourWords.AddWord(ArmourWord.Wellspring, 2.5f, ref eb);
        ArmourWords.FoldIntoSubStats(eb, table);
        That(table[(int)SubStat.MovementSpeed] == 0f, WordsF, "+5 shoes should buy no movement speed");
        That(Math.Abs(table[(int)SubStat.MPRegen] - 2.5f) < 0.0001f, WordsF, "Wellspring 2.5 should be +2.5 MP a second");
        // Block chance — only a skill grants it — never makes a wall.
        That(Math.Abs(Combat.BlockChance(0.13f) - 0.13f) < 0.001f, CombatF, "a 13% block chance should stay 13%");
        That(Combat.BlockChance(1.4f) <= 0.95f + 0.0001f, CombatF, "block chance passed 95%");
        That(Combat.BlockChance(-0.2f) == 0f, CombatF, "a negative block chance should be none");
    }

    // ---- worn accuracy and evasion reach the roll -----------------------------------------------------
    // Pinned because it was silently dead: Ink wrote Evasion and Accuracy onto gear, the character sheet showed them,
    // and the roll that decides a hit never read either. Ten worn points must move the number by ten.
    static void WornAccuracyAndEvasion()
    {
        var u = UnitStatInputs.Default(10);
        var none = default(EquipBonuses);
        var worn = new EquipBonuses { accuracy = 10, evasion = 10 };
        That(UnitStatMath.Hit(u, worn, 0f) - UnitStatMath.Hit(u, none, 0f) == 10, StatMath,
             "10 worn accuracy should add exactly 10 to HIT");
        That(UnitStatMath.Flee(u, worn, 0f) - UnitStatMath.Flee(u, none, 0f) == 10, StatMath,
             "10 worn evasion should add exactly 10 to FLEE");
    }

    // ---- the jump every ten points of a primary -------------------------------------------------------
    // A jump that shrinks, or lands off a multiple of ten, shows only as a build that "feels flat". The two
    // literals are the promise from the action plan: crossing 10 adds 4, crossing 100 adds 22.
    static void StatJumps()
    {
        That(UnitStatMath.StatJumpBonus(10) - UnitStatMath.StatJumpBonus(9) == 4, StatMath,
             "crossing 10 no longer adds 4: " + (UnitStatMath.StatJumpBonus(10) - UnitStatMath.StatJumpBonus(9)));
        That(UnitStatMath.StatJumpBonus(100) - UnitStatMath.StatJumpBonus(99) == 22, StatMath,
             "crossing 100 no longer adds 22: " + (UnitStatMath.StatJumpBonus(100) - UnitStatMath.StatJumpBonus(99)));
        int lastJump = 0;
        for (int stat = 1; stat <= 300; stat++)
        {
            int step = UnitStatMath.StatJumpBonus(stat) - UnitStatMath.StatJumpBonus(stat - 1);
            if (stat % 10 != 0)
                That(step == 0, StatMath, "the jump bonus moved at " + stat + ", which is not a multiple of ten");
            else
            {
                That(step > lastJump, StatMath, "the jump at " + stat + " is not bigger than the one before it");
                lastJump = step;
            }
        }

        // Melee and ranged attack are mirrors: swap STR and DEX and one becomes the other.
        var none = default(EquipBonuses);
        var u = UnitStatInputs.Default(30);
        u.str = 57; u.dex = 23; u.luk = 14;
        var swapped = u;
        swapped.str = 23; swapped.dex = 57;
        That(UnitStatMath.MeleeAttack(u, none, 0f) == UnitStatMath.RangedAttack(swapped, none, 0f), StatMath,
             "melee attack and ranged attack are no longer mirrors with STR and DEX swapped");

        // The held weapon picks which one a unit swings with — and a shuriken picks by the higher of STR and DEX.
        foreach (WeaponType w in Enum.GetValues(typeof(WeaponType)))
        {
            int expected = WeaponTypes.SwingsRanged(w, u.str, u.dex) ? UnitStatMath.RangedAttack(u, none, 0f)
                                                                     : UnitStatMath.MeleeAttack(u, none, 0f);
            That(UnitStatMath.PhysicalAttack(w, u, none, 0f) == expected, StatMath,
                 "holding a " + w + " swings with the wrong attack");
        }
        That(WeaponTypes.SwingsRanged(WeaponType.Bow, 99, 1) && WeaponTypes.SwingsRanged(WeaponType.Firearm, 99, 1),
             "unity/Assets/Scripts/WeaponType.cs", "a bow or a firearm is no longer always ranged");
        That(!WeaponTypes.SwingsRanged(WeaponType.Sword, 1, 99), "unity/Assets/Scripts/WeaponType.cs",
             "a sword swings ranged with high DEX");
        That(UnitStatMath.PhysicalAttack(WeaponType.Shuriken, swapped, none, 0f) == UnitStatMath.RangedAttack(swapped, none, 0f),
             StatMath, "a shuriken with DEX above STR should swing with ranged attack");
        That(UnitStatMath.PhysicalAttack(WeaponType.Shuriken, u, none, 0f) == UnitStatMath.MeleeAttack(u, none, 0f),
             StatMath, "a shuriken with STR above DEX should swing with melee attack");
        That(!WeaponTypes.SwingsRanged(WeaponType.Shuriken, 40, 40), "unity/Assets/Scripts/WeaponType.cs",
             "a shuriken with STR equal to DEX should swing melee");

        // A worn primary reaches the formulas once folded into a unit's inputs — the creature's road.
        var gear = default(EquipBonuses);
        gear.str = 10;
        That(UnitStatMath.MeleeAttack(u.WithGear(gear), none, 0f) > UnitStatMath.MeleeAttack(u, none, 0f), StatMath,
             "a worn STR bonus does not raise melee attack once folded into a unit's inputs");
    }

    // ---- the element chart ----------------------------------------------------------------------------
    // Read against the enum's REAL member count rather than a typed number, so adding an eleventh element is
    // caught here instead of throwing the first time a creature carrying it spawns.
    static void ElementChart()
    {
        int members = Enum.GetValues(typeof(Element)).Length;
        That(Elements.Count == members, ElementF,
             "the element table is sized " + Elements.Count + " but there are " + members + " elements");
        if (Elements.Count != members) return;

        // rAthena accepts −100 to 200 in its element file, so anything outside that is a typing slip.
        for (int level = 1; level <= Elements.MaxLevel; level++)
            foreach (Element attack in Enum.GetValues(typeof(Element)))
                foreach (Element armour in Enum.GetValues(typeof(Element)))
                {
                    float m = Elements.Modifier(attack, armour, level);
                    That(!float.IsNaN(m) && !float.IsInfinity(m), ElementF,
                         attack + " against level-" + level + " " + armour + " is not a real number");
                    That(m >= -1f && m <= 2f, ElementF,
                         attack + " against level-" + level + " " + armour + " is outside −1..2: " + m);
                }
        That(Math.Abs(Elements.Modifier(Element.Neutral, Element.Neutral, 1) - 1f) < 0.001f, ElementF,
             "plain against plain should be exactly one");

        // Cells pinned against Ragnarok's pre-renewal tables — the design promises those, copied exactly.
        ElementCell(Element.Fire, Element.Fire, 1, 0.25f);
        ElementCell(Element.Fire, Element.Fire, 2, 0f);
        ElementCell(Element.Fire, Element.Fire, 3, -0.25f);
        ElementCell(Element.Fire, Element.Fire, 4, -0.5f);
        ElementCell(Element.Neutral, Element.Ghost, 1, 0.25f);
        ElementCell(Element.Neutral, Element.Ghost, 2, 0.25f);
        ElementCell(Element.Neutral, Element.Ghost, 3, 0f);
        ElementCell(Element.Neutral, Element.Ghost, 4, 0f);
        ElementCell(Element.Wind, Element.Water, 1, 1.75f);
        ElementCell(Element.Earth, Element.Earth, 1, 1f);
        ElementCell(Element.Poison, Element.Undead, 1, -0.25f);
        ElementCell(Element.Holy, Element.Holy, 4, -1f);
        ElementCell(Element.Ghost, Element.Neutral, 1, 0.25f);
        ElementCell(Element.Water, Element.Fire, 3, 2f);
        // A level outside 1..4 is held to that range rather than reading past the table.
        ElementCell(Element.Fire, Element.Fire, 0, 0.25f);
        ElementCell(Element.Fire, Element.Fire, 9, -0.5f);

        // A hit turned into a heal gives what it would have dealt, times how far below zero the chart goes.
        That(Elements.ElementHeal(100f, -0.5f) == 50, ElementF,
             "100 after defence at −50% should heal 50, got " + Elements.ElementHeal(100f, -0.5f));
        That(Elements.ElementHeal(-40f, -0.5f) == 1, ElementF,
             "defence bigger than the attack should still heal the floor of 1, got " + Elements.ElementHeal(-40f, -0.5f));
    }

    static void ElementCell(Element attack, Element armour, int level, float want)
    {
        float got = Elements.Modifier(attack, armour, level);
        That(Math.Abs(got - want) < 0.001f, ElementF,
             attack + " against level-" + level + " " + armour + " should be " + want + ", got " + got);
    }

    // ---- the passive ring -----------------------------------------------------------------------------
    static void Ring()
    {
        // Every stat appears on the ring exactly once. A stat missing from it throws only when its node is
        // drawn, which is long after the mistake was made.
        var seen = new Dictionary<StatId, int>();
        foreach (var stat in SkillRing.Ring)
            seen[stat] = seen.TryGetValue(stat, out int n) ? n + 1 : 1;
        foreach (StatId stat in Enum.GetValues(typeof(StatId)))
            That(seen.TryGetValue(stat, out int count) && count == 1, RingF,
                 stat + " appears on the ring " + (seen.TryGetValue(stat, out int c) ? c : 0) + " times, not once");

        // Every node is worth 1% a rank and stops at 33 ranks (decided 2026-10-18).
        That(SkillRing.NodeCap == 33, RingF, "a ring node should stop at 33 ranks, not " + SkillRing.NodeCap);
        foreach (var stat in SkillRing.Ring)
            for (int ranks = 0; ranks <= 60; ranks++)
            {
                float want = 0.01f * Math.Min(ranks, 33);
                That(Math.Abs(SkillRing.Percent(stat, ranks) - want) < 0.0001f, RingF,
                     stat + " at " + ranks + " ranks is " + SkillRing.Percent(stat, ranks) + ", not " + want);
            }
        That(RingProgress.MaxLevel == 99, RingF, "the ring should stop at level 99");
    }

    // ---- the trademark roll: a class, then a skill of it evenly; and what a material's rarity is ---------------
    // A trademark is an ULTIMATE's on 1% of new weapons and an active's or a passive's on the rest, every skill inside
    // its class equally likely; a weapon type with only one class gives that class everything (decided 2026-10-13).
    // Gear has no rarity (decided 2026-10-18); materials keep theirs.
    const string RarityF = "unity/Assets/Scripts/Rarity.cs";

    static void TrademarkRoll()
    {
        That(CraftingMath.UltimateTrademarkPercent == 1, CraftF,
             "an ultimate trademark should come on 1% of new weapons, not " + CraftingMath.UltimateTrademarkPercent + "%");

        // In ANY mix of classes, the class draw over 0..99 hands each class exactly its share, and every candidate's
        // chances together make certainty.
        int[][] mixes = { new[] { 3, 15 }, new[] { 1, 1 }, new[] { 0, 7 }, new[] { 4, 0 }, new[] { 2, 40 } };
        foreach (var mix in mixes)
        {
            int ultimates = mix[0], others = mix[1];
            string name = ultimates + " ultimate(s) and " + others + " other(s)";
            int toUltimates = 0;
            for (int roll = 0; roll < 100; roll++) if (CraftingMath.TrademarkIsUltimate(ultimates, others, roll)) toUltimates++;
            int want = ultimates <= 0 ? 0 : others <= 0 ? 100 : CraftingMath.UltimateTrademarkPercent;
            That(toUltimates == want, CraftF, "with " + name + ", " + toUltimates + " of 100 class draws land on an ultimate, not " + want);
            float certain = CraftingMath.TrademarkChance(true, ultimates, others) * ultimates
                          + CraftingMath.TrademarkChance(false, ultimates, others) * others;
            That(Math.Abs(certain - 1f) < 1e-5f, CraftF, "with " + name + " every trademark's chance adds up to " + certain + ", not 1");
        }
        // The sword as built in this shape: 3 ultimates and 15 others — 1/3% each ultimate, 6.6% each other.
        That(Math.Abs(CraftingMath.TrademarkChance(true, 3, 15) - 0.01f / 3f) < 1e-6f, CraftF, "one of 3 ultimates should roll at 1/3%");
        That(Math.Abs(CraftingMath.TrademarkChance(false, 3, 15) - 0.99f / 15f) < 1e-6f, CraftF, "one of 15 others should roll at 6.6%");
        That(CraftingMath.TrademarkChance(true, 0, 0) == 0f && CraftingMath.TrademarkChance(false, 0, 0) == 0f, CraftF,
             "a weapon type with nothing to roll still has a trademark chance");

        // THE ROLL TEXT: six parts, every part in its place whether or not a trademark or a word is there, every Ink
        // line carrying its tier, and it reads back exactly what was written.
        RollTextRoundTrip("", ArmourWord.None);
        RollTextRoundTrip("skill.sword.lunge", ArmourWord.None);
        RollTextRoundTrip("", ArmourWord.Wellspring);
        RollTextRoundTrip("skill.sword.lunge", ArmourWord.Wellspring);

        // A material's rarity, as decided: Temper Dust and Quench Oil Common; Ink Watery Uncommon, Thin and Faint Rare,
        // Clear and Sharp Epic; Salt at its drop chance, Coarse Uncommon, Fine Epic, Pure Legendary (decided 2026-10-14).
        // Nothing that is not a material is rare.
        That(Rarities.OfMaterial(MaterialKind.TemperDust, 0) == Rarity.Common, RarityF, "Temper Dust is not Common");
        That(Rarities.OfMaterial(MaterialKind.QuenchOil, 0) == Rarity.Common, RarityF, "Quench Oil is not Common");
        Rarity[] ink = { Rarity.Uncommon, Rarity.Rare, Rarity.Rare, Rarity.Epic, Rarity.Epic };
        for (int tier = 1; tier <= CraftingMath.TopInkTier; tier++)
            That(Rarities.OfMaterial(MaterialKind.Ink, tier) == ink[tier - 1], RarityF,
                 "Ink tier " + tier + " is " + Rarities.OfMaterial(MaterialKind.Ink, tier) + ", not " + ink[tier - 1]);
        Rarity[] salt = { Rarity.Uncommon, Rarity.Epic, Rarity.Legendary };
        for (int tier = 1; tier <= salt.Length; tier++)
            That(Rarities.OfMaterial(MaterialKind.Salt, tier) == salt[tier - 1], RarityF,
                 "Salt tier " + tier + " is " + Rarities.OfMaterial(MaterialKind.Salt, tier) + ", not " + salt[tier - 1]);
        That(Rarities.OfMaterial(MaterialKind.None, 0) == Rarity.Common, RarityF, "something that is not a material is not Common");
    }

    const string EquipF = "unity/Assets/Scripts/EquipmentInstance.cs";

    // One copy written to roll text and read back.
    static void RollTextRoundTrip(string trademark, ArmourWord word)
    {
        var made = new EquipmentInstance { quality = Quality.C, upgrade = 7,
                                           trademarkSkillId = trademark, trademarkLift = trademark.Length > 0 ? 0.15f : 0f,
                                           word = word, wordAmount = word != ArmourWord.None ? 2.5f : 0f };
        made.piece.attack = 12;
        made.piece.hardDefenseTenths = 36;
        made.inkStats.Add(InkStat.Defense); made.inkTiers.Add(4); made.inkValues.Add(4.3f);
        made.cardSlots = 3; made.worth = 12345;
        made.basePercent = 108;
        made.baseStats.attack = 54;
        string text = made.EncodeRoll();
        string name = "a copy with " + (trademark.Length > 0 ? "a trademark" : "no trademark") + " and "
                      + (word != ArmourWord.None ? "a word" : "no word");
        That(text.Split(';').Length == 7, EquipF, name + " is written in " + text.Split(';').Length + " parts, not 7: " + text);
        var back = new EquipmentInstance();
        back.ReadRoll(text);
        That(back.piece.hardDefenseTenths == 36, EquipF, name + " loses its hard defence on the way back: " + text);
        That(back.inkStats.Count == 1 && back.inkStats[0] == InkStat.Defense && back.inkTiers[0] == 4
             && Math.Abs(back.inkValues[0] - 4.3f) < 1e-5f, EquipF, name + " loses its Ink line or its tier on the way back: " + text);
        That(back.cardSlots == 3 && back.worth == 12345, EquipF, name + " loses its card slots or its worth on the way back: " + text);
        That(back.quality == Quality.C && back.trademarkSkillId == trademark && back.word == word, EquipF,
             name + " does not read back as written: " + text);
        That(Math.Abs(back.trademarkLift - made.trademarkLift) < 1e-5f && Math.Abs(back.wordAmount - made.wordAmount) < 1e-5f
             && back.piece.attack == 12, EquipF, name + " loses a finished number on the way back: " + text);
        That(back.basePercent == 108 && back.baseStats.attack == 54, EquipF, name + " loses its base roll on the way back: " + text);
        // A text with no base part never rolled: 0, which counts as 100 — never a rolled 100.
        var old = new EquipmentInstance();
        old.ReadRoll(string.Join(";", text.Split(';'), 0, 5));
        That(old.basePercent == 0, EquipF, "a five-part text should read back as never rolled, got " + old.basePercent);
    }

    // ---- what a kill drops: a worn piece, and Temper Dust and Quench Oil --------------------------------------
    // Decided 2026-10-13: one kill in ten drops a piece of what the creature wears. Every kill drops
    // 0.60 Temper Dust and 0.06 Quench Oil a grade of its file's level, times its kind's multiple and its gear's pay.
    const string LootF = "unity/Assets/Scripts/CreatureLoot.cs";

    // ---- what a creature wears, and card slots (decided 2026-10-18) -------------------------------------------
    static void WornGearAndSlots()
    {
        var kinds = new[] { CreatureKind.Normal, CreatureKind.Elite, CreatureKind.Shadow };
        int[][] wantShares = { new[] { 300, 210, 150, 100, 70, 50, 40, 30, 20, 15, 15 }, new[] { 350, 240, 170, 110, 70, 40, 20 },
                               new[] { 450, 300, 150, 100 } };
        int[] low = { 0, 4, 7 };
        for (int k = 0; k < kinds.Length; k++)
        {
            That(CreatureLoot.FileFolds(kinds[k]) == low[k], LootF, kinds[k] + "'s fewest folds should be " + low[k]);
            var hits = new int[11];
            for (int roll = 0; roll < 1000; roll++)
            {
                int f = CreatureLoot.RollWornFolds(kinds[k], roll);
                That(f >= low[k] && f <= 10, LootF, kinds[k] + " rolled " + f + " folds, outside " + low[k] + "..10");
                hits[Math.Min(10, Math.Max(0, f))]++;
            }
            for (int i = 0; i < wantShares[k].Length; i++)
                That(hits[low[k] + i] == wantShares[k][i], LootF, kinds[k] + " +" + (low[k] + i) + " comes up " + hits[low[k] + i]
                     + " times in 1000, not " + wantShares[k][i]);
        }

        That(CraftingMath.CardSlotCap(EquipSlotType.Weapon) == 4 && CraftingMath.CardSlotCap(EquipSlotType.Armor) == 2
             && CraftingMath.CardSlotCap(EquipSlotType.Boots) == 2 && CraftingMath.CardSlotCap(EquipSlotType.Garment) == 2
             && CraftingMath.CardSlotCap(EquipSlotType.Head) == 0, CraftF, "card slot caps should be weapon 4, armour 2, hat 0");
        int[] weaponOdds = { 50, 25, 10, 3, 0 };
        for (int n = 0; n < weaponOdds.Length; n++)
            That(CraftingMath.SlotChancePct(EquipSlotType.Weapon, n) == weaponOdds[n], CraftF, "weapon slot " + (n + 1) + " should be " + weaponOdds[n] + "%");
        int[] armourOdds = { 25, 5, 0 };
        for (int n = 0; n < armourOdds.Length; n++)
            That(CraftingMath.SlotChancePct(EquipSlotType.Boots, n) == armourOdds[n], CraftF, "armour slot " + (n + 1) + " should be " + armourOdds[n] + "%");
        That(CraftingMath.SlotMaterialCost(Quality.Raw, 3) == CraftingMath.QuenchMaterialCost(Quality.Raw) * 27
             && CraftingMath.SlotGoldCost(Quality.A, 2) == CraftingMath.QuenchGoldCost(Quality.A) * 9, CraftF, "slotting should triple per slot held");

        // Worn card slots, every roll: weapon 0-2 / 2-3 / 3-4 evenly; armour a quarter of normals 1, elites 1-2, shadows 2.
        int[,] weaponRange = { { 0, 2 }, { 2, 3 }, { 3, 4 } };
        for (int k = 0; k < kinds.Length; k++)
        {
            var seen = new int[5];
            for (int roll = 0; roll < 100; roll++) seen[CreatureLoot.RollWornCardSlots(EquipSlotType.Weapon, kinds[k], roll)]++;
            int span = weaponRange[k, 1] - weaponRange[k, 0] + 1;
            for (int n = 0; n <= 4; n++)
            {
                bool inside = n >= weaponRange[k, 0] && n <= weaponRange[k, 1];
                That(inside ? Math.Abs(seen[n] - 100 / span) <= 1 : seen[n] == 0, LootF, kinds[k] + " weapon gets " + n + " slots " + seen[n] + " times in 100");
            }
        }
        int normalOne = 0, eliteTwo = 0, shadowTwo = 0;
        for (int roll = 0; roll < 100; roll++)
        {
            if (CreatureLoot.RollWornCardSlots(EquipSlotType.Armor, CreatureKind.Normal, roll) == 1) normalOne++;
            if (CreatureLoot.RollWornCardSlots(EquipSlotType.Armor, CreatureKind.Elite, roll) == 2) eliteTwo++;
            if (CreatureLoot.RollWornCardSlots(EquipSlotType.Armor, CreatureKind.Shadow, roll) == 2) shadowTwo++;
        }
        That(normalOne == 25 && eliteTwo == 50 && shadowTwo == 100, LootF, "armour slots: normal 1 on 25, elite 2 on 50, shadow 2 on 100 of 100 rolls");
        That(CreatureLoot.RollWornCardSlots(EquipSlotType.Head, CreatureKind.Shadow, 99) == 0, LootF, "a hat should never get a slot");

        int[] gradeEdges = { 1, 14, 15, 28, 29, 42, 43, 56, 57, 70, 71, 84, 85, 99, 170 };
        Quality[] gradeAt = { Quality.Raw, Quality.Raw, Quality.F, Quality.F, Quality.E, Quality.E, Quality.D, Quality.D,
                              Quality.C, Quality.C, Quality.B, Quality.B, Quality.A, Quality.A, Quality.A };
        for (int i = 0; i < gradeEdges.Length; i++)
            That(CreatureLoot.WornGrade(gradeEdges[i]) == gradeAt[i], LootF, "a level-" + gradeEdges[i] + " creature should wear "
                 + gradeAt[i] + ", not " + CreatureLoot.WornGrade(gradeEdges[i]));
        That(CreatureLoot.InkTierFor(Quality.Raw) == 0 && CreatureLoot.InkTierFor(Quality.F) == 1 && CreatureLoot.InkTierFor(Quality.C) == 4
             && CreatureLoot.InkTierFor(Quality.B) == 5 && CreatureLoot.InkTierFor(Quality.A) == 5, LootF, "worn Ink tier by grade is off");

        // A summon: every material held, or nothing; a boss leaves after 10 minutes with nobody hitting it.
        That(!AggroMath.GivesUp(0f, 4.99f) && AggroMath.GivesUp(0f, 5f), LootF, "a creature should give up 5 seconds after its target left its aggro area");
        That(AggroMath.TakesAggro(11f, 10f) && !AggroMath.TakesAggro(10f, 10f), LootF, "an attacker should take aggro only by having dealt more than the target");
        That(SummonMath.CanPay(new[] { 1, 2 }, new[] { 1, 2 }) && !SummonMath.CanPay(new[] { 1, 2 }, new[] { 1, 1 })
             && !SummonMath.CanPay(new[] { 1 }, new int[0]), LootF, "a summon must need every material in full");
        That(!SummonMath.ShouldLeave(0f, float.NegativeInfinity, 599f) && SummonMath.ShouldLeave(0f, float.NegativeInfinity, 600f)
             && !SummonMath.ShouldLeave(0f, 500f, 1000f) && SummonMath.ShouldLeave(0f, 500f, 1100f), LootF,
             "a summoned boss should leave 10 minutes after it appeared or was last hit, whichever is later");

        // A rolled weapon brings a build that fits it: casters wear cloth and spend most on INT, shooters and quick
        // blades leather on AGI/DEX, everything swung for strength metal on STR — never plate on a wand.
        var w = new float[6];
        foreach (WeaponType weapon in Enum.GetValues(typeof(WeaponType)))
        {
            if (weapon == WeaponType.None) continue;
            var armour = CreatureLoot.BuildFor(weapon, w);
            float sum = w[0] + w[1] + w[2] + w[3] + w[4] + w[5];
            That(Math.Abs(sum - 1f) < 0.001f, LootF, weapon + "'s build weights add to " + sum + ", not 1");
            bool caster = weapon == WeaponType.Staff || weapon == WeaponType.Wand || weapon == WeaponType.Scythe;
            if (caster) That(armour == MaterialKind.Cloth && w[3] >= 0.4f && w[0] == 0f, LootF, weapon + " should be cloth and INT, no STR");
            else That(armour != MaterialKind.Cloth && w[3] == 0f, LootF, weapon + " should not be a cloth INT build");
        }
    }

    static void KillDropNumbers()
    {
        That(Math.Abs(CreatureLoot.GearDropChance - 0.10f) < 1e-6f, LootF, "a kill should drop a worn piece one time in ten");
        That(CreatureLoot.TemperDustPerKill == 60 && CreatureLoot.QuenchOilPerKill == 2, LootF,
             "a x1 kill should drop 0.60 Temper Dust and 0.02 Quench Oil");
        // Over the hundred rolls, a kill pays exactly its hundredths: whole units always, and the rest that often.
        foreach (float m in new[] { 1f, 1.5f, 3f, 30f, 122f })
            foreach (int perKill in new[] { CreatureLoot.TemperDustPerKill, CreatureLoot.QuenchOilPerKill })
            {
                long want = (long)Math.Floor(perKill * (double)m + 0.5);
                long got = 0;
                for (int roll = 0; roll < 100; roll++) got += CreatureLoot.UnitsPerKill(perKill, m, roll);
                That(got == want, LootF, "a x" + m + " creature pays " + got + " hundredths over 100 kills at " + perKill
                     + " a kill, not " + want);
            }
        // The pinned cases: a x1 creature drops a Temper Dust on 60 kills in 100 and a Quench Oil on 2; a x30 one 18
        // Temper Dust; a creature's level changes nothing.
        That(CreatureLoot.UnitsPerKill(60, 1f, 59) == 1 && CreatureLoot.UnitsPerKill(60, 1f, 60) == 0, LootF,
             "a x1 creature should drop a Temper Dust on rolls 0 to 59 only");
        That(CreatureLoot.UnitsPerKill(2, 1f, 1) == 1 && CreatureLoot.UnitsPerKill(2, 1f, 2) == 0, LootF,
             "a x1 creature should drop a Quench Oil on rolls 0 and 1 only");
        That(CreatureLoot.UnitsPerKill(60, 30f, 0) == 18 && CreatureLoot.UnitsPerKill(60, 30f, 99) == 18, LootF,
             "a x30 creature should drop exactly 18 Temper Dust");
        // Each Salt and Ink opens from its multiplier — Coarse and Watery x1, Fine and Thin x3, Faint x10, Pure and Clear
        // x30, Sharp x90 — and falls its x1 chance times the multiplier on average, past 1 as more than one.
        foreach (var line in CreatureLoot.Lines)
        {
            float from = line.kind == MaterialKind.Salt ? (line.step == 1 ? 1f : line.step == 2 ? 3f : 30f)
                       : (line.step == 1 ? 1f : line.step == 2 ? 3f : line.step == 3 ? 10f : line.step == 4 ? 30f : 90f);
            That(line.fromMultiplier == from && (from <= 1f || CreatureLoot.ExpectedAt(line, from - 0.01f) == 0f)
                 && CreatureLoot.ExpectedAt(line, from) > 0f, LootF,
                 line.kind + " tier " + line.step + " should open from x" + from + ", not x" + line.fromMultiplier);
            That(Math.Abs(CreatureLoot.ExpectedAt(line, 150f) - line.chance * 150f) < 1e-4f, LootF,
                 line.kind + " tier " + line.step + " does not fall its x1 chance times the multiplier");
        }
        That(Math.Abs(CreatureLoot.ExpectedAt(CreatureLoot.Lines[4], 90f) - 0.15f) < 1e-5f, LootF, "Sharp Ink should drop 15% at x90");
    }

    // ---- what a quality grade is worth ------------------------------------------------------------------
    // The grade ladder. Every rung has to be worth more than the one below it, in each of the five things a grade
    // decides, or a quench can take a piece backwards while reading as a promotion.
    static void QualityLadder()
    {
        var grades = (Quality[])Enum.GetValues(typeof(Quality));
        That(grades.Length > 1, CraftF, "there is only one quality grade");

        for (int i = 1; i < grades.Length; i++)
        {
            var lower = grades[i - 1];
            var upper = grades[i];
            That(CraftingMath.InkSlots(upper) >= CraftingMath.InkSlots(lower), CraftF,
                 upper + " holds fewer written stats than " + lower);
            That(CraftingMath.WeaponMasteryCap(upper) > CraftingMath.WeaponMasteryCap(lower), CraftF,
                 upper + " caps weapon mastery no higher than " + lower);
            That(CraftingMath.TrainingSpeed(upper) > CraftingMath.TrainingSpeed(lower), CraftF,
                 upper + " trains no faster than " + lower);
            That(CraftingMath.Band(upper) == CraftingMath.Band(lower) + 1, CraftF,
                 upper + " does not fold with the band above " + lower + "'s");
            // A HIGHER GRADE IS ALWAYS STRONGER (decided 2026-10-14): a grade at +0 beats the grade below it at +10.
            That(CraftingMath.TotalPercent(upper, 0) > CraftingMath.TotalPercent(lower, CraftingMath.FoldCap(lower)), CraftF,
                 upper + " +0 is worth no more than " + lower + " at its cap");
        }
        foreach (var grade in grades)
            That(CraftingMath.FoldCap(grade) == 10, CraftF, grade + " folds to +" + CraftingMath.FoldCap(grade) + ", not +10");

        // Every fold and quench percentage, added: the agreed table.
        That(CraftingMath.TotalPercent(Quality.Raw, 0) == 0 && CraftingMath.TotalPercent(Quality.Raw, 10) == 100
             && CraftingMath.TotalPercent(Quality.F, 0) == 200 && CraftingMath.TotalPercent(Quality.F, 10) == 400
             && CraftingMath.TotalPercent(Quality.E, 0) == 600 && CraftingMath.TotalPercent(Quality.A, 0) == 4200
             && CraftingMath.TotalPercent(Quality.A, 10) == 4900, CraftF,
             "the fold and quench percentages should be 0 / 100 at Raw, 200 / 400 at F, 600 at E +0, 4,200 / 4,900 at A");

        // A WEAPON'S BASE IS 50 × ITS SWING TIME, ÷ 1.5 for a pair (decided 2026-10-14). The swing times are the
        // character settings' own floats, rounded to thousandths once: 1.3 and 1.4 sit just below themselves as floats.
        int Atk(WeaponType w, float seconds, int folds, Quality q, int roll) =>
            CraftingMath.PieceNumbers(EquipSlotType.Weapon, w, MaterialKind.Metal, folds, q, roll,
                                      CraftingMath.Thousandths(seconds), CraftingMath.Thousandths(0.5f)).attack;
        That(Atk(WeaponType.Sword, 1f, 0, Quality.Raw, 0) == 50, CraftF, "a Raw +0 sword should be worth its base, 50");
        That(Atk(WeaponType.Sword, 1f, 0, Quality.Raw, 100) == 50, CraftF, "a roll of 100 and no roll should be the same");
        That(Atk(WeaponType.TwoHandSword, 1.3f, 0, Quality.Raw, 0) == 65, CraftF, "a Greatsword's base should be 65");
        var sniperBase = CraftingMath.PieceNumbers(EquipSlotType.Weapon, WeaponType.Firearm, MaterialKind.Metal, 0, Quality.Raw, 0,
                                                   CraftingMath.Thousandths(1.4f), CraftingMath.Thousandths(0.5f));
        That(sniperBase.magicAttack == 70 && sniperBase.attack == 0, CraftF,
             "a Sniper's base should be 70 MAGIC attack and no attack — its ordinary attack is a magic shot");
        That(Atk(WeaponType.Shuriken, 0.9f, 0, Quality.Raw, 0) == 45, CraftF, "a Shuriken's base should be 45");
        That(Atk(WeaponType.Katar, 0.85f, 0, Quality.Raw, 0) == 28, CraftF, "a Katar, a pair, should be 28.3 → 28");
        That(Atk(WeaponType.Axe, 1.1f, 0, Quality.Raw, 0) == 37, CraftF, "an Axe, a pair, should be 36.7 → 37");
        That(Atk(WeaponType.Sword, 1f, 10, Quality.A, 0) == 2500, CraftF, "an A +10 sword should be 2,500");
        That(Atk(WeaponType.Sword, 1f, 10, Quality.A, 115) == 2875, CraftF, "an A +10 sword at a 115% roll should be 50 × 1.15 × 50 = 2,875");
        That(Atk(WeaponType.Sword, 1f, 0, Quality.Raw, 108) == 54, CraftF, "a 108% roll should be a base of 54");
        var staff = CraftingMath.PieceNumbers(EquipSlotType.Weapon, WeaponType.Staff, MaterialKind.None, 0, Quality.Raw, 0, 1200, 500);
        That(staff.attack == 0 && staff.magicAttack == 60, CraftF, "a staff's base should be 60 magic attack, got " + staff.magicAttack);
        var based = CraftingMath.BaseNumbers(EquipSlotType.Weapon, WeaponType.Sword, MaterialKind.Metal, 108, 1000, 500);
        That(based.attack == 54, CraftF, "a sword's Base line at 108% should be 54, got " + based.attack);

        // ARMOUR: HP, MP, attack speed and evasion as before (head and shoes half); HARD DEFENCE the same on every piece,
        // 7.0% at A +10 at the best ordinary roll (115%), scaled by the roll like every other number (decided 2026-10-18).
        CraftingMath.PieceStats Arm(EquipSlotType s, MaterialKind m, int folds, Quality q, int roll = 0) =>
            CraftingMath.PieceNumbers(s, WeaponType.None, m, folds, q, roll, 0, 0);
        var metal = Arm(EquipSlotType.Armor, MaterialKind.Metal, 0, Quality.Raw);
        That(metal.hardDefenseTenths == 1 && metal.hp == 33, CraftF, "Raw +0 metal body armour should be 0.1% hard defence and 33 HP, got " + metal.hardDefenseTenths + " and " + metal.hp);
        var leather = Arm(EquipSlotType.Armor, MaterialKind.Leather, 0, Quality.Raw);
        That(Math.Abs(leather.attackSpeedPercent - 2.1f) < 0.001f && leather.evasion == 0 && leather.hardDefenseTenths == 0, CraftF,
             "Raw +0 leather body armour should be 2.1% attack speed and 0 evasion, got " + leather.attackSpeedPercent + " and " + leather.evasion);
        var leatherTop = Arm(EquipSlotType.Armor, MaterialKind.Leather, 10, Quality.A);
        That(leatherTop.evasion == 21, CraftF, "A +10 leather body armour should be 21 evasion, got " + leatherTop.evasion);
        var cloth = Arm(EquipSlotType.Armor, MaterialKind.Cloth, 0, Quality.Raw);
        That(cloth.hardMagicDefenseTenths == 1 && cloth.mp == 17 && cloth.hardDefenseTenths == 0, CraftF, "Raw +0 cloth body armour should be 0.1% hard magic defence and 17 MP");
        var shoes = Arm(EquipSlotType.Boots, MaterialKind.Metal, 0, Quality.Raw);
        That(shoes.hardDefenseTenths == 1 && shoes.hp == 17, CraftF, "Raw +0 metal shoes: the same hard defence as a body, half the HP");
        int[] climbTop = { 3, 7, 14, 24, 36, 52, 70 };
        for (int g = 0; g <= (int)Quality.A; g++)
            That(Arm(EquipSlotType.Armor, MaterialKind.Metal, 10, (Quality)g, 115).hardDefenseTenths == climbTop[g], CraftF,
                 (Quality)g + " +10 metal at 115% should give " + climbTop[g] + " tenths, got " + Arm(EquipSlotType.Armor, MaterialKind.Metal, 10, (Quality)g, 115).hardDefenseTenths);
        That(Arm(EquipSlotType.Armor, MaterialKind.Metal, 0, Quality.A, 115).hardDefenseTenths == 60, CraftF, "A +0 metal at 115% should give 6.0%");
        That(Arm(EquipSlotType.Armor, MaterialKind.Metal, 10, Quality.A).hardDefenseTenths == 61
             && Arm(EquipSlotType.Armor, MaterialKind.Metal, 10, Quality.A, 85).hardDefenseTenths == 52
             && Arm(EquipSlotType.Armor, MaterialKind.Metal, 10, Quality.A, 130).hardDefenseTenths == 79, CraftF,
             "an A +10 piece's hard defence should scale with its roll: 5.2% at 85%, 6.1% at 100%, 7.9% at 130%");
        int setDef = Arm(EquipSlotType.Armor, MaterialKind.Metal, 10, Quality.A, 115).hardDefenseTenths
                     + Arm(EquipSlotType.Garment, MaterialKind.Metal, 10, Quality.A, 115).hardDefenseTenths
                     + Arm(EquipSlotType.Boots, MaterialKind.Metal, 10, Quality.A, 115).hardDefenseTenths;
        That(setDef == 210, CraftF, "a full A +10 metal set at 115% should give 21% hard defence, got " + setDef + " tenths");
        var bare = Arm(EquipSlotType.Head, MaterialKind.None, 10, Quality.A);
        That(bare.hardDefenseTenths == 0 && bare.hp == 0 && bare.mp == 0, CraftF, "armour with no material should be worth nothing");
        That(CraftingMath.PieceNumbers(EquipSlotType.None, WeaponType.None, MaterialKind.Metal, 10, Quality.A, 0, 0, 0).hardDefenseTenths == 0,
             CraftF, "a piece with no slot should be worth nothing");
        That(CraftingMath.RollOrPar(0) == 100 && CraftingMath.RollOrPar(85) == 85, CraftF, "a piece that never rolled counts as 100");
    }

    // ---- what a fold and a quench cost, and what recycling gives back -----------------------------------
    // A fold takes Temper Dust and a quench takes Quench Oil, each as many units as the grade counted from one
    // (decided 2026-10-13). The fees are kills of income at the grade's level (decided 2026-10-14). Recycling returns
    // half of what the piece provably took — and nothing for a fold made at Raw, because a tutorial's first run gives
    // those free and a return on them would be paid out of nothing.
    static void CraftCosts()
    {
        var grades = (Quality[])Enum.GetValues(typeof(Quality));
        int[] levels = { 7, 21, 35, 50, 64, 78, 92 };
        for (int g = 0; g < grades.Length; g++)
        {
            That(CraftingMath.FoldMaterialCost(grades[g]) == g + 1, CraftF,
                 "a fold at " + grades[g] + " costs " + CraftingMath.FoldMaterialCost(grades[g]) + " Temper Dust, not " + (g + 1));
            That(CraftingMath.QuenchMaterialCost(grades[g]) == g + 1, CraftF,
                 "a quench at " + grades[g] + " costs " + CraftingMath.QuenchMaterialCost(grades[g]) + " Quench Oil, not " + (g + 1));
            That(CraftingMath.GradePriceLevel(grades[g]) == levels[g], CraftF,
                 grades[g] + "'s fees are priced at level " + CraftingMath.GradePriceLevel(grades[g]) + ", not " + levels[g]);
            That(CraftingMath.FoldGoldCost(grades[g]) == 6 * levels[g] && CraftingMath.QuenchGoldCost(grades[g]) == 60 * levels[g], CraftF,
                 "a fold at " + grades[g] + " should cost " + 6 * levels[g] + " gold and a quench " + 60 * levels[g]);
        }
        That(CraftingMath.FoldGoldCost(Quality.Raw) == 42 && CraftingMath.FoldGoldCost(Quality.A) == 552
             && CraftingMath.QuenchGoldCost(Quality.Raw) == 420 && CraftingMath.QuenchGoldCost(Quality.B) == 4680, CraftF,
             "a fold should cost 42 gold at Raw and 552 at A, a quench 420 at Raw and 4,680 at B");

        foreach (var grade in grades)
            for (int folds = 0; folds <= CraftingMath.FoldCap(grade); folds++)
            {
                // What that piece took at the least, counted here on its own: every grade it climbed was folded to its
                // cap and quenched once, and its standing folds were bought at its own grade.
                int spentDust = folds * CraftingMath.FoldMaterialCost(grade), spentOil = 0;
                int spentGold = folds * CraftingMath.FoldGoldCost(grade);
                for (int g = 0; g < (int)grade; g++)
                {
                    var from = (Quality)g;
                    spentDust += CraftingMath.FoldCap(from) * CraftingMath.FoldMaterialCost(from);
                    spentOil += CraftingMath.QuenchMaterialCost(from);
                    spentGold += CraftingMath.FoldCap(from) * CraftingMath.FoldGoldCost(from) + CraftingMath.QuenchGoldCost(from);
                }
                CraftingMath.RecycleYield(grade, folds, out int dust, out int oil, out int gold);
                string piece = grade + " +" + folds;
                That(dust >= 0 && oil >= 0 && gold >= 0, CraftF, "recycling " + piece + " gives back less than nothing");
                That(2 * dust <= spentDust || dust == CraftingMath.RecycleLeastDust, CraftF,
                     "recycling " + piece + " gives back " + dust + " Temper Dust of the " + spentDust + " it took");
                That(dust >= CraftingMath.RecycleLeastDust, CraftF,
                     "recycling " + piece + " gives back " + dust + " Temper Dust, under the least of " + CraftingMath.RecycleLeastDust);
                That(2 * oil <= spentOil, CraftF, "recycling " + piece + " gives back " + oil + " Quench Oil of the " + spentOil + " it took");
                That(2 * gold <= spentGold, CraftF, "recycling " + piece + " gives back " + gold + " gold of the " + spentGold + " it took");
                That(dust == Math.Max(CraftingMath.RecycleLeastDust, spentDust / 2) && oil == spentOil / 2 && gold == spentGold / 2,
                     CraftF, "recycling " + piece + " should give back half of all it took, Raw folds included, got "
                     + dust + " Temper Dust, " + oil + " Quench Oil and " + gold + " gold");
            }

        // An unfolded F piece took ten Raw folds (10 Temper Dust, 420 gold) and one quench (1 Quench Oil, 420 gold):
        // half back is 5 Temper Dust, no Quench Oil and 420 gold.
        CraftingMath.RecycleYield(Quality.F, 0, out int fDust, out int fOil, out int fGold);
        That(fDust == 5 && fOil == 0 && fGold == 420, CraftF,
             "an unfolded F piece should recycle to 5 Temper Dust, 0 Quench Oil and 420 gold, got " + fDust + ", " + fOil + " and " + fGold);
        CraftingMath.RecycleYield(Quality.Raw, 0, out int rawDust, out int rawOil, out int rawGold);
        That(rawDust == 1 && rawOil == 0 && rawGold == 0, CraftF,
             "an untouched Raw piece should recycle to 1 Temper Dust and nothing else, got " + rawDust + ", " + rawOil + " and " + rawGold);
        // The top of the ladder: an A +10 piece took 280 Temper Dust, 21 Quench Oil and 36,120 gold, its Raw folds included.
        CraftingMath.RecycleYield(Quality.A, 10, out int aDust, out int aOil, out int aGold);
        That(aDust == 140 && aOil == 10 && aGold == 18060, CraftF,
             "an A +10 piece should recycle to 140 Temper Dust, 10 Quench Oil and 18,060 gold, got " + aDust + ", " + aOil + " and " + aGold);

        // Several pieces recycled together give back exactly the sum of each alone: a Raw +0 and an F +0 make
        // 1 + 5 Temper Dust and 0 + 420 gold.
        int sumDust = 0, sumOil = 0, sumGold = 0;
        var sumInks = new int[6];
        CraftingMath.AddRecycleYield(Quality.Raw, 0, null, ref sumDust, ref sumOil, ref sumGold, sumInks);
        CraftingMath.AddRecycleYield(Quality.F, 0, null, ref sumDust, ref sumOil, ref sumGold, sumInks);
        // A piece's worth is what recycling it gives back, priced: gold plus each material at its value.
        That(CraftingMath.WorthOf(100, 3, 20, 2, 50) == 100 + 60 + 100, CraftF, "worth should be gold + dust × value + oil × value");
        That(sumDust == 6 && sumOil == 0 && sumGold == 420, CraftF,
             "a Raw +0 and an F +0 recycled together should give 6 Temper Dust, 0 Quench Oil and 420 gold, got "
             + sumDust + ", " + sumOil + " and " + sumGold);

        // Merging: Ink ten into one; Salt at its worth, 20 Coarse into a Fine and 5 Fine into a Pure; nothing past Pure.
        That(CraftingMath.MergeCount(MaterialKind.Ink, 1) == 10 && CraftingMath.MergeCount(MaterialKind.Salt, 1) == 20
             && CraftingMath.MergeCount(MaterialKind.Salt, 2) == 5 && CraftingMath.MergeCount(MaterialKind.Salt, 3) == 0
             && CraftingMath.MergeCount(MaterialKind.TemperDust, 1) == 0, CraftF,
             "merging should take 10 Ink, 20 Coarse Salt, 5 Fine Salt, and nothing past Pure");
    }

    // ---- a fold's and a quench's luck -----------------------------------------------------------------------------
    // Pinned because a chance nobody can see is a chance nobody notices is wrong.
    static void QuenchOdds()
    {
        // A fold below +4 always lands; from +4 it takes its grade's row; at the cap there is no fold.
        foreach (Quality grade in Enum.GetValues(typeof(Quality)))
        {
            for (int f = 0; f < CraftingMath.SafeFolds; f++)
                That(CraftingMath.FoldChancePct(grade, f) == 100, CraftF, grade + " +" + f + " can fail a fold");
            for (int f = CraftingMath.SafeFolds + 1; f < CraftingMath.FoldCap(grade); f++)
                That(CraftingMath.FoldChancePct(grade, f) <= CraftingMath.FoldChancePct(grade, f - 1), CraftF,
                     grade + " +" + f + " folds more surely than +" + (f - 1));
            That(CraftingMath.FoldChancePct(grade, CraftingMath.FoldCap(grade)) == 0, CraftF, grade + " folds past its cap");
        }
        That(CraftingMath.FoldChancePct(Quality.Raw, 4) == 90 && CraftingMath.FoldChancePct(Quality.Raw, 9) == 40
             && CraftingMath.FoldChancePct(Quality.A, 4) == 22 && CraftingMath.FoldChancePct(Quality.A, 9) == 10, CraftF,
             "a fold should be 90% → 40% at Raw and 22% → 10% at A");
        That(CraftingMath.FoldFailFolds(4) == 4 && CraftingMath.FoldFailFolds(9) == 8, CraftF,
             "a failed fold should take off one fold and never go below +4");

        // A quench goes up, or fails where its Salt says; the chance never passes 95%.
        foreach (Quality grade in Enum.GetValues(typeof(Quality)))
            for (int salt = 0; salt <= 3; salt++)
            {
                int up = CraftingMath.QuenchUpChance(grade, salt);
                That(up >= 0 && up <= CraftingMath.MaxUpChance, CraftF, grade + " with salt " + salt + " has a chance of " + up);
                if (salt > 0)
                    That(up >= CraftingMath.QuenchUpChance(grade, salt - 1), CraftF, "salt " + salt + " helps " + grade + " less than salt " + (salt - 1));
            }
        That(CraftingMath.QuenchUpChance(Quality.Raw, 0) == 40000 && CraftingMath.QuenchUpChance(Quality.B, 0) == 3500
             && CraftingMath.QuenchUpChance(Quality.C, 2) == 8125 && CraftingMath.QuenchUpChance(Quality.B, 3) == 5250
             && CraftingMath.QuenchUpChance(Quality.A, 3) == 0, CraftF,
             "a quench should be 40% at Raw, 3.5% at B, 8.125% at C with Fine, 5.25% at B with Pure, and never at A");
        CraftingMath.QuenchFail(Quality.E, 0, out var noSalt, out int noSaltFolds);
        CraftingMath.QuenchFail(Quality.Raw, 0, out var rawFail, out int rawFolds);
        CraftingMath.QuenchFail(Quality.C, 1, out var coarse, out int coarseFolds);
        CraftingMath.QuenchFail(Quality.C, 2, out var fine, out int fineFolds);
        CraftingMath.QuenchFail(Quality.B, 3, out var pure, out int pureFolds);
        That(noSalt == Quality.F && noSaltFolds == 0 && rawFail == Quality.Raw && rawFolds == 0
             && coarse == Quality.C && coarseFolds == 4 && fine == Quality.C && fineFolds == 4
             && pure == Quality.B && pureFolds == 9, CraftF,
             "a failed quench should drop one grade to +0 with no Salt (Raw stays Raw), keep its grade at +4 with Coarse or Fine, and at +9 with Pure");
        That(!CraftingMath.CanQuench(CraftingMath.Best), CraftF, "the best grade can still be quenched");
    }

    // ---- what one Ink writes ----------------------------------------------------------------------------
    // Pinned because the roll is a weighted walk: a stat that cannot come up, or one that comes up twice as
    // often as its weight, is silent — the piece simply ends up with a different stat than it should have.
    static void InkRoll()
    {
        // An Ink line = the stat's top value x the grade's share (F..A 50..100%) x the tier's share (60..100%), rounded
        // once (decided 2026-10-18). Sharp on A writes the top value; Defence tops at 6.0% so four pieces make 24%.
        That(Math.Abs(CraftingMath.InkValue(InkStat.Defense, Quality.A, 5) - 6f) < 0.001f, CraftF, "Sharp Defence on A should be 6.0%");
        That(Math.Abs(CraftingMath.InkValue(InkStat.Defense, Quality.F, 1) - 1.8f) < 0.001f, CraftF, "Watery Defence on F should be 1.8%");
        That(Math.Abs(CraftingMath.InkValue(InkStat.Attack, Quality.A, 5) - 10f) < 0.001f, CraftF, "Sharp Attack on A should be 10");
        That(Math.Abs(CraftingMath.InkValue(InkStat.STR, Quality.D, 4) - 9f) < 0.001f, CraftF, "Clear STR on D should be 9 (9.45 rounded)");
        That(Math.Abs(CraftingMath.InkValue(InkStat.CritChance, Quality.C, 3) - 1.9f) < 0.001f, CraftF, "Faint crit on C should be 1.9% (1.92 rounded)");
        That(CraftingMath.InkValue(InkStat.Attack, Quality.Raw, 5) == 0f, CraftF, "a Raw piece has no Ink slot, so Ink on it is worth nothing");
        // Every tier and every grade is worth at least the one below, and a flat stat is always whole.
        foreach (var stat in CraftingMath.All)
            for (int g = (int)Quality.F; g <= (int)Quality.A; g++)
                for (int tier = 1; tier <= 5; tier++)
                {
                    float v = CraftingMath.InkValue(stat, (Quality)g, tier);
                    if (tier > 1) That(v >= CraftingMath.InkValue(stat, (Quality)g, tier - 1), CraftF, stat + " drops from tier " + (tier - 1) + " to " + tier + " at " + (Quality)g);
                    if (g > (int)Quality.F) That(v >= CraftingMath.InkValue(stat, (Quality)(g - 1), tier), CraftF, stat + " drops from grade " + (Quality)(g - 1) + " to " + (Quality)g);
                }

        // THE PRIMARY PREMIUM: a Sharp A primary is worth clearly more than the Attack line it is priced against.
        foreach (var stat in CraftingMath.All)
            if (CraftingMath.IsPrimary(stat))
                That(CraftingMath.InkValue(stat, Quality.A, 5) > CraftingMath.InkValue(InkStat.Attack, Quality.A, 5), CraftF,
                     stat + " is worth no more than the same Ink's Attack");

        // Every stat is reachable on a fresh roll, exactly once each, and the whole table is covered.
        var seen = new HashSet<InkStat>();
        var none = new List<InkStat>();
        int total = CraftingMath.RemainingInkWeight(none);
        for (int roll = 0; roll < total; roll++)
        {
            That(CraftingMath.RollInkStat(roll, none, out var got), CraftF, "roll " + roll + " wrote nothing");
            seen.Add(got);
        }
        That(seen.Count == CraftingMath.All.Length, CraftF,
             "only " + seen.Count + " of " + CraftingMath.All.Length + " stats can be rolled");

        // And with stats already written, the rest share the whole remaining weight between them — no stat
        // starved, none doubled by a roll that wrapped.
        var already = new List<InkStat> { InkStat.Attack, InkStat.Defense, InkStat.STR };
        int left = CraftingMath.RemainingInkWeight(already);
        var counts = new Dictionary<InkStat, int>();
        for (int roll = 0; roll < left; roll++)
        {
            That(CraftingMath.RollInkStat(roll, already, out var got), CraftF, "roll " + roll + " wrote nothing");
            That(!already.Contains(got), CraftF, got + " was rolled again though it is already written");
            counts[got] = counts.TryGetValue(got, out int c) ? c + 1 : 1;
        }
        int tally = 0;
        foreach (var kv in counts) tally += kv.Value;
        That(tally == left, CraftF, "the re-spread covers " + tally + " of " + left);
        foreach (var kv in counts)
        {
            int wanted = CraftingMath.IsPrimary(kv.Key) ? 20 : 80;
            That(kv.Value == wanted, CraftF,
                 kv.Key + " takes " + kv.Value + " of the table where its weight is " + wanted);
        }

        // A piece carrying everything cannot be written on.
        var everything = new List<InkStat>(CraftingMath.All);
        That(CraftingMath.RemainingInkWeight(everything) == 0, CraftF, "a full piece still has weight to roll");
        That(!CraftingMath.RollInkStat(0, everything, out _), CraftF, "a full piece was written on anyway");
    }

    // ---- what practice is worth -------------------------------------------------------------------------
    // Pinned because every one of these is a silent wrongness: a level that costs the wrong number of kills,
    // a creature far below you that still teaches, or a lift that runs past the ceiling it promised.
    static void MasteryCurve()
    {
        // The level a pile of experience buys is exactly the level whose cost it has met, and never more — to the
        // hundredth every track now counts in.
        for (int level = 0; level <= 79; level++)
        {
            double cost = MasteryMath.WeaponXpFor(level);
            That(MasteryMath.WeaponLevelFrom(cost) == level, MasteryF,
                 "level " + level + " costs " + cost + " experience, which reads back as level " + MasteryMath.WeaponLevelFrom(cost));
            if (level > 0)
                That(MasteryMath.WeaponLevelFrom(cost - 0.01) == level - 1, MasteryF,
                     "a hundredth under level " + level + "'s cost is not level " + (level - 1));
        }

        // WEAPON MASTERY IS PAID TWO THIRDS OF EVERY KILL (decided 2026-09-30), on the character's own curve: mastery M
        // to M+1 costs what base level M+1 does. At par, mastery 70 — the highest grade's cap — comes at base 74.
        for (int level = 0; level < 79; level++)
            That(MasteryMath.WeaponXpFor(level + 1) - MasteryMath.WeaponXpFor(level) == UnitStatMath.LevelCost(level + 1), MasteryF,
                 "weapon mastery " + level + " to " + (level + 1) + " no longer costs what base level " + (level + 1) + " does");
        That(MasteryMath.WeaponXpFor(70) == 9925452.0, MasteryF,
             "weapon mastery 70 no longer costs 9,925,452 experience: " + MasteryMath.WeaponXpFor(70));
        That(MasteryMath.WeaponXpFromKill(300, Quality.E) == 200.0, MasteryF,
             "a kill worth 300 does not pay a par weapon 200");


        // AND WHAT THOSE LEVELS ARE WORTH: half a percent a level on each of the weapon's three stats, so 35% at 70.
        That(Math.Abs(MasteryMath.WeaponPercent(70) - 0.35f) < 0.0001f, MasteryF,
             "weapon mastery 70 no longer adds 35% to each of its stats: " + MasteryMath.WeaponPercent(70));

        // SKILL LEVELS (decided 2026-10-03): one mastery point per weapon mastery level, and every skill level after
        // the first adds +5% — level 1 is the skill as authored, level 10 is +45%, level 5 +20%.
        That(MasteryMath.SkillPointsFor(70) == 70 && MasteryMath.SkillPointsFor(0) == 0 && MasteryMath.SkillPointsFor(-3) == 0,
             MasteryF, "a weapon mastery level no longer gives exactly one mastery point");
        That(MasteryMath.SkillLevelLift(0) == 0f && MasteryMath.SkillLevelLift(1) == 0f, MasteryF,
             "an unlearned or level-1 skill is lifted at all");
        That(Math.Abs(MasteryMath.SkillLevelLift(10) - 0.45f) < 0.0001f, MasteryF,
             "a level-10 skill is no longer +45%: " + MasteryMath.SkillLevelLift(10));
        That(Math.Abs(MasteryMath.SkillLevelLift(5) - 0.20f) < 0.0001f, MasteryF,
             "a level-5 skill is no longer +20%: " + MasteryMath.SkillLevelLift(5));

        // A KILL PAYS THE WEAPON TWO THIRDS OF WHAT IT PAID YOU, at the speed the grade in hand trains — E is par.
        // A kill worth nothing teaches nothing, which is what makes a training dummy
        // (it pays no experience because it never dies) a dead end for mastery rather than a free ride.
        That(MasteryMath.WeaponXpFromKill(0, Quality.A) == 0f, MasteryF,
             "a kill worth no experience still teaches");
        That(MasteryMath.WeaponXpFromKill(-50, Quality.A) == 0f, MasteryF,
             "a kill worth less than nothing still teaches");
        That(MasteryMath.WeaponXpFromKill(100, Quality.A) > MasteryMath.WeaponXpFromKill(100, Quality.Raw), MasteryF,
             "a fine weapon trains no faster than a raw one");

    }

    // ---- a buff's number is a bonus, stacking or not --------------------------------------------------
    // Pinned because it broke: one field meant a whole multiplier on a plain buff and a per-stack bonus on a
    // stacking one, so Momentum authored at 1.03 ran as +103% a stack. The literals are the promise written on the
    // property itself: 0.3 is +30%, and ten stacks of 0.03 are +30% too.
    static void BuffBonus()
    {
        var single = new UnitStates.State { stacking = false };
        single.stacks.Add(new UnitStates.State.Stack { magnitude = 0.3f });
        single.Refold();
        That(Math.Abs(single.Mult - 1.3f) < 0.0001f, StatesF,
             "a single +30% buff (0.3) no longer multiplies by 1.3: " + single.Mult);

        var pile = new UnitStates.State { stacking = true };
        for (int i = 0; i < 10; i++) pile.stacks.Add(new UnitStates.State.Stack { magnitude = 0.03f });
        pile.Refold();
        That(Math.Abs(pile.Mult - 1.3f) < 0.0001f, StatesF,
             "ten stacks of +3% (0.03 each) no longer multiply by 1.3: " + pile.Mult);
    }

    // ---- a trademark skill's efficiency ------------------------------------------------------------------
    // Decided 2026-09-30: a flat 5% for everyone, trademark skills only (whatever weapon is held), +20 points on the cast's main numbers.
    const string EfficiencyF = "unity/Assets/Scripts/SkillEfficiency.cs";

    static void SkillEfficiencyNumbers()
    {
        That(SkillEfficiency.Procs(true, 0.0499f), EfficiencyF, "a roll of 0.0499 on a trademark no longer procs");
        That(!SkillEfficiency.Procs(true, 0.05f), EfficiencyF, "a roll of 0.05 on a trademark now procs — the chance is over 5%");
        That(!SkillEfficiency.Procs(false, 0f), EfficiencyF, "a skill that is not a trademark skill can now proc");
        That(Math.Abs(SkillEfficiency.OutputLift(0.10f, true) - 0.30f) < 0.0001f, EfficiencyF,
             "an efficient cast on a +10% trademark no longer lifts by +30%: " + SkillEfficiency.OutputLift(0.10f, true));
        That(Math.Abs(SkillEfficiency.OutputLift(0.10f, false) - 0.10f) < 0.0001f, EfficiencyF,
             "a cast that is not efficient now has its lift changed: " + SkillEfficiency.OutputLift(0.10f, false));
    }

    // ---- a timed effect's clock stops while its owner is away -----------------------------------------
    // Decided 2026-09-23: only death ends a timed effect, and logging out PAUSES it. A wrong answer here is silent —
    // a buff that comes back with the wrong time left looks exactly like a buff.
    const string SaveF = "unity/Assets/Scripts/TimedEffectSave.cs";

    static void TimedEffectPause()
    {
        // Saved at game-time 100 with 30s left on one stack and 5s on another; read back on a server whose clock says 5.
        var live = new UnitStates.State { id = StateId.MaxHP, stacking = true, startTime = 90f, nextTick = 100.4f, description = "d" };
        live.stacks.Add(new UnitStates.State.Stack { endTime = 130f, magnitude = 0.2f, intAmount = 3 });
        live.stacks.Add(new UnitStates.State.Stack { endTime = 105f, magnitude = 0.1f, intAmount = 4 });
        live.stacks.Add(new UnitStates.State.Stack { endTime = 99f,  magnitude = 9f,   intAmount = 9 });
        live.Refold();
        float fracBefore = (130f - 100f) / (130f - 90f);

        var rec = TimedEffectSave.Capture(live, 100f);
        That(rec != null && rec.stackSecondsLeft.Length == 2, SaveF,
             "a stack with no time left should be dropped, keeping 2 — kept " + (rec == null ? "nothing" : rec.stackSecondsLeft.Length.ToString()));
        if (rec == null) return;
        That(TimedEffectSave.TryKind(rec, out var kind) && kind == StateId.MaxHP, SaveF, "the saved kind should read back as MaxHP");

        var back = new UnitStates.State();
        That(TimedEffectSave.Restore(rec, 5f, back), SaveF, "a record with stacks in it should restore");
        That(Math.Abs(back.endTime - 35f) < 0.001f, SaveF,
             "30s left, restored at 5, should end at 35 (the clock paused) — ends at " + back.endTime);
        That(back.stacks.Count == 2 && Math.Abs(back.stacks[1].endTime - 10f) < 0.001f, SaveF,
             "each stack keeps its own time left: the 5s one should end at 10");
        That(Math.Abs(back.magnitude - 0.3f) < 0.0001f && back.intAmount == 7, SaveF,
             "the totals rebuilt from the stacks should be 0.3 and 7, got " + back.magnitude + " and " + back.intAmount);
        float fracAfter = (back.endTime - 5f) / (back.endTime - back.startTime);
        That(Math.Abs(fracAfter - fracBefore) < 0.001f, SaveF,
             "the tray's fill should read the same before and after: " + fracBefore + " vs " + fracAfter);
        That(Math.Abs(back.nextTick - 5.4f) < 0.001f, SaveF,
             "0.4s to the next tick should still be 0.4s after the pause, got " + (back.nextTick - 5f));

        // Delayed damage already due when the save was written is still owed — due the moment it is read back.
        var debt = new UnitStates.State { id = StateId.DeferredDamage, stacking = true, startTime = 90f };
        debt.stacks.Add(new UnitStates.State.Stack { endTime = 99f, intAmount = 40 });
        debt.Refold();
        var owed = TimedEffectSave.Capture(debt, 100f);
        That(owed != null && owed.stackSecondsLeft.Length == 1 && owed.stackSecondsLeft[0] == 0f, SaveF,
             "delayed damage past its time must be kept, due at once — not dropped");

        // A name the game no longer has is refused rather than guessed at.
        That(!TimedEffectSave.TryKind(new SaveData.TimedEffect { kind = "Slow" }, out _), SaveF,
             "a removed state's name must not read back as some other state");
    }

    // ---- regen is paid per SECOND ---------------------------------------------------------------------
    // Pinned because it broke: regen was authored per tick and labelled per second, so the 0.8s tick healed 25% more
    // than any screen said. The rule now lives in the one payout health and mana both use, so it is checked there:
    // a rate of R a second, ticked for T seconds, pays R x T, give or take the one point still carried over.
    // The payout is private to the ticker, so it is reached by name — a rename fails here loudly instead of
    // quietly leaving regen unchecked.
    static void RegenPerSecond()
    {
        // RAGNAROK'S NATURAL REGENERATION (rAthena status_calc_regen): VIT/5 + MaxHP/200 (at least 1) per 6 seconds;
        // 1 + INT/6 + MaxMP/100 per 8 seconds, plus (INT-120)/2 + 4 from 120 INT.
        const string RegenF = "unity/Assets/Scripts/UnitStatMath.cs";
        That(UnitStatMath.HpRegenPerInterval(30, 1000) == 11, RegenF, "VIT 30, 1000 HP should restore 11 per 6s, got " + UnitStatMath.HpRegenPerInterval(30, 1000));
        That(UnitStatMath.HpRegenPerInterval(1, 100) == 1, RegenF, "VIT 1, 100 HP should restore the least 1 per 6s, got " + UnitStatMath.HpRegenPerInterval(1, 100));
        That(UnitStatMath.HpRegenPerInterval(99, 5000) == 44, RegenF, "VIT 99, 5000 HP should restore 44 per 6s, got " + UnitStatMath.HpRegenPerInterval(99, 5000));
        That(UnitStatMath.MpRegenPerInterval(1, 50) == 1, RegenF, "INT 1, 50 MP should restore 1 per 8s, got " + UnitStatMath.MpRegenPerInterval(1, 50));
        That(UnitStatMath.MpRegenPerInterval(60, 1000) == 21, RegenF, "INT 60, 1000 MP should restore 21 per 8s, got " + UnitStatMath.MpRegenPerInterval(60, 1000));
        That(UnitStatMath.MpRegenPerInterval(130, 2000) == 51, RegenF, "INT 130, 2000 MP should restore 51 per 8s, got " + UnitStatMath.MpRegenPerInterval(130, 2000));

        var owed = typeof(StatusController).GetMethod("Owed", BindingFlags.NonPublic | BindingFlags.Static);
        if (owed == null)
        {
            Fail(StatusF, "the regen payout (Owed) is gone, so regen per second is no longer checked");
            return;
        }
        foreach (float rate in new[] { 6f, 2.5f, 0.5f })
        {
            var args = new object[] { 0f, rate };
            int ticks = 100, paid = 0;
            for (int i = 0; i < ticks; i++) paid += (int)owed.Invoke(null, args);
            float seconds = ticks * UnitStates.RegenInterval;
            That(Math.Abs(paid - rate * seconds) <= 1f, StatusF,
                 "regen of " + rate + " a second paid " + paid + " over " + seconds + " seconds, not " + rate * seconds);
        }
    }

    // THE ONE WALKING RULE every body and every route obeys. A wrong answer here is silent: a creature that will not
    // climb a hill, or plans a route up a ledge it then walks into, looks like "the AI being dumb" for weeks.
    const string WalkF = "unity/Assets/Scripts/GroundElevation.cs";
    static void WalkingRule()
    {
        float t20 = (float)Math.Tan(20.0 * Math.PI / 180.0), t50 = (float)Math.Tan(50.0 * Math.PI / 180.0);
        float t44 = (float)Math.Tan(44.0 * Math.PI / 180.0);
        That(GroundElevation.Walkable(0.05f, 0.5f, 0f, 0f), WalkF, "a one-notch curb (0.05) between flat ground must be walkable");
        That(!GroundElevation.Walkable(0.1f, 0.5f, 0f, 0f), WalkF, "a two-notch ledge (0.1) between flat ground must be a wall");
        That(GroundElevation.Walkable(0.5f * t20, 0.5f, t20, t20), WalkF, "a 20-degree hillside must be walkable");
        That(GroundElevation.Walkable(0.5f * t44, 0.5f, t44, t44), WalkF, "a 44-degree slope must be walkable (the limit is 45)");
        That(!GroundElevation.Walkable(0.5f * t50, 0.5f, t50, t50), WalkF, "a 50-degree slope must be a wall (the limit is 45)");
        That(GroundElevation.Walkable(0.09f, 0.5f, 0f, t20), WalkF, "the foot of a 20-degree slope, flat on one side, must be walkable");
        That(!GroundElevation.Walkable(0.25f, 0.5f, t20, 0f), WalkF, "a 0.25 drop beside a 20-degree slope must be a wall");
        That(!GroundElevation.Walkable(1.5f, 0.5f, 0f, 0f), WalkF, "a cliff must be a wall");
        That(GroundElevation.Walkable(-0.05f, 0.5f, 0f, 0f) == GroundElevation.Walkable(0.05f, 0.5f, 0f, 0f), WalkF,
             "down a step and up it must get the same answer");
        // Every body takes the full step: a capsule grown to HeightForFullStep accepts it, at any size and scale.
        foreach (var (radius, sy, sr) in new[]{ (0.013f, 1f, 1f), (0.3f, 0.044f, 0.044f), (0.15f, 1f, 1f), (0.3f, 0.2f, 0.25f), (0.02f, 3f, 0.5f) })
        {
            float h = GroundElevation.HeightForFullStep(radius, sy, sr);
            That(GroundElevation.StepBoundFor(h, radius, sy, sr) >= GroundElevation.MaxClimbStep, WalkF,
                 "a capsule of radius " + radius + " at scale " + sy + "/" + sr + " grown to " + h + " must take the full step");
        }
        That(GroundElevation.HeightForFullStep(0.15f, 1f, 1f) <= 0.5f, WalkF,
             "the player's 0.5-tall capsule must already take the full step (it must never be grown)");
    }

    // WHERE RAIN WATER STANDS, and which mirror each puddle shows. A wrong answer is silent: a puddle a few
    // millimetres off its surface shows a reflection a pixel or two out of place, which nobody can name as a bug.
    const string WaterF = "unity/Assets/Scripts/WaterPooling.cs";
    static void WaterRules()
    {
        var heights = new[] { 0f, 2.7f, 2.704f, 0.003f };
        That(WaterPooling.MatchHeight(2.704f, heights, 4) == 2, WaterF, "a cliff top's grass 4 mm over the ground must match its own height, not the ground's");
        That(WaterPooling.MatchHeight(2.7f, heights, 4) == 1, WaterF, "the ground at 2.7 must match 2.7");
        That(WaterPooling.MatchHeight(0.0015f, heights, 3) == 0, WaterF, "a surface 1.5 mm over a water height must still lie on it");
        That(WaterPooling.MatchHeight(1.2f, heights, 4) == -1, WaterF, "ground at no water height must hold no water");
        That(WaterPooling.MatchHeight(0.002f, heights, 4) == 3, WaterF, "a surface between two close heights must match the NEAREST");

        // A flat square at 0 and a tilted one: only the flat one is water.
        var verts = new[]
        {
            new UnityEngine.Vector3(0, 0, 0), new UnityEngine.Vector3(1, 0, 0), new UnityEngine.Vector3(0, 0, 1), new UnityEngine.Vector3(1, 0, 1),
            new UnityEngine.Vector3(2, 0, 0), new UnityEngine.Vector3(3, 0.5f, 0), new UnityEngine.Vector3(2, 0, 1),
        };
        var tris = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5 };
        var found = new List<WaterPooling.FlatHeight>();
        WaterPooling.AddFlatTriangles(verts, tris, UnityEngine.Matrix4x4.identity, found);
        That(found.Count == 1 && Math.Abs(found[0].area - 1f) < 1e-4f, WaterF,
             "one flat unit square and one slope must give one water height of area 1, got " + found.Count);
        WaterPooling.KeepWaterHeights(found, 16);
        That(found.Count == 1, WaterF, "a water height of exactly the smallest area must be kept");

        // A sheet lies only on its own level ground. A strip 8 units long: level at 0.6 for its first 2 units, then a slope
        // climbing from 0.05 to 1.25 that passes through 0.6 at x = 4.75, the centre of a square. Only the level squares may hold the 0.6 sheet — the
        // slope's crossing is no level ground (it drew a ring of water round a hollow, 2026-09-28).
        {
            var sv = new List<UnityEngine.Vector3>();
            var st = new List<int>();
            void Quad(float x0, float y0, float x1, float y1)
            {
                int b0 = sv.Count;
                sv.Add(new UnityEngine.Vector3(x0, y0, 0)); sv.Add(new UnityEngine.Vector3(x1, y1, 0));
                sv.Add(new UnityEngine.Vector3(x0, y0, 1)); sv.Add(new UnityEngine.Vector3(x1, y1, 1));
                st.AddRange(new[] { b0, b0 + 2, b0 + 1, b0 + 1, b0 + 2, b0 + 3 });
            }
            Quad(0f, 0.6f, 2f, 0.6f);
            for (int s = 0; s < 6; s++) Quad(2f + s, 0.05f + 0.2f * s, 3f + s, 0.05f + 0.2f * (s + 1));
            var levelFound = new List<WaterPooling.FlatHeight>();
            WaterPooling.AddFlatTriangles(sv.ToArray(), st.ToArray(), UnityEngine.Matrix4x4.identity, levelFound);
            var sheets = WaterPooling.JoinFilms(levelFound);
            int lx = 16, lz = 2;
            var lgrid = new float[lx * lz];
            for (int k = 0; k < lgrid.Length; k++) lgrid[k] = float.NegativeInfinity;
            WaterPooling.RaiseToGround(sv.ToArray(), st.ToArray(), UnityEngine.Matrix4x4.identity, 0f, 0f, WaterPooling.RunoffCell, lx, lz, lgrid);
            var levelOf = new byte[lx * lz];
            WaterPooling.MarkLevelGround(sv.ToArray(), st.ToArray(), UnityEngine.Matrix4x4.identity, sheets, sheets.Count,
                                         lgrid, 0f, 0f, WaterPooling.RunoffCell, lx, lz, levelOf);
            int levelSquares = 0, slopeSquares = 0;
            for (int i = 0; i < lx; i++) { if (levelOf[i] == 0) continue; if (i < 4) levelSquares++; else slopeSquares++; }
            That(sheets.Count == 1 && levelSquares == 4 && slopeSquares == 0, WaterF,
                 "the 0.6 sheet must lie on its 4 level squares and none of the slope through 0.6, got " + levelSquares +
                 " level and " + slopeSquares + " on the slope");
        }

        // Smooth water: between two rain amounts each lake glides from where it stood at the lower one. A lake that
        // was there starts at its lower level; a lake that was not starts just under its own bottom (it grows from
        // nothing, never pops in at full depth).
        {
            var upA = new WaterPooling.Lake { bottom = 0f, level = 0.10f }; upA.cells.Add(0); upA.cells.Add(1);
            var upB = new WaterPooling.Lake { bottom = 0.3f, level = 0.35f }; upB.cells.Add(5);
            var lowA = new WaterPooling.Lake { bottom = 0f, level = 0.08f }; lowA.cells.Add(1);
            var bodyOf = new byte[8];
            bodyOf[0] = bodyOf[1] = 3; bodyOf[5] = 4;   // two sheets first: upper lakes are bodies 2 and 3 (+1 stored)
            var from = new float[8];
            WaterPooling.StartLevels(new List<WaterPooling.Lake> { lowA }, new List<WaterPooling.Lake> { upA, upB }, 2, bodyOf, 2, from);
            That(Math.Abs(from[2] - 0.08f) < 1e-6f, WaterF, "a lake that stood at the lower rain must glide from its lower level 0.08, got " + from[2]);
            That(from[3] < 0.3f, WaterF, "a lake that did not exist at the lower rain must start under its bottom 0.3, got " + from[3]);
        }

        // Runoff: a grid, low on the left half, a 1.5 cliff on the right. The foot of the cliff gets the most, far
        // flat ground gets none, and the cliff top (nothing above it) gets none.
        int nx = 20, nz = 3;
        float cell = WaterPooling.RunoffCell;
        var grid = new float[nx * nz];
        for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) grid[j * nx + i] = i >= 10 ? 1.5f : 0f;
        var run = new float[nx * nz];
        WaterPooling.Runoff(grid, nx, nz, cell, new[] { 0f, 1.5f }, 2, run);
        var runNoWater = new float[nx * nz];
        WaterPooling.Runoff(grid, nx, nz, cell, new[] { 1.5f }, 1, runNoWater);
        That(runNoWater[1 * nx + 9] == 0f, WaterF, "ground at no water height must receive no runoff, got " + runNoWater[1 * nx + 9]);
        float foot = run[1 * nx + 9], far = run[1 * nx + 0], top = run[1 * nx + 15];
        That(Math.Abs(foot - (1f - cell / WaterPooling.RunoffReach)) < 1e-4f, WaterF, "the square at a cliff's foot must get almost all the runoff, got " + foot);
        That(far == 0f, WaterF, "flat ground " + 10 * cell + " units from the cliff (beyond the reach) must get no runoff, got " + far);
        That(top == 0f, WaterF, "a cliff top with nothing above it must get no runoff, got " + top);
        That(Math.Abs(WaterPooling.SampleSmooth(run, nx, nz, 0f, 0f, cell, 9.5f * cell, 1.5f * cell) - foot) < 1e-5f, WaterF,
             "reading the runoff at a square's centre must give that square's own value");

        // Lakes fill from the rain their slopes collect. What the lakes hold must equal what fell on the sloping ground
        // (the map's edge keeps it in), a map with no slope must hold no lake, and two bowls whose water meets over the
        // ridge between them must become one lake.
        int bn = 21;
        var bowl = new float[bn * bn];
        for (int j = 0; j < bn; j++) for (int i = 0; i < bn; i++) bowl[j * bn + i] = 0.01f * ((i - 10) * (i - 10) + (j - 10) * (j - 10));
        var allShed = new bool[bn * bn];
        for (int k = 0; k < allShed.Length; k++) allShed[k] = true;
        float rain = 0.02f;
        var lakes = WaterPooling.Fill(WaterPooling.BuildWatershed(bowl, bn, bn, allShed), bowl, cell, rain);
        double held = 0;
        if (lakes.Count == 1) foreach (int c in lakes[0].cells) held += lakes[0].level - bowl[c];
        That(lakes.Count == 1 && Math.Abs(held - rain * bowl.Length) < 1e-3, WaterF,
             "a bowl must hold exactly the rain that fell on it: " + rain * bowl.Length + " squares of water, got " + held +
             " in " + lakes.Count + " lake(s)");
        var flat = new float[bn * bn];
        var noShed = new bool[bn * bn];
        That(WaterPooling.Fill(WaterPooling.BuildWatershed(flat, bn, bn, noShed), flat, cell, 0.04f).Count == 0, WaterF,
             "level ground everywhere (a town) must collect no lake");
        var twin = new float[bn * bn];
        for (int j = 0; j < bn; j++) for (int i = 0; i < bn; i++)
            twin[j * bn + i] = Math.Min(0.02f * ((i - 5) * (i - 5) + (j - 10) * (j - 10)), 0.02f * ((i - 15) * (i - 15) + (j - 10) * (j - 10)));
        var twinWs = WaterPooling.BuildWatershed(twin, bn, bn, allShed);
        That(WaterPooling.Fill(twinWs, twin, cell, 0.002f).Count == 2, WaterF, "two bowls with a little rain must be two lakes");
        That(WaterPooling.Fill(twinWs, twin, cell, 0.5f).Count == 1, WaterF, "two bowls whose water rises over the ridge must be one lake");
        // A small bowl under a big slope fills deeper than a big bowl under a small one: the left bowl here drains the
        // whole left side, the right one only a sliver.
        var lopsided = new float[bn * bn];
        for (int j = 0; j < bn; j++) for (int i = 0; i < bn; i++)
            lopsided[j * bn + i] = i < 16 ? 0.004f * (i - 3) * (i - 3) + 0.001f * (j - 10) * (j - 10) : 0.3f + 0.002f * (i - 18) * (i - 18) + 0.001f * (j - 10) * (j - 10);
        lakes = WaterPooling.Fill(WaterPooling.BuildWatershed(lopsided, bn, bn, allShed), lopsided, cell, 0.01f);
        float deepLeft = 0f, deepRight = 0f;
        foreach (var l in lakes) { int c0 = l.cells[0] % bn; if (c0 < 16) deepLeft = Math.Max(deepLeft, l.level - l.bottom); else deepRight = Math.Max(deepRight, l.level - l.bottom); }
        That(deepLeft > deepRight, WaterF, "the hollow under the bigger slope must stand deeper, got " + deepLeft + " against " + deepRight);

        // The user's three-step slope: a hollow high on a long slope, a small one just below it, the flat bottom at the
        // map's edge. The high one overflows; its spill runs down the slope INTO the small one below, which fills to its
        // rim though its own little slope could not fill it — and only then does water go on to the bottom.
        int sx = 40, sz = 3;
        var slope = new float[sx * sz];
        for (int j = 0; j < sz; j++)
            for (int i = 0; i < sx; i++)
                slope[j * sx + i] = 0.02f * i - 0.1f * Math.Max(0f, 1f - Math.Abs(i - 30) / 2.5f) - 0.1f * Math.Max(0f, 1f - Math.Abs(i - 24) / 2.5f);
        var slopeShed = new bool[sx * sz];
        for (int k = 0; k < slopeShed.Length; k++) slopeShed[k] = true;
        lakes = WaterPooling.Fill(WaterPooling.BuildWatershed(slope, sx, sz, slopeShed), slope, cell, 0.008f);
        WaterPooling.Lake middle = null;
        foreach (var l in lakes) if (Math.Abs(l.bottom - 0.38f) < 0.005f) middle = l;
        That(middle != null && middle.level > 0.4199f, WaterF,
             "the small hollow below an overflowing one must fill to its rim (0.42) with the water running down into it, got " +
             (middle != null ? middle.level.ToString() : "no lake"));

        // Rain adds a step every 30 seconds (each filling in over 3), up to the most one spell leaves; dry weather drains it.
        float step = WaterPooling.RainPerStep;
        That(WaterPooling.Rainfall(1000, 1f, 1000, 0f, 0, 0f) == 0f, WaterF, "no time, no rain");
        That(Math.Abs(WaterPooling.Rainfall(1003, 1f, 1000, 0f, 0, 0f) - step) < 1e-7f, WaterF, "the first step must be in after 3 seconds");
        That(Math.Abs(WaterPooling.Rainfall(1029, 1f, 1000, 0f, 0, 0f) - step) < 1e-7f, WaterF, "the rain must hold still between steps");
        That(Math.Abs(WaterPooling.Rainfall(1033, 1f, 1000, 0f, 0, 0f) - 2 * step) < 1e-7f, WaterF, "30 seconds on, two steps");
        That(Math.Abs(WaterPooling.Rainfall(1033, 0.5f, 1000, 0f, 0, 0f) - step) < 1e-7f, WaterF, "a half-strength rain must fall half as fast");
        That(WaterPooling.Rainfall(100000, 1f, 1000, 0f, 0, 0f) == WaterPooling.MostRain, WaterF, "a long rain must stop at the most");
        That(Math.Abs(WaterPooling.Rainfall(1000 + WaterPooling.DrainSeconds / 2, 0f, 1000, 1f, 0, 0f) - WaterPooling.MostRain / 2) < 1e-6f,
             WaterF, "after a long rain, half the drain time must leave half the water");
        That(WaterPooling.Rainfall(1000 + WaterPooling.DrainSeconds * 2, 0f, 1000, 1f, 0, 0f) == 0f, WaterF, "the water must drain away");
        That(Math.Abs(WaterPooling.Rainfall(5000, 0f, 1000, 0f, 0, 1f) - step) < 1e-7f, WaterF,
             "a place that always holds water must keep one step of it with no rain");
        // Level ground a few millimetres apart lies under one sheet.
        var flats = new List<WaterPooling.FlatHeight> { new WaterPooling.FlatHeight { y = 2.7f, top = 2.7f, area = 5f },
                                                        new WaterPooling.FlatHeight { y = 2.704f, top = 2.704f, area = 1f },
                                                        new WaterPooling.FlatHeight { y = 1.5f, top = 1.5f, area = 3f } };
        var films = WaterPooling.JoinFilms(flats);
        That(films.Count == 2 && films[0].y == 2.7f && films[0].top == 2.704f, WaterF, "levels 4 mm apart must share one sheet");
    }

    // ---- the creation hexagon spends exactly the starting points ---------------------------------------
    // A new character starts with the hexagon's stats and NONE left over; that is only true while every legal
    // spread costs exactly the starting grant. A change to the point-cost curve or the grant breaks it silently —
    // characters would be born with points to spare, or in debt.
    static void CreationSpreads()
    {
        int legal = 0;
        for (int str = 1; str <= 9; str++)
        for (int agi = 1; agi <= 9; agi++)
        for (int vit = 1; vit <= 9; vit++)
        {
            var s = new int[6];
            s[0] = str; s[1] = agi; s[2] = vit;
            s[UnitStatMath.CreationOpposite(0)] = 10 - str;
            s[UnitStatMath.CreationOpposite(1)] = 10 - agi;
            s[UnitStatMath.CreationOpposite(2)] = 10 - vit;
            if (!UnitStatMath.IsCreationSpread(s)) { Fail(StatMath, "hexagon spread " + string.Join(",", s) + " was refused"); return; }
            int cost = 0;
            foreach (var v in s) cost += UnitStatMath.PointsToRaise(v);
            if (cost != UnitStatMath.StartingStatPoints)
            { Fail(StatMath, "hexagon spread " + string.Join(",", s) + " costs " + cost + ", not the starting " + UnitStatMath.StartingStatPoints); return; }
            legal++;
        }
        That(legal == 729, StatMath, "expected 729 legal hexagon spreads, counted " + legal);
        That(!UnitStatMath.IsCreationSpread(new[] { 9, 9, 9, 9, 9, 9 }), StatMath, "a spread whose pairs add past 10 was accepted");
        That(!UnitStatMath.IsCreationSpread(new[] { 10, 5, 5, 0, 5, 5 }), StatMath, "a spread outside 1..9 was accepted");
        That(!UnitStatMath.IsCreationSpread(new[] { 5, 5, 5, 5, 5 }), StatMath, "a spread of five stats was accepted");
        That(!UnitStatMath.IsCreationSpread(null), StatMath, "no spread at all was accepted");
    }

    // WHO A KILL BELONGS TO: the points each person collects for one creature, and how its experience is split by
    // them. Pinned from docs/2-systems/loot-owner-pointing.md, agreed 2026-10-13.
    static void KillPointNumbers()
    {
        const string F = "unity/Assets/Scripts/KillLedger.cs";
        bool Near(float a, float b) => System.Math.Abs(a - b) < 0.001f;
        That(KillPointsMath.DealtPoints == 60f && KillPointsMath.HeldPoints == 15f && KillPointsMath.HealedPoints == 15f
             && KillPointsMath.FirstHitPoints == 5f && KillPointsMath.LastHitPoints == 5f, F,
             "the points are 60 damage, 15 holding its attention, 15 healing, 5 first hit, 5 last hit");
        // Alone, at range, never touched: 70 points exist and all of them are theirs, so the whole kill is.
        float solo = KillPointsMath.Points(500, 500, 0, 0, 0, 0, true, true);
        That(Near(solo, 70f), F, "a solo kill of a creature that never came after them is " + solo + " points, not 70");
        That(KillPointsMath.Share(1000, solo, solo) == 1000, F, "a solo kill no longer pays the whole of its experience");
        // The agreed example: three who dealt 30% each have 18; a healer with 10% of the damage and all the healing has 21.
        float dealer = KillPointsMath.Points(300, 1000, 0, 0, 0, 400, false, false);
        float healer = KillPointsMath.Points(100, 1000, 0, 0, 400, 400, false, false);
        That(Near(dealer, 18f) && Near(healer, 21f), F, "three dealers at 30% and a healer no longer score 18 and 21");
        // All 15 for holding its attention are shared by the time it spent after each person, hit or not.
        That(Near(KillPointsMath.Points(0, 500, 80, 80, 0, 0, false, false), 15f), F, "holding it the whole fight is not 15 points");
        That(Near(KillPointsMath.Points(0, 500, 20, 80, 0, 0, false, false), 3.75f), F, "holding it a quarter of the fight is not 3.75 points");
        That(KillPointsMath.Points(0, 0, 0, 0, 0, 0, false, false) == 0f, F, "somebody who did nothing has points");
        That(KillPointsMath.Share(10, 1f, 3f) == 3 && KillPointsMath.Share(10, 0f, 3f) == 0 && KillPointsMath.Share(0, 1f, 3f) == 0
             && KillPointsMath.Share(10, 3f, 3f) == 10, F, "a share is not the kill's worth x points / all points, rounded down");
        That(Near(KillPointsMath.KeptAfterRegain(100, 50), 0.5f) && KillPointsMath.KeptAfterRegain(100, 0) == 0f
             && KillPointsMath.KeptAfterRegain(0, 0) == 0f && Near(KillPointsMath.KeptAfterRegain(100, 100), 1f), F,
             "a creature that heals half its missing health back no longer halves every claim on it");
    }

    // WHAT A CREATURE'S MULTIPLIER PAYS: everything a kill is worth, times the multiplier, half up (decided 2026-10-20).
    static void CreaturePayNumbers()
    {
        const string F = "unity/Assets/Scripts/CreaturePay.cs";
        That(CreaturePay.Scaled(1000, 1f) == 1000 && CreaturePay.Scaled(1000, 30f) == 30000 && CreaturePay.Scaled(1000, 0f) == 1000, F,
             "x1 does not pay exactly what the level gives, or a multiplier below 1 pays less");
        That(CreaturePay.Scaled(3, 1.5f) == 5 && CreaturePay.Scaled(1, 1.4f) == 1 && CreaturePay.Scaled(0, 30f) == 0, F,
             "an amount x the multiplier is not rounded half up");
        That(Math.Abs(CreaturePay.ExpectedPieces(0.04f, 30f) - 1.2f) < 1e-5f && Math.Abs(CreaturePay.ExpectedPieces(0.01f, 3f) - 0.03f) < 1e-6f
             && CreaturePay.ExpectedPieces(0f, 90f) == 0f, F, "a chance x the multiplier is not how many fall on average");
        That(CreaturePay.PiecesFor(1.2f, 0.19f) == 2 && CreaturePay.PiecesFor(1.2f, 0.21f) == 1 && CreaturePay.PiecesFor(0.03f, 0.02f) == 1
             && CreaturePay.PiecesFor(0.03f, 0.04f) == 0 && CreaturePay.PiecesFor(0f, 0f) == 0, F,
             "an average of pieces is not rolled as the whole pieces and one more on what is left over");
        // What a kill pays: the file level's experience and the levels grown in the fight, together times the multiplier.
        int at50 = UnitStatMath.KillXp(50), at60 = UnitStatMath.KillXp(60);
        That(CreatureKinds.KillExperience(50, 50, 3f) == 3 * at50, F, "a x3 creature does not pay 3 times");
        That(CreatureKinds.KillExperience(60, 50, 3f) == 3 * at60, F,
             "levels a creature grew in the fight are not multiplied with the rest");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.NeverFights, GoldTier.Ordinary, 5, 3f) == 600, F, "a rescue from a x3 creature does not pay 3 times");
    }

    // The total health of a x1 creature of the reference build at its level's +0 metal armour — what the health curve
    // is checked against, worked out through the game's own functions.
    static int ReferenceCreatureHP(int level)
    {
        var eb = default(EquipBonuses);
        foreach (var slot in new[] { EquipSlotType.Armor, EquipSlotType.Boots, EquipSlotType.Garment, EquipSlotType.Head })
            eb.hp += CraftingMath.PieceNumbers(slot, WeaponType.None, MaterialKind.Metal, 0, CreatureLoot.WornGrade(level), 100, 0, 0).hp;
        return UnitStatMath.MaxHP(UnitStatMath.ReferenceBuild(level), eb, 0f);
    }

    // A CREATURE'S OWN MATERIAL: the tier its grade gives it, what a tier sells for, how often it falls and in how many
    // piles. Pinned from docs/3-world/creature-materials.md, agreed 2026-10-16.
    static void CreatureMaterialNumbers()
    {
        const string F = "unity/Assets/Scripts/CreatureMaterials.cs";
        That(CreatureMaterials.SellValue(1, Rarity.Common) == 20 && CreatureMaterials.SellValue(99, Rarity.Common) == 1980, F,
             "an ordinary material sells for 20 x its creature's level");
        That(CreatureMaterials.SellValue(40, Rarity.Uncommon) == 4000 && CreatureMaterials.SellValue(40, Rarity.Rare) == 20000
             && CreatureMaterials.SellValue(40, Rarity.Epic) == 80000 && CreatureMaterials.SellValue(40, Rarity.Legendary) == 400000, F,
             "a rarer material sells for 100, 500, 2,000 or 10,000 x level by its tier");
        That(CreatureMaterials.SellValue(0, Rarity.Common) == 20, F, "a creature with no level still sells as level 1");
        That(CreatureMaterials.ExpectedPieces(CreatureKind.Normal, Rarity.Common, 1f) == 0.5f && CreatureMaterials.ExpectedPieces(CreatureKind.Normal, Rarity.Uncommon, 1f) == 0.1f
             && CreatureMaterials.ExpectedPieces(CreatureKind.Normal, Rarity.Rare, 1f) == 0.02f && CreatureMaterials.ExpectedPieces(CreatureKind.Normal, Rarity.Epic, 1f) == 0.005f
             && CreatureMaterials.ExpectedPieces(CreatureKind.Normal, Rarity.Legendary, 1f) == 0.001f, F,
             "a x1 creature drops its tiers at 1 in 2, 10, 50, 200 and 1,000");
        foreach (Rarity tier in System.Enum.GetValues(typeof(Rarity)))
            That(CreatureMaterials.ExpectedPieces(CreatureKind.Shadow, tier, 120f) == 0f, F, "a shadow dropped a " + tier + " material of its own");
        // The rule the whole table rests on: a kill's material is worth 10 gold x level x its multiplier on average at
        // the tier its multiplier gives, whatever the kind, so rarity changes how the value arrives and never how much.
        foreach (var kind in new[] { CreatureKind.Normal, CreatureKind.Elite })
            foreach (float m in new[] { 1f, 2.5f, 3f, 9f, 10f, 29f, 30f, 89f, 90f, 150f })
            {
                Rarity tier;
                That(CreatureMaterials.TierOfMultiplier(kind, m, out tier), F, "a x" + m + " " + kind + " has no material tier");
                That(System.Math.Abs(CreatureMaterials.ExpectedPieces(kind, tier, m) * CreatureMaterials.SellValue(40, tier) - 400.0 * m) < 0.05 * m, F,
                     "a x" + m + " " + kind + "'s " + tier + " material is no longer worth 10 gold x level x " + m + " a kill");
            }
        // THE MULTIPLIER DECIDES THE TIER: x1 Common, x3 Uncommon, x10 Rare, x30 Epic, x90 Legendary; a shadow none.
        Rarity got;
        float[] tierFrom = { 1f, 3f, 10f, 30f, 90f };
        for (int t = 0; t < tierFrom.Length; t++)
        {
            That(CreatureMaterials.TierOfMultiplier(CreatureKind.Normal, tierFrom[t], out got) && got == (Rarity)t, F,
                 "a x" + tierFrom[t] + " creature's material should be " + (Rarity)t + ", not " + got);
            if (t > 0)
                That(CreatureMaterials.TierOfMultiplier(CreatureKind.Normal, tierFrom[t] - 0.01f, out got) && got == (Rarity)(t - 1), F,
                     "just under x" + tierFrom[t] + " should still be " + (Rarity)(t - 1));
        }
        That(!CreatureMaterials.TierOfMultiplier(CreatureKind.Shadow, 120f, out got), F, "a shadow has a material tier");
        for (int step = 1; step <= 5; step++)
            That(Rarities.OfMaterial(MaterialKind.Creature, step) == (Rarity)(step - 1)
                 && CreatureMaterials.StepOfTier((Rarity)(step - 1)) == step, RarityCheckFile, "a creature material's step " + step + " is not its tier");
    }
    const string RarityCheckFile = "unity/Assets/Scripts/Rarity.cs";

    // SAVED CHARACTERS: the gold a rescue pays, the drops a rebel gives up, the side each behaviour stands on, and the
    // one question every targeting path asks. Pinned from docs/3-world/saved-characters.md, agreed 2026-09-30.
    static void SavedCharacterNumbers()
    {
        const string F = "unity/Assets/Scripts/SavedCharacterRules.cs";
        const string S = "unity/Assets/Scripts/Side.cs";
        That(SavedCharacterRules.TransformChance == 0.15f, F, "a beaten normal or elite turns back 15% of the time");
        // Decided 2026-10-13: gold worth 4, 8 or 12 kills of the creature (10 gold a level) plus 3, 6 or 9 of its
        // ordinary material (two kills each), so a rescue is worth 10, 20 or 30 kills in all.
        That(SavedCharacterRules.RescueGold(SavedBehaviour.NeverFights, GoldTier.Ordinary, 5, 1f) == 200, F, "an ordinary rescue pays gold worth 4 kills");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.NeverFights, GoldTier.Helpless, 5, 1f) == 400, F, "a helpless rescue pays gold worth 8 kills");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.NeverFights, GoldTier.Wealthy, 5, 1f) == 600, F, "a wealthy rescue pays gold worth 12 kills");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.NeverFights, GoldTier.Ordinary, 0, 1f) == 40, F, "a creature with no level pays as level 1");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.Ally, GoldTier.Wealthy, 5, 1f) == 0, F, "an ally pays no gold");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.Rebel, GoldTier.Ordinary, 5, 1f) == 0, F, "a rebel pays no gold");
        That(SavedCharacterRules.RescueMaterials(SavedBehaviour.NeverFights, GoldTier.Ordinary) == 3
             && SavedCharacterRules.RescueMaterials(SavedBehaviour.NeverFights, GoldTier.Helpless) == 6
             && SavedCharacterRules.RescueMaterials(SavedBehaviour.NeverFights, GoldTier.Wealthy) == 9, F,
             "a rescue hands over 3, 6 or 9 of the creature's material");
        // In the material's REAL tier: whole pieces plus a chance of one more, so the worth is the same at every tier.
        That(SavedCharacterRules.RescuePieces(SavedBehaviour.NeverFights, GoldTier.Wealthy, Rarity.Common, 0) == 9
             && SavedCharacterRules.RescuePieces(SavedBehaviour.NeverFights, GoldTier.Wealthy, Rarity.Common, 9999) == 9, F,
             "a Wealthy rescue of a Common material is not 9 pieces");
        That(SavedCharacterRules.RescuePieces(SavedBehaviour.NeverFights, GoldTier.Wealthy, Rarity.Uncommon, 7999) == 2
             && SavedCharacterRules.RescuePieces(SavedBehaviour.NeverFights, GoldTier.Wealthy, Rarity.Uncommon, 8000) == 1, F,
             "a Wealthy rescue of an Uncommon is not 1 + an 80% chance of a 2nd");
        That(SavedCharacterRules.RescuePieces(SavedBehaviour.NeverFights, GoldTier.Ordinary, Rarity.Rare, 1199) == 1
             && SavedCharacterRules.RescuePieces(SavedBehaviour.NeverFights, GoldTier.Ordinary, Rarity.Rare, 1200) == 0, F,
             "an Ordinary rescue of a Rare is not a 12% chance of one");
        That(SavedCharacterRules.RescuePieces(SavedBehaviour.NeverFights, GoldTier.Wealthy, Rarity.Legendary, 179) == 1
             && SavedCharacterRules.RescuePieces(SavedBehaviour.NeverFights, GoldTier.Wealthy, Rarity.Legendary, 180) == 0, F,
             "a Wealthy rescue of a Legendary is not a 1.8% chance of one");
        That(SavedCharacterRules.RescuePieces(SavedBehaviour.Rebel, GoldTier.Wealthy, Rarity.Common, 0) == 0, F, "a rebel hands over material");
        That(SavedCharacterRules.RescueMaterials(SavedBehaviour.Rebel, GoldTier.Wealthy) == 0
             && SavedCharacterRules.RescueMaterials(SavedBehaviour.Ally, GoldTier.Wealthy) == 0, F, "an ally or a rebel hands over material");
        foreach (GoldTier tier in new[] { GoldTier.Ordinary, GoldTier.Helpless, GoldTier.Wealthy })
            That(SavedCharacterRules.RescueKills(tier) + 2 * SavedCharacterRules.RescueMaterials(SavedBehaviour.NeverFights, tier)
                 == 10 * (tier == GoldTier.Ordinary ? 1 : tier == GoldTier.Helpless ? 2 : 3), F,
                 "a " + tier + " rescue is no longer worth 10, 20 or 30 kills in all");
        That(SavedCharacterRules.FightingMultiplier(SavedBehaviour.Ally, 1f) == 2f && SavedCharacterRules.FightingMultiplier(SavedBehaviour.Ally, 5f) == 10f
             && SavedCharacterRules.FightingMultiplier(SavedBehaviour.Rebel, 5f) == 5f && SavedCharacterRules.FightingMultiplier(SavedBehaviour.NeverFights, 3f) == 3f, F,
             "an ally should fight at twice its creature's multiplier, every other character at its creature's own");
        That(SavedCharacterRules.KillsWorth(SavedBehaviour.Rebel) == 3 && SavedCharacterRules.KillsWorth(SavedBehaviour.Ally) == 1
             && SavedCharacterRules.KillsWorth(SavedBehaviour.NeverFights) == 1, F, "a rebel's death should pay 3 kills of its creature");
        That(!SavedCharacterRules.CanTurnBack(CreatureKind.Shadow), F, "a shadow never turns back");
        That(SavedCharacterRules.StaySeconds(SavedBehaviour.NeverFights) == 30f && SavedCharacterRules.StaySeconds(SavedBehaviour.Ally) == 30f
             && SavedCharacterRules.StaySeconds(SavedBehaviour.Rebel) == 30f, F, "every saved character stays 30s");
        That(SavedCharacterRules.DrawnWidth == 0.5f, F, "a saved character is drawn at half its picture's width");
        That(SavedCharacterRules.AppearAuraSeconds == 2f && SavedCharacterRules.AppearAuraScale == 0.5f, F, "the appear aura plays 2s at half size before it fades");
        That(SavedCharacterRules.SideOf(SavedBehaviour.Ally) == Side.Players && SavedCharacterRules.SideOf(SavedBehaviour.Rebel) == Side.Creatures
             && SavedCharacterRules.SideOf(SavedBehaviour.NeverFights) == Side.None, F, "allies stand with the players, rebels with the creatures, the rest with nobody");
        That(Sides.Hostile(Side.Players, Side.Creatures) && !Sides.Hostile(Side.Players, Side.Players)
             && !Sides.Hostile(Side.None, Side.Creatures) && !Sides.Hostile(Side.Players, Side.None), S, "only the two opposite sides fight; nobody fights one who stands with nobody");
    }

    // ---- quest steps: judged on what the character holds (decided 2026-10-04) ----------------------------------
    // A wrong answer here is silent: a step that never finishes looks like a player who has not done it yet, and one
    // that finishes too early skips a lesson nobody notices was skipped.
    const string QuestF = "unity/Assets/Scripts/QuestMath.cs";
    const string RoutesF = "unity/Assets/Scripts/WorldRoutes.cs";

    static void QuestSteps()
    {
        var bag = new List<QuestHeld> { new QuestHeld { itemId = "item.sword", folds = 1, quality = Quality.Raw } };
        That(QuestMath.Met(QuestObjectiveKind.Have, "item.sword", 1, bag), QuestF, "a sword in the bag is 'have a sword'");
        That(!QuestMath.Met(QuestObjectiveKind.FoldedTo, "item.sword", 2, bag), QuestF, "one fold is not 'folded twice'");
        That(!QuestMath.Met(QuestObjectiveKind.Wear, "item.sword", 1, bag), QuestF, "a sword in the bag is not a sword worn");
        var worn = new List<QuestHeld> { new QuestHeld { itemId = "item.sword", folds = 0, quality = Quality.F, worn = true } };
        That(QuestMath.Met(QuestObjectiveKind.Have, "item.sword", 1, worn), QuestF, "a worn sword still counts as 'have a sword'");
        That(QuestMath.Met(QuestObjectiveKind.FoldedTo, "item.sword", 2, worn), QuestF, "a sword above Raw counts as folded (a quench spends its folds)");
        That(QuestMath.Met(QuestObjectiveKind.Wear, "item.sword", 1, worn), QuestF, "a worn sword is a sword worn");
        That(!QuestMath.Met(QuestObjectiveKind.TalkTo, "item.sword", 1, bag), QuestF, "a talk step is never met by holding something");
        That(QuestMath.CanPay(8, 8, 800, 800) && !QuestMath.CanPay(7, 8, 800, 800) && !QuestMath.CanPay(8, 8, 799, 800),
             QuestF, "a bill is paid only when both the material and the gold cover it");
        // The first tutorial's bill: three Raw pieces folded five times each, at 1 Temper Dust and 42 gold a fold.
        int dust = 0, gold = 0;
        QuestMath.AddForgedPiece(5, CraftingMath.FoldMaterialCost(Quality.Raw), CraftingMath.FoldGoldCost(Quality.Raw), ref dust, ref gold);
        That(dust == 5 && gold == 210, QuestF, "a piece folded five times from Raw costs 5 Temper Dust and 210 gold");
        QuestMath.AddForgedPiece(5, CraftingMath.FoldMaterialCost(Quality.Raw), CraftingMath.FoldGoldCost(Quality.Raw), ref dust, ref gold);
        QuestMath.AddForgedPiece(5, CraftingMath.FoldMaterialCost(Quality.Raw), CraftingMath.FoldGoldCost(Quality.Raw), ref dust, ref gold);
        That(dust == 15 && gold == 630, QuestF, "three pieces folded five times each from Raw cost 15 Temper Dust and 630 gold");
        // A Health step: 80% of 76 is met at 61 and not at 60; from 7 health, potions healing 30 each take two to get there.
        That(QuestMath.HealthMet(61, 76, 80) && !QuestMath.HealthMet(60, 76, 80), QuestF, "80% of 76 health is 61");
        That(QuestMath.HealthNeeded(76, 80) == 61, QuestF, "the least health meeting 80% of 76 is 61");
        That(QuestMath.UsesToReach(7, 76, 80, 30) == 2 && QuestMath.UsesToReach(61, 76, 80, 30) == 0
             && QuestMath.UsesToReach(7, 76, 80, 0) == 0, QuestF,
             "from 7 of 76 health, 30-health potions take 2 to reach 80%; none when already there or when they heal nothing");
        That(QuestMath.BuyBill(80, 3, 1, 1) == 80 && QuestMath.BuyBill(80, 1, 0, 2) == 0, QuestF,
             "buying 3 at 80 with 1 bought and 1 free costs 80; more free than asked costs nothing");
        That(QuestMath.BuyNeed(1, 0, 3) == 3 && QuestMath.BuyNeed(1, 2, 1) == 3 && QuestMath.BuyNeed(1, 3, 0) == 3
             && QuestMath.BuyNeed(2, 0, 0) == 2, QuestF,
             "a step asking for 1 with 3 free needs all 3 bought; with nothing free it needs what it asks for");
        That(QuestMath.HealOfOneUse(new List<float> { 30f }) == 30 && QuestMath.HealOfOneUse(new List<float> { 12.5f, 12.5f, 0.5f }) == 26
             && QuestMath.HealOfOneUse(new List<float>()) == 0, QuestF, "one use heals the sum of its heals, rounded half up");
        int c2 = 0, g2 = 0;
        QuestMath.AddForgedPiece(1, 1, 50, ref c2, ref g2);
        That(c2 == 1 && g2 == 50, QuestF, "a piece already folded owes only the folds still missing");

        var doors = new Dictionary<string, List<string>>
        {
            ["a"] = new List<string> { "b", "c" }, ["b"] = new List<string> { "a", "d" },
            ["c"] = new List<string> { "a" }, ["d"] = new List<string> { "b" },
        };
        That(WorldRoutes.NextFrom(doors, "a", "d", out int n) == "b" && n == 2, RoutesF, "a to d goes through b, two doors");
        That(WorldRoutes.NextFrom(doors, "a", "a", out n) == "a" && n == 0, RoutesF, "the same map is no doors away");
        That(WorldRoutes.NextFrom(doors, "a", "z", out n) == null && n == -1, RoutesF, "a map with no way to it has no next map");
    }

    // ---- where a walk cycle's feet land, and how fast it walks -----------------------------------------------------
    // Pinned on the REAL ankle paths of the Run2 clip on the Casual2_Female body at its in-game height (0.327), recorded
    // 2026-10-05 — the clip whose hand-typed steps were L7 R17. A wrong landing rule moves the footstep sounds off the
    // feet; a wrong speed makes every planted foot slide, and neither shows up anywhere but in play.
    const string StrideF = "unity/Assets/Scripts/StrideMath.cs";
    // ---- shops: what a sale pays, and what getting a sold thing back costs --------------------------------------
    const string ShopF = "unity/Assets/Scripts/ShopMath.cs";

    static void ShopNumbers()
    {
        That(ShopMath.TaxEach(75) == 25 && ShopMath.TaxEach(76) == 26 && ShopMath.TaxEach(1) == 1 && ShopMath.TaxEach(0) == 0,
             ShopF, "the Tax on one is a third of its value, rounded up: 75 -> 25, 76 -> 26, 1 -> 1, nothing on 0");
        That(ShopMath.SalePay(75, 3) == 150, ShopF, "3 sold worth 75 each must pay 150 (75 less 25 Tax, three times)");
        That(ShopMath.BuyCost(168, 4) == 896, ShopF, "4 bought worth 168 each must cost 896 (168 and 56 Tax, four times)");
        That(ShopMath.TaxOn(168, 4) == 224, ShopF, "the Tax on 4 worth 168 must be 224 — per unit, never on the total");
        That(ShopMath.SalePay(int.MaxValue, 2) > int.MaxValue, ShopF, "a sale bigger than 2,147,483,647 must pay in full, not stop or wrap");
        That(ShopMath.SalePay(5, 0) == 0, ShopF, "selling none must pay nothing");
        bool everySellPriceKept = true;
        for (int z = 0; z <= 20000; z++)
            if (ShopMath.SalePay(ShopMath.ValueForSellPrice(z), 1) != z) { everySellPriceKept = false; break; }
        That(everySellPriceKept, ShopF, "every old sell price 0..20,000 must still be exactly what one sells for after the move to one value");
        That(ShopMath.ValueForSellPrice(25) == 38 && ShopMath.ValueForSellPrice(112) == 168, ShopF,
             "the move to one value: sell price 25 -> worth 38, 112 -> 168");
        That(ShopMath.BuybackPrice(112, 1) == 150, ShopF, "a Healing Potion sold for 112 must cost 150 back (112 and 38 Tax)");
        That(ShopMath.BuybackPrice(1120, 10) == 1500, ShopF, "ten sold for 112 each must cost 1,500 back — the Tax per unit, never on the total");
        That(ShopMath.BuybackPrice(1, 1) == 2, ShopF, "what paid 1 must cost 2 back — never free storage");
        That(ShopMath.BuybackPrice(0, 1) == 1, ShopF, "even a thing that paid nothing must cost one back");
        That(ShopMath.BuybackPrice(int.MaxValue, 1) > int.MaxValue, ShopF, "the price of the largest sale must stay above what it paid, not wrap");
        That(ShopMath.BuybackEndsAt(1000) == 4600, ShopF, "a thing sold at 1000 must be kept until 4600");
        That(ShopMath.MinutesShown(3600) == 60, ShopF, "a full hour must read 60 min");
        That(ShopMath.MinutesShown(61) == 2, ShopF, "61 seconds must read 2 min");
        That(ShopMath.MinutesShown(1) == 1, ShopF, "the last second must still read 1 min");
        That(ShopMath.MinutesShown(0) == 0, ShopF, "nothing left must read 0");
        That(ShopMath.SecondsUntilMinuteChanges(61) == 1, ShopF, "at 61 s the shown 2 min must change in 1 s");
        That(ShopMath.SecondsUntilMinuteChanges(120) == 60, ShopF, "at 120 s the shown 2 min must change in 60 s");
        That(ShopMath.SecondsUntilMinuteChanges(30) == 30, ShopF, "at 30 s the last minute must end in 30 s");
    }

    // ---- world events: what a kill pays under an experience rate, and how a GM's duration is read -------------
    const string EventF = "unity/Assets/Scripts/WorldEventMath.cs";

    static void WorldEventNumbers()
    {
        That(WorldEventMath.Multiplied(40, 1.5) == 60, EventF, "40 experience at ×1.5 must pay 60");
        That(WorldEventMath.Multiplied(5, 1.5) == 8, EventF, "5 at ×1.5 is 7.5, which must round UP to 8");
        That(WorldEventMath.Multiplied(4, 0.1) == 0, EventF, "4 at ×0.1 is 0.4, which must pay nothing");
        That(WorldEventMath.Multiplied(int.MaxValue, 3) == int.MaxValue, EventF, "a huge rate must stop at the largest count, not wrap negative");
        That(WorldEventMath.Multiplied(40, 0) == 0, EventF, "a rate of 0 must pay nothing");

        That(WorldEventMath.TryParseDuration("2h", out var s) && s == 7200, EventF, "'2h' must be 7200 seconds");
        That(WorldEventMath.TryParseDuration("30m", out s) && s == 1800, EventF, "'30m' must be 1800 seconds");
        That(WorldEventMath.TryParseDuration("1.5d", out s) && s == 129600, EventF, "'1.5d' must be 129600 seconds");
        That(WorldEventMath.TryParseDuration("90s", out s) && s == 90, EventF, "'90s' must be 90 seconds");
        That(!WorldEventMath.TryParseDuration("90", out _), EventF, "a duration with no unit must be refused");
        That(!WorldEventMath.TryParseDuration("0h", out _), EventF, "a zero duration must be refused");
        That(!WorldEventMath.TryParseDuration("-1h", out _), EventF, "a negative duration must be refused");

        That(WorldEventMath.DurationText(7200) == "2 h", EventF, "7200 s must read '2 h'");
        That(WorldEventMath.DurationText(4800) == "1 h 20 min", EventF, "4800 s must read '1 h 20 min'");
        That(WorldEventMath.DurationText(2730) == "45 min 30 s", EventF, "2730 s must read '45 min 30 s'");
        That(WorldEventMath.MultiplierText(1.5) == "×1.5", EventF, "1.5 must read '×1.5'");
    }

    static void StrideRules()
    {
        float[] lF = { -.0240f, -.0145f, -.0007f, .0173f, .0383f, .0556f, .0612f, .0477f, .0280f, .0092f, -.0094f,
                       -.0270f, -.0446f, -.0576f, -.0680f, -.0771f, -.0813f, -.0777f, -.0675f, -.0533f, -.0363f };
        float[] lH = { .0827f, .0714f, .0548f, .0390f, .0263f, .0173f, .0106f, .0017f, .0009f, .0020f, .0021f,
                       .0018f, .0046f, .0137f, .0276f, .0436f, .0614f, .0784f, .0887f, .0923f, .0907f };
        float[] rF = { -.0228f, -.0421f, -.0599f, -.0745f, -.0855f, -.0918f, -.0923f, -.0876f, -.0778f, -.0664f, -.0552f,
                       -.0438f, -.0278f, -.0050f, .0184f, .0397f, .0542f, .0499f, .0347f, .0157f, -.0037f };
        float[] rH = { .0013f, .0023f, .0070f, .0152f, .0267f, .0412f, .0578f, .0722f, .0796f, .0799f, .0724f,
                       .0588f, .0414f, .0263f, .0162f, .0112f, .0093f, .0043f, .0015f, .0017f, .0019f };
        const float height = 0.327f;
        var plants = new List<FootPlant>();
        StrideMath.FindPlants(lF, lH, false, 0.05f * height, plants);
        StrideMath.FindPlants(rF, rH, true, 0.05f * height, plants);
        That(plants.Count == 2 && !plants[0].right && plants[0].frame == 7 && plants[1].right && plants[1].frame == 17, StrideF,
             "Run2 should land L7 and R17 (the frames typed by hand), got " + plants.Count + " plant(s)");
        float left = StrideMath.ContactSpeed(lF, lH, 7, 0.02f * height, 30f);
        float right = StrideMath.ContactSpeed(rF, rH, 17, 0.02f * height, 30f);
        That(Math.Abs(left - 0.5538f) < 0.001f, StrideF, "Run2's left foot should slide back at 0.554 a second, got " + left);
        That(Math.Abs(right - 0.549f) < 0.001f, StrideF, "Run2's right foot should slide back at 0.549 a second (across the loop), got " + right);
        That(Math.Abs(StrideMath.LockedPlayback(1.2f, 0.6f) - 2f) < 0.0001f, StrideF, "1.2 a second on a 0.6 clip should play at 2x");
        That(StrideMath.LockedPlayback(1.2f, 0f) == 1f, StrideF, "an unmeasured clip should play at 1x, never freeze");
    }

    // ---- when a swing connects: one hit, or a pair weapon's two (decided 2026-10-09) ------------------------
    // The server lands a person's blows and a creature's from this one answer, so a wrong number here silently
    // changes every Katar and Axe hit in the game (and every other weapon if the count went wrong).
    const string SwingF = "unity/Assets/Scripts/WeaponType.cs";
    static void SwingContactRules()
    {
        foreach (WeaponType w in Enum.GetValues(typeof(WeaponType)))
        {
            int n = SwingContacts.Of(w, 0.7f, 0.9f, 0.5f, out float first, out float second, out float power);
            bool pair = WeaponTypes.IsDualWield(w);
            That(n == (pair ? 2 : 1), SwingF, w + " should land " + (pair ? 2 : 1) + " hit(s) per swing, lands " + n);
            That(Math.Abs(first - 0.7f) < 1e-6f, SwingF, w + "'s first hit should land at 0.7 of the swing, lands at " + first);
        }
        That(WeaponTypes.IsDualWield(WeaponType.Katar) && WeaponTypes.IsDualWield(WeaponType.Axe), SwingF,
             "the katar and the axe are the pair weapons (user, 2026-10-09)");
        SwingContacts.Of(WeaponType.Katar, 0.7f, 0.9f, 0.5f, out _, out float at, out float pw);
        That(Math.Abs(at - 0.9f) < 1e-6f && Math.Abs(pw - 0.5f) < 1e-6f, SwingF, "the other hand should land at 0.9 with half power, got " + at + " / " + pw);
        SwingContacts.Of(WeaponType.Axe, 0.7f, 0.5f, 0.5f, out float f2, out float early, out _);
        That(early > f2, SwingF, "the other hand's hit can never land before the first, landed at " + early + " vs " + f2);
        SwingContacts.Of(WeaponType.Axe, 0.7f, 1.4f, 2f, out _, out float late, out float strong);
        That(late <= 1f && strong <= 1f, SwingF, "the other hand's hit can never land past the end of the swing or above full power, got " + late + " / " + strong);
    }

    const string DrawF = "unity/Assets/Scripts/WeaponDrawMath.cs";
    static void WeaponDrawRules()
    {
        // Agreed with the user 2026-10-09: draw time from weight, 0.2 s + 0.006 s per point.
        That(Math.Abs(WeaponDrawMath.DrawSeconds(90) - 0.74f) < 1e-5f, DrawF, "a weight-90 weapon should draw in 0.74 s, draws in " + WeaponDrawMath.DrawSeconds(90));
        That(Math.Abs(WeaponDrawMath.DrawSeconds(60) - 0.56f) < 1e-5f, DrawF, "a weight-60 weapon should draw in 0.56 s, draws in " + WeaponDrawMath.DrawSeconds(60));
        That(Math.Abs(WeaponDrawMath.DrawSeconds(0) - 0.2f) < 1e-6f && Math.Abs(WeaponDrawMath.DrawSeconds(-5) - 0.2f) < 1e-6f, DrawF,
             "empty hands still take the 0.2 s floor, got " + WeaponDrawMath.DrawSeconds(0));
        // The greatsword weighs 133 so that it takes 1 s either way (the user, 2026-10-10).
        That(Math.Abs(WeaponDrawMath.DrawSeconds(133) - 0.998f) < 1e-5f, DrawF, "a weight-133 weapon should draw in 0.998 s, draws in " + WeaponDrawMath.DrawSeconds(133));
        // A weapon spun during its put-away (2026-10-10): nothing outside the window, half its turning at the middle,
        // all of it at the end; off the fist only inside the window, fully up through its middle.
        That(WeaponDrawMath.SpinWindowShare(0.1f, 0.2f, 0.6f) == 0f && WeaponDrawMath.SpinWindowShare(0.6f, 0.2f, 0.6f) == 0f
             && WeaponDrawMath.SpinWindowShare(0.5f, 0.6f, 0.2f) == 0f, DrawF, "a spin shows nothing before its window, at its end, or for a window with no length");
        That(Math.Abs(WeaponDrawMath.SpinWindowShare(0.4f, 0.2f, 0.6f) - 0.5f) < 1e-5f, DrawF,
             "halfway between 0.2 and 0.6 is half the spin window, got " + WeaponDrawMath.SpinWindowShare(0.4f, 0.2f, 0.6f));
        That(WeaponDrawMath.SpinTurnShare(0f) == 0f && Math.Abs(WeaponDrawMath.SpinTurnShare(0.5f) - 0.5f) < 1e-6f && Math.Abs(WeaponDrawMath.SpinTurnShare(1f) - 1f) < 1e-6f, DrawF,
             "a spin has turned nothing at its start, half at its middle and everything at its end");
        That(WeaponDrawMath.SpinLiftShare(0f) == 0f && Math.Abs(WeaponDrawMath.SpinLiftShare(0.5f) - 1f) < 1e-6f && Math.Abs(WeaponDrawMath.SpinLiftShare(1f)) < 1e-5f
             && Math.Abs(WeaponDrawMath.SpinLiftShare(0.1f) - 0.5f) < 1e-5f, DrawF,
             "a spun weapon is on the fist at both ends of the window, half lifted a tenth in, fully lifted at the middle");
        // A glide of 10 units at 20 u/s with 0.2 s of easing lasts 0.7 s, 1.2 s with the slack; a broken one is capped.
        That(Math.Abs(WeaponDrawMath.OrderedMoveSeconds(10f, 20f, 0.2f, 5f) - 1.2f) < 1e-5f, DrawF,
             "a 10-unit glide at 20 u/s with 0.2 s easing should be exempt for 1.2 s, got " + WeaponDrawMath.OrderedMoveSeconds(10f, 20f, 0.2f, 5f));
        That(Math.Abs(WeaponDrawMath.OrderedMoveSeconds(1000f, 1f, 0f, 5f) - 5.5f) < 1e-5f, DrawF,
             "an ordered move is never exempt past the longest hop plus slack, got " + WeaponDrawMath.OrderedMoveSeconds(1000f, 1f, 0f, 5f));
        // The quiet spell: 5 s since the last blow, and only with the weapon on the body.
        That(!WeaponDrawMath.PeaceBonusesOn(true, 104.9f, 100f), DrawF, "4.9 s after a blow is still not quiet");
        That(WeaponDrawMath.PeaceBonusesOn(true, 105f, 100f), DrawF, "5 s after a blow the peace bonuses pay");
        That(!WeaponDrawMath.PeaceBonusesOn(false, 200f, 100f), DrawF, "a drawn weapon never earns the peace bonuses");
        That(Math.Abs(WeaponDrawMath.NaturalRegen(3f, true) - 6f) < 1e-6f && Math.Abs(WeaponDrawMath.NaturalRegen(3f, false) - 3f) < 1e-6f, DrawF,
             "peace doubles natural recovery and nothing else");
        That(Math.Abs(WeaponDrawMath.RegenPerSecond(3f, true, 2f) - 8f) < 1e-6f, DrawF,
             "peace doubles the natural 3/s to 6/s and leaves a timed +2/s alone: 8/s, got " + WeaponDrawMath.RegenPerSecond(3f, true, 2f));
        That(Math.Abs(WeaponDrawMath.NoticedSqr(4f, true) - 16f) < 1e-5f && Math.Abs(WeaponDrawMath.NoticedSqr(4f, false) - 4f) < 1e-6f, DrawF,
             "somebody at peace 2 units away is weighed as 4 away (16 squared), got " + WeaponDrawMath.NoticedSqr(4f, true));
        That(Math.Abs(WeaponDrawMath.NoticeRadius(6f, true) - 3f) < 1e-6f && Math.Abs(WeaponDrawMath.NoticeRadius(6f, false) - 6f) < 1e-6f, DrawF,
             "peace halves the distance a creature notices you from (6 becomes 3)");
        // The draw buff: at most once per 15 s, and longer than any draw buff lasts.
        That(WeaponDrawMath.DrawBuffDue(1f, 0f, true), DrawF, "the first draw ever grants its buff");
        That(!WeaponDrawMath.DrawBuffDue(14.9f, 0f, false) && WeaponDrawMath.DrawBuffDue(15f, 0f, false), DrawF, "the next draw buff waits 15 s");

        // The state: a draw runs its window, a request made mid-move is carried out after it, a reset is instant.
        const string StateF = "unity/Assets/Scripts/WeaponDraw.cs";
        var d = new WeaponDraw();
        d.RequestDraw(0.74f, 10f);
        That(d.Phase == DrawPhase.Drawing, StateF, "a draw from the body starts drawing, is " + d.Phase);
        That(!d.Tick(10.7f) && d.Phase == DrawPhase.Drawing, StateF, "0.7 s into a 0.74 s draw is still drawing");
        d.RequestSheathe(0.74f, 10.72f);
        That(d.Tick(10.75f), StateF, "the draw finishing is the 'weapon drawn' moment");
        That(d.Phase == DrawPhase.Sheathing, StateF, "a sheathe asked for mid-draw starts the moment the draw ends, is " + d.Phase);
        int v = d.Version;
        d.Reset(11f);
        That(d.Phase == DrawPhase.Sheathed && d.Version > v, StateF, "a reset puts the weapon away at once and is announced");
        d.RequestSheathe(0.74f, 12f);
        That(d.Phase == DrawPhase.Sheathed, StateF, "putting away a weapon already away changes nothing");
    }

    // ---- the kill combo: a run of kills inside ten seconds, a multiplier on what each pays, and the per-hour meter ----
    const string ComboF = "unity/Assets/Scripts/KillComboMath.cs";

    static void KillComboNumbers()
    {
        That(KillComboMath.Step(0, 0, 1000) == 1, ComboF, "the first kill must start a run of 1");
        That(KillComboMath.Step(5, 1000, 1009.9) == 6, ComboF, "a kill 9.9 s after the last must carry the run to 6");
        That(KillComboMath.Step(5, 1000, 1010) == 6, ComboF, "a kill exactly 10 s after the last must still carry the run");
        That(KillComboMath.Step(5, 1000, 1010.01) == 1, ComboF, "a kill just past 10 s must start a new run of 1");
        That(KillComboMath.EndsAt(1000) == 1010, ComboF, "a run that killed at 1000 must end at 1010");

        That(KillComboMath.BonusPercent(0) == 0, ComboF, "no run must add nothing");
        That(KillComboMath.BonusPercent(1) == 1, ComboF, "a run of 1 must add 1%");
        That(KillComboMath.BonusPercent(50) == 50, ComboF, "a run of 50 must add 50%");
        That(KillComboMath.BonusPercent(51) == 50, ComboF, "a run past 50 must stop at 50%");
        That(KillComboMath.BonusPercent(int.MaxValue) == 50, ComboF, "a huge run must stop at 50%, not overflow");

        That(KillComboMath.Paid(2, 24) == 2, ComboF, "2 at a run of 24 is 2.48, which must pay 2");
        That(KillComboMath.Paid(2, 25) == 3, ComboF, "2 at a run of 25 is 2.5, which must round UP to 3");
        That(KillComboMath.Paid(100, 12) == 112, ComboF, "100 at a run of 12 must pay 112");
        That(KillComboMath.Paid(100, 0) == 100, ComboF, "no run must pay exactly what the kill is worth");
        That(KillComboMath.Paid(0, 30) == 0, ComboF, "a kill worth nothing must pay nothing, whatever the run");

        That(KillComboMath.PerHour(100, 30) == 6000, ComboF, "100 over 30 s must be read over the 60 s floor: 6000 an hour");
        That(KillComboMath.PerHour(100, 600) == 600, ComboF, "100 over ten minutes must be 600 an hour");
        That(KillComboMath.PerHour(0, 600) == 0, ComboF, "nothing earned must be 0 an hour");

        // 10:00:30 — the current minute is 30 s in, so the ring covers 9 full minutes and those 30 s.
        double now = 36030;
        That(KillComboMath.SecondsCovered(0, now) == 570, ComboF, "the ring must cover 9 minutes and the current 30 s");
        That(KillComboMath.SecondsCovered(now - 45, now) == 45, ComboF, "a meter 45 s old must cover 45 s");

        var buckets = new long[KillComboMath.MeterBuckets];
        long b = KillComboMath.BucketOf(now);
        KillComboMath.Add(buckets, b, 40);
        long later = KillComboMath.Roll(buckets, b, b + 3);
        KillComboMath.Add(buckets, later, 60);
        That(KillComboMath.Sum(buckets) == 100, ComboF, "three minutes on, both kills must still be in the meter");
        later = KillComboMath.Roll(buckets, later, b + KillComboMath.MeterBuckets);
        That(KillComboMath.Sum(buckets) == 60, ComboF, "ten minutes on, the first kill must have left the meter");
        That(KillComboMath.Roll(buckets, later, later - 2) == later && KillComboMath.Sum(buckets) == 60, ComboF,
             "a clock that went backwards must empty nothing");
        later = KillComboMath.Roll(buckets, later, later + 25);
        That(KillComboMath.Sum(buckets) == 0, ComboF, "a meter left for longer than ten minutes must be empty");
    }

    const string KeeperF = "unity/Assets/Scripts/KeeperMath.cs";
    const string StorageF = "unity/Assets/Scripts/StorageMoves.cs";

    static void KeeperNumbers()
    {
        That(KeeperMath.KillsPerDoor == 4, KeeperF, "a door of a Keeper's trip must cost 4 kills of income");
        That(KeeperMath.StorageLimit == 600, KeeperF, "storage must hold 600 things");
        That(KeeperMath.TeleportPrice(0, 50) == 0, KeeperF, "a trip of no doors must cost nothing");
        That(KeeperMath.TeleportPrice(-1, 50) == 0, KeeperF, "a trip with no way there must cost nothing, never a negative price");
        That(KeeperMath.TeleportPrice(1, 1) == 40, KeeperF, "a trip of 1 door at level 1 must cost 40");
        That(KeeperMath.TeleportPrice(1, 99) == 3960, KeeperF, "a trip of 1 door at level 99 must cost 3,960");
        That(KeeperMath.TeleportPrice(7, 50) == 14000, KeeperF, "a trip of 7 doors at level 50 must cost 14,000");
        That(KeeperMath.TeleportPrice(1, 0) == 40, KeeperF, "a level below 1 must be priced as level 1");
        That(KeeperMath.TeleportPrice(30000000, 99) == 118800000000L, KeeperF, "a huge trip must not overflow a whole number");

        var home = new UnityEngine.Vector3(10f, 2f, 5f);
        var north = KeeperMath.ArrivalSpot(home, 0f, 1.5f);
        That(Math.Abs(north.x - 10f) < 0.001f && Math.Abs(north.y - 2f) < 0.001f && Math.Abs(north.z - 6.5f) < 0.001f, KeeperF,
             "a Keeper facing north must set you down 1.5 m north of them, at their height");
        var east = KeeperMath.ArrivalSpot(home, 90f, 1.5f);
        That(Math.Abs(east.x - 11.5f) < 0.001f && Math.Abs(east.z - 5f) < 0.001f, KeeperF, "a Keeper facing east must set you down 1.5 m east of them");
        var south = KeeperMath.ArrivalSpot(home, 180f, 1.5f);
        That(Math.Abs(south.x - 10f) < 0.001f && Math.Abs(south.z - 3.5f) < 0.001f, KeeperF, "a Keeper facing south must set you down 1.5 m south of them");

        That(KeeperMath.WithinReach(3f, 0f, 3f), KeeperF, "exactly at the reach must count as within it");
        That(!KeeperMath.WithinReach(3f, 0.1f, 3f), KeeperF, "just past the reach must not count");
        That(KeeperMath.WithinReach(-2f, -2f, 3f), KeeperF, "2 m west and 2 m south is 2.83 m away, within 3 m");

        That(!KeeperMath.Fits(600, 600, true), KeeperF, "a new thing must not fit in a full storage");
        That(KeeperMath.Fits(600, 600, false), KeeperF, "more of a stack already stored must fit in a full storage");
        That(KeeperMath.Fits(599, 600, true), KeeperF, "a new thing must fit while one place is left");

        // Each area's walkable cells, grouped once when a map is labelled, so a random landing is one lookup.
        var labels = new ushort[] { 0, 2, 1, 2, 2, 0, 1 };
        var grouped = KeeperMath.GroupByLabel(labels, 2, out var firstOf);
        That(grouped.Length == 5, KeeperF, "five walkable cells must be grouped, and the two of label 0 left out");
        That(firstOf.Length == 4 && firstOf[1] == 0 && firstOf[2] == 2 && firstOf[3] == 5, KeeperF,
             "area 1 must own entries 0-1 and area 2 entries 2-4");
        That(grouped[0] == 2 && grouped[1] == 6, KeeperF, "area 1's cells must be positions 2 and 6, in order");
        That(grouped[2] == 1 && grouped[3] == 3 && grouped[4] == 4, KeeperF, "area 2's cells must be positions 1, 3 and 4, in order");
        var none = KeeperMath.GroupByLabel(null, 3, out var noneFirst);
        That(none.Length == 0 && noneFirst.Length == 5 && noneFirst[4] == 0, KeeperF, "no labels at all must group nothing");
        var gap = KeeperMath.GroupByLabel(new ushort[] { 3, 1, 3 }, 3, out var gapFirst);
        That(gapFirst[2] - gapFirst[1] == 1 && gapFirst[3] - gapFirst[2] == 0 && gapFirst[4] - gapFirst[3] == 2
             && grouped.Length == 5 && gap[gapFirst[3]] == 0 && gap[gapFirst[3] + 1] == 2, KeeperF,
             "an area with no cells must own no entries, and the areas after it must still find theirs");

        // What a take adds to the bag a save writes.
        var d = new SaveData
        {
            bagItemIds = new[] { "item.sword", "item.potion" }, bagCounts = new[] { 1, 3 }, bagRefines = new[] { 2, 0 },
            bagCards = new[] { "", "" }, bagRolls = new[] { "r", "" }, bagCopyIds = new[] { "c0", "" },
        };
        StorageMoves.AddToSavedBag(d, "item.potion", 2, 0, "", "", "");
        That(d.bagItemIds.Length == 2 && d.bagCounts[1] == 5, StorageF, "a stack taken out of storage must join the stack already in the bag");
        StorageMoves.AddToSavedBag(d, "item.sword", 1, 4, "card", "roll", "c1");
        That(d.bagItemIds.Length == 3 && d.bagItemIds[2] == "item.sword" && d.bagCounts[2] == 1 && d.bagRefines[2] == 4
             && d.bagCards[2] == "card" && d.bagRolls[2] == "roll" && d.bagCopyIds[2] == "c1", StorageF,
             "a piece of gear taken out of storage must be its own entry at the end, with its own four values");
        That(d.bagCounts[0] == 1 && d.bagCopyIds[0] == "c0", StorageF, "taking a piece out of storage must not touch the piece already carried");
        StorageMoves.AddToSavedBag(d, "item.sword", 3, 0, "", "", "");
        That(d.bagItemIds.Length == 4 && d.bagCounts[3] == 3 && d.bagCopyIds[3] == "", StorageF,
             "a stack must never join an entry that is a piece of gear, even of the same item");
        var empty = new SaveData();
        StorageMoves.AddToSavedBag(empty, "item.potion", 4, 0, "", "", "");
        That(empty.bagItemIds != null && empty.bagItemIds.Length == 1 && empty.bagCounts[0] == 4 && empty.bagCopyIds[0] == "", StorageF,
             "a take into a save with no bag at all must make a bag of that one stack");
        StorageMoves.AddToSavedBag(empty, "item.potion", 0, 0, "", "", "");
        That(empty.bagCounts[0] == 4, StorageF, "a take of nothing must change nothing");

        // What a REFUSED take puts back: exactly what the take added, and nothing else.
        StorageMoves.RemoveFromSavedBag(d, "item.potion", 2, "");
        That(d.bagItemIds.Length == 4 && d.bagCounts[1] == 3, StorageF,
             "a refused take of 2 must leave the stack it joined with the 3 it had before");
        StorageMoves.RemoveFromSavedBag(d, "item.sword", 1, "c1");
        That(d.bagItemIds.Length == 3 && d.bagCopyIds[0] == "c0" && d.bagCopyIds[2] == "" && d.bagCounts[2] == 3
             && d.bagRefines.Length == 3 && d.bagCards.Length == 3 && d.bagRolls.Length == 3, StorageF,
             "a refused take of a piece must remove that very piece from every bag array, and no other entry");
        StorageMoves.RemoveFromSavedBag(empty, "item.potion", 4, "");
        That(empty.bagItemIds.Length == 0 && empty.bagCounts.Length == 0 && empty.bagCopyIds.Length == 0, StorageF,
             "a refused take of a whole stack must remove the stack's entry, not leave one of nothing");
        StorageMoves.RemoveFromSavedBag(empty, "item.potion", 1, "");
        That(empty.bagItemIds.Length == 0, StorageF, "taking back from a bag that does not hold the thing must change nothing");
    }

    // ---- how much a bag holds: 24 entries a tab, and Ragnarok's carry weight ----------------------------------
    // Promised numbers (2026-10-12): 24 a tab, 2000 + 30 per STR. A stack already held never needs a new entry, so a
    // tab at its limit still takes more of what it holds.
    const string BagRoomF = "unity/Assets/Scripts/BagRoom.cs";
    static void BagRoomNumbers()
    {
        That(BagRoom.WeightCap(0) == 2000, BagRoomF, "carry weight with no STR should be 2000, is " + BagRoom.WeightCap(0));
        That(BagRoom.WeightCap(10) == 2300, BagRoomF, "carry weight at 10 STR should be 2300, is " + BagRoom.WeightCap(10));
        That(BagRoom.SlotsPerCategory == 24, BagRoomF, "a tab should hold 24 entries, holds " + BagRoom.SlotsPerCategory);
        That(BagRoom.SlotsNeeded(true, 3, false) == 3, BagRoomF, "three pieces of gear should need three entries");
        That(BagRoom.SlotsNeeded(false, 99, true) == 0, BagRoomF, "more of a stack already held should need no entry");
        That(BagRoom.SlotsNeeded(false, 99, false) == 1, BagRoomF, "a new stack should need one entry");
        That(BagRoom.Judge(24, 0, 0, 1, 2000) == BagRefusal.None, BagRoomF, "a full tab should still take more of a held stack");
        That(BagRoom.Judge(24, 1, 0, 1, 2000) == BagRefusal.SlotsFull, BagRoomF, "a full tab should refuse a new entry");
        That(BagRoom.Judge(23, 1, 0, 1, 2000) == BagRefusal.None, BagRoomF, "the 24th entry should fit");
        That(BagRoom.Judge(0, 1, 1990, 10, 2000) == BagRefusal.None, BagRoomF, "reaching the weight limit exactly should fit");
        That(BagRoom.Judge(0, 1, 1991, 10, 2000) == BagRefusal.TooHeavy, BagRoomF, "going one past the weight limit should be refused");
        That(BagRoom.Judge(0, 1, 5000, 0, 2000) == BagRefusal.None, BagRoomF, "a weightless thing should never be too heavy");
        That(BagRoom.MostThatFits(3000, 10, true, 1500, 2, 2000) == 250, BagRoomF, "500 weight of room should take 250 of a weight-2 pile, takes " + BagRoom.MostThatFits(3000, 10, true, 1500, 2, 2000));
        That(BagRoom.MostThatFits(5, 24, false, 0, 1, 2000) == 0, BagRoomF, "a full tab should take none of a new pile");
        That(BagRoom.MostThatFits(5, 24, true, 0, 1, 2000) == 5, BagRoomF, "a full tab should take all of a held pile that is light enough");
    }

    // ---- cape emblems: what is read from an upload is exactly what is stored and drawn --------------------
    // A wrong decode is silent: a picture that comes out shifted, mirrored or with pink left solid still looks like
    // "an emblem" until someone compares it with what they drew.
    const string EmblemF = "unity/Assets/Scripts/EmblemImage.cs";

    static byte[] Bmp24(int width, int height, Func<int, int, (byte r, byte g, byte b)> pixel)
    {
        int stride = (width * 3 + 3) & ~3;
        var f = new byte[54 + stride * height];
        f[0] = (byte)'B'; f[1] = (byte)'M';
        BitConverter.GetBytes(f.Length).CopyTo(f, 2);
        BitConverter.GetBytes(54).CopyTo(f, 10);
        BitConverter.GetBytes(40).CopyTo(f, 14);
        BitConverter.GetBytes(width).CopyTo(f, 18);
        BitConverter.GetBytes(height).CopyTo(f, 22);
        BitConverter.GetBytes((short)1).CopyTo(f, 26);
        BitConverter.GetBytes((short)24).CopyTo(f, 28);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                int at = 54 + (height - 1 - y) * stride + x * 3;
                f[at] = b; f[at + 1] = g; f[at + 2] = r;
            }
        return f;
    }

    static void EmblemPictures()
    {
        int side = EmblemImage.Side;
        // Pink everywhere except a red pixel in the top-left corner and a blue one in the bottom-right.
        var bmp = Bmp24(side, side, (x, y) => x == 0 && y == 0 ? ((byte)255, (byte)0, (byte)0)
                                             : x == side - 1 && y == side - 1 ? ((byte)0, (byte)0, (byte)255)
                                             : ((byte)255, (byte)0, (byte)255));
        var rgba = EmblemImage.Decode(bmp, out string why);
        That(rgba != null, EmblemF, "a 24 x 24, 24-bit BMP is refused: " + why);
        if (rgba == null) return;
        That(rgba[0] == 255 && rgba[1] == 0 && rgba[2] == 0 && rgba[3] == 255, EmblemF, "the BMP's top-left pixel is not solid red — rows read upside down?");
        int last = (side * side - 1) * 4;
        That(rgba[last + 2] == 255 && rgba[last + 3] == 255, EmblemF, "the BMP's bottom-right pixel is not solid blue");
        That(rgba[4 + 3] == 0, EmblemF, "pink #FF00FF is not see-through");

        var png = EmblemImage.EncodePng(rgba);
        var back = EmblemImage.Decode(png, out why);
        That(back != null, EmblemF, "the PNG it writes is refused by its own reader: " + why);
        if (back != null)
        {
            bool same = back.Length == rgba.Length;
            for (int i = 0; same && i < back.Length; i++) same = back[i] == rgba[i];
            That(same, EmblemF, "writing the PNG and reading it back changes a pixel");
        }
        That(EmblemImage.Id(png) == EmblemImage.Id(EmblemImage.EncodePng(rgba)), EmblemF, "the same picture gets two different ids");

        That(EmblemImage.Decode(Bmp24(side + 1, side, (x, y) => ((byte)1, (byte)2, (byte)3)), out _) == null, EmblemF, "a 25 x 24 picture is accepted");
        That(EmblemImage.Decode(new byte[EmblemImage.MaxFileBytes + 1], out _) == null, EmblemF, "a file over the size limit is accepted");
        That(EmblemImage.Decode(new byte[] { 1, 2, 3, 4 }, out _) == null, EmblemF, "a file that is neither BMP nor PNG is accepted");
    }

    static int Main()
    {
        WeaponDrawRules();
        EmblemPictures();
        ExperienceCurve();
        CreationSpreads();
        StatPoints();
        AccuracyAndDefence();
        StatJumps();
        ElementChart();
        WornAccuracyAndEvasion();
        ArmourWordRules();
        StrikeGrowthRules();
        FlightRules();
        GlideTripSeconds();
        Ring();
        QualityLadder();
        CraftCosts();
        TrademarkRoll();
        KillDropNumbers();
        WornGearAndSlots();
        QuenchOdds();
        InkRoll();
        MasteryCurve();
        BuffBonus();
        SkillEfficiencyNumbers();
        TimedEffectPause();
        RegenPerSecond();
        WalkingRule();
        WaterRules();
        SavedCharacterNumbers();
        CreatureMaterialNumbers();
        KillPointNumbers();
        CreaturePayNumbers();
        QuestSteps();
        StrideRules();
        WorldEventNumbers();
        ShopNumbers();
        BagRoomNumbers();
        SwingContactRules();
        KillComboNumbers();
        KeeperNumbers();

        if (failures == 0)
        {
            Console.WriteLine("game math clean - every pinned number is what it was.");
            return 0;
        }
        Console.WriteLine("");
        Console.WriteLine(failures + " number(s) have moved.");
        return 1;
    }
}
