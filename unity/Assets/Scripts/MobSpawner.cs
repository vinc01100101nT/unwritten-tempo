using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// THE runtime choke point for "make a live mob from catalog data" during Play. The mob BUILDER
/// (<c>RigBuilder25D</c>) is editor-only, so it can't stamp a <see cref="MonsterDef"/> into a live mob in a
/// build. Instead every mob is baked to a PREFAB marked Addressable under address <c>Mobs/&lt;asset name&gt;</c>
/// at author time (Tools ▸ unwritten ▸ Mobs &amp; NPCs ▸ Bake Mob Prefabs); this loads that prefab by def and
/// clones it. Route all data-driven spawning through here so the address key + caching + hideFlags rule live
/// in one place.
///
/// Loading is ASYNC (Addressables): the first spawn of a given mob loads its prefab off the main path, then
/// the loaded prefab asset is cached for the session and every later spawn is an immediate
/// <see cref="UnityEngine.Object.Instantiate"/>. Instances are plain clones (NOT Addressables-managed), so a
/// mob's death/recycle lifecycle (<c>MobRespawn</c>, <c>DeathDissolve</c>) is unchanged — only where the
/// prefab comes from changed (Addressables instead of a Resources folder).
/// </summary>
public static class MobSpawner
{
    const string AddressPrefix = "Mobs/";

    // key -> loaded prefab handle. Kept for the session (never released) so repeated summons are instant;
    // a failed load caches a default (invalid) handle so a missing address isn't re-attempted every summon.
    static readonly Dictionary<string, AsyncOperationHandle<GameObject>> loaded =
        new Dictionary<string, AsyncOperationHandle<GameObject>>();

    /// <summary>Spawn <paramref name="def"/>'s baked prefab with its feet at <paramref name="foot"/>. The
    /// prefab loads async on first use then is cached, so <paramref name="onSpawned"/> may fire this frame
    /// (cached) or a few frames later (first load). Delivers the new mob, or null if that def has no baked
    /// Addressable prefab (baker not run for it, or Addressables content not built).</summary>
    public static void Spawn(MonsterDef def, Vector3 foot, Action<GameObject> onSpawned = null)
    {
        if (def == null) { onSpawned?.Invoke(null); return; }
        string key = AddressPrefix + def.name;

        if (loaded.TryGetValue(key, out var cached))
        {
            onSpawned?.Invoke(cached.IsValid() && cached.Result != null ? Clone(cached.Result, foot) : null);
            return;
        }

        var handle = Addressables.LoadAssetAsync<GameObject>(key);
        handle.Completed += h =>
        {
            loaded[key] = h;   // cache success OR failure (an invalid/failed handle → null, not retried)
            var prefab = h.Status == AsyncOperationStatus.Succeeded ? h.Result : null;
            onSpawned?.Invoke(prefab != null ? Clone(prefab, foot) : null);
        };
    }

    static GameObject Clone(GameObject prefab, Vector3 foot)
    {
        var go = UnityEngine.Object.Instantiate(prefab, foot, Quaternion.identity);
        go.hideFlags = HideFlags.DontSaveInEditor;   // a Play-only clone must never be baked into a saved scene
        return go;
    }
}
