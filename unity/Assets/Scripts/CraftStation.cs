/// <summary>
/// WHERE A PIECE IS WORKED. One station in town, the smith, works every piece — metal, cloth and leather alike — with
/// the same four actions: fold, quench, Ink and recycle. <see cref="None"/> is an ordinary townsperson, who only talks.
///
/// <para>2 and 3 were the loom (cloth) and the tannery (leather), merged into the smith on 2026-10-17. Never give
/// either number to a new station: a townsperson saved with one would silently become it.</para>
/// </summary>
public enum CraftStation { None = 0, Smith = 1 }

/// <summary>Which station works on which piece, and which material a piece is made of.</summary>
public static class CraftStations
{
    /// <summary>The station that works on <paramref name="item"/>, or <see cref="CraftStation.None"/> for anything
    /// that is not made at one. Read off the item's own material: a piece is worked where its material is worked.
    /// </summary>
    public static CraftStation For(Item item)
    {
        if (item == null || item.slot == EquipSlotType.None) return CraftStation.None;
        switch (item.madeOf)
        {
            case MaterialKind.Metal:
            case MaterialKind.Cloth:
            case MaterialKind.Leather: return CraftStation.Smith;
            default:                   return CraftStation.None;
        }
    }

    /// <summary>The bag position of the strongest material of <paramref name="kind"/> carried (highest step, the first
    /// of equals), or -1 — the Salt a quench offers and the Ink a write uses, on the server and in the window alike.</summary>
    public static int StrongestOfKind(System.Collections.Generic.List<BagEntry> bag, MaterialKind kind)
    {
        int best = -1, bestTier = 0;
        if (bag == null) return best;
        for (int i = 0; i < bag.Count; i++)
        {
            var e = bag[i];
            if (e == null || e.def == null || e.def.materialKind != kind) continue;
            if (e.def.materialStep > bestTier) { bestTier = e.def.materialStep; best = i; }
        }
        return best;
    }

    /// <summary>What a station is called on screen.</summary>
    public static string Label(CraftStation s)
    {
        switch (s)
        {
            case CraftStation.Smith:   return "Smith";
            default:                   return "";
        }
    }
}
