using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT A CREATURE'S KIND AND LEVEL MAKE IT DROP — the materials half of every kill, as plain numbers, so the rule can be
/// checked by calling it. <see cref="LootDropper"/> turns each line into the real item and rolls it.
///
/// <para><b>The grade it drops follows its level.</b> The seven material grades split levels 1 to 99 into seven even
/// stretches; everything above 99 (the shadows) drops the top grade. A creature drops its own grade's metal, cloth and
/// leather — what a piece of that quality folds with — plus the chance of an Ink or a Salt.</para>
///
/// <para><b>Elites drop much more folding material and the rarer Inks and Salts; a shadow drops what an elite drops, at
/// higher chances.</b> Every number below is a first guess, to be tuned in play. Design: docs/3-world/mob-roster.md.</para>
/// </summary>
public static class CreatureLoot
{
    /// <summary>How many material grades there are (Raw, F, E, D, C, B, A).</summary>
    public const int Grades = 7;

    /// <summary>The highest creature level on the ordinary ladder; the grades split 1 to this evenly.</summary>
    public const int LadderTop = 99;

    /// <summary>The material grade (1 = Raw … 7 = A) a creature of <paramref name="level"/> drops.</summary>
    public static int GradeForLevel(int level) =>
        Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1, level) * (float)Grades / LadderTop), 1, Grades);

    /// <summary>One thing a kill may give up. <see cref="step"/> is the Ink or Salt tier (1 up to 5 or 3); for metal, cloth
    /// and leather it is 0, meaning "the creature's own grade".</summary>
    public readonly struct Line
    {
        public readonly MaterialKind kind;
        public readonly int step;
        public readonly float chance;
        public readonly int min, max;

        public Line(MaterialKind kind, int step, float chance, int min, int max)
        {
            this.kind = kind; this.step = step; this.chance = chance; this.min = min; this.max = max;
        }
    }

    static readonly Line[] NormalLines =
    {
        new Line(MaterialKind.Metal,   0, 0.20f, 1, 1),
        new Line(MaterialKind.Cloth,   0, 0.20f, 1, 1),
        new Line(MaterialKind.Leather, 0, 0.20f, 1, 1),
        new Line(MaterialKind.Ink,     1, 0.04f, 1, 1),   // Watery
        new Line(MaterialKind.Ink,     2, 0.01f, 1, 1),   // Thin
        new Line(MaterialKind.Salt,    1, 0.02f, 1, 1),   // Coarse
    };

    static readonly Line[] EliteLines =
    {
        new Line(MaterialKind.Metal,   0, 0.60f, 5, 10),
        new Line(MaterialKind.Cloth,   0, 0.60f, 5, 10),
        new Line(MaterialKind.Leather, 0, 0.60f, 5, 10),
        new Line(MaterialKind.Ink,     3, 0.30f, 1, 1),   // Faint
        new Line(MaterialKind.Ink,     4, 0.15f, 1, 1),   // Clear
        new Line(MaterialKind.Ink,     5, 0.05f, 1, 1),   // Sharp
        new Line(MaterialKind.Salt,    2, 0.20f, 1, 1),   // Fine
        new Line(MaterialKind.Salt,    3, 0.05f, 1, 1),   // Pure
    };

    static readonly Line[] ShadowLines =
    {
        new Line(MaterialKind.Metal,   0, 1.00f, 5, 10),
        new Line(MaterialKind.Cloth,   0, 1.00f, 5, 10),
        new Line(MaterialKind.Leather, 0, 1.00f, 5, 10),
        new Line(MaterialKind.Ink,     3, 0.60f, 1, 1),   // Faint
        new Line(MaterialKind.Ink,     4, 0.30f, 1, 1),   // Clear
        new Line(MaterialKind.Ink,     5, 0.15f, 1, 1),   // Sharp
        new Line(MaterialKind.Salt,    2, 0.40f, 1, 1),   // Fine
        new Line(MaterialKind.Salt,    3, 0.15f, 1, 1),   // Pure
    };

    /// <summary>Every material line a creature of <paramref name="kind"/> rolls on death.</summary>
    public static IReadOnlyList<Line> LinesFor(CreatureKind kind) =>
        kind == CreatureKind.Shadow ? ShadowLines : kind == CreatureKind.Elite ? EliteLines : NormalLines;

    /// <summary>The grade a line's material is taken from: its own tier for an Ink or a Salt, the creature's grade
    /// otherwise.</summary>
    public static int StepFor(in Line line, int creatureLevel) =>
        line.step > 0 ? line.step : GradeForLevel(creatureLevel);
}
