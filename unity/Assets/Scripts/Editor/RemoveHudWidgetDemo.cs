using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

// QUICKFIX 2026-06-25 — strips the baked "HudWidgetDemoCanvas" left in Systems.unity by the
// now-deleted "Build HUD Widgets" preview harness. The demo window is made of plain baked UI
// GameObjects, so deleting the scripts alone doesn't remove it — Unity has to destroy the subtree
// and re-save the scene. Run this once, then delete this file + its .meta.
public static class RemoveHudWidgetDemo
{
    const string ScenePath = "Assets/Scenes/Systems.unity";

    [MenuItem("Tools/unwritten/Quick Fixes/Remove HUD Widgets Demo")]
    static void Remove()
    {
        var scene = EditorSceneManager.GetSceneByPath(ScenePath);
        bool openedHere = false;
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            openedHere = true;
        }

        int removed = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "HudWidgetDemoCanvas")
            {
                Object.DestroyImmediate(root);
                removed++;
            }
        }

        if (removed == 0)
        {
            Debug.Log("[unwritten] No HudWidgetDemoCanvas found in Systems.unity — nothing to remove.");
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            return;
        }

        // The demo may have created the scene's only EventSystem as a child of the canvas it just
        // deleted — re-add one so game UI still receives clicks.
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            EditorSceneManager.MoveGameObjectToScene(es, scene);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedHere) EditorSceneManager.CloseScene(scene, true);

        Debug.Log($"[unwritten] Removed the HUD Widgets demo canvas from {ScenePath} and saved. " +
                  "You can now delete Assets/Scripts/Editor/RemoveHudWidgetDemo.cs (this one-shot cleanup tool).");
    }
}
