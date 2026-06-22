using System.Collections.Generic;
using UnityEngine;

/// <summary>Known Pxltgr prop families. Used to auto-fill a group's prefabs from Assets/Prefabs/Props when its
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
}

public enum AmountMode { Density, Count }

/// <summary>Which Pxltgr ground texture the routed paths use. Maps to the matching <c>ground_*</c> prop's Base Map.</summary>
public enum PathTile { Road, Soil, Stones, Rock }

[System.Serializable]
public class RecipePropGroup
{
    public string label = "Props";

    [Tooltip("Exact prefabs to place. If this is empty, the Category below is used to auto-fill from the pack.")]
    public List<GameObject> prefabs = new List<GameObject>();

    [Tooltip("Used ONLY when Prefabs is empty — pulls the known Pxltgr prefabs of this kind from Assets/Prefabs/Props.")]
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
}

[System.Serializable]
public class RecipeMobGroup
{
    public string label = "Mobs";
    [Tooltip("Archetype name from MobRoster (Slime, Bat, Kappa, Flam, Cyclope, or Boss). Empty = weighted mix of all.")]
    public string archetypeName = "";
    public AmountMode amountMode = AmountMode.Density;
    [Tooltip("Density mode: mobs per square world-unit (try ~0.02–0.04).")]
    public float density = 0.02f;
    public int count = 12;
    public float minSpacing = 1.8f;
    [Tooltip("Keep mobs this far from the player spawn so you don't arrive in a swarm.")]
    public float clearFromSpawn = 8f;
}

/// <summary>
/// The ONE content-driven world-generator recipe for <i>unwritten</i>. Author it in the Inspector
/// (Assets ▸ Create ▸ unwritten ▸ Map Recipe) and bake it with <c>RecipeGenerator</c> (the Generate button).
/// There is no "town generator" vs "field generator": a map is simply whatever ground + prop groups + mobs you
/// put in the recipe, so the same generator yields a town (Houses in StreetRows + a tavern), a wild field
/// (scattered trees + mobs), a dungeon (your dungeon prefabs), or anything else — purely from content.
///
/// All maps use the single blended grass→dirt ground (no tile seams). Determinism: <see cref="seed"/> drives
/// every random choice, so the same recipe + seed bakes the same map; tick <see cref="randomizeSeed"/> to roll a
/// fresh one (written back to <see cref="seed"/> to stay reproducible).
/// </summary>
[CreateAssetMenu(fileName = "MapRecipe", menuName = "unwritten/Map Recipe", order = 0)]
public class MapRecipe : ScriptableObject
{
    [Header("Identity")]
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

    [Header("Paths")]
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
    public List<RecipeMobGroup> mobGroups = new List<RecipeMobGroup>();

    [Header("Ambience")]
    [Tooltip("Add the looping day/night forest ambience to the map.")]
    public bool forestAmbience = true;
}
