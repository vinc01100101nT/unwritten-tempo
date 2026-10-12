using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The baked world-map image + free-form layout, loaded at runtime by <see cref="MapWindow"/>.
/// Produced by the editor "Bake World Map" step (in <c>WorldCreator</c> via <c>WorldAtlasBaker</c>): either the
/// painted world-map picture the layout was drawn on (one picture pixel = one world unit), or one top-down snapshot
/// per map composited into a single <see cref="atlas"/> texture by each map's world-space rectangle.
///
/// Lives in <c>Resources/</c> so the HUD can <c>Resources.Load&lt;WorldAtlas&gt;("WorldAtlas")</c> without any
/// scene wiring. Map space: <c>x</c>→east, <c>y</c>→south; the atlas is north-up / east-right, and empty space is
/// dark. The HUD maps the player's current scene + position to a marker over the image.
/// </summary>
public class WorldAtlas : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string scene;    // scene/file name (== sanitized map id)
        public float x, y, w, h; // world-space rect, top-left (x→east, y→south), size in world units
        public string type;     // town | field | dungeon | special
        public string name;     // what players call it, from the world layout ("Lastline Field 1"); empty = unnamed
    }

    [Tooltip("Composited top-down image of the whole world (free-form, world-space rectangles).")]
    public Texture2D atlas;

    // World-space bounds the atlas image spans (top-left origin; y is south-down). Used to map any world point
    // to a normalized position over the image.
    public float minX, minY;     // top-left world corner that maps to image (u=0, top row)
    public float worldW, worldH; // total world span the image covers
    public float ppu;            // pixels per world unit used when compositing

    public List<Entry> maps = new List<Entry>();

    /// <summary>One door between two maps, either way through — the world layout's portals, copied here by the bake
    /// so the game can find its way between maps (<see cref="WorldRoutes"/>) and draw each door where it stands. A
    /// READ-ONLY MIRROR of <c>tools/world-creator/worlds/world-regions.json</c>, never edited by hand: the layout stays
    /// the one owner, and <c>tools/check-map-ids.py</c> fails the moment this copy disagrees with it.</summary>
    [System.Serializable]
    public class Link
    {
        public string a, b;      // the two maps it joins (kept the first two fields: the checker reads them in this order)
        public int id;           // the door's permanent id in the layout
        public float ax, az;     // where the door stands in map a: metres from that map's centre, x east, z north
        public float bx, bz;     // where it stands in map b, the same way
    }

    public List<Link> links = new List<Link>();

    public Entry Find(string sceneName)
    {
        for (int i = 0; i < maps.Count; i++)
            if (maps[i].scene == sceneName) return maps[i];
        return null;
    }

    /// <summary>What players call <paramref name="sceneName"/> — its name in the world layout, or the scene name itself
    /// when the layout gives it none.</summary>
    public string NameOf(string sceneName)
    {
        var e = Find(sceneName);
        return e != null && !string.IsNullOrEmpty(e.name) ? e.name : sceneName;
    }
}
