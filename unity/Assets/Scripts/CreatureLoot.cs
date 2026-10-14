using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT A CREATURE'S KIND, LEVEL AND GEAR MAKE IT DROP, as plain numbers, so every rule can be checked by calling it.
/// <see cref="LootDropper"/> turns each answer into the real item and rolls it.
///
/// <para><b>Temper Dust and Quench Oil on every kill</b>, more the higher its level's grade (the seven grades split levels
/// 1 to 99 into seven even stretches; everything above 99, the shadows, is the top grade), its kind and its gear
/// (<see cref="UnitsPerKill"/>). <b>The chance of an Ink or a Salt</b>, rarer ones from elites and shadows. <b>One piece
/// of what it wears</b>, on <see cref="GearDropChance"/> of kills, as a new copy whose rarity its kind rolls
/// (<see cref="DropRarity"/>).</para>
///
/// <para>Every number below is a first guess, to be tuned in play. Design: docs/3-world/mob-roster.md.</para>
/// </summary>
public static class CreatureLoot
{
    /// <summary>How many material grades there are (Raw, F, E, D, C, B, A).</summary>
    public const int Grades = 7;

    /// <summary>The highest creature level on the ordinary ladder; the grades split 1 to this evenly.</summary>
    public const int LadderTop = 99;

    /// <summary>The material grade (1 = Raw … 7 = A) of a creature of <paramref name="level"/>.</summary>
    public static int GradeForLevel(int level) =>
        Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1, level) * (float)Grades / LadderTop), 1, Grades);

    /// <summary>The chance, on every kind of creature, that a kill drops one piece of what it wears.</summary>
    public const float GearDropChance = 0.10f;

    // The rarities a NORMAL creature's dropped piece can roll: Common, Uncommon and Rare, at their shares of the one
    // rarity draw (CraftingMath.RarityAt) — 50, 30 and 14 of 94.
    static readonly int[] NormalDropRarities = { 1, 1, 1, 0, 0 };

    /// <summary>How wide the roll <see cref="DropRarity"/> takes is: 0 up to this, less one.</summary>
    public static int DropRarityRollSize => CraftingMath.RarityDrawTotal(NormalDropRarities);

    /// <summary>The rarity of the piece a creature of <paramref name="kind"/> drops, for a roll of 0 up to
    /// <see cref="DropRarityRollSize"/> − 1: an elite's is always Epic, a shadow's always Legendary, and a normal
    /// creature's Common, Uncommon or Rare by their shares. What a creature WEARS is always Common.</summary>
    public static Rarity DropRarity(CreatureKind kind, int roll)
    {
        if (kind == CreatureKind.Shadow) return Rarity.Legendary;
        if (kind == CreatureKind.Elite) return Rarity.Epic;
        return CraftingMath.RarityAt(NormalDropRarities, roll);
    }

    /// <summary>Hundredths of a unit of Temper Dust a kill drops for each grade of its creature's level, before its kind
    /// and its gear multiply it.</summary>
    public const int TemperDustPerGrade = 60;
    /// <summary>The same for Quench Oil.</summary>
    public const int QuenchOilPerGrade = 6;

    /// <summary>How many units of a material dropped at <paramref name="perGradeHundredths"/> a grade one kill gives up:
    /// that many hundredths for each grade of <paramref name="fileLevel"/> (<see cref="GradeForLevel"/>), times the kind's
    /// multiple (<see cref="CreatureKinds.ExperienceMultiplier"/>), times what its gear pays
    /// (<see cref="CreaturePay.Scaled"/>) — whole units, and one more when <paramref name="roll0To99"/> falls under what
    /// is left over. <paramref name="fileLevel"/> is the level in the creature's FILE, never a level it grew by hitting
    /// people.</summary>
    public static int UnitsPerKill(int perGradeHundredths, int fileLevel, CreatureKind kind, Quality gearGrade, int gearFolds,
                                   int roll0To99)
    {
        // The amount is whole units plus a chance of one more, so it is kept in hundredths of a unit.
        long hundredths = HundredthsPerKill(perGradeHundredths, fileLevel, kind, gearGrade, gearFolds);
        long units = hundredths / 100;
        if (roll0To99 < hundredths % 100) units++;
        return (int)System.Math.Min(int.MaxValue, units);
    }

    /// <summary>What one kill gives up of a material dropped at <paramref name="perGradeHundredths"/> a grade, in
    /// HUNDREDTHS of a unit: 60 is a 60% chance of one, 2,540 is 25 and a 40% chance of one more. The one sum
    /// <see cref="UnitsPerKill"/> rolls from and the drop listing reads, so the two cannot disagree.</summary>
    public static long HundredthsPerKill(int perGradeHundredths, int fileLevel, CreatureKind kind, Quality gearGrade, int gearFolds) =>
        CreaturePay.Scaled((long)perGradeHundredths * GradeForLevel(fileLevel) * CreatureKinds.ExperienceMultiplier(kind),
                           gearGrade, gearFolds);

    /// <summary>One thing a kill may give up: an Ink or a Salt of tier <see cref="step"/> (1 up to 5 or 3).</summary>
    public readonly struct Line
    {
        public readonly MaterialKind kind;
        public readonly int step;
        public readonly float chance;
        public readonly int min, max;
        /// <summary>The lowest grade of creature level (1 = Raw … 7 = A, <see cref="GradeForLevel"/>) that drops this
        /// line; any creature at or above it does. A SALT OR AN INK DROPS FROM THE LEVELS IT IS USED AT AND ABOVE (decided
        /// 2026-10-14): priced at the level it is cheapest to use at, a Pure Salt from a level-1 creature would be worth a
        /// thousand of its kills.</summary>
        public readonly int fromGrade;

        public Line(MaterialKind kind, int step, float chance, int min, int max, int fromGrade = 1)
        {
            this.kind = kind; this.step = step; this.chance = chance; this.min = min; this.max = max;
            this.fromGrade = fromGrade;
        }
    }

    /// <summary>Does a creature of <paramref name="fileLevel"/> drop <paramref name="line"/> at all.</summary>
    public static bool DropsAt(in Line line, int fileLevel) => GradeForLevel(fileLevel) >= line.fromGrade;

    // The grade each Salt and each Ink starts dropping from — the grade it is used at: Coarse from F, Fine from C, Pure
    // from B; Watery from F, Thin from D, Faint from C, Clear from B, Sharp from A.
    const int CoarseFrom = 2, FineFrom = 5, PureFrom = 6;
    const int WateryFrom = 2, ThinFrom = 4, FaintFrom = 5, ClearFrom = 6, SharpFrom = 7;

    static readonly Line[] NormalLines =
    {
        new Line(MaterialKind.Ink,     1, 0.04f, 1, 1, WateryFrom),   // Watery
        new Line(MaterialKind.Ink,     2, 0.01f, 1, 1, ThinFrom),   // Thin
        new Line(MaterialKind.Salt,    1, 0.10f, 1, 1, CoarseFrom),   // Coarse
        new Line(MaterialKind.Salt,    2, 0.005f, 1, 1, FineFrom),  // Fine
        new Line(MaterialKind.Salt,    3, 0.001f, 1, 1, PureFrom),  // Pure
    };

    static readonly Line[] EliteLines =
    {
        new Line(MaterialKind.Ink,     3, 0.30f, 1, 1, FaintFrom),   // Faint
        new Line(MaterialKind.Ink,     4, 0.15f, 1, 1, ClearFrom),   // Clear
        new Line(MaterialKind.Ink,     5, 0.05f, 1, 1, SharpFrom),   // Sharp
        new Line(MaterialKind.Salt,    2, 0.20f, 1, 1, FineFrom),   // Fine
        new Line(MaterialKind.Salt,    3, 0.05f, 1, 1, PureFrom),   // Pure
    };

    static readonly Line[] ShadowLines =
    {
        new Line(MaterialKind.Ink,     3, 0.60f, 1, 1, FaintFrom),   // Faint
        new Line(MaterialKind.Ink,     4, 0.30f, 1, 1, ClearFrom),   // Clear
        new Line(MaterialKind.Ink,     5, 0.15f, 1, 1, SharpFrom),   // Sharp
        new Line(MaterialKind.Salt,    2, 0.40f, 1, 1, FineFrom),   // Fine
        new Line(MaterialKind.Salt,    3, 0.15f, 1, 1, PureFrom),   // Pure
    };

    /// <summary>Every Ink and Salt line a creature of <paramref name="kind"/> rolls on death.</summary>
    public static IReadOnlyList<Line> LinesFor(CreatureKind kind) =>
        kind == CreatureKind.Shadow ? ShadowLines : kind == CreatureKind.Elite ? EliteLines : NormalLines;

    /// <summary>The tier a line's material is taken from: its own, or the creature's grade for a line with none.</summary>
    public static int StepFor(in Line line, int creatureLevel) =>
        line.step > 0 ? line.step : GradeForLevel(creatureLevel);
}
