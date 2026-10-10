using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A hair cut where a hat sits on the head: in every direction around the head, the hair higher than the hat comes
/// down in that direction is cut away exactly — triangles crossing the line included — so a worn hat shows the hair
/// hanging below it and nothing above it (<see cref="HeadPiece.cutRim"/>).
///
/// <para>The arithmetic is plain functions on numbers (<see cref="Rim"/>, <see cref="Above"/>, <see cref="Cut"/>).
/// <see cref="Below"/> wraps them in a mesh and keeps one per (hair, hat) for the session, shared by everybody
/// wearing that pair — the cost is one cut the first time a hair meets a hat, never per person and never per frame.
/// A fixed set of meshes made at author time instead would have been 13 x 10 copies, about 26 MB of hair, shipped
/// whether anybody wore them or not.</para>
/// </summary>
public static class HairCut
{
    static readonly Dictionary<(Mesh, HeadPiece), Mesh> made = new Dictionary<(Mesh, HeadPiece), Mesh>();

    /// <summary>The part of <paramref name="hair"/> that shows under <paramref name="hat"/>. Made once per pair.</summary>
    public static Mesh Below(Mesh hair, HeadPiece hat)
    {
        if (hair == null || hat == null || hat.cutRim == null || hat.cutRim.Length == 0) return hair;
        var key = (hair, hat);
        if (made.TryGetValue(key, out var cut)) return cut;

        var verts = hair.vertices;
        var side = new float[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            side[i] = Above(verts[i], hat.cutCentre, hat.cutUp, hat.cutAcross, hat.cutRim);

        var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        Cut(verts, hair.normals, hair.uv, hair.triangles, side, pos, nrm, uv, tris);
        cut = new Mesh { name = hair.name + " under " + hat.name };
        cut.indexFormat = pos.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        cut.SetVertices(pos);
        cut.SetNormals(nrm);
        cut.SetUVs(0, uv);
        cut.SetTriangles(tris, 0);
        cut.RecalculateBounds();
        cut.UploadMeshData(true);
        made[key] = cut;
        return cut;
    }

    /// <summary>How far <paramref name="p"/> stands above the hat's lower edge in its own direction around the head:
    /// positive is cut away, zero or less is kept.</summary>
    public static float Above(Vector3 p, Vector3 centre, Vector3 up, Vector3 across, float[] rim)
    {
        var d = p - centre;
        float height = Vector3.Dot(d, up);
        var flat = d - up * height;
        var other = Vector3.Cross(up, across);
        float angle = Mathf.Atan2(Vector3.Dot(flat, other), Vector3.Dot(flat, across));
        return height - Rim(rim, angle);
    }

    /// <summary>The hat's lower edge at <paramref name="angle"/> (radians, 0 along Cut Across), read between the two
    /// nearest of its evenly spaced directions.</summary>
    public static float Rim(float[] rim, float angle)
    {
        int n = rim.Length;
        float at = Mathf.Repeat(angle / (2f * Mathf.PI), 1f) * n;
        int a = Mathf.FloorToInt(at) % n;
        int b = (a + 1) % n;
        return Mathf.Lerp(rim[a], rim[b], at - Mathf.Floor(at));
    }

    /// <summary>Keep every part whose <paramref name="side"/> is zero or less; a triangle crossing the line is cut
    /// along it into one or two triangles, its new corners placed where the side reaches zero along each edge, with
    /// their normal and texture position blended. Fills the four lists.</summary>
    public static void Cut(Vector3[] pos, Vector3[] nrm, Vector2[] uv, int[] tris, float[] side,
                           List<Vector3> outPos, List<Vector3> outNrm, List<Vector2> outUv, List<int> outTris)
    {
        var kept = new Dictionary<int, int>();
        var onEdge = new Dictionary<long, int>();
        var corner = new int[4];

        int Keep(int i)
        {
            if (kept.TryGetValue(i, out int k)) return k;
            k = outPos.Count; kept[i] = k;
            outPos.Add(pos[i]); outNrm.Add(nrm[i]); outUv.Add(uv[i]);
            return k;
        }

        int Cross(int a, int b)
        {
            long key = a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;
            if (onEdge.TryGetValue(key, out int k)) return k;
            float t = side[a] / (side[a] - side[b]);
            k = outPos.Count; onEdge[key] = k;
            outPos.Add(Vector3.Lerp(pos[a], pos[b], t));
            outNrm.Add(Vector3.Lerp(nrm[a], nrm[b], t).normalized);
            outUv.Add(Vector2.Lerp(uv[a], uv[b], t));
            return k;
        }

        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            int n = 0;
            for (int e = 0; e < 3; e++)
            {
                int a = tris[t + e], b = tris[t + (e + 1) % 3];
                if (side[a] <= 0f) corner[n++] = Keep(a);
                if ((side[a] <= 0f) != (side[b] <= 0f)) corner[n++] = Cross(a, b);
            }
            for (int k = 1; k + 1 < n; k++) { outTris.Add(corner[0]); outTris.Add(corner[k]); outTris.Add(corner[k + 1]); }
        }
    }
}
