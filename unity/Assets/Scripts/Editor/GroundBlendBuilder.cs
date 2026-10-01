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
        const float CurbWorldPerTexture = 0.5f;           // one full curb strip spans this many world units along the edge

        // BORDER-RING stones along each curb edge: a band of the raised top itself, drawn with the curb texture — part
        // of the one ground, never a strip laid over it (RULE ONEGROUND). Width = one texture-row scaled so the stones
        // stay SQUARE (read the texture's real aspect, so new art can't stretch them); BorderRows widens it in rows.
        const int   BorderRows  = 1;
        var   curbTex   = curbMat != null ? curbMat.GetTexture("_BaseMap") as Texture2D : null;
        float texAspect = curbTex != null ? curbTex.width / (float)curbTex.height : 6f;
        float borderWidth = BorderRows * (CurbWorldPerTexture / texAspect);   // world width of BorderRows square rows

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
        var tex = GroundTiles.Load("CURB_blocks");
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
