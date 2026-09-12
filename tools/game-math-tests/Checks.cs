// The game's arithmetic, pinned. Run by tools/check-game-math.sh, outside Unity, in about a second.
//
// WHAT EARNS A CHECK HERE: a function where a WRONG ANSWER IS SILENT — a plausible-looking number nobody
// notices for a week. Not things that would obviously break on screen the moment you pressed play; those you
// would see anyway, and testing them is the waste this file exists to avoid.
//
// PREFER THE GENERAL FORM. "The element table matches its list's real size" survives an eleventh element being
// added; "the table is 10 by 10" does not, and is the check that quietly stops covering the thing it names.
// Write a specific number only where a specific number was PROMISED — the 95% accuracy ceiling, the half-damage
// floor on glancing magic. Those are promises, and a promise has to be stated to be checked.
using System;
using System.Collections.Generic;

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

        // Magic is dodge-RESISTANT, never dodgeable: it keeps all its bite at zero evasion and at least half
        // of it however evasive the target.
        That(Math.Abs(Combat.MagicGlanceMult(0) - 1f) < 0.001f, CombatF,
             "magic should lose nothing against a target with no evasion");
        That(Math.Abs(Combat.MagicGlanceMaxReduction - 0.5f) < 0.0001f, CombatF,
             "magic's worst case is no longer half damage: " + Combat.MagicGlanceMaxReduction);
        for (int flee = 0; flee <= 5000; flee += 50)
            That(Combat.MagicGlanceMult(flee) >= 0.5f - 0.001f, CombatF,
                 "magic fell below half damage against evasion " + flee);
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
    }

    static int Main()
    {
        ExperienceCurve();
        StatPoints();
        AccuracyAndDefence();
        ElementChart();
        Ring();
        RarityLadder();

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
