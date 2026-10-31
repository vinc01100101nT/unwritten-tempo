using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for a <see cref="MapRecipe"/> asset: draws the normal fields plus a one-click <b>Generate Map</b>
/// button that bakes the recipe into <c>Assets/Scenes/&lt;mapName&gt;.unity</c> via <see cref="RecipeGenerator"/>.
/// </summary>
[CustomEditor(typeof(MapRecipe))]
public class MapRecipeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var recipe = (MapRecipe)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox($"Bakes Assets/Scenes/{recipe.mapName}.unity (overwrites it). Then press Play to walk the map.",
                                MessageType.Info);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("⚒ Generate Map", GUILayout.Height(34)))
            {
                var res = RecipeGenerator.Generate(recipe);
                if (res.ok)
                    EditorUtility.DisplayDialog("Generate Map",
                        $"Baked '{recipe.mapName}'\n→ {res.path}\n\n{res.props} props.\n\nPress Play to walk it.", "OK");
            }
        }
    }
}
