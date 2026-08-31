using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Swaps gameplay maps additively UNDER the persistent <c>Systems</c> scene, so the HUD,
/// cursor, character data, player and camera (all living in Systems) are never unloaded —
/// no <c>DontDestroyOnLoad</c> anywhere. A <see cref="Portal3D"/> calls <see cref="Travel"/>;
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

    /// <summary>The map THIS machine is standing in, by scene name — the one answer to "where am I".
    ///
    /// <para>A player's machine holds exactly one map at a time (Systems is never the active one), so the active
    /// scene IS that answer. A SERVER holds several at once and this is meaningless there: it is deliberately never
    /// read on the server, which learns each person's map from that person (see <see cref="NetPlayer"/>) and each
    /// creature's from the scene the creature actually lives in.</para></summary>
    public static string CurrentMap { get; private set; } = "";

    /// <summary>Bumped every time <see cref="CurrentMap"/> changes. Anything that must re-decide on a map change
    /// compares this integer rather than comparing map NAMES every frame — the watched creatures ask once each per
    /// frame, and a string compare at that rate is the cost PERFORMANCE.md bans.</summary>
    public static int MapVersion { get; private set; }

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
        SceneTravel.Target = spawnId;                     // PersistentPlayer3D reads this on arrival

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

    /// <summary>Park <paramref name="go"/> in the scene that is never unloaded, so a map swap cannot take it.
    ///
    /// <para>THE one place that answers "this object must outlive the map I am standing in". Anything a SERVER
    /// told this machine about — another player's body, a creature — belongs to the world, not to the map the
    /// listener happens to be in when it arrives. Mirror builds those with a plain Instantiate, which drops them
    /// in the ACTIVE scene (the current map), and <see cref="Travel"/> then unloads that scene and destroys them.
    /// Nothing re-sends them, so everything the server had already announced was gone for good after one portal.</para></summary>
    public static void KeepAcrossMaps(GameObject go)
    {
        if (go == null || go.transform.parent != null) return;   // a child follows its parent, not this

        var systems = SceneManager.GetSceneByName(SystemsScene);
        if (!systems.IsValid() || !systems.isLoaded)
        {
            // SAID OUT LOUD, ONCE. This one call is the whole of "survives a portal", and every way it can decline
            // to act leaves behaviour identical to the bug it fixes — so a silent no-op is indistinguishable from a
            // fix that ran and did not work, which is the most expensive kind of failure to chase.
            if (!warnedNoSystems)
            {
                warnedNoSystems = true;
                Debug.LogWarning("[map] the '" + SystemsScene + "' scene is not loaded, so things the server sent "
                                 + "are living in the current map and will be destroyed when it is left.");
            }
            return;
        }

        // NEVER SERIALIZED. This is the scene that is actually open and saved in the editor, so a Play-only object
        // parked here would be baked into the scene file and re-spawn as a phantom on every load ever after. The
        // bodies already carry this; the network shells did not, and this is where they arrive.
        go.hideFlags = HideFlags.DontSaveInEditor;
        SceneManager.MoveGameObjectToScene(go, systems);
    }

    static bool warnedNoSystems;

    /// <summary>Make a loaded map the active scene and run its housekeeping. Public so
    /// <see cref="GameRoot"/> can adopt a map that was already open in the editor.</summary>
    public static void Activate(Scene scene)
    {
        if (!scene.IsValid() || scene.name == SystemsScene) return;
        SceneManager.SetActiveScene(scene);
        // WHERE THIS MACHINE IS, written down the moment it changes rather than asked for later.
        // A server activates every map it holds, so this ends up naming the last one it loaded — which is why the
        // server never reads it and each side is told its map by whoever actually knows.
        if (CurrentMap != scene.name)
        {
            CurrentMap = scene.name;
            MapVersion++;
        }
        StripForeignGlobals(scene);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Activate(scene);
        // A map arriving is the one thing a server operator needs to see in the log: it is the difference
        // between "asked for the town" and "the town is up". Server-only — a player sees the map instead.
        if (GameServer.IsServer && scene.name != SystemsScene)
            Debug.Log("[server] map '" + scene.name + "' loaded — "
                      + scene.rootCount + " root objects, active scene is now '"
                      + SceneManager.GetActiveScene().name + "'");
    }

    // Maps authored before the Systems split may still carry their own camera / audio listener /
    // event system. Disable them so the singletons in Systems win.
    static void StripForeignGlobals(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var cam in root.GetComponentsInChildren<Camera>(true)) { cam.enabled = false; cam.tag = "Untagged"; }
            foreach (var al in root.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;
            foreach (var es in root.GetComponentsInChildren<EventSystem>(true)) es.gameObject.SetActive(false);
            // A map may carry its own Sun for edit-mode lighting; Systems owns the single runtime light.
            foreach (var lt in root.GetComponentsInChildren<Light>(true)) lt.enabled = false;
        }
    }
}
