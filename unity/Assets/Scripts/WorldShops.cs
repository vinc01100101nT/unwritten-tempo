using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EVERY SHOP IN THE WORLD — the one list of what each one sells and whether it buys. A shopkeeper is a townsperson
/// whose row in <see cref="WorldTownsfolk"/> names a shop from here by its id, so every town's tool dealer can share
/// one stock list and changing what they sell never means touching the person.
///
/// <para>Lives at <c>Resources/WorldShops</c>. What a thing costs is the item's own <see cref="Item.buyPrice"/>, and
/// what a shop pays for one is its <see cref="Item.zeny"/>: a price is a fact about the item, written once, and the
/// server is the only one that multiplies it by a count.</para>
/// </summary>
[CreateAssetMenu(fileName = "WorldShops", menuName = "unwritten/World Shops", order = 4)]
public class WorldShops : ScriptableObject
{
    public const string ResourcePath = "WorldShops";

    [System.Serializable]
    public class Row
    {
        [Tooltip("What townsfolk rows call this shop by, e.g. 'tool-dealer'.")]
        public string id = "";
        [Tooltip("The item ids it sells, in the order they are listed. An item with no buy price is left out.")]
        public List<string> stock = new List<string>();
        [Tooltip("Whether it buys what players carry: anything whose sell value is above 0.")]
        public bool buysLoot;
    }

    public List<Row> rows = new List<Row>();

    static WorldShops loaded;
    static bool looked;

    /// <summary>The list, loaded once. Null when it has not been made — no shop then sells or buys anything.</summary>
    public static WorldShops Instance
    {
        get
        {
            if (!looked) { looked = true; loaded = Resources.Load<WorldShops>(ResourcePath); }
            return loaded;
        }
    }

    /// <summary>The shop called <paramref name="id"/>, or null.</summary>
    public static Row Find(string id)
    {
        var list = Instance;
        if (list == null || string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < list.rows.Count; i++)
            if (list.rows[i] != null && list.rows[i].id == id) return list.rows[i];
        return null;
    }

    /// <summary>Does <paramref name="shop"/> sell <paramref name="def"/>: it is on the stock list, has a price, and is not
    /// gear — gear is never bought (decided 2026-09-20).</summary>
    public static bool Sells(Row shop, Item def)
    {
        if (shop == null || def == null || def.buyPrice <= 0 || def.slot != EquipSlotType.None) return false;
        for (int i = 0; i < shop.stock.Count; i++)
            if (ItemCatalog.Resolve(shop.stock[i]) == def) return true;
        return false;
    }

    /// <summary>Does any shop in the world sell <paramref name="def"/>.</summary>
    public static bool AnySells(Item def)
    {
        var list = Instance;
        if (list == null || def == null) return false;
        for (int i = 0; i < list.rows.Count; i++) if (Sells(list.rows[i], def)) return true;
        return false;
    }

    /// <summary>Would <paramref name="shop"/> buy this stack: it buys at all, and the thing has a sell value. ONE RULE,
    /// whatever kind of item it is: a sell value of 0 is what makes a thing unsellable (<see cref="Item.CanBeSold"/>).</summary>
    public static bool Buys(Row shop, BagEntry e)
        => shop != null && shop.buysLoot && e != null && e.def != null && e.count > 0 && e.def.CanBeSold;
}
