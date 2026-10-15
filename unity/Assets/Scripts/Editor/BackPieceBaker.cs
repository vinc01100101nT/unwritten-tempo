using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns a model made from a picture (an OBJ saved as text, plus its colour picture) into a piece worn on the upper
/// back — a cape — with its own swinging bones (<see cref="BackPiece"/>). Run it through the live editor:
/// <c>BackPieceBaker.Bake("red_cape", ...)</c>. It reads <c>2_Generated/BackPieceModels/&lt;name&gt;/</c> and writes
/// <c>2_Generated/Characters/BackPieces/&lt;name&gt;</c> (mesh, material, prefab), editing each in place on a re-bake
/// so the item showing it keeps its link.
///
/// <para><b>Where it sits.</b> Every body in the cast shares one mesh space: feet at 0, up is +Z, the back is +Y. The
/// model stands along +Y with its open front toward +Z, as the picture-to-model tool writes it; it is turned into that
/// space, scaled by <paramref name="scale"/>, and its top placed at <paramref name="topHeight"/>.</para>
///
/// <para><b>How it bends.</b> Everything above <paramref name="clothTop"/> (the collar and shoulder flaps, in the
/// model's own height) is fixed. Below it hang three columns of <paramref name="rows"/> bones each — down the left
/// side, the middle of the back and the right side — and each point of the cloth follows the four bones nearest it,
/// by height and by side. A side column can swing apart from the back, which is what lets an arm push it open.</para>
/// </summary>
public static class BackPieceBaker
{
    const string SourceDir = "Assets/2_Generated/BackPieceModels/";
    const string OutDir = "Assets/2_Generated/Characters/BackPieces/";
    const string TemplateMaterial = "Assets/2_Generated/Characters/HeadPieces/army_helmet.mat";
    const string PlainBody = "Assets/2_Generated/Characters/Bodies/BaseCharacter.prefab";
    const int Columns = 3;
    // A side column hangs this far out toward the cloth's edge, as a share of the cloth's half-width at that height.
    const float SideColumnShare = 0.65f;

    public static string Bake(string name, float scale, float topHeight, float clothTop, int rows, float frontSlack)
    {
        string src = SourceDir + name + "/";
        var text = File.ReadAllText(src + name + ".obj.txt");
        var pos = new List<Vector3>();
        var uv = new List<Vector2>();
        var nrm = new List<Vector3>();
        var corners = new List<(int v, int t, int n)>();
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

        float modelTop = float.MinValue, modelBottom = float.MaxValue;
        foreach (var c in corners) { modelTop = Mathf.Max(modelTop, pos[c.v].y); modelBottom = Mathf.Min(modelBottom, pos[c.v].y); }
        // Model space (right-handed, +Y up, open front +Z) into the shared mesh space (+Z up, back +Y).
        Vector3 Place(Vector3 a) => new Vector3(-a.x * scale, -a.z * scale, (a.y - modelTop) * scale + topHeight);
        Vector3 Turn(Vector3 a) => new Vector3(-a.x, -a.z, a.y);

        float pivotTop = Place(new Vector3(0, clothTop, 0)).z;
        float hem = Place(new Vector3(0, modelBottom, 0)).z;
        float step = (pivotTop - hem) / rows;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
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
                    verts.Add(Place(pos[c.v]));
                    uvs.Add(uv[c.t]);
                    normals.Add(Turn(nrm[c.n]).normalized);
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

        // Where each side column hangs at each row's pivot, and at the hem: a share of the cloth's half-width there.
        var side = new float[rows + 1];
        for (int k = 0; k <= rows; k++)
        {
            float z = pivotTop - k * step, half = 0f;
            foreach (var v in verts) if (Mathf.Abs(v.z - z) < step * 0.5f) half = Mathf.Max(half, Mathf.Abs(v.x));
            side[k] = Mathf.Max(half * SideColumnShare, 1e-5f);
        }
        var weights = new BoneWeight[verts.Count];
        for (int i = 0; i < verts.Count; i++) weights[i] = WeightAt(verts[i], pivotTop, step, rows, side);

        Directory.CreateDirectory(OutDir);
        var mesh = new Mesh { name = name + "_mesh" };
        mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.boneWeights = weights;

        // Bone 0 is the piece's root (fixed). Then each column top to bottom: left, middle, right.
        int count = 1 + Columns * rows;
        var bind = new Matrix4x4[count];
        bind[0] = Matrix4x4.identity;
        for (int c = 0; c < Columns; c++)
            for (int k = 0; k < rows; k++)
                bind[Index(c, k, rows)] = Matrix4x4.Translate(-Pivot(c, k, pivotTop, step, side));
        mesh.bindposes = bind;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        mesh = SaveOrUpdate(mesh, OutDir + name + "_mesh.asset");

        var texPath = src + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
        importer.textureType = TextureImporterType.Default;
        importer.maxTextureSize = 1024;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        var matPath = OutDir + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterial)) { name = name };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        // Cloth is seen from both sides — the lining shows inside the open front.
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(mat);

        var root = new GameObject(name);
        var chain = new Transform[Columns * rows];
        var tips = new Vector3[Columns * rows];
        var allBones = new Transform[count];
        allBones[0] = root.transform;
        string[] word = { "Left", "Middle", "Right" };
        for (int c = 0; c < Columns; c++)
        {
            Transform parent = root.transform;
            Vector3 above = Vector3.zero;
            for (int k = 0; k < rows; k++)
            {
                var bone = new GameObject("Sway" + word[c] + (k + 1)).transform;
                bone.SetParent(parent, false);
                Vector3 at = Pivot(c, k, pivotTop, step, side);
                bone.localPosition = at - above;
                int i = Index(c, k, rows);
                chain[i - 1] = bone;
                tips[i - 1] = Pivot(c, k + 1, pivotTop, step, side) - at;
                allBones[i] = bone;
                parent = bone;
                above = at;
            }
        }
        var smr = root.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.sharedMaterial = mat;
        smr.bones = allBones;
        smr.rootBone = root.transform;
        smr.quality = SkinQuality.Bone4;
        var b = mesh.bounds;
        b.Expand(b.size.z);
        smr.localBounds = b;
        var piece = root.AddComponent<BackPiece>();
        piece.chain = chain;
        piece.tips = tips;
        piece.backward = Vector3.up;
        piece.backClearance = -frontSlack;
        piece.armRadius = ArmRadius();

        var prefabPath = OutDir + name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return prefabPath + ": " + verts.Count + " points, " + tris.Count / 3 + " triangles, " + Columns + "x" + rows +
               " bones, cloth from " + pivotTop.ToString("F5") + " to hem " + hem.ToString("F5") + ", side columns at " +
               side[0].ToString("F5") + ".." + side[rows].ToString("F5") + ", arm radius " + piece.armRadius.ToString("F5");
    }

    static int Index(int column, int row, int rows) => 1 + column * rows + row;

    // Row k's pivot in a column; row == rows is the hem. The middle column hangs down the centre of the back.
    static Vector3 Pivot(int column, int row, float pivotTop, float step, float[] side) =>
        new Vector3((column - 1) * side[row], 0f, pivotTop - row * step);

    // A point follows the two rows nearest its height and, across, the middle column and the side column on its side.
    static BoneWeight WeightAt(Vector3 v, float pivotTop, float step, int rows, float[] side)
    {
        if (v.z >= pivotTop) return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        float along = (pivotTop - v.z) / step;
        int k = Mathf.Min(Mathf.FloorToInt(along), rows - 1);
        float f = Mathf.Clamp01(along - k);
        int lowerRow = Mathf.Min(k + 1, rows - 1);
        float across = Mathf.Lerp(side[k], side[k + 1], f);
        float u = Mathf.Clamp(v.x / across, -1f, 1f);
        int sideColumn = u < 0f ? 0 : 2;
        float s = Mathf.Abs(u);
        if (lowerRow == k) f = 0f;
        return new BoneWeight
        {
            boneIndex0 = Index(1, k, rows), weight0 = (1f - f) * (1f - s),
            boneIndex1 = Index(sideColumn, k, rows), weight1 = (1f - f) * s,
            boneIndex2 = Index(1, lowerRow, rows), weight2 = f * (1f - s),
            boneIndex3 = Index(sideColumn, lowerRow, rows), weight3 = f * s,
        };
    }

    // How thick the upper arm is, in the shared mesh space: the middle distance of the plain body's upper-arm skin from
    // the line between shoulder joint and elbow. Every body shares this skeleton.
    static float ArmRadius()
    {
        var body = AssetDatabase.LoadAssetAtPath<GameObject>(PlainBody).GetComponentInChildren<SkinnedMeshRenderer>();
        var bones = body.bones;
        int arm = -1, fore = -1;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].name.EndsWith("LeftArm")) arm = i;
            if (bones[i].name.EndsWith("LeftForeArm")) fore = i;
        }
        var mesh = body.sharedMesh;
        Vector3 a = mesh.bindposes[arm].inverse.GetColumn(3), b = mesh.bindposes[fore].inverse.GetColumn(3);
        var w = mesh.boneWeights;
        var v = mesh.vertices;
        var d = new List<float>();
        for (int i = 0; i < v.Length; i++)
            if (w[i].boneIndex0 == arm && w[i].weight0 > 0.5f)
                d.Add(Vector3.Cross(b - a, v[i] - a).magnitude / (b - a).magnitude);
        d.Sort();
        return d.Count > 0 ? d[d.Count / 2] : 0f;
    }

    static Mesh SaveOrUpdate(Mesh made, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(made, path);
            return made;
        }
        EditorUtility.CopySerialized(made, existing);
        existing.name = Path.GetFileNameWithoutExtension(path);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}
