using System.Collections.Generic;

/// <summary>
/// WHAT A CREATURE DROPS, read from the same tables its death roll reads (<see cref="LootDropper"/>) — so what
/// <c>/mobinfo</c> and <c>/whodrops</c> say a creature drops is what it drops. Plain lookups, no components.
/// </summary>
public static class CreatureDrops
{
    /// <summary>One thing a kill may give up: the item, its chance, how many come in a pile when it does, and how many
    /// separate piles of it fall (one for everything but a grade-D elite's two Rare).</summary>
    public readonly struct Drop
    {
        public readonly Item item;
        public readonly float chance;
        public readonly int min, max;
        public readonly int pilesMin, pilesMax;
        public Drop(Item item, float chance, int min, int max) : this(item, chance, min, max, 1, 1) { }
        public Drop(Item item, float chance, int min, int max, int pilesMin, int pilesMax)
        {
            this.item = item; this.chance = chance; this.min = min; this.max = max;
            this.pilesMin = pilesMin; this.pilesMax = pilesMax;
        }
    }

    /// <summary>Every NAMED drop of <paramref name="def"/>: its hand-authored drops, the materials only it drops
    /// (<see cref="CreatureMaterials"/>), its kind's Inks and Salts, the Temper Dust and Quench Oil every kill gives up
    /// (how many: the least and the most a kill can roll), each piece it wears at <see cref="CreatureLoot.GearDropChance"/>
    /// split between them, then its find (<see cref="CreatureFinds"/>).</summary>
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
        AddOwnMaterial(def.material, def.kind, into);
        var lines = CreatureLoot.LinesFor(def.kind);
        for (int i = 0; i < lines.Count; i++)
        {
            if (!CreatureLoot.DropsAt(lines[i], def.level)) continue;
            var item = LootDropper.MaterialFor(lines[i], def.level);
            if (item != null) into.Add(new Drop(item, lines[i].chance, lines[i].min, lines[i].max));
        }
        AddUnits(MaterialCatalog.TemperDust, CreatureLoot.TemperDustPerGrade, def, into);
        AddUnits(MaterialCatalog.QuenchOil, CreatureLoot.QuenchOilPerGrade, def, into);
        // THE PIECES IT WEARS — what its body seats, one per slot, the last authored for a slot winning — one of them on
        // one kill in ten.
        wornScratch.Clear();
        if (def.startingGear != null)
            foreach (var piece in def.startingGear)
                if (piece != null && piece.slot != EquipSlotType.None) wornScratch[piece.slot] = piece;
        int droppable = 0;
        foreach (var piece in wornScratch.Values) if (LootDropper.Droppable(piece)) droppable++;
        if (droppable > 0)
            foreach (var piece in wornScratch.Values)
                if (LootDropper.Droppable(piece)) into.Add(new Drop(piece, CreatureLoot.GearDropChance / droppable, 1, 1));
        wornScratch.Clear();
        // ITS FIND: one roll at its kind's chance, shared evenly between what its element carries.
        var finds = CreatureFinds.Of(def.element);
        float each = CreatureFinds.ChanceOfOne(def.kind, def.element);
        for (int i = 0; i < finds.Count; i++) into.Add(new Drop(finds[i], each, 1, 1));
    }

    static readonly Dictionary<EquipSlotType, Item> wornScratch = new Dictionary<EquipSlotType, Item>();

    // Temper Dust or Quench Oil on every kill: the fewest and the most units the roll can give.
    static void AddUnits(Item material, int perGrade, MonsterDef def, List<Drop> into)
    {
        if (material == null) return;
        int least = CreatureLoot.UnitsPerKill(perGrade, def.level, def.kind, def.gearQuality, def.FileFolds, 99);
        int most = CreatureLoot.UnitsPerKill(perGrade, def.level, def.kind, def.gearQuality, def.FileFolds, 0);
        if (most <= 0) return;
        // Under one whole unit a kill, it is a CHANCE of one, not one every kill.
        if (least == 0)
            into.Add(new Drop(material, CreatureLoot.HundredthsPerKill(perGrade, def.level, def.kind, def.gearQuality, def.FileFolds) / 100f, 1, 1));
        else into.Add(new Drop(material, 1f, least, most));
    }

    // The same chance and amounts LootDropper rolls a creature's own material at.
    static void AddOwnMaterial(Item item, CreatureKind kind, List<Drop> into)
    {
        if (!CreatureMaterials.Is(item)) return;
        var tier = CreatureMaterials.TierOfStep(item.materialStep);
        float chance = CreatureMaterials.Chance(kind, tier);
        if (chance <= 0f) return;
        CreatureMaterials.Piles(kind, tier, out int pilesMin, out int pilesMax);
        CreatureMaterials.PileSize(kind, tier, out int sizeMin, out int sizeMax);
        into.Add(new Drop(item, chance, sizeMin, sizeMax, pilesMin, pilesMax));
    }

    /// <summary>Everything a kill of <paramref name="def"/> may give up, written as one line — "Raw Metal 20%, …,
    /// Hard Bread 5%". The one wording of it: <c>/mobinfo</c> and the wiki's creature table both print
    /// this, so the two can never describe the same creature's drops differently. Never on a hot path.</summary>
    public static string Line(MonsterDef def)
    {
        Of(def, lineScratch);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < lineScratch.Count; i++)
        {
            var drop = lineScratch[i];
            if (i > 0) sb.Append(", ");
            sb.Append(drop.item.displayName).Append(' ').Append(LootDropper.ChanceText(drop.chance));
            if (drop.max > 1) sb.Append(" ×").Append(CountText(drop.min, drop.max));
            if (drop.pilesMax > 1) sb.Append(" in ").Append(CountText(drop.pilesMin, drop.pilesMax)).Append(" piles");
        }
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

    /// <summary>Every creature that may give up <paramref name="item"/>, in catalog order. The one answer to "who drops this": <c>/whodrops</c>, <c>/iteminfo</c> and the
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
        }
        lineScratch.Clear();
    }

    /// <summary>A count written the way it is shown: "3", or "5–10" for a range.</summary>
    public static string CountText(int min, int max) => min == max ? min.ToString() : min + "–" + max;

    static Item ByAssetName(string assetName)
    {
        foreach (var it in ItemCatalog.All) if (it != null && it.name == assetName) return it;
        return null;
    }
}
