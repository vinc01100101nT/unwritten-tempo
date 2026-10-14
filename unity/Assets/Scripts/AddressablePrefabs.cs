using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

/// <summary>
/// THE one way to fetch a prefab asset by Addressable address at runtime. Every system that draws prefabs from
/// Addressables goes through here — mobs (<see cref="MobSpawner"/>) and character models (<see cref="CharacterModels"/>)
/// — so the address→handle cache, the "don't retry a failed address" rule, the async plumbing and the letting go all
/// exist in exactly one place instead of once per system.
///
/// <para>This owns LOADING and LETTING GO. What a caller does with the loaded prefab — where it spawns it, what it
/// parents it to, which hideFlags a clone carries — stays with that caller, because those genuinely differ per
/// system.</para>
///
/// <para><b>Who is using what.</b> Whoever asks for a prefab hands back the object that now uses it (the clone it
/// built, the creature it dressed), and the compiler will not let it forget: that object being alive is what keeps
/// the prefab in memory. A user that is only waiting in a pool says so with <see cref="Park"/>, so a pool full of
/// spare bodies does not hold a creature nobody can see.</para>
///
/// <para><b>When it lets go.</b> On a player's machine, each time a map is left: a prefab nobody has used during the
/// map just left nor the one before it is released, along with any parked spare still made from it, and its content
/// file leaves memory. So the map you just walked out of stays ready for walking back into, and memory stops growing
/// with every map visited. A game server never lets go — it holds every map it runs.</para>
///
/// <para>A failed address caches its failure too, so a missing entry costs one attempt rather than one per call.</para>
/// </summary>
public static class AddressablePrefabs
{
    /// <summary>How many maps must be left without a prefab being used before it is let go. Two keeps the map you just
    /// left ready to walk back into.</summary>
    const int MapsKept = 2;

    sealed class Held
    {
        public AsyncOperationHandle<GameObject> handle;
        public readonly List<UnityEngine.Object> users = new List<UnityEngine.Object>();
        public int lastUsed;
        public int pruneAt = 8;
    }

    static readonly Dictionary<string, Held> loaded = new Dictionary<string, Held>();

    // Addresses with a load already in flight, and everyone waiting on it. Without this, twelve townsfolk asking for
    // the same body in one frame would each start their own load of it — the cache below only fills once the FIRST
    // one finishes, so it cannot dedupe requests made before that.
    static readonly Dictionary<string, List<Func<GameObject, UnityEngine.Object>>> inFlight =
        new Dictionary<string, List<Func<GameObject, UnityEngine.Object>>>();

    // Users waiting in a pool: alive, but not a reason to keep their prefab.
    static readonly HashSet<UnityEngine.Object> parked = new HashSet<UnityEngine.Object>();

    static readonly List<string> releasing = new List<string>();

    /// <summary>How many maps this machine has left so far.</summary>
    public static int MapsLeft { get; private set; }

    /// <summary>Raised on a player's machine each time a map is left, after what was unused is let go. Anything that keeps
    /// pictures, meshes or materials made FROM a loaded prefab's contents drops the ones whose source has gone.</summary>
    public static event Action MapLeft;

    /// <summary>Deliver the prefab at <paramref name="address"/>, or null if that address has nothing behind it, to
    /// <paramref name="use"/>, which returns the object that now uses it — or null if it built nothing.
    /// <paramref name="use"/> runs THIS frame when the address is already loaded, or a few frames later on the first
    /// request for it. Concurrent requests for one address share a single load.</summary>
    public static void Load(string address, Func<GameObject, UnityEngine.Object> use)
    {
        if (string.IsNullOrEmpty(address)) { use?.Invoke(null); return; }

        if (loaded.TryGetValue(address, out var held)) { Deliver(held, use); return; }

        if (inFlight.TryGetValue(address, out var queue)) { queue.Add(use); return; }
        inFlight[address] = new List<Func<GameObject, UnityEngine.Object>> { use };

        Addressables.LoadAssetAsync<GameObject>(address).Completed += h =>
        {
            // Success OR failure is cached: a failed handle delivers null and is not retried.
            var entry = new Held { handle = h, lastUsed = MapsLeft };
            loaded[address] = entry;

            if (!inFlight.TryGetValue(address, out var waiting)) return;
            inFlight.Remove(address);
            foreach (var cb in waiting) Deliver(entry, cb);
        };
    }

    static void Deliver(Held held, Func<GameObject, UnityEngine.Object> use)
    {
        var prefab = held.handle.IsValid() && held.handle.Status == AsyncOperationStatus.Succeeded
            ? held.handle.Result : null;
        var user = use?.Invoke(prefab);
        if (prefab == null || user == null) return;

        held.lastUsed = MapsLeft;
        if (held.users.Contains(user)) return;
        // Users come and go all session long (people walking past in town), so the dead are swept out whenever the
        // list has doubled since the last sweep — a cost spread over the adds, never a per-frame scan.
        if (held.users.Count >= held.pruneAt)
        {
            held.users.RemoveAll(IsGone);
            held.pruneAt = Mathf.Max(8, held.users.Count * 2);
        }
        held.users.Add(user);
    }

    static readonly Predicate<UnityEngine.Object> IsGone = u => u == null;

    /// <summary><paramref name="user"/> is going into a pool: it stays alive, but no longer keeps its prefab loaded.
    /// If that prefab is let go while it waits, it is destroyed with it — so a pool must expect a hole where a spare
    /// was.</summary>
    public static void Park(UnityEngine.Object user)
    {
        if (user != null) parked.Add(user);
    }

    /// <summary><paramref name="user"/> is coming back out of a pool to be used again. False when it may not be: its
    /// prefab was let go while it waited, and it is destroyed (Unity only finishes that at the end of the frame, so a
    /// pool cannot tell from the object alone).</summary>
    public static bool Unpark(UnityEngine.Object user)
    {
        if (user == null || !parked.Remove(user)) return false;
        foreach (var held in loaded.Values)
            if (held.users.Contains(user)) held.lastUsed = MapsLeft;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    static async void TellMapLeftNextFrame()
    {
        await Awaitable.NextFrameAsync();
        MapLeft?.Invoke();
    }

    // Runs once per map left — never per frame — so a walk over what is loaded is the whole cost.
    static void OnSceneUnloaded(Scene scene)
    {
        if (GameServer.IsServer || scene.name == MapManager.SystemsScene) return;
        MapsLeft++;

        releasing.Clear();
        foreach (var pair in loaded)
        {
            var held = pair.Value;
            if (!held.handle.IsValid() || held.handle.Status != AsyncOperationStatus.Succeeded) continue;

            held.users.RemoveAll(IsGone);
            held.pruneAt = Mathf.Max(8, held.users.Count * 2);
            bool inUse = false;
            foreach (var u in held.users)
                if (!parked.Contains(u)) { inUse = true; break; }

            if (inUse) held.lastUsed = MapsLeft;
            else if (MapsLeft - held.lastUsed >= MapsKept) releasing.Add(pair.Key);
        }

        foreach (var address in releasing)
        {
            var held = loaded[address];
            loaded.Remove(address);
            // A parked spare is still made of this prefab's meshes and pictures; it goes with them.
            foreach (var u in held.users)
            {
                parked.Remove(u);
                if (u is GameObject go) UnityEngine.Object.Destroy(go);
            }
            Addressables.Release(held.handle);
        }
        parked.RemoveWhere(IsGone);
        // AFTER the letting go, so what was made from a prefab let go just now is thrown away in this same map change —
        // one frame later, because Unity only finishes destroying a released file's pictures and meshes at the end of
        // the frame, and until then nothing can tell they are gone.
        TellMapLeftNextFrame();

        if (releasing.Count > 0)
            GameLog.Info(LogCat.Client, "let go of " + releasing.Count + " creature/body prefab(s) unused for "
                         + MapsKept + " maps; " + loaded.Count + " still loaded");
    }
}
