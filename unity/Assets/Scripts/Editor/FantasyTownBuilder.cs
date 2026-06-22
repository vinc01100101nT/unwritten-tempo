using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click <b>furnished</b> fantasy town built from the Pxltgr prefabs in <c>Assets/Prefabs/Props</c>
/// (the ones <see cref="FbxPropExtractor"/> sliced out of the single Assets.fbx). It lays a tiled ground
/// plot from the pack's <c>ground_*</c> tiles, lines a central street with houses, scatters trees / decor,
/// and wires the whole travel loop to the field — Town ↔ NatureStage via <see cref="Portal3D"/> — in one go.
///
/// Props (houses/trees/decor) are placed at <b>×0.3</b> (the scale the artist confirmed matches our unit /
/// player size). Ground tiles get their OWN scale, sized to <see cref="GroundTileTarget"/> world units, so
/// a few big tiles cover the plot instead of thousands of tiny ones.
///
/// PLACEMENT IS BOUNDS-DRIVEN: every prop is positioned by its measured world bounds (footprint centre to
/// the target XZ, bottom of bounds onto the ground), NOT by its pivot — the extracted meshes have their
/// origin offset from the geometry, so pivot-based placement floated them. Spacing uses measured widths so
/// houses never overlap.
///
/// Ground tiles are placed collider-free (they GPU-instance) over ONE flat floor collider on the Ground
/// layer (cheap; see PERFORMANCE.md). Houses/trees/carts keep their MeshColliders so they block movement
/// and routing (the pathfinder treats anything off the Ground layer as a wall); small decor (flowers,
/// grass…) is stripped of colliders. Safe to re-run (overwrites Town.unity).
/// </summary>
public static class FantasyTownBuilder
{
    const string PropsDir       = "Assets/Prefabs/Props";
    const string TownScenePath  = "Assets/Scenes/Town.unity";
    const string FieldScenePath = "Assets/Scenes/NatureStage.unity";
    const string FieldScene     = "NatureStage";

    const float Scale            = 0.3f;   // prop scale that matches our units (per the artist)
    const float GroundTileTarget = 2.5f;   // desired ground-tile size in world units (decoupled from prop scale)
    const float PlotX            = 34f;    // plot size (world units)
    const float PlotZ            = 24f;
    const int   MaxTiles         = 900;    // safety budget on ground-tile count
    const float RoadHalf         = 1.6f;   // half-width of the central paved street
    const int   Seed             = 73;
    // If houses end up facing AWAY from the street, flip this to false and re-run.
    const bool  HouseFrontIsPlusZ = true;

    [MenuItem("Tools/unwritten/World/Build Fantasy Town (houses + grounds)", priority = 221)]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Fantasy Town", "Exit Play mode first, then re-run.", "OK"); return; }

        var grassSrc = Prefab("ground_grass");
        if (grassSrc == null)
        { EditorUtility.DisplayDialog("Fantasy Town", $"No ground tiles found in {PropsDir} (expected 'ground_grass').\nRun Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first.", "OK"); return; }

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Fantasy Town", "No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers.", "OK"); return; }
        int groundMask = 1 << groundLayer;

        var rng = new System.Random(Seed);
        var blockers = new List<(Vector2 c, float r)>();   // building footprints decor must avoid

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        Build25DSpike.BuildLight();   // authoring Sun (disabled at Play; Systems owns the runtime light)

        // ── GROUND ────────────────────────────────────────────────────────────────────────────────────
        // Size the ground tiles to GroundTileTarget world units regardless of their native mesh size, so a
        // handful of tiles cover the plot (a tile at the ×0.3 prop scale would be tiny → thousands of them).
        var nativeGrass = MeasureScaled(grassSrc, 1f);
        float groundScale = GroundTileTarget / Mathf.Max(0.01f, nativeGrass.size.x, nativeGrass.size.z);

        var groundRoot = new GameObject("Ground_Town");
        var gb = MeasureScaled(grassSrc, groundScale);
        float tileW = Mathf.Max(0.05f, gb.size.x), tileD = Mathf.Max(0.05f, gb.size.z);
        float topY = gb.max.y, offX = gb.center.x, offZ = gb.center.z;   // pivot→bounds offsets at this scale

        int cols = Mathf.Max(4, Mathf.CeilToInt(PlotX / tileW));
        int rows = Mathf.Max(4, Mathf.CeilToInt(PlotZ / tileD));
        if ((long)cols * rows > MaxTiles)            // budget guard (shouldn't bind with big tiles)
        {
            float k = Mathf.Sqrt(MaxTiles / (float)(cols * rows));
            cols = Mathf.Max(4, Mathf.FloorToInt(cols * k));
            rows = Mathf.Max(4, Mathf.FloorToInt(rows * k));
        }
        float plotX = cols * tileW, plotZ = rows * tileD;

        var road = Prefab("ground_road"); var soil = Prefab("ground_soil"); var stones = Prefab("ground_stones");
        float startX = -plotX * 0.5f + tileW * 0.5f, startZ = -plotZ * 0.5f + tileD * 0.5f;
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                float cx = startX + i * tileW, cz = startZ + j * tileD;
                GameObject src = grassSrc;
                if (road != null && Mathf.Abs(cz) <= RoadHalf) src = road;          // central street
                else if (soil != null && rng.NextDouble() < 0.07) src = soil;        // dirt patches
                else if (stones != null && rng.NextDouble() < 0.04) src = stones;
                var tile = (GameObject)PrefabUtility.InstantiatePrefab(src);
                tile.transform.localScale = Vector3.one * groundScale;
                tile.transform.position = new Vector3(cx - offX, -topY, cz - offZ);  // bounds centre → grid, top → y=0
                foreach (var col in tile.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                tile.transform.SetParent(groundRoot.transform, true);
            }
        WorldBuildUtil.SetLayerRecursive(groundRoot, groundLayer);

        // One flat floor collider on the Ground layer = the walkable surface (player snap + path ground rays).
        var floor = new GameObject("Ground_Floor") { layer = groundLayer };
        floor.transform.SetParent(groundRoot.transform, false);
        var fbox = floor.AddComponent<BoxCollider>();
        fbox.center = new Vector3(0f, -0.5f, 0f);                 // top at y=0 (tile surface)
        fbox.size = new Vector3(plotX + 2f, 1f, plotZ + 2f);

        var cfg = new GameObject("Pathfinder3DConfig").AddComponent<Pathfinder3DConfig>();
        cfg.heightAware = true; cfg.groundMask = groundMask; cfg.maxStep = 0.6f;   // obstacles = everything off Ground

        Physics.SyncTransforms();   // so the surface raycasts hit the floor we just made

        // ── HOUSES along the street ────────────────────────────────────────────────────────────────────
        var buildings = new GameObject("Buildings");
        var houses = Load("WoodenHouse_1_LOD0", "WoodenHouse_2_LOD0", "WoodenHouse_3_LOD0", "House1", "House2", "House3");
        int placedHouses = 0;
        if (houses.Count > 0)
        {
            float maxW = 0f, maxD = 0f;
            var foot = new List<Vector2>();
            foreach (var h in houses) { var b = MeasureScaled(h, Scale); foot.Add(new Vector2(b.size.x, b.size.z)); maxW = Mathf.Max(maxW, b.size.x); maxD = Mathf.Max(maxD, b.size.z); }

            float rowZ = RoadHalf + 0.7f + maxD * 0.5f;          // house CENTRE — clears the road by half its depth
            float spacing = maxW + 1.0f;                          // centre-to-centre, by measured width → no overlap
            float xFrom = -plotX * 0.5f + maxW * 0.5f + 0.8f;
            float xTo   =  plotX * 0.5f - maxW * 0.5f - 2.5f;     // leave the east end open for the gate/portal
            int perRow = Mathf.Max(1, Mathf.FloorToInt((xTo - xFrom) / spacing) + 1);
            int hi = 0;
            for (int s = 0; s < perRow; s++)
            {
                float x = xFrom + s * spacing;
                int si = hi % houses.Count;
                PlaceProp(buildings.transform, houses[si], x, -rowZ, HouseFrontIsPlusZ ? 0f : 180f, groundMask, Scale, true);
                blockers.Add((new Vector2(x, -rowZ), 0.5f * Mathf.Max(foot[si].x, foot[si].y) + 0.5f));
                hi++;
                int ni = hi % houses.Count;
                PlaceProp(buildings.transform, houses[ni], x, rowZ, HouseFrontIsPlusZ ? 180f : 0f, groundMask, Scale, true);
                blockers.Add((new Vector2(x, rowZ), 0.5f * Mathf.Max(foot[ni].x, foot[ni].y) + 0.5f));
                hi++;
                placedHouses += 2;
            }
        }

        // ── LANDMARKS (Tavern + Windmill) toward the back ───────────────────────────────────────────────
        float backZ = plotZ * 0.5f - 3.0f;
        PlaceLandmark(buildings.transform, Prefab("Tavern_LOD0"),   -plotX * 0.32f, -backZ, groundMask, blockers);
        PlaceLandmark(buildings.transform, Prefab("Windmill_LOD0"),  plotX * 0.32f,  backZ, groundMask, blockers);

        // ── DECOR ───────────────────────────────────────────────────────────────────────────────────────
        var decor = new GameObject("Decor");
        var trees = Load("Oak_tree1", "Oak_tree2", "Oak_tree3", "Birch_tree1", "Birch_tree2", "Birch_tree3",
                         "Pine_tree1", "Pine_tree2", "Pine_tree3", "Deciduous_tree1", "Deciduous_tree2", "Willow_tree1");
        var cover = Load("Flower1", "Flower2", "Flower3", "Flower4", "Flower5", "Flower6", "Flower7", "Flower8", "Flower9",
                         "Grass1", "Grass2", "Grass3", "Grass4", "Mushroom1", "Mushroom2", "Mushroom3", "Fern1", "Fern2",
                         "Sunflower", "Rye");
        var rocks = Load("Rock1", "Rock2", "Rock3", "Stone1_LOD0", "Stone2_LOD0", "Stone3_LOD0");
        var carts = Load("cart1_LOD0", "cart2_LOD0", "cart3_LOD0", "cart4_LOD0");

        // Tree ring just inside the N / S / W edges (E stays open for the gate).
        if (trees.Count > 0)
        {
            float ez = plotZ * 0.5f - 1.2f, ex = plotX * 0.5f - 1.2f;
            for (float x = -ex; x <= ex; x += 2.6f)
            {
                TryDecor(decor.transform, trees[rng.Next(trees.Count)], x + Jit(rng, 0.4f),  ez + Jit(rng, 0.4f), groundMask, blockers, true, 0.9f);
                TryDecor(decor.transform, trees[rng.Next(trees.Count)], x + Jit(rng, 0.4f), -ez + Jit(rng, 0.4f), groundMask, blockers, true, 0.9f);
            }
            for (float z = -ez + 2.0f; z <= ez - 2.0f; z += 2.6f)
                TryDecor(decor.transform, trees[rng.Next(trees.Count)], -ex + Jit(rng, 0.4f), z + Jit(rng, 0.4f), groundMask, blockers, true, 0.9f);
        }

        // Carts as roadside market stalls near the street centre.
        for (int i = 0; i < carts.Count && i < 3; i++)
            TryDecor(decor.transform, carts[i], -4f + i * 4f, (i % 2 == 0 ? 1f : -1f) * (RoadHalf + 0.9f), groundMask, blockers, true, 0.7f);

        // Loose cover (flowers/grass/mushrooms) sprinkled off the road, away from buildings.
        if (cover.Count > 0)
            for (int n = 0; n < 90; n++)
            {
                float x = (float)(rng.NextDouble() - 0.5) * plotX, z = (float)(rng.NextDouble() - 0.5) * plotZ;
                TryDecor(decor.transform, cover[rng.Next(cover.Count)], x, z, groundMask, blockers, false, 0.35f);
            }
        // A few rocks for texture.
        if (rocks.Count > 0)
            for (int n = 0; n < 10; n++)
            {
                float x = (float)(rng.NextDouble() - 0.5) * plotX, z = (float)(rng.NextDouble() - 0.5) * plotZ;
                TryDecor(decor.transform, rocks[rng.Next(rocks.Count)], x, z, groundMask, blockers, true, 0.5f);
            }

        // ── SPAWNS + PORTAL TO FIELD (east gate) ────────────────────────────────────────────────────────
        float gateX = plotX * 0.5f - 1.2f;
        RegionBuilder.MakeSpawn(scene, "Start",     new Vector3(0f, SurfaceY(0f, 0f, groundMask), 0f));
        RegionBuilder.MakeSpawn(scene, "FromField", new Vector3(gateX - 3f, SurfaceY(gateX - 3f, 0f, groundMask), 0f));
        RegionBuilder.MakePortal(scene, "Portal_ToField", new Vector3(gateX, SurfaceY(gateX, 0f, groundMask), 0f),
                                 FieldScene, "FromTown", new Color(0.4f, 0.8f, 1f));

        // ── SAVE + REGISTER + AUTO-LINK THE FIELD ───────────────────────────────────────────────────────
        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var open = SceneManager.GetSceneByPath(TownScenePath);
        if (open.IsValid() && open.isLoaded && open != scene) EditorSceneManager.CloseScene(open, true);
        EditorSceneManager.SaveScene(scene, TownScenePath);
        WorldBuildUtil.RegisterInBuildSettings(TownScenePath);

        bool linked = false;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(FieldScenePath) != null)
        {
            var field = EditorSceneManager.OpenScene(FieldScenePath, OpenSceneMode.Single);
            RegionBuilder.LinkFieldBack(field);
            EditorSceneManager.SaveScene(field);
            WorldBuildUtil.RegisterInBuildSettings(FieldScenePath);
            linked = true;
        }
        EditorSceneManager.OpenScene(TownScenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        Debug.Log($"[unwritten] Fantasy Town built → {TownScenePath} ({cols}×{rows} tiles @{groundScale:0.00}, {placedHouses} houses, link={linked}).");
        EditorUtility.DisplayDialog("Fantasy Town — built",
            $"Built {TownScenePath} from the Pxltgr pack:\n" +
            $"• {cols}×{rows} ground tiles (≈{GroundTileTarget}u each) on one flat floor collider\n" +
            $"• {placedHouses} houses (×{Scale}) lining the street + Tavern & Windmill\n" +
            "• tree ring, market carts, scattered flowers/grass/rocks\n" +
            "• Spawns 'Start' + 'FromField' and a cyan portal (east gate) → NatureStage\n" +
            (linked ? "• NatureStage auto-linked back (gold portal → Town)\n" : "• (NatureStage not found — build it to complete the loop)\n") +
            "\nProps are placed by measured bounds (feet on the ground, no overlap).\n\n" +
            "PLAY: open Town and press Play. If houses face AWAY from the street, flip HouseFrontIsPlusZ in " +
            "FantasyTownBuilder.cs and re-run. Nudge anything by hand, then Region ▸ Drop Selected to Ground.", "OK");
    }

    // ── placement helpers ────────────────────────────────────────────────────────────────────────────
    /// <summary>Place a prop so its measured footprint CENTRE sits at (x,z) and the BOTTOM of its bounds
    /// rests on the ground — robust to the off-pivot meshes the FBX split produced.</summary>
    static GameObject PlaceProp(Transform parent, GameObject prefab, float x, float z, float rotY, int groundMask, float scale, bool solid)
    {
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.localScale = Vector3.one * scale;
        go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
        go.transform.position = Vector3.zero;

        float gy = SurfaceY(x, z, groundMask);
        if (TryWorldBounds(go, out var b))
            go.transform.position = new Vector3(x - b.center.x, gy - b.min.y, z - b.center.z);
        else
            go.transform.position = new Vector3(x, gy, z);

        go.transform.SetParent(parent, true);
        if (!solid)   // decorative cover shouldn't block movement or routing
            foreach (var col in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
        return go;
    }

    static void PlaceLandmark(Transform parent, GameObject prefab, float x, float z, int groundMask, List<(Vector2, float)> blockers)
    {
        if (prefab == null) return;
        var b = MeasureScaled(prefab, Scale);
        PlaceProp(parent, prefab, x, z, 0f, groundMask, Scale, true);
        blockers.Add((new Vector2(x, z), 0.5f * Mathf.Max(b.size.x, b.size.z) + 0.6f));
    }

    // Place a decor prop only if its spot is clear of buildings and off the paved street.
    static void TryDecor(Transform parent, GameObject prefab, float x, float z, int groundMask,
                         List<(Vector2 c, float r)> blockers, bool solid, float radius)
    {
        if (prefab == null) return;
        if (Mathf.Abs(z) <= RoadHalf + 0.4f) return;                    // keep the paved street clear
        var p = new Vector2(x, z);
        foreach (var b in blockers) if (Vector2.Distance(p, b.c) < b.r + radius) return;
        var go = PlaceProp(parent, prefab, x, z, (float)(((int)(x * 53 + z * 17)) % 360), groundMask, Scale, solid);
        if (go != null && solid) blockers.Add((p, radius));
    }

    static float Jit(System.Random rng, float amt) => (float)(rng.NextDouble() * 2 - 1) * amt;

    // ── prefab + measurement utilities ─────────────────────────────────────────────────────────────────
    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{name}.prefab");

    static List<GameObject> Load(params string[] names)
    {
        var list = new List<GameObject>();
        foreach (var n in names) { var go = Prefab(n); if (go != null) list.Add(go); }
        return list;
    }

    // Ground surface Y under (x,z) — raycast onto the floor collider (Ground layer). Falls back to 0.
    static float SurfaceY(float x, float z, int groundMask)
    {
        if (Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out var hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return 0f;
    }

    // Combined world-space renderer bounds of a live instance (true visual footprint, any pivot).
    static bool TryWorldBounds(GameObject go, out Bounds b)
    {
        b = new Bounds(go.transform.position, Vector3.zero);
        bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return has;
    }

    // World-space bounds of a prefab instantiated at the origin and scaled — for tile size / footprints.
    static Bounds MeasureScaled(GameObject prefab, float scale)
    {
        var t = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        t.transform.position = Vector3.zero;
        t.transform.rotation = Quaternion.identity;
        t.transform.localScale = Vector3.one * scale;
        bool has = TryWorldBounds(t, out var b);
        if (!has) b = new Bounds(Vector3.zero, Vector3.one * scale);
        Object.DestroyImmediate(t);
        return b;
    }
}
