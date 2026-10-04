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
        That(CreatureKinds.KillExperience(CreatureKind.Normal, 50) == UnitStatMath.KillXp(50), StatMath,
             "a normal creature does not pay its level's amount");
        That(CreatureKinds.KillExperience(CreatureKind.Elite, 50) == 10 * UnitStatMath.KillXp(50), StatMath,
             "an elite does not pay ten times its level's amount");
        That(CreatureKinds.KillExperience(CreatureKind.Shadow, 150) == 20 * UnitStatMath.KillXp(150), StatMath,
             "a shadow does not pay twenty times its level's amount");

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
        That(Math.Abs(Combat.MaxHitChance - 0.95f) < 0.0001f, CombatF,
             "the accuracy CEILING is no longer 95%: " + Combat.MaxHitChance);
        for (int hit = -500; hit <= 500; hit += 25)
            for (int flee = 0; flee <= 500; flee += 25)
            {
                float chance = Combat.HitChance01(hit, flee);
                That(chance >= 0.05f - 0.0001f && chance <= 0.95f + 0.0001f, CombatF,
                     "hit chance left its window at hit " + hit + " against evasion " + flee + ": " + chance);
            }

        // A critical hit ignores defence. Every value, not one sample.
        for (int def = 0; def <= 200; def += 20)
            That(Math.Abs(Combat.AfterDefense(100f, def, true) - 100f) < 0.001f, CombatF,
                 "a critical hit was reduced by defence (" + def + ")");

        // An ordinary hit loses exactly the flat defence — no percentage is taken here.
        for (int def = 0; def <= 200; def += 20)
            That(Math.Abs(Combat.AfterDefense(100f, def, false) - (100f - def)) < 0.001f, CombatF,
                 "an ordinary hit should lose exactly its defence (" + def + ")");

        // A crit's chance is held inside [0, 95%] however much LUK either side has — the same sum for every unit.
        for (int luk = 0; luk <= 1000; luk += 50)
            foreach (float chance in new[] { 0f, 0.3f, 0.95f, 5f })
            {
                float c = UnitStatMath.EffectiveCritChance(chance, luk);
                That(c >= 0f && c <= 0.95f + 0.0001f, StatMath,
                     "crit chance " + chance + " against " + luk + " LUK left [0, 95%]: " + c);
            }
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
        That(Math.Abs(ArmourWords.Amount(ArmourWord.Bulwark, Quality.A) - 8f) < 0.001f, WordsF, "Bulwark at A should be 8%");
        That(Math.Abs(ArmourWords.Amount(ArmourWord.Fleet, Quality.A) - 10f) < 0.001f, WordsF, "Fleet at A should be 10%");
        That(Math.Abs(ArmourWords.Amount(ArmourWord.Emberward, Quality.A) - 15f) < 0.001f, WordsF, "a ward at A should be 15%");

        // Ironblood's percentage reaches the crit roll as the LUK it is worth: 7.5% at 0.3% a point is 25 LUK.
        That(UnitStatMath.CritShieldAsLuk(7.5f) == 25, StatMath, "7.5% crit shield should be worth 25 LUK");
        // A quarter cut takes a quarter off; a ward cuts damage but never touches immunity or a heal.
        That(Math.Abs(ArmourWords.CutBy(8f, 0.25f) - 6f) < 0.001f, WordsF, "a 25% cut of 8 should be 6");
        That(Math.Abs(Elements.Warded(1.5f, 0.1f) - 1.35f) < 0.001f, ElementF, "a 10% ward on 150% should leave 135%");
        That(Elements.Warded(0f, 0.5f) == 0f && Elements.Warded(-0.5f, 0.5f) == -0.5f, ElementF,
             "a ward must never change an immune or a healing answer");
        // A worn word lands in the sub-stat table in that stat's own units.
        var table = new float[SubStats.All.Length];
        var eb = default(EquipBonuses);
        ArmourWords.AddWord(ArmourWord.Fleet, 10f, ref eb);
        ArmourWords.AddWord(ArmourWord.Wellspring, 2.5f, ref eb);
        ArmourWords.FoldIntoSubStats(eb, table);
        That(Math.Abs(table[(int)SubStat.MovementSpeed] - 0.1f) < 0.0001f, WordsF, "Fleet 10% should be +0.10 movement speed");
        That(Math.Abs(table[(int)SubStat.MPRegen] - 2.5f) < 0.0001f, WordsF, "Wellspring 2.5 should be +2.5 MP a second");
        // Block chance with a worn extra never makes a wall.
        That(Math.Abs(Combat.BlockChance(0.05f, 0.08f) - 0.13f) < 0.001f, CombatF, "5% block + 8% Guarding should be 13%");
        That(Combat.BlockChance(0.9f, 0.5f) <= 0.95f + 0.0001f, CombatF, "block chance passed 95%");
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

        // The held weapon picks which one a unit swings with.
        foreach (WeaponType w in Enum.GetValues(typeof(WeaponType)))
        {
            int expected = WeaponTypes.IsRanged(w) ? UnitStatMath.RangedAttack(u, none, 0f)
                                                   : UnitStatMath.MeleeAttack(u, none, 0f);
            That(UnitStatMath.PhysicalAttack(w, u, none, 0f) == expected, StatMath,
                 "holding a " + w + " swings with the wrong attack");
        }
        That(WeaponTypes.IsRanged(WeaponType.Bow) && WeaponTypes.IsRanged(WeaponType.Firearm), "unity/Assets/Scripts/WeaponType.cs",
             "a bow or a firearm is no longer a ranged weapon");

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

        foreach (var stat in SkillRing.Ring)
        {
            float cap = SkillRing.CapFor(stat);
            That(Math.Abs(SkillRing.Percent(stat, 0)) < 0.0001f, RingF,
                 stat + " is worth something at zero ranks");
            float last = -1f;
            for (int ranks = 0; ranks <= 200; ranks++)
            {
                float value = SkillRing.Percent(stat, ranks);
                That(value >= last, RingF, stat + " goes DOWN between " + (ranks - 1) + " and " + ranks + " ranks");
                That(value < cap + 0.0001f, RingF,
                     stat + " passed its ceiling at " + ranks + " ranks: " + value + " over " + cap);
                last = value;
            }
        }
    }

    // ---- what a quality grade is worth ------------------------------------------------------------------
    // The ladder rarity used to be. Every rung has to be worth more than the one below it, in each of the five
    // things a grade decides, or a quench can take a piece backwards while reading as a promotion.
    static void QualityLadder()
    {
        var grades = (Quality[])Enum.GetValues(typeof(Quality));
        That(grades.Length > 1, CraftF, "there is only one quality grade");

        for (int i = 1; i < grades.Length; i++)
        {
            var lower = grades[i - 1];
            var upper = grades[i];
            That(CraftingMath.FoldCap(upper) > CraftingMath.FoldCap(lower), CraftF,
                 upper + " allows no more folds than " + lower);
            That(CraftingMath.QualityLift(upper) >= CraftingMath.QualityLift(lower), CraftF,
                 upper + " lifts a fold total less than " + lower);
            That(CraftingMath.InkSlots(upper) >= CraftingMath.InkSlots(lower), CraftF,
                 upper + " holds fewer written stats than " + lower);
            That(CraftingMath.WeaponMasteryCap(upper) > CraftingMath.WeaponMasteryCap(lower), CraftF,
                 upper + " caps weapon mastery no higher than " + lower);
            That(CraftingMath.SkillLevelCap(upper, 10) >= CraftingMath.SkillLevelCap(lower, 10), CraftF,
                 upper + " lets fewer of a skill's levels count than " + lower);
            That(CraftingMath.TrainingSpeed(upper) > CraftingMath.TrainingSpeed(lower), CraftF,
                 upper + " trains no faster than " + lower);
            That(CraftingMath.Band(upper) == CraftingMath.Band(lower) + 1, CraftF,
                 upper + " does not fold with the band above " + lower + "'s");
        }

        // A fold is never worth less than the folds themselves, and no folds is worth nothing.
        foreach (var grade in grades)
            for (int folds = 0; folds <= CraftingMath.FoldCap(grade); folds++)
            {
                int atk = CraftingMath.PieceNumbers(EquipSlotType.Weapon, WeaponType.Sword, MaterialKind.Metal, folds, grade).attack;
                That(atk >= folds, CraftF, grade + " +" + folds + " is worth " + atk + " attack, below its own folds");
                if (folds == 0) That(atk == 0, CraftF, "an unfolded " + grade + " piece is worth " + atk);
            }

        // A caster's weapon folds into magic attack.
        var staff = CraftingMath.PieceNumbers(EquipSlotType.Weapon, WeaponType.Staff, MaterialKind.Metal, 10, Quality.A);
        That(staff.attack == 0 && staff.magicAttack > 0, CraftF, "a caster's weapon no longer folds into magic attack");

        // An armour piece's MATERIAL decides what a fold buys; its SLOT how much (decided 2026-09-23). The literals are
        // the agreed table: body armour at A +35 is 23 defence and 182 HP in metal, 11.4% attack speed and 11 evasion
        // in leather, 23 magic defence and 91 MP in cloth; a head or shoes take half.
        var metal = CraftingMath.PieceNumbers(EquipSlotType.Armor, WeaponType.None, MaterialKind.Metal, 35, Quality.A);
        That(metal.defense == 23 && metal.hp == 182 && metal.magicDefense == 0 && metal.evasion == 0, CraftF,
             "metal body armour at A +35 should be 23 defence and 182 HP, got " + metal.defense + " and " + metal.hp);
        var leather = CraftingMath.PieceNumbers(EquipSlotType.Armor, WeaponType.None, MaterialKind.Leather, 35, Quality.A);
        That(Math.Abs(leather.attackSpeedPercent - 11.4f) < 0.001f && leather.evasion == 11 && leather.defense == 0, CraftF,
             "leather body armour at A +35 should be 11.4% attack speed and 11 evasion, got " + leather.attackSpeedPercent + " and " + leather.evasion);
        var cloth = CraftingMath.PieceNumbers(EquipSlotType.Armor, WeaponType.None, MaterialKind.Cloth, 35, Quality.A);
        That(cloth.magicDefense == 23 && cloth.mp == 91 && cloth.defense == 0, CraftF,
             "cloth body armour at A +35 should be 23 magic defence and 91 MP, got " + cloth.magicDefense + " and " + cloth.mp);
        var shoes = CraftingMath.PieceNumbers(EquipSlotType.Boots, WeaponType.None, MaterialKind.Metal, 35, Quality.A);
        That(shoes.defense == 11 && shoes.hp == 91, CraftF,
             "metal shoes at A +35 should take half: 11 defence and 91 HP, got " + shoes.defense + " and " + shoes.hp);
        var bare = CraftingMath.PieceNumbers(EquipSlotType.Head, WeaponType.None, MaterialKind.None, 35, Quality.A);
        That(bare.defense == 0 && bare.hp == 0 && bare.mp == 0, CraftF, "armour with no material should fold into nothing");
    }

    // ---- a quench's odds, with and without Salt ---------------------------------------------------------
    // Pinned because all three outcomes are invisible: a set that does not add to a hundred, or a Salt that
    // makes a quench certain, reads exactly like one that does not.
    static void QuenchOdds()
    {
        foreach (Quality grade in Enum.GetValues(typeof(Quality)))
            for (int salt = 0; salt <= 3; salt++)
            {
                CraftingMath.QuenchOdds(grade, salt, out int up, out int same, out int down);
                That(up + same + down == 100, CraftF,
                     grade + " with salt " + salt + " has odds summing to " + (up + same + down));
                That(up >= 0 && same >= 0 && down >= 0, CraftF, grade + " with salt " + salt + " has a negative chance");
                That(up <= CraftingMath.MaxUpPct, CraftF,
                     grade + " with salt " + salt + " can go up " + up + "% — a quench is never a certainty");

                CraftingMath.QuenchOdds(grade, 0, out _, out _, out int plainDown);
                if (plainDown > 0)
                    That(down >= CraftingMath.DownFloorPct, CraftF,
                         grade + " with salt " + salt + " can fall only " + down + "%, below the floor");

                if (salt > 0)
                {
                    CraftingMath.QuenchOdds(grade, salt - 1, out int lessUp, out _, out _);
                    That(up >= lessUp, CraftF, "salt " + salt + " helps " + grade + " less than salt " + (salt - 1));
                }
            }

        // A quench moves ONE rung, never two, and never below F once F has been reached.
        foreach (Quality grade in Enum.GetValues(typeof(Quality)))
        {
            if (!CraftingMath.CanQuench(grade)) continue;
            for (int roll = 0; roll < 100; roll++)
            {
                var landed = CraftingMath.QuenchResult(grade, 0, roll);
                int step = (int)landed - (int)grade;
                That(step >= -1 && step <= 1, CraftF, grade + " quenched to " + landed + " — more than one rung");
                // F is the FLOOR once it has been reached. A Raw piece that fails simply stays Raw: it is the
                // state a forge hands over, not a rung anything falls back to.
                var floor = grade < Quality.F ? grade : Quality.F;
                That(landed >= floor, CraftF, grade + " quenched below " + floor + ", to " + landed);
            }
        }
        That(!CraftingMath.CanQuench(CraftingMath.Best), CraftF, "the best grade can still be quenched");
    }

    // ---- what one Ink writes ----------------------------------------------------------------------------
    // Pinned because the roll is a weighted walk: a stat that cannot come up, or one that comes up twice as
    // often as its weight, is silent — the piece simply ends up with a different stat than it should have.
    static void InkRoll()
    {
        // Every tier is worth more than the one below, for every stat.
        foreach (var stat in CraftingMath.All)
            for (int tier = 2; tier <= 5; tier++)
                That(CraftingMath.InkValue(stat, tier) > CraftingMath.InkValue(stat, tier - 1), CraftF,
                     stat + " at tier " + tier + " is worth no more than at " + (tier - 1));

        // THE PRIMARY PREMIUM: a primary is worth clearly more than the Attack line it is priced against.
        for (int tier = 1; tier <= 5; tier++)
        {
            float attack = CraftingMath.InkValue(InkStat.Attack, tier);
            foreach (var stat in CraftingMath.All)
                if (CraftingMath.IsPrimary(stat))
                    That(CraftingMath.InkValue(stat, tier) > attack, CraftF,
                         stat + " at tier " + tier + " is worth no more than the same tier's Attack");
        }

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

        // THE GRADE TABLE, SCALED TO EACH SKILL'S OWN MAX: a 10-level skill counts 2/3/4/6/7/9/10 from Raw to A, a
        // 5-level one 1/2/2/3/4/5/5, and a 1-level skill always counts — every grade lets a learned skill be used.
        int[] tenLevel = { 2, 3, 4, 6, 7, 9, 10 };
        int[] fiveLevel = { 1, 2, 2, 3, 4, 5, 5 };
        for (int g = 0; g <= (int)Quality.A; g++)
        {
            var q = (Quality)g;
            That(CraftingMath.SkillLevelCap(q, 10) == tenLevel[g], MasteryF,
                 q + " lets " + CraftingMath.SkillLevelCap(q, 10) + " levels of a 10-level skill count, not " + tenLevel[g]);
            That(CraftingMath.SkillLevelCap(q, 5) == fiveLevel[g], MasteryF,
                 q + " lets " + CraftingMath.SkillLevelCap(q, 5) + " levels of a 5-level skill count, not " + fiveLevel[g]);
            That(CraftingMath.SkillLevelCap(q, 1) == 1, MasteryF, q + " does not let a 1-level skill count");
        }

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

    // SAVED CHARACTERS: the gold a rescue pays, the drops a rebel gives up, the side each behaviour stands on, and the
    // one question every targeting path asks. Pinned from docs/3-world/saved-characters.md, agreed 2026-09-30.
    static void SavedCharacterNumbers()
    {
        const string F = "unity/Assets/Scripts/SavedCharacterRules.cs";
        const string S = "unity/Assets/Scripts/Side.cs";
        That(SavedCharacterRules.TransformChance == 0.15f, F, "a beaten normal or elite turns back 15% of the time");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.NeverFights, GoldTier.Ordinary, 5) == 100, F, "an ordinary rescue pays the mob's gold x20");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.NeverFights, GoldTier.Helpless, 5) == 200, F, "a helpless rescue pays x40");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.NeverFights, GoldTier.Wealthy, 5) == 300, F, "a wealthy rescue pays x60");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.Ally, GoldTier.Wealthy, 5) == 0, F, "an ally pays no gold");
        That(SavedCharacterRules.RescueGold(SavedBehaviour.Rebel, GoldTier.Ordinary, 5) == 0, F, "a rebel pays no gold");
        That(SavedCharacterRules.LootKindOfRebel(CreatureKind.Normal) == CreatureKind.Elite, F, "a rebel from a normal drops as an elite");
        That(SavedCharacterRules.LootKindOfRebel(CreatureKind.Elite) == CreatureKind.Shadow, F, "a rebel from an elite drops as a shadow");
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
        That(!QuestMath.InBag("item.sword", worn), QuestF, "a worn sword is not in the bag, where a station works");
        That(!QuestMath.Met(QuestObjectiveKind.TalkTo, "item.sword", 1, bag), QuestF, "a talk step is never met by holding something");
        That(QuestMath.CanPay(8, 8, 800, 800) && !QuestMath.CanPay(7, 8, 800, 800) && !QuestMath.CanPay(8, 8, 799, 800),
             QuestF, "a bill is paid only when both the material and the gold cover it");

        var doors = new Dictionary<string, List<string>>
        {
            ["a"] = new List<string> { "b", "c" }, ["b"] = new List<string> { "a", "d" },
            ["c"] = new List<string> { "a" }, ["d"] = new List<string> { "b" },
        };
        That(WorldRoutes.NextFrom(doors, "a", "d", out int n) == "b" && n == 2, RoutesF, "a to d goes through b, two doors");
        That(WorldRoutes.NextFrom(doors, "a", "a", out n) == "a" && n == 0, RoutesF, "the same map is no doors away");
        That(WorldRoutes.NextFrom(doors, "a", "z", out n) == null && n == -1, RoutesF, "a map with no way to it has no next map");
    }

    static int Main()
    {
        ExperienceCurve();
        CreationSpreads();
        StatPoints();
        AccuracyAndDefence();
        StatJumps();
        ElementChart();
        WornAccuracyAndEvasion();
        ArmourWordRules();
        Ring();
        QualityLadder();
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
        QuestSteps();

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
