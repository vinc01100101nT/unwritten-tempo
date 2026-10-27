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
        [Tooltip("Written by the minimap baker. 0 = the map is the one picture above. Above 0 = the map is also drawn in " +
                 "square pictures this many world units wide (TilePicture), which the minimap shows around the player; " +
                 "the one picture is then only the whole-map overview.")]
        public float tileSize;
    }

    /// <summary>The Resources name (under <see cref="PictureFolder"/>) of the square picture covering map units
    /// gx*tileSize..(gx+1)*tileSize west to east and gy*tileSize.. south to north.</summary>
    public static string TilePicture(string map, int gx, int gy) => map + "/" + gx + "_" + gy;

    public List<Entry> maps = new List<Entry>();

    /// <summary>The entry for <paramref name="map"/>, or null when nobody has drawn it.</summary>
    public Entry Find(string map)
    {
        for (int i = 0; i < maps.Count; i++)
            if (maps[i] != null && maps[i].map == map) return maps[i];
        return null;
    }

    // THE ONE OWNER OF MAP PICTURES. The minimap and the Map window can be drawing the same picture at once, and an
    // unload by either would pull it out from under the other — so neither loads or unloads one itself: each takes a
    // picture here and gives it back, and a picture is let go only when nobody is still drawing it.
    static readonly Dictionary<string, (Texture2D picture, int users)> held = new Dictionary<string, (Texture2D, int)>();

    /// <summary>Take the picture of <paramref name="map"/> to draw, loading it if nobody holds it yet. Null when the map
    /// has none. Every picture taken is given back with <see cref="ReleasePicture"/>.</summary>
    public static Texture2D AcquirePicture(string map)
    {
        if (string.IsNullOrEmpty(map)) return null;
        if (held.TryGetValue(map, out var h)) { held[map] = (h.picture, h.users + 1); return h.picture; }
        var picture = Resources.Load<Texture2D>(PictureFolder + "/" + map);
        if (picture == null) return null;
        held[map] = (picture, 1);
        return picture;
    }

    /// <summary>Give back the picture of <paramref name="map"/> taken with <see cref="AcquirePicture"/>; the last one
    /// to give it back lets it go.</summary>
    public static void ReleasePicture(string map)
    {
        if (string.IsNullOrEmpty(map) || !held.TryGetValue(map, out var h)) return;
        if (h.users > 1) { held[map] = (h.picture, h.users - 1); return; }
        held.Remove(map);
        if (h.picture != null) Resources.UnloadAsset(h.picture);
    }

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
