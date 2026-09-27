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
    // The element converters, filed under the element each one gives: a creature's roll adds only its own element's.
    static Dictionary<Element, List<Item>> convertersByElement;

    static void EnsurePools()
    {
        if (wornBySlot != null) return;
        wornBySlot = new Dictionary<EquipSlotType, List<Item>>();
        unworn = new List<Item>();
        convertersByElement = new Dictionary<Element, List<Item>>();
        foreach (var it in Resources.LoadAll<Item>("Items"))
        {
            if (it == null) continue;
            if (it.slot == EquipSlotType.None)
            {
                // A quest item is never put down, so it is never handed out at random either.
                if (it.category == ItemCategory.Quest || it.materialKind != MaterialKind.None) continue;
                if (ConverterElement(it, out var element))
                {
                    if (!convertersByElement.TryGetValue(element, out var own))
                        convertersByElement[element] = own = new List<Item>();
                    own.Add(it);
                }
                else unworn.Add(it);
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

    /// <summary>A random item that is neither worn nor a material — a potion, a food — and never a quest item, plus the
    /// element converters of <paramref name="creatureElement"/> alone, all equally likely. Null when none exists.
    /// Materials are left out on purpose: which grade a creature drops is its level's answer (<see cref="CreatureLoot"/>),
    /// and a random one would hand a level-3 creature's killer the top grade. A converter comes only from a creature of
    /// its own element, so a Neutral creature never drops one.</summary>
    public static Item RandomConsumable(Element creatureElement)
    {
        EnsurePools();
        convertersByElement.TryGetValue(creatureElement, out var own);
        int ownCount = own != null ? own.Count : 0;
        int total = unworn.Count + ownCount;
        if (total == 0) return null;
        int pick = CombatRng.Range(0, total);
        return pick < unworn.Count ? unworn[pick] : own[pick - unworn.Count];
    }

    /// <summary><paramref name="item"/>'s share of what <see cref="RandomConsumable"/> picks from for a creature of
    /// <paramref name="creatureElement"/> — one over the size of that pool, or 0 when the pool never holds it. The same
    /// pool the pick reads, so what a creature is said to drop is what it drops.</summary>
    public static float ShareOfRandomConsumable(Item item, Element creatureElement)
    {
        if (item == null) return 0f;
        EnsurePools();
        convertersByElement.TryGetValue(creatureElement, out var own);
        int total = unworn.Count + (own != null ? own.Count : 0);
        if (total == 0) return 0f;
        return unworn.Contains(item) || (own != null && own.Contains(item)) ? 1f / total : 0f;
    }

    /// <summary>The element a converter item gives, read off the skill it runs — the effect is the ONE place a
    /// converter's element lives, so the item carries no second copy of it.</summary>
    static bool ConverterElement(Item it, out Element element)
    {
        element = Element.Neutral;
        var def = it.IsUsable ? SkillCatalog.Get(it.useSkillId) : null;
        if (def == null || def.bindings == null) return false;
        foreach (var binding in def.bindings)
        {
            if (binding == null || binding.effects == null) continue;
            foreach (var e in binding.effects)
                if (e != null && e.kind == SkillEffectKind.ElementConverter)
                {
                    element = e.element;
                    return true;
                }
        }
        return false;
    }

    /// <summary>Forget every cached list, so a newly authored or changed item is picked up. The editor calls this before
    /// writing the wiki's tables; the game never needs to.</summary>
    public static void Reload()
    {
        itemsById = null; cardsByName = null; wornBySlot = null; unworn = null; convertersByElement = null; all = null;
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
