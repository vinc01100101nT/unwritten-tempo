using UnityEngine;

/// <summary>How RARE a thing is to come by. A material is as rare as it is to find; gear and everything else is
/// <see cref="Common"/> — gear has no rarity (decided 2026-10-18).
///
/// <para>⚠️ The numbers are PINNED: the server sends one with every drop. Never reorder; a new rarity takes the next
/// number.</para></summary>
public enum Rarity { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 4 }

/// <summary>
/// THE ONE PLACE that answers "how rare is this", what that rarity is called and what colour it is written in. Every
/// screen that shows an item asks here, so the bag, the tooltip, the floor, the worn list and the shop can never
/// disagree. Gear has none (decided 2026-10-18), so it is Common; a material's is read off its kind and step.
/// </summary>
public static class Rarities
{
    /// <summary>How rare <paramref name="def"/> is. Gear has no rarity, so it is Common; a crafting material goes by
    /// its kind and step (<see cref="OfMaterial"/>); consumables and everything else are Common.</summary>
    public static Rarity Of(Item def)
    {
        if (def == null) return Rarity.Common;
        if (def.materialKind != MaterialKind.None) return OfMaterial(def.materialKind, def.materialStep);
        return Rarity.Common;
    }

    /// <summary>How rare a crafting material of <paramref name="kind"/> at <paramref name="step"/> is. Temper Dust and
    /// Quench Oil are Common. Ink by tier: Watery Uncommon, Thin and Faint Rare, Clear and Sharp Epic. Salt
    /// by tier, at its drop chance (decided 2026-10-14): Coarse Uncommon, Fine Epic, Pure Legendary. A creature's own
    /// material is the tier its step names (<see cref="CreatureMaterials.TierOfStep"/>).</summary>
    public static Rarity OfMaterial(MaterialKind kind, int step)
    {
        switch (kind)
        {
            case MaterialKind.Creature:
                return CreatureMaterials.TierOfStep(step);
            case MaterialKind.Ink:
                return step >= 4 ? Rarity.Epic : step >= 2 ? Rarity.Rare : Rarity.Uncommon;
            case MaterialKind.Salt:
                return step >= 3 ? Rarity.Legendary : step >= 2 ? Rarity.Epic : Rarity.Uncommon;
            default:
                return Rarity.Common;
        }
    }

    /// <summary>The word a rarity goes by on screen.</summary>
    public static string Label(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Uncommon:  return "Uncommon";
            case Rarity.Rare:      return "Rare";
            case Rarity.Epic:      return "Epic";
            case Rarity.Legendary: return "Legendary";
            default:               return "Common";
        }
    }

    /// <summary>The colour an item of <paramref name="rarity"/> has its name written in, on a window or a floor plate.
    /// Common is the ordinary text colour; the rest are the interface theme's own rarity colours.</summary>
    public static Color Color(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Uncommon:  return UiKit.Good;
            case Rarity.Rare:      return UiKit.RarityRare;
            case Rarity.Epic:      return UiKit.RarityEpic;
            case Rarity.Legendary: return UiKit.RarityLegendary;
            default:               return UiKit.TextMain;
        }
    }

    // Rich-text opening tags, one per rarity, built on first use and kept — a tooltip line is coloured with these,
    // never with a string put together per hover. Built late so asking a rarity never wakes the interface theme.
    static string[] tags;

    /// <summary><paramref name="name"/> — a thing's name as a row writes it — in the colour of <paramref name="def"/>'s
    /// rarity, as rich text. Only the name: a price or a count beside it is left in the row's own colour.</summary>
    public static string Coloured(Item def, string name) => Tag(Of(def)) + name + "</color>";

    /// <summary>The rich-text tag that opens text in <paramref name="rarity"/>'s colour — close it with "&lt;/color&gt;".</summary>
    public static string Tag(Rarity rarity)
    {
        if (tags == null)
        {
            tags = new string[(int)Rarity.Legendary + 1];
            for (int r = 0; r < tags.Length; r++) tags[r] = "<color=" + UiKit.Hex(Color((Rarity)r)) + ">";
        }
        return tags[Mathf.Clamp((int)rarity, 0, tags.Length - 1)];
    }
}
