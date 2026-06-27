using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The baked world-map image + free-form layout, loaded at runtime by <see cref="WorldMapHUD"/>.
/// Produced by the editor "Bake World Map" step (in <c>WorldCreator</c> via <c>WorldAtlasBaker</c>): one
/// top-down snapshot per map, composited into a single <see cref="atlas"/> texture by each map's world-space
/// rectangle (free placement, any size — no grid).
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
    }

    [Tooltip("Composited top-down image of the whole world (free-form, world-space rectangles).")]
    public Texture2D atlas;

    // World-space bounds the atlas image spans (top-left origin; y is south-down). Used to map any world point
    // to a normalized position over the image.
    public float minX, minY;     // top-left world corner that maps to image (u=0, top row)
    public float worldW, worldH; // total world span the image covers
    public float ppu;            // pixels per world unit used when compositing

    public List<Entry> maps = new List<Entry>();

    public Entry Find(string sceneName)
    {
        for (int i = 0; i < maps.Count; i++)
            if (maps[i].scene == sceneName) return maps[i];
        return null;
    }
}
