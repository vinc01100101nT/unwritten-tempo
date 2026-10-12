using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes the wiki's item data — <c>wiki/content/items.generated.json</c> — from every item asset, and nothing else ever
/// writes it. The Equipment and Materials pages both read it.
///
/// <para><b>Every value comes from the game's own sources:</b> the item's type from <see cref="ItemTypes.Of"/>, its slot
/// from <see cref="EquipSlots.Label"/>, what a forge costs from the band-1 material <see cref="MaterialCatalog"/>
/// names, chances in <see cref="LootDropper.ChanceText"/>'s wording, and who drops it from
/// <see cref="CreatureDrops.WhoDrops"/> — the same answer <c>/whodrops</c> gives.</para>
///
/// <para><b>When it runs</b> is decided in one place for every Database page — <see cref="WikiExport"/>. It writes one
/// file in the wiki folder and touches nothing Unity saves.</para>
/// </summary>
public static class ItemExport
{
    static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
    public static string WikiPath => Path.Combine(RepoRoot, "wiki", "content", "items.generated.json");

    [Serializable] class Export { public string note; public string[] grades; public Entry[] items; public Trademark[] trademarks; public Word[] words; }

    // One trademark a forge can roll, and what it raises at each grade (grades[] order).
    [Serializable] class Trademark { public string name, weapon, rarity, chance; public Lines[] byGrade; }
    // One armour word, the slot it rolls on, and what it does at each grade (grades[] order).
    [Serializable] class Word { public string name, slot, chance; public int slotOrder; public string[] byGrade; }
    // A weapon's reach as the wiki shows it. A weapon that is melee or ranged by its holder's higher stat reaches
    // bare-hand distance when it swings melee and its own range only when it throws, so it shows both.
    static string ReachText(Item it)
    {
        if (it.slot != EquipSlotType.Weapon) return "";
        float unarmed = CharacterSpec.Active != null ? CharacterSpec.Active.unarmedReach : 0f;
        float own = WeaponReach.Of(it, unarmed, true);
        if (WeaponTypes.RangedByStat(it.weaponType))
            return unarmed.ToString("0.##") + " melee · " + own.ToString("0.##") + " ranged";
        return it.attackRange > 0f ? own.ToString("0.##") : "";
    }

    [Serializable] class Lines { public string[] lines; }

    [Serializable] class Entry
    {
        public string id, name, description, type, slot, weaponType, element, madeOf, forgeMaterial, reach;
        public string materialKind, mergesInto, rarity;
        public int slotOrder, materialKindOrder, forgeUnits, forgeGold, cardSlots, weight, zeny, materialStep;
        public Source[] droppedBy;
    }

    [Serializable] class Source { public string name; public int level; public string chance, count; }

    public static int Write()
    {
        var sources = new List<CreatureDrops.Source>();
        var list = new List<Entry>();

        foreach (var it in Resources.LoadAll<Item>("Items"))
        {
            if (it == null) continue;
            CreatureDrops.WhoDrops(it, sources);
            var by = new Source[sources.Count];
            for (int i = 0; i < sources.Count; i++)
                by[i] = new Source
                {
                    name = sources[i].creature.displayName,
                    level = sources[i].creature.level,
                    chance = LootDropper.ChanceText(sources[i].chance),
                    count = CreatureDrops.CountText(sources[i].min, sources[i].max),
                };

            var band1 = it.madeOf != MaterialKind.None ? MaterialCatalog.Band(it.madeOf, 1) : null;
            list.Add(new Entry
            {
                id = it.id,
                name = it.displayName,
                description = it.description ?? "",
                type = ItemTypes.Of(it).ToString(),
                slot = EquipSlots.Label(it.slot),
                slotOrder = EquipSlots.Order(it.slot),
                weaponType = it.weaponType == WeaponType.None ? "" : WeaponTypes.Label(it.weaponType),
                reach = ReachText(it),
                element = it.element.ToString(),
                madeOf = it.madeOf == MaterialKind.None ? "" : it.madeOf.ToString(),
                forgeUnits = it.forgeMaterialCost,
                forgeMaterial = band1 != null ? band1.displayName : "",
                forgeGold = it.forgeGoldCost,
                cardSlots = it.cardSlots,
                weight = it.weight,
                zeny = it.zeny,
                materialKind = it.materialKind == MaterialKind.None ? "" : it.materialKind.ToString(),
                materialKindOrder = (int)it.materialKind,
                materialStep = it.materialStep,
                mergesInto = it.mergesInto != null ? it.mergesInto.displayName : "",
                // A weapon's rarity is its copy's trademark's, which the design alone does not have — so none here.
                rarity = it.slot == EquipSlotType.Weapon ? "" : Rarities.Label(Rarities.Of(it, null)),
                droppedBy = by,
            });
        }

        list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

        var grades = (Quality[])Enum.GetValues(typeof(Quality));
        var gradeNames = Array.ConvertAll(grades, q => q.ToString());

        // TRADEMARKS: every skill a forge can roll onto each weapon, its rarity, and the forge's own odds.
        var trademarks = new List<Trademark>();
        var lines = new List<string>();
        foreach (WeaponType weapon in Enum.GetValues(typeof(WeaponType)))
        {
            if (!Trademarks.AnyFor(weapon)) continue;
            foreach (var s in SkillCatalog.All)
            {
                if (!Trademarks.Rollable(s, weapon)) continue;
                var byGrade = new Lines[grades.Length];
                for (int g = 0; g < grades.Length; g++)
                {
                    ItemTooltip.TrademarkLines(s, CraftingMath.TrademarkLift(grades[g]), lines);
                    byGrade[g] = new Lines { lines = lines.ToArray() };
                }
                trademarks.Add(new Trademark
                {
                    name = s.displayName,
                    weapon = WeaponTypes.Label(weapon),
                    rarity = Rarities.Label(Rarities.OfTrademark(s)),
                    chance = LootDropper.ChanceText(Trademarks.ChanceOf(s, weapon)),
                    byGrade = byGrade,
                });
            }
        }
        trademarks.Sort((a, b) => string.CompareOrdinal(a.weapon, b.weapon) != 0 ? string.CompareOrdinal(a.weapon, b.weapon)
                                                                                 : string.CompareOrdinal(a.name, b.name));

        // ARMOUR WORDS: every word, on the one slot that rolls it, every word of a slot equally likely.
        var words = new List<Word>();
        foreach (EquipSlotType slot in Enum.GetValues(typeof(EquipSlotType)))
        {
            var slotWords = ArmourWords.For(slot);
            foreach (var w in slotWords)
            {
                var byGrade = new string[grades.Length];
                for (int g = 0; g < grades.Length; g++) byGrade[g] = ArmourWords.Describe(w, ArmourWords.Amount(w, grades[g]));
                words.Add(new Word
                {
                    name = w.ToString(),
                    slot = EquipSlots.Label(slot),
                    slotOrder = EquipSlots.Order(slot),
                    chance = LootDropper.ChanceText(1f / slotWords.Length),
                    byGrade = byGrade,
                });
            }
        }

        var export = new Export
        {
            grades = gradeNames,
            trademarks = trademarks.ToArray(),
            words = words.ToArray(),
            note = "GENERATED from the item assets, the creature catalog and the skills by the editor (WikiExport), after " +
                   "any change to the game's data or code. Do NOT hand-edit: the next change overwrites this file.",
            items = list.ToArray(),
        };
        SkillExport.WriteIfChanged(WikiPath, JsonUtility.ToJson(export, true) + "\n");
        return list.Count;
    }
}
