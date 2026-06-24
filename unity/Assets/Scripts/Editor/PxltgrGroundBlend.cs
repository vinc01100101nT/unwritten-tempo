using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Alternative ground builder to <see cref="PxltgrGround"/> that lays a plot as a <b>single blended mesh</b>
/// instead of a grid of swapped tiles — reproducing the trick the Pxltgr "Fantasy Landscape" FBX uses for its
/// grass-melts-into-soil look. Inspecting that FBX showed the ground meshes carry white RGB vertex colours with a
/// 0..1 <i>alpha ring</i>; the GPU interpolates that alpha across each triangle into a soft gradient, and an
/// alpha-blended material reveals the dirt beneath. We do the flat-field equivalent here: ONE subdivided plane
/// whose per-vertex alpha is painted from noise (organic dirt blobs), plus a per-vertex RED channel carrying the
/// feathered weight of the routed <see cref="PathRouting"/> polylines, rendered by the OPAQUE
/// <c>Unwritten/GroundBlend</c> shader which layers grass↔dirt (by alpha) then the path tile (by red). Result:
/// dirt patches and winding paths feather into the grass with NO hard tile seams — and, being opaque, no overdraw
/// (see PERFORMANCE.md; one mesh + one draw beats hundreds of feathered transparent tiles).
///
/// Produces the surface at y=0 + a flat floor collider + a <see cref="Pathfinder3DConfig"/> via
/// <see cref="PxltgrGround.AddWalkableFloor"/>, so scatter/mobs/pathfinding
/// all work unchanged. Textures are pulled from the pack's own <c>ground_grass</c> / <c>ground_soil</c> prefab
/// materials, so it tracks <c>Fix Pxltgr Materials</c> with no extra wiring.
/// </summary>
public static class PxltgrGroundBlend
{
    const string PropsDir = "Assets/Prefabs/Props";
    const string MeshDir  = "Assets/Meshes/Generated";
    const string MatDir   = "Assets/Materials/Generated";

    /// <summary>Per-vertex ground sampler: given a world XZ, return the four blend weights as a Vector4 mapped to the
    /// mesh vertex-colour channels — <c>x</c>=road (.r), <c>y</c>=stone (.g), <c>z</c>=rock (.b), <c>w</c>=dirt (.a),
    /// all 0..1. The procedural <see cref="LayPlot"/> overload feeds a noise-dirt + routed-path sampler (stone/rock
    /// left 0); the Map Painter importer feeds one that reads its painted cell grid.</summary>
    public delegate void GroundSampler(float worldX, float worldZ, out Vector4 weights);

    /// <summary>
    /// Lay a single blended grass→dirt ground mesh ~<paramref name="sizeX"/>×<paramref name="sizeZ"/> world units
    /// centred on (<paramref name="center"/>.x, <paramref name="center"/>.z), surface at y=0, under a new root in
    /// <paramref name="scene"/> on <paramref name="groundLayer"/>. Returns the ground root, or <c>null</c> if the
    /// <c>ground_grass</c> prefab or the <c>Unwritten/GroundBlend</c> shader is missing (caller shows a dialog).
    /// </summary>
    /// <param name="dirtAmount">0 = nearly all grass, 1 = mostly dirt. ~0.3 gives occasional organic patches.</param>
    /// <param name="paths">Routed path polylines (world XZ) baked into the vertex red channel as a feathered path
    /// weight. Null/empty = no paths.</param>
    /// <param name="pathWidth">Full width of the path in world units (0 = no path even if routes are supplied).</param>
    /// <param name="pathTexName">Ground prop name whose Base Map textures the path (e.g. "ground_road",
    /// "ground_soil", "ground_stones"). Falls back to the dirt texture if missing.</param>
    /// <param name="cell">Vertex spacing in world units — smaller = smoother patch edges, more verts.</param>
    public static GameObject LayPlot(Scene scene, string rootName, Vector3 center, float sizeX, float sizeZ,
                                     int groundLayer, int seed = 73,
                                     IReadOnlyList<PathRouting.Route> paths = null, float pathWidth = 0f,
                                     string pathTexName = null, float dirtAmount = 0.3f, float cell = 0.8f)
    {
        bool hasPath = paths != null && paths.Count > 0 && pathWidth > 0f;
        float pathHalf = pathWidth * 0.5f;
        float pathFeather = Mathf.Max(0.4f, pathWidth * 0.25f);

        // Per-seed noise offsets so each plot gets a different patch layout (Mathf.PerlinNoise has no seed arg).
        var rng = new System.Random(seed);
        float ox1 = (float)rng.NextDouble() * 1000f, oz1 = (float)rng.NextDouble() * 1000f;
        float ox2 = (float)rng.NextDouble() * 1000f, oz2 = (float)rng.NextDouble() * 1000f;
        float thresh = Mathf.Lerp(0.78f, 0.40f, Mathf.Clamp01(dirtAmount));   // more dirt → lower threshold

        // Procedural sampler: two-octave noise blobs for dirt + feathered distance-to-route for paths. Stone/rock
        // (vertex .g/.b) stay 0 — those layers are the painter's domain, never the procedural recipe's.
        void Sample(float wx, float wz, out Vector4 w)
        {
            float n = Mathf.PerlinNoise(wx * 0.06f + ox1, wz * 0.06f + oz1) * 0.65f
                    + Mathf.PerlinNoise(wx * 0.13f + ox2, wz * 0.13f + oz2) * 0.35f;
            float dirtA = Smooth(thresh, thresh + 0.13f, n);
            float pathW = 0f;
            if (hasPath)
            {
                float d = PathRouting.MinDistanceXZ(new Vector2(wx, wz), paths);
                pathW = 1f - Smooth(pathHalf - pathFeather, pathHalf + pathFeather, d);
            }
            w = new Vector4(pathW, 0f, 0f, dirtA);   // (road, stone, rock, dirt)
        }

        return LayPlotSampled(scene, rootName, center, sizeX, sizeZ, groundLayer, Sample,
                              "ground_grass", "ground_soil", pathTexName, null, null, cell);
    }

    /// <summary>
    /// As <see cref="LayPlot"/>, but the per-vertex grass/dirt/path weights come from <paramref name="sample"/>
    /// instead of noise+routes — so a caller (the Map Painter importer) can drive the blend from an authored cell
    /// grid. Same single-mesh + opaque-shader output, walkable floor and Pathfinder config. Texture names resolve to
    /// the matching <c>ground_*</c> prop's Base Map (dirt/path fall back if their prop is missing). Returns the
    /// ground root, or <c>null</c> if the grass prop or the <c>Unwritten/GroundBlend</c> shader is missing.
    /// </summary>
    public static GameObject LayPlotSampled(Scene scene, string rootName, Vector3 center, float sizeX, float sizeZ,
                                            int groundLayer, GroundSampler sample,
                                            string grassTexName = "ground_grass", string dirtTexName = "ground_soil",
                                            string pathTexName = "ground_road", string stoneTexName = null,
                                            string rockTexName = null, float cell = 0.8f)
    {
        var grassTex = GroundBaseMap(string.IsNullOrEmpty(grassTexName) ? "ground_grass" : grassTexName);
        if (grassTex == null) return null;
        var dirtTex = (!string.IsNullOrEmpty(dirtTexName) ? GroundBaseMap(dirtTexName) : null)
                      ?? GroundBaseMap("ground_soil") ?? GroundBaseMap("ground_road");
        var pathTex  = (!string.IsNullOrEmpty(pathTexName)  ? GroundBaseMap(pathTexName)  : null) ?? dirtTex;
        var stoneTex = (!string.IsNullOrEmpty(stoneTexName) ? GroundBaseMap(stoneTexName) : null);
        var rockTex  = (!string.IsNullOrEmpty(rockTexName)  ? GroundBaseMap(rockTexName)  : null);

        var shader = Shader.Find("Unwritten/GroundBlend");
        if (shader == null)
        { Debug.LogError("[unwritten] Shader 'Unwritten/GroundBlend' not found (Assets/Shaders/GroundBlend.shader)."); return null; }

        // ── Build the subdivided plane, painting each vertex's path/dirt weight from the sampler ──────────
        int nx = Mathf.Clamp(Mathf.CeilToInt(sizeX / Mathf.Max(0.1f, cell)), 2, 240);
        int nz = Mathf.Clamp(Mathf.CeilToInt(sizeZ / Mathf.Max(0.1f, cell)), 2, 240);
        int vcount = (nx + 1) * (nz + 1);

        var verts = new Vector3[vcount];
        var norms = new Vector3[vcount];
        var uvs   = new Vector2[vcount];
        var cols  = new Color[vcount];
        float halfX = sizeX * 0.5f, halfZ = sizeZ * 0.5f;

        int vi = 0;
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float lx = -halfX + sizeX * (i / (float)nx);
                float lz = -halfZ + sizeZ * (j / (float)nz);
                float wx = center.x + lx, wz = center.z + lz;

                sample(wx, wz, out Vector4 w);

                verts[vi] = new Vector3(lx, 0f, lz);
                norms[vi] = Vector3.up;
                uvs[vi]   = new Vector2(wx, wz);
                cols[vi]  = new Color(Mathf.Clamp01(w.x), Mathf.Clamp01(w.y),       // .r=road, .g=stone
                                      Mathf.Clamp01(w.z), Mathf.Clamp01(w.w));      // .b=rock, .a=dirt
                vi++;
            }

        int row = nx + 1;
        var tris = new int[nx * nz * 6];
        int ti = 0;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int v0 = j * row + i, v1 = v0 + 1, v2 = v0 + row, v3 = v2 + 1;
                tris[ti++] = v0; tris[ti++] = v2; tris[ti++] = v1;   // wound so the face normal points +Y (up)
                tris[ti++] = v1; tris[ti++] = v2; tris[ti++] = v3;
            }

        var mesh = new Mesh { name = Sanitize(rootName) + "_ground" };
        mesh.indexFormat = vcount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = verts;
        mesh.normals  = norms;
        mesh.uv       = uvs;
        mesh.colors   = cols;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        // ── Persist the mesh + material as assets (so the saved scene keeps a stable reference) ──────────
        EnsureFolder(MeshDir);
        EnsureFolder(MatDir);
        string meshPath = $"{MeshDir}/{Sanitize(rootName)}_ground.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        string matPath = $"{MatDir}/{Sanitize(rootName)}_blend.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
        else mat.shader = shader;
        mat.SetTexture("_GrassMap", grassTex);
        if (dirtTex  != null) mat.SetTexture("_DirtMap",  dirtTex);
        if (pathTex  != null) mat.SetTexture("_PathMap",  pathTex);
        if (stoneTex != null) mat.SetTexture("_StoneMap", stoneTex);
        if (rockTex  != null) mat.SetTexture("_RockMap",  rockTex);
        mat.SetFloat("_GrassTiling", 0.4f);   // ~one tile per 2.5 units → matches the old tiled texel density
        mat.SetFloat("_DirtTiling", 0.4f);
        mat.SetFloat("_PathTiling", 0.4f);
        mat.SetFloat("_StoneTiling", 0.4f);
        mat.SetFloat("_RockTiling", 0.4f);
        EditorUtility.SetDirty(mat);

        // ── Scene objects ───────────────────────────────────────────────────────────────────────────────
        var root = new GameObject(rootName);
        SceneManager.MoveGameObjectToScene(root, scene);

        var vis = new GameObject("Ground_Visual");
        vis.transform.SetParent(root.transform, false);
        vis.transform.position = new Vector3(center.x, 0f, center.z);   // verts are local-centred; offset to plot
        vis.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = vis.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;   // flat ground casts nothing useful — save the pass
        mr.receiveShadows = true;

        WorldBuildUtil.SetLayerRecursive(root, groundLayer);
        PxltgrGround.AddWalkableFloor(scene, root, center, sizeX, sizeZ, groundLayer);

        AssetDatabase.SaveAssets();
        return root;
    }

    // smoothstep(a,b,x) — soft 0→1 ramp, the source of the feathered grass↔dirt edge.
    static float Smooth(float a, float b, float x)
    {
        x = Mathf.Clamp01((x - a) / Mathf.Max(1e-5f, b - a));
        return x * x * (3f - 2f * x);
    }

    // Pull a ground prop's Base Map so the blend uses the SAME textures the tiled ground does (tracks Fix Materials).
    static Texture GroundBaseMap(string prefabName)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{prefabName}.prefab");
        if (p == null) return null;
        var r = p.GetComponentInChildren<MeshRenderer>();
        var m = r != null ? r.sharedMaterial : null;
        if (m == null) return null;
        return m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap") : m.mainTexture;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
