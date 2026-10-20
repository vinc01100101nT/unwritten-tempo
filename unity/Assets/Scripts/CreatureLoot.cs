using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT A CREATURE WEARS AND WHAT IT DROPS, as plain numbers, so every rule can be checked by calling it.
/// <see cref="LootDropper"/> turns each answer into the real item and rolls it.
///
/// <para><b>What it wears</b>: pieces of the grade its LEVEL gives (<see cref="WornGrade"/>) — the gear a player of that
/// level is expected to have — at folds rolled each life. <b>What it drops</b> is decided by its typed MULTIPLIER alone
/// (<see cref="MonsterDef.multiplier"/>), never by its level or its gear: Temper Dust and Quench Oil on every kill
/// (<see cref="UnitsPerKill"/>), and the chance of an Ink or a Salt, the rarer ones opening only from a high enough
/// multiplier (<see cref="Lines"/>). <b>One piece of what it wears</b>, on <see cref="GearDropChance"/> of kills, as a new
/// copy with its base rolled 85% to 115%.</para>
///
/// <para>Every number below is a first guess, to be tuned in play. Design: docs/3-world/mob-roster.md.</para>
/// </summary>
public static class CreatureLoot
{
    /// <summary>How many material grades there are (Raw, F, E, D, C, B, A).</summary>
    public const int Grades = 7;

    /// <summary>The highest level on the ordinary ladder; the grades split 1 to this evenly.</summary>
    public const int LadderTop = 99;

    /// <summary>THE GRADE A CREATURE OF <paramref name="level"/> WEARS — the gear a player of that level is expected to
    /// have: levels 1 to 99 split into seven even stretches, 1–14 Raw, 15–28 F, 29–42 E, 43–56 D, 57–70 C, 71–84 B,
    /// 85 and up A (decided 2026-10-20).</summary>
    public static Quality WornGrade(int level) =>
        (Quality)(Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1, level) * (float)Grades / LadderTop), 1, Grades) - 1);

    /// <summary>The chance, on every kind of creature, that a kill drops one piece of what it wears.</summary>
    public const float GearDropChance = 0.10f;

    /// <summary>Hundredths of a unit of Temper Dust a ×1 kill drops; its multiplier multiplies it.</summary>
    public const int TemperDustPerKill = 60;
    /// <summary>The same for Quench Oil — a thirtieth of the Dust, the share a fold-and-quench climb uses (decided
    /// 2026-10-20: at 1 in 10 the Oil piled up while the Dust ran out).</summary>
    public const int QuenchOilPerKill = 2;

    /// <summary>How many units of a material dropped at <paramref name="perKillHundredths"/> one kill gives up:
    /// <see cref="HundredthsPerKill"/> as whole units, and one more when <paramref name="roll0To99"/> falls under what is
    /// left over.</summary>
    public static int UnitsPerKill(int perKillHundredths, float multiplier, int roll0To99)
    {
        // The amount is whole units plus a chance of one more, so it is kept in hundredths of a unit.
        long hundredths = HundredthsPerKill(perKillHundredths, multiplier);
        long units = hundredths / 100;
        if (roll0To99 < hundredths % 100) units++;
        return (int)System.Math.Min(int.MaxValue, units);
    }

    /// <summary>What one kill gives up of a material dropped at <paramref name="perKillHundredths"/>, in HUNDREDTHS of a
    /// unit: that many times the creature's multiplier (<see cref="CreaturePay.Scaled"/>). 60 is a 60% chance of one,
    /// 2,540 is 25 and a 40% chance of one more. The one sum <see cref="UnitsPerKill"/> rolls from and the drop listing
    /// reads, so the two cannot disagree.</summary>
    public static long HundredthsPerKill(int perKillHundredths, float multiplier) =>
        CreaturePay.Scaled(perKillHundredths, multiplier);

    /// <summary>One thing a kill may give up: an Ink or a Salt of tier <see cref="step"/> (1 up to 5 or 3).</summary>
    public readonly struct Line
    {
        public readonly MaterialKind kind;
        public readonly int step;
        /// <summary>The chance a ×1 kill drops one; the multiplier multiplies it, and past 1 it is more than one.</summary>
        public readonly float chance;
        /// <summary>The lowest multiplier that drops this line at all; anything from it up does.</summary>
        public readonly float fromMultiplier;

        public Line(MaterialKind kind, int step, float chance, float fromMultiplier)
        {
            this.kind = kind; this.step = step; this.chance = chance; this.fromMultiplier = fromMultiplier;
        }
    }

    /// <summary>Does a creature of <paramref name="multiplier"/> drop <paramref name="line"/> at all.</summary>
    public static bool DropsAt(in Line line, float multiplier) => CreaturePay.Clamp(multiplier) >= line.fromMultiplier;

    /// <summary>How many of <paramref name="line"/> a kill of a creature of <paramref name="multiplier"/> gives up on
    /// average: 0 below where it opens, else its ×1 chance times the multiplier (<see cref="CreaturePay.ExpectedPieces"/>).</summary>
    public static float ExpectedAt(in Line line, float multiplier) =>
        DropsAt(line, multiplier) ? CreaturePay.ExpectedPieces(line.chance, multiplier) : 0f;

    // THE MULTIPLIER EACH SALT AND INK OPENS FROM (decided 2026-10-20): Coarse and Watery from ×1, Fine and Thin from ×3,
    // Faint from ×10, Pure and Clear from ×30, Sharp from ×90. Every chance is what a ×1 kill would get; the ones that
    // open higher are sized so the multiplier they open at gets what that kind of creature got before (Sharp 15% at ×90).
    static readonly Line[] AllLines =
    {
        new Line(MaterialKind.Ink,  1, 0.04f,        1f),    // Watery
        new Line(MaterialKind.Ink,  2, 0.01f,        3f),    // Thin
        new Line(MaterialKind.Ink,  3, 0.01f,        10f),   // Faint
        new Line(MaterialKind.Ink,  4, 0.005f,       30f),   // Clear
        new Line(MaterialKind.Ink,  5, 0.15f / 90f,  90f),   // Sharp
        new Line(MaterialKind.Salt, 1, 0.10f,        1f),    // Coarse
        new Line(MaterialKind.Salt, 2, 0.005f,       3f),    // Fine
        new Line(MaterialKind.Salt, 3, 0.001f,       30f),   // Pure
    };

    /// <summary>Every Ink and Salt line a creature rolls on death, each gated and scaled by its multiplier.</summary>
    public static IReadOnlyList<Line> Lines => AllLines;

    // ---- what a creature wears, rolled each life ------------------------------------------------------------
    // Folds a creature's worn pieces take, in thousandths per fold count from the kind's lowest: each fold about 70%
    // as likely as the one before. Normal 0..10, elite 4..10, shadow 7..10.
    static readonly int[] NormalFolds = { 300, 210, 150, 100, 70, 50, 40, 30, 20, 15, 15 };
    static readonly int[] EliteFolds  = { 350, 240, 170, 110, 70, 40, 20 };
    static readonly int[] ShadowFolds = { 450, 300, 150, 100 };

    static int[] FoldTable(CreatureKind kind) =>
        kind == CreatureKind.Shadow ? ShadowFolds : kind == CreatureKind.Elite ? EliteFolds : NormalFolds;

    /// <summary>The fewest folds a creature of <paramref name="kind"/> wears: 0, 4 or 7. What its sheet shows its gear
    /// at, so the sheet never depends on a roll.</summary>
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
