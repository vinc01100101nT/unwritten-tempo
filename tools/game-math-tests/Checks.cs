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
            That(CraftingMath.SkillMasteryCap(upper) > CraftingMath.SkillMasteryCap(lower), CraftF,
                 upper + " caps skill mastery no higher than " + lower);
            That(CraftingMath.TrainingSpeed(upper) > CraftingMath.TrainingSpeed(lower), CraftF,
                 upper + " trains no faster than " + lower);
            That(CraftingMath.Band(upper) == CraftingMath.Band(lower) + 1, CraftF,
                 upper + " does not fold with the band above " + lower + "'s");
        }

        // A fold is never worth less than the folds themselves, and no folds is worth nothing.
        foreach (var grade in grades)
            for (int folds = 0; folds <= CraftingMath.FoldCap(grade); folds++)
            {
                CraftingMath.PieceNumbers(EquipSlotType.Weapon, WeaponType.Sword, folds, grade,
                                          out int atk, out _, out _, out _);
                That(atk >= folds, CraftF, grade + " +" + folds + " is worth " + atk + " attack, below its own folds");
                if (folds == 0) That(atk == 0, CraftF, "an unfolded " + grade + " piece is worth " + atk);
            }

        // Every armour place gains one of the two defences, and a weapon gains one of the two attacks.
        CraftingMath.PieceNumbers(EquipSlotType.Garment, WeaponType.None, 10, Quality.A, out _, out _, out int gDef, out int gMDef);
        That(gDef == 0 && gMDef > 0, CraftF, "a garment's folds no longer go to magic defence alone");
        CraftingMath.PieceNumbers(EquipSlotType.Weapon, WeaponType.Staff, 10, Quality.A, out int sAtk, out int sMAtk, out _, out _);
        That(sAtk == 0 && sMAtk > 0, CraftF, "a caster's weapon no longer folds into magic attack");
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
        // The level a pile of practice buys is exactly the level whose cost it has met, and never more.
        for (int level = 0; level <= 120; level++)
        {
            float cost = MasteryMath.WeaponPointsFor(level);
            That(MasteryMath.WeaponLevelFrom(cost) == level, MasteryF,
                 "level " + level + " costs " + cost + " points, which reads back as level " + MasteryMath.WeaponLevelFrom(cost));
            if (level > 0)
                That(MasteryMath.WeaponLevelFrom(cost - 0.001f) == level - 1, MasteryF,
                     "a hair under level " + level + "'s cost is not level " + (level - 1));
        }
        for (int level = 0; level <= 60; level++)
            That(MasteryMath.SkillLevelFrom(MasteryMath.SkillUsesFor(level)) == level, MasteryF,
                 "skill level " + level + " does not read back from its own cost in uses");

        // THE PROMISED FIGURES: 2,485 points to weapon 70 and 2,325 uses to skill 30.
        That(Math.Abs(MasteryMath.WeaponPointsFor(70) - 2485f) < 0.01f, MasteryF,
             "weapon mastery 70 no longer costs 2,485 points: " + MasteryMath.WeaponPointsFor(70));
        That(Math.Abs(MasteryMath.SkillUsesFor(30) - 2325f) < 0.01f, MasteryF,
             "skill mastery 30 no longer costs 2,325 uses: " + MasteryMath.SkillUsesFor(30));

        // Neither lift ever passes the ceiling, and both rise with the level.
        float lastSkill = -1f, lastPassive = -1f;
        for (int level = 0; level <= 200; level++)
        {
            float skill = MasteryMath.SkillLift(level);
            float passive = MasteryMath.PassiveLift(level);
            That(skill <= MasteryMath.MaxSkillLift + 0.0001f, MasteryF, "skill mastery " + level + " lifts " + skill);
            That(passive <= MasteryMath.MaxSkillLift + 0.0001f, MasteryF, "a passive at mastery " + level + " lifts " + passive);
            That(skill >= lastSkill, MasteryF, "the skill lift falls at level " + level);
            That(passive >= lastPassive, MasteryF, "the passive lift falls at level " + level);
            lastSkill = skill; lastPassive = passive;
        }

        // The challenge scale: nothing far below, a full point at your own level, and a hard ceiling above.
        That(MasteryMath.Challenge(30, 40) == 0f, MasteryF, "a creature ten levels below still teaches");
        That(MasteryMath.Challenge(29, 40) == 0f, MasteryF, "a creature eleven levels below still teaches");
        That(Math.Abs(MasteryMath.Challenge(40, 40) - 1f) < 0.0001f, MasteryF, "an even fight is not worth one point");
        That(Math.Abs(MasteryMath.Challenge(45, 40) - 1f) < 0.0001f, MasteryF, "five levels above is not still an even fight");
        float last = -1f;
        for (int victim = 25; victim <= 120; victim++)
        {
            float c = MasteryMath.Challenge(victim, 40);
            That(c >= last, MasteryF, "the challenge scale falls at a victim level of " + victim);
            That(c <= MasteryMath.MostPerKill + 0.0001f, MasteryF, "a level-" + victim + " kill is worth " + c);
            last = c;
        }

        // A kill pays by the SHARE taken, and a creature is worth the same whatever its health pool.
        That(Math.Abs(MasteryMath.PracticeFromKill(100, 100, 40, 40, Quality.E) - 1f) < 0.0001f, MasteryF,
             "a whole creature taken down alone is not worth one point");
        That(Math.Abs(MasteryMath.PracticeFromKill(50, 100, 40, 40, Quality.E) - 0.5f) < 0.0001f, MasteryF,
             "half a creature is not worth half a point");
        That(Math.Abs(MasteryMath.PracticeFromKill(9900, 10000, 40, 40, Quality.E)
                    - MasteryMath.PracticeFromKill(99, 100, 40, 40, Quality.E)) < 0.0001f, MasteryF,
             "a creature with a bigger health pool pays more for the same share");
        That(MasteryMath.PracticeFromKill(100, 100, 20, 40, Quality.A) == 0f, MasteryF,
             "a creature far below still pays, whatever the weapon");
        That(MasteryMath.PracticeFromKill(100, 100, 40, 40, Quality.A)
             > MasteryMath.PracticeFromKill(100, 100, 40, 40, Quality.Raw), MasteryF,
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
        QualityLadder();
        QuenchOdds();
        InkRoll();
        MasteryCurve();
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
