using System.Collections.Generic;

/// <summary>
/// WHAT A CREATURE DROPS, read from the same tables its death roll reads (<see cref="LootDropper"/>) — so what
/// <c>/mobinfo</c> and <c>/whodrops</c> say a creature drops is what it drops. Plain lookups, no components.
/// </summary>
public static class CreatureDrops
{
    /// <summary>One thing a kill may give up: the item, its chance, and how many come when it does.</summary>
    public readonly struct Drop
    {
        public readonly Item item;
        public readonly float chance;
        public readonly int min, max;
        public Drop(Item item, float chance, int min, int max) { this.item = item; this.chance = chance; this.min = min; this.max = max; }
    }

    /// <summary>Every NAMED drop of <paramref name="def"/>: its hand-authored drops, then its kind's materials at its
    /// level's grade. The one random potion or food is not in here — see <see cref="RandomChanceOf"/>.</summary>
    public static void Of(MonsterDef def, List<Drop> into)
    {
        into.Clear();
        if (def == null) return;
        if (def.itemDropNames != null && def.itemDropChances != null)
            for (int i = 0; i < def.itemDropNames.Length && i < def.itemDropChances.Length; i++)
            {
                // Named by asset, the way the baker loads it into the creature's drop table.
                var item = ByAssetName(def.itemDropNames[i]);
                if (item != null) into.Add(new Drop(item, def.itemDropChances[i], 1, 1));
            }
        var lines = CreatureLoot.LinesFor(def.kind);
        for (int i = 0; i < lines.Count; i++)
        {
            var item = LootDropper.MaterialFor(lines[i], def.level);
            if (item != null) into.Add(new Drop(item, lines[i].chance, lines[i].min, lines[i].max));
        }
    }

    /// <summary>Everything a kill of <paramref name="def"/> may give up, written as one line — "Raw Metal 20%, …, a
    /// random potion or food 30%". The one wording of it: <c>/mobinfo</c> and the wiki's creature table both print
    /// this, so the two can never describe the same creature's drops differently. Never on a hot path.</summary>
    public static string Line(MonsterDef def)
    {
        Of(def, lineScratch);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < lineScratch.Count; i++)
        {
            var drop = lineScratch[i];
            sb.Append(drop.item.displayName).Append(' ').Append(LootDropper.ChanceText(drop.chance));
            if (drop.max > 1) sb.Append(" ×").Append(CountText(drop.min, drop.max));
            sb.Append(", ");
        }
        sb.Append("a random potion or food ").Append(LootDropper.ChanceText(LootDropper.RandomItemChance));
        lineScratch.Clear();
        return sb.ToString();
    }

    static readonly List<Drop> lineScratch = new List<Drop>();

    /// <summary>One creature that may give up an item: who, its chance, and how many come when it does.</summary>
    public readonly struct Source
    {
        public readonly MonsterDef creature;
        public readonly float chance;
        public readonly int min, max;
        public Source(MonsterDef creature, float chance, int min, int max) { this.creature = creature; this.chance = chance; this.min = min; this.max = max; }
    }

    /// <summary>Every creature that may give up <paramref name="item"/> — as a named drop or as its one random potion
    /// or food — in catalog order. The one answer to "who drops this": <c>/whodrops</c>, <c>/iteminfo</c> and the
    /// wiki's material table all read it. Never on a hot path.</summary>
    public static void WhoDrops(Item item, List<Source> into)
    {
        into.Clear();
        if (item == null) return;
        foreach (var d in MonsterCatalog.All)
        {
            if (d == null) continue;
            Of(d, lineScratch);
            foreach (var drop in lineScratch)
                if (drop.item == item && drop.chance > 0f) into.Add(new Source(d, drop.chance, drop.min, drop.max));
            float random = RandomChanceOf(d, item);
            if (random > 0f) into.Add(new Source(d, random, 1, 1));
        }
        lineScratch.Clear();
    }

    /// <summary>A count written the way it is shown: "3", or "5–10" for a range.</summary>
    public static string CountText(int min, int max) => min == max ? min.ToString() : min + "–" + max;

    /// <summary>The chance a kill of <paramref name="def"/> gives up <paramref name="item"/> as its one random potion
    /// or food: the roll's own chance, times this item's share of what that roll picks from. 0 when the roll can
    /// never pick it.</summary>
    public static float RandomChanceOf(MonsterDef def, Item item) =>
        def == null ? 0f : LootDropper.RandomItemChance * ItemCatalog.ShareOfRandomConsumable(item, def.element);

    static Item ByAssetName(string assetName)
    {
        foreach (var it in ItemCatalog.All) if (it != null && it.name == assetName) return it;
        return null;
    }
}
