using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// THE runtime choke point for "make a live mob from catalog data" during Play. The mob BUILDER
/// (<c>RigBuilder25D</c>) is editor-only, so it can't stamp a <see cref="MonsterDef"/> into a live mob in a
/// build. Instead every mob is baked to a PREFAB marked Addressable under address <c>Mobs/&lt;asset name&gt;</c>
/// at author time (Tools ▸ unwritten ▸ Mobs &amp; NPCs ▸ Bake Mob Prefabs); this loads that prefab by def and
/// clones it. Route all data-driven spawning through here so the address key + caching + hideFlags rule live
/// in one place.
///
/// Loading is ASYNC and runs through <see cref="AddressablePrefabs"/> — the shared address→prefab cache the
/// character models use too, so the "load once, keep for the session, never retry a dead address" rule has one
/// owner rather than a copy per system. The first spawn of a given mob loads its prefab off the main path and
/// every later spawn is an immediate <see cref="UnityEngine.Object.Instantiate"/>. Instances are plain clones
/// (NOT Addressables-managed), so a mob's death/recycle lifecycle (<c>MobRespawn</c>, <c>DeathDissolve</c>) is
/// unchanged — only where the prefab comes from changed (Addressables instead of a Resources folder).
/// </summary>
public static class MobSpawner
{
    const string AddressPrefix = "Mobs/";

    /// <summary>Spawn <paramref name="def"/>'s baked prefab with its feet at <paramref name="foot"/>, belonging
    /// to <paramref name="map"/>. The prefab loads async on first use then is cached, so
    /// <paramref name="onSpawned"/> may fire this frame (cached) or a few frames later (first load). Delivers
    /// the new mob, or null if that def has no baked Addressable prefab (baker not run for it, or Addressables
    /// content not built).
    ///
    /// <para><b>The map is asked for, not guessed.</b> A creature's map is the scene it sits in, and Unity puts
    /// a new object in whichever scene was loaded LAST — which on a machine holding several is not the map that
    /// asked for it. Worse, the loading above means the creature is built some frames after the asking, so even
    /// the map that was current at the time may not be by then. Every caller therefore says which map its
    /// creature belongs to, and none of them can forget.</para></summary>
    public static void Spawn(MonsterDef def, Vector3 foot, Scene map, Action<GameObject> onSpawned = null)
    {
        if (def == null) { onSpawned?.Invoke(null); return; }

        AddressablePrefabs.Load(AddressPrefix + def.name,
                                prefab => onSpawned?.Invoke(prefab != null ? Clone(prefab, foot, map) : null));
    }

    static GameObject Clone(GameObject prefab, Vector3 foot, Scene map)
    {
        var go = UnityEngine.Object.Instantiate(prefab, foot, Quaternion.identity);
        go.hideFlags = HideFlags.DontSaveInEditor;   // a Play-only clone must never be baked into a saved scene
        MapManager.PutInMap(go, map);
        return go;
    }
}
