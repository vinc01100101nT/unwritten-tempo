using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// THE one way to fetch a prefab asset by Addressable address at runtime, loaded once and kept for the session.
/// Every system that draws prefabs from Addressables goes through here — mobs (<see cref="MobSpawner"/>) and
/// character models (<see cref="CharacterModels"/>) — so the address→handle cache, the "don't retry a failed
/// address" rule and the async plumbing exist in exactly one place instead of once per system.
///
/// <para>This owns LOADING ONLY. What a caller does with the loaded prefab — where it spawns it, what it parents
/// it to, which hideFlags a clone carries — stays with that caller, because those genuinely differ per system.</para>
///
/// <para>A failed address caches its failure too, so a missing entry costs one attempt rather than one per call.
/// Handles are never released: the set of addresses a session touches is small (the mobs it fought, the bodies it
/// wore) and re-loading one mid-fight would stall exactly when it must not.</para>
/// </summary>
public static class AddressablePrefabs
{
    static readonly Dictionary<string, AsyncOperationHandle<GameObject>> loaded =
        new Dictionary<string, AsyncOperationHandle<GameObject>>();

    // Addresses with a load already in flight, and everyone waiting on it. Without this, twelve townsfolk asking for
    // the same body in one frame would each start their own load of it — the cache below only fills once the FIRST
    // one finishes, so it cannot dedupe requests made before that.
    static readonly Dictionary<string, List<Action<GameObject>>> inFlight =
        new Dictionary<string, List<Action<GameObject>>>();

    /// <summary>Deliver the prefab at <paramref name="address"/>, or null if that address has nothing behind it.
    /// <paramref name="onPrefab"/> fires THIS frame when the address was already loaded, or a few frames later on
    /// the first request for it. Concurrent requests for one address share a single load.</summary>
    public static void Load(string address, Action<GameObject> onPrefab)
    {
        if (string.IsNullOrEmpty(address)) { onPrefab?.Invoke(null); return; }

        if (loaded.TryGetValue(address, out var cached))
        {
            onPrefab?.Invoke(cached.IsValid() ? cached.Result : null);
            return;
        }

        if (inFlight.TryGetValue(address, out var queue)) { queue.Add(onPrefab); return; }
        inFlight[address] = new List<Action<GameObject>> { onPrefab };

        Addressables.LoadAssetAsync<GameObject>(address).Completed += h =>
        {
            loaded[address] = h;   // cache success OR failure (an invalid/failed handle → null, not retried)
            var prefab = h.Status == AsyncOperationStatus.Succeeded ? h.Result : null;

            if (!inFlight.TryGetValue(address, out var waiting)) return;
            inFlight.Remove(address);
            foreach (var cb in waiting) cb?.Invoke(prefab);
        };
    }

    /// <summary>The prefab at <paramref name="address"/> if a previous <see cref="Load"/> already fetched it, else
    /// null. For callers that can act instantly when the body is warm and must wait otherwise.</summary>
    public static GameObject Cached(string address)
        => !string.IsNullOrEmpty(address) && loaded.TryGetValue(address, out var h) && h.IsValid() ? h.Result : null;
}
