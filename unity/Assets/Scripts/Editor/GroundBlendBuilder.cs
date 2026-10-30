using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Alternative ground builder to <see cref="WalkableGround"/> that lays a plot as a <b>single blended mesh</b>
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
/// <see cref="WalkableGround.AddWalkableFloor"/>, so scatter/mobs/pathfinding all work unchanged — or, when the biome
/// gives its ground mounds (<see cref="GroundBumps"/>), the raised surface with a <see cref="MeshCollider"/> on it
/// instead of the flat box. Textures are
/// the ground tiles, resolved by name through <see cref="GroundTiles.Load"/>.
///
/// <para><b>Elevated variant</b> (<see cref="LayPlotSampledElevated"/>): when a map paints per-cell heights, the
/// ground becomes real terrain — each painter cell is a flat top at its level's height, with TRUE vertical walls
/// (curbs / gutter sides / cliffs) between cells of differing levels and a skirt down the map border. It carries a
/// <see cref="MeshCollider"/> instead of the flat box, so the player capsule bumps cliffs + climbs curbs and the
/// height-aware pathfinder routes around drops — all off the SAME shaped surface.</para>
/// </summary>
public static class GroundBlendBuilder
{
    const string MeshDir       = "Assets/2_Generated/Meshes";
    const string MatDir        = "Assets/2_Generated/Materials";

    /// <summary>World units ONE ground-blend texture repeat spans in-game (the shader's world-XZ tiling = 1/this).
    /// The single source of truth for ground texture density: the material bake below AND the brush-palette manifest
    /// export both read it, so the map painter's preview tiles ground at the exact in-game scale.</summary>
    public const float GroundTexWorldSize = 2.5f;

    // One full curb strip spans this many world units along its edge — every curb this file lays, cell-painted or curved.
    public const float CurbWorldPerTexture = 0.5f;
    // The ground tile the curb's face and its band of stones are drawn with.
    public const string CurbTile = "CURB_blocks";

    /// <summary>Per-cell diagonal cuts painted with the map painter's 📏 line tool. <c>dir</c> names the KEPT corner
    /// in world orientation (0 none · 1 nw · 2 ne · 3 se · 4 sw; n = +Z, e = +X). The kept half sits at the cell's
    /// levels-grid notch, the cut-away half at <c>underLevel</c>; <c>keptCh</c>/<c>underCh</c> hold each half's blend
    /// channel (0 base, 1..4 overlays). Consumed by <see cref="LayPlotSampledElevated"/>.</summary>
    public class DiagonalCuts
    {
        public readonly int[,] dir, keptCh, underCh, underLevel;
        public bool any;
        public DiagonalCuts(int cols, int rows)
        { dir = new int[cols, rows]; keptCh = new int[cols, rows]; underCh = new int[cols, rows]; underLevel = new int[cols, rows]; }
    }

    /// <summary>Which half of a cut cell owns an edge (edge: 0=W 1=E 2=S 3=N): the kept corner's two adjacent
    /// edges belong to the kept half, the other two to the cut-away half.</summary>
    public static bool EdgeIsKept(int dir, int edge)
    {
        switch (dir)
        {
            case 1: return edge == 0 || edge == 3;   // nw → W + N
            case 2: return edge == 1 || edge == 3;   // ne → E + N
            case 3: return edge == 1 || edge == 2;   // se → E + S
            case 4: return edge == 0 || edge == 2;   // sw → W + S
            default: return true;
        }
    }

    // One-hot vertex colour for a blend channel: (r,g,b,a) = (road, stone, rock, dirt); base/unknown = all zero.
    static Color ChannelCol(int ch)
    {
        switch (ch)
        {
            case 1: return new Color(1, 0, 0, 0);
            case 2: return new Color(0, 1, 0, 0);
            case 3: return new Color(0, 0, 1, 0);
            case 4: return new Color(0, 0, 0, 1);
            default: return new Color(0, 0, 0, 0);
        }
    }

    /// <summary>Per-vertex ground sampler: given a world XZ, return the four blend weights as a Vector4 mapped to the
    /// mesh vertex-colour channels — <c>x</c>=road (.r), <c>y</c>=stone (.g), <c>z</c>=rock (.b), <c>w</c>=dirt (.a),
    /// all 0..1. The procedural <see cref="LayPlot"/> overload feeds a noise-dirt + routed-path sampler (stone/rock
    /// left 0); the Map Painter importer feeds one that reads its painted cell grid.</summary>
    public delegate void GroundSampler(float worldX, float worldZ, out Vector4 weights);

    /// <summary>Elevation-aware ground sampler used by the elevated builder: same as <see cref="GroundSampler"/> but
    /// also told the <paramref name="refLevel"/> — the ORIGINAL painted notch of the cell this vertex belongs to (NOT
    /// its corner-filled geometry level) — so the sampler can refuse to blend in cells painted at a DIFFERENT level (a
    /// hard texture cut at a step, soft blend within a level) instead of feathering across the cliff. The elevated
    /// builder feeds the ORIGINAL painted level; a cell that a corner-fill lifts into a higher region would instead be
    /// coloured SOLID from that higher region (see <c>RaisedRegionCol</c>) — dormant now that painted levels are exact — so a
    /// raised concave corner reads as the elevated tile with a hard edge rather than smearing the two regions.</summary>
    public delegate void GroundSamplerLeveled(float worldX, float worldZ, int refLevel, out Vector4 weights);

    /// <summary>
    /// Lay a single blended grass→dirt ground mesh ~<paramref name="sizeX"/>×<paramref name="sizeZ"/> world units
    /// centred on (<paramref name="center"/>.x, <paramref name="center"/>.z), surface at y=0, under a new root in
    /// <paramref name="scene"/> on <paramref name="groundLayer"/>. Returns the ground root, or <c>null</c> if the
    /// <c>BASE_OR_PATCH_meadow_grass</c> tile or the <c>Unwritten/GroundBlend</c> shader is missing (caller shows a dialog).
    /// </summary>
    /// <param name="dirtAmount">0 = nearly all grass, 1 = mostly dirt. ~0.3 gives occasional organic patches.</param>
    /// <param name="paths">Routed path polylines (world XZ) baked into the vertex red channel as a feathered path
    /// weight. Null/empty = no paths.</param>
    /// <param name="pathWidth">Full width of the path in world units (0 = no path even if routes are supplied).</param>
    /// <param name="pathTexName">Ground tile name that textures the path (e.g. "PATH_dirt_road",
    /// "BASE_OR_PATCH_OR_PATH_soil", "PATH_OR_SIDEWALK_dark_cobble"). Falls back to the dirt texture if missing.</param>
    /// <param name="cell">Vertex spacing in world units — smaller = smoother patch edges, more verts.</param>
    /// <param name="grassTexName">Ground tile name for the BASE surface (biome base, e.g. "BASE_OR_PATCH_meadow_grass"/"BASE_OR_PATCH_snow").</param>
    /// <param name="dirtTexName">Ground tile name for the PATCH surface blended in by the dirt mask (e.g. "BASE_OR_PATCH_OR_PATH_soil").</param>
    /// <param name="tint">Albedo multiply applied by the GroundBlend shader's <c>_Color</c> (biome hue). Null = white.</param>
    /// <param name="hardEdge">Snap the dirt + path weights to 0/1 so the grass↔dirt↔path edges are crisp, not feathered.</param>
    /// <param name="bumpHeight">How high the rolling mounds rise, world units; 0 = flat. See <see cref="GroundBumps"/>.</param>
    /// <param name="bumpWidth">How wide one hill is, world units.</param>
    /// <param name="bumpLevels">How many flat levels the ground settles into; 0 = smooth mounds.</param>
    /// <param name="bumpSteepestDegrees">The steepest slope the ground may have, in degrees.</param>
    /// <param name="cliffs">Rock cliffs lining both sides of the paths (<see cref="GroundCliffs"/>); null = none.</param>
    public static GameObject LayPlot(Scene scene, string rootName, Vector3 center, float sizeX, float sizeZ,
                                     int groundLayer, int seed = 73,
                                     IReadOnlyList<PathRouting.Route> paths = null, float pathWidth = 0f,
                                     string pathTexName = null, float dirtAmount = 0.3f, float cell = 0.8f,
                                     string grassTexName = "BASE_OR_PATCH_meadow_grass", string dirtTexName = "BASE_OR_PATCH_OR_PATH_soil",
                                     Color? tint = null, bool hardEdge = false,
                                     float bumpHeight = 0f, float bumpWidth = 10f, int bumpLevels = 0,
                                     float bumpSteepestDegrees = 5.4f, GroundCliffs.Spec cliffs = null)
    {
        return LayPlotSampled(scene, rootName, center, sizeX, sizeZ, groundLayer,
                              MakeProceduralSampler(seed, dirtAmount, paths, pathWidth, hardEdge),
                              grassTexName, dirtTexName, pathTexName, null, null, cell, tint,
                              bumpHeight, bumpWidth, seed, bumpLevels, bumpSteepestDegrees, cliffs);
    }

    /// <summary>
    /// The exact procedural ground sampler <see cref="LayPlot"/> bakes — two-octave noise blobs for dirt (vertex .a)
    /// plus a feathered distance-to-route for paths (vertex .r); stone/rock (.g/.b) stay 0, the painter's domain.
    /// Exposed so the sidecar writer (<c>MapSidecar.RasterizeGround</c>) can rasterise the SAME field into painter
    /// cells, guaranteeing a re-imported map's ground matches the generated one. Deterministic in <paramref name="seed"/>.
    /// </summary>
    public static GroundSampler MakeProceduralSampler(int seed, float dirtAmount,
        IReadOnlyList<PathRouting.Route> paths, float pathWidth, bool hardEdge)
    {
        bool hasPath = paths != null && paths.Count > 0 && pathWidth > 0f;
        float pathHalf = pathWidth * 0.5f;
        float pathFeather = Mathf.Max(0.4f, pathWidth * 0.25f);

        // Per-seed noise offsets so each plot gets a different patch layout (Mathf.PerlinNoise has no seed arg).
        var rng = new System.Random(seed);
        float ox1 = (float)rng.NextDouble() * 1000f, oz1 = (float)rng.NextDouble() * 1000f;
        float ox2 = (float)rng.NextDouble() * 1000f, oz2 = (float)rng.NextDouble() * 1000f;
        float thresh = Mathf.Lerp(0.78f, 0.40f, Mathf.Clamp01(dirtAmount));   // more dirt → lower threshold

        return (float wx, float wz, out Vector4 w) =>
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
            if (hardEdge)
            {
                dirtA = dirtA >= 0.5f ? 1f : 0f;
                pathW = pathW >= 0.5f ? 1f : 0f;
            }
            w = new Vector4(pathW, 0f, 0f, dirtA);   // (road, stone, rock, dirt)
        };
    }

    /// <summary>
    /// As <see cref="LayPlot"/>, but the per-vertex grass/dirt/path weights come from <paramref name="sample"/>
    /// instead of noise+routes — so a caller (the Map Painter importer) can drive the blend from an authored cell
    /// grid. Same single-mesh + opaque-shader output, walkable floor and Pathfinder config. Texture names resolve to
    /// the tile of that name in <see cref="GroundTiles"/> (dirt/path fall back if their tile is missing). Returns the
    /// ground root, or <c>null</c> if the grass tile or the <c>Unwritten/GroundBlend</c> shader is missing.
    /// <para><paramref name="bumpHeight"/> above 0 raises the plane into rolling mounds (<see cref="GroundBumps"/>),
    /// and the floor becomes a <see cref="MeshCollider"/> on that same shaped mesh instead of the flat box, so feet,
    /// props and pathing all stand on the mounds. <paramref name="cliffs"/> likewise raises the ground away from the
    /// roads and lines the pass with carved rock faces (<see cref="GroundCliffs"/>).</para>
    /// </summary>
    public static GameObject LayPlotSampled(Scene scene, string rootName, Vector3 center, float sizeX, float sizeZ,
                                            int groundLayer, GroundSampler sample,
                                            string grassTexName = "BASE_OR_PATCH_meadow_grass", string dirtTexName = "BASE_OR_PATCH_OR_PATH_soil",
                                            string pathTexName = "PATH_dirt_road", string stoneTexName = null,
                                            string rockTexName = null, float cell = 0.8f, Color? tint = null,
                                            float bumpHeight = 0f, float bumpWidth = 10f, int bumpSeed = 0,
                                            int bumpLevels = 0, float bumpSteepestDegrees = 5.4f,
                                            GroundCliffs.Spec cliffs = null)
    {
        var mat = MakeGroundMaterial(rootName, grassTexName, dirtTexName, pathTexName, stoneTexName, rockTexName, tint);
        if (mat == null) return null;

        // ── Build the subdivided plane, painting each vertex's path/dirt weight from the sampler ──────────
        // A shaped ground is drawn finer: its rounded level edges are only as round as the triangles drawing them.
        // Cliffs are part of this same ground, so its squares are as fine as the cliff's cut line needs.
        float finest = cliffs != null ? Mathf.Min(GroundBumps.DrawnSpacing, GroundCliffs.GridCell) : GroundBumps.DrawnSpacing;
        bool fine = bumpHeight > 0f || cliffs != null;
        if (fine) cell = Mathf.Min(cell, finest);
        // The 240-square cap never draws a shaped ground coarser than that (a 64 m field needs 256).
        int mostX = fine ? Mathf.Max(240, Mathf.CeilToInt(sizeX / finest)) : 240;
        int mostZ = fine ? Mathf.Max(240, Mathf.CeilToInt(sizeZ / finest)) : 240;
        int nx = Mathf.Clamp(Mathf.CeilToInt(sizeX / Mathf.Max(0.1f, cell)), 2, mostX);
        int nz = Mathf.Clamp(Mathf.CeilToInt(sizeZ / Mathf.Max(0.1f, cell)), 2, mostZ);
        int vcount = (nx + 1) * (nz + 1);

        var verts = new Vector3[vcount];
        var norms = new Vector3[vcount];
        var uvs   = new Vector2[vcount];
        var cols  = new Color[vcount];
        float halfX = sizeX * 0.5f, halfZ = sizeZ * 0.5f;

        bool bumpy = bumpHeight > 0f;
        bool shaped = bumpy || cliffs != null;
        float dx = sizeX / nx, dz = sizeZ / nz, originX = center.x - halfX, originZ = center.z - halfZ;
        // groundShape = the biome's hills or levels. Without cliffs that is the ground. With cliffs the pass floor stays
        // flat — so everything down there can still walk the whole pass — and the shape goes on the cliff tops instead,
        // so the cliffs' edges stand higher in some places and lower in others.
        // floorHeights = the ground before any cliff raises it; topRise = what the cliffs add on top; heights = drawn.
        float[] groundShape = null, floorHeights = null, topRise = null, heights = null;
        if (shaped)
        {
            groundShape = bumpy
                ? GroundBumps.Make(nx, nz, sizeX, sizeZ, originX, originZ, bumpSeed, bumpHeight, bumpWidth, bumpLevels,
                                   Mathf.Tan(bumpSteepestDegrees * Mathf.Deg2Rad))
                : new float[vcount];
            if (cliffs != null) { topRise = groundShape; floorHeights = new float[vcount]; }
            else floorHeights = groundShape;
            heights = (float[])floorHeights.Clone();
        }

        // The cliff-top height at every vertex: GroundCliffs shapes the one ground between it and the floor's height.
        var topHeights = cliffs != null ? new float[vcount] : null;
        int vi = 0;
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float lx = -halfX + sizeX * (i / (float)nx);
                float lz = -halfZ + sizeZ * (j / (float)nz);
                float wx = center.x + lx, wz = center.z + lz;

                sample(wx, wz, out Vector4 w);

                if (cliffs != null)
                    topHeights[vi] = floorHeights[vi] + cliffs.Standing(wx, wz) * (cliffs.height + topRise[vi]);
                verts[vi] = new Vector3(lx, shaped ? heights[vi] : 0f, lz);
                norms[vi] = Vector3.up;
                uvs[vi]   = new Vector2(wx, wz);
                cols[vi]  = new Color(Mathf.Clamp01(w.x), Mathf.Clamp01(w.y),       // .r=road, .g=stone
                                      Mathf.Clamp01(w.z), Mathf.Clamp01(w.w));      // .b=rock, .a=dirt
                vi++;
            }

        Mesh mesh;
        if (cliffs != null)
            // The cliffs are this ground, shaped: floor, foot, face, shoulder and top in one mesh (RULE ONEGROUND).
            mesh = GroundCliffs.BuildGround(cliffs, center, sizeX, sizeZ, nx, nz, floorHeights, topHeights,
                                            cliffs.height + GroundBumps.Highest(topRise), sample,
                                            Sanitize(rootName) + "_ground");
        else
        {
            int row = nx + 1;
            var tris = new int[nx * nz * 6];
            int ti = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int v0 = j * row + i, v1 = v0 + 1, v2 = v0 + row, v3 = v2 + 1;
                    // wound so the face normal points +Y (up)
                    tris[ti++] = v0; tris[ti++] = v2; tris[ti++] = v1;
                    tris[ti++] = v1; tris[ti++] = v2; tris[ti++] = v3;
                }

            mesh = new Mesh { name = Sanitize(rootName) + "_ground" };
            mesh.indexFormat = vcount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals  = norms;
            mesh.uv       = uvs;
            mesh.colors   = cols;
            mesh.triangles = tris;
            if (shaped) mesh.RecalculateNormals();   // a mound's slopes must face the sun to read as mounds
            mesh.RecalculateBounds();
        }
        PersistMesh(mesh, rootName);
        if (bumpy)
            Debug.Log($"[unwritten] {rootName}: {(cliffs != null ? "cliff tops" : "ground")} up to {GroundBumps.Highest(groundShape):0.000}u in " +
                      $"{(bumpLevels >= 2 ? bumpLevels + " levels" : "rolling mounds")}, steepest slope " +
                      $"{GroundBumps.SteepestSlope(groundShape, nx, nz, dx, dz):0.000}u per unit " +
                      $"(allowed {Mathf.Tan(bumpSteepestDegrees * Mathf.Deg2Rad):0.000}; every body walks up to {GroundElevation.MaxWalkSlopeDegrees}°).");

        // ── Scene objects ───────────────────────────────────────────────────────────────────────────────
        var root = new GameObject(rootName);
        SceneManager.MoveGameObjectToScene(root, scene);

        var vis = new GameObject("Ground_Visual");
        vis.transform.SetParent(root.transform, false);
        vis.transform.position = new Vector3(center.x, 0f, center.z);   // verts are local-centred; offset to plot
        vis.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = vis.AddComponent<MeshRenderer>();
        // A cliff map's ground is drawn with its rock layer on (uv1.x says where), and throws the cliffs' shadow
        // across the pass; flat ground casts nothing useful, so it saves the pass.
        mr.sharedMaterial = cliffs != null ? MakeCliffMaterial(mat, rootName, cliffs.faceColor) ?? mat : mat;
        mr.shadowCastingMode = cliffs != null ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.receiveShadows = true;

        WorldBuildUtil.SetLayerRecursive(root, groundLayer);
        if (cliffs != null)
            Debug.Log($"[unwritten] {rootName}: cliffs {cliffs.height:0.##}–{cliffs.height + GroundBumps.Highest(topRise):0.##}u " +
                      $"tall along the roads, pass {cliffs.passWidth:0.##}u wide, {cliffs.MeetingPoints.Count} meeting points, " +
                      $"shaped into the one ground ({mesh.vertexCount} vertices).");
        if (shaped)
        {
            // Collision IS the drawn surface, as on elevated ground — a flat box would leave feet inside the mounds.
            vis.AddComponent<MeshCollider>().sharedMesh = mesh;
            WalkableGround.EnsurePathfinderConfig(scene, groundLayer);
            Physics.SyncTransforms();
        }
        else WalkableGround.AddWalkableFloor(scene, root, center, sizeX, sizeZ, groundLayer);

        AssetDatabase.SaveAssets();
        return root;
    }

    /// <summary>
    /// As <see cref="LayPlotSampled"/>, but the ground carries real ELEVATION: <paramref name="levels"/> gives each
    /// painter cell an integer height notch (0 = base, +N = raised, −N = sunken). Each cell becomes a flat top at
    /// <c>level ×</c> <see cref="GroundElevation.LevelHeight"/>, with a TRUE vertical wall dropped on any edge whose
    /// neighbour sits lower (curb / gutter side / cliff) and a skirt around the map border. The blend look is
    /// preserved — every top and wall vertex still colours from <paramref name="sample"/>. Collision is a
    /// <see cref="MeshCollider"/> on the shaped mesh (not the flat box), so the player + height-aware pathfinder act
    /// off the same surface. Grid is <paramref name="cols"/>×<paramref name="rows"/> cells of
    /// <paramref name="cellSize"/> world units, centred on origin. Returns the ground root, or <c>null</c> on a
    /// missing grass prop / shader.
    /// </summary>
    public static GameObject LayPlotSampledElevated(Scene scene, string rootName, Vector3 center,
                                                    float sizeX, float sizeZ, int groundLayer, GroundSamplerLeveled sample,
                                                    int[,] levels, int cols, int rows, float cellSize,
                                                    string grassTexName = "BASE_OR_PATCH_meadow_grass", string dirtTexName = "BASE_OR_PATCH_OR_PATH_soil",
                                                    string pathTexName = "PATH_dirt_road", string stoneTexName = null,
                                                    string rockTexName = null, Color? tint = null, DiagonalCuts cuts = null)
    {
        var mat = MakeGroundMaterial(rootName, grassTexName, dirtTexName, pathTexName, stoneTexName, rockTexName, tint);
        if (mat == null) return null;
        // EXACT REPRODUCTION: a hand-painted map is authoritative — every cell renders at the height the author
        // painted, no more. `paintLevels` (colour reference) and `levels` (geometry) are therefore the SAME grid;
        // they're kept as two names only so the raised-cell colour path below stays a harmless no-op (a cell is
        // never "raised above its painted level" now). No auto corner-fill runs: an earlier pass raised concave-corner
        // cells to hide a background gap, but it silently FATTENED the author's shape (a 2-wide zigzag became 3-wide).
        // Concave corners are now the author's to fill (e.g. with the 📏 diagonal tool).
        var paintLevels = levels;
        var curbMat = MakeCurbMaterial();                 // null → 1-notch walls just stay in the ground-blend submesh
        var cornerMat = MakeCornerMaterial();             // single square stone dropped at every border corner (null → skip)

        // BORDER-RING stones along each curb edge: a band of the raised top itself, drawn with the curb texture — part
        // of the one ground, never a strip laid over it (RULE ONEGROUND). Width = one texture-row scaled so the stones
        // stay SQUARE (read the texture's real aspect, so new art can't stretch them); BorderRows widens it in rows.
        const int   BorderRows  = 1;
        float borderWidth = BorderRows * CurbBandWidth(curbMat);   // world width of BorderRows square rows

        // Sub-cells per painter cell → keep the grass/dirt blend as fine as the flat mesh's ~0.8u vertex grid. Must be
        // EVEN and ≥2 so a vertex lands on every cell CENTRE: a 1-cell-wide painted path only reaches full blend weight
        // where a vertex sits at its centre — sample only the cell corners (sub=1, which small cellSizes round down to)
        // and every path peaks at 0.5 and smears into its neighbours (the cellSize≈1 washed-out-paths bug).
        int sub = Mathf.Clamp(Mathf.RoundToInt(cellSize / 0.8f), 2, 8);
        sub += sub & 1;   // round up to even → a vertex always lands at each cell centre
        float lh = GroundElevation.LevelHeight;
        float ox = center.x - sizeX * 0.5f, oz = center.z - sizeZ * 0.5f;   // plot's −X/−Z corner in world

        // Border skirt drops to just under the lowest painted level so the map edge isn't a paper-thin cliff.
        int minLevel = 0; bool any = false;
        for (int cz = 0; cz < rows; cz++)
            for (int cx = 0; cx < cols; cx++) { int L = levels[cx, cz]; if (!any || L < minLevel) { minLevel = L; any = true; } }
        float skirtBottomY = minLevel * lh - 0.5f;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var cols4 = new List<Color>();
        var trisTop  = new List<int>();
        var trisWall = new List<int>();   // 1-notch curbs → own submesh + curb material (rest of the ground → trisTop)
        var trisCorner = new List<int>(); // single square stones at border corners → own submesh + corner material

        int Push(Vector3 pos, Vector3 nrm, Vector2 uv, Color col)
        { verts.Add(pos); norms.Add(nrm); uvs.Add(uv); cols4.Add(col); return verts.Count - 1; }

        Color SampleCol(float wx, float wz, int refLevel)
        {
            sample(wx, wz, refLevel, out Vector4 w);
            return new Color(Mathf.Clamp01(w.x), Mathf.Clamp01(w.y), Mathf.Clamp01(w.z), Mathf.Clamp01(w.w));
        }

        // Colour for a cell that a corner-fill lifted into a higher region (dormant now — painted levels are exact):
        // sample the tile of an orthogonal
        // neighbour PAINTED at the lifted level (an arm of the filled notch) at its cell centre, so the raised corner
        // reads as that elevated region's tile SOLID — not its own (lower) tile, and not a smear of the two. Falls back
        // to this cell's own centre if somehow no such neighbour exists.
        Color RaisedRegionCol(int cx, int cz, int liftedLevel)
        {
            int ax = cx, az = cz; bool found = false;
            void Try(int nx, int nz)
            { if (!found && nx >= 0 && nx < cols && nz >= 0 && nz < rows && paintLevels[nx, nz] == liftedLevel) { ax = nx; az = nz; found = true; } }
            Try(cx - 1, cz); Try(cx + 1, cz); Try(cx, cz - 1); Try(cx, cz + 1);
            return SampleCol(ox + (ax + 0.5f) * cellSize, oz + (az + 0.5f) * cellSize, liftedLevel);
        }

        // Emit a vertical wall quad (double-sided so it reads whichever way the camera orbits), coloured from the
        // higher cell's blend. A 1-notch INTERIOR drop is a decorative CURB: its quad goes to the curb submesh and is
        // UV'd from WORLD position (U along the edge, V = height in notches) so the stone strip flows unbroken across
        // adjacent cells and one row exactly fills each 0.2 step. Taller cliffs + the border skirt stay in the
        // ground-blend submesh with the legacy (along-edge, worldY) UV — GroundBlend samples world-XZ so it streaks.
        void AddWall(Vector3 tA, Vector3 tB, Vector3 bA, Vector3 bB, Vector3 outward, bool isCurb, int refLevel, Color? solid = null)
        {
            bool haveCurb = isCurb && curbMat != null;
            Color cA = solid ?? SampleCol(tA.x, tA.z, refLevel), cB = solid ?? SampleCol(tB.x, tB.z, refLevel);
            Vector2 uTA, uTB, uBA, uBB;
            if (haveCurb)
            {
                bool alongZ = Mathf.Abs(tA.x - tB.x) < 1e-4f;             // wall runs along Z (else along X)
                float sA = (alongZ ? tA.z : tA.x) / CurbWorldPerTexture;
                float sB = (alongZ ? tB.z : tB.x) / CurbWorldPerTexture;
                float vTop = (tA.y - bA.y) / lh;                          // notches of height → one strip row per 0.2 step
                uTA = new Vector2(sA, vTop); uTB = new Vector2(sB, vTop);
                uBA = new Vector2(sA, 0f);   uBB = new Vector2(sB, 0f);
            }
            else
            {
                float alongB = Vector3.Distance(new Vector3(tA.x, 0, tA.z), new Vector3(tB.x, 0, tB.z));
                uTA = new Vector2(0f, tA.y); uTB = new Vector2(alongB, tB.y);
                uBA = new Vector2(0f, bA.y); uBB = new Vector2(alongB, bB.y);
            }
            int itA = Push(tA, outward, uTA, cA);
            int itB = Push(tB, outward, uTB, cB);
            int ibA = Push(bA, outward, uBA, cA);
            int ibB = Push(bB, outward, uBB, cB);
            var t = haveCurb ? trisWall : trisTop;
            // Double-sided (both windings) so the face reads whichever side the camera orbits to.
            AddTriTo(t, itA, ibA, itB); AddTriTo(t, itB, ibA, ibB);
            AddTriTo(t, itA, itB, ibA); AddTriTo(t, itB, ibB, ibA);
        }
        void AddTriTo(List<int> t, int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); }

        // A band of the ground beside an edge, drawn in stone: <p0> is on the edge, <inward> the unit direction into
        // the top, <uAlong> how far along the edge (in texture repeats) a world point is. V runs across the band, one
        // row per BorderRows, so the stones read square and flow unbroken across neighbouring cells.
        Vector2 BandUV(Vector3 p, Vector3 p0, Vector3 inward, System.Func<float, float, float> uAlong)
            => new Vector2(uAlong(p.x, p.z), Vector3.Dot(new Vector3(p.x - p0.x, 0f, p.z - p0.z), inward) / borderWidth * BorderRows);

        // Keep the part of a flat convex polygon (XZ) on one side of a line: dot(p − p0, n) < d when <below>, else ≥ d.
        List<Vector3> Clip(List<Vector3> poly, Vector3 p0, Vector3 n, float d, bool below)
        {
            var output = new List<Vector3>(poly.Count + 2);
            float Side(Vector3 p) { float v = (p.x - p0.x) * n.x + (p.z - p0.z) * n.z - d; return below ? -v : v; }
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                float sa = Side(a), sb = Side(b);
                if (sa >= 0f) output.Add(a);
                if ((sa >= 0f) != (sb >= 0f)) output.Add(Vector3.Lerp(a, b, sa / (sa - sb)));
            }
            return output;
        }

        // A flat convex polygon at height <y> into <tris>, wound to face up, each point coloured and UV'd by the
        // given functions (own vertices, like every piece of this ground).
        void FlatPoly(List<Vector3> poly, float y, List<int> tris, System.Func<Vector3, Vector2> uv, System.Func<Vector3, Color> col)
        {
            if (poly.Count < 3) return;
            var at = new int[poly.Count];
            for (int i = 0; i < poly.Count; i++)
            {
                var p = new Vector3(poly[i].x, y, poly[i].z);
                at[i] = Push(p, Vector3.up, uv(p), col(p));
            }
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                int a = at[0], b = at[i], c = at[i + 1];
                if (Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).y < 0f) { int t = b; b = c; c = t; }
                AddTriTo(tris, a, b, c);
            }
        }

        // The square stone at a corner of a vertical curb FACE: the <borderWidth>-wide end of that face, running its
        // full height, drawn with the stone texture — the face itself, not a piece on it. <cTop> is the corner vertex on
        // the top edge; <along> runs into the face away from the corner; <outward> is the face normal. The whole stone
        // texture maps [0,1]²; it reads squished to the short face, as agreed.
        void CornerFace(Vector3 cTop, Vector3 along, Vector3 outward, float height)
        {
            Vector3 dn = Vector3.down * height;
            Vector3 tA = cTop, tB = cTop + along * borderWidth;
            int itA = Push(tA,      outward, new Vector2(0f, 1f), Color.white);
            int itB = Push(tB,      outward, new Vector2(1f, 1f), Color.white);
            int ibA = Push(tA + dn, outward, new Vector2(0f, 0f), Color.white);
            int ibB = Push(tB + dn, outward, new Vector2(1f, 0f), Color.white);
            AddTriTo(trisCorner, itA, ibA, itB); AddTriTo(trisCorner, itB, ibA, ibB);
            AddTriTo(trisCorner, itA, itB, ibA); AddTriTo(trisCorner, itB, ibB, ibA);   // double-sided, like the walls
        }

        // A curb wall whose ends may be corner stones: the stone ends (<stoneA> at tA's end, <stoneB> at tB's end) are
        // cut out of the wall and drawn as stones, the rest is the wall as usual — one face, no piece laid over it.
        void AddCurbWall(Vector3 tA, Vector3 tB, Vector3 bA, Vector3 bB, Vector3 outward, bool isCurb, int refLevel,
                         Color? solid, bool stoneA, bool stoneB)
        {
            Vector3 along = (tB - tA).normalized;
            float height = tA.y - bA.y;
            if (stoneA) { CornerFace(tA, along, outward, height); tA += along * borderWidth; bA += along * borderWidth; }
            if (stoneB) { CornerFace(tB, -along, outward, height); tB -= along * borderWidth; bB -= along * borderWidth; }
            AddWall(tA, tB, bA, bB, outward, isCurb, refLevel, solid);
        }

        int LvlAt(int x, int z) => (x >= 0 && x < cols && z >= 0 && z < rows) ? levels[x, z] : 0;

        // Level of (x,z)'s half that owns its <edge> side (0=W 1=E 2=S 3=N): a diagonally-cut cell presents its KEPT
        // level on the kept corner's two edges and its UNDER level on the other two; uncut cells present their level.
        int CellEdgeLevel(int x, int z, int edge)
            => (cuts == null || cuts.dir[x, z] == 0) ? levels[x, z]
             : EdgeIsKept(cuts.dir[x, z], edge) ? levels[x, z] : cuts.underLevel[x, z];

        // Wall check against the neighbour's PER-EDGE level (nEdge = the edge of the NEIGHBOUR facing us), so walls
        // sit right beside diagonally-cut cells too. Off-map → the border skirt. 1-notch drop = decorative curb.
        bool WallB(int nx, int nz, int nEdge, int L, out float bottomY, out bool isCurb)
        {
            isCurb = false;
            if (nx < 0 || nx >= cols || nz < 0 || nz >= rows) { bottomY = skirtBottomY; return true; }
            int nL = CellEdgeLevel(nx, nz, nEdge);
            if (nL < L) { bottomY = nL * lh; isCurb = L - nL == 1; return true; }
            bottomY = 0f; return false;
        }

        // The checker curb FACE of a 45° 1-notch step (the hypotenuse of a diagonally-cut cell). UVs run ALONG the
        // edge (absolute projection onto the edge direction) so stones stay square and tile seamlessly across colinear
        // cut cells; V = notches on the face. <A,B> = hypotenuse ends (XZ); <yTop,yBot> the step.
        void AddDiagonalCurbFace(Vector3 A, Vector3 B, float yTop, float yBot, Color faceCol)
        {
            Vector3 dir = new Vector3(B.x - A.x, 0f, B.z - A.z).normalized;
            float Uof(float x, float z) => (x * dir.x + z * dir.z) / CurbWorldPerTexture;   // absolute → seamless across cells
            float vTop = (yTop - yBot) / lh;                                                 // one curb row per 0.2 notch
            Vector3 nrm = Vector3.Cross(Vector3.up, dir);                                     // face normal (double-sided anyway)
            Vector3 tA = new Vector3(A.x, yTop, A.z), tB = new Vector3(B.x, yTop, B.z);
            Vector3 bA = new Vector3(A.x, yBot, A.z), bB = new Vector3(B.x, yBot, B.z);
            int itA = Push(tA, nrm, new Vector2(Uof(A.x, A.z), vTop), faceCol);
            int itB = Push(tB, nrm, new Vector2(Uof(B.x, B.z), vTop), faceCol);
            int ibA = Push(bA, nrm, new Vector2(Uof(A.x, A.z), 0f),   faceCol);
            int ibB = Push(bB, nrm, new Vector2(Uof(B.x, B.z), 0f),   faceCol);
            AddTriTo(trisWall, itA, ibA, itB); AddTriTo(trisWall, itB, ibA, ibB);
            AddTriTo(trisWall, itA, itB, ibA); AddTriTo(trisWall, itB, ibB, ibA);   // double-sided
        }

        // A diagonally-CUT cell (painter 📏 line tool): two flat half-triangles — the KEPT corner's half at the
        // cell's levels-grid notch, the cut-away half at its own underLevel — with a vertical step along the
        // hypotenuse when they differ. Both halves are coloured SOLID from their painted tile (the same hard-cut
        // style level seams already use). A 1-notch step, on the hypotenuse or a straight edge, gets the checker curb
        // face and a band of stones along the higher half's top; a taller one is a plain wall.
        void EmitCutCell(int cx, int cz)
        {
            int dir = cuts.dir[cx, cz];
            int Lk = levels[cx, cz], Lu = cuts.underLevel[cx, cz];
            float yK = Lk * lh, yU = Lu * lh;
            Color colK = ChannelCol(cuts.keptCh[cx, cz]);
            Color colU = ChannelCol(cuts.underCh[cx, cz]);
            float x0 = ox + cx * cellSize, x1 = x0 + cellSize;
            float z0 = oz + cz * cellSize, z1 = z0 + cellSize;
            // world corners (+Z = the painter's north): nw / ne / se / sw
            Vector3 nw = new Vector3(x0, 0f, z1), ne = new Vector3(x1, 0f, z1);
            Vector3 se = new Vector3(x1, 0f, z0), sw = new Vector3(x0, 0f, z0);
            Vector3 K, O, A, B;   // kept corner, opposite corner, hypotenuse ends
            switch (dir)
            {
                case 1:  K = nw; O = se; A = sw; B = ne; break;
                case 2:  K = ne; O = sw; A = nw; B = se; break;
                case 3:  K = se; O = nw; A = ne; B = sw; break;
                default: K = sw; O = ne; A = se; B = nw; break;
            }
            bool keptHigh = Lk > Lu;
            bool curbStep = Mathf.Abs(Lk - Lu) == 1 && curbMat != null;

            // Each half's top: its triangle, with a band of stones cut out of it along each curb edge it owns — the
            // hypotenuse's first, where two bands meet — and the rest its plain painted tile.
            void Half(Vector3 corner, bool kept)
            {
                int L = kept ? Lk : Lu;
                float y = L * lh;
                Color col = kept ? colK : colU;
                var rest = new List<Vector3> { corner, A, B };
                if (curbStep && kept == keptHigh)
                {
                    Vector3 inward = corner - (A + B) * 0.5f; inward.y = 0f; inward.Normalize();
                    Vector3 hd = new Vector3(B.x - A.x, 0f, B.z - A.z).normalized;
                    float U(float x, float z) => (x * hd.x + z * hd.z) / CurbWorldPerTexture;
                    FlatPoly(Clip(rest, A, inward, borderWidth, true), y, trisWall, p => BandUV(p, A, inward, U), _ => Color.white);
                    rest = Clip(rest, A, inward, borderWidth, false);
                }
                if (curbMat != null)
                    for (int edge = 0; edge < 4; edge++)
                    {
                        if (EdgeIsKept(dir, edge) != kept) continue;
                        int nx = edge == 0 ? cx - 1 : edge == 1 ? cx + 1 : cx;
                        int nz = edge == 2 ? cz - 1 : edge == 3 ? cz + 1 : cz;
                        int nEdge = edge ^ 1;
                        if (!WallB(nx, nz, nEdge, L, out _, out bool curb) || !curb) continue;
                        Vector3 p0 = edge == 1 ? new Vector3(x1, 0f, z0) : edge == 3 ? new Vector3(x0, 0f, z1) : new Vector3(x0, 0f, z0);
                        Vector3 inward = edge == 0 ? Vector3.right : edge == 1 ? Vector3.left : edge == 2 ? Vector3.forward : Vector3.back;
                        System.Func<float, float, float> U = edge < 2 ? (System.Func<float, float, float>)((x, z) => z / CurbWorldPerTexture)
                                                                      : (x, z) => x / CurbWorldPerTexture;
                        FlatPoly(Clip(rest, p0, inward, borderWidth, true), y, trisWall, p => BandUV(p, p0, inward, U), _ => Color.white);
                        rest = Clip(rest, p0, inward, borderWidth, false);
                    }
                FlatPoly(rest, y, trisTop, p => new Vector2(p.x, p.z), _ => col);
            }
            Half(K, true);    // kept half
            Half(O, false);   // cut-away half

            if (Lk != Lu)   // step along the hypotenuse, faced from the higher half toward the lower
            {
                float yT = keptHigh ? yK : yU, yB = keptHigh ? yU : yK;
                Vector3 raisedCorner = keptHigh ? K : O;
                Vector3 inward = raisedCorner - (A + B) * 0.5f; inward.y = 0f; inward.Normalize();
                Color faceCol = keptHigh ? colK : colU;
                if (curbStep) AddDiagonalCurbFace(A, B, yT, yB, faceCol);   // 1-notch drop → checker curb
                else                                                        // taller cliff → plain blend-textured wall
                    AddWall(new Vector3(A.x, yT, A.z), new Vector3(B.x, yT, B.z),
                            new Vector3(A.x, yB, A.z), new Vector3(B.x, yB, B.z),
                            -inward, false, paintLevels[cx, cz], faceCol);
            }

            // straight edges: each belongs wholly to one half — wall it against the neighbour's facing half
            void Edge(int edge, int nx, int nz, int nEdge, Vector3 eA, Vector3 eB, Vector3 outwardE)
            {
                bool kept = EdgeIsKept(dir, edge);
                int L = kept ? Lk : Lu;
                if (!WallB(nx, nz, nEdge, L, out float by, out bool curb)) return;
                float topY = L * lh;
                AddWall(new Vector3(eA.x, topY, eA.z), new Vector3(eB.x, topY, eB.z),
                        new Vector3(eA.x, by, eA.z),   new Vector3(eB.x, by, eB.z),
                        outwardE, curb, paintLevels[cx, cz], kept ? colK : colU);
            }
            Edge(0, cx - 1, cz, 1, new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), Vector3.left);
            Edge(1, cx + 1, cz, 0, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), Vector3.right);
            Edge(2, cx, cz - 1, 3, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), Vector3.back);
            Edge(3, cx, cz + 1, 2, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), Vector3.forward);
        }

        for (int cz = 0; cz < rows; cz++)
            for (int cx = 0; cx < cols; cx++)
            {
                if (cuts != null && cuts.dir[cx, cz] != 0) { EmitCutCell(cx, cz); continue; }   // diagonal half-cell
                int L = levels[cx, cz];         // filled level → geometry (top height, walls, collision)
                int Lp = paintLevels[cx, cz];   // original painted level → blend-region reference (colour only)
                bool raised = L > Lp;           // always false now (levels == paintLevels); kept as a harmless no-op
                // When lifted, colour the WHOLE cell (top + its curbs) from that higher region so the corner reads as
                // the elevated tile SOLID. Otherwise colour off the cell's own painted level (soft blend within a level).
                Color raisedCol = raised ? RaisedRegionCol(cx, cz, L) : default;
                float topY = L * lh;
                float x0 = ox + cx * cellSize, x1 = x0 + cellSize;
                float z0 = oz + cz * cellSize, z1 = z0 + cellSize;

                // WALLS: one per edge whose neighbour is lower (or the map border). Each boundary is owned by the
                // HIGHER cell, so a wall is emitted exactly once. Border edges always skirt.
                bool wW = WallB(cx - 1, cz, 1, L, out float byW, out bool curbW);   // vs the neighbour's E-facing half
                bool wE = WallB(cx + 1, cz, 0, L, out float byE, out bool curbE);
                bool wS = WallB(cx, cz - 1, 3, L, out float byS, out bool curbS);
                bool wN = WallB(cx, cz + 1, 2, L, out float byN, out bool curbN);

                // STONES on this cell's top, part of it: a band along each curb edge; a square stone at each OUTWARD
                // (convex) corner where two curb edges meet (the two bands stop short of it); and a square stone at each
                // INWARD (concave) corner — where this cell's two neighbours toward the corner sit at its own level and
                // the diagonal one a notch lower, so the two arms' bands would otherwise meet only at a point. The
                // higher side owns the stone; base plane L=0 counts, so a SUNKEN lane's concave turns are bridged too.
                bool bandW = curbW && curbMat != null, bandE = curbE && curbMat != null;
                bool bandS = curbS && curbMat != null, bandN = curbN && curbMat != null;
                bool Inward(int dx, int dz) => L >= 0 && LvlAt(cx + dx, cz) == L && LvlAt(cx, cz + dz) == L && LvlAt(cx + dx, cz + dz) == L - 1;
                bool sSW = cornerMat != null && ((curbW && curbS) || Inward(-1, -1));
                bool sSE = cornerMat != null && ((curbE && curbS) || Inward(1, -1));
                bool sNW = cornerMat != null && ((curbW && curbN) || Inward(-1, 1));
                bool sNE = cornerMat != null && ((curbE && curbN) || Inward(1, 1));
                float bw = borderWidth;

                // TOP: a flat patch at topY on a sub×sub grid, per-vertex coloured from the blend field (own verts per
                // cell so nothing interpolates across a level seam — colours still match because they sample one field).
                // Where the cell has stones, the grid gains lines one band in from those edges so every square of it
                // is wholly stone or wholly top.
                var xs = new List<float>(sub + 3);
                var zs = new List<float>(sub + 3);
                for (int i = 0; i <= sub; i++) { xs.Add(Mathf.Lerp(x0, x1, i / (float)sub)); zs.Add(Mathf.Lerp(z0, z1, i / (float)sub)); }
                void AddLine(List<float> lines, float v) { foreach (var e in lines) if (Mathf.Abs(e - v) < 1e-5f) return; lines.Add(v); }
                if (bandW || sSW || sNW) AddLine(xs, x0 + bw);
                if (bandE || sSE || sNE) AddLine(xs, x1 - bw);
                if (bandS || sSW || sSE) AddLine(zs, z0 + bw);
                if (bandN || sNW || sNE) AddLine(zs, z1 - bw);
                xs.Sort(); zs.Sort();
                var idx = new int[xs.Count, zs.Count];
                for (int j = 0; j < zs.Count; j++)
                    for (int i = 0; i < xs.Count; i++) idx[i, j] = -1;
                int TopVertex(int i, int j)
                {
                    if (idx[i, j] < 0)
                    {
                        float wx = xs[i], wz = zs[j];
                        Color c = raised ? raisedCol : SampleCol(wx, wz, Lp);
                        idx[i, j] = Push(new Vector3(wx, topY, wz), Vector3.up, new Vector2(wx, wz), c);
                    }
                    return idx[i, j];
                }
                for (int j = 0; j + 1 < zs.Count; j++)
                    for (int i = 0; i + 1 < xs.Count; i++)
                    {
                        float xa = xs[i], xb = xs[i + 1], za = zs[j], zb = zs[j + 1];
                        float mx = (xa + xb) * 0.5f, mz = (za + zb) * 0.5f;
                        bool w = mx < x0 + bw, e = mx > x1 - bw, s = mz < z0 + bw, n = mz > z1 - bw;
                        List<int> into = null; System.Func<float, float, Vector2> uv = null;
                        if      (sSW && w && s) { into = trisCorner; uv = (x, z) => new Vector2((x - x0) / bw, (z - z0) / bw); }
                        else if (sSE && e && s) { into = trisCorner; uv = (x, z) => new Vector2((x1 - x) / bw, (z - z0) / bw); }
                        else if (sNW && w && n) { into = trisCorner; uv = (x, z) => new Vector2((x - x0) / bw, (z1 - z) / bw); }
                        else if (sNE && e && n) { into = trisCorner; uv = (x, z) => new Vector2((x1 - x) / bw, (z1 - z) / bw); }
                        else if (bandW && w && !(curbS && s) && !(curbN && n))
                        { into = trisWall; uv = (x, z) => new Vector2(z / CurbWorldPerTexture, (x - x0) / bw * BorderRows); }
                        else if (bandE && e && !(curbS && s) && !(curbN && n))
                        { into = trisWall; uv = (x, z) => new Vector2(z / CurbWorldPerTexture, (x1 - x) / bw * BorderRows); }
                        else if (bandS && s && !(curbW && w) && !(curbE && e))
                        { into = trisWall; uv = (x, z) => new Vector2(x / CurbWorldPerTexture, (z - z0) / bw * BorderRows); }
                        else if (bandN && n && !(curbW && w) && !(curbE && e))
                        { into = trisWall; uv = (x, z) => new Vector2(x / CurbWorldPerTexture, (z1 - z) / bw * BorderRows); }

                        if (into == null)
                        {
                            int v0 = TopVertex(i, j), v1 = TopVertex(i + 1, j), v2 = TopVertex(i, j + 1), v3 = TopVertex(i + 1, j + 1);
                            AddTriTo(trisTop, v0, v2, v1); AddTriTo(trisTop, v1, v2, v3);   // +Y up
                        }
                        else
                        {
                            int s0 = Push(new Vector3(xa, topY, za), Vector3.up, uv(xa, za), Color.white);
                            int s1 = Push(new Vector3(xb, topY, za), Vector3.up, uv(xb, za), Color.white);
                            int s2 = Push(new Vector3(xa, topY, zb), Vector3.up, uv(xa, zb), Color.white);
                            int s3 = Push(new Vector3(xb, topY, zb), Vector3.up, uv(xb, zb), Color.white);
                            AddTriTo(into, s0, s2, s1); AddTriTo(into, s1, s2, s3);
                        }
                    }

                // The walls, with the square stone wrapped onto each face's end at an OUTWARD corner (a clean "corner
                // post" that hides the faces' cut stones).
                bool post = cornerMat != null;
                if (wW) AddCurbWall(new Vector3(x0, topY, z0), new Vector3(x0, topY, z1), new Vector3(x0, byW, z0), new Vector3(x0, byW, z1),
                                    Vector3.left, curbW, Lp, raised ? (Color?)raisedCol : null, post && curbW && curbS, post && curbW && curbN);
                if (wE) AddCurbWall(new Vector3(x1, topY, z0), new Vector3(x1, topY, z1), new Vector3(x1, byE, z0), new Vector3(x1, byE, z1),
                                    Vector3.right, curbE, Lp, raised ? (Color?)raisedCol : null, post && curbE && curbS, post && curbE && curbN);
                if (wS) AddCurbWall(new Vector3(x0, topY, z0), new Vector3(x1, topY, z0), new Vector3(x0, byS, z0), new Vector3(x1, byS, z0),
                                    Vector3.back, curbS, Lp, raised ? (Color?)raisedCol : null, post && curbS && curbW, post && curbS && curbE);
                if (wN) AddCurbWall(new Vector3(x0, topY, z1), new Vector3(x1, topY, z1), new Vector3(x0, byN, z1), new Vector3(x1, byN, z1),
                                    Vector3.forward, curbN, Lp, raised ? (Color?)raisedCol : null, post && curbN && curbW, post && curbN && curbE);
            }

        var mesh = new Mesh { name = Sanitize(rootName) + "_ground" };
        mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(cols4);
        // submesh 0 = tops (blend); + curbs (strip) + corners (square stone) when each has geometry & a material.
        var subTris = new List<List<int>> { trisTop };
        var subMats = new List<Material> { mat };
        if (curbMat   != null && trisWall.Count   > 0) { subTris.Add(trisWall);   subMats.Add(curbMat); }
        if (cornerMat != null && trisCorner.Count > 0) { subTris.Add(trisCorner); subMats.Add(cornerMat); }
        mesh.subMeshCount = subTris.Count;
        for (int s = 0; s < subTris.Count; s++) mesh.SetTriangles(subTris[s], s);
        mesh.RecalculateBounds();
        PersistMesh(mesh, rootName);

        var root = new GameObject(rootName);
        SceneManager.MoveGameObjectToScene(root, scene);

        var vis = new GameObject("Ground_Visual");
        vis.transform.SetParent(root.transform, false);
        vis.transform.position = Vector3.zero;   // elevated verts are already in world XZ (centred on origin)
        vis.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = vis.AddComponent<MeshRenderer>();
        mr.sharedMaterials = subMats.ToArray();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = true;
        vis.AddComponent<MeshCollider>().sharedMesh = mesh;   // the shaped surface IS the collider (player + pathfinder)

        WorldBuildUtil.SetLayerRecursive(root, groundLayer);
        WalkableGround.EnsurePathfinderConfig(scene, groundLayer);
        Physics.SyncTransforms();   // so the importer's prop drop-rays hit the shaped ground

        AssetDatabase.SaveAssets();
        return root;
    }

    /// <summary>What a curbed ground holds besides its streets and paving: a stream cut into it, its ground lifted into
    /// mounds and ramps, and more ground with a look of its own (a bridge's model) joined into it.</summary>
    public sealed class CurbedExtras
    {
        /// <summary>Signed distance into water, above 0 inside it; null when there is none.</summary>
        public System.Func<float, float, float> sink;
        /// <summary>How deep the water's bed goes below the raised top, how steep its banks fall (rise per unit in), and
        /// how far below the raised top the water's surface lies.</summary>
        public float depth = 0.45f, bankSlope = 0.7f, waterBelow = 0.2f;
        /// <summary>The look of the water's bed and banks, as a vertex colour of the ground material.</summary>
        public Color bedLook = new Color(0f, 0f, 0f, 1f);
        /// <summary>Height added to the ground at a point (mounds, ramps up to a bridge's ends); null for none.</summary>
        public System.Func<float, float, float> lift;
        /// <summary>Called with the ground's vertex lists before the mesh is made, to join more ground into it with looks of
        /// its own (a bridge's model): its tangents, and its triangles by the material each is drawn with — one drawn
        /// submesh per material after the ground's own.</summary>
        public System.Action<List<Vector3>, List<Vector3>, List<Vector2>, List<Color>, List<Vector4>, Dictionary<Material, List<int>>> joinTops;
    }

    /// <summary>
    /// A ground of LEVELS bounded by SMOOTH CURVES, for a plan drawn in shapes rather than painted in cells: the ground
    /// stands at a whole number of curbs (<paramref name="rise"/> each) above its own height, from <paramref name="lowest"/>
    /// (0 or below) up. <paramref name="raise"/> gives, at any point, one value per step of height: the signed distance into
    /// the ground standing at level lowest + t + 1 or higher (above 0 inside it). Each step's curb is traced along the line
    /// where its value is 0 — through every cell, at the exact point between two samples — so a round street gets a round
    /// curb, not steps or 45-degree cuts; two steps meeting on one line make a curb two high. Like the cell-painted curbs, the curb has a vertical face and, on the raised top along
    /// its edge, a band of the curb's own stones one row wide, both in the curb's submesh; along a curve the band bends
    /// with it, its stones running the length of the curb (measured along it, each run shrunk a little to hold a whole
    /// number of stones), so face and band line up stone for stone.
    /// <para>With <paramref name="extras"/>: water cut into the ground (its bed sloping down from a band of curb stones at
    /// its edge, gentle enough to walk out of, so nobody is ever trapped in it) with a water surface on it, the ground
    /// lifted into mounds and ramps, and a bridge's model joined in. Tops are coloured from <paramref name="sample"/>.
    /// One mesh, one collider: the curb face, the band, the banks and the bridges are all part of the ground, never pieces
    /// laid on it (RULE ONEGROUND). The water is a separate surface, drawn only. Centred on the origin; the caller keeps
    /// both meshes.</para>
    /// </summary>
    public static GameObject LayPlotCurbed(Scene scene, string rootName, float sizeX, float sizeZ, float cellSize, int groundLayer,
                                           System.Func<float, float, float[]> raise, int lowest, float rise, GroundSampler sample,
                                           Material groundMat, Material curbMat, out Mesh mesh, CurbedExtras extras = null)
        => LayPlotCurbed(scene, rootName, sizeX, sizeZ, cellSize, groundLayer, raise, lowest, rise, sample, groundMat, curbMat, out mesh, out _, extras);

    public static GameObject LayPlotCurbed(Scene scene, string rootName, float sizeX, float sizeZ, float cellSize, int groundLayer,
                                           System.Func<float, float, float[]> raise, int lowest, float rise, GroundSampler sample,
                                           Material groundMat, Material curbMat, out Mesh mesh, out Mesh water, CurbedExtras extras = null)
    {
        extras ??= new CurbedExtras();
        System.Func<float, float, float> sink = extras.sink ?? ((x, z) => -1f);
        System.Func<float, float, float> lift = extras.lift ?? ((x, z) => 0f);
        int cols = Mathf.RoundToInt(sizeX / cellSize), rows = Mathf.RoundToInt(sizeZ / cellSize);
        float ox = -sizeX * 0.5f, oz = -sizeZ * 0.5f;
        float band = curbMat != null ? CurbBandWidth(curbMat) : 0f;
        float waterLevel = rise - extras.waterBelow;
        // NEVER HOLDS THE EDITOR: this runs on the editor's own thread, so a step that takes too long stops the build with
        // the step's name instead of freezing the editor (it froze once, 2026-10-28).
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var said = new System.Text.StringBuilder();
        void Step(string what)
        {
            said.Append(what).Append(' ').Append(clock.ElapsedMilliseconds).Append(" ms; ");
            if (clock.ElapsedMilliseconds > 60000) throw new System.Exception("curbed ground took over a minute, at: " + said);
        }

        // The fields at every corner of every cell: f the curb's, one value per step of height, and g the water's.
        var f = new float[cols + 1, rows + 1][]; var g = new float[cols + 1, rows + 1];
        for (int i = 0; i <= cols; i++)
            for (int j = 0; j <= rows; j++) { float x = ox + i * cellSize, z = oz + j * cellSize; f[i, j] = raise(x, z); g[i, j] = sink(x, z); }
        int steps = f[0, 0].Length;
        // The water and its band of stones lie in ground one curb up, so levels 0 and 1 are always among them.
        if (lowest > 0 || lowest + steps < 1) throw new System.Exception($"the ground's levels run from {lowest} to {lowest + steps}; they must reach 0 and 1");
        int LevelOf(float[] at) { int l = lowest; foreach (float x in at) if (x > 0f) l++; return l; }
        Step("fields");
        bool Plain(int i, int j)
        {
            float gHi = Mathf.Max(Mathf.Max(g[i, j], g[i + 1, j]), Mathf.Max(g[i + 1, j + 1], g[i, j + 1]));
            if (gHi > -band) return false;
            for (int t = 0; t < steps; t++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var (a, b) in new[] { (i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1) }) { lo = Mathf.Min(lo, f[a, b][t]); hi = Mathf.Max(hi, f[a, b][t]); }
                if (!(lo > band || hi <= 0f)) return false;
            }
            return true;
        }
        // The cells the curb, its band or the water runs through are cut into four triangles round their middle.
        var split = new List<(Vector2[] p, float[][] v, float[] w)>();
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                if (Plain(i, j)) continue;
                Vector2 sw = new Vector2(ox + i * cellSize, oz + j * cellSize), m = sw + Vector2.one * (cellSize * 0.5f);
                split.Add((new[] { sw, sw + new Vector2(cellSize, 0f), sw + new Vector2(cellSize, cellSize), sw + new Vector2(0f, cellSize), m },
                           new[] { f[i, j], f[i + 1, j], f[i + 1, j + 1], f[i, j + 1], raise(m.x, m.y) },
                           new[] { g[i, j], g[i + 1, j], g[i + 1, j + 1], g[i, j + 1], sink(m.x, m.y) }));
            }
        Step("cut cells " + split.Count);

        // THE CURB'S LENGTH: its pieces of line (on dry ground only), joined into runs, each point given how far along it is.
        var lines = new List<(Vector2 a, Vector2 b)>();
        var lowSide = new List<Vector2>();   // per piece of curb: the way down, toward the low ground
        var lineStep = new List<int>();      // per piece of curb: the step of height it is the curb of
        var edgeLines = new List<(Vector2 a, Vector2 b)>();
        foreach (var (q, v, w) in split)
            foreach (var (x, y) in new[] { (0, 1), (1, 2), (2, 3), (3, 0) })
            {
                for (int t = 0; t < steps; t++)
                {
                var cut = Crossing(q[x], q[y], q[4], v[x][t], v[y][t], v[4][t], 0f);
                if (cut.HasValue && (cut.Value.b - cut.Value.a).sqrMagnitude > 1e-8f)
                {
                    var mid = (cut.Value.a + cut.Value.b) * 0.5f;
                    if (sink(mid.x, mid.y) <= -band)
                    {
                        lines.Add(cut.Value);
                        // Down the field's slope across this very triangle — the slope that cut the top here, so the face
                        // always looks at the low ground beside it. (Reading the field a step off the piece's middle
                        // instead landed on the wrong side wherever the curb bends tightly: 118 faces turned their
                        // backs to the camera and showed the sky through the ground, 2026-10-28.)
                        lowSide.Add(-Slope(q[x], q[y], q[4], v[x][t], v[y][t], v[4][t]));
                        lineStep.Add(t);
                    }
                }
                }
                var edge = Crossing(q[x], q[y], q[4], w[x], w[y], w[4], 0f);
                if (edge.HasValue && (edge.Value.b - edge.Value.a).sqrMagnitude > 1e-8f) edgeLines.Add(edge.Value);
            }
        var along = Lengths(lines, CurbWorldPerTexture, Step);
        var edgeAlong = Lengths(edgeLines, CurbWorldPerTexture, Step);
        Step("lengths");
        var all = new List<(Vector2 a, Vector2 b)>(lines); all.AddRange(edgeLines);
        var allAlong = new List<Vector2>(along); allAlong.AddRange(edgeAlong);
        var bins = new Dictionary<(int, int), List<int>>();
        for (int k = 0; k < all.Count; k++)
        {
            var key = (Mathf.FloorToInt(all[k].a.x), Mathf.FloorToInt(all[k].a.y));
            if (!bins.TryGetValue(key, out var l)) bins[key] = l = new List<int>();
            l.Add(k);
        }
        // How far along its curb the point nearest `pt` is, the way the curb runs there (`ahead`, a unit step in the
        // direction it counts up) and how many repeats a unit of that way is worth (`rate`).
        float AlongNear(Vector2 pt, out Vector2 ahead, out float rate)
        {
            float best = float.MaxValue, u = 0f;
            ahead = Vector2.zero; rate = 0f;
            int bx = Mathf.FloorToInt(pt.x), bz = Mathf.FloorToInt(pt.y);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!bins.TryGetValue((bx + dx, bz + dz), out var l)) continue;
                    foreach (int k in l)
                    {
                        var (a, b) = all[k];
                        Vector2 ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(pt - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                        float dd = (a + ab * t - pt).sqrMagnitude;
                        if (dd < best)
                        {
                            best = dd; u = Mathf.Lerp(allAlong[k].x, allAlong[k].y, t);
                            float span = allAlong[k].y - allAlong[k].x, len = Mathf.Sqrt(ab.sqrMagnitude);
                            ahead = len > 1e-6f ? ab / len * Mathf.Sign(span) : Vector2.zero;
                            rate = len > 1e-6f ? Mathf.Abs(span) / len : 0f;
                        }
                    }
                }
            return u;
        }

        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var colours = new List<Color>();
        var trisTop = new List<int>(); var trisCurb = new List<int>();
        var wverts = new List<Vector3>(); var wtris = new List<int>();
        var shared = new Dictionary<(int, int, int), int>();
        // 0 the ground's own height, 1 a band of curb stones, 2 the raised top, 3 the water's bed and banks. The bed's depth
        // comes from the water's value carried on the piece's corners (`v`), never read afresh: on the bed's edge that value
        // is exactly 0, so the bed meets the band beside it with no crack (read afresh it was a few thousandths off).
        // The level the pieces being laid stand at (0 and 2 regions alike; the band stands at the level its curb raised it to).
        int curLevel = 1;
        float HeightOf(Vector2 pt, int region, float v)
        {
            float l = lift(pt.x, pt.y);
            if (region == 3) return rise + l - Mathf.Min(extras.depth + rise, Mathf.Max(0f, v) * extras.bankSlope);
            return curLevel * rise + l;
        }
        // A piece of the band of stones: its stones counted along the ONE stretch of curb nearest its middle, so all its
        // corners agree. (Each corner read on its own could land on two different stretches — either side of where a
        // loop of curb closes, or of where two curbs meet — and squeezed many stones into one small piece: 514 striped
        // pieces, 2026-10-28.) So its corners are its own, never shared with the piece beside it.
        Vector2 bandMid = Vector2.zero, bandAhead = Vector2.zero; float bandU = 0f, bandRate = 0f;
        int Point(Vector2 pt, float v, int region, Vector2 inside)
        {
            var key = (Mathf.RoundToInt(pt.x * 1000f), Mathf.RoundToInt(pt.y * 1000f), region == 0 ? 100 + curLevel : region);
            int at;
            if (region != 1 && shared.TryGetValue(key, out at)) return at;
            at = verts.Count;
            verts.Add(new Vector3(pt.x, HeightOf(pt, region, v), pt.y)); norms.Add(Vector3.up);
            if (region == 1)
            {
                uvs.Add(new Vector2(bandU + Vector2.Dot(pt - bandMid, bandAhead) * bandRate, Mathf.Clamp01(v / Mathf.Max(1e-5f, band))));
                colours.Add(Color.white);
                return at;
            }
            else if (region == 3) { uvs.Add(pt); colours.Add(extras.bedLook); }
            else
            {
                // A point on a region's edge takes its colour from just inside its own region.
                Vector2 q = pt + (inside - pt).normalized * Mathf.Min(0.02f, (inside - pt).magnitude);
                sample(q.x, q.y, out var w);
                uvs.Add(pt); colours.Add(new Color(w.x, w.y, w.z, w.w));
            }
            shared[key] = at;
            return at;
        }
        // Unity draws a triangle's front where its corners run clockwise seen from above.
        void Fan(List<(Vector2 p, float v)> poly, int region)
        {
            if (poly.Count < 3) return;
            float area = 0f;
            for (int k = 0; k < poly.Count; k++) { var a0 = poly[k].p; var b0 = poly[(k + 1) % poly.Count].p; area += a0.x * b0.y - b0.x * a0.y; }
            if (Mathf.Abs(area) < 1e-7f) return;
            Vector2 mid = Vector2.zero; foreach (var (pt, _) in poly) mid += pt; mid /= poly.Count;
            var idx = new List<int>();
            if (region == 1) { bandMid = mid; bandU = AlongNear(mid, out bandAhead, out bandRate); }
            foreach (var (pt, v) in poly) idx.Add(Point(pt, v, region, mid));
            var into = region == 1 ? trisCurb : trisTop;
            bool cw = area < 0f;
            for (int k = 1; k + 1 < idx.Count; k++) { into.Add(idx[0]); into.Add(cw ? idx[k] : idx[k + 1]); into.Add(cw ? idx[k + 1] : idx[k]); }
            if (region == 3)
            {
                // The water over the bed: the same shape at the water's level, drawn only.
                int w0 = wverts.Count;
                foreach (var (pt, _) in poly) wverts.Add(new Vector3(pt.x, waterLevel, pt.y));
                for (int k = 1; k + 1 < poly.Count; k++) { wtris.Add(w0); wtris.Add(cw ? w0 + k : w0 + k + 1); wtris.Add(cw ? w0 + k + 1 : w0 + k); }
            }
        }
        // A triangle's pieces: the water (w above 0), the band of stones along the water, then the ground's levels.
        void Region(Vector2 a, Vector2 b, Vector2 c, float wa, float wb, float wc)
        {
            var byW = new List<(Vector2, float)> { (a, wa), (b, wb), (c, wc) };
            curLevel = 1;
            Fan(Clip(byW, 0f, float.PositiveInfinity), 3);
            var dry = Clip(byW, float.NegativeInfinity, 0f);
            if (dry.Count < 3) return;
            if (band > 0f)
            {
                var nearW = Clip(dry, -band, 0f);
                if (nearW.Count >= 3)
                {
                    var bandPoly = new List<(Vector2, float)>();
                    foreach (var (pt, wv) in nearW) bandPoly.Add((pt, band + wv));
                    curLevel = 1;
                    Fan(bandPoly, 1);
                }
                dry = Clip(dry, float.NegativeInfinity, -band);
                if (dry.Count < 3) return;
            }
            // Carry the curb's fields over to the dry piece's corners, read at each one.
            var dryF = new List<(Vector2 p, float[] v)>();
            foreach (var (pt, _) in dry) dryF.Add((pt, raise(pt.x, pt.y)));
            Levels(dryF, 0);
        }
        // A dry piece cut at each step of height in turn: below the step it stands at that level, above it on to the next.
        void Levels(List<(Vector2 p, float[] v)> poly, int t)
        {
            if (poly.Count < 3) return;
            if (t == steps) { AtLevel(poly, t); return; }
            AtLevel(ClipStep(poly, t, float.NegativeInfinity, 0f), t);
            Levels(ClipStep(poly, t, 0f, float.PositiveInfinity), t + 1);
        }
        // A piece standing at level lowest + t: the band of stones along the curb that raised it there, then the rest.
        void AtLevel(List<(Vector2 p, float[] v)> poly, int t)
        {
            if (poly.Count < 3) return;
            curLevel = lowest + t;
            if (t == 0 || band <= 0f) { Fan(Step1(poly, Mathf.Max(0, t - 1)), 0); return; }
            var lifted = Step1(poly, t - 1);
            Fan(Clip(lifted, 0f, band), 1);
            Fan(Clip(lifted, band, float.PositiveInfinity), 0);
        }

        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                if (!Plain(i, j)) continue;
                Vector2 sw = new Vector2(ox + i * cellSize, oz + j * cellSize);
                curLevel = LevelOf(f[i, j]);
                Fan(new List<(Vector2, float)> { (sw, 0f), (sw + new Vector2(0f, cellSize), 0f),
                                                 (sw + new Vector2(cellSize, cellSize), 0f), (sw + new Vector2(cellSize, 0f), 0f) }, 0);
            }
        Step("whole cells");
        int done = 0;
        foreach (var (q, v, w) in split)
        {
            foreach (var (x, y) in new[] { (0, 1), (1, 2), (2, 3), (3, 0) })
                Region(q[x], q[y], q[4], w[x], w[y], w[4]);
            if (++done % 2000 == 0) Step("cut cells done " + done);
        }
        Step("cut cells done " + done);

        // A curb's face: from one level below `level` up to it along the piece a→b, turned toward the low side (`low`),
        // following the ground under it; `ua`/`ub` how far along its curb each end is.
        void Face(Vector2 a, Vector2 b, Vector2 low, float ua, float ub, int level)
        {
            Vector2 e = (b - a).normalized, side = new Vector2(-e.y, e.x);
            if (Vector2.Dot(side, low) < 0f) side = -side;
            Vector3 n = new Vector3(side.x, 0f, side.y);
            float la = lift(a.x, a.y) + (level - 1) * rise, lb = lift(b.x, b.y) + (level - 1) * rise;
            int t0 = verts.Count;
            foreach (var (pt, y, u) in new[] { (a, rise + la, ua), (b, rise + lb, ub), (a, la, ua), (b, lb, ub) })
            {
                verts.Add(new Vector3(pt.x, y, pt.y)); norms.Add(n); uvs.Add(new Vector2(u, (y - (pt == a ? la : lb)) / rise)); colours.Add(Color.white);
            }
            void Tri(int i0, int i1, int i2)
            {
                bool front = Vector3.Dot(Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]), n) >= 0f;
                trisCurb.Add(i0); trisCurb.Add(front ? i1 : i2); trisCurb.Add(front ? i2 : i1);
            }
            Tri(t0, t0 + 2, t0 + 1); Tri(t0 + 1, t0 + 2, t0 + 3);
        }
        for (int k = 0; k < lines.Count; k++) Face(lines[k].a, lines[k].b, lowSide[k], along[k].x, along[k].y, lowest + lineStep[k] + 1);
        // Where LOW ground reaches the band of stones along the water, the band stands up out of it the same as a curb, so it
        // gets the same face, toward the dry side. (Without one, the step had no side at all and the sky showed through the
        // gap, everywhere a street or a tree's pit ran down to the water, 2026-10-28.)
        foreach (var (q, v, w) in split)
            foreach (var (x, y) in new[] { (0, 1), (1, 2), (2, 3), (3, 0) })
            {
                var step = Crossing(q[x], q[y], q[4], w[x], w[y], w[4], -band);
                if (!step.HasValue) continue;
                Vector2 a = step.Value.a, b = step.Value.b;
                float fa = raise(a.x, a.y)[-lowest], fb = raise(b.x, b.y)[-lowest];
                if (fa >= 0f && fb >= 0f) continue;                     // raised on both ends: the top meets the band level
                if (fa >= 0f) a = Vector2.Lerp(a, b, fa / (fa - fb));
                else if (fb >= 0f) b = Vector2.Lerp(a, b, fa / (fa - fb));
                if ((b - a).sqrMagnitude < 1e-8f) continue;
                Vector2 m = (a + b) * 0.5f;
                float um = AlongNear(m, out var ahead, out var rate);
                Face(a, b, -Slope(q[x], q[y], q[4], w[x], w[y], w[4]), um + Vector2.Dot(a - m, ahead) * rate, um + Vector2.Dot(b - m, ahead) * rate, 1);
            }
        var tangents = new List<Vector4>();
        var joined = new Dictionary<Material, List<int>>();
        extras.joinTops?.Invoke(verts, norms, uvs, colours, tangents, joined);
        Step("faces and joins, " + verts.Count + " points");

        mesh = new Mesh { name = rootName + "_ground", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetColors(colours);
        if (tangents.Count == verts.Count) mesh.SetTangents(tangents);
        bool curbs = curbMat != null && trisCurb.Count > 0;
        var mats = new List<Material> { groundMat };
        if (curbs) mats.Add(curbMat);
        int own = mats.Count;
        mats.AddRange(joined.Keys);
        mesh.subMeshCount = mats.Count;
        mesh.SetTriangles(trisTop, 0);
        if (curbs) mesh.SetTriangles(trisCurb, 1);
        for (int k = own; k < mats.Count; k++) mesh.SetTriangles(joined[mats[k]], k);
        mesh.RecalculateBounds();
        water = null;
        if (wtris.Count > 0)
        {
            water = new Mesh { name = rootName + "_water", indexFormat = wverts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            water.SetVertices(wverts);
            var wn = new Vector3[wverts.Count]; var wuv = new Vector2[wverts.Count];
            for (int k = 0; k < wverts.Count; k++) { wn[k] = Vector3.up; wuv[k] = new Vector2(wverts[k].x, wverts[k].z); }
            water.SetNormals(wn); water.SetUVs(0, wuv); water.SetTriangles(wtris, 0);
            water.RecalculateBounds();
        }

        var root = new GameObject(rootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        var vis = new GameObject("Ground_Visual");
        vis.transform.SetParent(root.transform, false);
        vis.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = vis.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats.ToArray();
        // Ground joined with a standing shape (a bridge) casts, as that shape did on its own.
        mr.shadowCastingMode = joined.Count > 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.receiveShadows = true;
        vis.AddComponent<MeshCollider>().sharedMesh = mesh;
        Step("collider");
        Debug.Log("[unwritten] curbed ground " + rootName + ": " + said);
        WorldBuildUtil.SetLayerRecursive(root, groundLayer);
        return root;
    }

    // The width of the band of curb stones on a raised top: one row of the curb texture's stones, kept square by reading
    // the texture's real aspect, so new art can never stretch them.
    static float CurbBandWidth(Material curbMat) => CurbBandWidth(curbMat != null ? curbMat.GetTexture("_BaseMap") : null);
    public static float CurbBandWidth(Texture tex)
    {
        float aspect = tex != null ? tex.width / (float)tex.height : 6f;
        return CurbWorldPerTexture / aspect;
    }

    // The part of a flat polygon whose value lies between lo and hi, its edges cut where the value crosses them.
    static List<(Vector2 p, float v)> Clip(List<(Vector2 p, float v)> poly, float lo, float hi)
    {
        List<(Vector2, float)> Keep(List<(Vector2 p, float v)> src, float limit, bool above)
        {
            var o = new List<(Vector2, float)>();
            for (int k = 0; k < src.Count; k++)
            {
                var (a, va) = src[k]; var (b, vb) = src[(k + 1) % src.Count];
                bool ina = above ? va >= limit : va <= limit, inb = above ? vb >= limit : vb <= limit;
                if (ina) o.Add((a, va));
                if (ina != inb) { float t = (limit - va) / (vb - va); o.Add((Vector2.Lerp(a, b, t), limit)); }
            }
            return o;
        }
        var r = poly;
        if (!float.IsNegativeInfinity(lo)) r = Keep(r, lo, true);
        if (r.Count >= 3 && !float.IsPositiveInfinity(hi)) r = Keep(r, hi, false);
        return r;
    }

    // The part of a flat polygon whose step-t value lies between lo and hi, every step's value carried along its cut edges.
    static List<(Vector2 p, float[] v)> ClipStep(List<(Vector2 p, float[] v)> poly, int t, float lo, float hi)
    {
        List<(Vector2, float[])> Keep(List<(Vector2 p, float[] v)> src, float limit, bool above)
        {
            var o = new List<(Vector2, float[])>();
            for (int k = 0; k < src.Count; k++)
            {
                var (a, va) = src[k]; var (b, vb) = src[(k + 1) % src.Count];
                bool ina = above ? va[t] >= limit : va[t] <= limit, inb = above ? vb[t] >= limit : vb[t] <= limit;
                if (ina) o.Add((a, va));
                if (ina != inb)
                {
                    float u = (limit - va[t]) / (vb[t] - va[t]);
                    var at = new float[va.Length];
                    for (int i = 0; i < at.Length; i++) at[i] = Mathf.Lerp(va[i], vb[i], u);
                    at[t] = limit;
                    o.Add((Vector2.Lerp(a, b, u), at));
                }
            }
            return o;
        }
        var r = poly;
        if (!float.IsNegativeInfinity(lo)) r = Keep(r, lo, true);
        if (r.Count >= 3 && !float.IsPositiveInfinity(hi)) r = Keep(r, hi, false);
        return r;
    }

    // A polygon with only its step-t value.
    static List<(Vector2 p, float v)> Step1(List<(Vector2 p, float[] v)> poly, int t)
    {
        var o = new List<(Vector2 p, float v)>(poly.Count);
        foreach (var (p, v) in poly) o.Add((p, v[t]));
        return o;
    }

    // Where the value equals `at` inside the triangle a, b, c: the piece of line across it, or none.
    static (Vector2 a, Vector2 b)? Crossing(Vector2 a, Vector2 b, Vector2 c, float va, float vb, float vc, float at)
    {
        var pts = new List<Vector2>(2);
        foreach (var (p, q, vp, vq) in new[] { (a, b, va, vb), (b, c, vb, vc), (c, a, vc, va) })
            if ((vp > at) != (vq > at)) pts.Add(Vector2.Lerp(p, q, (at - vp) / (vq - vp)));
        return pts.Count == 2 ? (pts[0], pts[1]) : ((Vector2, Vector2)?)null;
    }

    // Which way, and how steeply, the value rises across the triangle a, b, c (taken as flat between its corners).
    static Vector2 Slope(Vector2 a, Vector2 b, Vector2 c, float va, float vb, float vc)
    {
        Vector2 e1 = b - a, e2 = c - a;
        float d1 = vb - va, d2 = vc - va, det = e1.x * e2.y - e1.y * e2.x;
        if (Mathf.Abs(det) < 1e-12f) return Vector2.zero;
        return new Vector2(d1 * e2.y - d2 * e1.y, e1.x * d2 - e2.x * d1) / det;
    }

    // How far along its curb each piece's two ends are: the pieces joined at their shared ends into runs, each run's length
    // stretched or shrunk a little to hold a whole number of the curb texture's stones, so its stones never end in a
    // sliver. Returned in texture repeats (one per `per` units of curb).
    static List<Vector2> Lengths(List<(Vector2 a, Vector2 b)> lines, float per, System.Action<string> step = null)
    {
        var u = new List<Vector2>(new Vector2[lines.Count]);
        (int, int) Key(Vector2 v) => (Mathf.RoundToInt(v.x * 1000f), Mathf.RoundToInt(v.y * 1000f));
        var ends = new Dictionary<(int, int), List<int>>();
        for (int k = 0; k < lines.Count; k++)
            foreach (var e in new[] { lines[k].a, lines[k].b })
            {
                if (!ends.TryGetValue(Key(e), out var l)) ends[Key(e)] = l = new List<int>();
                l.Add(k);
            }
        var done = new bool[lines.Count];
        for (int start = 0; start < lines.Count; start++)
        {
            if (done[start]) continue;
            if (step != null && start % 500 == 0) step("lengths " + start);
            // Walk back to the run's beginning (an end only one piece touches), or round a closed loop to where it began.
            int first = start; Vector2 from = lines[start].a;
            var seen = new HashSet<int> { start };
            while (true)
            {
                // Not List.Find: for a list of numbers it answers 0, a real piece, when nothing matches (it froze the
                // editor twice, 2026-10-28, walking round the same piece forever).
                int next = -1;
                foreach (int k in ends[Key(from)]) if (k != first && !seen.Contains(k)) { next = k; break; }
                if (next < 0) break;
                seen.Add(next);
                from = lines[next].a == from || Key(lines[next].a).Equals(Key(from)) ? lines[next].b : lines[next].a;
                first = next;
            }
            // Then forward from there, adding up the length.
            var run = new List<(int k, bool flip)>();
            int at = first; Vector2 p = from; float total = 0f;
            while (at >= 0 && !done[at])
            {
                done[at] = true;
                bool flip = !Key(lines[at].a).Equals(Key(p));
                Vector2 q = flip ? lines[at].a : lines[at].b;
                run.Add((at, flip));
                total += Vector2.Distance(p, q);
                p = q;
                int cur = at;
                at = -1;
                foreach (int k in ends[Key(p)]) if (k != cur && !done[k]) { at = k; break; }
            }
            float scale = total > per ? Mathf.Round(total / per) * per / total : 1f, s = 0f;
            foreach (var (k, flip) in run)
            {
                float len = Vector2.Distance(lines[k].a, lines[k].b) * scale / per;
                u[k] = flip ? new Vector2(s + len, s) : new Vector2(s, s + len);
                s += len;
            }
        }
        return u;
    }

    // The cliff's material: a copy of this map's own ground material with its rock layer switched on, so the grass on
    // the cliff top is the ground's grass exactly and the face fades in from it. The face is the biome's colour when it
    // has one — lighter up the cliff, darker and greyer at its foot, in patches, split by cracks — else the gravel
    // ground tile. Null (the cliff then draws as plain ground) if that tile is needed and missing.
    static Material MakeCliffMaterial(Material groundMat, string rootName, Color faceColor)
    {
        bool flat = faceColor.a > 0f;
        var tex = flat ? null : GroundTiles.Load("BASE_OR_PATCH_OR_PATH_gravel_scree");
        if (!flat && tex == null) return null;
        EnsureFolder(MatDir);
        string matPath = $"{MatDir}/{Sanitize(rootName)}_cliff.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(groundMat); AssetDatabase.CreateAsset(mat, matPath); }
        else mat.CopyPropertiesFromMaterial(groundMat);
        mat.EnableKeyword("_CLIFF_ROCK");
        if (flat) mat.EnableKeyword("_CLIFF_FACE_COLOR"); else mat.DisableKeyword("_CLIFF_FACE_COLOR");
        mat.SetTexture("_CliffRockMap", tex);
        mat.SetColor("_CliffFaceColor", flat ? new Color(faceColor.r, faceColor.g, faceColor.b, 1f) : Color.white);
        mat.SetFloat("_CliffRockTiling", 1f / GroundTexWorldSize);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // smoothstep(a,b,x) — soft 0→1 ramp, the source of the feathered grass↔dirt edge.
    static float Smooth(float a, float b, float x)
    {
        x = Mathf.Clamp01((x - a) / Mathf.Max(1e-5f, b - a));
        return x * x * (3f - 2f * x);
    }

    // Resolve the ground textures + create/update the shared GroundBlend material asset. Returns null (and logs)
    // if the grass texture (the required base) or the shader is missing. The one place both the flat and elevated
    // builders make their material, so they stay identical.
    static Material MakeGroundMaterial(string rootName, string grassTexName, string dirtTexName, string pathTexName,
                                       string stoneTexName, string rockTexName, Color? tint)
    {
        var grassTex = GroundBaseMap(string.IsNullOrEmpty(grassTexName) ? "BASE_OR_PATCH_meadow_grass" : grassTexName);
        if (grassTex == null) return null;
        var dirtTex = (!string.IsNullOrEmpty(dirtTexName) ? GroundBaseMap(dirtTexName) : null)
                      ?? GroundBaseMap("BASE_OR_PATCH_OR_PATH_soil") ?? GroundBaseMap("PATH_dirt_road");
        var pathTex  = (!string.IsNullOrEmpty(pathTexName)  ? GroundBaseMap(pathTexName)  : null) ?? dirtTex;
        var stoneTex = (!string.IsNullOrEmpty(stoneTexName) ? GroundBaseMap(stoneTexName) : null);
        var rockTex  = (!string.IsNullOrEmpty(rockTexName)  ? GroundBaseMap(rockTexName)  : null);

        var shader = Shader.Find("Unwritten/GroundBlend");
        if (shader == null)
        { Debug.LogError("[unwritten] Shader 'Unwritten/GroundBlend' not found (Assets/Shaders/GroundBlend.shader)."); return null; }

        EnsureFolder(MatDir);
        string matPath = $"{MatDir}/{Sanitize(rootName)}_blend.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
        else mat.shader = shader;
        mat.SetTexture("_GrassMap", grassTex);
        if (dirtTex  != null) mat.SetTexture("_DirtMap",  dirtTex);
        if (pathTex  != null) mat.SetTexture("_PathMap",  pathTex);
        if (stoneTex != null) mat.SetTexture("_StoneMap", stoneTex);
        if (rockTex  != null) mat.SetTexture("_RockMap",  rockTex);
        float tiling = 1f / GroundTexWorldSize;   // one texture repeat per GroundTexWorldSize world units
        mat.SetFloat("_GrassTiling", tiling);
        mat.SetFloat("_DirtTiling", tiling);
        mat.SetFloat("_PathTiling", tiling);
        mat.SetFloat("_StoneTiling", tiling);
        mat.SetFloat("_RockTiling", tiling);
        mat.SetColor("_Color", tint ?? Color.white);   // biome hue (always written so a re-bake resets a stale tint)
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // The decorative CURB material for 1-notch drops (raised-sidewalk lip / gutter side): a plain lit strip of
    // CURB_blocks.png, tiled by the wall's world-space UVs (baked in AddWall). Returns null (walls then stay in the
    // ground-blend submesh) if the texture or URP/Lit shader is missing. Idempotent — reuses the asset across bakes.
    static Material MakeCurbMaterial()
    {
        var tex = GroundTiles.Load(CurbTile);
        if (tex == null) return null;
        // Unwritten/GroundTrim (not URP/Lit): stamps the same stencil bit 32 as the ground so the hand-drawn INK
        // overlay skips the curb wall (a steep curb viewed edge-on otherwise smears into a solid black ink band).
        var shader = Shader.Find("Unwritten/GroundTrim");
        if (shader == null) return null;
        EnsureFolder(MatDir);
        string matPath = $"{MatDir}/ground_curb.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
        else mat.shader = shader;
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // The single SQUARE-STONE material dropped at every border corner (inward + outward). Its texture is one lone
    // stone (CORNER_curb_slab.png) mapped [0,1]² onto each corner quad, so a corner always reads as one clean
    // full stone instead of a UV-sliced sliver or two overlapping strips. Returns null (corners then get no stone)
    // if the texture or URP/Lit shader is missing. Idempotent — reuses the asset across bakes.
    static Material MakeCornerMaterial()
    {
        var tex = GroundTiles.Load("CORNER_curb_slab");
        if (tex == null) return null;
        // Unwritten/GroundTrim (not URP/Lit): stamps stencil bit 32 so the INK overlay skips the corner stone too.
        var shader = Shader.Find("Unwritten/GroundTrim");
        if (shader == null) return null;
        EnsureFolder(MatDir);
        string matPath = $"{MatDir}/ground_curb_corner.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
        else mat.shader = shader;
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Persist the mesh as an asset (so the saved scene keeps a stable reference), replacing any prior bake.
    static void PersistMesh(Mesh mesh, string rootName, string suffix = "_ground")
    {
        EnsureFolder(MeshDir);
        string meshPath = $"{MeshDir}/{Sanitize(rootName)}{suffix}.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);
    }

    // Every ground texture comes from the one tile folder, by name (GroundTiles is the single way in).
    static Texture GroundBaseMap(string name) => GroundTiles.Load(name);

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
