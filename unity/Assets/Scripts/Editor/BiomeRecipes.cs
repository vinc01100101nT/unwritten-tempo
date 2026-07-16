using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click author helper: stamps a STARTER <see cref="MapRecipe"/> asset for each <see cref="Biome"/> into
/// <c>Assets/3_Data/Recipes/Biomes/</c> so you can wire world-map cells immediately instead of hand-building every recipe.
/// Each starter sets the biome (ground base/patch textures + hue come from <c>RecipeGenerator.BiomeOf</c>), a sensible
/// <c>dirtAmount</c>, and a couple of generic prop/mob groups you then tune. Create-if-absent: it never clobbers a
/// recipe you've already edited — delete one to re-stamp it fresh.
///
/// Not a quick-fix/migration tool — it's a re-runnable content-authoring helper (keeper), so it lives on the main menu.
/// </summary>
public static class BiomeRecipes
{
    const string Dir = "Assets/3_Data/Recipes/Biomes";

    [MenuItem("Tools/unwritten/World/Create Biome Starter Recipes", priority = 189)]
    static void Create()
    {
        EnsureFolder(Dir);

        int made = 0, skipped = 0;
        foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
        {
            string key = b.ToString().ToLowerInvariant();
            string path = $"{Dir}/Biome_{b}.asset";
            if (AssetDatabase.LoadAssetAtPath<MapRecipe>(path) != null) { skipped++; continue; }

            var r = ScriptableObject.CreateInstance<MapRecipe>();
            r.id = $"map.biome-{key}";
            r.mapName = $"Biome_{b}";
            r.biome = b;
            r.randomizeSeed = false;
            r.seed = 12345;
            Configure(r, b);

            AssetDatabase.CreateAsset(r, path);
            made++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        var first = AssetDatabase.LoadAssetAtPath<MapRecipe>($"{Dir}/Biome_Grassland.asset");
        if (first != null) { Selection.activeObject = first; EditorGUIUtility.PingObject(first); }

        EditorUtility.DisplayDialog("Biome Starter Recipes",
            $"Created {made} new starter recipe(s), skipped {skipped} existing in {Dir}/.\n\n"
            + "Each is a tunable starting point — open one, drop in the props/mobs you want, then point a world-map "
            + "cell's template at it (or set the cell's biome). Grassland reproduces the original look exactly.", "OK");
    }

    // Biome-appropriate starter content. Categories auto-fill from Assets/1_Authored/Prefabs/Props; empty mob archetype = a
    // weighted mix of all field mobs. Densities lean sparse for harsh biomes, lush for grassland.
    static void Configure(MapRecipe r, Biome b)
    {
        r.dirtAmount = b switch
        {
            Biome.Desert    => 0.55f,
            Biome.Volcanic  => 0.45f,
            Biome.Corrupted => 0.40f,
            Biome.Snow      => 0.25f,
            _               => 0.18f,
        };

        r.propGroups = new List<RecipePropGroup>();
        r.mobGroups  = new List<RecipeMobGroup>();

        // Rocks read in every biome; trees/foliage scale with how lush the biome is.
        float treeDensity = b switch
        {
            Biome.Grassland => 0.020f,
            Biome.Snow      => 0.012f,
            Biome.Desert    => 0.004f,
            Biome.Volcanic  => 0.003f,
            Biome.Corrupted => 0.006f,
            _               => 0.012f,
        };

        if (treeDensity > 0f)
            r.propGroups.Add(new RecipePropGroup
            {
                label = "Trees", category = PropCategory.Trees, placement = PlacementRule.Scatter,
                amountMode = AmountMode.Density, density = treeDensity, solid = true, minSpacing = 2.5f, avoidPath = true,
            });

        r.propGroups.Add(new RecipePropGroup
        {
            label = "Rocks", category = PropCategory.Rocks, placement = PlacementRule.Scatter,
            amountMode = AmountMode.Density, density = b == Biome.Volcanic || b == Biome.Desert ? 0.012f : 0.008f,
            solid = true, minSpacing = 2f, avoidPath = true,
        });

        if (b == Biome.Grassland || b == Biome.Snow)
            r.propGroups.Add(new RecipePropGroup
            {
                label = "Ground cover", category = PropCategory.Grass, placement = PlacementRule.Scatter,
                amountMode = AmountMode.Density, density = 0.03f, solid = false, minSpacing = 0.8f,
            });

        r.mobGroups.Add(new RecipeMobGroup
        {
            label = "Wildlife", archetypeName = "", amountMode = AmountMode.Density,
            density = 0.02f, minSpacing = 1.8f, clearFromSpawn = 8f,
        });
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
