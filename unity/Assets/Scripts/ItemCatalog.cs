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
