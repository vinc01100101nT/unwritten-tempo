using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Phase 3: generates a <b>3D town</b> by reusing the 2D layout brain (<see cref="TownGenerator"/>)
/// and swapping the backend. It feeds the generator a synthetic marker <see cref="TownTheme"/> (so
/// the algorithm runs unchanged), then walks the resulting <see cref="TownLayout"/> and instantiates
/// Kenney <i>prefabs</i> from a <see cref="Town3DTheme"/> — ground tiles, roads, boundary, and one
/// prefab per placed prop (houses/trees/wells) — instead of painting tilemaps.
///
/// Flat for now (all ground tops at y=0); the design leaves room for the Phase-4 height field. Floor
/// collision is ONE big box on a 'Ground' layer (perf rule), per-prop box colliders block movement,
/// and a <see cref="Pathfinder3DConfig"/> turns on height-aware routing. Output:
/// <b>Assets/Scenes/Town3D.unity</b> — open and press Play.
/// </summary>
public static class Town3DBaker
{
    const string ScenePath  = "Assets/Scenes/Town3D.unity";
    const string ThemePath  = "Assets/MapGen/Town3DTheme_Default.asset";

    // Layout params (edit to taste, or select your own MapRecipe before running to override).
    const int Width = 24, Height = 24;

    // Scenic border: tiered Kenney hills ringing the flat town (see TerrainSandbox for the look).
    const int   ScenicBorderCells = 7;     // how many cells of hills around the town
    const int   ScenicMaxTier = 5;         // tallest hill tier
    const float ScenicTierStep = 0.6f;     // world height per tier
    const float ScenicRise = 0.55f;        // tiers gained per cell outward
    const float ScenicNoise = 0.18f, ScenicNoiseAmp = 1.6f;   // bumpiness
    const float ScenicBaseSlab = 1.0f;     // block body depth below the surface
    const float TerrainOverlap = 1.3f;     // overlap so rounded shoulders merge (memory: kenney-3d-terrain-overlap)

    // ---- tiered-interior state (set per Generate(), read by EmitTerrain/EmitProps/spawn snap) ----
    static bool   _tiered;
    static int[]  _tier;                    // per interior cell, row-major (y*width+x)
    static int    _tW, _tH;
    static float  _tierStep;

    // World-Y of the top surface of interior cell (x,y). 0 when not tiered. Clamped to the grid.
    static float InteriorTopY(int x, int y)
    {
        if (!_tiered || _tier == null) return 0f;
        x = Mathf.Clamp(x, 0, _tW - 1); y = Mathf.Clamp(y, 0, _tH - 1);
        return _tier[y * _tW + x] * _tierStep;
    }

    [MenuItem("Tools/unwritten/Generate 3D Town", priority = 10)]
    static void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("3D Town", "Exit Play mode first, then re-run.", "OK"); return; }

        var theme = ResolveTheme();
        if (theme == null) return;                       // ResolveTheme already explained why
        if (theme.groundPrefab == null)
        { Fail("Town3DTheme has no Ground prefab assigned. Bake the kits first (Build Prefabs ▸ Both Kits)."); return; }

        int rendererIndex = Build25DSpike.EnsureUniversalRenderer();
        if (rendererIndex < 0) return;
        int groundLayer = Build25DElevationSpike.EnsureGroundLayer();
        if (groundLayer < 0) { Fail("No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers."); return; }

        // 1) Run the SHARED layout algorithm via a synthetic 2D theme + recipe.
        var map = new Dictionary<PropTemplate, Prop3D>();
        var recipe = BuildRecipe(theme, map);
        var L = TownGenerator.Generate(recipe);

        float cs = theme.cellSize > 0f ? theme.cellSize : FootprintXZ(theme.groundPrefab);
        if (cs <= 0f) cs = 1f;

        // 2) Emit the 3D scene.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        Build25DSpike.BuildLight();
        var root = new GameObject("Town3D").transform;

        // Tiered interior: build the heightfield + flatten house pads/plaza BEFORE emitting, so the
        // terrain, props and spawns all read the same per-cell tier. Off → classic flat town.
        _tiered = theme.tieredInterior;
        _tier = null; _tW = L.width; _tH = L.height; _tierStep = theme.interiorTierStep;
        if (_tiered)
        {
            _tier = Terrain3DField.BuildHeightmap(L.width, L.height, theme.interiorTiers,
                                                  theme.interiorNoiseScale, recipe.seed);
            FlattenPads(L, theme, map);
        }

        EmitTerrain(L, theme, cs, groundLayer, root);
        if (theme.scenicBorder) EmitScenicBorder(L, theme, cs, groundLayer, root);
        else EmitBoundary(L, theme, cs, groundLayer, root);
        int props = EmitProps(L, theme, map, cs, root);
        BuildPathfinderConfig(groundLayer, theme);

        // Rig (player at the open centre, camera, mobs, HUD) — reuse the spike helpers.
        Vector3 centerWorld = CellToWorld(L.center.x, L.center.y, L, cs);
        var player = Build25DSpike.BuildPlayer(centerWorld);
        Build25DSpike.BuildCamera(rendererIndex, player);
        int mobs = Build25DSpike.BuildMonsters(centerWorld, Mathf.Max(2f, cs * 2f));
        Build25DSpike.BuildCharacter();
        Build25DSpike.BuildHUD();
        if (player != null) Build25DSpike.BuildCursor(player);

        // Tiered terrain: make ramps walkable but cliffs walls, and drop every unit onto the surface
        // (the plaza is flattened to tier 0, but mobs ring outward onto higher tiers). Same as TerrainSandbox.
        if (_tiered)
        {
            Physics.SyncTransforms();
            LayerMask groundMask = 1 << groundLayer;
            foreach (var cc in Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            {
                cc.slopeLimit = 45f;                       // ramps are walkable…
                cc.stepOffset = 0.1f;                      // …but cliffs (>> this) are walls
                Terrain3DField.SnapToGround(cc.transform, groundMask, Build25DSpike.FootOffsetY);
            }
        }

        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var open = SceneManager.GetSceneByPath(ScenePath);
        if (open.IsValid() && open.isLoaded && open != scene) EditorSceneManager.CloseScene(open, true);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.playModeStartScene = null;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        Debug.Log($"[unwritten] 3D town generated → {ScenePath}: {Width}×{Height}, seed {recipe.seed}, " +
                  $"cell {cs:F2}u, {props} props, {mobs} mobs. Open & press Play.");
        EditorUtility.DisplayDialog("3D Town — generated",
            $"Built {ScenePath}\n\n" +
            $"• {Width}×{Height} cells (seed {recipe.seed})\n• {props} props placed (houses/trees/…)\n" +
            $"• {mobs} mobs, billboard player at the plaza\n\n" +
            "Press Play. LEFT-click to move; the player routes around houses. Re-run for a new seed.\n" +
            "Tune the prefab palette in " + ThemePath + ".", "OK");
    }

    // ---- layout bridge -------------------------------------------------------

    // Build a transient recipe whose theme is a MARKER TownTheme: sentinel (non-null) tiles so the
    // generator marks ground/road/boundary cells, and one synthetic PropTemplate per Prop3D so its
    // placement logic runs. We map each synthetic template back to its Prop3D for emit.
    static MapRecipe BuildRecipe(Town3DTheme t3d, Dictionary<PropTemplate, Prop3D> map)
    {
        var sentinel = ScriptableObject.CreateInstance<Tile>();   // any non-null TileBase = "this cell is set"

        var theme = ScriptableObject.CreateInstance<TownTheme>();
        theme.groundTiles.Add(sentinel);
        theme.roadTiles.Add(sentinel);
        theme.boundaryTiles.Add(sentinel);
        theme.decorTiles.Add(sentinel);
        theme.pathTile = sentinel;

        foreach (var p in t3d.props)
        {
            if (p == null || p.prefab == null) continue;
            var pt = ScriptableObject.CreateInstance<PropTemplate>();
            pt.placement = p.placement;
            pt.weight = Mathf.Max(0f, p.weight);
            pt.size = new Vector2Int(Mathf.Max(1, p.size.x), Mathf.Max(1, p.size.y));
            pt.anchorCell = (p.anchorCell == Vector2Int.zero) ? new Vector2Int(pt.size.x / 2, 0) : p.anchorCell;
            pt.collisionMode = PropCollision.Auto;
            // Fill the whole footprint with the sentinel on a layer named by `kind`, so Stamp marks
            // it solid/tree/decor exactly as a real 2D prop would.
            string layer = string.IsNullOrEmpty(p.kind) ? "building" : p.kind;
            for (int x = 0; x < pt.size.x; x++)
                for (int y = 0; y < pt.size.y; y++)
                    pt.cells.Add(new PropTemplate.Cell { layer = layer, pos = new Vector2Int(x, y), tile = sentinel });
            theme.props.Add(pt);
            map[pt] = p;
        }

        var recipe = ScriptableObject.CreateInstance<MapRecipe>();
        recipe.mapName = "Town3D";
        recipe.seed = Random.Range(1, 999999);
        recipe.theme = theme;
        recipe.width = Width; recipe.height = Height;
        recipe.buildingDensity = 0.5f; recipe.treeDensity = 0.4f;
        recipe.roadCount = 4; recipe.roadWidth = 2; recipe.roadWander = 2.5f;
        recipe.openCenter = true; recipe.openCenterSize = 3;
        // Scenic terrain forms the boundary; otherwise a solid fence ring. (Props stay off the edge
        // either way via the placement margins.)
        recipe.boundary = t3d.scenicBorder ? BoundaryStyle.None : BoundaryStyle.Fence;
        recipe.exitCount = 2;
        recipe.terrainPatchCount = 0;              // RuleTile patches are 2D-only
        recipe.roadDecor = 0f; recipe.fieldDecor = 0f;
        recipe.npcSpawnCount = 0; recipe.monsterSpawnerCount = 0;
        recipe.guaranteeConnectivity = true;
        return recipe;
    }

    // ---- 3D emit -------------------------------------------------------------

    static void EmitTerrain(TownLayout L, Town3DTheme t, float cs, int groundLayer, Transform root)
    {
        if (_tiered) { EmitTieredInterior(L, t, cs, groundLayer, root); return; }

        var roads = new GameObject("Roads").transform; roads.SetParent(root);
        float roadDrop = t.roadPrefab != null ? -TopY(t.roadPrefab) : 0f;

        if (t.tiledGround)
        {
            // Chunky per-cell blocks (Kenney platform style). Tops dome + edges show — best for raised
            // terrain, not a flat lawn. A hidden floor box handles collision.
            var ground = new GameObject("Ground").transform; ground.SetParent(root);
            float groundDrop = -TopY(t.groundPrefab);
            for (int y = 0; y < L.height; y++)
                for (int x = 0; x < L.width; x++)
                {
                    Vector3 w = CellToWorld(x, y, L, cs);
                    if (t.roadPrefab != null && L.road[L.Idx(x, y)] != null)
                        PlaceVisual(t.roadPrefab, new Vector3(w.x, roadDrop + 0.01f, w.z), Quaternion.identity, roads);
                    else
                        PlaceVisual(t.groundPrefab, new Vector3(w.x, groundDrop, w.z), Quaternion.identity, ground);
                }
            BuildFloor(L, cs, groundLayer);
            return;
        }

        // Flat ground: ONE grass plane = the visual + the walk-collider (no per-cell pillows / gaps).
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10×10u at scale 1
        plane.name = "Ground"; plane.layer = groundLayer; plane.transform.SetParent(root, true);
        plane.transform.localScale = new Vector3(L.width * cs / 10f, 1f, L.height * cs / 10f);
        plane.transform.position = Vector3.zero;                       // plane surface at y=0
        var tmat = Terrain3DArt.Material();   // grass tiles, same as the hills, so the floor matches
        if (tmat != null) plane.GetComponent<MeshRenderer>().sharedMaterial = tmat;
        else
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.36f, 0.55f, 0.27f) };
            m.SetFloat("_Smoothness", 0f);
            plane.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        // Roads = flat tiles sitting just above the grass plane (they read well, per the first render).
        if (t.roadPrefab != null)
            for (int y = 0; y < L.height; y++)
                for (int x = 0; x < L.width; x++)
                    if (L.road[L.Idx(x, y)] != null)
                    {
                        Vector3 w = CellToWorld(x, y, L, cs);
                        PlaceVisual(t.roadPrefab, new Vector3(w.x, roadDrop + 0.02f, w.z), Quaternion.identity, roads);
                    }
    }

    // Level the ground UNDER each house (footprint + a 1-cell apron) to a single tier, so multi-cell
    // buildings sit flush on a flat pad with a walkable ring (where the ramp lands). The plaza is
    // flattened to tier 0 so the player spawns level. Scatter props (trees/decor) follow the raw terrain.
    static void FlattenPads(TownLayout L, Town3DTheme t, Dictionary<PropTemplate, Prop3D> map)
    {
        int W = L.width, H = L.height;
        int Cx(int v) => Mathf.Clamp(v, 0, W - 1);
        int Cy(int v) => Mathf.Clamp(v, 0, H - 1);

        // Open centre → tier 0 (flat spawn plaza).
        int r = Mathf.Max(1, t.tieredInterior ? 2 : 0);
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = L.center.x + dx, y = L.center.y + dy;
                if (x >= 0 && y >= 0 && x < W && y < H) _tier[y * W + x] = 0;
            }

        // Each building footprint (+1-cell apron) → one tier.
        foreach (var placed in L.placed)
        {
            if (!map.TryGetValue(placed.template, out var p) || p == null) continue;
            string kind = (p.kind ?? "").ToLowerInvariant();
            if (kind.Contains("tree") || kind.Contains("bush") || kind.Contains("decor")) continue;

            var sz = placed.template.size;
            int x0 = placed.origin.x, y0 = placed.origin.y;
            int T = _tier[Cy(y0) * W + Cx(x0)];
            if (t.biasHousesHigh)   // sit the house on the HIGHEST part it touches (favours high ground, no >1 gaps)
                for (int yy = 0; yy < sz.y; yy++)
                    for (int xx = 0; xx < sz.x; xx++)
                        T = Mathf.Max(T, _tier[Cy(y0 + yy) * W + Cx(x0 + xx)]);

            for (int yy = -1; yy <= sz.y; yy++)
                for (int xx = -1; xx <= sz.x; xx++)
                {
                    int cx = x0 + xx, cy = y0 + yy;
                    if (cx >= 0 && cy >= 0 && cx < W && cy < H) _tier[cy * W + cx] = T;
                }
        }
    }

    // Multi-level interior: one merged grass block + flat collider per cell at its tier top, ramps on a
    // spanning tree so every plateau is reachable, roads laid on the surface. Mirrors TerrainSandbox via
    // the shared Terrain3DField helpers.
    static void EmitTieredInterior(TownLayout L, Town3DTheme t, float cs, int groundLayer, Transform root)
    {
        var block = t.groundPrefab;
        var b = Terrain3DField.MeasureBounds(block);
        var ground  = new GameObject("Ground").transform;           ground.SetParent(root);
        var ramps   = new GameObject("Ramps").transform;            ramps.SetParent(root);
        var collide = new GameObject("TerrainCollision").transform; collide.SetParent(root);

        for (int y = 0; y < L.height; y++)
            for (int x = 0; x < L.width; x++)
            {
                float top = InteriorTopY(x, y);
                Vector3 w = CellToWorld(x, y, L, cs);
                Terrain3DField.EmitBlock(block, b, ground, w, top, t.interiorBaseSlab, cs);
                Terrain3DField.EmitFlatCollider(collide, w, top, cs, groundLayer);
            }

        foreach (var kv in Terrain3DField.PlanRamps(_tier, L.width, L.height))
            Terrain3DField.EmitRamp(block, b, ramps, CellToWorld(kv.Key.x, kv.Key.y, L, cs),
                                    InteriorTopY(kv.Key.x, kv.Key.y), kv.Value, t.interiorTierStep, cs, groundLayer);

        if (t.roadPrefab != null)
        {
            var roads = new GameObject("Roads").transform; roads.SetParent(root);
            float roadDrop = -TopY(t.roadPrefab);
            for (int y = 0; y < L.height; y++)
                for (int x = 0; x < L.width; x++)
                    if (L.road[L.Idx(x, y)] != null)
                    {
                        Vector3 w = CellToWorld(x, y, L, cs);
                        PlaceVisual(t.roadPrefab, new Vector3(w.x, InteriorTopY(x, y) + roadDrop + 0.02f, w.z),
                                    Quaternion.identity, roads);
                    }
        }
    }

    // Edge-ring solids = the boundary (props never sit on the extreme edge). Place the boundary prefab
    // there with a blocking box collider.
    static void EmitBoundary(TownLayout L, Town3DTheme t, float cs, int groundLayer, Transform root)
    {
        if (t.boundaryPrefab == null) return;
        var parent = new GameObject("Boundary").transform; parent.SetParent(root);
        var b = MeasureBounds(t.boundaryPrefab);
        float drop = -b.min.y;

        void TryEdge(int x, int y)
        {
            var c = new Vector2Int(x, y);
            bool solid = L.building.ContainsKey(c) || L.tree[L.Idx(x, y)] != null;
            if (!solid) return;
            Vector3 w = CellToWorld(x, y, L, cs);
            var go = PlaceVisual(t.boundaryPrefab, new Vector3(w.x, drop, w.z), Quaternion.identity, parent);
            AddBox(go, new Vector3(b.center.x, b.center.y, b.center.z), new Vector3(cs, Mathf.Max(b.size.y, 1f), cs));
        }
        for (int x = 0; x < L.width; x++) { TryEdge(x, 0); TryEdge(x, L.height - 1); }
        for (int y = 0; y < L.height; y++) { TryEdge(0, y); TryEdge(L.width - 1, y); }
    }

    // Tiered Kenney hills ringing the flat town: tier rises with distance OUTSIDE the town rectangle
    // (+ Perlin), so the town sits in a flat bowl walled by terrain. Tier 0 = y0 (flush with the town
    // plane); higher tiers are cliffs the player can't climb → the natural map boundary. Same overlap
    // trick as TerrainSandbox so the lawn merges. No ramps (scenery). Reuses the ground prefab.
    static void EmitScenicBorder(TownLayout L, Town3DTheme t, float cs, int groundLayer, Transform root)
    {
        var block = t.groundPrefab;
        var b = MeasureBounds(block);
        float fx = Mathf.Max(0.01f, b.size.x), fy = Mathf.Max(0.01f, b.size.y), fz = Mathf.Max(0.01f, b.size.z);
        var parent = new GameObject("ScenicTerrain").transform; parent.SetParent(root);
        float nox = Random.Range(0f, 100f), noz = Random.Range(0f, 100f);
        int BC = ScenicBorderCells;

        for (int y = -BC; y < L.height + BC; y++)
            for (int x = -BC; x < L.width + BC; x++)
            {
                if (x >= 0 && x < L.width && y >= 0 && y < L.height) continue;   // town interior stays flat

                int distOut = Mathf.Max(Mathf.Max(-x, x - (L.width - 1)), Mathf.Max(-y, y - (L.height - 1)));
                float nz = Mathf.PerlinNoise(nox + x * ScenicNoise, noz + y * ScenicNoise);
                int tier = Mathf.Clamp(Mathf.FloorToInt((distOut - 1) * ScenicRise + nz * ScenicNoiseAmp), 0, ScenicMaxTier);
                float top = tier * ScenicTierStep;           // tier 0 == town level (y=0)
                float height = ScenicBaseSlab + top;
                Vector3 w = CellToWorld(x, y, L, cs);

                var go = (GameObject)PrefabUtility.InstantiatePrefab(block);
                go.transform.SetParent(parent, true);
                go.transform.localScale = new Vector3(cs / fx * TerrainOverlap, height / fy, cs / fz * TerrainOverlap);
                go.transform.position = new Vector3(w.x, -ScenicBaseSlab, w.z);   // base buried, top at `top`
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                Terrain3DArt.Apply(go);   // paint hills with the 2D grass/dirt tiles
                GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);

                // Flat walk/stand collider at the tier top (Ground layer). Tier 0 is a walkable apron;
                // higher tiers are cliffs (player stepOffset can't climb) = the boundary.
                var col = new GameObject("c"); col.transform.SetParent(parent, true);
                col.transform.position = new Vector3(w.x, top - 0.1f, w.z); col.layer = groundLayer;
                col.AddComponent<BoxCollider>().size = new Vector3(cs, 0.2f, cs);
                GameObjectUtility.SetStaticEditorFlags(col, (StaticEditorFlags)~0);
            }
    }

    static int EmitProps(TownLayout L, Town3DTheme t, Dictionary<PropTemplate, Prop3D> map, float cs, Transform root)
    {
        var parent = new GameObject("Props").transform; parent.SetParent(root);
        int n = 0;
        foreach (var placed in L.placed)
        {
            if (!map.TryGetValue(placed.template, out var p) || p.prefab == null) continue;

            // Footprint-centre cell → world; base on y=0.
            float cx = placed.origin.x + (p.size.x - 1) * 0.5f;
            float cz = placed.origin.y + (p.size.y - 1) * 0.5f;
            Vector3 w = CellToWorldF(cx, cz, L, cs);
            var b = MeasureBounds(p.prefab);
            float topY = InteriorTopY(placed.origin.x, placed.origin.y);   // 0 when flat; the pad/cell tier when tiered

            string kind = (p.kind ?? "").ToLowerInvariant();
            bool isTree = kind.Contains("tree") || kind.Contains("bush");
            // Houses keep door-facing (-Z); scatter props get a random quarter-turn for variety.
            Quaternion rot = (p.placement == PropPlacement.Lot) ? Quaternion.identity
                                                                : Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f);
            var go = PlaceVisual(p.prefab, new Vector3(w.x, topY - b.min.y, w.z), rot, parent);

            if (kind.Contains("decor")) { /* walkable, no collider */ }
            else if (isTree)
                AddBox(go, new Vector3(b.center.x, b.center.y, b.center.z), new Vector3(cs * 0.4f, Mathf.Max(b.size.y, 1f), cs * 0.4f));
            else   // building / wall / default
                AddBox(go, new Vector3(b.center.x, b.center.y, b.center.z),
                       new Vector3(p.size.x * cs * 0.95f, Mathf.Max(b.size.y, 1f), p.size.y * cs * 0.95f));
            n++;
        }
        return n;
    }

    // One big flat collider on the Ground layer = the walk surface (visual ground tiles stay collider-free).
    static void BuildFloor(TownLayout L, float cs, int groundLayer)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "GroundCollider";
        floor.layer = groundLayer;
        floor.transform.localScale = new Vector3(L.width * cs, 0.2f, L.height * cs);
        floor.transform.position = new Vector3(0f, -0.1f, 0f);   // top face at y=0
        floor.GetComponent<MeshRenderer>().enabled = false;       // invisible — the grass tiles are the look
    }

    static void BuildPathfinderConfig(int groundLayer, Town3DTheme t)
    {
        var cfg = new GameObject("Pathfinder3DConfig").AddComponent<Pathfinder3DConfig>();
        cfg.heightAware = true;
        cfg.groundMask = 1 << groundLayer;
        // Ramp half-steps pass, full cliffs block. Tiered interior gates on its own tier rise.
        cfg.maxStep = t.tieredInterior ? t.interiorTierStep * 0.75f : 0.6f;
    }

    // ---- placement / measurement helpers -------------------------------------

    static GameObject PlaceVisual(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, rot);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);  // collision is handled separately
        GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
        return go;
    }

    static void AddBox(GameObject go, Vector3 center, Vector3 size)
    {
        var bc = go.AddComponent<BoxCollider>();
        bc.center = center;
        bc.size = size;
    }

    static Vector3 CellToWorld(int x, int y, TownLayout L, float cs) => CellToWorldF(x, y, L, cs);
    static Vector3 CellToWorldF(float x, float y, TownLayout L, float cs)
        => new Vector3((x - L.width * 0.5f + 0.5f) * cs, 0f, (y - L.height * 0.5f + 0.5f) * cs);

    static readonly Dictionary<GameObject, Bounds> _bcache = new Dictionary<GameObject, Bounds>();
    static Bounds MeasureBounds(GameObject prefab)
    {
        if (_bcache.TryGetValue(prefab, out var cached)) return cached;
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        tmp.transform.position = Vector3.zero; tmp.transform.rotation = Quaternion.identity;
        var rends = tmp.GetComponentsInChildren<Renderer>();
        Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(Vector3.zero, Vector3.one);
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        Object.DestroyImmediate(tmp);
        _bcache[prefab] = b;
        return b;
    }

    static float FootprintXZ(GameObject prefab) { var b = MeasureBounds(prefab); return Mathf.Max(b.size.x, b.size.z); }
    static float TopY(GameObject prefab) => MeasureBounds(prefab).max.y;

    // ---- theme resolution / auto-default -------------------------------------

    static Town3DTheme ResolveTheme()
    {
        if (Selection.activeObject is Town3DTheme sel) return sel;
        var existing = AssetDatabase.LoadAssetAtPath<Town3DTheme>(ThemePath);
        if (existing != null) return existing;
        foreach (var g in AssetDatabase.FindAssets("t:Town3DTheme"))
            return AssetDatabase.LoadAssetAtPath<Town3DTheme>(AssetDatabase.GUIDToAssetPath(g));
        return CreateDefaultTheme();
    }

    // Auto-wire a starter theme from the baked prefabs so the user doesn't hand-assign anything.
    static Town3DTheme CreateDefaultTheme()
    {
        GameObject P(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
        const string plat = "Assets/Prefabs/KenneyPlatformer/";
        const string town = "Assets/Prefabs/KenneyFantasyTown/";

        var ground = P(plat + "block-grass.prefab");
        if (ground == null)
        { Fail("No baked prefabs found. Run Tools ▸ unwritten ▸ Build Prefabs ▸ Both Kits (and Assemble House) first."); return null; }

        var t = ScriptableObject.CreateInstance<Town3DTheme>();
        t.groundPrefab = ground;
        t.roadPrefab = P(town + "road.prefab");           // null-safe (roads fall back to ground)
        t.boundaryPrefab = P(town + "fence.prefab");

        void Add(string path, Vector2Int size, PropPlacement place, string kind, float weight)
        { var pf = P(path); if (pf != null) t.props.Add(new Prop3D { prefab = pf, size = size, placement = place, kind = kind, weight = weight }); }

        Add(town + "house-small.prefab", new Vector2Int(3, 3), PropPlacement.Lot, "building", 1f);
        Add(town + "tree.prefab",        new Vector2Int(1, 1), PropPlacement.OpenGround, "tree", 1f);
        Add(town + "tree-high.prefab",   new Vector2Int(1, 1), PropPlacement.OpenGround, "tree", 0.6f);
        Add(town + "fountain-round.prefab", new Vector2Int(2, 2), PropPlacement.PlazaCenter, "building", 1f);
        Add(plat + "barrel.prefab",      new Vector2Int(1, 1), PropPlacement.Roadside, "decor", 1f);

        if (!AssetDatabase.IsValidFolder("Assets/MapGen")) AssetDatabase.CreateFolder("Assets", "MapGen");
        AssetDatabase.CreateAsset(t, ThemePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[unwritten] Created default Town3DTheme at " + ThemePath + " — edit it to swap prefabs / add props.");
        return t;
    }

    static void Fail(string msg)
    {
        EditorUtility.DisplayDialog("3D Town", msg, "OK");
        Debug.LogError("[unwritten] 3D Town: " + msg);
    }
}
