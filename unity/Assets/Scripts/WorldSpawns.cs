using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHERE EVERY CREATURE IN THE WORLD LIVES — the one list of it, kept outside the maps. Each row is one spawn area in
/// one map: which creatures, how many, and where. When the server loads a map it reads that map's rows and builds the
/// spawn areas in memory (see <see cref="WorldSpawner"/>), so nothing about creatures is saved inside a map and moving
/// them never means opening one.
///
/// <para>Lives at <c>Resources/WorldSpawns</c>. A spot is written in the MAP'S OWN coordinates — the numbers
/// <c>/where</c> prints — so a map keeps its creatures wherever the server happens to set it down.</para>
/// </summary>
[CreateAssetMenu(fileName = "WorldSpawns", menuName = "unwritten/World Spawns", order = 2)]
public class WorldSpawns : ScriptableObject
{
    public const string ResourcePath = "WorldSpawns";

    [System.Serializable]
    public class Row
    {
        [Tooltip("The map's scene name, e.g. 'lst_fild01'.")]
        public string map = "";
        [Tooltip("A creature's id or name, e.g. 'mob.shadow.veyra' — always that creature, and how an elite or a " +
                 "shadow is placed. Empty = a weighted mix of the Biome's normals inside the level range below.")]
        public string creature = "";
        [Tooltip("Only when Creature is empty: whose normals are drawn into the mix.")]
        public Biome biome = Biome.Grassland;
        [Tooltip("Only when Creature is empty: the lowest level drawn into the mix.")]
        public int minLevel = 1;
        [Tooltip("Only when Creature is empty: the highest level drawn into the mix.")]
        public int maxLevel = 99;
        [Tooltip("Only when Creature is empty: also draw the normals that belong to no biome.")]
        public bool includeCommons = true;
        [Tooltip("How many of them are kept alive at once.")]
        [Min(0)] public int population = 1;
        [Tooltip("The middle of the area, in the map's own coordinates — what /where prints: X west to east, Y south " +
                 "to north, Z height.")]
        [ShownCoords] public Vector3 center;
        [Tooltip("They appear anywhere on the ground within this distance of the middle.")]
        [Min(0f)] public float radius = 10f;
        [Tooltip("Never appear closer than this to a player.")]
        [Min(0f)] public float playerClearRadius = 6f;
    }

    public List<Row> rows = new List<Row>();

    /// <summary>Every map <paramref name="def"/> is placed in BY NAME, with how many of it that map keeps alive —
    /// several rows naming it in one map are one map. A row that draws a mix is left out: it says only that the
    /// creature MIGHT appear, and its count belongs to the whole mix. Fills <paramref name="into"/> in list order.</summary>
    public static void HomesOf(MonsterDef def, List<(string map, int count)> into)
    {
        into.Clear();
        var list = Instance;
        if (def == null || list == null) return;
        foreach (var row in list.rows)
        {
            if (row == null || string.IsNullOrEmpty(row.creature) || MonsterCatalog.Get(row.creature) != def) continue;
            int at = into.FindIndex(h => h.map == row.map);
            if (at < 0) into.Add((row.map, row.population));
            else into[at] = (row.map, into[at].count + row.population);
        }
    }

    /// <summary>How many of each creature <paramref name="map"/> keeps alive, by creature id — every row there that names
    /// the creature, added up. A row that draws a random mix is left out: its count belongs to the whole mix, never to
    /// one creature in it. Adds to <paramref name="into"/> (cleared first).</summary>
    public static void KeptIn(string map, Dictionary<string, int> into)
    {
        into.Clear();
        var list = Instance;
        if (list == null || string.IsNullOrEmpty(map)) return;
        foreach (var row in list.rows)
        {
            if (row == null || row.map != map || row.population <= 0 || string.IsNullOrWhiteSpace(row.creature)) continue;
            var def = MonsterCatalog.Get(row.creature);
            if (def == null || string.IsNullOrEmpty(def.id)) continue;
            into.TryGetValue(def.id, out int n);
            into[def.id] = n + row.population;
        }
    }

    static readonly List<MonsterDef> mixScratch = new List<MonsterDef>();

    /// <summary>Every creature that can live in <paramref name="map"/>: each one a row names, and each one a mix row
    /// can draw (<see cref="MonsterCatalog.MixOf"/>, the server's own rule). Each creature once, lowest level first,
    /// then by name. A row naming a creature that is not on the roster adds nothing — that area stays empty.</summary>
    public static void CreaturesIn(string map, List<MonsterDef> into)
    {
        into.Clear();
        var list = Instance;
        if (list == null || string.IsNullOrEmpty(map)) return;
        foreach (var row in list.rows)
        {
            if (row == null || row.map != map || row.population <= 0) continue;
            if (!string.IsNullOrWhiteSpace(row.creature))
            {
                var named = MonsterCatalog.Get(row.creature);
                if (named != null && !into.Contains(named)) into.Add(named);
                continue;
            }
            MonsterCatalog.MixOf(row.biome, row.minLevel, row.maxLevel, row.includeCommons, mixScratch);
            foreach (var m in mixScratch) if (!into.Contains(m)) into.Add(m);
        }
        into.Sort(ByLevelThenName);
    }

    static int ByLevelThenName(MonsterDef a, MonsterDef b) =>
        a.level != b.level ? a.level.CompareTo(b.level) : string.CompareOrdinal(a.displayName, b.displayName);

    static WorldSpawns loaded;
    static bool looked;

    /// <summary>The list, loaded once. Null when it has not been made — every map is then empty of creatures, and the
    /// server says so once.</summary>
    public static WorldSpawns Instance
    {
        get
        {
            if (!looked) { looked = true; loaded = Resources.Load<WorldSpawns>(ResourcePath); }
            return loaded;
        }
    }
}
