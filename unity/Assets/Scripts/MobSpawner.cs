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
                                prefab => onSpawned?.Invoke(prefab != null ? Clone(prefab, foot, map, def) : null));
    }

    static GameObject Clone(GameObject prefab, Vector3 foot, Scene map, MonsterDef def)
    {
        var go = UnityEngine.Object.Instantiate(prefab, foot, Quaternion.identity);
        go.hideFlags = HideFlags.DontSaveInEditor;   // a Play-only clone must never be baked into a saved scene
        MapManager.PutInMap(go, map);
        // ITS KIND, from its data, before anything on it asks — its skills, its gear and its loot all read this.
        var health = go.GetComponent<Health>();   // perf-ok: once per creature made
        if (health != null)
        {
            health.kind = def.kind;
            // ITS ELEMENT AND LEVEL, from its data, on every machine — so the server and a watching copy can never
            // disagree about what a hit on it does. The prefab's baked element is only the baker's copy of this.
            health.element = def.element;
            health.elementLevel = def.elementLevel;
        }
        // ITS GEAR'S GRADE, FOLDS AND INK come from its catalog entry, which OWNS them; the baked prefab only mirrors
        // what the baker saw. Its gear was already put on while it was being built, so a mirror that has gone stale —
        // the table edited without re-baking — is corrected here by putting it on again at the entry's settings.
        // Only on the machine that owns the world: a watching copy is told what the creature wears, and rolls nothing.
        if (GameServer.IsServer)
        {
            var gear = go.GetComponent<UnitEquipment>();   // perf-ok: once per creature made
            if (gear != null && (gear.gearQuality != def.gearQuality || gear.gearFolds != def.gearFolds ||
                                 gear.gearInkTier != def.gearInkTier))
            {
                gear.gearQuality = def.gearQuality;
                gear.gearFolds = def.gearFolds;
                gear.gearInkTier = def.gearInkTier;
                gear.ReseatForNewLife();
                var block = go.GetComponent<UnitStatBlock>();   // perf-ok: once per creature made
                if (block != null) block.Rederive(healToFull: true);
            }
        }
        // ITS SIZE, from its data, on every machine — how tall it stands, its body, and the melee reach both read it, so
        // the server and a watching copy can never disagree about where a blow reaches. The prefab's baked size is only
        // the baker's copy of this.
        MobBody.Fit(go, def.kind, def.size);
        // A SHADOW IS A 3D CHARACTER, decided by its kind, here — the one door every creature is made through — so no
        // caller can make one flat. Before it is announced, because what crosses the wire is read off the creature at
        // the moment it is published. It keeps the body for life, through every pooled respawn.
        if (CreatureKinds.Is3D(def.kind)) go.AddComponent<MobShadow3DVisual>();   // perf-ok: once per creature made
        // AND A KIND THAT SUMMONS gets the list its summons are kept in, knowing which creature it is so it can work out
        // whom it calls. Server only: a watching copy summons nothing, it is told about each summon.
        if (GameServer.IsServer && CreatureKinds.Summons(def.kind)) go.AddComponent<CreatureSummons>().def = def;   // perf-ok: once per creature made
        return go;
    }
}
