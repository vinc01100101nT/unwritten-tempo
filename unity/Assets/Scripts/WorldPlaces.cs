using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The world's named places — towns, landmarks, caves, ponds, dungeon doors — each at a spot a body can stand on, read from
/// <c>Resources/WorldPlaces.json</c>, which the world layout tool writes from the layout every time it runs. What a GM's
/// <c>/tp</c> resolves a name to. Loaded once, on first use.
/// </summary>
public static class WorldPlaces
{
    /// <summary>The map the places are in.</summary>
    public const string Map = SavePoint.DefaultMap;

    /// <summary>What kind of place it is, as the layout file names it.</summary>
    public enum Kind { Other, Town, Landmark, DungeonDoor, Cave, Pond }

    public sealed class Place
    {
        public string id, name;
        public Kind kind;
        /// <summary>The spot in the world map's own coordinates (x east, z north, height above Lastline).</summary>
        public Vector3 inMap;
        /// <summary>Where the map marks it, in the same coordinates — the spot itself, except a cave, whose spot is in
        /// front of its mouth and whose mark is on the mouth.</summary>
        public Vector3 mark;
    }

    [System.Serializable] sealed class FileShape { public Origin origin; public Entry[] places; }
    [System.Serializable] sealed class Origin { public float x, y, height; }
    [System.Serializable] sealed class Entry { public string id, name, kind; public float x, y, height, mark_x, mark_y; }

    static Kind KindOf(string s) => s switch
    {
        "town" => Kind.Town, "landmark" => Kind.Landmark, "dungeon_door" => Kind.DungeonDoor, "cave" => Kind.Cave,
        "pond" => Kind.Pond, _ => Kind.Other,
    };

    static List<Place> all;

    public static IReadOnlyList<Place> All
    {
        get
        {
            if (all != null) return all;
            all = new List<Place>();
            var text = Resources.Load<TextAsset>("WorldPlaces");
            if (text == null) return all;
            var file = JsonUtility.FromJson<FileShape>(text.text);
            if (file?.origin == null || file.places == null) return all;
            // The world is built around Lastline: its middle is the map's (0, 0), its ground the map's height 0, and the
            // layout's y runs south where the map's z runs north.
            foreach (var e in file.places)
                all.Add(new Place { id = e.id, name = e.name ?? "", kind = KindOf(e.kind),
                                    inMap = new Vector3(e.x - file.origin.x, e.height - file.origin.height, file.origin.y - e.y),
                                    mark = new Vector3(e.mark_x - file.origin.x, e.height - file.origin.height, file.origin.y - e.mark_y) });
            return all;
        }
    }

    // A name as /tp compares it: letters and digits only, in lower case — "Old Oak", "old_oak" and "oldoak" are one.
    static string Key(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>The places whose id or name is <paramref name="name"/>, or else every one whose id or name holds it.</summary>
    public static List<Place> Named(string name)
    {
        var exact = new List<Place>();
        var partial = new List<Place>();
        string want = Key(name ?? "");
        if (want.Length == 0) return exact;
        foreach (var p in All)
        {
            string id = Key(p.id), shown = Key(p.name);
            if (id == want || shown == want) exact.Add(p);
            else if (id.Contains(want) || (shown.Length > 0 && shown.Contains(want))) partial.Add(p);
        }
        return exact.Count > 0 ? exact : partial;
    }
}
