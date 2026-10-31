using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The ONE world generator for <i>unwritten</i>. Bakes a <see cref="MapRecipe"/> data asset into a playable
/// scene — ground + prop groups + spawn + ambience — entirely from the recipe's content, so a town, a
/// wild field and a dungeon are all just different recipes (no per-kind generators).
///
/// It is a thin orchestrator over the existing, battle-tested building blocks:
/// <list type="bullet">
/// <item>Ground: <see cref="WalkableGround"/> / <see cref="GroundBlendBuilder"/> (tiled or blended grass→dirt).</item>
/// <item>Prop seating: <see cref="PropPlacement"/> (bounds-driven PlaceOnGround / StripColliders).</item>
/// <item>Spawns &amp; portals: <see cref="RegionBuilder"/> (MakeSpawn).</item>
/// <item>Mobs: <see cref="RigBuilder25D.BuildMonster(in MonsterArchetype, Vector3)"/> + <see cref="MobRoster"/>.</item>
/// </list>
/// All randomness flows from <see cref="MapRecipe.seed"/>, so a recipe + seed reproduces a map exactly.
/// </summary>
public static class RecipeGenerator
{
    const string PropsDir = "Assets/1_Authored/Prefabs/Props";
    const string ScenesDir = "Assets/Scenes";
    const float  Inset = 2f;       // pull placement in from the plot edge
    const float  PathMargin = 0.75f;   // extra clearance a solid prop keeps from a path edge

    // ── modular wall kit (Pxltgr) — used by PlacementRule.PerimeterWall ──────────────────────────────────
    // Straight runs + corner posts + towers + a gate. The straight piece is measured at bake time for its run
    // length, so the wall tiles seamlessly whatever the kit's authored size/scale is.
    static readonly string[] WallStraightNames = { "Wall_part1", "Wall_part2" };
    static readonly string[] WallTowerNames    = { "Wall_tower1", "Wall_tower2" };
    const string WallCornerName   = "Wall_corner";
    const string WallEntranceName = "Wall_entrance";
    const string WallGateLName    = "Wall_gateL";
    const string WallGateRName    = "Wall_gateR";
    // ⚠️ ORIENTATION: the kit's authored facing isn't knowable from code, so we assume a straight piece's FRONT
    // faces +Z/+X and flip the far (north/east) edges 180° to face inward. If the walls end up facing OUTWARD in
    // Play, flip this sign (the whole wall turns 180°). Towers are ~square so their yaw is cosmetic.
    const float WallFacingSign = 1f;

    // ── wind sway ────────────────────────────────────────────────────────────────────────────────────────
    // Soft prop groups get their renderers re-materialed onto Unwritten/FoliageWind so they sway. The cloned
    // sway materials are cached per SOURCE material within one bake, so every flower shares ONE material and
    // stays batched (and we don't make N copies). Cleared at the top of each Generate.
    static readonly Dictionary<(Material src, bool canopy), Material> _swayMats = new Dictionary<(Material, bool), Material>();
    static Shader _foliageWind;

    // Solid sibling of the sway shader (Unwritten/StylizedLit): re-materials a solid prop (house/wall/rock) onto
    // the clean storybook lighting WITHOUT wind. Cached per source material within one invocation so instances
    // stay batched. See ApplyStylizedLitToSelected.
    static Shader _stylizedLit;

    // ── sidecar capture (see MapSidecar) ──────────────────────────────────────────────────────────────────
    // Every placed prop is recorded here as a painter ObjectCell during a bake, then written next to the scene as
    // <name>.map.json so the generated map can be re-opened + hand-edited in the Map Painter. Reset per Generate.
    static List<MapSidecar.ObjectCell> _sidecarObjects;
    static float _sidecarSizeX, _sidecarSizeZ;

    // PathTile → the ground_* prop whose Base Map textures the path. Falls back to dirt inside LayPlot if missing.
    static string PathTexName(PathTile t) => t switch
    {
        PathTile.Road   => "PATH_dirt_road",
        PathTile.Soil   => "BASE_OR_PATCH_OR_PATH_soil",
        PathTile.Stones => "PATH_OR_SIDEWALK_dark_cobble",
        PathTile.Rock   => "BASE_OR_PATCH_OR_PATH_gravel_scree",
        _               => "PATH_dirt_road",
    };

    // A biome's look (ground tiles, colour wash, road, props) is its row in the BiomeTable — the one place it lives.
    // A missing table, row or tile fails the bake with a dialog rather than falling back to some other look.
    static BiomeRow BiomeRowOrExplain(Biome b)
    {
        var table = BiomeTable.Load();
        string problem =
            table == null ? $"No biome table at {BiomeTable.AssetPath}." :
            table.For(b) is not BiomeRow row ? $"The biome table has no row for {b}." :
            row.baseTile == null ? $"The {b} row has no main ground tile." :
            row.patchTile == null ? $"The {b} row has no patch tile." : null;
        if (problem == null) return table.For(b);
        EditorUtility.DisplayDialog("Generate Map", problem, "OK");
        return null;
    }

    // ── ENTRY POINTS ───────────────────────────────────────────────────────────────────────────────────
    /// <summary>Bake <paramref name="r"/> into Assets/Scenes/&lt;mapName&gt;.unity. Returns success + the scene
    /// path and how much was placed. Leaves the new map open &amp; active. The recipe inspector's Generate button
    /// (<see cref="MapRecipeEditor"/>) is the caller.</summary>
    public static (bool ok, string path, int props) Generate(MapRecipe r)
        => Generate(r, null);

    /// <summary>As <see cref="Generate(MapRecipe)"/>, but <paramref name="exitPointsXZ"/> (a map's doorway positions in
    /// world-XZ) routes the paths to terminate at each doorway — so doors land on roads, not mid-field. When null/empty,
    /// paths fall back to the recipe's random pathCount routes.
    /// <paramref name="mapPicksRoad"/> true = the caller set this map's road, so it wins over the biome's.
    /// <paramref name="leaveOpen"/> false = save and close the new map, leaving the scenes that were open (and the
    /// active one) exactly as they were — for builds run without the user at the controls.</summary>
    public static (bool ok, string path, int props) Generate(MapRecipe r, IReadOnlyList<Vector2> exitPointsXZ,
                                                             bool mapPicksRoad = false, bool leaveOpen = true)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Generate Map", "Exit Play mode first, then re-run.", "OK"); return (false, null, 0); }
        if (r == null) return (false, null, 0);
        var bio = BiomeRowOrExplain(r.biome);
        if (bio == null) return (false, null, 0);
        bool biomeRoad = !mapPicksRoad && bio.roadTile != null;
        string roadTex = biomeRoad ? bio.roadTile.name : PathTexName(r.pathTile);
        float patchAmount = bio.patchAmount >= 0f ? bio.patchAmount : r.dirtAmount;

        string safe = Sanitize(string.IsNullOrWhiteSpace(r.mapName) ? "NewMap" : r.mapName);
        string path = $"{ScenesDir}/{safe}.unity";
        if (!leaveOpen && SceneManager.GetSceneByPath(path).isLoaded)
        { Debug.LogError($"[unwritten] '{safe}' is open in the editor; not rebuilding it behind the user's back."); return (false, null, 0); }

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Generate Map", "No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers.", "OK"); return (false, null, 0); }
        int mask = 1 << groundLayer;

        int seed = r.randomizeSeed ? (int)(System.DateTime.Now.Ticks & 0x7fffffff) : r.seed;
        if (r.randomizeSeed) { r.seed = seed; EditorUtility.SetDirty(r); }   // write back so the layout is reproducible
        var rng = new System.Random(seed);

        _swayMats.Clear();   // fresh per-bake material cache (see ApplySway)
        _sidecarObjects = new List<MapSidecar.ObjectCell>();   // fresh per-bake prop capture (see MapSidecar)
        _sidecarSizeX = r.sizeX; _sidecarSizeZ = r.sizeZ;
        _foliageWind = Shader.Find("Unwritten/FoliageWind");
        if (_foliageWind != null) EnsureAlwaysIncluded(_foliageWind);   // so foliage sway survives a player build

        var wasActive = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        RigBuilder25D.BuildLight();   // authoring Sun (disabled at Play; Systems owns the runtime light)
        // The map's biome, stamped so the running game can tell which biome this map is (BiomeWeather reads it).
        var biomeTag = new GameObject("MapBiome");
        SceneManager.MoveGameObjectToScene(biomeTag, scene);
        biomeTag.AddComponent<MapBiome>().biome = r.biome;

        Vector3 center = Vector3.zero;
        var ground = LayGround(scene, r, exitPointsXZ, bio, roadTex, patchAmount, safe, seed, groundLayer,
                               out var paths, out var cliffs);
        if (ground == null)
        {
            EditorUtility.DisplayDialog("Generate Map",
                "Couldn't build the ground — need the 'BASE_OR_PATCH_meadow_grass' tile in Assets/1_Authored/Art/GroundTiles "
                + "(and, for the blended style, the 'Unwritten/GroundBlend' shader).", "OK");
            EditorSceneManager.CloseScene(scene, true);
            return (false, null, 0);
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
                props += PlaceGroup(g, bio, child.transform, center, r.sizeX, r.sizeZ, rng, mask, paths, r.pathWidth, blockers, cliffs);
            }

        // NO CREATURES ARE PUT IN THE MAP. Where creatures live is the world spawn list (WorldSpawns), read by the server
        // as it loads each map — so nothing about creatures is ever saved inside a scene.

        // NO ARRIVAL POINT IS PLANTED HERE. One used to go at the map's centre, where this also puts the plaza and
        // its props, so arriving there put a player inside a building. An arrival point belongs beside the door a
        // player comes in by, so whoever adds the door adds it.

        // ── SAVE + REGISTER ──────────────────────────────────────────────────────────────────────────────
        if (!Directory.Exists(ScenesDir)) AssetDatabase.CreateFolder("Assets", "Scenes");
        var open = SceneManager.GetSceneByPath(path);
        if (open.IsValid() && open.isLoaded && open != scene) EditorSceneManager.CloseScene(open, true);
        EditorSceneManager.SaveScene(scene, path);
        WorldBuildUtil.RegisterInBuildSettings(path);
        if (leaveOpen) EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        else
        {
            EditorSceneManager.CloseScene(scene, true);
            if (wasActive.IsValid() && wasActive.isLoaded) SceneManager.SetActiveScene(wasActive);
        }
        AssetDatabase.SaveAssets();

        // ── SIDECAR: write the painter-editable map.json next to the scene (round-trip into the Map Painter) ──
        WriteSidecar(safe, r, center, seed, paths, biomeRoad ? PainterBrushForTile(roadTex) : PainterRoadBrush(r.pathTile),
                     patchAmount);

        Debug.Log($"[unwritten] Generated map '{safe}' (seed {seed}) → {path}: {props} props.");
        return (true, path, props);
    }

    // The map's roads and its ground, from the recipe and seed alone.
    static GameObject LayGround(Scene scene, MapRecipe r, IReadOnlyList<Vector2> exitPointsXZ, BiomeRow bio,
                                string roadTex, float patchAmount, string safe, int seed, int groundLayer,
                                out List<PathRouting.Route> paths, out GroundCliffs.Spec cliffs)
    {
        Vector3 center = Vector3.zero;
        // Route the path network ONCE: it drives the ground bake, follow-path placement, and prop clearance.
        // With doorways given, roads link them to each other so doors sit on roads; else random routes.
        bool toExits = exitPointsXZ != null && exitPointsXZ.Count > 0;
        paths = toExits
            ? PathRouting.BuildToExits(new Vector2(center.x, center.z), r.sizeX, r.sizeZ, exitPointsXZ, r.pathWander, seed, r.pathSmoothness)
            : r.pathStyle == PathStyle.Cardinal
                ? PathRouting.BuildCardinal(new Vector2(center.x, center.z), r.sizeX, r.sizeZ)
                : PathRouting.Build(new Vector2(center.x, center.z), r.sizeX, r.sizeZ, r.pathCount, r.pathWander, seed, r.pathSmoothness);
        // Ground = one blended mesh; the biome row chooses its base/patch/road tiles + hue, the recipe can add a tint on
        // top and opt into hard (un-feathered) edges. The biome row also says how high its ground's mounds rise; the
        // recipe says whether rock cliffs line the roads.
        bool cliffsOn = r.cliffs == CliffChoice.On || (r.cliffs == CliffChoice.FollowBiome && bio.cliffs);
        cliffs = cliffsOn && r.cliffHeight > 0f && paths.Count > 0
            ? new GroundCliffs.Spec(paths, r.cliffHeight, r.cliffPassWidth, seed, center, r.sizeX, r.sizeZ,
                                    r.cliffMeetingPoints, bio.cliffFaceColor) : null;
        return GroundBlendBuilder.LayPlot(scene, $"Ground_{safe}", center, r.sizeX, r.sizeZ, groundLayer, seed,
                                          paths, r.pathWidth, roadTex, patchAmount, 0.8f,
                                          bio.baseTile.name, bio.patchTile.name, bio.tint * r.groundTint,
                                          r.groundStyle == GroundStyle.HardEdge, bio.bumpHeight, bio.bumpWidth,
                                          bio.bumpLevels, bio.bumpSteepestDegrees, cliffs);
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

    // Sway the SELECTED object(s) as a whole tree — for hand-dropped single-mesh trees (GPT/Trellis exports whose
    // one baked material isn't named "*leaves", so "Apply Foliage Wind to Open Scene" can't tell trunk from crown).
    // Swaps EVERY material on the selection onto the wind shader, with params auto-sized to each object's height so
    // the trunk base stays planted and only the crown rustles. Works on both a scene instance AND a prefab asset
    // picked in the Project window (a prefab gets its sway material saved to disk + the prefab re-saved, so it isn't
    // left pointing at a runtime-only material that would serialize to null → pink). Re-runnable / idempotent.
    [MenuItem("Tools/unwritten/World/Sway Selected Tree", priority = 193)]
    static void SwaySelectedTree()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Sway Selected Tree", "Exit Play mode first, then re-run.", "OK"); return; }

        _foliageWind = Shader.Find("Unwritten/FoliageWind");
        if (_foliageWind == null)
        { EditorUtility.DisplayDialog("Sway Selected Tree", "Shader 'Unwritten/FoliageWind' not found — is Assets/Shaders/FoliageWind.shader imported?", "OK"); return; }

        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        { EditorUtility.DisplayDialog("Sway Selected Tree", "Select the tree object(s) — a scene instance or a prefab in the Project window — first, then re-run.", "OK"); return; }

        _swayMats.Clear();
        EnsureAlwaysIncluded(_foliageWind);

        int objs = 0, mats = 0, prefabs = 0;
        var local = new Dictionary<Material, Material>();
        foreach (var go in sel)
        {
            var rends = go.GetComponentsInChildren<MeshRenderer>(true);
            if (rends.Length == 0) continue;
            bool isAsset = EditorUtility.IsPersistent(go);   // a prefab picked in the Project window (not a scene instance)

            // Full-bend height = the object's own height so only the top third of ANY tree really moves.
            float h = ModelHeight(go.transform, rends);

            bool touched = false;
            foreach (var rend in rends)
            {
                var arr = rend.sharedMaterials;
                for (int i = 0; i < arr.Length; i++)
                {
                    var src = arr[i];
                    if (src == null || src.shader == _foliageWind) continue;
                    if (!local.TryGetValue(src, out var m) || m == null)
                    {
                        m = GetSwayMaterial(src, true);                 // canopy clone (copies Base Map/tint/cutout)
                        m.SetFloat("_SwayHeight", h);                    // size the bend to THIS tree
                        m.SetFloat("_SwayStiffness", 2.2f);             // high = trunk base stays rigid
                        m.SetFloat("_SwayAmount", 0.06f);
                        m.SetFloat("_SwaySpeed", 0.8f);
                        local[src] = m;
                    }
                    // A material written into a PREFAB asset must itself be a saved .mat asset — an in-memory
                    // material serializes as a NULL slot and the prop renders pink. Persist on demand (scene
                    // objects keep the cheap in-memory clone, embedded in the scene file as before).
                    if (isAsset && !EditorUtility.IsPersistent(m))
                    {
                        m = PersistSwayMaterial(m);
                        local[src] = m;
                        _swayMats[(src, true)] = m;
                    }
                    arr[i] = m; mats++; touched = true;
                }
                rend.sharedMaterials = arr;
            }
            if (!touched) continue;
            objs++;
            if (isAsset) { PrefabUtility.SavePrefabAsset(go); prefabs++; }
            else UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        }
        if (prefabs > 0) { AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }

        EditorUtility.DisplayDialog("Sway Selected Tree",
            $"Swayed {objs} object(s), {mats} material(s).\n\nPress Play to see it move (the wind driver runs at runtime).\n"
            + (prefabs > 0
                ? $"{prefabs} prefab asset(s) saved to disk (sway materials written under Prefabs/Props/Materials)."
                : "Save the scene (Ctrl+S) to keep it."), "OK");
    }

    // Folder-driven sway: the FOLDER a prefab lives in declares its intent, so we never guess "tree vs plant" from
    // the prop's name (the flaky old path). Run these after adding new foliage prefabs — they bake the wind material
    // straight into each PREFAB asset in the folder (persisted .mat, so every placed instance inherits it).
    //   • Trees folder  → canopy profile: sway ONLY each tree's leaf material (bark stays rigid), base planted.
    //                     A tree with no leaf-named material (a single-mesh export) falls back to swaying WHOLE so it
    //                     still visibly moves instead of silently doing nothing — and it's reported as a fallback.
    //   • Plants folder → whole-plant decor profile: sway EVERY material (no leaf/bark split needed).
    const string TreesDir  = PropsDir + "/Trees";
    const string PlantsDir = PropsDir + "/Plants";

    [MenuItem("Tools/unwritten/World/Sway Trees Folder (leaves only)", priority = 195)]
    static void SwayTreesFolder() => SwayPrefabFolder("Sway Trees Folder", TreesDir, treeMode: true);

    [MenuItem("Tools/unwritten/World/Sway Plants Folder (whole plant)", priority = 196)]
    static void SwayPlantsFolder() => SwayPrefabFolder("Sway Plants Folder", PlantsDir, treeMode: false);

    // Bake wind sway into every prefab in a folder. treeMode = canopy (leaves-only, base-rigid params sized to each
    // tree's height); !treeMode = whole-plant decor. Reuses the exact same GetSwayMaterial/PersistSwayMaterial path
    // as Sway Selected Tree, so there's ONE material-baking code path. Re-runnable / idempotent.
    static void SwayPrefabFolder(string title, string folder, bool treeMode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog(title, "Exit Play mode first, then re-run.", "OK"); return; }

        _foliageWind = Shader.Find("Unwritten/FoliageWind");
        if (_foliageWind == null)
        { EditorUtility.DisplayDialog(title, "Shader 'Unwritten/FoliageWind' not found — is Assets/Shaders/FoliageWind.shader imported?", "OK"); return; }

        if (!AssetDatabase.IsValidFolder(folder))
        { EditorUtility.DisplayDialog(title, $"Folder not found:\n{folder}\n\nMove the prefabs there first.", "OK"); return; }

        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        if (guids.Length == 0)
        { EditorUtility.DisplayDialog(title, $"No prefabs in:\n{folder}", "OK"); return; }

        _swayMats.Clear();
        EnsureAlwaysIncluded(_foliageWind);

        int prefabs = 0, mats = 0, fellBack = 0, skipped = 0;
        var fallbackNames = new List<string>();
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            var rends = go.GetComponentsInChildren<MeshRenderer>(true);
            if (rends.Length == 0) { skipped++; continue; }

            // Full-bend height = the prefab's own height so only the crown really moves (trees only).
            float h = ModelHeight(go.transform, rends);

            // A tree with a leaf-named material sways leaves-only; one without falls back to swaying the whole tree.
            bool anyFoliage = false;
            if (treeMode)
                foreach (var r in rends) foreach (var src in r.sharedMaterials)
                    if (IsFoliageMat(src)) { anyFoliage = true; break; }
            bool filterToFoliage = treeMode && anyFoliage;     // which materials: leaves only, or all
            bool canopyParams    = treeMode;                   // tree feel (base rigid, sized to height) vs decor feel
            if (treeMode && !anyFoliage) { fellBack++; fallbackNames.Add(go.name); }

            int matsThis = 0;
            foreach (var rend in rends)
            {
                var arr = rend.sharedMaterials;
                for (int i = 0; i < arr.Length; i++)
                {
                    var src = arr[i];
                    if (src == null || src.shader == _foliageWind) continue;
                    if (filterToFoliage && !IsFoliageMat(src)) continue;   // trees: leave the bark rigid

                    if (!_swayMats.TryGetValue((src, canopyParams), out var m) || m == null)
                    {
                        m = GetSwayMaterial(src, canopyParams);            // clone (copies Base Map/tint/cutout)
                        if (canopyParams)
                        {
                            m.SetFloat("_SwayHeight", h);                  // size the bend to THIS tree
                            m.SetFloat("_SwayStiffness", 2.2f);           // high = trunk base stays rigid
                            m.SetFloat("_SwayAmount", 0.06f);
                            m.SetFloat("_SwaySpeed", 0.8f);
                        }
                    }
                    // A material written into a PREFAB asset must be a saved .mat (an in-memory one serializes to a
                    // null slot → pink). Persist once and reuse the persisted asset for the rest of the folder.
                    if (!EditorUtility.IsPersistent(m))
                    {
                        m = PersistSwayMaterial(m);
                        _swayMats[(src, canopyParams)] = m;
                    }
                    arr[i] = m; matsThis++;
                }
                rend.sharedMaterials = arr;
            }

            if (matsThis == 0) { skipped++; continue; }
            PrefabUtility.SavePrefabAsset(go);
            prefabs++; mats += matsThis;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg = $"{folder}:\n• {prefabs} prefab(s) swayed, {mats} material(s) (saved to disk).\n"
                   + $"• {skipped} skipped (no mesh / nothing to sway).";
        if (treeMode)
        {
            msg += $"\n• {fellBack} tree(s) had no leaf material → swayed WHOLE as a fallback.";
            if (fallbackNames.Count > 0) msg += "\n   " + string.Join(", ", fallbackNames);
        }
        msg += "\n\nSway materials written under Prefabs/Props/Materials. Press Play to see it move.";
        EditorUtility.DisplayDialog(title, msg, "OK");
    }

    // Re-material the SELECTED solid prop(s) — houses, walls, rocks — onto Unwritten/StylizedLit, the clean
    // storybook lighting (flat lambert + soft shadow + SH ambient + lamps, no specular sheen) WITHOUT wind. This
    // is the solid counterpart to "Sway Selected Tree": use it on raw Trellis/GPT buildings that look like a
    // muddy glossy blob on URP/Lit. Swaps EVERY material on the selection. Re-runnable. Save the scene to keep it.
    [MenuItem("Tools/unwritten/World/Stylize Selected Solid Prop", priority = 194)]
    static void ApplyStylizedLitToSelected()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Stylize Solid Prop", "Exit Play mode first, then re-run.", "OK"); return; }

        _stylizedLit = Shader.Find("Unwritten/StylizedLit");
        if (_stylizedLit == null)
        { EditorUtility.DisplayDialog("Stylize Solid Prop", "Shader 'Unwritten/StylizedLit' not found — is Assets/Shaders/StylizedLit.shader imported?", "OK"); return; }

        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        { EditorUtility.DisplayDialog("Stylize Solid Prop", "Select the solid prop(s) in the Hierarchy first, then re-run.", "OK"); return; }

        EnsureAlwaysIncluded(_stylizedLit);

        // One undo step for the whole op — Ctrl+Z restores every renderer's original material array in one press.
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Stylize Solid Prop");
        int group = Undo.GetCurrentGroup();

        int objs = 0, mats = 0;
        var local = new Dictionary<Material, Material>();
        foreach (var go in sel)
        {
            var rends = go.GetComponentsInChildren<MeshRenderer>(true);
            if (rends.Length == 0) continue;

            bool touched = false;
            foreach (var rend in rends)
            {
                var arr = rend.sharedMaterials;
                bool rendTouched = false;
                for (int i = 0; i < arr.Length; i++)
                {
                    var src = arr[i];
                    if (src == null || src.shader == _stylizedLit) continue;
                    if (!local.TryGetValue(src, out var m) || m == null)
                    {
                        m = new Material(_stylizedLit) { name = src.name + " (stylized)" };
                        if      (src.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
                        else if (src.HasProperty("_MainTex")) m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
                        if      (src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                        else if (src.HasProperty("_Color"))     m.SetColor("_BaseColor", src.GetColor("_Color"));
                        local[src] = m;
                    }
                    arr[i] = m; mats++; touched = true; rendTouched = true;
                }
                if (rendTouched)
                {
                    Undo.RecordObject(rend, "Stylize Solid Prop");   // capture original array so Ctrl+Z restores it
                    rend.sharedMaterials = arr;
                    // Most props are prefab instances; the material swap becomes a prefab OVERRIDE. RecordObject
                    // alone makes undo work but NOT redo — the override change isn't in the redo diff. Registering
                    // the instance modification puts it on the undo stack properly so Ctrl+Y re-applies. (Measured:
                    // without this, redo on a prefab-instance renderer leaves it on the original material.)
                    if (PrefabUtility.IsPartOfPrefabInstance(rend))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(rend);
                }
            }
            if (touched) objs++;
        }
        Undo.CollapseUndoOperations(group);

        if (objs > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sel[0].scene);
        EditorUtility.DisplayDialog("Stylize Solid Prop",
            $"Stylized {objs} object(s), {mats} material(s) onto Unwritten/StylizedLit.\n\n"
            + "Solid, single-sided, no sway — the clean storybook wash for buildings.\nSave the scene (Ctrl+S) to keep it.", "OK");
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
    static int PlaceGroup(RecipePropGroup g, BiomeRow bio, Transform parent, Vector3 center, float sizeX, float sizeZ,
                          System.Random rng, int mask, IReadOnlyList<PathRouting.Route> paths, float pathWidth,
                          List<(Vector2 c, float r)> blockers, GroundCliffs.Spec cliffs)
    {
        bool hasPath = paths != null && paths.Count > 0 && pathWidth > 0f;
        bool sway = g.ShouldSway;                          // soft decor leans in the wind (Unwritten/FoliageWind)
        float pathClear = pathWidth * 0.5f + PathMargin;   // solid props must stay this far off any path
        // The "main street" = the longest route; StreetRows houses line it.
        PathRouting.Route mainRoute = null;
        if (hasPath) { float bestL = -1f; foreach (var rt in paths) { float L = rt.Length; if (L > bestL) { bestL = L; mainRoute = rt; } } }

        // Resolve the prefab pool: explicit picks win; then the biome's props for this category; else the pack list.
        var pool = new List<GameObject>();
        float countMultiplier = 1f;   // the biome's "how many" for its own props; 1 for picks and the pack list
        if (g.prefabs != null) foreach (var p in g.prefabs) if (p != null) pool.Add(p);
        if (pool.Count == 0 && g.category != PropCategory.None && bio.TryGetProps(g.category, out var biomeProps))
        {
            countMultiplier = bio.CountMultiplier(g.category);
            if (biomeProps != null) foreach (var p in biomeProps) if (p != null) pool.Add(p);
            if (pool.Count == 0)
            {
                Debug.Log($"[unwritten] Prop group '{g.label}': the {bio.biome} biome places no {g.category}; skipped.");
                return 0;
            }
        }
        if (pool.Count == 0) foreach (var n in CategoryNames(g.category)) { var pf = LoadProp(n); if (pf != null) pool.Add(pf); }
        // PerimeterWall ignores category/picks for the run pieces — it loads the wall kit by name.
        if (pool.Count == 0 && g.placement == PlacementRule.PerimeterWall)
            foreach (var n in WallStraightNames) { var pf = LoadProp(n); if (pf != null) pool.Add(pf); }
        if (pool.Count == 0)
        {
            Debug.LogWarning($"[unwritten] Prop group '{g.label}' has no usable prefabs (empty picks + category {g.category}); skipped.");
            return 0;
        }

        float minX = center.x - sizeX * 0.5f + Inset, maxX = center.x + sizeX * 0.5f - Inset;
        float minZ = center.z - sizeZ * 0.5f + Inset, maxZ = center.z + sizeZ * 0.5f - Inset;
        var placedAt = new List<Vector3>();
        int placed = 0;
        BrushAndOffset(g, out string brush, out float fwd);   // painter brush id + yaw correction for the sidecar

        // Seat one prop at (x,z) if the spot is clear. applyAvoidPath is off for street/roadside rules (they
        // intentionally sit near the path).
        bool TryPlaceAt(float x, float z, bool applyAvoidPath, bool underOthers = false)
        {
            if (g.clearFromCenter > 0f)
            { float dx = x - center.x, dz = z - center.z; if (dx * dx + dz * dz < g.clearFromCenter * g.clearFromCenter) return false; }
            // Nothing stands in a cliff's face or on its lip, or it would poke through the rock.
            if (cliffs != null && cliffs.InCliffBand(x, z)) return false;
            // Unobstructed guarantee: a SOLID prop never sits on a path; decor avoids it only if it opted in.
            if (hasPath && (g.solid || (applyAvoidPath && g.avoidPath))
                && PathRouting.MinDistanceXZ(new Vector2(x, z), paths) < pathClear) return false;

            float selfR = Mathf.Max(0.3f, g.minSpacing * 0.5f);
            var here = new Vector2(x, z);
            foreach (var p in placedAt) if ((new Vector2(p.x, p.z) - here).sqrMagnitude < g.minSpacing * g.minSpacing) return false;
            if (!underOthers)
                foreach (var b in blockers) if ((b.c - here).magnitude < b.r + selfR) return false;

            var prefab = pool[rng.Next(pool.Count)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            float s = g.scale * (1f + (float)(rng.NextDouble() * 2 - 1) * g.scaleJitter);
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, s);
            go.transform.rotation = Quaternion.Euler(0f, g.randomYaw ? (float)rng.NextDouble() * 360f : 0f, 0f);
            PropPlacement.PlaceOnGround(go, new Vector3(x, 0f, z), mask, g.solid);   // solid props seat pivot-to-ground (foundation lip sinks in); decor stays bounds-on-ground
            go.transform.SetParent(parent, true);
            WorldBuildUtil.SetLayerRecursive(go, 0);                        // Default — never the Ground layer
            if (!g.solid) PropPlacement.StripColliders(go);                 // decor = walk-through
            if (sway) ApplySway(go, g.CanopyOnly);                          // Auto+solid (trees) = leaves only; else whole plant

            RecordSidecar(go, prefab.name, brush, fwd);
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
                int target = TargetCount(g, sizeX, sizeZ, countMultiplier);
                int attempts = 0, max = Mathf.Max(8, target * 15);
                while (placed < target && attempts < max)
                { attempts++; if (TryPlaceAt(RangeF(rng, minX, maxX), RangeF(rng, minZ, maxZ), true)) placed++; }
                break;
            }
            case PlacementRule.Center:
            {
                int target = TargetCount(g, sizeX, sizeZ, countMultiplier);
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
            case PlacementRule.Clumps:
            {
                // Bunches on the ground-tile grid: each bunch starts on a random cell and grows through touching
                // cells to 1..cellsPerClump cells; every cell is then filled with propsPerCell tries. TryPlaceAt keeps
                // each prop off the road (with avoidPath); bunches grow under trees and around rocks, as grass does.
                float cellM = GroundBlendBuilder.GroundTexWorldSize;
                int cols = Mathf.Max(1, Mathf.FloorToInt((maxX - minX) / cellM));
                int rows = Mathf.Max(1, Mathf.FloorToInt((maxZ - minZ) / cellM));
                int clumps = TargetCount(g, sizeX, sizeZ, countMultiplier);
                var cells = new HashSet<Vector2Int>();
                var grown = new List<Vector2Int>();
                for (int c = 0; c < clumps; c++)
                {
                    grown.Clear();
                    grown.Add(new Vector2Int(rng.Next(cols), rng.Next(rows)));
                    int want = 1 + rng.Next(Mathf.Max(1, g.cellsPerClump));
                    for (int tries = 0; grown.Count < want && tries < want * 8; tries++)
                    {
                        var from = grown[rng.Next(grown.Count)];
                        var next = from + (rng.Next(4) switch { 0 => Vector2Int.right, 1 => Vector2Int.left, 2 => Vector2Int.up, _ => Vector2Int.down });
                        if (next.x < 0 || next.y < 0 || next.x >= cols || next.y >= rows || grown.Contains(next)) continue;
                        grown.Add(next);
                    }
                    foreach (var cell in grown) cells.Add(cell);
                }
                foreach (var cell in cells)
                {
                    float cx0 = minX + cell.x * cellM, cz0 = minZ + cell.y * cellM;
                    for (int k = 0; k < Mathf.Max(1, g.propsPerCell); k++)
                        if (TryPlaceAt(cx0 + (float)rng.NextDouble() * cellM, cz0 + (float)rng.NextDouble() * cellM, true, underOthers: true)) placed++;
                }
                break;
            }
            case PlacementRule.EdgeRing:
            {
                float step = Mathf.Max(1f, g.minSpacing);
                int cap = g.amountMode == AmountMode.Count ? TargetCount(g, sizeX, sizeZ, countMultiplier) : int.MaxValue;
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
                int cap = g.amountMode == AmountMode.Count ? TargetCount(g, sizeX, sizeZ, countMultiplier) : int.MaxValue;
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
                int cap = g.amountMode == AmountMode.Count ? TargetCount(g, sizeX, sizeZ, countMultiplier) : int.MaxValue;
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
            case PlacementRule.Grid:
            {
                // Buildings on a rectangular block grid. SOLID props auto-skip the path band (see TryPlaceAt), so the
                // routed roads carve the streets and the houses left standing form blocks between them — a walled town.
                // Set randomYaw=false in the recipe for aligned, orderly blocks; clearFromCenter keeps the plaza open.
                float pitch = Mathf.Max(2f, g.minSpacing);
                int cap = g.amountMode == AmountMode.Count ? TargetCount(g, sizeX, sizeZ, countMultiplier) : int.MaxValue;
                for (float x = minX + pitch * 0.5f; x <= maxX && placed < cap; x += pitch)
                    for (float z = minZ + pitch * 0.5f; z <= maxZ && placed < cap; z += pitch)
                        if (TryPlaceAt(x, z, false)) placed++;
                break;
            }
            case PlacementRule.PerimeterWall:
            {
                placed += BuildPerimeterWall(g, parent, pool, minX, maxX, minZ, maxZ, center, mask, blockers);
                break;
            }
        }
        return placed;
    }

    // Assemble the modular wall kit around the plot's inset rectangle: a tower on each corner, straight runs tiling
    // each edge (measured so they butt seamlessly), and a gate opening centred on the SOUTH edge (min Z). Pieces are
    // seated bottom-on-ground (Y0) and registered as blockers so a later Grid/Scatter group won't overlap the wall.
    static int BuildPerimeterWall(RecipePropGroup g, Transform parent, List<GameObject> straight,
                                  float minX, float maxX, float minZ, float maxZ, Vector3 center, int mask,
                                  List<(Vector2 c, float r)> blockers)
    {
        if (straight.Count == 0) return 0;
        var corner   = LoadProp(WallCornerName);
        GameObject tower = null; foreach (var n in WallTowerNames) { tower = LoadProp(n); if (tower != null) break; }
        var entrance = LoadProp(WallEntranceName);
        var gateL    = LoadProp(WallGateLName);
        var gateR    = LoadProp(WallGateRName);
        var cornerPiece = tower != null ? tower : corner;   // square towers hide corner-orientation guesswork

        // Measure one straight piece (at the group's scale) for its run length + which local axis it runs along.
        float segLen = 2f; bool runAxisIsX = true;
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(straight[0]);
            probe.transform.localScale = Vector3.one * Mathf.Max(0.01f, g.scale);
            probe.transform.rotation = Quaternion.identity;
            if (PropPlacement.TryWorldBounds(probe, out var pb))
            { runAxisIsX = pb.size.x >= pb.size.z; segLen = Mathf.Max(0.5f, runAxisIsX ? pb.size.x : pb.size.z); }
            Object.DestroyImmediate(probe);
        }
        float horizYaw = runAxisIsX ? 0f : 90f;   // yaw that makes the run align with X (top/bottom edges)
        float vertYaw  = runAxisIsX ? 90f : 0f;   // yaw that makes the run align with Z (left/right edges)
        float gateHalf = segLen * 0.6f;            // half-width of the gate opening on the south edge (~one piece wide)
        int placed = 0;
        int idx = 0;   // walks the straight-piece pool so Wall_part1/2 alternate
        BrushAndOffset(g, out string brush, out float fwd);   // sidecar brush id (never the reserved "wall" autotile key)

        void Register(GameObject go)
        {
            if (!g.solid) return;
            float r = Mathf.Max(0.5f, segLen * 0.5f);
            if (PropPlacement.TryWorldBounds(go, out var wb)) r = Mathf.Max(r, Mathf.Max(wb.extents.x, wb.extents.z));
            blockers.Add((new Vector2(go.transform.position.x, go.transform.position.z), r));
        }
        void Run(GameObject pf, float x, float z, float yaw)
        { var go = SeatPiece(pf, x, z, yaw, g.scale, g, false, parent, mask); Register(go); RecordSidecar(go, pf.name, brush, fwd); placed++; }

        // 4 corner towers
        Run(cornerPiece, minX, minZ, 0f); Run(cornerPiece, maxX, minZ, 90f);
        Run(cornerPiece, maxX, maxZ, 180f); Run(cornerPiece, minX, maxZ, 270f);

        // South edge (z=minZ) with a centred gate gap; North edge (z=maxZ) flipped to face inward.
        for (float x = minX + segLen; x <= maxX - segLen * 0.5f; x += segLen)
        {
            if (Mathf.Abs(x - center.x) <= gateHalf) continue;   // leave the gate opening
            Run(straight[idx++ % straight.Count], x, minZ, horizYaw);
        }
        for (float x = minX + segLen; x <= maxX - segLen * 0.5f; x += segLen)
            Run(straight[idx++ % straight.Count], x, maxZ, horizYaw + 180f * WallFacingSign);

        // West edge (x=minX); East edge (x=maxX) flipped to face inward.
        for (float z = minZ + segLen; z <= maxZ - segLen * 0.5f; z += segLen)
            Run(straight[idx++ % straight.Count], minX, z, vertYaw);
        for (float z = minZ + segLen; z <= maxZ - segLen * 0.5f; z += segLen)
            Run(straight[idx++ % straight.Count], maxX, z, vertYaw + 180f * WallFacingSign);

        // Gate on the south edge: a single entrance piece if the kit has one, else the L/R gate pair flanking centre.
        if (entrance != null) Run(entrance, center.x, minZ, horizYaw);
        else
        {
            if (gateL != null) Run(gateL, center.x - gateHalf * 0.5f, minZ, horizYaw);
            if (gateR != null) Run(gateR, center.x + gateHalf * 0.5f, minZ, horizYaw);
        }
        return placed;
    }

    // Instantiate one prefab, scale + yaw it, seat its bounds-bottom on the ground (Y0), parent + layer it, and
    // strip/sway per the group. Used by the structured rules (PerimeterWall) that need an EXPLICIT yaw rather than
    // the random-yaw scatter path. Caller registers blockers if it wants later groups to avoid this piece.
    static GameObject SeatPiece(GameObject prefab, float x, float z, float yawDeg, float scale,
                                RecipePropGroup g, bool sway, Transform parent, int mask)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
        PropPlacement.PlaceOnGround(go, new Vector3(x, 0f, z), mask, g.solid);
        go.transform.SetParent(parent, true);
        WorldBuildUtil.SetLayerRecursive(go, 0);
        if (!g.solid) PropPlacement.StripColliders(go);
        if (sway) ApplySway(go, g.CanopyOnly);
        return go;
    }



    // ── SIDECAR (painter-editable map.json) ────────────────────────────────────────────────────────────────
    // Resolve a prop group's painter brush id + the yaw correction to bake into the sidecar. Brush = the group's
    // label (or its category name), lowercased; facing correction from the per-category seed. Never emit the reserved
    // "wall" key (the importer would autotile it from cells, dropping the exact pieces) — generated walls round-trip
    // as explicit prefab objects.
    static void BrushAndOffset(RecipePropGroup g, out string brush, out float fwd)
    {
        fwd = ForwardOffsetSeed(g.category);
        string src = string.IsNullOrEmpty(g.label) ? g.category.ToString() : g.label;
        brush = Reserve(src.Trim().ToLowerInvariant().Replace(' ', '_'));
    }

    // The mesh-front facing correction per prop family (the only non-zero source pack quirk: Pxltgr houses face −Z).
    static float ForwardOffsetSeed(PropCategory c) => c == PropCategory.Houses ? 180f : 0f;

    static string Reserve(string brush) => brush == "wall" ? "wall_pieces" : brush;

    // Record one placed prop into the per-bake sidecar capture (no-op if capture is off). World yaw read back from
    // the seated instance; MapSidecar inverts the importer's centre-translate + vertical-flip + forwardOffset.
    static void RecordSidecar(GameObject go, string prefab, string brush, float fwd)
    {
        if (_sidecarObjects == null || go == null) return;
        _sidecarObjects.Add(MapSidecar.ObjectFromWorld(brush, prefab, go.transform.position,
                                                       go.transform.eulerAngles.y, _sidecarSizeX, _sidecarSizeZ, fwd));
    }

    // Painter ground-brush id for a recipe path tile: ask the palette which brush it bound to that blend channel
    // (1 road · 2 stone · 3 rock · 4 soil), so the sidecar names ground cells the same way the author's palette does.
    // Falls back to the default ground_* ids when there's no palette or that channel is unfilled.
    static string PainterRoadBrush(PathTile t)
    {
        int ch = t switch { PathTile.Road => 1, PathTile.Stones => 2, PathTile.Rock => 3, PathTile.Soil => 4, _ => 1 };
        string id = LoadPalette()?.GroundIdForChannel(ch);
        if (!string.IsNullOrEmpty(id)) return id;
        return t switch
        {
            PathTile.Road   => "path_dirt_road",
            PathTile.Stones => "path_or_sidewalk_dark_cobble",
            PathTile.Rock   => "base_or_patch_or_path_gravel_scree",
            PathTile.Soil   => "base_or_patch_or_path_soil",
            _               => "path_dirt_road",
        };
    }

    // Painter ground-brush id for a biome's road tile: the palette brush showing that exact tile, else the tile's own
    // name in the painter's id form (lowercase), which is what the palette would call it once added.
    static string PainterBrushForTile(string tileName)
    {
        var pal = LoadPalette();
        if (pal != null)
            foreach (var rb in pal.Resolve())
                if (rb.IsGround && rb.groundTex == tileName) return rb.id;
        return tileName.ToLowerInvariant();
    }

    // Write <name>.map.json beside the painter: captured props (lossless layout/facing) + the ground rasterised from
    // the SAME procedural sampler the bake used (roads + dirt patches), so the painter's Import reproduces this map.
    static void WriteSidecar(string mapName, MapRecipe r, Vector3 center, int seed, IReadOnlyList<PathRouting.Route> paths,
                             string roadBrush, float patchAmount)
    {
        var pal = LoadPalette();
        float cellSize = pal != null && pal.cellSize > 0f ? pal.cellSize : 4f;
        int cols = Mathf.Max(1, Mathf.RoundToInt(r.sizeX / cellSize));
        int rows = Mathf.Max(1, Mathf.RoundToInt(r.sizeZ / cellSize));

        var sampler = GroundBlendBuilder.MakeProceduralSampler(seed, patchAmount, paths, r.pathWidth,
                                                              r.groundStyle == GroundStyle.HardEdge);
        var ground = MapSidecar.RasterizeGround(sampler, center, r.sizeX, r.sizeZ, cellSize, cols, rows, roadBrush);

        var doc = new MapSidecar.Doc
        {
            name = mapName, cellSize = cellSize,
            size = new MapSidecar.Vec2(r.sizeX, r.sizeZ), cols = cols, rows = rows,
            ground = ground, objects = _sidecarObjects ?? new List<MapSidecar.ObjectCell>(),
        };
        try
        {
            MapSidecar.Write(doc, mapName);
            Debug.Log($"[unwritten] Sidecar → {MapSidecar.PathFor(mapName)} ({doc.objects.Count} props, {ground.Count} ground cells). Open it in the Map Painter (Import map…) to edit.");
        }
        catch (System.Exception e) { Debug.LogWarning($"[unwritten] Could not write map sidecar for '{mapName}': {e.Message}"); }
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────────────────
    static int TargetCount(RecipePropGroup g, float sizeX, float sizeZ, float countMultiplier)
        => Mathf.Max(0, Mathf.RoundToInt((g.amountMode == AmountMode.Count ? g.count : sizeX * sizeZ * g.density) * countMultiplier));

    static float RangeF(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    static GameObject LoadProp(string name) => PropResolver.Load(name);   // resolves by name across Props subfolders

    // ── WIND SWAY ──────────────────────────────────────────────────────────────────────────────────────────
    // The height a prop stands in the world at its own top object's scale — the units the foliage shader reads its
    // full-bend height in. Read from the meshes, because a prefab in the Project window reports bounds that leave its
    // scale out: the canopy materials were sized 19.4 for pines that stand 5.9 tall at 0.3, so trees barely moved.
    static float ModelHeight(Transform top, MeshRenderer[] rends)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var r in rends)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var toTop = top.worldToLocalMatrix * r.transform.localToWorldMatrix;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? mb.min.x : mb.max.x, (c & 2) == 0 ? mb.min.y : mb.max.y, (c & 4) == 0 ? mb.min.z : mb.max.z);
                float y = toTop.MultiplyPoint3x4(corner).y;
                lo = Mathf.Min(lo, y);
                hi = Mathf.Max(hi, y);
            }
        }
        return hi > lo ? Mathf.Max((hi - lo) * top.lossyScale.y, 0.5f) : 0.5f;
    }

    // Re-material a prop's renderers onto Unwritten/FoliageWind so its vertices bend with the world wind, reusing
    // one cloned material per (source material, profile) so instances stay batched. Two profiles:
    //   • DECOR  (canopy = false): whole-plant decor (flowers/grass/ferns) — sway EVERY material, snappy default look.
    //   • CANOPY (canopy = true):  trees/bushes — sway ONLY the leaf material (the trunk/bark stays rigid), with
    //                              taller/gentler/slower params so the crown rustles instead of the whole tree whipping.
    internal static void ApplySway(GameObject go, bool canopy)
    {
        // Found here too, for a caller that is not a map Generate (the seamless world builder).
        if (_foliageWind == null) _foliageWind = Shader.Find("Unwritten/FoliageWind");
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
    // Plant, … while trunks are *_bark).
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

    // Save a runtime sway material as a real .mat asset so it survives being assigned to a PREFAB asset (an
    // in-memory material written into a prefab serializes as a NULL slot → the prop turns pink). Deterministic
    // path under Prefabs/Props/Materials, so re-running REPLACES the same asset in place rather than piling up copies.
    internal static Material PersistSwayMaterial(Material m)
    {
        if (EditorUtility.IsPersistent(m)) return m;
        const string dir = "Assets/1_Authored/Prefabs/Props/Materials";
        if (!AssetDatabase.IsValidFolder(dir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/1_Authored/Prefabs")) AssetDatabase.CreateFolder("Assets/1_Authored", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/1_Authored/Prefabs/Props")) AssetDatabase.CreateFolder("Assets/1_Authored/Prefabs", "Props");
            AssetDatabase.CreateFolder("Assets/1_Authored/Prefabs/Props", "Materials");
        }
        string path = $"{dir}/{m.name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { EditorUtility.CopySerialized(m, existing); return existing; }
        AssetDatabase.CreateAsset(m, path);
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

    // ── BRUSH PALETTE ────────────────────────────────────────────────────────────────────────────────────
    // The BrushPalette asset (edited in Unity, exported to the web painter) supplies the painter ground-brush ids
    // for the sidecar round-trip + the cell size. The procedural prop lists live in CategoryNamesSeed below (the
    // painter palette is now freeform assets, no longer a category→prefab map). Cached per domain reload.
    static BrushPalette _palette; static bool _paletteSearched;
    internal static BrushPalette LoadPalette()
    {
        if (_paletteSearched) return _palette;
        _paletteSearched = true;
        var guids = AssetDatabase.FindAssets("t:BrushPalette");
        if (guids.Length > 0) _palette = AssetDatabase.LoadAssetAtPath<BrushPalette>(AssetDatabase.GUIDToAssetPath(guids[0]));
        return _palette;
    }

    // Canonical Pxltgr prefab names per family (LOD0 where the pack ships LODs) — the procedural generator's source.
    internal static string[] CategoryNames(PropCategory c) => CategoryNamesSeed(c);

    static string[] CategoryNamesSeed(PropCategory c) => c switch
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
