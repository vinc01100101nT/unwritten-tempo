using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Guarantees that pressing Play boots the persistent <c>Systems</c> scene — the ONLY scene that
/// owns the camera (plus player / HUD / cursor / character). Maps (Town, NatureStage) are
/// content-only and carry no camera (<see cref="MapManager.StripForeignGlobals"/> even disables any
/// a map still holds), so if Play starts in a map directly you get a black "No cameras rendering"
/// screen. This bridge has two jobs:
///
///   1. <b>Pin the editor Play-start scene to Systems.unity.</b> "Setup Systems Scene"
///      (<see cref="SystemsSceneBuilder"/>) sets this once, but the value lives in the project's
///      <c>Library/</c> folder, which is NOT committed — a fresh clone / repo relocation / Library
///      rebuild silently resets it to null, and the next Play boots a camera-less map. Because this
///      type is <see cref="InitializeOnLoadAttribute"/>, it runs on every editor load and re-pins
///      the start scene, so the repo self-heals instead of needing a manual re-run.
///
///   2. <b>Remember the active map</b> so <see cref="GameRoot"/> loads THAT map under Systems
///      (press Play in Town → test Town), not just its default firstMap.
///
/// In a build there's no editor start-scene override; scene index 0 is Systems and GameRoot uses
/// its firstMap.
/// </summary>
[InitializeOnLoad]
static class SystemsPlayMode
{
    const string SystemsScenePath = "Assets/Scenes/Systems.unity";
    const string SandboxScene = "CharacterSandbox";   // the standalone character-select playground

    static SystemsPlayMode()
    {
        // Defer off the load tick so the AssetDatabase is ready to resolve the SceneAsset.
        EditorApplication.delayCall += EnsurePlayStartsAtSystems;
        EditorApplication.playModeStateChanged += OnChange;
    }

    static void OnChange(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;

        var active = SceneManager.GetActiveScene();

        // The character sandbox and any *Spike* scene own their OWN camera and aren't part of the game, so don't
        // force Systems — clear the override and let them boot themselves. (Re-pinned automatically next time a
        // real map is played.) This is why pressing Play in a spike scene no longer errors on a missing boot-map.
        if (active.IsValid() && (active.name == SandboxScene || active.name.Contains("Spike")))
        {
            EditorSceneManager.playModeStartScene = null;
            return;
        }

        EnsurePlayStartsAtSystems();   // belt-and-suspenders in case it was cleared this session

        if (active.IsValid() && !string.IsNullOrEmpty(active.name) && active.name != MapManager.SystemsScene)
            SessionState.SetString("unwritten.bootMap", active.name);
    }

    // Point the editor Play-start scene at Systems so the camera always boots, whatever map is open.
    // No-op until Systems.unity exists (run Setup Systems Scene first) or if it's already pinned.
    static void EnsurePlayStartsAtSystems()
    {
        var systems = AssetDatabase.LoadAssetAtPath<SceneAsset>(SystemsScenePath);
        if (systems == null || EditorSceneManager.playModeStartScene == systems) return;

        EditorSceneManager.playModeStartScene = systems;
        UnityEngine.Debug.Log("[unwritten] Play-start scene pinned to Systems.unity so the camera always boots " +
            "(self-heal: this setting lives in the uncommitted Library and is lost on a fresh clone). " +
            "Press Play from any map.");
    }
}
