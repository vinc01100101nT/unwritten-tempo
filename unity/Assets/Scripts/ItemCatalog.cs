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

    // Every worn item, filed under the place it is worn. Built on first ask and kept.
    static Dictionary<EquipSlotType, List<Item>> wornBySlot;
    // The element converters, filed under the element each one gives: a creature's find is only its own element's.
    static Dictionary<Element, List<Item>> convertersByElement;

    static void EnsurePools()
    {
        if (wornBySlot != null) return;
        wornBySlot = new Dictionary<EquipSlotType, List<Item>>();
        convertersByElement = new Dictionary<Element, List<Item>>();
        foreach (var it in Resources.LoadAll<Item>("Items"))
        {
            if (it == null) continue;
            if (it.slot == EquipSlotType.None)
            {
                // A quest item is never put down, and a material comes from its own rolls, so neither is a find.
                if (it.category == ItemCategory.Quest || it.materialKind != MaterialKind.None) continue;
                if (ConverterElement(it, out var element))
                {
                    if (!convertersByElement.TryGetValue(element, out var own))
                        convertersByElement[element] = own = new List<Item>();
                    own.Add(it);
                }
                continue;
            }
            // A worn piece made of nothing is worn for its look alone and is not handed out at random.
            if (it.slot != EquipSlotType.Weapon && it.madeOf == MaterialKind.None) continue;
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

    /// <summary>The element converters that give <paramref name="element"/> — what a creature of that element gives up
    /// as its find (<see cref="CreatureFinds"/>). Empty for an element that has no converter.</summary>
    public static IReadOnlyList<Item> ConvertersOf(Element element)
    {
        EnsurePools();
        return convertersByElement.TryGetValue(element, out var own) ? own : NoItems;
    }

    static readonly List<Item> NoItems = new List<Item>();

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

    public struct Restores { public bool health, mana, healthOverTime, manaOverTime; }

    // What an item's skill gives back, read off its effects once per skill and kept: an instant heal restores health; a
    // timed health or mana regeneration restores that one over time. Auto-potion asks this to find a drink.
    static readonly Dictionary<string, Restores> restoresBySkill = new Dictionary<string, Restores>();

    public static Restores WhatItRestores(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return default;
        if (restoresBySkill.TryGetValue(skillId, out var known)) return known;
        var r = new Restores();
        var def = SkillCatalog.Get(skillId);
        if (def != null && def.bindings != null)
            for (int b = 0; b < def.bindings.Count; b++)
            {
                var fx = def.bindings[b]?.effects;
                if (fx == null) continue;
                for (int i = 0; i < fx.Count; i++)
                {
                    var e = fx[i];
                    if (e == null) continue;
                    if (e.kind == SkillEffectKind.Heal) r.health = true;
                    else if (e.kind == SkillEffectKind.SubStatBuff && e.subStat == SubStat.HPRegen) { r.health = true; r.healthOverTime = true; }
                    else if (e.kind == SkillEffectKind.SubStatBuff && e.subStat == SubStat.MPRegen) { r.mana = true; r.manaOverTime = true; }
                }
            }
        restoresBySkill[skillId] = r;
        return r;
    }

    /// <summary>The picture of the item that runs <paramref name="skillId"/> — the consumable that uses it, or the hat
    /// that runs it while worn — or null when no item does. A potion's or a hat's skill has no painted icon of its own,
    /// so its tiles wear the item's picture; the item stays the one owner of that picture. Built once and kept.</summary>
    public static Sprite IconOfSkill(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return null;
        if (iconsBySkill == null)
        {
            iconsBySkill = new Dictionary<string, Sprite>();
            foreach (var it in All)
            {
                if (it == null || it.icon == null) continue;
                if (!string.IsNullOrEmpty(it.useSkillId)) iconsBySkill[it.useSkillId] = it.icon;
                if (!string.IsNullOrEmpty(it.wornSkillId)) iconsBySkill[it.wornSkillId] = it.icon;
            }
        }
        return iconsBySkill.TryGetValue(skillId, out var s) ? s : null;
    }
    static Dictionary<string, Sprite> iconsBySkill;

    /// <summary>Forget every cached list, so a newly authored or changed item is picked up. The editor calls this before
    /// writing the wiki's tables; the game never needs to.</summary>
    public static void Reload()
    {
        itemsById = null; cardsByName = null; wornBySlot = null; convertersByElement = null; all = null;
        restoresBySkill.Clear();
        iconsBySkill = null;
        CrateMath.Reload();
        CreatureFinds.Reload();
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
