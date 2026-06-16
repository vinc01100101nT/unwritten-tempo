using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// All the settings for one generated town, saved as an asset you can tweak and re-generate.
/// Keep the same Seed to get the exact same town again; change it for a brand-new one. Nothing here
/// runs during the game — it only drives the generator. Create via Assets ▸ Create ▸ unwritten ▸ Map Recipe.
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Map Recipe", fileName = "MapRecipe")]
public class MapRecipe : ScriptableObject
{
    [Header("Basics")]
    [Tooltip("The name this town's scene file is saved as.")]
    public string mapName = "NewTown";
    [Tooltip("The 'dice roll' for this town. Same number = same town every time; change it to get a different layout.")]
    public int seed = 12345;
    [Tooltip("The pack of tiles, buildings and trees this town is built from.")]
    public TownTheme theme;

    [Header("Size")]
    [Tooltip("How many tiles wide the town is.")]
    [Min(8)] public int width = 48;
    [Tooltip("How many tiles tall the town is.")]
    [Min(8)] public int height = 48;

    [Header("How busy the town is")]
    [Tooltip("How many buildings to place. 0 = almost none, 1 = as many as can fit.")]
    [FormerlySerializedAs("townDensity")]
    [Range(0f, 1f)] public float buildingDensity = 0.5f;
    [Tooltip("How many trees to scatter around. 0 = none, 1 = lots.")]
    [Range(0f, 1f)] public float treeDensity = 0.4f;

    [Header("Roads")]
    [Tooltip("How many main roads spread out from the middle to the edges.")]
    [FormerlySerializedAs("spokeCount")]
    [Min(1)] public int roadCount = 4;
    [Tooltip("How thick the roads are. 1 = thin trail, 3 = wide road.")]
    [Range(1, 3)] public int roadWidth = 2;
    [Tooltip("How much the roads curve and wander. 0 = dead straight, 5 = very curvy.")]
    [Range(0f, 5f)] public float roadWander = 2.5f;
    [Tooltip("Leave an open space in the middle of town (like a square or marketplace).")]
    [FormerlySerializedAs("centralPlaza")]
    public bool openCenter = true;
    [Tooltip("How big that open middle space is, in tiles. Only used when 'Open Center' is on.")]
    [FormerlySerializedAs("plazaRadius")]
    [Min(0)] public int openCenterSize = 3;

    [Header("Town edge & exits")]
    [Tooltip("What surrounds the town: nothing, a ring of trees, a fence, a wall, or water.")]
    public BoundaryStyle boundary = BoundaryStyle.Treeline;
    [Tooltip("How many gaps in the edge you can walk through to leave town. Each one becomes a doorway to another map.")]
    [FormerlySerializedAs("gateCount")]
    [Range(1, 4)] public int exitCount = 2;

    [Header("Ground patches (different-looking ground)")]
    [Tooltip("How many patches of different ground to scatter (sand/darker dirt/etc). Needs Patch Terrains set on the Theme. 0 = none.")]
    [Min(0)] public int terrainPatchCount = 6;
    [Tooltip("Smallest patch size, in tiles. Bigger reads as a real clearing instead of a speck.")]
    [Min(1)] public int terrainPatchMinSize = 12;
    [Tooltip("Largest patch size, in tiles.")]
    [Min(1)] public int terrainPatchMaxSize = 40;
    [Tooltip("How bumpy the patch outline is. 0 = smooth circle, around 0.7 = a natural blobby shape.")]
    [Range(0f, 1f)] public float terrainPatchNoise = 0.7f;

    [Header("Little extras")]
    [Tooltip("How much small decoration (flowers, pebbles) appears next to the roads.")]
    [FormerlySerializedAs("streetDecor")]
    [Range(0f, 1f)] public float roadDecor = 0.3f;
    [Tooltip("How much small decoration appears out in the open grass.")]
    [FormerlySerializedAs("yardDecor")]
    [Range(0f, 1f)] public float fieldDecor = 0.3f;
    [Tooltip("How many spots to place townspeople (NPCs).")]
    [Min(0)] public int npcSpawnCount = 3;
    [Tooltip("How many enemy spawn spots. Towns are usually safe — leave at 0 unless you want monsters.")]
    [Min(0)] public int monsterSpawnerCount = 0;

    [Header("Safety")]
    [Tooltip("Makes sure every building door, exit and spawn can actually be walked to. Best left on.")]
    public bool guaranteeConnectivity = true;
}

/// <summary>How the town's outer edge is enclosed; the gaps in it become exits to other maps.</summary>
public enum BoundaryStyle { None, Treeline, Fence, Wall, Water }
