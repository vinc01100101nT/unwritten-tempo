using UnityEditor;
using UnityEngine;

// QUICKFIX 2026-06-28 — scaffolds a starter DungeonRecipe.asset for the Build World dungeon template slot;
// delete after the dungeon recipe exists and has been hand-tuned (the asset is the keeper, not this tool).
public static class DungeonRecipeScaffold
{
    const string AssetPath = "Assets/DungeonRecipe.asset";

    [MenuItem("Tools/unwritten/Quick Fixes/Create Starter Dungeon Recipe")]
    public static void Create()
    {
        var existing = AssetDatabase.LoadAssetAtPath<MapRecipe>(AssetPath);
        if (existing != null &&
            !EditorUtility.DisplayDialog("Dungeon Recipe exists",
                $"{AssetPath} already exists. Overwrite it with fresh starter defaults?",
                "Overwrite", "Cancel"))
            return;

        var r = ScriptableObject.CreateInstance<MapRecipe>();

        r.id = "map.dungeon";
        r.mapName = "DungeonTemplate";   // Build World overrides name/seed/size/biome per cell
        r.seed = 24680;
        r.randomizeSeed = true;

        // Dungeons in the world map are 40×40; the per-cell override sets the real size anyway.
        r.sizeX = 40f;
        r.sizeZ = 40f;
        r.dirtAmount = 0.85f;            // mostly bare rock floor, little grass
        r.biome = Biome.Volcanic;        // per-cell biome (Volcanic/Corrupted) overrides this
        r.groundStyle = GroundStyle.HardEdge;

        // A tight cross so there's always a clear walkable spine to the exits.
        r.pathStyle = PathStyle.Cardinal;
        r.pathTile = PathTile.Rock;
        r.pathCount = 2;
        r.pathWidth = 3f;
        r.pathWander = 0.15f;
        r.pathSmoothness = 0;

        r.forestAmbience = false;        // no chirping birds underground

        // Walls hugging the plot edge (the wall kit is loaded by name, no prefabs to assign).
        r.propGroups.Add(new RecipePropGroup
        {
            label = "Walls",
            placement = PlacementRule.PerimeterWall,
            solid = true,
            scale = 0.3f,
            sway = SwayMode.Off,
        });

        // Scattered rocks/boulders as cover + obstacles, kept off the path and away from spawn.
        r.propGroups.Add(new RecipePropGroup
        {
            label = "Boulders",
            category = PropCategory.Rocks,
            placement = PlacementRule.Scatter,
            amountMode = AmountMode.Density,
            density = 0.03f,
            solid = true,
            scale = 0.35f,
            scaleJitter = 0.25f,
            minSpacing = 2.5f,
            clearFromCenter = 4f,
            avoidPath = true,
            randomYaw = true,
            sway = SwayMode.Off,
        });

        // Biome-themed mob pack, mid-to-elite tier — the per-cell biome decides the actual roster.
        r.mobGroups.Add(new RecipeMobGroup
        {
            label = "Dungeon mobs",
            archetypeName = "",
            minTier = 2,
            maxTier = 4,
            includeCommons = false,
            amountMode = AmountMode.Density,
            density = 0.035f,
            minSpacing = 2f,
            clearFromSpawn = 6f,
        });

        AssetDatabase.DeleteAsset(AssetPath);
        AssetDatabase.CreateAsset(r, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorGUIUtility.PingObject(r);
        Selection.activeObject = r;
        Debug.Log($"[DungeonRecipeScaffold] Created {AssetPath}. Drag it into Build World's Dungeon template slot.");
    }
}
