using System.Collections.Generic;
using UnityEngine;

/// <summary>Known Pxltgr prop families. Used to auto-fill a group's prefabs from Assets/1_Authored/Prefabs/Props when its
/// explicit prefab list is empty — so you can say "Trees" without dragging each one in.</summary>
public enum PropCategory { None, Trees, Stumps, Rocks, Bushes, Grass, Flowers, Ferns, Mushrooms, Houses, Landmarks, Carts }

/// <summary>How a prop group is arranged on the plot.</summary>
public enum PlacementRule
{
    Scatter,     // random across the whole plot (trees in a field, loose flowers)
    EdgeRing,    // a ring just inside the four edges (a treeline / wall border)
    StreetRows,  // two rows flanking the central path (houses lining a street)
    Roadside,    // hugging the central path edges (market carts, lamps)
    Center,      // clustered near the middle (landmarks: tavern, well, statue)
    Grid,        // buildings on a rectangular block grid; roads (paths) carve the streets between blocks (a walled town)
    PerimeterWall, // assemble the modular Pxltgr wall kit (Wall_part/corner/tower/gate) around the plot edge, with a gate gap
    Clumps,      // bunches: 2.5 m ground-tile cells grown into touching shapes, each filled densely; never on a road
}

public enum AmountMode { Density, Count }

/// <summary>Whether a prop group's plants sway in the wind. <c>Auto</c> = sway soft decor (non-solid props:
/// flowers, grass, ferns) and leave solid props (trees, houses, rocks) planted; <c>On</c>/<c>Off</c> force it.</summary>
public enum SwayMode { Auto, On, Off }

/// <summary>Which ground tile the routed paths use, when the map's biome sets no road of its own (see BiomeTable).</summary>
public enum PathTile { Road, Soil, Stones, Rock }

/// <summary>Theme of a map, and the biome a creature spawns in. What a biome LOOKS like — its ground tiles, colour
/// wash, road and which props each prop category means — is its row in the editor's BiomeTable, never code here.</summary>
// NOTE: Unity serializes enums by integer position, so existing assets store the index, not the name.
// Only ever APPEND new members at the end — inserting/reordering silently remaps every saved value.
// `None` is the mob-side "biome-agnostic" value: a MonsterDef set to None is spawn-eligible in ANY
// biome's level band (see MonsterCatalog.Eligible). A MAP recipe should never be None.
public enum Biome { Grassland, Snow, Desert, Volcanic, Corrupted, None, Forest, Highland, Peaks, Steppe, Coast, Swamp, Sacred, Haunted, Mist }

/// <summary>How the ground base/patch/path weights are rendered. <c>Blended</c> = the seamless feathered gradient
/// (default, today's look). <c>HardEdge</c> = snap each blend weight to 0/1 so grass↔dirt↔path meet in crisp seams
/// (an RO-ish hard-tile feel) — same single mesh + shader, just no feathering. A full tile-grid ground would be a
/// separate builder behind the same one call site in <c>RecipeGenerator</c>; this is the cheap in-pipeline variant.</summary>
public enum GroundStyle { Blended, HardEdge }

/// <summary>Shape of the road network. <c>Winding</c> = the default <c>pathCount</c> wandering routes through a
/// jittered hub. <c>Cardinal</c> = a straight plaza-centred cross: one road each to N/E/S/W meeting exactly at the
/// map centre (ignores <c>pathCount</c>/<c>pathWander</c>) — for a planned, RO-style town.</summary>
public enum PathStyle { Winding, Cardinal }

[System.Serializable]
public class RecipePropGroup
{
    public string label = "Props";

    [Tooltip("Exact prefabs to place. If this is empty, the Category below is used to auto-fill from the pack.")]
    public List<GameObject> prefabs = new List<GameObject>();

    [Tooltip("Used ONLY when Prefabs is empty — pulls the known Pxltgr prefabs of this kind from Assets/1_Authored/Prefabs/Props.")]
    public PropCategory category = PropCategory.None;

    public PlacementRule placement = PlacementRule.Scatter;

    public AmountMode amountMode = AmountMode.Density;
    [Tooltip("Density mode: items per square world-unit (try 0.01–0.07).")]
    public float density = 0.02f;
    [Tooltip("Count mode: how many to place (subject to spacing / free space).")]
    public int count = 20;

    [Tooltip("Solid props keep their colliders (block movement + pathfinding). Decor is stripped to walk-through.")]
    public bool solid = true;
    [Range(0.02f, 5f)] public float scale = 0.3f;
    [Range(0f, 0.6f)] public float scaleJitter = 0.15f;
    [Tooltip("Minimum gap between items in this group (world units).")]
    public float minSpacing = 1.5f;
    [Tooltip("Keep this group at least this far from the map centre / player spawn (0 = no clearance).")]
    public float clearFromCenter = 0f;
    [Tooltip("Keep this group off the central path/street. Applies to Scatter / EdgeRing / Center only.")]
    public bool avoidPath = false;
    public bool randomYaw = true;
    [Tooltip("StreetRows only: distance from the centre line out to each of the two rows.")]
    public float rowOffset = 4f;
    [Tooltip("Clumps only: the most 2.5 m cells one bunch grows to (each bunch is 1 to this many cells, touching). The number of bunches is Count (or Density per square unit).")]
    public int cellsPerClump = 6;
    [Tooltip("Clumps only: how many props are tried in each 2.5 m cell of a bunch.")]
    public int propsPerCell = 10;

    [Tooltip("Wind sway, synced to the world wind. Auto = sway foliage automatically: whole-plant decor (flowers/" +
             "grass) sways entirely, while solid props sway only their LEAF material (tree canopies rustle, trunks " +
             "stay rigid; rocks/houses have no leaves so they're unaffected). On = force whole-plant sway. Off = none.")]
    public SwayMode sway = SwayMode.Auto;

    /// <summary>Does this group sway at all? (Anything but Off.)</summary>
    public bool ShouldSway => sway != SwayMode.Off;

    /// <summary>Sway only the foliage (leaf) materials, leaving structural materials rigid. True for Auto on a SOLID
    /// prop (a tree: canopy rustles, trunk stays put); whole-plant decor and an explicit On sway every material.</summary>
    public bool CanopyOnly => sway == SwayMode.Auto && solid;
}

/// <summary>
/// The ONE content-driven world-generator recipe for <i>unwritten</i>. Author it in the Inspector
/// (Assets ▸ Create ▸ unwritten ▸ Map Recipe) and bake it with <c>RecipeGenerator</c> (the Generate button).
/// There is no "town generator" vs "field generator": a map is simply whatever ground + prop groups you
/// put in the recipe, so the same generator yields a town (Houses in StreetRows + a tavern), a wild field
/// (scattered trees), a dungeon (your dungeon prefabs), or anything else — purely from content. Its creatures are
/// not part of the map: they come from the world spawn list (WorldSpawns) when the server loads it.
///
/// All maps use the single blended grass→dirt ground (no tile seams). Determinism: <see cref="seed"/> drives
/// every random choice, so the same recipe + seed bakes the same map; tick <see cref="randomizeSeed"/> to roll a
/// fresh one (written back to <see cref="seed"/> to stay reproducible).
/// </summary>
[CreateAssetMenu(fileName = "MapRecipe", menuName = "unwritten/Map Recipe", order = 0)]
public class MapRecipe : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable kebab id, e.g. 'map.field-01'. Quests/portals reference THIS, never the display name. Must be unique.")]
    public string id = "map.new";
    [Tooltip("Scene file name — saved to Assets/Scenes/<mapName>.unity. Re-generating overwrites that scene.")]
    public string mapName = "NewMap";
    public int seed = 12345;
    [Tooltip("Roll a fresh seed on every Generate (the rolled seed is written back to 'seed' so it stays reproducible).")]
    public bool randomizeSeed = true;

    [Header("Ground")]
    public float sizeX = 60f;
    public float sizeZ = 60f;
    [Range(0f, 1f), Tooltip("0 = mostly grass, 1 = mostly dirt — controls the organic soil patches.")]
    public float dirtAmount = 0.18f;

    [Tooltip("Theme preset. The biome's row in the Biome Table picks the ground tiles, colour tint, road and props.")]
    public Biome biome = Biome.Grassland;
    [Tooltip("Extra albedo multiply ON TOP of the biome's own tint (white = just the biome's tint, no override).")]
    public Color groundTint = Color.white;
    [Tooltip("Blended = seamless feathered gradient (default). HardEdge = crisp 0/1 seams between grass/dirt/path.")]
    public GroundStyle groundStyle = GroundStyle.Blended;

    [Header("Paths")]
    [Tooltip("Winding = pathCount wandering routes through a hub. Cardinal = a straight plaza-centred cross to N/E/S/W (ignores pathCount/pathWander).")]
    public PathStyle pathStyle = PathStyle.Winding;
    [Tooltip("Which ground texture the paths use (the 'select your tile as path' choice).")]
    public PathTile pathTile = PathTile.Road;
    [Tooltip("How many winding routes cross the map (0 = no paths). 2+ share a centre hub → a crossroads.")]
    public int pathCount = 1;
    [Tooltip("Full width of a path in world units (0 = no paths). Solid props are cleared from within half this + a margin.")]
    public float pathWidth = 3.5f;
    [Range(0f, 1f), Tooltip("How much the routes bend off a straight line (0 = straight, 1 = very winding).")]
    public float pathWander = 0.5f;
    [Range(0, 3), Tooltip("Rounds the angular corners into curves (0 = sharp/faceted look, 1 = gentle, 2-3 = smooth). Chaikin passes.")]
    public int pathSmoothness = 0;

    [Header("Content")]
    public List<RecipePropGroup> propGroups = new List<RecipePropGroup>();

}
