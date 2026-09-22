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
        [Tooltip("The map's scene name, e.g. 'field_0'.")]
        public string map = "";
        [Tooltip("A creature's id or name, e.g. 'mob.shadow.tsubame' — always that creature, and how an elite or a " +
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
        [Tooltip("The middle of the area, in the map's own coordinates — what /where prints.")]
        public Vector3 center;
        [Tooltip("They appear anywhere on the ground within this distance of the middle.")]
        [Min(0f)] public float radius = 10f;
        [Tooltip("Never appear closer than this to a player.")]
        [Min(0f)] public float playerClearRadius = 6f;
    }

    public List<Row> rows = new List<Row>();

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
