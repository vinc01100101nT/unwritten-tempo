using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The baked world-map image + grid layout, loaded at runtime by <see cref="WorldMapHUD"/>.
/// Produced by the editor "Bake World Map" step (in <c>WorldCreator</c> via <c>WorldAtlasBaker</c>): one
/// top-down snapshot per map, composited into a single <see cref="atlas"/> texture arranged by grid cell.
///
/// Lives in <c>Resources/</c> so the HUD can <c>Resources.Load&lt;WorldAtlas&gt;("WorldAtlas")</c> without any
/// scene wiring. The atlas is north-up / east-right (each cell is a fixed-north top-down render), and cells with
/// no map are left dark. The HUD maps the player's current scene + position to a marker over the image.
/// </summary>
public class WorldAtlas : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string scene;   // scene/file name (== sanitized map id)
        public int col, row;    // grid cell
        public string type;     // town | field | dungeon | special
    }

    [Tooltip("Composited top-down image of the whole world (cols×rows cells, each cellPixels²).")]
    public Texture2D atlas;

    public int cols, rows;       // grid size in cells
    public int minCol, minRow;   // grid origin (the cell at atlas pixel column 0 / top row)
    public int cellPixels;       // edge of one map's thumbnail in the atlas
    public float worldSize = 60; // world units per square map (for sub-cell player placement)

    public List<Entry> maps = new List<Entry>();

    public Entry Find(string sceneName)
    {
        for (int i = 0; i < maps.Count; i++)
            if (maps[i].scene == sceneName) return maps[i];
        return null;
    }
}
