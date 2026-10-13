using UnityEngine;

/// <summary>
/// THE MATERIAL ONLY ONE CREATURE DROPS — what it sells for, how often it falls and how much of it, as plain numbers,
/// so every rule can be checked by calling it. A creature's file names its materials (<see cref="MonsterDef.material"/>,
/// <see cref="MonsterDef.rareMaterial"/>); <see cref="LootDropper"/> rolls them and <see cref="CreatureDrops"/> lists
/// them, both from here. Design: docs/3-world/creature-materials.md.
///
/// <para><b>A kill is worth <see cref="GoldPerLevel"/> gold × the creature's level, and no kill drops gold</b>: the
/// ordinary material falls on half of a normal creature's kills, so one sells for twice that. A rare material's tier
/// is the only setting it has — its chance and its price both follow from it.</para>
/// </summary>
public static class CreatureMaterials
{
    /// <summary>What a normal kill is worth on average, per creature level.</summary>
    public const int GoldPerLevel = 10;

    /// <summary>How many separate piles of its ordinary material an elite gives up, and how many are in each. Never
    /// scaled by how many players fought it.</summary>
    public const int ElitePilesMin = 3, ElitePilesMax = 5, ElitePileMin = 4, ElitePileMax = 7;

    /// <summary>The tier a material's saved step stands for: 1 is Common … 5 is Legendary. 0 = nobody chose one.</summary>
    public static Rarity TierOfStep(int step) => (Rarity)Mathf.Clamp(step - 1, (int)Rarity.Common, (int)Rarity.Legendary);

    /// <summary>The step a tier is saved as on its item.</summary>
    public static int StepOfTier(Rarity tier) => (int)tier + 1;

    /// <summary>Is <paramref name="item"/> a creature's own material with a tier chosen.</summary>
    public static bool Is(Item item) => item != null && item.materialKind == MaterialKind.Creature && item.materialStep >= 1;

    /// <summary>The chance (0–1) one kill of a <paramref name="kind"/> gives up its material of <paramref name="tier"/>.
    /// A normal creature's rare tiers are each a different length of hunt; an elite comes back so seldom that its
    /// chances are per elite kill, and it has no Uncommon. A shadow has no material of its own here.</summary>
    public static float Chance(CreatureKind kind, Rarity tier)
    {
        if (kind == CreatureKind.Elite)
            switch (tier)
            {
                case Rarity.Common:    return 1f;
                case Rarity.Rare:      return 0.5f;
                case Rarity.Epic:      return 0.2f;
                case Rarity.Legendary: return 0.05f;
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
            case Rarity.Uncommon:  perLevel = 25; break;
            case Rarity.Rare:      perLevel = 125; break;
            case Rarity.Epic:      perLevel = 500; break;
            case Rarity.Legendary: perLevel = 2500; break;
            default:               perLevel = 2 * GoldPerLevel; break;
        }
        return (int)System.Math.Min(int.MaxValue, perLevel * System.Math.Max(1, level));
    }

    /// <summary>Does a kill of a <paramref name="kind"/> give up its material of <paramref name="tier"/> as several
    /// separate piles — only an elite's ordinary material does, so everyone who brought it down can reach for one.</summary>
    public static bool InPiles(CreatureKind kind, Rarity tier) => kind == CreatureKind.Elite && tier == Rarity.Common;

    /// <summary>How many piles that is, fewest to most; one for everything that is not <see cref="InPiles"/>.</summary>
    public static void Piles(CreatureKind kind, Rarity tier, out int min, out int max)
    {
        bool piles = InPiles(kind, tier);
        min = piles ? ElitePilesMin : 1;
        max = piles ? ElitePilesMax : 1;
    }

    /// <summary>How many are in each pile, fewest to most.</summary>
    public static void PileSize(CreatureKind kind, Rarity tier, out int min, out int max)
    {
        bool piles = InPiles(kind, tier);
        min = piles ? ElitePileMin : 1;
        max = piles ? ElitePileMax : 1;
    }
}
