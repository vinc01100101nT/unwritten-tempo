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
        for (int i = 0; i < list.rows.Count; i++)
        {
            var row = list.rows[i];
            if (row == null || row.population <= 0 || row.map != map.name) continue;
            Build(row, map, i);
            areas++;
            creatures += row.population;
        }
        if (areas > 0)
            GameLog.Info(LogCat.Spawn, "map '" + map.name + "' given " + areas + " spawn area(s), " + creatures + " creature(s).");
    }

    /// <summary>Forget a map that has gone, so it is populated again if it comes back.</summary>
    public static void Forget(Scene map) => populated.Remove(map.handle);

    static bool warnedNoList;

    static void Build(WorldSpawns.Row row, Scene map, int index)
    {
        var go = new GameObject("Spawn area " + index + (string.IsNullOrEmpty(row.creature) ? "" : " (" + row.creature + ")"));
        // Built while the game runs, so it must never be baked into a scene an editor might save.
        go.hideFlags = HideFlags.DontSaveInEditor;
        // SWITCHED OFF UNTIL IT IS FILLED IN: its Start reads the settings below, and a spawner that started first would
        // fill itself with the defaults.
        go.SetActive(false);
        MapManager.PutInMap(go, map);
        go.transform.position = MapManager.InWorld(row.center, map.name);

        var s = go.AddComponent<FieldMobSpawner>();
        s.population        = row.population;
        s.radius            = row.radius;
        s.fixedMobId        = row.creature ?? "";
        s.biome             = row.biome;
        s.minLevel          = row.minLevel;
        s.maxLevel          = row.maxLevel;
        s.includeCommons    = row.includeCommons;
        s.playerClearRadius = row.playerClearRadius;
        go.SetActive(true);
    }
}
