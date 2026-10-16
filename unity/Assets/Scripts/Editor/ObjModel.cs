using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reading a model made from a picture (an OBJ saved as text) into the shared body space, and saving a baked mesh in
/// place — the steps every baker of worn pieces shares (<c>BackPieceBaker</c>, <c>CostumeBaker</c>).
/// </summary>
public static class ObjModel
{
    /// <summary>The OBJ's points, picture positions, normals, and every triangle corner as (point, picture position,
    /// normal) indices.</summary>
    public static void Parse(string text, List<Vector3> pos, List<Vector2> uv, List<Vector3> nrm,
                             List<(int v, int t, int n)> corners)
    {
        foreach (var raw in text.Split('\n'))
        {
            var p = raw.Trim().Split(' ');
            if (p.Length < 3) continue;
            if (p[0] == "v") pos.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
            else if (p[0] == "vt") uv.Add(new Vector2(F(p[1]), F(p[2])));
            else if (p[0] == "vn") nrm.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
            else if (p[0] == "f" && p.Length == 4)
                for (int k = 1; k <= 3; k++)
                {
                    var q = p[k].Split('/');
                    corners.Add((int.Parse(q[0]) - 1, int.Parse(q[1]) - 1, int.Parse(q[2]) - 1));
                }
        }
    }

    /// <summary>One point per distinct corner, each point put where <paramref name="place"/> says and each normal
    /// turned by <paramref name="turn"/>, and every triangle wound so Unity draws the side the model's own normals
    /// face.</summary>
    public static void Build(List<Vector3> pos, List<Vector2> uv, List<Vector3> nrm, List<(int v, int t, int n)> corners,
                             System.Func<Vector3, Vector3> place, System.Func<Vector3, Vector3> turn,
                             List<Vector3> verts, List<Vector2> uvs, List<Vector3> normals, List<int> tris)
    {
        var seen = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < corners.Count; i += 3)
        {
            var idx = new int[3];
            for (int k = 0; k < 3; k++)
            {
                var c = corners[i + k];
                if (!seen.TryGetValue(c, out int at))
                {
                    at = verts.Count;
                    seen[c] = at;
                    verts.Add(place(pos[c.v]));
                    uvs.Add(uv[c.t]);
                    normals.Add(turn(nrm[c.n]).normalized);
                }
                idx[k] = at;
            }
            // Unity draws the side whose corners run clockwise; keep the side the model's own normals face.
            Vector3 a = verts[idx[0]], e = verts[idx[1]], d = verts[idx[2]];
            Vector3 face = Vector3.Cross(e - a, d - a);
            Vector3 want = normals[idx[0]] + normals[idx[1]] + normals[idx[2]];
            if (Vector3.Dot(face, want) < 0f) (idx[1], idx[2]) = (idx[2], idx[1]);
            tris.AddRange(idx);
        }
    }

    /// <summary>Save <paramref name="made"/> at <paramref name="path"/>, or rewrite the mesh already there in place so
    /// everything pointing at it keeps its link. Returns the saved mesh.</summary>
    public static Mesh SaveOrUpdate(Mesh made, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(made, path);
            return made;
        }
        // Rewritten through the mesh's own calls: copying serialized data over a live mesh leaves its old drawing data
        // and bone weights in the running editor (docs/5-ai-learnings/rebuilding-an-asset-blanks-open-scene-references.md).
        existing.Clear();
        existing.indexFormat = made.indexFormat;
        existing.SetVertices(made.vertices);
        existing.SetNormals(made.normals);
        existing.SetUVs(0, made.uv);
        existing.subMeshCount = made.subMeshCount;
        for (int i = 0; i < made.subMeshCount; i++) existing.SetTriangles(made.GetTriangles(i), i);
        existing.boneWeights = made.boneWeights;
        existing.bindposes = made.bindposes;
        existing.RecalculateBounds();
        existing.RecalculateTangents();
        Object.DestroyImmediate(made);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    public static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}
