using System.Collections.Generic;

/// <summary>
/// A CREATURE'S FIND — the one consumable a kill can give up besides materials (decided 2026-10-13). WHICH one is its
/// element's answer: a creature of an element that has converters gives up one of those, and a creature of any other
/// element gives up that element's food. HOW OFTEN is its kind's answer. No shop sells a find and none buys one; what
/// a shop sells (potions, wings, the Antidote) no creature drops.
///
/// <para>Plain lookups and one division: the kill's roll and what <c>/mobinfo</c> and the wiki say a creature drops
/// both read this, so the two cannot disagree.</para>
/// </summary>
public static class CreatureFinds
{
    /// <summary>The chance (0–1) one kill of a <paramref name="kind"/> gives up its find.</summary>
    public static float Chance(CreatureKind kind) =>
        kind == CreatureKind.Shadow ? 1f : kind == CreatureKind.Elite ? 0.5f : 0.05f;

    // The food each element WITHOUT a converter carries, by item id. An element with converters carries those instead,
    // so it has no row here.
    static string FoodIdOf(Element element)
    {
        switch (element)
        {
            case Element.Neutral: return "item.hard-bread";
            case Element.Poison:  return "item.spiced-broth";
            case Element.Dark:    return "item.lucky-dumpling";
            case Element.Undead:  return "item.grilled-fish";
            case Element.Ghost:   return "item.sage-stew";
            case Element.Holy:    return "item.travellers-tea";
            default:              return null;
        }
    }

    // Worked out once per element and kept: a kill only looks its list up.
    static readonly Dictionary<Element, List<Item>> byElement = new Dictionary<Element, List<Item>>();

    /// <summary>Everything a creature of <paramref name="element"/> can give up as its find: its element's converters,
    /// or its element's one food. One kill gives up at most one of them.</summary>
    public static IReadOnlyList<Item> Of(Element element)
    {
        if (byElement.TryGetValue(element, out var known)) return known;
        var list = new List<Item>();
        var converters = ItemCatalog.ConvertersOf(element);
        if (converters.Count > 0) list.AddRange(converters);
        else
        {
            var food = ItemCatalog.Resolve(FoodIdOf(element));
            if (food != null) list.Add(food);
        }
        byElement[element] = list;
        return list;
    }

    /// <summary>The find one kill of a creature of <paramref name="element"/> gives up once its chance has come up —
    /// each of its element's finds equally likely. Null when the element carries none.</summary>
    public static Item Roll(Element element)
    {
        var finds = Of(element);
        return finds.Count == 0 ? null : finds[CombatRng.Range(0, finds.Count)];
    }

    /// <summary>The chance (0–1) one kill gives up ONE NAMED find of its element: the kind's chance, shared evenly
    /// between the finds that element carries.</summary>
    public static float ChanceOfOne(CreatureKind kind, Element element)
    {
        int count = Of(element).Count;
        return count == 0 ? 0f : Chance(kind) / count;
    }

    /// <summary>Forget the kept lists, so a newly authored item is picked up. Called with <see cref="ItemCatalog.Reload"/>.</summary>
    public static void Reload() => byElement.Clear();
}
