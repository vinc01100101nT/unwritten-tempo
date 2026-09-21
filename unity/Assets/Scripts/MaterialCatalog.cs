using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHICH MATERIAL IS WHICH — the one place a craft and a band number are turned into the actual item. Built once
/// from the item catalog and kept, because a station asks this on every fold and a search through every item
/// would be a search through every item.
/// </summary>
public static class MaterialCatalog
{
    // [craft][band] → the item. Bands are 1..7, so the arrays have eight places and place 0 is never used.
    const int Bands = 8;
    static readonly Dictionary<MaterialKind, Item[]> bands = new Dictionary<MaterialKind, Item[]>();
    static bool built;

    /// <summary>The band-<paramref name="band"/> material of <paramref name="craft"/> (Metal, Cloth or Leather),
    /// or null when nobody has authored one.</summary>
    public static Item Band(MaterialKind craft, int band)
    {
        Build();
        if (band < 1 || band >= Bands) return null;
        return bands.TryGetValue(craft, out var row) ? row[band] : null;
    }

    /// <summary>The Ink of <paramref name="tier"/> (1 Watery .. 5 Sharp), or null when nobody has authored one.
    /// Ink is one ladder shared by every craft, so it is keyed by tier alone.</summary>
    public static Item Ink(int tier)
    {
        Build();
        if (tier < 1 || tier >= Bands) return null;
        return bands.TryGetValue(MaterialKind.Ink, out var row) ? row[tier] : null;
    }

    /// <summary>Forget what was found, so a newly authored material is picked up. The editor calls this; the game
    /// never needs to.</summary>
    public static void Forget() { built = false; bands.Clear(); }

    static void Build()
    {
        if (built) return;
        built = true;
        bands.Clear();
        foreach (var it in Resources.LoadAll<Item>("Items"))
        {
            if (it == null) continue;
            // The three band ladders, and Ink's tier ladder beside them (Salt is never handed back, so it is not
            // filed). Ink's five tiers sit in the same eight-place row, which is why the ceiling is shared.
            if (it.materialKind != MaterialKind.Metal && it.materialKind != MaterialKind.Cloth
                && it.materialKind != MaterialKind.Leather && it.materialKind != MaterialKind.Ink) continue;
            if (it.materialStep < 1 || it.materialStep >= Bands) continue;
            if (!bands.TryGetValue(it.materialKind, out var row)) bands[it.materialKind] = row = new Item[Bands];
            row[it.materialStep] = it;
        }
    }
}
