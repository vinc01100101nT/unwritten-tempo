/// <summary>
/// WHERE A PIECE IS MADE. Each craft has its own station in town and its own materials: a smith forges metal, a
/// loom weaves cloth, a tannery tans leather. <see cref="None"/> is an ordinary townsperson, who only talks.
///
/// <para>The three actions are the same at every station — forge, fold, quench — and so is Ink. What changes is
/// which pieces a station will take, and which material band those pieces fold with.</para>
/// </summary>
public enum CraftStation { None = 0, Smith = 1, Loom = 2, Tannery = 3 }

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
            case MaterialKind.Metal:   return CraftStation.Smith;
            case MaterialKind.Cloth:   return CraftStation.Loom;
            case MaterialKind.Leather: return CraftStation.Tannery;
            default:                   return CraftStation.None;
        }
    }

    /// <summary>The material kind a station works in.</summary>
    public static MaterialKind Works(CraftStation s)
    {
        switch (s)
        {
            case CraftStation.Smith:   return MaterialKind.Metal;
            case CraftStation.Loom:    return MaterialKind.Cloth;
            case CraftStation.Tannery: return MaterialKind.Leather;
            default:                   return MaterialKind.None;
        }
    }

    /// <summary>What a station is called on screen.</summary>
    public static string Label(CraftStation s)
    {
        switch (s)
        {
            case CraftStation.Smith:   return "Smith";
            case CraftStation.Loom:    return "Loom";
            case CraftStation.Tannery: return "Tannery";
            default:                   return "";
        }
    }
}
