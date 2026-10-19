using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns a model made from a picture (an OBJ saved as text, plus its colour picture) into a costume worn by the bodies
/// it is fitted to (<see cref="CostumePiece"/>). Run it through the live editor:
/// <c>CostumeBaker.Bake("robe", new[] { "Soldier_Male", "Knight_Male" }, wide, tall, topHeight, margin)</c>. It reads
/// <c>2_Generated/CostumeModels/&lt;name&gt;/</c> and writes <c>2_Generated/Characters/Costumes/&lt;name&gt;/</c> (the
/// fitted costume, the material, the prefab), editing each in place on a re-bake so the item showing it keeps its link.
///
/// <para><b>Where it sits.</b> Every body in the cast shares one mesh space: feet at 0, up is +Z, the back is +Y, arms
/// straight out to the sides. The model stands along +Y with its front toward +Z, as the picture-to-model tool writes
/// it, drawn with its arms straight out the same way. It is turned into the bodies' space, scaled across by
/// <paramref name="wide"/> and up by <paramref name="tall"/>, its top placed at <paramref name="topHeight"/> and its
/// middle on the first body's spine.</para>
///
/// <para><b>One costume for every body.</b> Every point of the costume that sits inside any of the bodies, or closer to
/// its skin than <paramref name="margin"/>, is pushed out to that distance. Every point bends with the bones of the
/// nearest spot of the FIRST body's skin, in the same shares, so a sleeve bends where the arm does. Each body it is
/// fitted to is listed, and is drawn whole under the costume while it is worn.</para>
/// </summary>
public static class CostumeBaker
{
    const string SourceDir = "Assets/2_Generated/CostumeModels/";
    const string OutDir = "Assets/2_Generated/Characters/Costumes/";
    const string BodyDir = "Assets/2_Generated/Characters/Bodies/";
    const string TemplateMaterial = "Assets/2_Generated/Characters/HeadPieces/army_helmet.mat";
    const string SpineBone = "Spine2";
    // The body's sides are about a ten-thousandth of a unit, which Unity's own vector sums round to zero (see
    // BackPieceBaker); the fitting is worked out at this many times the size and scaled back.
    const float Work = 1000f;

    public static string Bake(string name, string[] bodies, float wide, float tall, float topHeight, float margin)
    {
        string src = SourceDir + name + "/";
        var pos = new List<Vector3>();
        var uv = new List<Vector2>();
        var nrm = new List<Vector3>();
        var corners = new List<(int v, int t, int n)>();
        ObjModel.Parse(File.ReadAllText(src + name + ".obj.txt"), pos, uv, nrm, corners);
        float modelTop = float.MinValue;
        Vector2 modelMiddle = Vector2.zero;
        foreach (var p in pos) modelTop = Mathf.Max(modelTop, p.y);
        foreach (var p in pos) modelMiddle += new Vector2(p.x, p.z);
        modelMiddle /= Mathf.Max(1, pos.Count);

        var report = new System.Text.StringBuilder();
        var shapes = new List<SkinnedMeshRenderer>();
        foreach (var bodyName in bodies)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyDir + bodyName + ".prefab");
            var body = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (body == null || body.sharedMesh == null) { report.Append(bodyName).Append(": no body model\n"); continue; }
            if (!shapes.Exists(b => b.sharedMesh.name == body.sharedMesh.name)) shapes.Add(body);
        }
        if (shapes.Count == 0) return report.Append("nothing baked").ToString();

        var first = shapes[0];
        int spine = Find(first.bones, SpineBone);
        Vector3 spineAt = spine >= 0 ? (Vector3)first.sharedMesh.bindposes[spine].inverse.GetColumn(3) : Vector3.zero;
        // Model space (right-handed, +Y up, front +Z) into the bodies' space (+Z up, back +Y), middle on the spine.
        System.Func<Vector3, Vector3> place = a => new Vector3(-(a.x - modelMiddle.x) * wide + spineAt.x,
                                                               -(a.z - modelMiddle.y) * wide + spineAt.y,
                                                               (a.y - modelTop) * tall + topHeight);
        System.Func<Vector3, Vector3> turn = a => new Vector3(-a.x / wide, -a.z / wide, a.y / tall);
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        ObjModel.Build(pos, uv, nrm, corners, place, turn, verts, uvs, normals, tris);

        // PUSHED OUT OF EVERY BODY'S SKIN, then bent like the first body's.
        foreach (var body in shapes)
            report.Append(body.sharedMesh.name).Append(": ").Append(PushOut(body.sharedMesh, verts, margin))
                  .Append(" costume points pushed out\n");
        var weights = BendLike(first.sharedMesh, verts);

        Directory.CreateDirectory(OutDir + name);
        var cloth = new Mesh { name = name + "_costume" };
        cloth.SetVertices(verts);
        // The model's own normals, never worked out again (see BackPieceBaker: at this size Unity's sum is zero).
        cloth.SetNormals(normals);
        cloth.SetUVs(0, uvs);
        cloth.SetTriangles(tris, 0);
        cloth.boneWeights = weights;
        cloth.RecalculateBounds();
        cloth.RecalculateTangents();
        cloth = ObjModel.SaveOrUpdate(cloth, OutDir + name + "/" + cloth.name + ".asset");

        var fits = new List<CostumePiece.Fit>();
        foreach (var body in shapes) fits.Add(new CostumePiece.Fit { bodyMesh = body.sharedMesh.name });

        var boneNames = new string[first.bones.Length];
        for (int i = 0; i < boneNames.Length; i++) boneNames[i] = first.bones[i].name;
        var prefabPath = OutDir + name + "/" + name + ".prefab";
        var root = new GameObject(name);
        var piece = root.AddComponent<CostumePiece>();
        piece.costume = cloth;
        piece.bones = boneNames;
        piece.fits = fits.ToArray();
        piece.material = MaterialFor(name, src);
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return report.Append("costume ").Append(tris.Count / 3).Append(" triangles, ").Append(verts.Count)
                     .Append(" points; prefab ").Append(prefabPath).ToString();
    }

    /// <summary>Fit the baked costume <paramref name="name"/> to more bodies — each must have every bone the costume
    /// bends with — without changing the costume's shape. <paramref name="bodies"/> null = every body in the cast. A body
    /// already fitted is fitted again.</summary>
    public static string FitBodies(string name, string[] bodies)
    {
        var prefabPath = OutDir + name + "/" + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var piece = root.GetComponent<CostumePiece>();
        var report = new System.Text.StringBuilder();
        try
        {
            if (bodies == null)
            {
                var all = new List<string>();
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { BodyDir.TrimEnd('/') }))
                    all.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
                bodies = all.ToArray();
            }
            var fits = new List<CostumePiece.Fit>(piece.fits);
            foreach (var bodyName in bodies)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyDir + bodyName + ".prefab");
                var body = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                if (body == null || body.sharedMesh == null) { report.Append(bodyName).Append(": no body model\n"); continue; }
                var missing = MissingBone(piece.bones, body.bones);
                if (missing != null) { report.Append(bodyName).Append(": no bone ").Append(missing).Append("\n"); continue; }
                var plain = body.sharedMesh;
                fits.RemoveAll(f => f.bodyMesh == plain.name);
                fits.Add(new CostumePiece.Fit { bodyMesh = plain.name });
                report.Append(bodyName).Append(": fitted\n");
            }
            piece.fits = fits.ToArray();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return report.Append(piece.fits.Length).Append(" bodies fitted").ToString();
    }

    // The first of the costume's bones the body lacks, or null when it has them all.
    static string MissingBone(string[] wanted, Transform[] bones)
    {
        foreach (var w in wanted)
        {
            bool found = false;
            foreach (var b in bones) if (b != null && b.name == w) { found = true; break; }
            if (!found) return w;
        }
        return null;
    }

    // Every costume point inside the body, or nearer its skin than `margin`, moved out to `margin`. Returns how many.
    static int PushOut(Mesh plain, List<Vector3> verts, float margin)
    {
        var bodyN = plain.normals;
        var bodyT = plain.triangles;
        var bigBody = Big(plain.vertices);
        int pushed = 0;
        for (int i = 0; i < verts.Count; i++)
        {
            var at = verts[i] * Work;
            Nearest(bigBody, bodyT, at, out int tri, out Vector3 spot, out Vector3 bary);
            var skin = (bodyN[bodyT[tri]] * bary.x + bodyN[bodyT[tri + 1]] * bary.y + bodyN[bodyT[tri + 2]] * bary.z).normalized;
            if (Vector3.Dot(at - spot, skin) >= margin * Work) continue;
            verts[i] = (spot + skin * margin * Work) / Work;
            pushed++;
        }
        return pushed;
    }

    // Each costume point bends with the bones of the nearest spot of the body's skin, in that spot's shares.
    static BoneWeight[] BendLike(Mesh plain, List<Vector3> verts)
    {
        var bodyW = plain.boneWeights;
        var bodyT = plain.triangles;
        var bigBody = Big(plain.vertices);
        var weights = new BoneWeight[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            Nearest(bigBody, bodyT, verts[i] * Work, out int tri, out _, out Vector3 bary);
            weights[i] = Blend(bodyW[bodyT[tri]], bary.x, bodyW[bodyT[tri + 1]], bary.y, bodyW[bodyT[tri + 2]], bary.z);
        }
        return weights;
    }

    static Vector3[] Big(Vector3[] v)
    {
        var big = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) big[i] = v[i] * Work;
        return big;
    }

    // The nearest spot of the surface (points, triangles) to `p`: which triangle, where, and its corners' shares.
    static void Nearest(Vector3[] v, int[] t, Vector3 p, out int tri, out Vector3 spot, out Vector3 bary)
    {
        tri = 0; spot = Vector3.zero; bary = new Vector3(1, 0, 0);
        float best = float.MaxValue;
        for (int i = 0; i < t.Length; i += 3)
        {
            var q = ClosestOnTriangle(p, v[t[i]], v[t[i + 1]], v[t[i + 2]], out Vector3 w);
            float d = (q - p).sqrMagnitude;
            if (d < best) { best = d; tri = i; spot = q; bary = w; }
        }
    }

    // Closest point on triangle abc to p, with its corner shares (Ericson, Real-Time Collision Detection 5.1.5).
    static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 w)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) { w = new Vector3(1, 0, 0); return a; }
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) { w = new Vector3(0, 1, 0); return b; }
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float s = d1 / (d1 - d3);
            w = new Vector3(1 - s, s, 0); return a + s * ab;
        }
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) { w = new Vector3(0, 0, 1); return c; }
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float s = d2 / (d2 - d6);
            w = new Vector3(1 - s, 0, s); return a + s * ac;
        }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float s = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            w = new Vector3(0, 1 - s, s); return b + s * (c - b);
        }
        float denom = 1f / (va + vb + vc);
        float v2 = vb * denom, w2 = vc * denom;
        w = new Vector3(1 - v2 - w2, v2, w2);
        return a + ab * v2 + ac * w2;
    }

    // Three corners' bending in the given shares, kept to the four strongest bones.
    static BoneWeight Blend(BoneWeight a, float sa, BoneWeight b, float sb, BoneWeight c, float sc)
    {
        var sum = new Dictionary<int, float>();
        void Add(int bone, float w) { if (w > 0f) sum[bone] = (sum.TryGetValue(bone, out var x) ? x : 0f) + w; }
        foreach (var (bw, s) in new[] { (a, sa), (b, sb), (c, sc) })
        {
            Add(bw.boneIndex0, bw.weight0 * s); Add(bw.boneIndex1, bw.weight1 * s);
            Add(bw.boneIndex2, bw.weight2 * s); Add(bw.boneIndex3, bw.weight3 * s);
        }
        var list = new List<KeyValuePair<int, float>>(sum);
        list.Sort((x, y) => y.Value.CompareTo(x.Value));
        float total = 0f;
        for (int i = 0; i < list.Count && i < 4; i++) total += list[i].Value;
        var r = new BoneWeight();
        if (list.Count > 0) { r.boneIndex0 = list[0].Key; r.weight0 = list[0].Value / total; }
        if (list.Count > 1) { r.boneIndex1 = list[1].Key; r.weight1 = list[1].Value / total; }
        if (list.Count > 2) { r.boneIndex2 = list[2].Key; r.weight2 = list[2].Value / total; }
        if (list.Count > 3) { r.boneIndex3 = list[3].Key; r.weight3 = list[3].Value / total; }
        return r;
    }

    // The costume's own picture, imported the way painted pictures are, on a material that keeps its own colours.
    static Material MaterialFor(string name, string src)
    {
        var texPath = src + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
        importer.textureType = TextureImporterType.Default;
        // The size the bodies' own pictures are; at 1024 the outfit's was 896,576 bytes, held with the item list.
        importer.maxTextureSize = 512;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        var matPath = OutDir + name + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterial)) { name = name };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        mat.DisableKeyword("_CAPE_TINT");
        mat.SetFloat("_OwnColours", 1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static int Find(Transform[] bones, string name)
    {
        for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name.EndsWith(name)) return i;
        return -1;
    }
}
