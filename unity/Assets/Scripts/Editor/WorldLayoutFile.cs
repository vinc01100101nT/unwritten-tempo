using System;
using System.IO;
using UnityEngine;

/// <summary>
/// THE WORLD LAYOUT FILE — <c>tools/world-creator/worlds/world-regions.json</c>, written by the browser World Creator
/// and the one owner of every map's place and every door between maps. This class is the one place the editor learns
/// where that file is, and the one place that turns a door's saved anchor into a spot in its map.
///
/// <para><b>The door rule</b> (the browser tool states the same one): a door stands at its anchor's <c>at</c> when it
/// has one, and otherwise on the rim of its map — the map's box pulled in by <see cref="EdgeMargin"/> — at
/// <c>offset</c> along the named side, counted west to east on the north and south sides and NORTH TO SOUTH on the
/// east and west sides. Whoever walks through arrives at the anchor's <c>arriveAt</c> when it has one, and otherwise
/// <see cref="SpawnInset"/> inside the door. Every spot is metres from the map's centre, x east, z north.</para>
/// </summary>
public static class WorldLayoutFile
{
    public const float EdgeMargin = 4f;
    public const float SpawnInset = 3f;
    public const float DefaultSize = 60f;   // a map that omits its size (the browser tool's DEFAULT_SIZE)

    public static string Path => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(Application.dataPath, "..", "..", "tools", "world-creator", "worlds", "world-regions.json"));

    /// <summary>Whether <paramref name="path"/> names the world layout file itself, however it is spelled.</summary>
    public static bool IsThisFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try { return string.Equals(System.IO.Path.GetFullPath(path), Path, StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    public static string ReadText() => File.ReadAllText(Path);

    /// <summary>Where a door stands in its map. <paramref name="at"/> is the anchor's exact spot, or null/empty when it
    /// has none.</summary>
    public static void DoorPoint(string edge, float offset, float sizeX, float sizeZ, float[] at, out float x, out float z)
    {
        if (at != null && at.Length >= 2) { x = at[0]; z = at[1]; return; }
        float t = offset < 0f ? 0f : offset > 1f ? 1f : offset;
        float hx = sizeX * 0.5f - EdgeMargin, hz = sizeZ * 0.5f - EdgeMargin;
        float alongX = -hx + 2f * hx * t;   // west → east
        float alongZ = hz - 2f * hz * t;    // north → south
        switch (edge)
        {
            case "S": x = alongX; z = -hz; return;
            case "E": x = +hx; z = alongZ; return;
            case "W": x = -hx; z = alongZ; return;
            default:  x = alongX; z = +hz; return;   // "N"
        }
    }

    /// <summary>Where somebody walking through that door from the other side arrives.</summary>
    public static void ArrivalPoint(string edge, float offset, float sizeX, float sizeZ, float[] at, float[] arriveAt,
                                    out float x, out float z)
    {
        if (arriveAt != null && arriveAt.Length >= 2) { x = arriveAt[0]; z = arriveAt[1]; return; }
        DoorPoint(edge, offset, sizeX, sizeZ, at, out x, out z);
        switch (edge)
        {
            case "N": z -= SpawnInset; break;
            case "S": z += SpawnInset; break;
            case "E": x -= SpawnInset; break;
            case "W": x += SpawnInset; break;
        }
    }

    /// <summary>The side of the map's box nearest a spot, and how far along that side the spot falls (0..1, in the
    /// direction <see cref="DoorPoint"/> counts) — what an anchor with an exact spot carries as its edge and offset.</summary>
    public static void NearestSide(float x, float z, float sizeX, float sizeZ, out string edge, out float offset)
    {
        float hx = sizeX * 0.5f, hz = sizeZ * 0.5f;
        float best = hz - z; edge = "N";
        if (z + hz < best) { best = z + hz; edge = "S"; }
        if (x + hx < best) { best = x + hx; edge = "W"; }
        if (hx - x < best) { edge = "E"; }
        float t = edge == "N" || edge == "S" ? (hx > 0f ? (x + hx) / (2f * hx) : 0.5f)
                                             : (hz > 0f ? (hz - z) / (2f * hz) : 0.5f);
        offset = t < 0f ? 0f : t > 1f ? 1f : t;
    }
}
