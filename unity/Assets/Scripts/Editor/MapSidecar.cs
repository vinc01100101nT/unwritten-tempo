using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Writes a Map Painter <c>map.json</c> SIDECAR beside a freshly-baked scene, so any GENERATED map can be re-opened
/// in the web Map Painter (<i>Import map…</i>), hand-edited, and re-baked via <see cref="MapPainterImporter"/> — the
/// round-trip that makes auto-generated maps editable (the "World Creator owns generation, Map Painter owns edits"
/// workflow). The schema mirrors EXACTLY what <see cref="MapPainterImporter"/> reads and the painter's
/// <c>importMap()</c> consumes:
/// <code>{ version, name, cellSize, size{x,z}, cols, rows, ground[{brush,cx,cz}], objects[{brush,prefab,x,z,yaw}] }</code>
///
/// <para><b>Coordinate inverse.</b> The importer maps a painter cell/prop into the centred Unity scene with a
/// vertical flip (painter row 0 = north) plus a centre translation, and adds the palette's per-brush
/// <c>forwardOffset</c> to each prop's yaw. This writer applies the exact inverse so a re-import reproduces the same
/// world positions, facings and ground.</para>
///
/// <para><b>Lossy by design (painter limits, not bugs):</b> the painter has no per-instance scale (re-import seats
/// everything at the palette's <c>defaultScale</c>, so per-prop scale jitter is dropped) and no mob/biome layer
/// (mobs and the map's biome tint aren't represented). Layout, prefab choice, facing, roads and dirt round-trip.</para>
/// </summary>
public static class MapSidecar
{
    [System.Serializable] public class Vec2 { public float x, z; public Vec2() {} public Vec2(float x, float z) { this.x = x; this.z = z; } }
    [System.Serializable] public class GroundCell { public string brush; public int cx, cz; }
    [System.Serializable] public class ObjectCell { public string brush, prefab; public float x, z, yaw; }

    [System.Serializable]
    public class Doc
    {
        public int version = 1;
        public string name;
        public float cellSize = 4f;
        public Vec2 size;
        public int cols, rows;
        public List<GroundCell> ground = new List<GroundCell>();
        public List<ObjectCell> objects = new List<ObjectCell>();
    }

    // Generated sidecars live OUTSIDE Assets/ (so Unity doesn't import them as TextAssets), collected next to the
    // painter at tools/map-painter/exports/<name>.map.json — open one there with the painter's "Import map…".
    public static string ExportsDir()
        => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "tools", "map-painter", "exports"));

    public static string PathFor(string mapName) => Path.Combine(ExportsDir(), Sanitize(mapName) + ".map.json");

    // Inverse of MapPainterImporter's prop mapping:
    //   importer:  worldX = painterX - sizeX/2 ;  worldZ = (sizeZ - painterZ) - sizeZ/2 ;  yaw = painterYaw + fwdOffset
    //   inverse :  painterX = worldX + sizeX/2 ;  painterZ = sizeZ/2 - worldZ           ;  painterYaw = worldYaw - fwdOffset
    public static ObjectCell ObjectFromWorld(string brush, string prefab, Vector3 world, float worldYawDeg,
                                             float sizeX, float sizeZ, float forwardOffset = 0f)
        => new ObjectCell
        {
            brush  = brush,
            prefab = prefab,
            x   = Round(world.x + sizeX * 0.5f),
            z   = Round(sizeZ * 0.5f - world.z),
            yaw = Round(Mathf.Repeat(worldYawDeg - forwardOffset, 360f)),
        };

    /// <summary>Rasterise a continuous ground sampler into painter ground cells — one dominant brush per cell, grass
    /// (the base layer) omitted. Cells are emitted in PAINTER file space (row 0 = north): we sample at the world
    /// point the importer will map each file cell back to (it flips cz on the way in), so the round-trip is exact.</summary>
    public static List<GroundCell> RasterizeGround(GroundBlendBuilder.GroundSampler sample, Vector3 center,
                                                   float sizeX, float sizeZ, float cellSize, int cols, int rows,
                                                   string roadBrush)
    {
        var list = new List<GroundCell>();
        for (int cx = 0; cx < cols; cx++)
            for (int czf = 0; czf < rows; czf++)
            {
                int czUsed = (rows - 1) - czf;                                  // importer flips cz on input
                float wx = center.x + (cx + 0.5f) * cellSize - sizeX * 0.5f;
                float wz = center.z + (czUsed + 0.5f) * cellSize - sizeZ * 0.5f;
                sample(wx, wz, out Vector4 w);
                string brush = ClassifyGround(w, roadBrush);
                if (brush != null) list.Add(new GroundCell { brush = brush, cx = cx, cz = czf });
            }
        return list;
    }

    // Winning ground brush for a sampled weight vector, or null for grass (the base, never stored). A path/road reads
    // over a dirt patch (the GroundBlend shader layers the path last too). Channels: .x road, .y stone, .z rock, .w dirt.
    static string ClassifyGround(Vector4 w, string roadBrush)
    {
        if (w.x >= 0.5f) return roadBrush;
        if (w.y >= 0.5f) return "stone";
        if (w.z >= 0.5f) return "rock";
        if (w.w >= 0.5f) return "dirt";
        return null;
    }

    public static void Write(Doc doc, string mapName)
    {
        Directory.CreateDirectory(ExportsDir());
        File.WriteAllText(PathFor(mapName), JsonUtility.ToJson(doc, true));
    }

    static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
