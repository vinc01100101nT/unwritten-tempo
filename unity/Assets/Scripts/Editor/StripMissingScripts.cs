using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Maintenance: removes "missing script" components (a Behaviour whose C# script was deleted/renamed, leaving a
/// dangling reference) from every object in the currently-open scene(s). Idempotent — safe to re-run anytime the
/// Console shows "The referenced script (Unknown) on this Behaviour is missing!".
/// </summary>
public static class StripMissingScripts
{
    [MenuItem("Tools/unwritten/Strip Missing Scripts (open scenes)")]
    public static void Run()
    {
        int total = 0, objects = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (n > 0) { total += n; objects++; }
                }

            if (total > 0) EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[unwritten] Stripped {total} missing script(s) from {objects} object(s)." +
                  (total > 0 ? " Save the scene (Ctrl+S) to persist." : " Nothing to clean."));
    }
}
