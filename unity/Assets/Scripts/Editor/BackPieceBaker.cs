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
    // Placed 2026-10-16 from the user's marked screenshot: from the lower back down to just above the hem, almost the
    // cape's full width there.
    const float EmblemSize = 0.0085f;
    const float EmblemMiddleHeight = 0.0088f;
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
            if (visible[i / 3] && !HangsForward(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]], pivotTop)
                && !FacesInward(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]]))
                keptTris.AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
        // ONE SHEET OF CLOTH, WITH ITS HOLES FILLED. The model is two sheets a hair apart (measured 2026-10-16: 235 and
        // 252 triangles, 0.0007 apart in depth); the outer one has small holes, and the inner one showing through them
        // was the pale spots on the cape in every colour. Only the outer sheet is kept where they overlap, and every
        // small hole left in it is closed.
        int layered = keptTris.Count / 3;
        keptTris = OuterLayerOnly(verts, keptTris);
        layered -= keptTris.Count / 3;
        int filled = FillHoles(verts, uvs, normals, keptTris);
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

        // THE WEARER'S COLOURS. The picture the cape is drawn with carries no colour of its own (see CapeTint in
        // CharacterLitInput.hlsl): how light each texel is, whether it is trim, and how far down the cape it sits.
        var tintPath = OutDir + name + "_tint.png";
        BakeTint(File.ReadAllBytes(texPath), verts, uvs, tris, topHeight, hem, tintPath, out Color clothColour, out Color trimColour);
        AssetDatabase.ImportAsset(tintPath);
        var tintImporter = (TextureImporter)AssetImporter.GetAtPath(tintPath);
        tintImporter.textureType = TextureImporterType.Default;
        tintImporter.sRGBTexture = false;
        tintImporter.alphaSource = TextureImporterAlphaSource.FromInput;
        tintImporter.maxTextureSize = 1024;
        tintImporter.mipmapEnabled = true;
        tintImporter.textureCompression = TextureImporterCompression.Compressed;
        tintImporter.filterMode = FilterMode.Bilinear;
        tintImporter.SaveAndReimport();
        var tint = AssetDatabase.LoadAssetAtPath<Texture2D>(tintPath);

        var matPath = OutDir + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterial)) { name = name };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetTexture("_BaseMap", tint);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tint);
        mat.EnableKeyword("_CAPE_TINT");
        mat.SetColor("_CapeCloth", clothColour);
        mat.SetColor("_CapeClothHem", clothColour);
        mat.SetColor("_CapeTrim", trimColour);
        mat.SetFloat("_CapeGradient", 0f);
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
        emblemMat.DisableKeyword("_CAPE_TINT");
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
        piece.backward = Vector3.up;
        piece.backClearance = -frontSlack;
        piece.armRadius = arm;
        piece.clothColour = clothColour;
        piece.trimColour = trimColour;

        var prefabPath = OutDir + name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return prefabPath + ": " + cut + " triangles cut as unseen from behind, hanging forward or facing inward (" + layered +
               " of them the inner sheet), " + filled + " holes filled, " + verts.Count + " points, " + tris.Count / 3 + " triangles, " + Columns + "x" + rows +
               " bones, " + emblemTris.Count / 3 + " emblem triangles, cloth from " + pivotTop.ToString("F5") + " to hem " + hem.ToString("F5") + ", side columns at " +
               side[0].ToString("F5") + ".." + side[rows].ToString("F5") + ", arm radius " + piece.armRadius.ToString("F5");
    }

    // A texel is TRIM when its green is at least this share of its red (the gold: 138 of 171), CLOTH otherwise (the red:
    // 12 of 135) — measured on the red cape's own picture, 2026-10-16.
    const float TrimGreenShare = 0.45f;
    // How many texels the heights are spread past each island's edge, so blending never reads an empty texel.
    const int HeightSpread = 4;
    // A cloth colour covering more than this share of the cloth exactly is a flat painted-over patch, not shading: the
    // red cape's picture has 150,048 of its 881,472 red texels at exactly 144,13,11 (measured 2026-10-16), painted over
    // dark marks, and they showed as light spots under every chosen colour. Such texels take the shading around them.
    const float FlatPatchShare = 0.05f;
    // How far, in texels, a flat patch looks for the shading around it; then four times as far where that finds none.
    const int FlatPatchReach = 12;
    // The trim's highlights are squeezed to this share of how far they rise above its middle brightness, so a light
    // trim colour never runs past white: the brightest trim texel was 1.55 times the middle (measured 2026-10-16).
    const float TrimHighlightSqueeze = 0.3f;

    // The cape's colour picture turned into the one CapeTint reads, written to outPath: red = the texel's brightness
    // over twice its kind's middle brightness, green = trim or not, alpha = how far down the cape (0 at the top, 1 at
    // the hem), drawn from the cloth triangles' own heights. The cape's own two colours (each kind's middle colour) come
    // back, so a wearer who chose nothing sees the cape as it was made.
    static void BakeTint(byte[] png, List<Vector3> verts, List<Vector2> uvs, List<int> tris, float top, float hem,
                         string outPath, out Color clothColour, out Color trimColour)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        source.LoadImage(png);
        int w = source.width, h = source.height;
        var px = source.GetPixels32();
        Object.DestroyImmediate(source);

        var isTrim = new bool[px.Length];
        var bright = new float[px.Length];
        var clothV = new List<float>();
        var trimV = new List<float>();
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            isTrim[i] = c.g >= c.r * TrimGreenShare && c.r > 60;
            bright[i] = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
            if (c.r + c.g + c.b <= 30) continue;
            if (isTrim[i]) trimV.Add(bright[i]);
            else clothV.Add(bright[i]);
        }
        float clothMid = Middle(clothV), trimMid = Middle(trimV);
        clothColour = MiddleColour(px, isTrim, false);
        trimColour = MiddleColour(px, isTrim, true);

        // How far down the cape each texel is, from the triangles laid out in the picture.
        var down = new float[px.Length];
        var known = new bool[px.Length];
        float span = Mathf.Max(top - hem, 1e-6f);
        for (int t = 0; t < tris.Count; t += 3)
        {
            Vector2 a = uvs[tris[t]], b = uvs[tris[t + 1]], c = uvs[tris[t + 2]];
            float da = (top - verts[tris[t]].z) / span, db = (top - verts[tris[t + 1]].z) / span, dc = (top - verts[tris[t + 2]].z) / span;
            Vector2 pa = new Vector2(a.x * w, a.y * h), pb = new Vector2(b.x * w, b.y * h), pc = new Vector2(c.x * w, c.y * h);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))));
            int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y))));
            int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y))));
            float area = (pb.y - pc.y) * (pa.x - pc.x) + (pc.x - pb.x) * (pa.y - pc.y);
            if (Mathf.Abs(area) < 1e-8f) continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float qx = x + 0.5f, qy = y + 0.5f;
                    float l0 = ((pb.y - pc.y) * (qx - pc.x) + (pc.x - pb.x) * (qy - pc.y)) / area;
                    float l1 = ((pc.y - pa.y) * (qx - pc.x) + (pa.x - pc.x) * (qy - pc.y)) / area;
                    float l2 = 1f - l0 - l1;
                    if (l0 < -0.01f || l1 < -0.01f || l2 < -0.01f) continue;
                    int i = y * w + x;
                    down[i] = Mathf.Clamp01(l0 * da + l1 * db + l2 * dc);
                    known[i] = true;
                }
        }
        for (int pass = 0; pass < HeightSpread; pass++)
        {
            var grow = (bool[])known.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (known[i]) continue;
                    for (int k = 0; k < 4 && !grow[i]; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || !known[ny * w + nx]) continue;
                        down[i] = down[ny * w + nx];
                        grow[i] = true;
                    }
                }
            known = grow;
        }

        // How light each texel is against its kind's middle brightness.
        var level = new float[px.Length];
        var counted = new bool[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            float mid = isTrim[i] ? trimMid : clothMid;
            level[i] = bright[i] / Mathf.Max(mid, 1e-3f);
            if (isTrim[i] && level[i] > 1f) level[i] = 1f + (level[i] - 1f) * TrimHighlightSqueeze;
            counted[i] = !isTrim[i] && px[i].r + px[i].g + px[i].b > 30;
        }

        // A FLAT PAINTED-OVER PATCH takes the shading of the cloth around it.
        var flat = FlatPatch(px, isTrim);
        for (int i = 0; i < px.Length; i++) if (flat[i]) counted[i] = false;
        var near = AroundAverage(level, counted, w, h, FlatPatchReach);
        var far = AroundAverage(level, counted, w, h, FlatPatchReach * 4);
        for (int i = 0; i < px.Length; i++)
            if (flat[i]) level[i] = !float.IsNaN(near[i]) ? near[i] : !float.IsNaN(far[i]) ? far[i] : 1f;

        var outPx = new Color32[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            float shade = Mathf.Clamp01(level[i] * 0.5f);
            outPx[i] = new Color32((byte)Mathf.RoundToInt(shade * 255f), isTrim[i] ? (byte)255 : (byte)0, 0,
                                   (byte)Mathf.RoundToInt(down[i] * 255f));
        }
        var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.SetPixels32(outPx);
        File.WriteAllBytes(outPath, result.EncodeToPNG());
        Object.DestroyImmediate(result);
    }

    // The texels of the one exact cloth colour that covers more than FlatPatchShare of the cloth — none when no colour does.
    static bool[] FlatPatch(Color32[] px, bool[] isTrim)
    {
        var count = new Dictionary<int, int>();
        int cloth = 0;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (isTrim[i] || c.r + c.g + c.b <= 30) continue;
            cloth++;
            int key = (c.r << 16) | (c.g << 8) | c.b;
            count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1;
        }
        int best = -1, bestCount = 0;
        foreach (var kv in count) if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
        var flat = new bool[px.Length];
        if (cloth == 0 || bestCount < cloth * FlatPatchShare) return flat;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            flat[i] = !isTrim[i] && ((c.r << 16) | (c.g << 8) | c.b) == best;
        }
        return flat;
    }

    // Each texel's average of the counted texels within reach (a square), or NaN where there are none: two passes of
    // running sums, so the cost does not grow with the reach.
    static float[] AroundAverage(float[] value, bool[] counted, int w, int h, int reach)
    {
        var sum = new float[w * h];
        var num = new float[w * h];
        var rowSum = new float[w + 1];
        var rowNum = new float[w + 1];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                rowSum[x + 1] = rowSum[x] + (counted[i] ? value[i] : 0f);
                rowNum[x + 1] = rowNum[x] + (counted[i] ? 1f : 0f);
            }
            for (int x = 0; x < w; x++)
            {
                int a = Mathf.Max(0, x - reach), b = Mathf.Min(w, x + reach + 1);
                sum[y * w + x] = rowSum[b] - rowSum[a];
                num[y * w + x] = rowNum[b] - rowNum[a];
            }
        }
        var colSum = new float[h + 1];
        var colNum = new float[h + 1];
        var result = new float[w * h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                colSum[y + 1] = colSum[y] + sum[y * w + x];
                colNum[y + 1] = colNum[y] + num[y * w + x];
            }
            for (int y = 0; y < h; y++)
            {
                int a = Mathf.Max(0, y - reach), b = Mathf.Min(h, y + reach + 1);
                float n = colNum[b] - colNum[a];
                result[y * w + x] = n > 0f ? (colSum[b] - colSum[a]) / n : float.NaN;
            }
        }
        return result;
    }

    static float Middle(List<float> values)
    {
        if (values.Count == 0) return 1f;
        values.Sort();
        return values[values.Count / 2];
    }

    // The middle colour of one kind of texel: each channel's middle value, so a few odd texels do not pull it.
    static Color MiddleColour(Color32[] px, bool[] isTrim, bool trim)
    {
        var r = new List<float>();
        var g = new List<float>();
        var b = new List<float>();
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (isTrim[i] != trim || c.r + c.g + c.b <= 30) continue;
            r.Add(c.r / 255f);
            g.Add(c.g / 255f);
            b.Add(c.b / 255f);
        }
        return new Color(Middle(r), Middle(g), Middle(b), 1f);
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

    // A triangle of the cloth's INSIDE: it faces toward the cape's middle line (and the wearer) rather than away from
    // it. Both sides of the cloth are drawn and the lighting does not turn round for the far side, so such a triangle
    // poking out a hair in front of the outer cloth was lit as if it faced the wearer's front — the pale marks on the back
    // and the ragged pale bits at the hem (measured 2026-10-16: triangles 0.0003 to 0.0044 in front of the outer sheet,
    // 51 of the 255 kept facing inward).
    static bool FacesInward(Vector3 p0, Vector3 p1, Vector3 p2)
    {
        Vector3 facing = Vector3.Cross(p1 - p0, p2 - p0);
        Vector3 mid = (p0 + p1 + p2) / 3f;
        return Vector3.Dot(facing, new Vector3(mid.x, mid.y, 0f)) < 0f;
    }

    // The cloth split into its separate sheets (triangles that share a corner position are one sheet); every triangle of
    // a sheet is dropped where another sheet lies further back (deeper +Y) at the same place seen from behind — so the
    // outer sheet is the one kept wherever two overlap, and a sheet stays wherever it is the only one.
    static List<int> OuterLayerOnly(List<Vector3> verts, List<int> tris)
    {
        int n = tris.Count / 3;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        var firstAt = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < n; i++)
            for (int k = 0; k < 3; k++)
            {
                var key = Weld(verts[tris[3 * i + k]]);
                if (firstAt.TryGetValue(key, out int other)) parent[Root(i)] = Root(other);
                else firstAt[key] = i;
            }
        var sheetOf = new int[n];
        var sheets = new Dictionary<int, int>();
        for (int i = 0; i < n; i++)
        {
            int r = Root(i);
            if (!sheets.TryGetValue(r, out int sh)) { sh = sheets.Count; sheets[r] = sh; }
            sheetOf[i] = sh;
        }

        const int Grid = 256;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (int t in tris) { var v = verts[t]; minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z); }
        float cell = Mathf.Max(maxX - minX, maxZ - minZ) / (Grid - 1);
        var deepest = new Dictionary<(int sheet, int gx, int gz), float>();
        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = verts[tris[3 * i]], p1 = verts[tris[3 * i + 1]], p2 = verts[tris[3 * i + 2]];
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - minX) / cell));
            int x1 = Mathf.Min(Grid - 1, Mathf.CeilToInt((Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) - minX) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z)) - minZ) / cell));
            int z1 = Mathf.Min(Grid - 1, Mathf.CeilToInt((Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z)) - minZ) / cell));
            for (int gx = x0; gx <= x1; gx++)
                for (int gz = z0; gz <= z1; gz++)
                    if (DepthAt(p0, p1, p2, minX + gx * cell, minZ + gz * cell, out float y))
                    {
                        var key = (sheetOf[i], gx, gz);
                        deepest[key] = deepest.TryGetValue(key, out float d) ? Mathf.Max(d, y) : y;
                    }
        }
        int sheetCount = sheets.Count;
        var kept = new List<int>();
        for (int i = 0; i < n; i++)
        {
            Vector3 mid = (verts[tris[3 * i]] + verts[tris[3 * i + 1]] + verts[tris[3 * i + 2]]) / 3f;
            int gx = Mathf.Clamp(Mathf.RoundToInt((mid.x - minX) / cell), 0, Grid - 1);
            int gz = Mathf.Clamp(Mathf.RoundToInt((mid.z - minZ) / cell), 0, Grid - 1);
            bool coveredFromBehind = false;
            for (int sh = 0; sh < sheetCount && !coveredFromBehind; sh++)
                if (sh != sheetOf[i] && deepest.TryGetValue((sh, gx, gz), out float other) && other > mid.y) coveredFromBehind = true;
            if (!coveredFromBehind) kept.AddRange(new[] { tris[3 * i], tris[3 * i + 1], tris[3 * i + 2] });
        }
        return kept;
    }

    // A hole with more edges than this is not a hole but the cloth's own outer edge (or a gap too big to close flat).
    const int MaxHoleEdges = 48;

    static Vector3Int Weld(Vector3 v) =>
        new Vector3Int(Mathf.RoundToInt(v.x * 1e6f), Mathf.RoundToInt(v.y * 1e6f), Mathf.RoundToInt(v.z * 1e6f));

    // Close every small hole in the cloth: each loop of edges that only one triangle uses, other than the longest (the
    // cloth's outer edge), gets a new point in its middle and a fan of triangles to it, wound to face the way the loop's
    // own points face. Returns how many holes were closed.
    static int FillHoles(List<Vector3> verts, List<Vector2> uvs, List<Vector3> normals, List<int> tris)
    {
        var groupOf = new Dictionary<int, int>();
        var groupKey = new Dictionary<Vector3Int, int>();
        var anyVertex = new List<int>();
        int Group(int v)
        {
            if (groupOf.TryGetValue(v, out int g)) return g;
            var key = Weld(verts[v]);
            if (!groupKey.TryGetValue(key, out g)) { g = anyVertex.Count; groupKey[key] = g; anyVertex.Add(v); }
            groupOf[v] = g;
            return g;
        }
        var uses = new Dictionary<(int, int), int>();
        for (int t = 0; t < tris.Count; t += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = Group(tris[t + k]), b = Group(tris[t + (k + 1) % 3]);
                var e = a < b ? (a, b) : (b, a);
                uses[e] = uses.TryGetValue(e, out int u) ? u + 1 : 1;
            }
        var next = new Dictionary<int, List<int>>();
        foreach (var kv in uses)
        {
            if (kv.Value != 1) continue;
            var (a, b) = kv.Key;
            if (!next.TryGetValue(a, out var la)) next[a] = la = new List<int>();
            if (!next.TryGetValue(b, out var lb)) next[b] = lb = new List<int>();
            la.Add(b);
            lb.Add(a);
        }
        var loops = new List<List<int>>();
        var done = new HashSet<int>();
        foreach (int start in next.Keys)
        {
            if (done.Contains(start) || next[start].Count != 2) continue;
            var loop = new List<int> { start };
            done.Add(start);
            int prev = start, at = next[start][0];
            bool closed = false;
            while (loop.Count <= MaxHoleEdges * 4)
            {
                if (at == start) { closed = true; break; }
                if (done.Contains(at) || next[at].Count != 2) break;
                loop.Add(at);
                done.Add(at);
                int go = next[at][0] == prev ? next[at][1] : next[at][0];
                prev = at;
                at = go;
            }
            if (closed) loops.Add(loop);
        }
        int longest = -1;
        for (int i = 0; i < loops.Count; i++) if (longest < 0 || loops[i].Count > loops[longest].Count) longest = i;
        int filled = 0;
        for (int i = 0; i < loops.Count; i++)
        {
            var loop = loops[i];
            if (i == longest || loop.Count < 3 || loop.Count > MaxHoleEdges) continue;
            Vector3 mid = Vector3.zero, facing = Vector3.zero;
            Vector2 uv = Vector2.zero;
            foreach (int g in loop) { int v = anyVertex[g]; mid += verts[v]; facing += normals[v]; uv += uvs[v]; }
            mid /= loop.Count;
            uv /= loop.Count;
            int centre = verts.Count;
            verts.Add(mid);
            uvs.Add(uv);
            normals.Add(facing.normalized);
            for (int k = 0; k < loop.Count; k++)
            {
                int a = anyVertex[loop[k]], b = anyVertex[loop[(k + 1) % loop.Count]];
                Vector3 face = Vector3.Cross(verts[b] - verts[a], mid - verts[a]);
                if (Vector3.Dot(face, facing) >= 0f) tris.AddRange(new[] { a, b, centre });
                else tris.AddRange(new[] { a, centre, b });
            }
            filled++;
        }
        return filled;
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
