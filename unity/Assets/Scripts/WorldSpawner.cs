using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// PUTS A MAP'S CREATURES INTO IT as the server loads it: one spawn area per row of <see cref="WorldSpawns"/> naming
/// that map, built in memory and never saved. The spawning itself — keeping a population alive, the pool, the respawn
/// waits, finding the ground — is <see cref="FieldMobSpawner"/>'s, exactly as when the areas were saved inside maps.
///
/// <para>Server only: a player's machine is told about creatures, it never makes them.</para>
/// </summary>
public static class WorldSpawner
{
    // Maps already given their areas, by scene handle — a map activated twice is populated once. A map that is let go
    // takes its areas with it, and its handle is forgotten, so loading it again populates it again.
    static readonly HashSet<int> populated = new HashSet<int>();

    // Every spawn area built into each map, by scene handle — what a seamless world tends, waking and putting to sleep.
    static readonly Dictionary<int, List<FieldMobSpawner>> built = new Dictionary<int, List<FieldMobSpawner>>();
    static readonly List<FieldMobSpawner> None = new List<FieldMobSpawner>();

    /// <summary>The spawn areas built into <paramref name="map"/> (empty off the server, or before it was populated).
    /// The list is the store itself: read it, never change it.</summary>
    public static List<FieldMobSpawner> AreasIn(Scene map) => built.TryGetValue(map.handle, out var list) ? list : None;

    /// <summary>Build every spawn area <paramref name="map"/> has in the list. Does nothing off the server, for the
    /// persistent scene, or for a map already done.</summary>
    public static void Populate(Scene map)
    {
        if (!GameServer.IsServer || !map.IsValid() || map.name == MapManager.SystemsScene) return;
        if (!populated.Add(map.handle)) return;

        var list = WorldSpawns.Instance;
        if (list == null)
        {
            if (!warnedNoList)
            {
                warnedNoList = true;
                GameLog.Warn(LogCat.Spawn, "no '" + WorldSpawns.ResourcePath + "' list in Resources — every map is empty of creatures.");
            }
            return;
        }

        int areas = 0, creatures = 0;
        bool seamless = SeamlessWorld.InScene(map) != null;
        // Every row on one area spreads its creatures away from the others' (FieldMobSpawner.SpotInShape).
        var mates = new Dictionary<int, List<FieldMobSpawner>>();
        for (int i = 0; i < list.rows.Count; i++)
        {
            var row = list.rows[i];
            if (row == null || row.population <= 0 || row.map != map.name) continue;
            List<FieldMobSpawner> onArea = null;
            if (row.area >= 0 && !mates.TryGetValue(row.area, out onArea)) mates[row.area] = onArea = new List<FieldMobSpawner>();
            var s = Build(row, map, i, seamless, onArea);
            onArea?.Add(s);
            areas++;
            creatures += row.population;
        }
        if (areas > 0)
            GameLog.Info(LogCat.Spawn, "map '" + map.name + "' given " + areas + " spawn area(s), " + creatures + " creature(s).");
    }

    /// <summary>Forget a map that has gone, so it is populated again if it comes back.</summary>
    public static void Forget(Scene map)
    {
        populated.Remove(map.handle);
        built.Remove(map.handle);
    }

    static bool warnedNoList;


    static FieldMobSpawner Build(WorldSpawns.Row row, Scene map, int index, bool seamless, List<FieldMobSpawner> areaMates)
    {
        var go = new GameObject("Spawn area " + index + (string.IsNullOrEmpty(row.creature) ? "" : " (" + row.creature + ")"));
        // Built while the game runs, so it must never be baked into a scene an editor might save.
        go.hideFlags = HideFlags.DontSaveInEditor;
        // SWITCHED OFF UNTIL IT IS FILLED IN: its Start reads the settings below, and a spawner that started first would
        // fill itself with the defaults.
        go.SetActive(false);
        MapManager.PutInMap(go, map);
        var area = WorldSpawns.Instance.AreaOf(row);
        go.transform.position = MapManager.InWorld(area?.center ?? row.center, map.name);

        var s = go.AddComponent<FieldMobSpawner>();
        s.population        = row.population;
        s.radius            = area?.radius ?? row.radius;
        if (area != null && area.runs.Length >= 3) s.SetShape(area.runs, MapManager.InWorld(Vector3.zero, map.name), areaMates);
        s.fixedMobId        = row.creature ?? "";
        s.biome             = row.biome;
        s.minLevel          = row.minLevel;
        s.maxLevel          = row.maxLevel;
        s.includeCommons    = row.includeCommons;
        s.playerClearRadius = row.playerClearRadius;
        if (!built.TryGetValue(map.handle, out var list)) built[map.handle] = list = new List<FieldMobSpawner>();
        list.Add(s);
        // A SEAMLESS WORLD WAKES ITS AREAS ITSELF, once somebody is near and the ground under the area has loaded —
        // switched on now, an area would look for its floor before any of it exists (SeamlessWorld).
        if (!seamless) go.SetActive(true);
        return s;
    }
}
