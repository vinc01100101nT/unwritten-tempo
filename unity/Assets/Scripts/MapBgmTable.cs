using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single source of truth for "which <see cref="BgmTrack"/> does each map play" — a plain lookup table
/// (scene name → track). One asset lives at <c>Resources/MapBgmTable</c>; the persistent <see cref="MusicDirector"/>
/// reads it on every map load. This is DATA, not baked into the scenes: attaching / changing a map's music is a
/// one-field edit here (see the Map BGM Table editor window) and needs NO map re-generate.
/// </summary>
[CreateAssetMenu(fileName = "MapBgmTable", menuName = "unwritten/Map BGM Table", order = 2)]
public class MapBgmTable : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        [Tooltip("Scene file name (no extension), e.g. 'town_0'.")]
        public string sceneName;
        [Tooltip("Track to play on that map. Empty = silence there.")]
        public BgmTrack track;
    }

    public List<Entry> entries = new List<Entry>();

    /// <summary>Track for <paramref name="sceneName"/>, or null if unlisted. Linear scan — the list is tiny (one row
    /// per map) and this runs only on map load, never per frame.</summary>
    public BgmTrack Lookup(string sceneName)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].sceneName == sceneName) return entries[i].track;
        return null;
    }

    static MapBgmTable cached;
    static bool loaded;

    /// <summary>The project's table from Resources (cached; null if none authored yet).</summary>
    public static MapBgmTable Load()
    {
        if (!loaded) { cached = Resources.Load<MapBgmTable>("MapBgmTable"); loaded = true; }
        return cached;
    }
}
