using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 4 terrain sandbox — MERGED, TIERED, RAMP-GATED Kenney terrain (dialed in here, then folded
/// into <see cref="Town3DBaker"/>). Builds <b>Assets/Scenes/Terrain25D.unity</b>:
///   • quantised-Perlin heightmap of grass blocks, each scaled to OVERLAP neighbours so the rounded
///     shoulders merge into one lawn (memory <c>kenney-3d-terrain-overlap</c>);
///   • blocks are VISUAL-ONLY; the walk surface is a FLAT box collider per cell at its tier top;
///   • every tier edge is an IMPASSABLE cliff — the ONLY way up is a generated grass RAMP. Ramps are
///     placed on a union-find spanning tree of the tier regions, so every area stays reachable;
///   • player <c>stepOffset</c> is tiny (cliffs are walls) but <c>slopeLimit</c> walks ramps;
///     <see cref="Pathfinder3DConfig"/> routes up via ramps (cliffs exceed maxStep).
/// Standalone — open & press Play, click to walk; you must take a ramp to reach higher ground.
/// </summary>
public static class TerrainSandbox
{
    const string ScenePath = "Assets/Scenes/Terrain25D.unity";
    const string BlockPath = "Assets/Prefabs/KenneyPlatformer/block-grass.prefab";

    // ---- tuning knobs --------------------------------------------------------
    const int   PatchW = 18, PatchD = 18;    // cells
    const int   Tiers = 3;                    // distinct height levels (0..Tiers-1)
    const float TierStep = 0.6f;              // world height per tier (the cliff/ramp rise)
    const float NoiseScale = 0.16f;           // smaller = broader plateaus
    const float BaseSlab = 1.0f;              // height of the lowest tier's block
    const float OverlapScale = 1.3f;          // >1 so neighbours overlap and the dirt grooves close
    const float CliffStep = 0.1f;             // player stepOffset — << TierStep so cliffs are walls
    const float RampThick = 0.6f;             // ramp wedge thickness — body sinks into the hill (hides brown)

    static readonly Vector2Int[] N4 =
        { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    static LayerMask groundMask;
    static int W, H;
    static int[] tier;
    static float fx, fy, fz, cs;

    [MenuItem("Tools/unwritten/Terrain Sandbox", priority = 11)]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Terrain Sandbox", "Exit Play mode first, then re-run.", "OK"); return; }

        var block = AssetDatabase.LoadAssetAtPath<GameObject>(BlockPath);
        if (block == null) { EditorUtility.DisplayDialog("Terrain Sandbox",
            "Missing " + BlockPath + "\nRun Build Prefabs ▸ Platformer Kit first.", "OK"); return; }

        int rendererIndex = Build25DSpike.EnsureUniversalRenderer();
        if (rendererIndex < 0) return;
        int groundLayer = Build25DElevationSpike.EnsureGroundLayer();
        if (groundLayer < 0) { EditorUtility.DisplayDialog("Terrain Sandbox",
            "No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers.", "OK"); return; }
        groundMask = 1 << groundLayer;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        Build25DSpike.BuildLight();

        var b = MeasureBounds(block);
        fx = Mathf.Max(0.01f, b.size.x); fy = Mathf.Max(0.01f, b.size.y); fz = Mathf.Max(0.01f, b.size.z);
        cs = fx;
        W = PatchW; H = PatchD;

        // 1) Heightmap.
        tier = new int[W * H];
        float nox = Random.Range(0f, 100f), noz = Random.Range(0f, 100f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float n = Mathf.PerlinNoise(nox + x * NoiseScale, noz + y * NoiseScale);
                tier[Idx(x, y)] = Mathf.Clamp(Mathf.FloorToInt(n * Tiers), 0, Tiers - 1);
            }

        // 2) Where ramps go (spanning tree over tier regions).
        var ramps = PlanRamps();   // lowCell -> outward dir toward the higher neighbour

        // 3) Emit.
        var visual = new GameObject("Terrain").transform;
        var collide = new GameObject("TerrainCollision").transform;
        float ox = -W * cs * 0.5f, oz = -H * cs * 0.5f;

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float top = TopOf(x, y);
                float wx = ox + (x + 0.5f) * cs, wz = oz + (y + 0.5f) * cs;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(block);
                go.transform.SetParent(visual, true);
                go.transform.localScale = new Vector3(cs / fx * OverlapScale, top / fy, cs / fz * OverlapScale);
                go.transform.position = new Vector3(wx, 0f, wz);
                Strip(go); Terrain3DArt.Apply(go); MakeStatic(go);

                if (ramps.TryGetValue(new Vector2Int(x, y), out var dir))
                    PlaceRamp(block, b, collide, new Vector3(wx, 0f, wz), top, dir, groundLayer);
                else
                    PlaceFlatCollider(collide, new Vector3(wx, top - 0.1f, wz), groundLayer);
            }

        BuildPathfinderConfig();

        var player = Build25DSpike.BuildPlayer(Vector3.zero);
        Build25DSpike.BuildCamera(rendererIndex, player);
        Build25DSpike.BuildCharacter();
        Build25DSpike.BuildHUD();
        if (player != null) Build25DSpike.BuildCursor(player);

        Physics.SyncTransforms();
        foreach (var cc in Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
        {
            cc.slopeLimit = 45f;       // ramps are walkable…
            cc.stepOffset = CliffStep; // …but cliffs (>> this) are walls
            SnapToGround(cc.transform);
        }

        var open = SceneManager.GetSceneByPath(ScenePath);
        if (open.IsValid() && open.isLoaded && open != scene) EditorSceneManager.CloseScene(open, true);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.playModeStartScene = null;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        Debug.Log($"[unwritten] Terrain Sandbox: {W}×{H}, {Tiers} tiers @ {TierStep}u, {ramps.Count} ramps. " +
                  "Press Play — cliffs block; take a ramp to climb.");
        EditorUtility.DisplayDialog("Terrain Sandbox — built",
            $"Built {ScenePath}\n\n{W}×{H}, {Tiers} tiers, {ramps.Count} ramps.\n\n" +
            "Press Play, LEFT-click to walk. You should be BLOCKED by every cliff and only reach higher " +
            "ground by walking a grass ramp (the path auto-routes to one). Knobs at top of the file.\n\n" +
            "If it feels right, I fold this into Generate 3D Town.", "OK");
    }

    // ---- ramp planning (union-find spanning tree over tier regions) ----------

    static Dictionary<Vector2Int, Vector2Int> PlanRamps()
    {
        // Region id per cell (flood fill of equal-tier 4-connected cells).
        var region = new int[W * H];
        for (int i = 0; i < region.Length; i++) region[i] = -1;
        int regionCount = 0;
        var queue = new Queue<Vector2Int>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (region[Idx(x, y)] != -1) continue;
                int id = regionCount++;
                region[Idx(x, y)] = id; queue.Enqueue(new Vector2Int(x, y));
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    foreach (var d in N4)
                    {
                        var nb = c + d;
                        if (!In(nb) || region[Idx(nb)] != -1) continue;
                        if (tier[Idx(nb)] != tier[Idx(c)]) continue;
                        region[Idx(nb)] = id; queue.Enqueue(nb);
                    }
                }
            }

        var uf = new int[regionCount];
        for (int i = 0; i < regionCount; i++) uf[i] = i;
        int Find(int a) { while (uf[a] != a) a = uf[a] = uf[uf[a]]; return a; }

        var ramps = new Dictionary<Vector2Int, Vector2Int>();
        var lowCells = new HashSet<Vector2Int>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var c = new Vector2Int(x, y);
                foreach (var d in N4)
                {
                    var nb = c + d;
                    if (!In(nb)) continue;
                    if (tier[Idx(nb)] != tier[Idx(c)] + 1) continue;   // c is exactly one tier lower than nb
                    int ra = Find(region[Idx(c)]), rb = Find(region[Idx(nb)]);
                    if (ra == rb) continue;
                    if (lowCells.Contains(c)) continue;                // one ramp per cell
                    uf[ra] = rb;
                    ramps[c] = d; lowCells.Add(c);
                }
            }
        return ramps;
    }

    // ---- emit helpers --------------------------------------------------------

    static void PlaceFlatCollider(Transform parent, Vector3 pos, int layer)
    {
        var go = new GameObject("c"); go.transform.SetParent(parent, true);
        go.transform.position = pos; go.layer = layer;
        var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(cs, 0.2f, cs);
        MakeStatic(go);
    }

    // A solid grass WEDGE (same block-grass prefab as the terrain) rising one TierStep toward `dir`.
    // It's oversized by OverlapScale so its grass fuses into the low AND high lawns (no seam/gap), and
    // thick so the body sinks into the hill — only the grass walkway shows, not a floating brown slab.
    static void PlaceRamp(GameObject block, Bounds b, Transform parent, Vector3 lowCenter, float top, Vector2Int dir, int layer)
    {
        float run = cs, rise = TierStep;
        float angleRad = Mathf.Atan2(rise, run);
        float angle = angleRad * Mathf.Rad2Deg, cosA = Mathf.Cos(angleRad);
        float length = Mathf.Sqrt(run * run + rise * rise) * OverlapScale;   // overlap both ends into the lawns
        float width = cs * OverlapScale;                                     // overlap the sides into the cliff

        Vector3 scale; Quaternion rot;
        if (dir.x != 0)   // ramp length along X
        {
            scale = new Vector3(length / fx, RampThick / fy, width / fz);
            rot = Quaternion.Euler(0f, 0f, dir.x > 0 ? angle : -angle);
        }
        else              // ramp length along Z
        {
            scale = new Vector3(width / fx, RampThick / fy, length / fz);
            rot = Quaternion.Euler(dir.y > 0 ? -angle : angle, 0f, 0f);
        }
        // Root sits at the slope's geometric centre (below the slope line by half-thickness so the TOP
        // FACE rides low→high). The block is offset by its bounds centre under the root, so rotation
        // spins it about its centre — NOT its off-base pivot (which made it fling into the air).
        var root = new GameObject("ramp").transform;
        root.SetParent(parent, true);
        root.SetPositionAndRotation(
            new Vector3(lowCenter.x, top + rise * 0.5f - RampThick * 0.5f * cosA, lowCenter.z), rot);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(block);
        go.transform.SetParent(root, false);
        go.transform.localScale = scale;
        go.transform.localPosition = -Vector3.Scale(b.center, scale);   // centre the mesh on the root
        Strip(go);
        Terrain3DArt.Apply(go);   // ramp painted with the same grass tile (top face reads grass)
        var bc = go.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size;
        go.layer = layer;
        MakeStatic(go);
    }

    static void BuildPathfinderConfig()
    {
        var cfg = new GameObject("Pathfinder3DConfig").AddComponent<Pathfinder3DConfig>();
        cfg.heightAware = true;
        cfg.groundMask = groundMask;
        cfg.maxStep = TierStep * 0.75f;   // ramp half-steps pass; a full cliff does not
    }

    // ---- small helpers -------------------------------------------------------

    static int Idx(int x, int y) => y * W + x;
    static int Idx(Vector2Int c) => c.y * W + c.x;
    static bool In(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < W && c.y < H;
    static float TopOf(int x, int y) => BaseSlab + tier[Idx(x, y)] * TierStep;

    static void Strip(GameObject go) { foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c); }
    static void MakeStatic(GameObject go) => GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);

    static void SnapToGround(Transform t)
    {
        Vector3 p = t.position;
        if (Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out var hit, 120f, groundMask, QueryTriggerInteraction.Ignore))
            t.position = new Vector3(p.x, hit.point.y + Build25DSpike.FootOffsetY, p.z);
    }

    static Bounds MeasureBounds(GameObject prefab)
    {
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        tmp.transform.position = Vector3.zero; tmp.transform.rotation = Quaternion.identity; tmp.transform.localScale = Vector3.one;
        var rends = tmp.GetComponentsInChildren<Renderer>();
        Bounds bb = rends.Length > 0 ? rends[0].bounds : new Bounds(Vector3.zero, Vector3.one);
        for (int i = 1; i < rends.Length; i++) bb.Encapsulate(rends[i].bounds);
        Object.DestroyImmediate(tmp);
        return bb;
    }
}
