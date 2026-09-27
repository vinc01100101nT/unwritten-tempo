using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes the wiki's creature table — <c>wiki/content/monsters.generated.json</c> — from the creature catalog and the
/// world spawn list, and nothing else ever writes it.
///
/// <para><b>Why this exists.</b> The catalog owns every creature's numbers and the spawn list owns where each one
/// lives; a hand-typed wiki table restating them would drift the first time either changed. So the wiki READS this
/// export instead, the way it reads the skill export (<see cref="SkillExport"/>).</para>
///
/// <para><b>Every value comes from the game's own functions, never from here:</b> combat numbers from
/// <see cref="MonsterDefStats"/> (what the Mob Table and <c>/mobinfo</c> show), the size group from
/// <see cref="MobSizes"/>, drops from <see cref="CreatureDrops.Line"/>, and homes from
/// <see cref="WorldSpawns.HomesOf"/> — so a row that draws a random mix is left out exactly as <c>/where</c> leaves
/// it out. Map names come from the world layout, the one home of them.</para>
///
/// <para><b>When it runs</b> is decided in one place for every Database page — <see cref="WikiExport"/>. It writes one
/// file outside the project's assets and touches nothing Unity saves.</para>
/// </summary>
public static class MobExport
{
    static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    public static string WikiPath => Path.Combine(RepoRoot, "wiki", "content", "monsters.generated.json");

    static string LayoutPath => Path.Combine(RepoRoot, "tools", "world-creator", "worlds", "world-regions.json");

    [Serializable] class Export { public string note; public Creature[] creatures; }

    [Serializable] class Creature
    {
        public string id, name, kind, element, size, biome, drops;
        public int level, elementLevel, hp, atk, def, flee, xp, goldMin, goldMax;
        public Home[] homes;
    }

    [Serializable] class Home { public string map, name; public int count; }

    [Serializable] class LayoutJson { public LayoutMap[] maps; }
    [Serializable] class LayoutMap { public string id, name; }

    /// <summary>Regenerate the file from the assets. Returns how many creatures were written.</summary>
    public static int Write()
    {
        MonsterCatalog.Reload();
        var names = MapNames();
        var homes = new List<(string map, int count)>();
        var list = new List<Creature>();

        foreach (var d in MonsterCatalog.All)
        {
            if (d == null) continue;
            WorldSpawns.HomesOf(d, homes);
            var h = new Home[homes.Count];
            for (int i = 0; i < homes.Count; i++)
                h[i] = new Home
                {
                    map = homes[i].map,
                    name = names.TryGetValue(homes[i].map, out var n) ? n : homes[i].map,
                    count = homes[i].count,
                };

            list.Add(new Creature
            {
                id = d.id,
                name = d.displayName,
                kind = d.kind.ToString(),
                level = d.level,
                element = d.element.ToString(),
                elementLevel = d.elementLevel,
                size = MobSizes.GroupOf(MobSizes.Of(d.kind, d.size)).ToString(),
                biome = d.biome.ToString(),
                hp = MonsterDefStats.Derived(d, MonsterDefStats.Stat.HP),
                atk = MonsterDefStats.Derived(d, MonsterDefStats.Stat.Atk),
                def = MonsterDefStats.Derived(d, MonsterDefStats.Stat.Def),
                flee = MonsterDefStats.Derived(d, MonsterDefStats.Stat.Flee),
                xp = d.xpReward,
                goldMin = d.goldMin,
                goldMax = d.goldMax,
                drops = CreatureDrops.Line(d),
                homes = h,
            });
        }

        list.Sort((a, b) => a.level != b.level ? a.level.CompareTo(b.level) : string.CompareOrdinal(a.name, b.name));

        var export = new Export
        {
            note = "GENERATED from the creature catalog and the world spawn list by the editor (WikiExport), after any " +
                   "change to the game's data or code. Do NOT hand-edit: the next change overwrites this file.",
            creatures = list.ToArray(),
        };
        SkillExport.WriteIfChanged(WikiPath,JsonUtility.ToJson(export, true) + "\n");
        return list.Count;
    }

    // The world map export names its maps through this too — the layout is the one home of a map's name.
    internal static Dictionary<string, string> MapNames()
    {
        var names = new Dictionary<string, string>();
        try
        {
            var layout = JsonUtility.FromJson<LayoutJson>(File.ReadAllText(LayoutPath));
            if (layout?.maps != null)
                foreach (var m in layout.maps)
                    if (m != null && !string.IsNullOrEmpty(m.id)) names[m.id] = string.IsNullOrEmpty(m.name) ? m.id : m.name;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Mob Table] Could not read the world layout for map names, so the wiki will show map ids: "
                             + e.Message);
        }
        return names;
    }
}
