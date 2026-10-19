using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The models of bodies wearing costumes: each body model joined with a costume (<see cref="CostumePiece"/>) into one,
/// made the first time anybody on this machine wears that pair, shared by everyone wearing it, and let go when the last
/// of them takes it off or leaves.
///
/// <para><b>What it costs.</b> Joining copies the two models' points once per pair; nothing runs per frame. A joined
/// model is the size of the two together — 537,016 bytes for the soldier in the adventurer outfit, measured 2026-10-16 —
/// held only while somebody wears it.</para>
/// </summary>
public static class CostumeModels
{
    sealed class Made
    {
        public Mesh mesh;
        public int wearers;
    }

    static readonly Dictionary<(CostumePiece, Mesh), Made> made = new Dictionary<(CostumePiece, Mesh), Made>();

    /// <summary>The model of <paramref name="plain"/> (the body's own, drawn by <paramref name="body"/>) wearing
    /// <paramref name="piece"/>, counted as worn once more; null when the costume was never fitted to that body. Every
    /// non-null answer is given back with <see cref="TakeOff"/>.</summary>
    public static Mesh Wear(CostumePiece piece, Mesh plain, SkinnedMeshRenderer body)
    {
        if (piece == null || plain == null || body == null) return null;
        if (!made.TryGetValue((piece, plain), out var m))
        {
            var mesh = piece.FitFor(plain.name) != null ? Join(piece, plain, body.bones) : null;
            if (mesh == null) return null;
            m = new Made { mesh = mesh };
            made[(piece, plain)] = m;
        }
        m.wearers++;
        return m.mesh;
    }

    /// <summary>One wearer of <paramref name="piece"/> on <paramref name="plain"/> is done with it; the last one lets
    /// the joined model go.</summary>
    public static void TakeOff(CostumePiece piece, Mesh plain)
    {
        if (piece == null || plain == null || !made.TryGetValue((piece, plain), out var m)) return;
        if (--m.wearers > 0) return;
        made.Remove((piece, plain));
        if (m.mesh != null) Object.Destroy(m.mesh);
    }

    /// <summary>The body's own parts, every triangle kept, then the costume as one more part, its bone weights pointed
    /// at the body's own bones by name. Null when the body lacks a bone the costume bends with.</summary>
    /// <remarks>The whole body stays under the cloth on purpose: leaving the covered skin out opened holes at the ends
    /// of the cloth (neck, wrists, waist) that showed the sky through the body from some angles.</remarks>
    public static Mesh Join(CostumePiece piece, Mesh plain, Transform[] bodyBones)
    {
        var costume = piece.costume;
        if (costume == null) return null;

        var boneOf = new int[piece.bones.Length];
        for (int i = 0; i < piece.bones.Length; i++)
        {
            boneOf[i] = -1;
            for (int b = 0; b < bodyBones.Length; b++)
                if (bodyBones[b] != null && bodyBones[b].name == piece.bones[i]) { boneOf[i] = b; break; }
            if (boneOf[i] < 0)
            {
                GameLog.Warn(LogCat.Client, "the costume '" + piece.name + "' bends with the bone '" + piece.bones[i] +
                                            "', which the body '" + plain.name + "' does not have");
                return null;
            }
        }

        int bodyCount = plain.vertexCount;
        var verts = new List<Vector3>(bodyCount + costume.vertexCount);
        var more = new List<Vector3>(costume.vertexCount);
        plain.GetVertices(verts); costume.GetVertices(more); verts.AddRange(more);
        var normals = new List<Vector3>(verts.Count);
        plain.GetNormals(normals); more.Clear(); costume.GetNormals(more); normals.AddRange(more);
        var uvs = new List<Vector2>(verts.Count);
        var moreUv = new List<Vector2>(costume.vertexCount);
        plain.GetUVs(0, uvs); costume.GetUVs(0, moreUv); uvs.AddRange(moreUv);
        var weights = new List<BoneWeight>(plain.boneWeights);
        foreach (var w in costume.boneWeights)
            weights.Add(new BoneWeight
            {
                boneIndex0 = boneOf[w.boneIndex0], weight0 = w.weight0,
                boneIndex1 = boneOf[w.boneIndex1], weight1 = w.weight1,
                boneIndex2 = boneOf[w.boneIndex2], weight2 = w.weight2,
                boneIndex3 = boneOf[w.boneIndex3], weight3 = w.weight3,
            });

        var mesh = new Mesh { name = plain.name + " in " + piece.name };
        mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        if (plain.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)
            && costume.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent))
        {
            var tangents = new List<Vector4>(verts.Count);
            var moreT = new List<Vector4>(costume.vertexCount);
            plain.GetTangents(tangents); costume.GetTangents(moreT); tangents.AddRange(moreT);
            mesh.SetTangents(tangents);
        }
        mesh.subMeshCount = plain.subMeshCount + 1;
        for (int s = 0; s < plain.subMeshCount; s++) mesh.SetTriangles(plain.GetTriangles(s), s);
        var cloth = costume.triangles;
        for (int i = 0; i < cloth.Length; i++) cloth[i] += bodyCount;
        mesh.SetTriangles(cloth, plain.subMeshCount);
        mesh.boneWeights = weights.ToArray();
        mesh.bindposes = plain.bindposes;
        mesh.RecalculateBounds();
        return mesh;
    }
}
