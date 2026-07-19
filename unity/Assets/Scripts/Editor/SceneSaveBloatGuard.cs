using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// THE single, centralized anti-bloat net. Runs automatically on EVERY edit-mode scene save — no per-file
/// wiring, no attaching anything to new effect/object scripts, ever. Right before a scene is written, it
/// strips any ROOT GameObject that is unmistakably runtime junk:
///   • name ends with "(Clone)"  — Unity's Instantiate suffix; authored scene objects are never named this.
///   • hideFlags has DontSaveInEditor — a runtime-only object that leaked into the scene graph.
/// Everything the game spawns at runtime is one of those two, so this quietly keeps every scene clean no
/// matter how many new effects/objects get added later. It only ever removes provably-runtime objects, so
/// it can't delete authored content. Add a new effect tomorrow → nothing to do; this catches it.
/// </summary>
[InitializeOnLoad]
public static class SceneSaveBloatGuard
{
    static SceneSaveBloatGuard()
    {
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    static void OnSceneSaving(Scene scene, string path)
    {
        if (Application.isPlaying) return;

        var stripped = new StringBuilder();
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!IsRuntimeJunk(root)) continue;
            stripped.Append($"\n   • {root.name} (flags={root.hideFlags})");
            Object.DestroyImmediate(root);
            count++;
        }
        if (count > 0)
            Debug.LogWarning($"[unwritten] SceneSaveBloatGuard: stripped {count} runtime object(s) from '{scene.name}' before save:{stripped}");
    }

    static bool IsRuntimeJunk(GameObject go)
        => go.name.EndsWith("(Clone)") || (go.hideFlags & HideFlags.DontSaveInEditor) != 0;
}
