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
    const string RarityF  = "unity/Assets/Scripts/Rarity.cs";
    const string StatesF  = "unity/Assets/Scripts/UnitStates.cs";
    const string StatusF  = "unity/Assets/Scripts/StatusController.cs";

    // ---- the experience curve, against its own closed form --------------------------------------------
    // Two expressions of one rule: the per-level cost, and a hand-written formula for the running total. They
    // are used in different places and nothing keeps them honest, so they are checked against each other. The
    // total is what a save older than v10 has its lifetime experience invented from, so a wrong one silently
    // rewrites real progress on load and the only symptom is a wrong number on a leaderboard.
    static void ExperienceCurve()
    {
        long running = 0;
        for (int level = 1; level <= 500; level++)
        {
            long closed = UnitStatMath.XpEarnedByLevel(level);
            if (closed != running)
            {
                Fail(StatMath, "experience earned by level " + level + " is " + closed +
                               ", but adding up each level's cost gives " + running);
                return;   // one report, not five hundred
            }
            running += UnitStatMath.XpToNextFrom(level);
        }
        That(UnitStatMath.XpEarnedByLevel(1) == 0, StatMath, "level 1 should have earned no experience");
        That(UnitStatMath.XpEarnedByLevel(0) == 0, StatMath, "level 0 should have earned no experience");
        That(UnitStatMath.XpEarnedByLevel(int.MaxValue) > 0, StatMath,
             "an absurd level should saturate, never go negative");
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

        // A critical hit ignores BOTH halves of defence. Every value, not one sample.
        for (int hard = 0; hard <= 100; hard += 10)
            for (int soft = 0; soft <= 200; soft += 20)
                That(Math.Abs(Combat.AfterDefense(100f, hard, soft, true) - 100f) < 0.001f, CombatF,
                     "a critical hit was reduced by defence (" + hard + "% hard, " + soft + " soft)");

        // Full hard defence removes the whole percentage, leaving only the flat subtraction.
        That(Math.Abs(Combat.AfterDefense(100f, 100, 7, false) - (-7f)) < 0.001f, CombatF,
             "100% hard defence should leave exactly minus the flat defence");

        // A crit's chance is held inside [0, 95%] however much LUK either side has — the same sum for every unit.
        for (int luk = 0; luk <= 1000; luk += 50)
            foreach (float chance in new[] { 0f, 0.3f, 0.95f, 5f })
            {
                float c = UnitStatMath.EffectiveCritChance(chance, luk);
                That(c >= 0f && c <= 0.95f + 0.0001f, StatMath,
                     "crit chance " + chance + " against " + luk + " LUK left [0, 95%]: " + c);
            }
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

        foreach (Element attack in Enum.GetValues(typeof(Element)))
            foreach (Element armour in Enum.GetValues(typeof(Element)))
            {
                float m = Elements.Modifier(attack, armour);
                That(!float.IsNaN(m) && !float.IsInfinity(m), ElementF,
                     attack + " against " + armour + " is not a real number");
                That(m >= 0f, ElementF, attack + " against " + armour + " is negative: " + m);
            }
        That(Math.Abs(Elements.Modifier(Element.Neutral, Element.Neutral) - 1f) < 0.001f, ElementF,
             "plain against plain should be exactly one");
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

    // ---- what a rarity is worth -----------------------------------------------------------------------
    static void RarityLadder()
    {
        var tiers = (Rarity[])Enum.GetValues(typeof(Rarity));
        That(Math.Abs(Rarities.OutputLift(tiers[0])) < 0.0001f, RarityF,
             "the lowest rarity should add nothing");
        float previousStep = -1f, previous = 0f;
        for (int i = 1; i < tiers.Length; i++)
        {
            float lift = Rarities.OutputLift(tiers[i]);
            float step = lift - previous;
            That(step > 0f, RarityF, tiers[i] + " is worth no more than " + tiers[i - 1]);
            That(step >= previousStep, RarityF,
                 "the rarity ladder stops accelerating at " + tiers[i] + " — its step is smaller than the last");
            previousStep = step; previous = lift;
        }

        // What a rolled main stat gains per tier is a PROMISED number, so it is written out rather than read back.
        int[] promised = { 0, 1, 2, 3, 5 };
        That(tiers.Length == promised.Length, RarityF,
             "there are " + tiers.Length + " rarity tiers but " + promised.Length + " promised main-stat extras");
        for (int i = 0; i < tiers.Length && i < promised.Length; i++)
            That(Rarities.StatExtra(tiers[i]) == promised[i], RarityF,
                 tiers[i] + " no longer adds +" + promised[i] + " to a rolled main stat: " + Rarities.StatExtra(tiers[i]));

        // A gear number lifted by rarity: nothing at the lowest tier, never below itself, and zero stays zero.
        foreach (var tier in tiers)
        {
            That(Rarities.Lifted(0, tier) == 0, RarityF, "a zero gear number lifted by " + tier + " is no longer zero");
            for (int flat = 1; flat <= 500; flat += 7)
                That(Rarities.Lifted(flat, tier) >= flat, RarityF, tier + " lifted " + flat + " below itself");
        }
        for (int flat = 0; flat <= 500; flat += 7)
            That(Rarities.Lifted(flat, tiers[0]) == flat, RarityF, "the lowest rarity changes a gear number of " + flat);
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

    static int Main()
    {
        ExperienceCurve();
        StatPoints();
        AccuracyAndDefence();
        StatJumps();
        ElementChart();
        Ring();
        RarityLadder();
        BuffBonus();
        RegenPerSecond();

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
