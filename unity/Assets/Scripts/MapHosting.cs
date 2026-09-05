using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Which maps this server is holding open, and when to let one go. Server-only; a player's machine holds the one
/// map they are standing in and has nothing to decide.
///
/// <para><b>Why a map is let go at all.</b> A map costs this process every creature in it, forever, whether or not
/// anybody is there to see them. Measured on this machine with nobody playing: the whole world at once is around
/// half of one processor core, and almost all of it is the creatures. A map nobody is in is that cost paid for
/// nothing.</para>
///
/// <para><b>Why letting go is safe.</b> A map is only released when nobody is standing in it, and everything in
/// it is only ever seen by somebody standing in it — so nothing anyone can see is taken away. A map that comes
/// back runs its spawners again and arrives fully populated, which is why nothing has to be remembered about
/// what was in it.</para>
///
/// <para><b>Why it is not switched off in place instead.</b> Switching a map off leaves every creature in it
/// still costing the two things the freeze never reaches — the engine still walks it every frame, and its shell
/// still pushes to the wire. Letting the map go destroys both, and it is the only version that gives the memory
/// back.</para>
/// </summary>
[DisallowMultipleComponent]
public class MapHosting : MonoBehaviour
{
    public static MapHosting Instance { get; private set; }

    /// <summary>How long a map may stand empty before it is let go. Long enough that walking through a door and
    /// straight back does not pay for a reload, short enough that a quiet field is not held all evening.</summary>
    public float emptyGraceSeconds = 45f;

    /// <summary>How often the held maps are looked at. This is a caretaker, not a hot path — the answer moves
    /// when somebody walks through a door, which is not something that happens sixty times a second.</summary>
    public float checkEverySeconds = 5f;

    // WHEN EACH MAP WAS LAST OCCUPIED, by name. A map that is missing from here has never been checked, which is
    // treated as "occupied just now" so a map cannot be let go the instant it finishes loading.
    readonly Dictionary<string, float> lastOccupied = new Dictionary<string, float>();
    readonly List<string> loadedNames = new List<string>();
    readonly List<string> toRelease = new List<string>();

    // Maps being brought up right now. A second request for the same map must wait for the first rather than
    // starting a second load of it.
    readonly HashSet<string> arriving = new HashSet<string>();

    float nextCheck;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        enabled = GameServer.IsServer;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Is this map standing right now?</summary>
    public static bool Holding(string map)
    {
        if (string.IsNullOrEmpty(map)) return false;
        var scene = SceneManager.GetSceneByName(map);
        return scene.IsValid() && scene.isLoaded;
    }

    /// <summary>Bring a map up because somebody is about to arrive in it, and keep it up long enough for them to
    /// get here. Safe to call for a map that is already standing — it just resets the clock on letting it go.
    ///
    /// <para>Answering a second caller while the first load is still running would start the same map twice, so
    /// a map on its way up is remembered until it lands.</para></summary>
    public static void Want(string map)
    {
        if (string.IsNullOrEmpty(map) || !GameServer.IsServer) return;
        var self = Instance;
        if (self == null) return;

        self.lastOccupied[map] = Time.time;
        if (Holding(map) || self.arriving.Contains(map)) return;

        self.arriving.Add(map);
        MapManager.LoadMap(map, null);
        GameLog.Info(LogCat.Map, "bringing up '" + map + "' — somebody is coming to it.");
    }

    void Update()
    {
        if (!GameServer.IsServer || Time.time < nextCheck) return;
        nextCheck = Time.time + checkEverySeconds;

        loadedNames.Clear();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || scene.name == MapManager.SystemsScene) continue;
            loadedNames.Add(scene.name);
            arriving.Remove(scene.name);
        }

        toRelease.Clear();
        for (int i = 0; i < loadedNames.Count; i++)
        {
            string map = loadedNames[i];
            if (PlayerUnits.AnyoneIn(map) || !lastOccupied.TryGetValue(map, out float when))
            {
                lastOccupied[map] = Time.time;
                continue;
            }
            if (Time.time - when >= emptyGraceSeconds) toRelease.Add(map);
        }

        // NEVER THE LAST ONE. A server holding nothing has nowhere to put somebody who arrives before it has been
        // told where they are going, and the cost of one quiet map is far less than the cost of that.
        if (toRelease.Count >= loadedNames.Count && toRelease.Count > 0) toRelease.RemoveAt(toRelease.Count - 1);

        for (int i = 0; i < toRelease.Count; i++) Release(toRelease[i]);
    }

    void Release(string map)
    {
        var scene = SceneManager.GetSceneByName(map);
        if (!scene.IsValid() || !scene.isLoaded) return;

        // THE CREATURES GO FIRST, THROUGH THE DOOR THEY ALREADY LEAVE BY. Every creature has a shell on the wire
        // that watches it, and that shell takes itself off the wire the moment the creature it watches is
        // switched off — the same path a death already uses. Letting the scene go without this would tear those
        // shells out from under the network layer instead, leaving it holding entries for things that are gone.
        int put = 0;
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            var mobs = roots[i].GetComponentsInChildren<MonsterAI3D>(true);   // perf-ok: once per map, only as that map is let go
            for (int m = 0; m < mobs.Length; m++)
            {
                if (mobs[m] == null || !mobs[m].gameObject.activeSelf) continue;
                mobs[m].gameObject.SetActive(false);
                put++;
            }
        }

        lastOccupied.Remove(map);
        GameLog.Info(LogCat.Map, "letting '" + map + "' go — nobody has been in it for "
                                 + Mathf.RoundToInt(emptyGraceSeconds) + "s. " + put + " creature(s) put away.");
        SceneManager.UnloadSceneAsync(scene);
    }
}
