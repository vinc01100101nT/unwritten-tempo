using UnityEngine;

/// <summary>
/// THE MATERIAL ONLY ONE CREATURE DROPS — what it sells for, how often it falls and how much of it, as plain numbers,
/// so every rule can be checked by calling it. A creature's file names its ONE material (<see cref="MonsterDef.material"/>);
/// <see cref="LootDropper"/> rolls it and <see cref="CreatureDrops"/> lists it, both from here. Design:
/// docs/3-world/creature-materials.md.
///
/// <para><b>A kill is worth <see cref="GoldPerLevel"/> gold × the creature's level, and no kill drops gold.</b> THE
/// CREATURE'S GRADE DECIDES ITS MATERIAL'S TIER (<see cref="TierOfGrade"/>), and every tier is priced so a normal kill's
/// material is worth that same 10 × level on average, whatever the tier: rarity changes how the value arrives, never
/// how much. An elite kill is worth <see cref="EliteKillsWorth"/> normal kills, at every tier.</para>
/// </summary>
public static class CreatureMaterials
{
    /// <summary>What a normal kill is worth on average, per creature level.</summary>
    public const int GoldPerLevel = 10;

    /// <summary>How many normal kills of its level one elite kill's material is worth.</summary>
    public const int EliteKillsWorth = 100;

    /// <summary>How many separate piles of one each a grade-D elite's Rare material falls as. Never scaled by how many
    /// players fought it.</summary>
    public const int EliteRarePiles = 2;

    // THE TIER EACH GRADE GIVES ITS CREATURE'S MATERIAL, indexed by (int)Quality: Raw, F, E, D, C, B, A. -1 = no
    // creature of that kind may wear that grade: an elite is never below D, and nothing is at A but a shadow, which has
    // no material. tools/check-creature-materials.py reads these two lines.
    static readonly int[] NormalTierByGrade = { 0, 1, 2, 3, 4, -1, -1 };
    static readonly int[] EliteTierByGrade  = { -1, -1, -1, 2, 3, 4, -1 };

    /// <summary>The tier a <paramref name="kind"/> in <paramref name="grade"/> gear drops its material at. False when
    /// that kind may not wear that grade, or has no material of its own (a shadow).</summary>
    public static bool TierOfGrade(CreatureKind kind, Quality grade, out Rarity tier)
    {
        int[] table = kind == CreatureKind.Normal ? NormalTierByGrade : kind == CreatureKind.Elite ? EliteTierByGrade : null;
        int g = (int)grade, t = table != null && g >= 0 && g < table.Length ? table[g] : -1;
        tier = t < 0 ? Rarity.Common : (Rarity)t;
        return t >= 0;
    }

    /// <summary>The tier a material's saved step stands for: 1 is Common … 5 is Legendary. 0 = nobody chose one.</summary>
    public static Rarity TierOfStep(int step) => (Rarity)Mathf.Clamp(step - 1, (int)Rarity.Common, (int)Rarity.Legendary);

    /// <summary>The step a tier is saved as on its item.</summary>
    public static int StepOfTier(Rarity tier) => (int)tier + 1;

    /// <summary>Is <paramref name="item"/> a creature's own material with a tier chosen.</summary>
    public static bool Is(Item item) => item != null && item.materialKind == MaterialKind.Creature && item.materialStep >= 1;

    /// <summary>The chance (0–1) one kill of a <paramref name="kind"/> gives up its material of <paramref name="tier"/>.
    /// A normal creature's chance × its tier's price is the same 10 × level at every tier. An elite comes back so seldom
    /// that its chances are per elite kill and sized to <see cref="EliteKillsWorth"/> normal kills: a sure 2 Rare, a 1 in 2
    /// Epic, a 1 in 10 Legendary. A shadow has no material of its own here.</summary>
    public static float Chance(CreatureKind kind, Rarity tier)
    {
        if (kind == CreatureKind.Elite)
            switch (tier)
            {
                case Rarity.Rare:      return 1f;
                case Rarity.Epic:      return 0.5f;
                case Rarity.Legendary: return 0.1f;
                default:               return 0f;
            }
        if (kind == CreatureKind.Shadow) return 0f;
        switch (tier)
        {
            case Rarity.Common:    return 0.5f;
            case Rarity.Uncommon:  return 0.1f;
            case Rarity.Rare:      return 0.02f;
            case Rarity.Epic:      return 0.005f;
            case Rarity.Legendary: return 0.001f;
            default:               return 0f;
        }
    }

    /// <summary>What a shop pays for one material of <paramref name="tier"/> from a creature of <paramref name="level"/>
    /// wearing gear of <paramref name="gearGrade"/> with <paramref name="gearFolds"/> folds — the same multiple of
    /// level whichever kind of creature dropped it, times what that gear pays (<see cref="CreaturePay"/>).</summary>
    public static int SellValue(int level, Rarity tier, Quality gearGrade, int gearFolds)
        => (int)System.Math.Min(int.MaxValue, CreaturePay.Scaled(SellValue(level, tier), gearGrade, gearFolds));

    /// <summary>The same for a creature in Raw gear with no folds — the price before gear.</summary>
    public static int SellValue(int level, Rarity tier)
    {
        long perLevel;
        switch (tier)
        {
            case Rarity.Uncommon:  perLevel = 100; break;
            case Rarity.Rare:      perLevel = 500; break;
            case Rarity.Epic:      perLevel = 2000; break;
            case Rarity.Legendary: perLevel = 10000; break;
            default:               perLevel = 2 * GoldPerLevel; break;
        }
        return (int)System.Math.Min(int.MaxValue, perLevel * System.Math.Max(1, level));
    }

    /// <summary>Does a kill of a <paramref name="kind"/> give up its material of <paramref name="tier"/> as several
    /// separate piles of one each — only a grade-D elite's Rare does, so the two fall apart on the ground.</summary>
    public static bool InPiles(CreatureKind kind, Rarity tier) => kind == CreatureKind.Elite && tier == Rarity.Rare;

    /// <summary>How many piles that is, fewest to most; one for everything that is not <see cref="InPiles"/>.</summary>
    public static void Piles(CreatureKind kind, Rarity tier, out int min, out int max)
    {
        min = max = InPiles(kind, tier) ? EliteRarePiles : 1;
    }

    /// <summary>How many are in each pile, fewest to most: always one.</summary>
    public static void PileSize(CreatureKind kind, Rarity tier, out int min, out int max)
    {
        min = max = 1;
    }
}
