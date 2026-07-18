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
/// <see cref="WalkableGround.AddWalkableFloor"/>, so scatter/mobs/pathfinding all work unchanged. Textures are
/// pulled from the pack's own <c>ground_grass</c> / <c>ground_soil</c> prefab materials, so it tracks
/// <c>Fix Pxltgr Materials</c> with no extra wiring.
///
/// <para><b>Elevated variant</b> (<see cref="LayPlotSampledElevated"/>): when a map paints per-cell heights, the
/// ground becomes real terrain — each painter cell is a flat top at its level's height, with TRUE vertical walls
/// (curbs / gutter sides / cliffs) between cells of differing levels and a skirt down the map border. It carries a
/// <see cref="MeshCollider"/> instead of the flat box, so the player capsule bumps cliffs + climbs curbs and the
/// height-aware pathfinder routes around drops — all off the SAME shaped surface.</para>
/// </summary>
public static class GroundBlendBuilder
{
    const string PropsDir      = "Assets/1_Authored/Prefabs/Props";
    const string MeshDir       = "Assets/2_Generated/Meshes";
    const string MatDir        = "Assets/2_Generated/Materials";
    const string GroundTilesDir = "Assets/1_Authored/Art/GroundTiles";

    /// <summary>World units ONE ground-blend texture repeat spans in-game (the shader's world-XZ tiling = 1/this).
    /// The single source of truth for ground texture density: the material bake below AND the brush-palette manifest
    /// export both read it, so the map painter's preview tiles ground at the exact in-game scale.</summary>
    public const float GroundTexWorldSize = 2.5f;

    /// <summary>Per-vertex ground sampler: given a world XZ, return the four blend weights as a Vector4 mapped to the
    /// mesh vertex-colour channels — <c>x</c>=road (.r), <c>y</c>=stone (.g), <c>z</c>=rock (.b), <c>w</c>=dirt (.a),
    /// all 0..1. The procedural <see cref="LayPlot"/> overload feeds a noise-dirt + routed-path sampler (stone/rock
    /// left 0); the Map Painter importer feeds one that reads its painted cell grid.</summary>
    public delegate void GroundSampler(float worldX, float worldZ, out Vector4 weights);

    /// <summary>Elevation-aware ground sampler used by the elevated builder: same as <see cref="GroundSampler"/> but
    /// also told the <paramref name="refLevel"/> — the ORIGINAL painted notch of the cell this vertex belongs to (NOT
    /// its corner-filled geometry level) — so the sampler can refuse to blend in cells painted at a DIFFERENT level (a
    /// hard texture cut at a step, soft blend within a level) instead of feathering across the cliff. The elevated
    /// builder feeds the ORIGINAL painted level (not the FillCornerNotches-filled geometry level); a cell the fill
    /// lifted into a higher region is instead coloured SOLID from that higher region (see <c>RaisedRegionCol</c>), so a
    /// raised concave corner reads as the elevated tile with a hard edge rather than smearing the two regions.</summary>
    public delegate void GroundSamplerLeveled(float worldX, float worldZ, int refLevel, out Vector4 weights);

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
    /// <param name="grassTexName">Ground prop name for the BASE surface (biome base, e.g. "ground_grass"/"ground_rock").</param>
    /// <param name="dirtTexName">Ground prop name for the PATCH surface blended in by the dirt mask (e.g. "ground_soil").</param>
    /// <param name="tint">Albedo multiply applied by the GroundBlend shader's <c>_Color</c> (biome hue). Null = white.</param>
    /// <param name="hardEdge">Snap the dirt + path weights to 0/1 so the grass↔dirt↔path edges are crisp, not feathered.</param>
    public static GameObject LayPlot(Scene scene, string rootName, Vector3 center, float sizeX, float sizeZ,
                                     int groundLayer, int seed = 73,
                                     IReadOnlyList<PathRouting.Route> paths = null, float pathWidth = 0f,
                                     string pathTexName = null, float dirtAmount = 0.3f, float cell = 0.8f,
                                     string grassTexName = "ground_grass", string dirtTexName = "ground_soil",
                                     Color? tint = null, bool hardEdge = false)
    {
        return LayPlotSampled(scene, rootName, center, sizeX, sizeZ, groundLayer,
                              MakeProceduralSampler(seed, dirtAmount, paths, pathWidth, hardEdge),
                              grassTexName, dirtTexName, pathTexName, null, null, cell, tint);
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
    /// the matching <c>ground_*</c> prop's Base Map (dirt/path fall back if their prop is missing). Returns the
    /// ground root, or <c>null</c> if the grass prop or the <c>Unwritten/GroundBlend</c> shader is missing.
    /// </summary>
    public static GameObject LayPlotSampled(Scene scene, string rootName, Vector3 center, float sizeX, float sizeZ,
                                            int groundLayer, GroundSampler sample,
                                            string grassTexName = "ground_grass", string dirtTexName = "ground_soil",
                                            string pathTexName = "ground_road", string stoneTexName = null,
                                            string rockTexName = null, float cell = 0.8f, Color? tint = null)
    {
        var mat = MakeGroundMaterial(rootName, grassTexName, dirtTexName, pathTexName, stoneTexName, rockTexName, tint);
        if (mat == null) return null;

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
        PersistMesh(mesh, rootName);

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
        WalkableGround.AddWalkableFloor(scene, root, center, sizeX, sizeZ, groundLayer);

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
                                                    string grassTexName = "ground_grass", string dirtTexName = "ground_soil",
                                                    string pathTexName = "ground_road", string stoneTexName = null,
                                                    string rockTexName = null, Color? tint = null)
    {
        var mat = MakeGroundMaterial(rootName, grassTexName, dirtTexName, pathTexName, stoneTexName, rockTexName, tint);
        if (mat == null) return null;
        // Two level grids, deliberately kept apart. `paintLevels` = the ORIGINAL painted notches → the reference the
        // blend uses to decide which cells share a region (colour). `levels` = the corner-filled notches → drives ONLY
        // the geometry (top heights, walls, collision). Colouring off the FILLED grid was the corner-blend bug: a
        // concave-corner cell that FillCornerNotches raises to match the higher region then counted as the SAME level
        // as that region and feathered the two textures together. Fix: colour NON-raised cells off their own painted
        // level, and colour a RAISED cell SOLID from the region it was lifted into (RaisedRegionCol) — so the corner
        // reads as the elevated tile with a hard edge, while its geometry still fills the notch for pathing.
        var paintLevels = levels;
        levels = FillCornerNotches(levels, cols, rows);   // fill concave corner notches so path turns read solid
        var curbMat = MakeCurbMaterial();                 // null → 1-notch walls just stay in the ground-blend submesh
        var cornerMat = MakeCornerMaterial();             // single square stone dropped at every border corner (null → skip)
        const float CurbWorldPerTexture = 0.5f;           // one full curb strip spans this many world units along the edge

        // Flat BORDER-RING trim: a strip of the same curb texture laid flat on the raised top along each curb edge.
        // Width = one texture-row scaled so the stones stay SQUARE (read the texture's real aspect, so new art can't
        // stretch them). BorderRows widens the trim in whole rows; a hair of Y-lift stops it z-fighting the top.
        const int   BorderRows  = 1;
        const float BorderLift  = 0.003f;
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

        // Colour for a cell that FillCornerNotches lifted into a higher region: sample the tile of an orthogonal
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

        // Flat trim quad laid ON TOP along a curb edge: <eA,eB> are the edge endpoints on the raised top; <inward> is
        // the unit direction into the raised cell. Runs the same world-U as the curb below (so trim + curb align) and
        // V across the strip width. Double-sided + normal-up, into the curb submesh (same material as the vertical curb).
        void AddBorder(Vector3 eA, Vector3 eB, Vector3 inward, float trimA = 0f, float trimB = 0f)
        {
            if (curbMat == null) return;
            Vector3 along = (eB - eA).normalized;   // pull each end back by its trim so an outward corner frees a stone slot
            eA += along * trimA; eB -= along * trimB;
            Vector3 up = Vector3.up * BorderLift;
            Vector3 oA = eA + up, oB = eB + up;
            Vector3 iA = eA + inward * borderWidth + up, iB = eB + inward * borderWidth + up;
            bool alongZ = Mathf.Abs(eA.x - eB.x) < 1e-4f;
            float uA = (alongZ ? eA.z : eA.x) / CurbWorldPerTexture;
            float uB = (alongZ ? eB.z : eB.x) / CurbWorldPerTexture;
            int voA = Push(oA, Vector3.up, new Vector2(uA, 0f),         Color.white);
            int voB = Push(oB, Vector3.up, new Vector2(uB, 0f),         Color.white);
            int viA = Push(iA, Vector3.up, new Vector2(uA, BorderRows), Color.white);
            int viB = Push(iB, Vector3.up, new Vector2(uB, BorderRows), Color.white);
            AddTriTo(trisWall, voA, viA, voB); AddTriTo(trisWall, voB, viA, viB);
            AddTriTo(trisWall, voA, voB, viA); AddTriTo(trisWall, voB, viB, viA);
        }

        // One flat borderWidth-square STONE dropped into a cell corner where two borders meet — the shared corner
        // treatment for BOTH corner kinds. INWARD (concave L): the two arm strips live on different cells and meet
        // only at a point, leaving this diagonal cell's corner bare (the old "one-block gap" / 1/3-stone sliver);
        // OUTWARD (convex): the two perpendicular strips are trimmed back a stone so they no longer overlap here
        // (killing their z-fight). Either way one clean square stone bridges the turn. <cornerX,cornerZ> is the cell
        // corner; <ix,iz> step ±borderWidth inward. The whole single-stone texture maps [0,1]² onto the quad, so it
        // always reads as one full stone regardless of where the strips' repeating grid falls.
        void AddCornerStone(float cornerX, float cornerZ, float ix, float iz, float topY)
        {
            if (cornerMat == null) return;
            Vector3 up = Vector3.up * BorderLift;
            float xa = cornerX, xb = cornerX + ix, za = cornerZ, zb = cornerZ + iz;
            int v00 = Push(new Vector3(xa, topY, za) + up, Vector3.up, new Vector2(0f, 0f), Color.white);
            int v10 = Push(new Vector3(xb, topY, za) + up, Vector3.up, new Vector2(1f, 0f), Color.white);
            int v01 = Push(new Vector3(xa, topY, zb) + up, Vector3.up, new Vector2(0f, 1f), Color.white);
            int v11 = Push(new Vector3(xb, topY, zb) + up, Vector3.up, new Vector2(1f, 1f), Color.white);
            AddTriTo(trisCorner, v00, v01, v10); AddTriTo(trisCorner, v10, v01, v11);
            AddTriTo(trisCorner, v00, v10, v01); AddTriTo(trisCorner, v10, v11, v01);
        }

        // The same square stone wrapped onto a VERTICAL curb FACE at an outward corner: a borderWidth-wide slice at
        // the corner end of the face, running the full curb height, pushed a hair proud of the wall so it hides that
        // face's cut corner stone. The wall itself stays whole underneath (it's the collider). <cTop> is the corner
        // vertex on the top edge; <along> runs into the face away from the corner; <outward> is the face normal;
        // <height> is the curb drop. The whole stone texture maps [0,1]²; it reads squished to the short face, as agreed.
        void AddCornerStoneWall(Vector3 cTop, Vector3 along, Vector3 outward, float height)
        {
            if (cornerMat == null) return;
            Vector3 push = outward * BorderLift, dn = Vector3.down * height;
            Vector3 tA = cTop + push, tB = cTop + along * borderWidth + push;
            int itA = Push(tA,      outward, new Vector2(0f, 1f), Color.white);
            int itB = Push(tB,      outward, new Vector2(1f, 1f), Color.white);
            int ibA = Push(tA + dn, outward, new Vector2(0f, 0f), Color.white);
            int ibB = Push(tB + dn, outward, new Vector2(1f, 0f), Color.white);
            AddTriTo(trisCorner, itA, ibA, itB); AddTriTo(trisCorner, itB, ibA, ibB);
            AddTriTo(trisCorner, itA, itB, ibA); AddTriTo(trisCorner, itB, ibB, ibA);   // double-sided
        }
        int LvlAt(int x, int z) => (x >= 0 && x < cols && z >= 0 && z < rows) ? levels[x, z] : 0;

        for (int cz = 0; cz < rows; cz++)
            for (int cx = 0; cx < cols; cx++)
            {
                int L = levels[cx, cz];         // filled level → geometry (top height, walls, collision)
                int Lp = paintLevels[cx, cz];   // original painted level → blend-region reference (colour only)
                bool raised = L > Lp;           // FillCornerNotches lifted this concave-corner cell into a higher region
                // When lifted, colour the WHOLE cell (top + its curbs) from that higher region so the corner reads as
                // the elevated tile SOLID. Otherwise colour off the cell's own painted level (soft blend within a level).
                Color raisedCol = raised ? RaisedRegionCol(cx, cz, L) : default;
                float topY = L * lh;
                float x0 = ox + cx * cellSize, x1 = x0 + cellSize;
                float z0 = oz + cz * cellSize, z1 = z0 + cellSize;

                // TOP: a flat sub×sub patch at topY, per-vertex coloured from the blend field (own verts per cell
                // so nothing interpolates across a level seam — colours still match because they sample one field).
                var idx = new int[sub + 1, sub + 1];
                for (int j = 0; j <= sub; j++)
                    for (int i = 0; i <= sub; i++)
                    {
                        float wx = Mathf.Lerp(x0, x1, i / (float)sub);
                        float wz = Mathf.Lerp(z0, z1, j / (float)sub);
                        Color c = raised ? raisedCol : SampleCol(wx, wz, Lp);
                        idx[i, j] = Push(new Vector3(wx, topY, wz), Vector3.up, new Vector2(wx, wz), c);
                    }
                for (int j = 0; j < sub; j++)
                    for (int i = 0; i < sub; i++)
                    {
                        int v0 = idx[i, j], v1 = idx[i + 1, j], v2 = idx[i, j + 1], v3 = idx[i + 1, j + 1];
                        AddTriTo(trisTop, v0, v2, v1); AddTriTo(trisTop, v1, v2, v3);   // +Y up
                    }

                // WALLS: one per edge whose neighbour is lower (or the map border). Each boundary is owned by the
                // HIGHER cell, so a wall is emitted exactly once. Border edges always skirt. All four are resolved up
                // front so a border strip can be trimmed back at whichever ends land on an OUTWARD (convex) corner.
                bool wW = WallBottom(levels, cols, rows, cx - 1, cz, L, lh, skirtBottomY, out float byW, out bool curbW);
                bool wE = WallBottom(levels, cols, rows, cx + 1, cz, L, lh, skirtBottomY, out float byE, out bool curbE);
                bool wS = WallBottom(levels, cols, rows, cx, cz - 1, L, lh, skirtBottomY, out float byS, out bool curbS);
                bool wN = WallBottom(levels, cols, rows, cx, cz + 1, L, lh, skirtBottomY, out float byN, out bool curbN);
                float bwT = borderWidth;
                if (wW)   // West (−X)
                {
                    AddWall(new Vector3(x0, topY, z0), new Vector3(x0, topY, z1),
                            new Vector3(x0, byW, z0),  new Vector3(x0, byW, z1),  Vector3.left, curbW, Lp,
                            raised ? (Color?)raisedCol : null);
                    if (curbW) AddBorder(new Vector3(x0, topY, z0), new Vector3(x0, topY, z1), Vector3.right,
                                         curbS ? bwT : 0f, curbN ? bwT : 0f);
                }
                if (wE)   // East (+X)
                {
                    AddWall(new Vector3(x1, topY, z0), new Vector3(x1, topY, z1),
                            new Vector3(x1, byE, z0),  new Vector3(x1, byE, z1),  Vector3.right, curbE, Lp,
                            raised ? (Color?)raisedCol : null);
                    if (curbE) AddBorder(new Vector3(x1, topY, z0), new Vector3(x1, topY, z1), Vector3.left,
                                         curbS ? bwT : 0f, curbN ? bwT : 0f);
                }
                if (wS)   // South (−Z)
                {
                    AddWall(new Vector3(x0, topY, z0), new Vector3(x1, topY, z0),
                            new Vector3(x0, byS, z0),  new Vector3(x1, byS, z0),  Vector3.back, curbS, Lp,
                            raised ? (Color?)raisedCol : null);
                    if (curbS) AddBorder(new Vector3(x0, topY, z0), new Vector3(x1, topY, z0), Vector3.forward,
                                         curbW ? bwT : 0f, curbE ? bwT : 0f);
                }
                if (wN)   // North (+Z)
                {
                    AddWall(new Vector3(x0, topY, z1), new Vector3(x1, topY, z1),
                            new Vector3(x0, byN, z1),  new Vector3(x1, byN, z1),  Vector3.forward, curbN, Lp,
                            raised ? (Color?)raisedCol : null);
                    if (curbN) AddBorder(new Vector3(x0, topY, z1), new Vector3(x1, topY, z1), Vector3.back,
                                         curbW ? bwT : 0f, curbE ? bwT : 0f);
                }
                // OUTWARD (convex) corners: on the flat top the two strips were trimmed back a stone — drop the square
                // stone into the freed slot. On the two VERTICAL faces meeting here, wrap the same stone onto each face
                // at the corner (a clean "corner post") so the faces' cut stones are hidden too.
                if (curbW && curbS)
                {
                    AddCornerStone(x0, z0, bwT, bwT, topY);
                    AddCornerStoneWall(new Vector3(x0, topY, z0), Vector3.forward, Vector3.left, topY - byW);
                    AddCornerStoneWall(new Vector3(x0, topY, z0), Vector3.right,   Vector3.back, topY - byS);
                }
                if (curbE && curbS)
                {
                    AddCornerStone(x1, z0, -bwT, bwT, topY);
                    AddCornerStoneWall(new Vector3(x1, topY, z0), Vector3.forward, Vector3.right, topY - byE);
                    AddCornerStoneWall(new Vector3(x1, topY, z0), Vector3.left,    Vector3.back,  topY - byS);
                }
                if (curbW && curbN)
                {
                    AddCornerStone(x0, z1, bwT, -bwT, topY);
                    AddCornerStoneWall(new Vector3(x0, topY, z1), Vector3.back,  Vector3.left,    topY - byW);
                    AddCornerStoneWall(new Vector3(x0, topY, z1), Vector3.right, Vector3.forward, topY - byN);
                }
                if (curbE && curbN)
                {
                    AddCornerStone(x1, z1, -bwT, -bwT, topY);
                    AddCornerStoneWall(new Vector3(x1, topY, z1), Vector3.back, Vector3.right,   topY - byE);
                    AddCornerStoneWall(new Vector3(x1, topY, z1), Vector3.left, Vector3.forward, topY - byN);
                }
            }

        // INWARD (concave L) corners: fill the missing corner stone so the checker border turns unbroken. The HIGHER
        // cell (base plane L=0 or a raised feature) owns the fill when both its orthogonal neighbours toward a corner
        // sit at its OWN level (so it has no curb/strip there) while the diagonal cell drops exactly one notch — the
        // height at which the two arms' 1-notch curb strips exist and meet only at this point. Base-plane L=0 counts
        // so a SUNKEN lane's concave turns (diagonal = −1) are bridged, not only raised features.
        for (int cz = 0; cz < rows; cz++)
            for (int cx = 0; cx < cols; cx++)
            {
                int L = levels[cx, cz];
                if (L < 0) continue;   // the higher side owns the stone; base plane (L=0) too, so a SUNKEN lane's
                                       // concave turns (diagonal = L−1) get bridged — not just raised features (L>0)
                float topY = L * lh;
                float x0 = ox + cx * cellSize, x1 = x0 + cellSize;
                float z0 = oz + cz * cellSize, z1 = z0 + cellSize;
                if (LvlAt(cx - 1, cz) == L && LvlAt(cx, cz - 1) == L && LvlAt(cx - 1, cz - 1) == L - 1)
                    AddCornerStone(x0, z0,  borderWidth,  borderWidth, topY);   // SW
                if (LvlAt(cx + 1, cz) == L && LvlAt(cx, cz - 1) == L && LvlAt(cx + 1, cz - 1) == L - 1)
                    AddCornerStone(x1, z0, -borderWidth,  borderWidth, topY);   // SE
                if (LvlAt(cx - 1, cz) == L && LvlAt(cx, cz + 1) == L && LvlAt(cx - 1, cz + 1) == L - 1)
                    AddCornerStone(x0, z1,  borderWidth, -borderWidth, topY);   // NW
                if (LvlAt(cx + 1, cz) == L && LvlAt(cx, cz + 1) == L && LvlAt(cx + 1, cz + 1) == L - 1)
                    AddCornerStone(x1, z1, -borderWidth, -borderWidth, topY);   // NE
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

    // Close CORNER NOTCHES: a cell that has two PERPENDICULAR neighbours both higher than it is the concave inner
    // corner of an L-turn (or the pinch where two paths kiss diagonally) — grass shows through it. Raise it to the
    // LOWER of the two arms so the corner reads solid, no gap peeks through, and the path connects for collision +
    // pathfinding. This catches turns even when the crook holds a TALLER tile (an earlier version wrongly required
    // BOTH sides to be lower and so skipped exactly those joints). Single pass off the ORIGINAL grid (writes to a
    // clone) so one fill can't cascade into a new one this pass; out-of-bounds counts as level 0 (never an arm).
    //
    // Only BASE-plane cells (level ≥ 0) are eligible: filling was ever only meant to close background GRASS gaps in
    // the crook of a RAISED feature. A cell the user deliberately SANK (level < 0 — a road/gutter/moat lane) is a
    // channel, and every diagonal TURN of that lane sits in the crook of two higher (base-level) neighbours — so the
    // old unconditional fill lifted every zigzag-lane corner up to the base and re-coloured it solid, shredding the
    // painted path into a diagonal smear. Skipping sunken cells keeps the lane exactly as painted (crisp corners),
    // while grass-gap fills between raised arms are unaffected.
    static int[,] FillCornerNotches(int[,] levels, int cols, int rows)
    {
        var outLv = (int[,])levels.Clone();
        int Lvl(int x, int z) => (x >= 0 && x < cols && z >= 0 && z < rows) ? levels[x, z] : int.MinValue;
        for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                int L = levels[x, z];
                if (L < 0) continue;   // never auto-raise a user-sunken lane cell — its corners stay crisp as painted
                int w = Lvl(x - 1, z), e = Lvl(x + 1, z), n = Lvl(x, z + 1), s = Lvl(x, z - 1);
                int fill = L;
                if (w > L && n > L) fill = Mathf.Max(fill, Mathf.Min(w, n));
                if (e > L && n > L) fill = Mathf.Max(fill, Mathf.Min(e, n));
                if (w > L && s > L) fill = Mathf.Max(fill, Mathf.Min(w, s));
                if (e > L && s > L) fill = Mathf.Max(fill, Mathf.Min(e, s));
                outLv[x, z] = fill;
            }
        return outLv;
    }

    // A wall is needed on an edge when the neighbour cell is LOWER (bottom = neighbour's top) or off the map
    // (bottom = the border skirt). Returns false (no wall) when the neighbour is the same height or higher —
    // that boundary is owned by the taller side, so we never double a wall. `L` is this (higher) cell's level.
    static bool WallBottom(int[,] levels, int cols, int rows, int nx, int nz, int L, float lh, float skirtBottomY, out float bottomY, out bool isCurb)
    {
        isCurb = false;
        if (nx < 0 || nx >= cols || nz < 0 || nz >= rows) { bottomY = skirtBottomY; return true; }   // border skirt (not a curb)
        int nL = levels[nx, nz];
        if (nL < L) { bottomY = nL * lh; isCurb = (L - nL == 1); return true; }   // exactly one notch down = decorative curb
        bottomY = 0f; return false;
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
    // ground_curb.png, tiled by the wall's world-space UVs (baked in AddWall). Returns null (walls then stay in the
    // ground-blend submesh) if the texture or URP/Lit shader is missing. Idempotent — reuses the asset across bakes.
    static Material MakeCurbMaterial()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{GroundTilesDir}/ground_curb.png");
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
    // stone (ground_curb_corner.png) mapped [0,1]² onto each corner quad, so a corner always reads as one clean
    // full stone instead of a UV-sliced sliver or two overlapping strips. Returns null (corners then get no stone)
    // if the texture or URP/Lit shader is missing. Idempotent — reuses the asset across bakes.
    static Material MakeCornerMaterial()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{GroundTilesDir}/ground_curb_corner.png");
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
    static void PersistMesh(Mesh mesh, string rootName)
    {
        EnsureFolder(MeshDir);
        string meshPath = $"{MeshDir}/{Sanitize(rootName)}_ground.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);
    }

    // Pull a ground prop's Base Map so the blend uses the SAME textures the tiled ground does (tracks Fix Materials).
    static Texture GroundBaseMap(string name)
    {
        var p = PropResolver.Load(name);
        if (p != null)
        {
            var r = p.GetComponentInChildren<MeshRenderer>();
            var m = r != null ? r.sharedMaterial : null;
            if (m != null)
                return m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap") : m.mainTexture;
        }
        // Not a prop — the palette pointed at a raw texture (or material) asset, e.g. the GroundTiles art. Resolve by name.
        return FindTextureAsset(name);
    }

    // Locate a ground texture the palette references directly: a Texture2D by that exact name, else a Material's base map.
    static Texture FindTextureAsset(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var guid in AssetDatabase.FindAssets($"{name} t:Texture2D"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        foreach (var guid in AssetDatabase.FindAssets($"{name} t:Material"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) != name) continue;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap") : m.mainTexture;
        }
        return null;
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
