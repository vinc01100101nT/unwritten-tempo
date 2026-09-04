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

    // BEFORE ANYTHING ELSE GETS A SAY. A map is set down in its place and every other map is let go the instant
    // it arrives, and both have to happen before anything in the game reacts to that map — the avatar re-seats
    // itself onto a spawn point the moment a map loads, and it would otherwise be standing on ground that is
    // about to move out from under it. Signing up this early puts this first in the queue for every machine.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    /// <summary>Portal travel: load the target map additively. The map being left goes on its own, through
    /// <see cref="LetGoOfEveryOtherMap"/> — the ONE rule that decides how many maps a machine holds, so a door
    /// does not get a second answer of its own.</summary>
    public static void Travel(string targetScene, string spawnId)
    {
        if (string.IsNullOrEmpty(targetScene)) return;
        SceneTravel.Target = spawnId;                     // PersistentPlayer3D reads this on arrival
        SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Additive);
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
                GameLog.Warn(LogCat.Map, "the '" + SystemsScene + "' scene is not loaded, so things the server sent "
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
        // ITS OWN GROUND FIRST, before anything in it takes a step or anything is built into it.
        ParkAwayFromTheOthers(scene);
        // ONE map on a machine somebody plays on, and this is the moment it is decided — every way a map can
        // arrive comes through here, including a map already open at boot that is adopted rather than loaded.
        LetGoOfEveryOtherMap(scene);
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

    // A map that goes away takes its place with it. Nothing unloads a map on a server today, so this is here
    // for the day something does: left behind, the old place would be handed out for a map now standing
    // somewhere else entirely, and every position for it would be wrong by the width of the gap with nothing
    // saying so.
    static void OnSceneUnloaded(Scene scene) => parked.Remove(scene.name);

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Activate(scene);
        // A map arriving is the one thing a server operator needs to see in the log: it is the difference
        // between "asked for the town" and "the town is up". Server-only — a player sees the map instead.
        if (GameServer.IsServer && scene.name != SystemsScene)
            GameLog.Info(LogCat.Server, "map '" + scene.name + "' loaded — "
                      + scene.rootCount + " root objects, active scene is now '"
                      + SceneManager.GetActiveScene().name + "'");
    }

    /// <summary>A MACHINE SOMEBODY PLAYS ON HOLDS EXACTLY ONE MAP: the one they are standing in. Every other
    /// gameplay map that happens to be open goes.
    ///
    /// <para>Two maps open are two sets of ground, walls and doors sharing one set of coordinates, so the second
    /// one is drawn straight through the first: a field's trees standing in the middle of a town, walls nobody
    /// can see that still stop you, and a door you never walked to that still takes you somewhere. It does not
    /// matter how the second one got there — two maps left open in the editor, a map adopted at boot while
    /// another was still loaded, or a door that has just brought a new one in — the answer is the same.</para>
    ///
    /// <para>A SERVER is the exception, and holds as many as it likes: nobody stands in them, they are open so
    /// their creatures are alive before anybody walks in, and it draws nothing at all.</para></summary>
    static void LetGoOfEveryOtherMap(Scene keep)
    {
        if (GameServer.IsServer) return;

        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            var s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded || s.name == SystemsScene || s == keep) continue;
            SceneManager.UnloadSceneAsync(s);
        }
    }

    /// <summary>Put <paramref name="go"/> in the map that made it, so it belongs to that map and no other.
    ///
    /// <para>THE answer to "which map is this thing in", because that question is answered by the scene the
    /// thing sits in — a creature reads its own map off itself and tells every watching machine. Unity drops
    /// anything built while the game runs into whichever map was loaded LAST, which on a machine holding
    /// several is not the map that made it. MEASURED on a server holding a town and a field: all twelve of the
    /// town's people were built into the field, because the model each of them waits for arrives after the
    /// second map has come up. Creatures and dropped loot are made the same way and go the same wrong
    /// place.</para></summary>
    public static void PutInMap(GameObject go, Scene map)
    {
        if (go == null || go.transform.parent != null) return;   // a child follows its parent, not this
        if (!map.IsValid() || !map.isLoaded || map.name == SystemsScene) return;
        if (go.scene == map) return;

        SceneManager.MoveGameObjectToScene(go, map);
    }

    /// <summary>The map this machine is standing in, as a scene. Invalid when there is no map yet.</summary>
    public static Scene CurrentScene => SceneManager.GetSceneByName(CurrentMap);

    // ---- maps are kept APART -----------------------------------------------------------------------------
    //
    // Every map is authored around its own middle, so two of them open at once occupy the SAME ground: a town's
    // houses and a field's trees standing inside each other, their creatures shoving each other about, and a
    // raycast looking for the floor finding the other map's. Only a machine holding several has the problem, and
    // that is only ever a server — so a server sets each map it holds down in its own place, far enough away
    // that nothing in one can reach anything in another.
    //
    // A player's machine holds one map and parks nothing, which is why WHERE a map is parked never has to be
    // agreed between machines: what crosses the wire is where somebody is INSIDE their map, and each machine
    // adds back its own answer for where it put that map. A player's answer is always nowhere.

    /// <summary>How far apart two parked maps are set down. Far past anything that reaches: sight, a blow, a
    /// creature's hunt, a raycast for the ground — while staying near enough the middle of the world that the
    /// numbers keep their precision.</summary>
    const float ParkingGap = 2000f;

    static readonly System.Collections.Generic.Dictionary<string, Vector3> parked =
        new System.Collections.Generic.Dictionary<string, Vector3>();

    /// <summary>Where this machine set <paramref name="mapName"/> down. Nowhere, for anybody but a server.</summary>
    public static Vector3 ParkedAt(string mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return Vector3.zero;
        return parked.TryGetValue(mapName, out var at) ? at : Vector3.zero;
    }

    /// <summary>A world position, told as where it is INSIDE its map — the only form that means the same thing
    /// on two machines that have set the same map down in different places.</summary>
    public static Vector3 InMap(Vector3 world, string mapName) => world - ParkedAt(mapName);

    /// <summary>The other way round: a position inside a map, put where THIS machine keeps that map.</summary>
    public static Vector3 InWorld(Vector3 inMap, string mapName) => inMap + ParkedAt(mapName);

    // Set a newly arrived map down in its own place. The first one a machine holds stays where it was authored,
    // so a machine that only ever holds one — every player's — never moves anything at all.
    //
    // The move happens the moment the map has finished loading, which is after the things in it have been
    // created but before any of them has run a first frame. Anything in a map that wrote down where it stood
    // BEFORE that would be remembering the wrong place; nothing in these maps does, and a new one must not.
    static void ParkAwayFromTheOthers(Scene scene)
    {
        if (!GameServer.IsServer || parked.ContainsKey(scene.name)) return;

        Vector3 at = new Vector3(parked.Count * ParkingGap, 0f, 0f);
        parked[scene.name] = at;
        if (at == Vector3.zero) return;

        foreach (var root in scene.GetRootGameObjects()) root.transform.position += at;

        // AND THE PHYSICS ENGINE HAS TO BE TOLD, or the map is only moved for things that LOOK at it. This
        // project keeps the automatic sync switched off, so until the next physics step every raycast still
        // finds the ground where it used to be — which is nowhere, once the ground has gone. The very first
        // thing this map does is ask sixteen times where its floor is so it can put its creatures on it, and a
        // spawner that never finds a floor quietly drops its whole population on its own doorstep: MEASURED,
        // all 32 of the field's creatures standing on one spot. Every editor tool here that moves ground before
        // raycasting it already does this; the one that moves a whole map at runtime must too.
        Physics.SyncTransforms();

        GameLog.Info(LogCat.Server, "map '" + scene.name + "' set down " + at.x + " away, clear of the others.");
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
