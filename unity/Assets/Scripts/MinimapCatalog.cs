using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT THE MINIMAP SHOWS FOR EVERY MAP — one list, kept outside the maps. Each entry names a map and the square of
/// that map its picture covers; the picture itself is <c>Resources/Minimaps/&lt;map&gt;</c>, loaded only while you
/// stand in that map. Nothing about the minimap is saved inside a map scene.
///
/// <para>Written by the editor (<c>MinimapBaker</c>): every map is drawn straight down, and a map is drawn again
/// whenever its scene is saved. The game only reads it.</para>
/// </summary>
public class MinimapCatalog : ScriptableObject
{
    public const string ResourcePath = "MinimapCatalog";
    public const string PictureFolder = "Minimaps";

    [System.Serializable]
    public class Entry
    {
        [Tooltip("The map's scene name, e.g. 'lastline'.")]
        public string map = "";
        [Tooltip("The corner of the picture at its bottom-left, in the map's own coordinates: X west to east, then " +
                 "the ground's north-south axis.")]
        public Vector2 worldMin;
        [Tooltip("The width (and height — the picture is square) of the ground the picture covers, in world units.")]
        public float worldSize = 100f;
    }

    public List<Entry> maps = new List<Entry>();

    /// <summary>The entry for <paramref name="map"/>, or null when nobody has drawn it.</summary>
    public Entry Find(string map)
    {
        for (int i = 0; i < maps.Count; i++)
            if (maps[i] != null && maps[i].map == map) return maps[i];
        return null;
    }

    /// <summary>The picture of <paramref name="map"/>, loaded now. Unload it with <see cref="Resources.UnloadAsset"/>
    /// when leaving the map.</summary>
    public static Texture2D LoadPicture(string map) =>
        string.IsNullOrEmpty(map) ? null : Resources.Load<Texture2D>(PictureFolder + "/" + map);

    static MinimapCatalog loaded;
    static bool looked;

    /// <summary>The list, loaded once. Null when it has not been made.</summary>
    public static MinimapCatalog Instance
    {
        get
        {
            if (!looked) { looked = true; loaded = Resources.Load<MinimapCatalog>(ResourcePath); }
            return loaded;
        }
    }
}
