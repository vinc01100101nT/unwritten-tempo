using UnityEngine;

/// <summary>
/// THE MATERIAL ONLY ONE CREATURE DROPS — what it sells for, how often it falls and how much of it, as plain numbers,
/// so every rule can be checked by calling it. A creature's file names its ONE material (<see cref="MonsterDef.material"/>);
/// <see cref="LootDropper"/> rolls it and <see cref="CreatureDrops"/> lists it, both from here. Design:
/// docs/3-world/creature-materials.md.
///
/// <para><b>A ×1 kill is worth <see cref="GoldPerLevel"/> gold × the creature's level, and no kill drops gold.</b> THE
/// CREATURE'S MULTIPLIER DECIDES ITS MATERIAL'S TIER (<see cref="TierOfMultiplier"/>), and every tier is priced so a ×1
/// kill's material is worth that same 10 × level on average, whatever the tier: rarity changes how the value arrives,
/// never how much. The multiplier multiplies how many fall (<see cref="ExpectedPieces"/>), so a kill is worth 10 × level
/// × its multiplier.</para>
/// </summary>
public static class CreatureMaterials
{
    /// <summary>What a normal kill is worth on average, per creature level.</summary>
    public const int GoldPerLevel = 10;

    // THE MULTIPLIER EACH TIER OPENS FROM, Common … Legendary (decided 2026-10-20): ×1, ×3, ×10, ×30, ×90.
    // tools/check-creature-materials.py reads this line.
    static readonly float[] TierFromMultiplier = { 1f, 3f, 10f, 30f, 90f };

    /// <summary>The tier a <paramref name="kind"/> of <paramref name="multiplier"/> drops its own material at: the highest
    /// tier its multiplier opens — a shadow's ×90 and above opens Legendary.</summary>
    public static bool TierOfMultiplier(CreatureKind kind, float multiplier, out Rarity tier)
    {
        tier = Rarity.Common;
        float m = CreaturePay.Clamp(multiplier);
        for (int t = TierFromMultiplier.Length - 1; t >= 0; t--)
            if (m >= TierFromMultiplier[t]) { tier = (Rarity)t; break; }
        return true;
    }

    /// <summary>The tier a material's saved step stands for: 1 is Common … 5 is Legendary. 0 = nobody chose one.</summary>
    public static Rarity TierOfStep(int step) => (Rarity)Mathf.Clamp(step - 1, (int)Rarity.Common, (int)Rarity.Legendary);

    /// <summary>The step a tier is saved as on its item.</summary>
    public static int StepOfTier(Rarity tier) => (int)tier + 1;

    /// <summary>Is <paramref name="item"/> a creature's own material with a tier chosen.</summary>
    public static bool Is(Item item) => item != null && item.materialKind == MaterialKind.Creature && item.materialStep >= 1;

    /// <summary>How many pieces of its material of <paramref name="tier"/> one kill of a <paramref name="kind"/> of
    /// <paramref name="multiplier"/> gives up on average: the tier's ×1 chance times the multiplier — past 1, more than
    /// one piece. A ×1 chance × its tier's price is the same 10 × level at every tier, so a kill's material is worth
    /// 10 × level × its multiplier.</summary>
    public static float ExpectedPieces(CreatureKind kind, Rarity tier, float multiplier)
    {
        return CreaturePay.ExpectedPieces(ChanceAtOne(tier), multiplier);
    }

    static float ChanceAtOne(Rarity tier)
    {
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

    /// <summary>THE TIER AND VALUE <paramref name="d"/>'s own material must have — both worked out from the creature
    /// (its kind and multiplier give the tier, then its level and tier give what a shop pays), never typed. The value is
    /// what pays exactly that once the Tax is taken. False when the creature has no material of its own.</summary>
    public static bool Derive(MonsterDef d, out int step, out int value)
    {
        step = 0; value = 0;
        if (d == null || !TierOfMultiplier(d.kind, d.multiplier, out Rarity tier)) return false;
        step = StepOfTier(tier);
        value = ShopMath.ValueForSellPrice(SellValue(d.level, tier));
        return true;
    }

    /// <summary>What a shop pays for one material of <paramref name="tier"/> from a creature of <paramref name="level"/>
    /// — the same multiple of level whatever the creature's kind or multiplier.</summary>
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

}
