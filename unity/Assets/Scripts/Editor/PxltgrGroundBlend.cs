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
/// whose per-vertex alpha is painted from noise (organic dirt blobs + an optional road), rendered by the OPAQUE
/// <c>Unwritten/GroundBlend</c> shader which <c>lerp</c>s grass↔dirt by that alpha. Result: dirt patches and the
/// road feather into the grass with NO hard tile seams — and, being opaque, no transparency overdraw
/// (see PERFORMANCE.md; one mesh + one draw beats hundreds of feathered transparent tiles).
///
/// Drop-in shaped like <see cref="PxltgrGround.LayPlot"/> (same surface at y=0, same flat floor collider +
/// <see cref="Pathfinder3DConfig"/> via <see cref="PxltgrGround.AddWalkableFloor"/>), so scatter/mobs/pathfinding
/// all work unchanged. Textures are pulled from the pack's own <c>ground_grass</c> / <c>ground_soil</c> prefab
/// materials, so it tracks <c>Fix Pxltgr Materials</c> with no extra wiring.
/// </summary>
public static class PxltgrGroundBlend
{
    const string PropsDir = "Assets/Prefabs/Props";
    const string MeshDir  = "Assets/Meshes/Generated";
    const string MatDir   = "Assets/Materials/Generated";

    /// <summary>
    /// Lay a single blended grass→dirt ground mesh ~<paramref name="sizeX"/>×<paramref name="sizeZ"/> world units
    /// centred on (<paramref name="center"/>.x, <paramref name="center"/>.z), surface at y=0, under a new root in
    /// <paramref name="scene"/> on <paramref name="groundLayer"/>. Returns the ground root, or <c>null</c> if the
    /// <c>ground_grass</c> prefab or the <c>Unwritten/GroundBlend</c> shader is missing (caller shows a dialog).
    /// </summary>
    /// <param name="dirtAmount">0 = nearly all grass, 1 = mostly dirt. ~0.3 gives occasional organic patches.</param>
    /// <param name="roadHalf">Half-width of an optional central dirt road strip along X (0 = no road).</param>
    /// <param name="cell">Vertex spacing in world units — smaller = smoother patch edges, more verts.</param>
    public static GameObject LayPlot(Scene scene, string rootName, Vector3 center, float sizeX, float sizeZ,
                                     int groundLayer, int seed = 73, float roadHalf = 0f,
                                     float dirtAmount = 0.3f, float cell = 0.8f)
    {
        var grassTex = GroundBaseMap("ground_grass");
        if (grassTex == null) return null;
        var dirtTex = GroundBaseMap("ground_soil") ?? GroundBaseMap("ground_road");

        var shader = Shader.Find("Unwritten/GroundBlend");
        if (shader == null)
        { Debug.LogError("[unwritten] Shader 'Unwritten/GroundBlend' not found (Assets/Shaders/GroundBlend.shader)."); return null; }

        // ── Build the subdivided plane with per-vertex dirt alpha ──────────────────────────────────────
        int nx = Mathf.Clamp(Mathf.CeilToInt(sizeX / Mathf.Max(0.1f, cell)), 2, 240);
        int nz = Mathf.Clamp(Mathf.CeilToInt(sizeZ / Mathf.Max(0.1f, cell)), 2, 240);
        int vcount = (nx + 1) * (nz + 1);

        // Per-seed noise offsets so each plot gets a different patch layout (Mathf.PerlinNoise has no seed arg).
        var rng = new System.Random(seed);
        float ox1 = (float)rng.NextDouble() * 1000f, oz1 = (float)rng.NextDouble() * 1000f;
        float ox2 = (float)rng.NextDouble() * 1000f, oz2 = (float)rng.NextDouble() * 1000f;
        float thresh = Mathf.Lerp(0.78f, 0.40f, Mathf.Clamp01(dirtAmount));   // more dirt → lower threshold

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

                // Two-octave value noise → organic blobs; smoothstep around the threshold = soft grass↔dirt edge.
                float n = Mathf.PerlinNoise(wx * 0.06f + ox1, wz * 0.06f + oz1) * 0.65f
                        + Mathf.PerlinNoise(wx * 0.13f + ox2, wz * 0.13f + oz2) * 0.35f;
                float a = Smooth(thresh, thresh + 0.13f, n);

                if (roadHalf > 0f)   // central dirt road along X, soft shoulders
                {
                    float d = Mathf.Abs(wz - center.z);
                    a = Mathf.Max(a, 1f - Smooth(roadHalf - 0.6f, roadHalf + 0.2f, d));
                }

                verts[vi] = new Vector3(lx, 0f, lz);
                norms[vi] = Vector3.up;
                uvs[vi]   = new Vector2(wx, wz);
                cols[vi]  = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
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
        if (dirtTex != null) mat.SetTexture("_DirtMap", dirtTex);
        mat.SetFloat("_GrassTiling", 0.4f);   // ~one tile per 2.5 units → matches the old tiled texel density
        mat.SetFloat("_DirtTiling", 0.4f);
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
