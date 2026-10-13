using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHICH MATERIAL IS WHICH — the one place a material kind and a tier are turned into the actual item. Built once
/// from the item catalog and kept, because a station asks this on every fold and a search through every item
/// would be a search through every item.
/// </summary>
public static class MaterialCatalog
{
    // [kind][tier] → the item. Tiers are 1..5 at most, so eight places are room enough and place 0 is never used.
    const int Bands = 8;
    static readonly Dictionary<MaterialKind, Item[]> bands = new Dictionary<MaterialKind, Item[]>();
    static Item temperDust, quenchOil;
    static bool built;

    /// <summary>Temper Dust — what every fold takes — or null when nobody has authored it.</summary>
    public static Item TemperDust { get { Build(); return temperDust; } }

    /// <summary>Quench Oil — what every quench takes — or null when nobody has authored it.</summary>
    public static Item QuenchOil { get { Build(); return quenchOil; } }

    /// <summary>The Ink of <paramref name="tier"/> (1 Watery .. 5 Sharp), or null when nobody has authored one.
    /// Ink is one ladder shared by every craft, so it is keyed by tier alone.</summary>
    public static Item Ink(int tier)
    {
        Build();
        if (tier < 1 || tier >= Bands) return null;
        return bands.TryGetValue(MaterialKind.Ink, out var row) ? row[tier] : null;
    }

    /// <summary>The Salt of <paramref name="tier"/> (1 Coarse .. 3 Pure), or null when nobody has authored one.</summary>
    public static Item Salt(int tier)
    {
        Build();
        if (tier < 1 || tier >= Bands) return null;
        return bands.TryGetValue(MaterialKind.Salt, out var row) ? row[tier] : null;
    }

    /// <summary>Forget what was found, so a newly authored material is picked up. The editor calls this; the game
    /// never needs to.</summary>
    public static void Forget() { built = false; bands.Clear(); temperDust = null; quenchOil = null; }

    static void Build()
    {
        if (built) return;
        built = true;
        bands.Clear();
        temperDust = null;
        quenchOil = null;
        foreach (var it in Resources.LoadAll<Item>("Items"))
        {
            if (it == null) continue;
            if (it.materialKind == MaterialKind.TemperDust) { temperDust = it; continue; }
            if (it.materialKind == MaterialKind.QuenchOil) { quenchOil = it; continue; }
            // Ink's and Salt's tier ladders, each in an eight-place row.
            if (it.materialKind == MaterialKind.None) continue;
            // A creature's own material is found through its creature, never by kind and step: dozens share each step.
            if (it.materialKind == MaterialKind.Creature) continue;
            if (it.materialStep < 1 || it.materialStep >= Bands) continue;
            if (!bands.TryGetValue(it.materialKind, out var row)) bands[it.materialKind] = row = new Item[Bands];
            row[it.materialStep] = it;
        }
    }
}
