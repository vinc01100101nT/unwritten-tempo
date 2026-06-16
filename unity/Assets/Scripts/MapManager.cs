using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Swaps gameplay maps additively UNDER the persistent <c>Systems</c> scene, so the HUD,
/// cursor, character data, player and camera (all living in Systems) are never unloaded —
/// no <c>DontDestroyOnLoad</c> anywhere. A <see cref="Portal"/> calls <see cref="Travel"/>;
/// <see cref="GameRoot"/> calls <see cref="LoadMap"/> for the first map.
///
/// On each map load it makes the new map the ACTIVE scene (so runtime spawns / lighting /
/// baked props belong to it and unload with it), and neutralises any camera / audio listener /
/// event system a map still carries from before the split — only the persistent ones in
/// Systems should run.
/// </summary>
public static class MapManager
{
    public const string SystemsScene = "Systems";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>Portal travel: load the target map additively, then unload the current one.
    /// Systems is the active-scene exception and is never unloaded.</summary>
    public static void Travel(string targetScene, string spawnId)
    {
        if (string.IsNullOrEmpty(targetScene)) return;

        Scene previous = SceneManager.GetActiveScene();   // the map we're leaving (Systems is never active)
        SceneTravel.Target = spawnId;                     // PersistentPlayer reads this on arrival

        var op = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Additive);
        if (op == null) return;                           // target not in Build Settings
        op.completed += _ =>
        {
            if (previous.IsValid() && previous.isLoaded && previous.name != SystemsScene)
                SceneManager.UnloadSceneAsync(previous);
        };
    }

    /// <summary>Boot a map with nothing to unload (the first map under Systems).</summary>
    public static void LoadMap(string sceneName, string spawnId)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        SceneTravel.Target = spawnId;
        SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
    }

    /// <summary>Make a loaded map the active scene and run its housekeeping. Public so
    /// <see cref="GameRoot"/> can adopt a map that was already open in the editor.</summary>
    public static void Activate(Scene scene)
    {
        if (!scene.IsValid() || scene.name == SystemsScene) return;
        SceneManager.SetActiveScene(scene);
        StripForeignGlobals(scene);
        BindCameraBounds(scene);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Activate(scene);

    // Maps authored before the Systems split may still carry their own camera / audio listener /
    // event system. Disable them (and strip foreign CameraFollow2D so the persistent camera is the
    // only follow rig) so the singletons in Systems win.
    static void StripForeignGlobals(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var follow in root.GetComponentsInChildren<CameraFollow2D>(true)) Object.Destroy(follow);
            foreach (var cam in root.GetComponentsInChildren<Camera>(true)) { cam.enabled = false; cam.tag = "Untagged"; }
            foreach (var al in root.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;
            foreach (var es in root.GetComponentsInChildren<EventSystem>(true)) es.gameObject.SetActive(false);
        }
    }

    // Clamp the persistent camera to THIS map's ground tilemap (bounds are per-map).
    static void BindCameraBounds(Scene scene)
    {
        var follow = Object.FindFirstObjectByType<CameraFollow2D>();
        if (follow == null) return;

        Tilemap ground = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var tm in root.GetComponentsInChildren<Tilemap>(true))
            {
                string n = tm.name.ToLowerInvariant();
                if (n.Contains("ground") || n.Contains("base") || n.Contains("floor") || n.Contains("terrain"))
                { ground = tm; break; }
            }
            if (ground != null) break;
        }
        follow.boundsTilemap = ground;   // null = no clamping, which is fine
    }
}
