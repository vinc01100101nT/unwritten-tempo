using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The ONE world generator for <i>unwritten</i>. Bakes a <see cref="MapRecipe"/> data asset into a playable
/// scene — ground + prop groups + mobs + spawn + ambience — entirely from the recipe's content, so a town, a
/// wild field and a dungeon are all just different recipes (no per-kind generators).
///
/// It is a thin orchestrator over the existing, battle-tested building blocks:
/// <list type="bullet">
/// <item>Ground: <see cref="PxltgrGround"/> / <see cref="PxltgrGroundBlend"/> (tiled or blended grass→dirt).</item>
/// <item>Prop seating: <see cref="PropPlacement"/> (bounds-driven PlaceOnGround / StripColliders).</item>
/// <item>Spawns &amp; portals: <see cref="RegionBuilder"/> (MakeSpawn). Warp wiring is <see cref="WorldCreator"/>'s job.</item>
/// <item>Mobs: <see cref="Build25DSpike.BuildMonster(in MonsterArchetype, Vector3)"/> + <see cref="MobRoster"/>.</item>
/// </list>
/// All randomness flows from <see cref="MapRecipe.seed"/>, so a recipe + seed reproduces a map exactly.
/// </summary>
public static class RecipeGenerator
{
    const string PropsDir = "Assets/Prefabs/Props";
    const string ScenesDir = "Assets/Scenes";
    const float  Inset = 2f;       // pull placement in from the plot edge
    const float  PathMargin = 0.75f;   // extra clearance a solid prop keeps from a path edge

    // ── wind sway ────────────────────────────────────────────────────────────────────────────────────────
    // Soft prop groups get their renderers re-materialed onto Unwritten/FoliageWind so they sway. The cloned
    // sway materials are cached per SOURCE material within one bake, so every flower shares ONE material and
    // stays batched (and we don't make N copies). Cleared at the top of each Generate.
    static readonly Dictionary<(Material src, bool canopy), Material> _swayMats = new Dictionary<(Material, bool), Material>();
    static Shader _foliageWind;

    // PathTile → the ground_* prop whose Base Map textures the path. Falls back to dirt inside LayPlot if missing.
    static string PathTexName(PathTile t) => t switch
    {
        PathTile.Road   => "ground_road",
        PathTile.Soil   => "ground_soil",
        PathTile.Stones => "ground_stones",
        PathTile.Rock   => "ground_rock",
        _               => "ground_road",
    };

    // ── ENTRY POINTS ───────────────────────────────────────────────────────────────────────────────────
    /// <summary>Bake <paramref name="r"/> into Assets/Scenes/&lt;mapName&gt;.unity. Returns success + the scene
    /// path and how much was placed. Leaves the new map open &amp; active. <see cref="WorldCreator"/> is the only
    /// caller — it clones a template recipe per map cell and bakes each via the exit-aware overload below.</summary>
    public static (bool ok, string path, int props, int mobs) Generate(MapRecipe r)
        => Generate(r, null);

    /// <summary>As <see cref="Generate(MapRecipe)"/>, but <paramref name="exitPointsXZ"/> (a map's warp doorway
    /// positions in world-XZ, passed by the World Creator) routes the paths to terminate at each doorway — so warp
    /// pads land on roads, not mid-field. When null/empty, paths fall back to the recipe's random pathCount routes.</summary>
    public static (bool ok, string path, int props, int mobs) Generate(MapRecipe r, IReadOnlyList<Vector2> exitPointsXZ)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Generate Map", "Exit Play mode first, then re-run.", "OK"); return (false, null, 0, 0); }
        if (r == null) return (false, null, 0, 0);

        string safe = Sanitize(string.IsNullOrWhiteSpace(r.mapName) ? "NewMap" : r.mapName);
        string path = $"{ScenesDir}/{safe}.unity";

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Generate Map", "No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers.", "OK"); return (false, null, 0, 0); }
        int mask = 1 << groundLayer;

        int seed = r.randomizeSeed ? (int)(System.DateTime.Now.Ticks & 0x7fffffff) : r.seed;
        if (r.randomizeSeed) { r.seed = seed; EditorUtility.SetDirty(r); }   // write back so the layout is reproducible
        var rng = new System.Random(seed);

        _swayMats.Clear();   // fresh per-bake material cache (see ApplySway)
        _foliageWind = Shader.Find("Unwritten/FoliageWind");
        if (_foliageWind != null) EnsureAlwaysIncluded(_foliageWind);   // so foliage sway survives a player build

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        Build25DSpike.BuildLight();   // authoring Sun (disabled at Play; Systems owns the runtime light)

        Vector3 center = Vector3.zero;
        // Route the path network ONCE: it drives the ground bake, follow-path placement, and prop clearance.
        // With warp exits (World Creator), roads run hub→each doorway so pads sit on roads; else random routes.
        bool toExits = exitPointsXZ != null && exitPointsXZ.Count > 0;
        var paths = toExits
            ? PathRouting.BuildToExits(new Vector2(center.x, center.z), r.sizeX, r.sizeZ, exitPointsXZ, r.pathWander, seed, r.pathSmoothness)
            : PathRouting.Build(new Vector2(center.x, center.z), r.sizeX, r.sizeZ, r.pathCount, r.pathWander, seed, r.pathSmoothness);
        // One ground style for every map: the single blended grass→dirt mesh (no tile seams; the path feathers in).
        var ground = PxltgrGroundBlend.LayPlot(scene, $"Ground_{safe}", center, r.sizeX, r.sizeZ, groundLayer, seed,
                                               paths, r.pathWidth, PathTexName(r.pathTile), r.dirtAmount);
        if (ground == null)
        {
            EditorUtility.DisplayDialog("Generate Map",
                "Couldn't build the ground — need the Pxltgr 'ground_grass' prefab in Assets/Prefabs/Props "
                + "(and, for the blended style, the 'Unwritten/GroundBlend' shader).\n\n"
                + "Run Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first.", "OK");
            EditorSceneManager.CloseScene(scene, true);
            return (false, null, 0, 0);
        }
        Physics.SyncTransforms();   // make the floor queryable for drop rays

        // ── PROPS ──────────────────────────────────────────────────────────────────────────────────────
        var propsRoot = new GameObject("Props");
        SceneManager.MoveGameObjectToScene(propsRoot, scene);
        var blockers = new List<(Vector2 c, float r)>();   // shared footprints later groups avoid
        int props = 0;
        if (r.propGroups != null)
            foreach (var g in r.propGroups)
            {
                if (g == null) continue;
                var child = new GameObject(string.IsNullOrEmpty(g.label) ? "Group" : g.label);
                child.transform.SetParent(propsRoot.transform, false);
                props += PlaceGroup(g, child.transform, center, r.sizeX, r.sizeZ, rng, mask, paths, r.pathWidth, blockers);
            }

        // ── MOBS ───────────────────────────────────────────────────────────────────────────────────────
        var mobsRoot = new GameObject("Mobs");
        SceneManager.MoveGameObjectToScene(mobsRoot, scene);
        Random.InitState(seed ^ unchecked((int)0x9E3779B9));   // deterministic mobs, decorrelated from props
        Vector3 spawnPos = WorldBuildUtil.SurfacePoint(center, mask);
        int mobs = 0;
        if (r.mobGroups != null)
            foreach (var m in r.mobGroups)
            {
                if (m == null) continue;
                var child = new GameObject(string.IsNullOrEmpty(m.label) ? "MobGroup" : m.label);
                child.transform.SetParent(mobsRoot.transform, false);
                mobs += PlaceMobs(m, child.transform, center, r.sizeX, r.sizeZ, mask, spawnPos);
            }

        // ── SPAWN + AMBIENCE ─────────────────────────────────────────────────────────────────────────────
        RegionBuilder.MakeSpawn(scene, "Start", spawnPos);   // boot/arrival point (World Creator adds warp spawns later)
        if (r.forestAmbience) EnsureForestAmbience(scene);

        // ── SAVE + REGISTER ──────────────────────────────────────────────────────────────────────────────
        if (!Directory.Exists(ScenesDir)) AssetDatabase.CreateFolder("Assets", "Scenes");
        var open = SceneManager.GetSceneByPath(path);
        if (open.IsValid() && open.isLoaded && open != scene) EditorSceneManager.CloseScene(open, true);
        EditorSceneManager.SaveScene(scene, path);
        WorldBuildUtil.RegisterInBuildSettings(path);
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        Debug.Log($"[unwritten] Generated map '{safe}' (seed {seed}) → {path}: {props} props, {mobs} mobs.");
        return (true, path, props, mobs);
    }

    // Retrofit: make the foliage in the CURRENTLY-OPEN scene sway, without re-baking it from a recipe. Handy for
    // hand-authored maps — it finds plant props by name and re-materials them onto the wind shader: whole-plant
    // DECOR (flowers/grass/ferns) sways entirely; TREES/bushes sway only their leaf material (trunk stays rigid).
    // Re-runnable (idempotent). Save the scene afterwards to keep it.
    static readonly string[] DecorNames = { "flower", "sunflower", "grass", "rye", "fern", "mushroom", "clover", "plant" };
    static readonly string[] TreeNames  = { "tree", "oak", "birch", "pine", "willow", "deciduous", "bush", "shrub" };

    [MenuItem("Tools/unwritten/World/Apply Foliage Wind to Open Scene", priority = 192)]
    static void ApplyWindToOpenScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Apply Foliage Wind", "Exit Play mode first, then re-run.", "OK"); return; }

        _foliageWind = Shader.Find("Unwritten/FoliageWind");
        if (_foliageWind == null)
        { EditorUtility.DisplayDialog("Apply Foliage Wind", "Shader 'Unwritten/FoliageWind' not found — is Assets/Shaders/FoliageWind.shader imported?", "OK"); return; }

        _swayMats.Clear();
        EnsureAlwaysIncluded(_foliageWind);

        var scene = SceneManager.GetActiveScene();
        int decor = 0, trees = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var rend in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                int kind = SwayKind(rend.transform);     // 2 = tree (canopy/leaves only), 1 = decor (whole), 0 = none
                if (kind == 2) { SwayRenderer(rend, true);  trees++; }
                else if (kind == 1) { SwayRenderer(rend, false); decor++; }
            }

        if (decor + trees > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        EditorUtility.DisplayDialog("Apply Foliage Wind",
            $"Applied wind sway in '{scene.name}':\n• {decor} decor renderer(s) (whole-plant)\n• {trees} tree renderer(s) (leaves only)\n\n"
            + "Make sure Systems has the WindGlobals driver (Setup Systems Scene adds it), then press Play.\n"
            + "Save the scene (Ctrl+S) to keep the change.", "OK");
    }

    // Classify a renderer by its name (or any ancestor's, e.g. its prop-group node): 2 = tree/bush (sway leaves
    // only), 1 = whole-plant decor, 0 = not foliage. Trees win over decor so a "Treeline" group reads as canopy.
    static int SwayKind(Transform t)
    {
        for (var x = t; x != null; x = x.parent)
        {
            string n = x.name.ToLowerInvariant();
            foreach (var k in TreeNames)  if (n.Contains(k)) return 2;
            foreach (var k in DecorNames) if (n.Contains(k)) return 1;
        }
        return 0;
    }

    // ── PROP PLACEMENT ENGINE ────────────────────────────────────────────────────────────────────────────
    static int PlaceGroup(RecipePropGroup g, Transform parent, Vector3 center, float sizeX, float sizeZ,
                          System.Random rng, int mask, IReadOnlyList<PathRouting.Route> paths, float pathWidth,
                          List<(Vector2 c, float r)> blockers)
    {
        bool hasPath = paths != null && paths.Count > 0 && pathWidth > 0f;
        bool sway = g.ShouldSway;                          // soft decor leans in the wind (Unwritten/FoliageWind)
        float pathClear = pathWidth * 0.5f + PathMargin;   // solid props must stay this far off any path
        // The "main street" = the longest route; StreetRows houses line it.
        PathRouting.Route mainRoute = null;
        if (hasPath) { float bestL = -1f; foreach (var rt in paths) { float L = rt.Length; if (L > bestL) { bestL = L; mainRoute = rt; } } }

        // Resolve the prefab pool: explicit picks win; otherwise auto-fill from the category.
        var pool = new List<GameObject>();
        if (g.prefabs != null) foreach (var p in g.prefabs) if (p != null) pool.Add(p);
        if (pool.Count == 0) foreach (var n in CategoryNames(g.category)) { var pf = LoadProp(n); if (pf != null) pool.Add(pf); }
        if (pool.Count == 0)
        {
            Debug.LogWarning($"[unwritten] Prop group '{g.label}' has no usable prefabs (empty picks + category {g.category}); skipped.");
            return 0;
        }

        float minX = center.x - sizeX * 0.5f + Inset, maxX = center.x + sizeX * 0.5f - Inset;
        float minZ = center.z - sizeZ * 0.5f + Inset, maxZ = center.z + sizeZ * 0.5f - Inset;
        var placedAt = new List<Vector3>();
        int placed = 0;

        // Seat one prop at (x,z) if the spot is clear. applyAvoidPath is off for street/roadside rules (they
        // intentionally sit near the path).
        bool TryPlaceAt(float x, float z, bool applyAvoidPath)
        {
            if (g.clearFromCenter > 0f)
            { float dx = x - center.x, dz = z - center.z; if (dx * dx + dz * dz < g.clearFromCenter * g.clearFromCenter) return false; }
            // Unobstructed guarantee: a SOLID prop never sits on a path; decor avoids it only if it opted in.
            if (hasPath && (g.solid || (applyAvoidPath && g.avoidPath))
                && PathRouting.MinDistanceXZ(new Vector2(x, z), paths) < pathClear) return false;

            float selfR = Mathf.Max(0.3f, g.minSpacing * 0.5f);
            var here = new Vector2(x, z);
            foreach (var p in placedAt) if ((new Vector2(p.x, p.z) - here).sqrMagnitude < g.minSpacing * g.minSpacing) return false;
            foreach (var b in blockers) if ((b.c - here).magnitude < b.r + selfR) return false;

            var prefab = pool[rng.Next(pool.Count)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            float s = g.scale * (1f + (float)(rng.NextDouble() * 2 - 1) * g.scaleJitter);
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, s);
            go.transform.rotation = Quaternion.Euler(0f, g.randomYaw ? (float)rng.NextDouble() * 360f : 0f, 0f);
            PropPlacement.PlaceOnGround(go, new Vector3(x, 0f, z), mask);   // bounds-driven seat (no float, any pivot)
            go.transform.SetParent(parent, true);
            WorldBuildUtil.SetLayerRecursive(go, 0);                        // Default — never the Ground layer
            if (!g.solid) PropPlacement.StripColliders(go);                 // decor = walk-through
            if (sway) ApplySway(go, g.CanopyOnly);                          // Auto+solid (trees) = leaves only; else whole plant

            placedAt.Add(go.transform.position);
            if (g.solid)
            {
                float r = selfR;
                if (PropPlacement.TryWorldBounds(go, out var wb)) r = Mathf.Max(selfR, Mathf.Max(wb.extents.x, wb.extents.z));
                blockers.Add((here, r));
            }
            return true;
        }

        switch (g.placement)
        {
            case PlacementRule.Scatter:
            {
                int target = TargetCount(g, sizeX, sizeZ);
                int attempts = 0, max = Mathf.Max(8, target * 15);
                while (placed < target && attempts < max)
                { attempts++; if (TryPlaceAt(RangeF(rng, minX, maxX), RangeF(rng, minZ, maxZ), true)) placed++; }
                break;
            }
            case PlacementRule.Center:
            {
                int target = TargetCount(g, sizeX, sizeZ);
                float radius = Mathf.Min(sizeX, sizeZ) * 0.25f;
                int attempts = 0, max = Mathf.Max(8, target * 20);
                while (placed < target && attempts < max)
                {
                    attempts++;
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f, dist = (float)rng.NextDouble() * radius;
                    if (TryPlaceAt(center.x + Mathf.Cos(ang) * dist, center.z + Mathf.Sin(ang) * dist, true)) placed++;
                }
                break;
            }
            case PlacementRule.EdgeRing:
            {
                float step = Mathf.Max(1f, g.minSpacing);
                int cap = g.amountMode == AmountMode.Count ? g.count : int.MaxValue;
                for (float x = minX; x <= maxX && placed < cap; x += step)
                { if (TryPlaceAt(x, maxZ, false)) placed++; if (placed < cap && TryPlaceAt(x, minZ, false)) placed++; }
                for (float z = minZ + step; z <= maxZ - step && placed < cap; z += step)
                { if (TryPlaceAt(minX, z, false)) placed++; if (placed < cap && TryPlaceAt(maxX, z, false)) placed++; }
                break;
            }
            case PlacementRule.StreetRows:
            {
                // Houses line the MAIN winding route: walk it and offset a house to either side, perpendicular to
                // the road's local direction so the two rows curve with it. No path → straight rows at center.z.
                float step = Mathf.Max(1f, g.minSpacing);
                int cap = g.amountMode == AmountMode.Count ? g.count : int.MaxValue;
                if (hasPath && mainRoute != null)
                {
                    foreach (var (pos, dir) in PathRouting.Walk(mainRoute, step))
                    {
                        if (placed >= cap) break;
                        Vector2 perp = new Vector2(-dir.y, dir.x);
                        Vector2 l = pos + perp * g.rowOffset, rt = pos - perp * g.rowOffset;
                        if (TryPlaceAt(l.x, l.y, false)) placed++;
                        if (placed < cap && TryPlaceAt(rt.x, rt.y, false)) placed++;
                    }
                }
                else
                {
                    float zTop = center.z + g.rowOffset, zBot = center.z - g.rowOffset;
                    for (float x = minX; x <= maxX && placed < cap; x += step)
                    { if (TryPlaceAt(x, zTop, false)) placed++; if (placed < cap && TryPlaceAt(x, zBot, false)) placed++; }
                }
                break;
            }
            case PlacementRule.Roadside:
            {
                // Carts/lamps hug the shoulder of EVERY route. No path → straight rows just off center.z.
                float step = Mathf.Max(1f, g.minSpacing);
                int cap = g.amountMode == AmountMode.Count ? g.count : int.MaxValue;
                float off = pathWidth * 0.5f + 1.5f;   // just outside the path's clearance band
                if (hasPath)
                {
                    foreach (var rt in paths)
                    {
                        if (placed >= cap) break;
                        foreach (var (pos, dir) in PathRouting.Walk(rt, step))
                        {
                            if (placed >= cap) break;
                            Vector2 perp = new Vector2(-dir.y, dir.x);
                            Vector2 a = pos + perp * off, b = pos - perp * off;
                            if (TryPlaceAt(a.x, a.y, false)) placed++;
                            if (placed < cap && TryPlaceAt(b.x, b.y, false)) placed++;
                        }
                    }
                }
                else
                {
                    float zTop = center.z + off, zBot = center.z - off;
                    for (float x = minX; x <= maxX && placed < cap; x += step)
                    { if (TryPlaceAt(x, zTop, false)) placed++; if (placed < cap && TryPlaceAt(x, zBot, false)) placed++; }
                }
                break;
            }
        }
        return placed;
    }

    // ── MOB PLACEMENT ────────────────────────────────────────────────────────────────────────────────────
    static int PlaceMobs(RecipeMobGroup m, Transform parent, Vector3 center, float sizeX, float sizeZ,
                         int mask, Vector3 spawn)
    {
        if (Build25DSpike.MonsterSheet() == null)
        { Debug.LogWarning("[unwritten] No Ninja Adventure monster sheet found; mob group skipped."); return 0; }

        int target = m.amountMode == AmountMode.Count ? m.count : Mathf.Max(0, Mathf.RoundToInt(sizeX * sizeZ * m.density));
        if (target <= 0) return 0;

        float minX = center.x - sizeX * 0.5f + Inset, maxX = center.x + sizeX * 0.5f - Inset;
        float minZ = center.z - sizeZ * 0.5f + Inset, maxZ = center.z + sizeZ * 0.5f - Inset;
        var spots = new List<Vector3>();
        int placed = 0, attempts = 0, max = target * 16;
        while (placed < target && attempts < max)
        {
            attempts++;
            float x = Random.Range(minX, maxX), z = Random.Range(minZ, maxZ);
            if (!Physics.Raycast(new Vector3(x, center.y + 60f, z), Vector3.down, out var hit, 200f, mask, QueryTriggerInteraction.Ignore))
                continue;
            if (new Vector2(hit.point.x - spawn.x, hit.point.z - spawn.z).magnitude < m.clearFromSpawn) continue;
            bool tooClose = false;
            foreach (var s in spots) if ((s - hit.point).sqrMagnitude < m.minSpacing * m.minSpacing) { tooClose = true; break; }
            if (tooClose) continue;

            var mob = Build25DSpike.BuildMonster(ResolveArchetype(m.archetypeName), hit.point);
            mob.transform.SetParent(parent, true);
            spots.Add(hit.point);
            placed++;
        }
        return placed;
    }

    static MonsterArchetype ResolveArchetype(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return MobRoster.WeightedPick();
        string n = name.Trim();
        if (string.Equals(n, "Boss", System.StringComparison.OrdinalIgnoreCase)) return MobRoster.Boss;
        foreach (var a in MobRoster.All)
            if (string.Equals(a.name, n, System.StringComparison.OrdinalIgnoreCase)) return a;
        Debug.LogWarning($"[unwritten] Mob archetype '{name}' not found in MobRoster; using a weighted pick.");
        return MobRoster.WeightedPick();
    }

    // ── AMBIENCE (migrated from the retired PxltgrFieldBuilder) ─────────────────────────────────────────────
    static void EnsureForestAmbience(Scene scene)
    {
        GameObject go = null;
        foreach (var root in scene.GetRootGameObjects()) if (root.name == "Ambient") { go = root; break; }
        if (go == null) { go = new GameObject("Ambient"); SceneManager.MoveGameObjectToScene(go, scene); }
        else foreach (var old in go.GetComponents<AudioSource>()) Object.DestroyImmediate(old);

        var amb = go.GetComponent<DayNightAmbience>();
        if (amb == null) amb = go.AddComponent<DayNightAmbience>();
        amb.dayClip   = SfxAssets.Ambient("BGS Loops/Forest Day",   "Forest Day");
        amb.nightClip = SfxAssets.Ambient("BGS Loops/Forest Night", "Forest Night");
        amb.maxVolume = 0.22f;
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────────────────
    static int TargetCount(RecipePropGroup g, float sizeX, float sizeZ)
        => g.amountMode == AmountMode.Count ? Mathf.Max(0, g.count) : Mathf.Max(0, Mathf.RoundToInt(sizeX * sizeZ * g.density));

    static float RangeF(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    static GameObject LoadProp(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{name}.prefab");

    // ── WIND SWAY ──────────────────────────────────────────────────────────────────────────────────────────
    // Re-material a prop's renderers onto Unwritten/FoliageWind so its vertices bend with the world wind, reusing
    // one cloned material per (source material, profile) so instances stay batched. Two profiles:
    //   • DECOR  (canopy = false): whole-plant decor (flowers/grass/ferns) — sway EVERY material, snappy default look.
    //   • CANOPY (canopy = true):  trees/bushes — sway ONLY the leaf material (the trunk/bark stays rigid), with
    //                              taller/gentler/slower params so the crown rustles instead of the whole tree whipping.
    static void ApplySway(GameObject go, bool canopy)
    {
        if (_foliageWind == null) return;   // shader missing (already warned at Generate); leave props un-swayed
        foreach (var rend in go.GetComponentsInChildren<MeshRenderer>(true)) SwayRenderer(rend, canopy);
    }

    // Swap one renderer's materials to their cached wind clones (idempotent — skips ones already on the shader).
    // Canopy mode swaps ONLY foliage-named materials (oak_leaves, Tree_leaves…) so bark/wood is left rigid.
    static void SwayRenderer(MeshRenderer rend, bool canopy)
    {
        if (_foliageWind == null || rend == null) return;
        var mats = rend.sharedMaterials;
        bool changed = false;
        for (int i = 0; i < mats.Length; i++)
        {
            var src = mats[i];
            if (src == null || src.shader == _foliageWind) continue;
            if (canopy && !IsFoliageMat(src)) continue;   // trees: sway the leaves, never the trunk
            mats[i] = GetSwayMaterial(src, canopy);
            changed = true;
        }
        if (changed) rend.sharedMaterials = mats;
    }

    // Leaf/plant material? Matched by name (the Pxltgr pack names them oak_leaves, Birch_leaves, Tree_leaves1,
    // Plant, … while trunks are *_bark) — the same convention PxltgrMaterialFixer keys its alpha-clip off.
    static readonly string[] FoliageMatKeywords = { "leaf", "leaves", "plant", "foliage", "needle", "fern", "bush" };
    static bool IsFoliageMat(Material m)
    {
        if (m == null) return false;
        string n = m.name.ToLowerInvariant();
        foreach (var k in FoliageMatKeywords) if (n.Contains(k)) return true;
        return false;
    }

    // Clone a source URP/Lit prop material onto the wind shader, copying its Base Map + tint + cutout so it looks
    // the same — just swaying. Cached per (source, profile) within a bake so instances share ONE material (batched).
    static Material GetSwayMaterial(Material src, bool canopy)
    {
        var key = (src, canopy);
        if (_swayMats.TryGetValue(key, out var m) && m != null) return m;

        m = new Material(_foliageWind) { name = src.name + (canopy ? " (wind canopy)" : " (wind)") };
        if      (src.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
        else if (src.HasProperty("_MainTex")) m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
        if      (src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
        else if (src.HasProperty("_Color"))     m.SetColor("_BaseColor", src.GetColor("_Color"));
        m.SetFloat("_Cutoff", src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0.5f);

        if (canopy)
        {
            // A tree crown is metres up and broad: a taller full-bend height keeps the lowest leaves calmer, a
            // lower stiffness lets the whole canopy lean together, and a smaller/slower amount reads as a heavy
            // rustle rather than the flower-tier whip. (World wind still scales all of this with the weather.)
            m.SetFloat("_SwayHeight", 3.0f);
            m.SetFloat("_SwayStiffness", 1.2f);
            m.SetFloat("_SwayAmount", 0.05f);
            m.SetFloat("_SwaySpeed", 0.85f);
        }

        _swayMats[key] = m;
        return m;
    }

    // Register a shader in Project Settings ▸ Graphics ▸ Always Included Shaders (idempotent) so a generated
    // shader the scene only references via runtime-cloned materials isn't stripped from a player build.
    static void EnsureAlwaysIncluded(Shader sh)
    {
        var gs = UnityEngine.Rendering.GraphicsSettings.GetGraphicsSettings();
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) return;   // already there
        int idx = arr.arraySize;
        arr.InsertArrayElementAtIndex(idx);
        arr.GetArrayElementAtIndex(idx).objectReferenceValue = sh;
        so.ApplyModifiedProperties();
    }

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    // Canonical Pxltgr prefab names per family (LOD0 where the pack ships LODs) — the same lists the old
    // field/town bakers used, now in one place so a recipe can say "Trees" without naming each prefab.
    static string[] CategoryNames(PropCategory c) => c switch
    {
        PropCategory.Trees     => new[] { "Oak_tree1", "Oak_tree2", "Oak_tree3", "Birch_tree1", "Birch_tree2",
                                          "Birch_tree3", "Pine_tree1", "Pine_tree2", "Pine_tree3", "Deciduous_tree1",
                                          "Deciduous_tree2", "Deciduous_tree3", "Deciduous_tree4", "Willow_tree1", "Willow_tree2" },
        PropCategory.Stumps    => new[] { "Deciduous_tree_stump_LOD0", "Pine_stump_LOD0" },
        PropCategory.Rocks     => new[] { "Rock1", "Rock2", "Rock3", "Stone1_LOD0", "Stone2_LOD0", "Stone3_LOD0" },
        PropCategory.Bushes    => new[] { "Bush1" },
        PropCategory.Grass     => new[] { "Grass1", "Grass2", "Grass3", "Grass4", "Rye" },
        PropCategory.Flowers   => new[] { "Flower1", "Flower2", "Flower3", "Flower4", "Flower5", "Flower6",
                                          "Flower7", "Flower8", "Flower9", "Sunflower" },
        PropCategory.Ferns     => new[] { "Fern1", "Fern2", "Fern3" },
        PropCategory.Mushrooms => new[] { "Mushroom1", "Mushroom2", "Mushroom3", "Mushroom4", "Mushroom5" },
        PropCategory.Houses    => new[] { "WoodenHouse_1_LOD0", "WoodenHouse_2_LOD0", "WoodenHouse_3_LOD0", "House1", "House2", "House3" },
        PropCategory.Landmarks => new[] { "Tavern_LOD0", "Windmill_LOD0" },
        PropCategory.Carts     => new[] { "cart1_LOD0", "cart2_LOD0", "cart3_LOD0", "cart4_LOD0" },
        _                      => System.Array.Empty<string>(),
    };
}
