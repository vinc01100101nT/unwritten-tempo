using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>
/// Because <c>Setup Systems Scene</c> sets the editor Play-start scene to Systems, pressing Play
/// always boots the persistent layer first — but that means the map you had open isn't the one
/// that loads. This bridge fixes that: right before entering Play, it remembers the active map's
/// name so <see cref="GameRoot"/> loads THAT map under Systems (so "press Play in Town" tests
/// Town). In a build there's no editor start-scene override, so GameRoot just uses its firstMap.
/// </summary>
[InitializeOnLoad]
static class SystemsPlayMode
{
    static SystemsPlayMode()
    {
        EditorApplication.playModeStateChanged += OnChange;
    }

    static void OnChange(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;

        var active = SceneManager.GetActiveScene();
        if (active.IsValid() && !string.IsNullOrEmpty(active.name) && active.name != MapManager.SystemsScene)
            SessionState.SetString("unwritten.bootMap", active.name);
    }
}
