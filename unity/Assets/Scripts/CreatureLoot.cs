using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT A CREATURE'S KIND, LEVEL AND GEAR MAKE IT DROP, as plain numbers, so every rule can be checked by calling it.
/// <see cref="LootDropper"/> turns each answer into the real item and rolls it.
///
/// <para><b>Temper Dust and Quench Oil on every kill</b>, more the higher its level's grade (the seven grades split levels
/// 1 to 99 into seven even stretches; everything above 99, the shadows, is the top grade), its kind and its gear
/// (<see cref="UnitsPerKill"/>). <b>The chance of an Ink or a Salt</b>, rarer ones from elites and shadows. <b>One piece
/// of what it wears</b>, on <see cref="GearDropChance"/> of kills, as a new copy with its base rolled 85% to 115%.</para>
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

    // ---- what a creature wears, rolled each life ------------------------------------------------------------
    // Folds a creature's worn pieces take, in thousandths per fold count from the kind's lowest: each fold about 70%
    // as likely as the one before. Normal 0..10, elite 4..10, shadow 7..10.
    static readonly int[] NormalFolds = { 300, 210, 150, 100, 70, 50, 40, 30, 20, 15, 15 };
    static readonly int[] EliteFolds  = { 350, 240, 170, 110, 70, 40, 20 };
    static readonly int[] ShadowFolds = { 450, 300, 150, 100 };

    static int[] FoldTable(CreatureKind kind) =>
        kind == CreatureKind.Shadow ? ShadowFolds : kind == CreatureKind.Elite ? EliteFolds : NormalFolds;

    /// <summary>The fewest folds a creature of <paramref name="kind"/> wears: 0, 4 or 7. What its pay and its value are
    /// worked out at, so they never depend on a roll.</summary>
    public static int FileFolds(CreatureKind kind) =>
        kind == CreatureKind.Shadow ? 7 : kind == CreatureKind.Elite ? 4 : 0;

    /// <summary>The folds a worn piece takes for a draw of <paramref name="roll0To999"/>.</summary>
    public static int RollWornFolds(CreatureKind kind, int roll0To999)
    {
        var table = FoldTable(kind);
        int left = Mathf.Clamp(roll0To999, 0, 999);
        for (int i = 0; i < table.Length; i++)
        {
            left -= table[i];
            if (left < 0) return FileFolds(kind) + i;
        }
        return FileFolds(kind) + table.Length - 1;
    }

    /// <summary>The card slots a worn piece in <paramref name="slot"/> takes for a draw of <paramref name="roll0To99"/>:
    /// a weapon 0-2 / 2-3 / 3-4 by kind, evenly; an armour piece 1 on a quarter of normals, 1-2 on elites, 2 on shadows.</summary>
    public static int RollWornCardSlots(EquipSlotType slot, CreatureKind kind, int roll0To99)
    {
        int roll = Mathf.Clamp(roll0To99, 0, 99);
        int cap = CraftingMath.CardSlotCap(slot);
        if (cap == 0) return 0;
        if (slot == EquipSlotType.Weapon)
        {
            int low = kind == CreatureKind.Shadow ? 3 : kind == CreatureKind.Elite ? 2 : 0;
            int high = kind == CreatureKind.Shadow ? 4 : kind == CreatureKind.Elite ? 3 : 2;
            return low + roll * (high - low + 1) / 100;
        }
        if (kind == CreatureKind.Shadow) return 2;
        if (kind == CreatureKind.Elite) return 1 + roll / 50;
        return roll < 25 ? 1 : 0;
    }

    /// <summary>The grade a normal creature of <paramref name="level"/> wears: Raw 1-24, F 25-49, E 50-74, D 75 and up.</summary>
    public static Quality GradeForNormal(int level) =>
        level >= 75 ? Quality.D : level >= 50 ? Quality.E : level >= 25 ? Quality.F : Quality.Raw;

    /// <summary>The Ink a creature's worn pieces are written with, by their grade: Raw none, F Watery up to C Clear,
    /// B and A Sharp.</summary>
    public static int InkTierFor(Quality grade) => Mathf.Min(CraftingMath.TopInkTier, (int)grade);

    // ---- a build that fits the weapon a creature rolled ------------------------------------------------------
    /// <summary>The armour a creature holding <paramref name="weapon"/> wears and how it spends its stat points, as
    /// weights in the order STR, AGI, VIT, INT, DEX, LUK: a caster's weapon (staff, wand, scythe) cloth and INT; a
    /// shooting weapon (bow, sniper, shuriken) or a quick blade (dagger, katar) leather and AGI/DEX; everything
    /// swung for strength metal and STR/VIT. So a creature that rolls a wand never wears plate on a STR build.</summary>
    public static MaterialKind BuildFor(WeaponType weapon, float[] weights)
    {
        switch (weapon)
        {
            case WeaponType.Staff:
            case WeaponType.Wand:
            case WeaponType.Scythe:
                SetWeights(weights, 0f, 0.05f, 0.15f, 0.45f, 0.3f, 0.05f);
                return MaterialKind.Cloth;
            case WeaponType.Bow:
            case WeaponType.Firearm:
            case WeaponType.Shuriken:
                SetWeights(weights, 0.1f, 0.3f, 0.05f, 0f, 0.45f, 0.1f);
                return MaterialKind.Leather;
            case WeaponType.Dagger:
            case WeaponType.Katar:
                SetWeights(weights, 0.1f, 0.45f, 0.05f, 0f, 0.25f, 0.15f);
                return MaterialKind.Leather;
            default:
                SetWeights(weights, 0.45f, 0.1f, 0.25f, 0f, 0.15f, 0.05f);
                return MaterialKind.Metal;
        }
    }

    static void SetWeights(float[] w, float str, float agi, float vit, float intel, float dex, float luk)
    {
        if (w == null || w.Length < 6) return;
        w[0] = str; w[1] = agi; w[2] = vit; w[3] = intel; w[4] = dex; w[5] = luk;
    }
}
