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
    // THE EMBLEM PATCH: a square on the upper back, in the shared mesh space, where a wearer's emblem is drawn.
    const float EmblemSize = 0.0042f;
    const float EmblemMiddleHeight = 0.0150f;
    // How far the patch stands off the cloth, so the two never fight over which is in front.
    const float EmblemLift = 0.00004f;

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

        // ONLY WHAT IS SEEN FROM BEHIND. Looking at the plain body from straight behind in its idle pose, every
        // triangle hidden by the cape's own back panel or by an arm is cut away, with the points nothing uses any more.
        // What is left is the panel that hangs behind the back and the arms; nothing wraps forward round the sides or
        // under the arms, so an arm has no cloth to pass through.
        float arm = ArmRadius();
        IdleArms(out var leftShoulder, out var leftElbow, out var rightShoulder, out var rightElbow);
        var visible = SeenFromBehind(verts, tris, new[] { (leftShoulder, leftElbow), (rightShoulder, rightElbow) }, arm);
        var keptTris = new List<int>();
        for (int i = 0; i < tris.Count; i += 3)
            if (visible[i / 3] && !HangsForward(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]], pivotTop))
                keptTris.AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
        var remap = new Dictionary<int, int>();
        var keptVerts = new List<Vector3>();
        var keptUvs = new List<Vector2>();
        var keptNormals = new List<Vector3>();
        for (int i = 0; i < keptTris.Count; i++)
        {
            int old = keptTris[i];
            if (!remap.TryGetValue(old, out int now))
            {
                now = keptVerts.Count;
                remap[old] = now;
                keptVerts.Add(verts[old]);
                keptUvs.Add(uvs[old]);
                keptNormals.Add(normals[old]);
            }
            keptTris[i] = now;
        }
        int cut = tris.Count / 3 - keptTris.Count / 3;
        verts = keptVerts;
        uvs = keptUvs;
        normals = keptNormals;
        tris = keptTris;

        // Where each side column hangs at each row's pivot, and at the hem: a share of the cloth's half-width there.
        var side = new float[rows + 1];
        for (int k = 0; k <= rows; k++)
        {
            float z = pivotTop - k * step, half = 0f;
            foreach (var v in verts) if (Mathf.Abs(v.z - z) < step * 0.5f) half = Mathf.Max(half, Mathf.Abs(v.x));
            side[k] = Mathf.Max(half * SideColumnShare, 1e-5f);
        }
        var weightList = new List<BoneWeight>();
        for (int i = 0; i < verts.Count; i++) weightList.Add(WeightAt(verts[i], pivotTop, step, rows, side));

        // The emblem's own copy of every cloth triangle that touches the square on the upper back: the same points and
        // weights, so it bends with the cloth, lifted a hair off it, with a picture laid flat from behind across the
        // square (left to right is +X, bottom to top is height). It is drawn by the second material; a wearer with no
        // emblem shows nothing there (BackPiece.ShowEmblem).
        var emblemTris = new List<int>();
        var copyOf = new Dictionary<int, int>();
        float emblemHalf = EmblemSize * 0.5f;
        bool InSquare(Vector3 v) => Mathf.Abs(v.x) <= emblemHalf && Mathf.Abs(v.z - EmblemMiddleHeight) <= emblemHalf;
        int clothCount = verts.Count;
        for (int i = 0; i < tris.Count; i += 3)
        {
            if (!InSquare(verts[tris[i]]) && !InSquare(verts[tris[i + 1]]) && !InSquare(verts[tris[i + 2]])) continue;
            for (int k = 0; k < 3; k++)
            {
                int old = tris[i + k];
                if (!copyOf.TryGetValue(old, out int at))
                {
                    at = verts.Count;
                    copyOf[old] = at;
                    var v = verts[old];
                    verts.Add(v + Vector3.up * EmblemLift);
                    normals.Add(normals[old]);
                    uvs.Add(new Vector2(v.x / EmblemSize + 0.5f, (v.z - EmblemMiddleHeight) / EmblemSize + 0.5f));
                    weightList.Add(weightList[old]);
                }
                emblemTris.Add(at);
            }
        }
        var weights = weightList.ToArray();

        Directory.CreateDirectory(OutDir);
        var mesh = new Mesh { name = name + "_mesh" };
        mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        // The model's own normals, not worked out again: at this size a triangle's sides are about a ten-thousandth of
        // a unit, and Unity's own normal sum comes out as zero (measured), which draws the cloth black.
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(tris, 0);
        mesh.SetTriangles(emblemTris, 1);
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

        // The emblem's material: the same character shader, cut out where the picture is see-through.
        var emblemPath = OutDir + name + "_emblem.mat";
        var emblemMat = AssetDatabase.LoadAssetAtPath<Material>(emblemPath);
        if (emblemMat == null)
        {
            emblemMat = new Material(mat) { name = name + "_emblem" };
            AssetDatabase.CreateAsset(emblemMat, emblemPath);
        }
        emblemMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
        if (emblemMat.HasProperty("_MainTex")) emblemMat.SetTexture("_MainTex", Texture2D.whiteTexture);
        emblemMat.SetFloat("_AlphaClip", 1f);
        emblemMat.SetFloat("_Cutoff", 0.5f);
        emblemMat.EnableKeyword("_ALPHATEST_ON");
        EditorUtility.SetDirty(emblemMat);

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
        smr.sharedMaterials = new[] { mat, emblemMat };
        smr.bones = allBones;
        smr.rootBone = root.transform;
        smr.quality = SkinQuality.Bone4;
        var b = mesh.bounds;
        b.Expand(b.size.z);
        smr.localBounds = b;
        var piece = root.AddComponent<BackPiece>();
        piece.chain = chain;
        piece.tips = tips;
        piece.rows = rows;
        piece.backward = Vector3.up;
        piece.backClearance = -frontSlack;
        piece.armRadius = arm;

        var prefabPath = OutDir + name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return prefabPath + ": " + cut + " triangles cut as unseen from behind or hanging forward, " + verts.Count + " points, " + tris.Count / 3 + " triangles, " + Columns + "x" + rows +
               " bones, " + emblemTris.Count / 3 + " emblem triangles, cloth from " + pivotTop.ToString("F5") + " to hem " + hem.ToString("F5") + ", side columns at " +
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

    // Which triangles can be seen from straight behind (looking along -Y): each is drawn into a grid over the cape's
    // width and height keeping the nearest depth, the arms included as solid tubes, and a triangle is seen when one of
    // its middle or its corners (pulled a little toward the middle) is the nearest thing at its grid square.
    static bool[] SeenFromBehind(List<Vector3> verts, List<int> tris, (Vector3 a, Vector3 b)[] arms, float armRadius)
    {
        const int Grid = 384;
        const float Pull = 0.15f;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var v in verts) { minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z); }
        float cell = Mathf.Max(maxX - minX, maxZ - minZ) / (Grid - 1);
        var depth = new float[Grid, Grid];
        for (int gx = 0; gx < Grid; gx++)
            for (int gz = 0; gz < Grid; gz++)
            {
                depth[gx, gz] = float.MinValue;
                var at = new Vector2(minX + gx * cell, minZ + gz * cell);
                foreach (var (a, b) in arms)
                {
                    Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z), ab = b2 - a2;
                    float t = Mathf.Clamp01(Vector2.Dot(at - a2, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                    float d = (at - (a2 + ab * t)).magnitude;
                    if (d < armRadius)
                        depth[gx, gz] = Mathf.Max(depth[gx, gz], Mathf.Lerp(a.y, b.y, t) + Mathf.Sqrt(armRadius * armRadius - d * d));
                }
            }
        int count = tris.Count / 3;
        for (int i = 0; i < count; i++)
        {
            Vector3 p0 = verts[tris[3 * i]], p1 = verts[tris[3 * i + 1]], p2 = verts[tris[3 * i + 2]];
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - minX) / cell));
            int x1 = Mathf.Min(Grid - 1, Mathf.CeilToInt((Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) - minX) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z)) - minZ) / cell));
            int z1 = Mathf.Min(Grid - 1, Mathf.CeilToInt((Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z)) - minZ) / cell));
            for (int gx = x0; gx <= x1; gx++)
                for (int gz = z0; gz <= z1; gz++)
                    if (DepthAt(p0, p1, p2, minX + gx * cell, minZ + gz * cell, out float y) && y > depth[gx, gz])
                        depth[gx, gz] = y;
        }
        var seen = new bool[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 p0 = verts[tris[3 * i]], p1 = verts[tris[3 * i + 1]], p2 = verts[tris[3 * i + 2]];
            Vector3 mid = (p0 + p1 + p2) / 3f;
            foreach (var q in new[] { mid, Vector3.Lerp(p0, mid, Pull), Vector3.Lerp(p1, mid, Pull), Vector3.Lerp(p2, mid, Pull) })
            {
                int gx = Mathf.Clamp(Mathf.RoundToInt((q.x - minX) / cell), 0, Grid - 1);
                int gz = Mathf.Clamp(Mathf.RoundToInt((q.z - minZ) / cell), 0, Grid - 1);
                if (q.y >= depth[gx, gz] - cell) { seen[i] = true; break; }
            }
        }
        return seen;
    }

    // A strand of the hem that curls forward round the legs: below the collar, its middle is in front of the body's
    // middle (in front of depth 0; +Y is the back). The back view still catches it under the hem, so it is cut by depth.
    static bool HangsForward(Vector3 p0, Vector3 p1, Vector3 p2, float collarBottom)
    {
        Vector3 mid = (p0 + p1 + p2) / 3f;
        return mid.z < collarBottom && mid.y < 0f;
    }

    // The depth of triangle p0-p1-p2 straight behind the point (x, z), when that point lies inside it seen from behind.
    static bool DepthAt(Vector3 p0, Vector3 p1, Vector3 p2, float x, float z, out float y)
    {
        y = 0f;
        float d = (p1.z - p2.z) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.z - p2.z);
        if (Mathf.Abs(d) < 1e-14f) return false;
        float a = ((p1.z - p2.z) * (x - p2.x) + (p2.x - p1.x) * (z - p2.z)) / d;
        float b = ((p2.z - p0.z) * (x - p2.x) + (p0.x - p2.x) * (z - p2.z)) / d;
        float c = 1f - a - b;
        if (a < 0f || b < 0f || c < 0f) return false;
        y = a * p0.y + b * p1.y + c * p2.y;
        return true;
    }

    // The plain body's shoulder joints and elbows in its idle pose, in the space a back piece is worn in (the upper-back
    // bone at its resting offset), so a cape can be shaped round the arms as they really hang.
    static void IdleArms(out Vector3 leftShoulder, out Vector3 leftElbow, out Vector3 rightShoulder, out Vector3 rightElbow)
    {
        var stage = new PreviewRenderUtility();
        try
        {
            var go = stage.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>(PlainBody));
            var anim = go.GetComponentInChildren<Animator>();
            var controller = CharacterSpec.Active.animatorController;
            AnimationClip idle = null;
            foreach (var c in controller.animationClips)
                if (idle == null && c.name.ToLowerInvariant().Contains("idle")) idle = c;
            anim.runtimeAnimatorController = controller;
            idle.SampleAnimation(anim.gameObject, 0f);
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var bones = smr.bones;
            int back = Find(bones, GarmentSocket.BackBone);
            var bind = smr.sharedMesh.bindposes[back];
            var worn = new GameObject("worn").transform;
            worn.SetParent(bones[back], false);
            worn.localPosition = bind.GetColumn(3);
            worn.localRotation = bind.rotation;
            worn.localScale = bind.lossyScale;
            Vector3 At(string bone) => worn.InverseTransformPoint(bones[Find(bones, bone)].position);
            leftShoulder = At("LeftArm");
            leftElbow = At("LeftForeArm");
            rightShoulder = At("RightArm");
            rightElbow = At("RightForeArm");
        }
        finally { stage.Cleanup(); }
    }

    static int Find(Transform[] bones, string name)
    {
        for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name.EndsWith(name)) return i;
        return -1;
    }

    // How far the cloth must stay from the line between shoulder joint and elbow, in the shared mesh space: the
    // furthest point of the plain body's upper-arm skin from that line, on either arm. Every body shares this skeleton.
    static float ArmRadius()
    {
        var body = AssetDatabase.LoadAssetAtPath<GameObject>(PlainBody).GetComponentInChildren<SkinnedMeshRenderer>();
        var bones = body.bones;
        var mesh = body.sharedMesh;
        var w = mesh.boneWeights;
        var v = mesh.vertices;
        float furthest = 0f;
        foreach (var (upper, lower) in new[] { ("LeftArm", "LeftForeArm"), ("RightArm", "RightForeArm") })
        {
            int arm = Find(bones, upper), fore = Find(bones, lower);
            Vector3 a = mesh.bindposes[arm].inverse.GetColumn(3), b = mesh.bindposes[fore].inverse.GetColumn(3);
            for (int i = 0; i < v.Length; i++)
                if (w[i].boneIndex0 == arm && w[i].weight0 > 0.5f)
                    furthest = Mathf.Max(furthest, Vector3.Cross(b - a, v[i] - a).magnitude / (b - a).magnitude);
        }
        return furthest;
    }

    static Mesh SaveOrUpdate(Mesh made, string path)
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

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}
