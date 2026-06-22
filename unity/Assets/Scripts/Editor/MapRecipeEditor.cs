using UnityEditor;
using UnityEngine;

/// <summary>Inspector for <see cref="MapRecipe"/>: the default fields plus the big "Generate" buttons that bake
/// the recipe into a scene via <see cref="RecipeGenerator"/>. Also provides a one-click starter recipe so you
/// have a working town-shaped example to tweak instead of a blank asset.</summary>
[CustomEditor(typeof(MapRecipe))]
public class MapRecipeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var r = (MapRecipe)target;

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Generate Map", GUILayout.Height(30)))
            {
                var res = RecipeGenerator.Generate(r);
                if (res.ok)
                    EditorUtility.DisplayDialog("Map generated",
                        $"Built {res.path}\n• {res.props} props\n• {res.mobs} mobs\n\n"
                        + "PLAY: the player/camera live in Systems — run 'Setup Systems Scene' once, then press Play.", "OK");
            }
            if (GUILayout.Button("Generate + Connect to maps…"))
            {
                var res = RecipeGenerator.Generate(r);
                if (res.ok) MapLinkWindow.Open(res.path, res.props, res.mobs);
            }
        }
        EditorGUILayout.HelpBox("Add prop groups (exact prefabs and/or a category) with a placement rule each — "
            + "StreetRows of Houses + Center Landmarks reads as a town; Scatter Trees + a mob group reads as a "
            + "field. Same generator, different content.", MessageType.None);
    }

    // ── STARTER RECIPE ────────────────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/unwritten/World/New Map Recipe (starter)", priority = 190)]
    static void CreateStarter()
    {
        var r = CreateInstance<MapRecipe>();
        r.mapName = "StarterTown";
        r.sizeX = r.sizeZ = 60f;
        r.dirtAmount = 0.18f;
        r.pathTile = PathTile.Road;
        r.pathCount = 2;          // two routes → a crossroads through the town centre
        r.pathWidth = 4.5f;
        r.pathWander = 0.45f;
        r.randomizeSeed = true;

        r.propGroups.Add(new RecipePropGroup {
            label = "Houses", category = PropCategory.Houses, placement = PlacementRule.StreetRows,
            amountMode = AmountMode.Count, count = 12, solid = true, scale = 0.3f, minSpacing = 5f, rowOffset = 4f });
        r.propGroups.Add(new RecipePropGroup {
            label = "Landmarks", category = PropCategory.Landmarks, placement = PlacementRule.Center,
            amountMode = AmountMode.Count, count = 2, solid = true, scale = 0.3f, minSpacing = 8f, clearFromCenter = 4f });
        r.propGroups.Add(new RecipePropGroup {
            label = "Treeline", category = PropCategory.Trees, placement = PlacementRule.EdgeRing,
            solid = true, scale = 0.3f, minSpacing = 2.6f });
        r.propGroups.Add(new RecipePropGroup {
            label = "Rocks", category = PropCategory.Rocks, placement = PlacementRule.Scatter,
            amountMode = AmountMode.Density, density = 0.006f, solid = true, scale = 0.3f, minSpacing = 2f, avoidPath = true });
        r.propGroups.Add(new RecipePropGroup {
            label = "Flowers", category = PropCategory.Flowers, placement = PlacementRule.Scatter,
            amountMode = AmountMode.Density, density = 0.03f, solid = false, scale = 0.3f, minSpacing = 0.6f, avoidPath = true });
        r.propGroups.Add(new RecipePropGroup {
            label = "Grass", category = PropCategory.Grass, placement = PlacementRule.Scatter,
            amountMode = AmountMode.Density, density = 0.05f, solid = false, scale = 0.3f, minSpacing = 0.5f, avoidPath = true });

        r.mobGroups.Add(new RecipeMobGroup {
            label = "Wildlife", archetypeName = "", amountMode = AmountMode.Density, density = 0.02f,
            minSpacing = 1.8f, clearFromSpawn = 8f });

        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/StarterTown Recipe.asset");
        AssetDatabase.CreateAsset(r, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = r;
        EditorGUIUtility.PingObject(r);
        EditorUtility.DisplayDialog("Map Recipe created",
            $"Created {path} — a town-shaped starter (houses along a street, tavern & windmill, treeline, decor, mobs).\n\n"
            + "Tweak its groups in the Inspector, then click 'Generate Map'. Duplicate it and swap the content to "
            + "make fields, dungeons, etc.", "OK");
    }
}
