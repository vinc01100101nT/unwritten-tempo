using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The one place that resolves a saved item/card id back to its shared asset, and encodes/decodes the
/// card list on an equipment instance. Save/load (equipment + bag) go through here so the scattered
/// <c>Resources.LoadAll&lt;Item&gt;("Items")</c> lookups collapse to a single cached map (centralization).
/// Author-time assets live in <c>Resources/Items</c> (keyed by <see cref="Item.id"/>) and
/// <c>Resources/Cards</c> (keyed by asset name — <see cref="Card"/> has no id field).
/// Not a hot path: the maps build lazily once (save/load only), never per-frame.
/// </summary>
public static class ItemCatalog
{
    static Dictionary<string, Item> itemsById;
    static Dictionary<string, Card> cardsByName;

    // Every item, split once into what is worn in each place and what is not worn at all. Built on first ask and kept.
    static Dictionary<EquipSlotType, List<Item>> wornBySlot;
    static List<Item> unworn;

    static void EnsurePools()
    {
        if (wornBySlot != null) return;
        wornBySlot = new Dictionary<EquipSlotType, List<Item>>();
        unworn = new List<Item>();
        foreach (var it in Resources.LoadAll<Item>("Items"))
        {
            if (it == null) continue;
            if (it.slot == EquipSlotType.None)
            {
                // A quest item is never put down, so it is never handed out at random either.
                if (it.category != ItemCategory.Quest) unworn.Add(it);
                continue;
            }
            if (!wornBySlot.TryGetValue(it.slot, out var list)) wornBySlot[it.slot] = list = new List<Item>();
            list.Add(it);
        }
    }

    /// <summary>EVERY item the game has, in no particular order — what a station reads to list what it can make.
    /// Built once and kept, like the pools above.</summary>
    public static IReadOnlyList<Item> All
    {
        get
        {
            if (all == null) all = new List<Item>(Resources.LoadAll<Item>("Items"));
            return all;
        }
    }
    static List<Item> all;

    /// <summary>A random item worn in <paramref name="slot"/>, or null when no such item exists.</summary>
    public static Item RandomFor(EquipSlotType slot)
    {
        EnsurePools();
        return wornBySlot.TryGetValue(slot, out var list) && list.Count > 0 ? list[CombatRng.Range(0, list.Count)] : null;
    }

    /// <summary>A random item that is not worn — a potion, a food, a material — and never a quest item. Null when none exists.</summary>
    public static Item RandomUnworn()
    {
        EnsurePools();
        return unworn.Count > 0 ? unworn[CombatRng.Range(0, unworn.Count)] : null;
    }

    /// <summary>The shared <see cref="Item"/> definition for a saved id, or null if it no longer exists.</summary>
    public static Item Resolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (itemsById == null)
        {
            itemsById = new Dictionary<string, Item>();
            foreach (var it in Resources.LoadAll<Item>("Items"))
                if (it != null && !string.IsNullOrEmpty(it.id)) itemsById[it.id] = it;
        }
        return itemsById.TryGetValue(id, out var def) ? def : null;
    }

    /// <summary>The <see cref="Card"/> asset for a saved asset-name, or null if it no longer exists.</summary>
    public static Card ResolveCard(string assetName)
    {
        if (string.IsNullOrEmpty(assetName)) return null;
        if (cardsByName == null)
        {
            cardsByName = new Dictionary<string, Card>();
            foreach (var c in Resources.LoadAll<Card>("Cards"))
                if (c != null) cardsByName[c.name] = c;
        }
        return cardsByName.TryGetValue(assetName, out var card) ? card : null;
    }

    /// <summary>Serialize inserted cards to a comma-joined asset-name string (JsonUtility can't nest lists).</summary>
    public static string EncodeCards(List<Card> cards)
    {
        if (cards == null || cards.Count == 0) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(cards[i].name);
        }
        return sb.ToString();
    }

    /// <summary>Rebuild an instance's card list from an <see cref="EncodeCards"/> string (unknown names dropped).</summary>
    public static void DecodeCards(string encoded, List<Card> into)
    {
        into.Clear();
        if (string.IsNullOrEmpty(encoded)) return;
        foreach (var name in encoded.Split(','))
        {
            var c = ResolveCard(name);
            if (c != null) into.Add(c);
        }
    }
}
