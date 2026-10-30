using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A TOWN'S GROUND TILES, any number of them (the user, 2026-10-29: "we will import more road tiles"). The town's tile list
/// becomes one stack of tile pictures (a texture array, one layer per tile), and every point of its ground says which
/// tile it wears; the ground shader (Unwritten/GroundTiles) blends the tiles of each triangle's three corners, so two tiles
/// meet in the same soft quarter-unit edge the five-look ground draws. Adding a tile is one more entry in the plan.
/// </summary>
public static class GroundTileArray
{
    // Every layer is this many pixels square: the ground tiles are imported at 1024 (1254-pixel sources) or 2048, and at
    // their tilings 1024 is already over 400 pixels a unit of ground.
    const int Size = 1024;
    public const string ShaderName = "Unwritten/GroundTiles";

    /// <summary>The tiles called <paramref name="names"/> (GroundTiles), stacked in that order and saved at
    /// <paramref name="path"/>: compressed as the tiles themselves are, with their mipmaps, not kept readable in the game.</summary>
    public static Texture2DArray Build(string[] names, string path)
    {
        var array = new Texture2DArray(Size, Size, names.Length, TextureFormat.DXT1, true, false)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path), wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear, anisoLevel = 4,
        };
        var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var was = RenderTexture.active;
        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                var src = GroundTiles.Load(names[i]) ?? throw new System.Exception("no ground tile " + names[i]);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply(true);
                EditorUtility.CompressTexture(tex, TextureFormat.DXT1, TextureCompressionQuality.Normal);
                for (int m = 0; m < tex.mipmapCount; m++) array.SetPixelData(tex.GetPixelData<byte>(m), m, i);
                Object.DestroyImmediate(tex);
            }
        }
        finally
        {
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
        }
        array.Apply(false, false);
        AssetDatabase.CreateAsset(array, path);
        var so = new SerializedObject(array);
        var readable = so.FindProperty("m_IsReadable");
        if (readable != null) { readable.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
        AssetDatabase.SaveAssetIfDirty(array);
        return array;
    }

    public static Material NewMaterial(Texture2DArray tiles, string name)
    {
        var shader = Shader.Find(ShaderName) ?? throw new System.Exception("no shader " + ShaderName);
        var mat = new Material(shader) { name = name };
        mat.SetTexture("_Tiles", tiles);
        return mat;
    }

    /// <summary>Turns the tile number each top point carries (its colour's red, as the ground was laid) into what the shader
    /// reads: every top triangle's three corner tiles (uv2) and their tilings (uv3), and the corner each point is (its
    /// colour, one of red, green, blue). Where a triangle's corners wear different tiles it gets points of its own; the
    /// rest keep sharing theirs. The other submeshes (curbs, bridges) are untouched. Returns how many triangles were split.</summary>
    public static int Spread(Mesh mesh, float[] tilings)
    {
        var verts = new List<Vector3>(); mesh.GetVertices(verts);
        var norms = new List<Vector3>(); mesh.GetNormals(norms);
        var uvs = new List<Vector2>(); mesh.GetUVs(0, uvs);
        var cols = new List<Color>(); mesh.GetColors(cols);
        var tans = new List<Vector4>(); mesh.GetTangents(tans);
        int count = verts.Count;
        var subs = new List<int[]>();
        for (int sm = 0; sm < mesh.subMeshCount; sm++) subs.Add(mesh.GetTriangles(sm));
        var top = subs[0];
        var tile = new int[count];
        var onTop = new bool[count];
        foreach (int v in top) { onTop[v] = true; tile[v] = Mathf.RoundToInt(cols[v].r); }
        var tiles = new List<Vector3>(new Vector3[count]);
        var scales = new List<Vector3>(new Vector3[count]);
        for (int v = 0; v < count; v++)
        {
            if (!onTop[v]) continue;
            int t = tile[v];
            if (t < 0 || t >= tilings.Length) throw new System.Exception($"a ground point wears tile {t}; the list has {tilings.Length}");
            tiles[v] = new Vector3(t, t, t);
            scales[v] = Vector3.one * tilings[t];
            cols[v] = new Color(1f, 0f, 0f, 1f);
        }
        int split = 0;
        for (int k = 0; k + 2 < top.Length; k += 3)
        {
            int a = tile[top[k]], b = tile[top[k + 1]], c = tile[top[k + 2]];
            if (a == b && b == c) continue;
            split++;
            for (int q = 0; q < 3; q++)
            {
                int v = top[k + q];
                verts.Add(verts[v]); norms.Add(norms[v]); uvs.Add(uvs[v]);
                if (tans.Count > 0) tans.Add(tans[v]);
                cols.Add(q == 0 ? new Color(1f, 0f, 0f, 1f) : q == 1 ? new Color(0f, 1f, 0f, 1f) : new Color(0f, 0f, 1f, 1f));
                tiles.Add(new Vector3(a, b, c));
                scales.Add(new Vector3(tilings[a], tilings[b], tilings[c]));
                top[k + q] = verts.Count - 1;
            }
        }
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetColors(cols);
        if (tans.Count == verts.Count) mesh.SetTangents(tans);
        mesh.SetUVs(2, tiles); mesh.SetUVs(3, scales);
        // No fade into the world's ground yet (the world builder writes it on its copy): said outright, because a channel
        // the mesh lacks is read as whatever the graphics card supplies.
        mesh.SetUVs(4, new List<Vector2>(new Vector2[verts.Count]));
        for (int sm = 0; sm < subs.Count; sm++) mesh.SetTriangles(subs[sm], sm);
        mesh.RecalculateBounds();
        return split;
    }
}
