using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools ▸ unwritten ▸ New Empty Map Scene.
///
/// Unity always keeps at least one scene loaded, so "Remove Scene" is greyed out on the
/// last open scene — which makes it fiddly to get a clean slate for map work. This gives
/// you that clean slate in one click: it closes EVERY currently-open scene (Systems, any
/// map, etc.) and replaces them with a single, empty, untitled scene.
///
/// Run it BEFORE "Generate Town" (or before hand-painting tiles) so the generator's
/// preview isn't visually overlapped by the Systems scene's player/camera/HUD. Nothing
/// is deleted from disk — Systems is merely unloaded; reopen Systems.unity from the
/// Project window (or via your Setup tools) when you're ready to Play.
/// </summary>
public static class NewMapSceneTool
{
    [MenuItem("Tools/unwritten/New Empty Map Scene")]
    static void NewEmptyMapScene()
    {
        // Offer to save anything dirty first (Save / Don't Save / Cancel). This returns
        // false ONLY if the user hits Cancel — in which case we bail and touch nothing.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        // NewSceneMode.Single closes ALL open scenes and opens one fresh scene in their
        // place; NewSceneSetup.EmptyScene means no default Camera/Light — a truly blank
        // hierarchy (Single mode already makes it the active scene).
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);   // belt-and-suspenders: new objects land here

        Debug.Log("[unwritten] New empty map scene ready — all other scenes unloaded. " +
                  "Run Tools ▸ unwritten ▸ Generate Town, or paint tiles by hand. " +
                  "Reopen Systems.unity from the Project window before you Play.");
    }
}
