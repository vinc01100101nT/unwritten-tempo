using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// PHASE 3 of the Map Painter pipeline (see the <c>map-painter-pipeline</c> memory): the Unity importer that bakes a
/// hand-painted <c>map.json</c> (exported by <c>tools/map-painter/index.html</c>) into a playable scene. The intra-map
/// complement to <see cref="RecipeGenerator"/>: where the recipe scatters content procedurally, this places EXACTLY
/// what the author painted — every road cell, every house and its yaw.
///
/// It is a thin orchestrator over the same battle-tested building blocks the recipe uses, so the output matches:
/// <list type="bullet">
/// <item>Ground: <see cref="GroundBlendBuilder.LayPlotSampled"/> — one blended grass→dirt→road mesh, but the per-vertex
/// path/dirt weights are sampled (bilinearly) from the painted cell grid instead of from noise + routes.</item>
/// <item>Props: <see cref="PropPlacement.PlaceOnGround"/> — bounds-driven seating (Y0, any pivot), at the prop's
/// NATIVE prefab size (nothing rescales it) and rotated to the painted yaw.</item>
/// <item>Spawn: <see cref="RegionBuilder.MakeSpawn"/>. Warp wiring stays <see cref="WorldCreator"/>/MapLinker's job.</item>
/// </list>
///
/// COORDINATE MAPPING (locked): the painter's world XZ is preserved with a pure translation that centres the map on
/// the origin — <c>worldX = x - sizeX/2</c>, <c>worldZ = z - sizeZ/2</c>, yaw applied directly. Because that's an
/// isometry (no mirror), every relative position AND facing the author painted is preserved exactly; the map's "north"
/// (the painter's top row) simply lands on −Z, which is cosmetic for a fresh map with no neighbours yet.
/// </summary>
public class MapPainterImporter : EditorWindow
{
    const string PropsDir  = "Assets/1_Authored/Prefabs/Props";
    const string ScenesDir = "Assets/Scenes";
    // Props are placed at their NATIVE prefab size (nothing rescales them). The modular WALL kit is the one
    // exception: it's tiled to fill grid cells, so it keeps its own structural fit-scale — unrelated to prop sizing.
    const float WallKitScale = 0.3f;

    // ── painter JSON schema (matches the exporter in tools/map-painter/index.html) ───────────────────────
    [System.Serializable] class Vec2     { public float x, z; }
    [System.Serializable] class GroundCell { public string brush; public int cx, cz; }
    [System.Serializable] class HeightCell { public int cx, cz, level; }
    [System.Serializable] class ObjectCell { public string brush, prefab; public float x, z, yaw; }
    [System.Serializable] class PaintedMap
    {
        public int version;
        public string name;
        public float cellSize = 4f;
        public Vec2 size;
        public int cols, rows;
        public GroundCell[] ground;
        public HeightCell[] heights;
        public ObjectCell[] objects;
    }

    string _jsonPath;
    string _mapName = "Prontera";

    // Where the painted content lands: a brand-new scene, or merged into the scene already open in Unity.
    enum Target { NewScene, OpenScene }
    Target _target = Target.NewScene;
    // When merging into the open scene, each category the author ticks is WIPED (its prior import) and rebuilt from the
    // JSON; unticked categories are left exactly as they are. Lets you re-import just the ground and keep hand-placed props.
    bool _impGround = true, _impProps = true, _impWalls = true, _impSpawn = true;

    [MenuItem("Tools/unwritten/World/Import Painted Map…", priority = 181)]
    static void Open()
    {
        var w = GetWindow<MapPainterImporter>(true, "Import Painted Map");
        w.minSize = new Vector2(440, 230);
        w._jsonPath = DefaultJsonPath();
        w.Show();
    }

    // tools/map-painter/map.json sits beside the painter, two levels above the Unity project's Assets folder.
    static string DefaultJsonPath()
    {
        string p = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "tools", "map-painter", "map.json"));
        return File.Exists(p) ? p : "";
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Bake a hand-painted map.json into a new scene, or merge it into the open scene.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel("map.json");
            _jsonPath = EditorGUILayout.TextField(_jsonPath);
            if (GUILayout.Button("Browse…", GUILayout.Width(72)))
            {
                string start = !string.IsNullOrEmpty(_jsonPath) && File.Exists(_jsonPath)
                    ? Path.GetDirectoryName(_jsonPath) : Application.dataPath;
                string picked = EditorUtility.OpenFilePanel("Select painted map.json", start, "json");
                if (!string.IsNullOrEmpty(picked)) { _jsonPath = picked; TryPrefillName(); }
            }
        }

        EditorGUILayout.Space();
        _target = (Target)EditorGUILayout.EnumPopup(new GUIContent("Import into"), _target);

        var active = SceneManager.GetActiveScene();
        bool openSaved = _target == Target.OpenScene && active.IsValid() && !string.IsNullOrEmpty(active.path);

        if (_target == Target.NewScene)
        {
            _mapName = EditorGUILayout.TextField(new GUIContent("Scene name", "Saved as Assets/Scenes/<name>.unity"), _mapName);
        }
        else
        {
            EditorGUILayout.LabelField("Open scene", active.IsValid() && !string.IsNullOrEmpty(active.path)
                ? active.name + "  (" + active.path + ")" : "— none saved —");
            if (!openSaved)
                EditorGUILayout.HelpBox("Open and SAVE a scene in Unity first — then it becomes the merge target.", MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Overwrite (ticked = wiped & rebuilt · unticked = left as-is):", EditorStyles.boldLabel);
            _impGround = EditorGUILayout.ToggleLeft(new GUIContent("Ground", "Replace the blended ground mesh (Ground_*)"), _impGround);
            _impProps  = EditorGUILayout.ToggleLeft(new GUIContent("Props", "Replace the painted props (the 'Props' group)"), _impProps);
            _impWalls  = EditorGUILayout.ToggleLeft(new GUIContent("Walls", "Replace the autotiled wall kit (the 'Wall' group)"), _impWalls);
            _impSpawn  = EditorGUILayout.ToggleLeft(new GUIContent("Spawn point", "Replace the 'Start' arrival point"), _impSpawn);
        }

        EditorGUILayout.Space();
        bool jsonOk = !string.IsNullOrEmpty(_jsonPath) && File.Exists(_jsonPath);
        bool ready = jsonOk && (_target == Target.NewScene ? !string.IsNullOrWhiteSpace(_mapName)
                                                           : openSaved && (_impGround || _impProps || _impWalls || _impSpawn));
        using (new EditorGUI.DisabledScope(!ready))
            if (GUILayout.Button(_target == Target.NewScene ? "Import & Bake Scene" : "Merge Into Open Scene", GUILayout.Height(34)))
                Import();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "After import: run World ▸ Apply Foliage Wind to Open Scene to make the trees sway, and "
            + "World ▸ Link Two Maps… to wire warps to neighbours.", MessageType.None);
    }

    void TryPrefillName()
    {
        try
        {
            var m = JsonUtility.FromJson<PaintedMap>(File.ReadAllText(_jsonPath));
            if (m != null && !string.IsNullOrWhiteSpace(m.name) && m.name != "PaintedMap") _mapName = m.name;
        }
        catch { /* leave the current name */ }
    }

    // ── IMPORT ────────────────────────────────────────────────────────────────────────────────────────
    void Import()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Import Painted Map", "Exit Play mode first, then re-run.", "OK"); return; }

        PaintedMap map;
        try { map = JsonUtility.FromJson<PaintedMap>(File.ReadAllText(_jsonPath)); }
        catch (System.Exception e)
        { EditorUtility.DisplayDialog("Import Painted Map", "Couldn't parse the JSON:\n" + e.Message, "OK"); return; }

        if (map == null || map.size == null || map.size.x <= 0 || map.size.z <= 0)
        { EditorUtility.DisplayDialog("Import Painted Map", "The map.json is missing a valid 'size'.", "OK"); return; }

        float sizeX = map.size.x, sizeZ = map.size.z;
        float cellSize = map.cellSize > 0 ? map.cellSize : 4f;
        int cols = map.cols > 0 ? map.cols : Mathf.Max(1, Mathf.RoundToInt(sizeX / cellSize));
        int rows = map.rows > 0 ? map.rows : Mathf.Max(1, Mathf.RoundToInt(sizeZ / cellSize));

        // VERTICAL-FLIP FIX: the painter's row 0 is NORTH (top of its canvas) and its z grows DOWNWARD (south), but the
        // game shows +Z as the far/north edge — so a direct copy bakes the map upside-down (the painted top row lands at
        // −Z). Reflect the painted z about the map centre on the way in (north → +Z) so the scene matches the canvas.
        // Yaw is deliberately left untouched: the painter measures yaw clockwise-from-north on a y-down canvas, which is
        // already the mirror of Unity's CCW-from-+Z, so the two reflections cancel and every facing stays correct.
        // Done here at the source, so all downstream ground/wall/gate/extension math keeps its existing orientation.
        if (map.objects != null)
            foreach (var o in map.objects) if (o != null) o.z = sizeZ - o.z;
        if (map.ground != null)
            foreach (var c in map.ground) if (c != null) c.cz = (rows - 1) - c.cz;
        if (map.heights != null)
            foreach (var h in map.heights) if (h != null) h.cz = (rows - 1) - h.cz;

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Import Painted Map", "No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers.", "OK"); return; }
        int mask = 1 << groundLayer;

        var palette = LoadPalette();
        // Resolve the palette ONCE: painted ground-brush id → its blend channel (0 base … 4), and channel → the
        // ground texture the author assigned. Object/wall brush id → its facing correction. Single source of truth.
        var resolved = palette != null ? palette.Resolve() : new List<ResolvedBrush>();
        var groundChannel = new Dictionary<string, int>();
        var chTex = new string[BrushPalette.MaxGroundChannels];
        var fwdOffset = new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var rb in resolved)
        {
            if (rb.IsGround && rb.channel >= 0 && rb.channel < chTex.Length)
            { groundChannel[rb.id] = rb.channel; chTex[rb.channel] = rb.groundTex; }
            if (!string.IsNullOrEmpty(rb.id)) fwdOffset[rb.id] = rb.forwardOffset;
        }
        string GroundTexFor(int ch, string fallback) => !string.IsNullOrEmpty(chTex[ch]) ? chTex[ch] : fallback;

        // ── TARGET SCENE: brand-new, or merge into the scene already open ──────────────────────────────
        // In OpenScene mode each ticked category is a REPLACE — wipe its prior import from the scene, then rebuild the
        // ticked ones; unticked categories are never touched. NewScene mode builds everything into a fresh empty scene.
        bool open = _target == Target.OpenScene;
        bool doGround = !open || _impGround;
        bool doProps  = !open || _impProps;
        bool doWalls  = !open || _impWalls;
        bool doSpawn  = !open || _impSpawn;

        string safe, scenePath;
        Scene scene;
        if (open)
        {
            scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            { EditorUtility.DisplayDialog("Import Painted Map", "Open and save a scene in Unity first, then re-run — that scene is the merge target.", "OK"); return; }
            scenePath = scene.path;
            safe = Sanitize(scene.name);
            ClearImported(scene, doGround, doProps, doWalls, doSpawn);
        }
        else
        {
            safe = Sanitize(string.IsNullOrWhiteSpace(_mapName) ? "PaintedMap" : _mapName);
            scenePath = $"{ScenesDir}/{safe}.unity";
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            RigBuilder25D.BuildLight();   // authoring Sun (disabled at Play; Systems owns the runtime light)
        }

        // ── GROUND: rasterise the painted cells into per-channel weight fields, then sample them per vertex ─
        var pathF  = new float[cols, rows];   // .r road
        var stoneF = new float[cols, rows];   // .g stone
        var rockF  = new float[cols, rows];   // .b rock
        var dirtF  = new float[cols, rows];   // .a dirt
        var unmapped = new HashSet<string>();
        if (map.ground != null)
            foreach (var c in map.ground)
            {
                if (c == null || c.cx < 0 || c.cx >= cols || c.cz < 0 || c.cz >= rows) continue;
                int ch = groundChannel.TryGetValue(c.brush, out var cc) ? cc : -1;
                switch (ch)
                {
                    case 1: pathF [c.cx, c.cz] = 1f; break;   // overlay 1 → RED
                    case 2: stoneF[c.cx, c.cz] = 1f; break;   // overlay 2 → GREEN
                    case 3: rockF [c.cx, c.cz] = 1f; break;   // overlay 3 → BLUE
                    case 4: dirtF [c.cx, c.cz] = 1f; break;   // overlay 4 → ALPHA
                    case 0: break;                            // base underlayer
                    default: unmapped.Add(c.brush); break;    // unknown brush → left as the base layer
                }
            }

        void Sample(float wx, float wz, out Vector4 w)
        {
            float fx = (wx + sizeX * 0.5f) / cellSize - 0.5f;   // world → painter cell coords (cell centres)
            float fz = (wz + sizeZ * 0.5f) / cellSize - 0.5f;
            w = new Vector4(Bilinear(pathF,  fx, fz, cols, rows), Bilinear(stoneF, fx, fz, cols, rows),
                            Bilinear(rockF,  fx, fz, cols, rows), Bilinear(dirtF,  fx, fz, cols, rows));
        }

        // ── ELEVATION: read the painted per-cell height notches (gutter = −1, raised = +1, …) ───────────────
        var levels = new int[cols, rows];
        bool hasElevation = false;
        if (map.heights != null)
            foreach (var h in map.heights)
            {
                if (h == null || h.cx < 0 || h.cx >= cols || h.cz < 0 || h.cz >= rows) continue;
                levels[h.cx, h.cz] = h.level;
                if (h.level != 0) hasElevation = true;
            }

        // Elevation-aware weights: a cell at a DIFFERENT height than refLevel doesn't feather in, so the ground
        // texture cuts hard at a step (a raised plaza keeps its own tile up to the cliff edge) but still blends softly
        // between same-level cells. refLevel = the height of the cell whose top/wall this vertex belongs to.
        void SampleLeveled(float wx, float wz, int refLevel, out Vector4 w)
        {
            float fx = (wx + sizeX * 0.5f) / cellSize - 0.5f;
            float fz = (wz + sizeZ * 0.5f) / cellSize - 0.5f;
            w = new Vector4(
                BilinearMasked(pathF,  fx, fz, cols, rows, levels, refLevel),
                BilinearMasked(stoneF, fx, fz, cols, rows, levels, refLevel),
                BilinearMasked(rockF,  fx, fz, cols, rows, levels, refLevel),
                BilinearMasked(dirtF,  fx, fz, cols, rows, levels, refLevel));
        }

        if (doGround)
        {
            var ground = hasElevation
                ? GroundBlendBuilder.LayPlotSampledElevated(
                    scene, $"Ground_{safe}", Vector3.zero, sizeX, sizeZ, groundLayer, SampleLeveled,
                    levels, cols, rows, cellSize,
                    GroundTexFor(0, "ground_grass"),
                    GroundTexFor(4, "ground_soil"),
                    GroundTexFor(1, "ground_road"),
                    GroundTexFor(2, "ground_stones"),
                    GroundTexFor(3, "ground_rock"))
                : GroundBlendBuilder.LayPlotSampled(
                    scene, $"Ground_{safe}", Vector3.zero, sizeX, sizeZ, groundLayer, Sample,
                    GroundTexFor(0, "ground_grass"),
                    GroundTexFor(4, "ground_soil"),
                    GroundTexFor(1, "ground_road"),
                    GroundTexFor(2, "ground_stones"),
                    GroundTexFor(3, "ground_rock"));
            if (ground == null)
            {
                EditorUtility.DisplayDialog("Import Painted Map",
                    "Couldn't build the ground — need the Pxltgr 'ground_grass' prefab in Assets/1_Authored/Prefabs/Props and the "
                    + "'Unwritten/GroundBlend' shader. Run Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first.", "OK");
                if (!open) EditorSceneManager.CloseScene(scene, true);
                return;
            }
        }
        // Make the floor queryable for the prop/wall drop-rays — the just-built ground, or (props-only merge) the existing one.
        Physics.SyncTransforms();

        // ── WALL CELLS: collect the painted wall-brush cells first (autotiled below) — done even when props are skipped,
        //    since the wall kit and its gate openings both read this set. Wall brush ignores the painted prefab/yaw.
        var wallCells = new HashSet<(int cx, int cz)>();
        if (map.objects != null)
            foreach (var o in map.objects)
            {
                if (o == null || string.IsNullOrEmpty(o.brush) || o.brush.ToLowerInvariant() != "wall") continue;
                int wcx = Mathf.FloorToInt(o.x / cellSize), wcz = Mathf.FloorToInt(o.z / cellSize);
                if (wcx >= 0 && wcx < cols && wcz >= 0 && wcz < rows) wallCells.Add((wcx, wcz));
            }

        // ── PROPS: one parent node per brush; seat each prop at its painted XZ + yaw ───────────────────────
        var missing = new HashSet<string>();
        int placed = 0, decor = 0;
        if (doProps)
        {
            var propsRoot = new GameObject("Props");
            SceneManager.MoveGameObjectToScene(propsRoot, scene);
            var groups = new Dictionary<string, Transform>();
            // fwdOffset (the palette's per-brush facing correction that aligns a mesh's real front to the painted
            // arrow) was resolved once up top alongside the ground channels.

            if (map.objects != null)
                foreach (var o in map.objects)
                {
                    if (o == null) continue;
                    if (!string.IsNullOrEmpty(o.brush) && o.brush.ToLowerInvariant() == "wall") continue;   // → wall kit
                    if (string.IsNullOrEmpty(o.prefab)) continue;
                    var prefab = PropResolver.Load(o.prefab);
                    if (prefab == null) { missing.Add(o.prefab); continue; }

                    string groupName = string.IsNullOrEmpty(o.brush) ? "Props" : Capitalize(o.brush);
                    if (!groups.TryGetValue(groupName, out var parent))
                    {
                        var g = new GameObject(groupName);
                        g.transform.SetParent(propsRoot.transform, false);
                        parent = g.transform;
                        groups[groupName] = parent;
                    }

                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    // Leave localScale as the prefab's own — the prefab owns its size (eyeballed + applied); nothing here rescales it.
                    float off = (!string.IsNullOrEmpty(o.brush) && fwdOffset.TryGetValue(o.brush, out var fo)) ? fo : 0f;
                    go.transform.rotation = Quaternion.Euler(0f, o.yaw + off, 0f);
                    PropPlacement.PlaceOnGround(go, new Vector3(o.x - sizeX * 0.5f, 0f, o.z - sizeZ * 0.5f), mask);
                    go.transform.SetParent(parent, true);
                    WorldBuildUtil.SetLayerRecursive(go, 0);                 // Default — never the Ground layer
                    if (PropPlacement.IsWalkThroughDecor(o.prefab)) { PropPlacement.StripColliders(go); decor++; }
                    placed++;
                }
        }

        // ── WALLS: autotile the painted wall cells into the modular kit (straights + corner/end towers + gates). Its own
        //    top-level "Wall" group (sibling of Props) so it can be re-imported independently of props.
        int walls = 0;
        if (doWalls && wallCells.Count > 0)
        {
            var wallsRoot = new GameObject("Wall");
            SceneManager.MoveGameObjectToScene(wallsRoot, scene);
            walls = AutotileWalls(wallCells, pathF, wallsRoot.transform, sizeX, sizeZ, cellSize, cols, rows, mask, WallKitScale);
        }

        // ── SPAWN (boot/arrival point; warps added later via MapLinker) ──────────────────────────────────
        if (doSpawn)
            RegionBuilder.MakeSpawn(scene, "Start", WorldBuildUtil.SurfacePoint(Vector3.zero, mask));

        // ── SAVE + REGISTER ──────────────────────────────────────────────────────────────────────────────
        if (open)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        else
        {
            if (!Directory.Exists(ScenesDir)) AssetDatabase.CreateFolder("Assets", "Scenes");
            var dup = SceneManager.GetSceneByPath(scenePath);
            if (dup.IsValid() && dup.isLoaded && dup != scene) EditorSceneManager.CloseScene(dup, true);
            EditorSceneManager.SaveScene(scene, scenePath);
            WorldBuildUtil.RegisterInBuildSettings(scenePath);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }
        AssetDatabase.SaveAssets();

        // Only report categories that were actually (re)built, so a partial merge doesn't imply it touched everything.
        var parts = new List<string>();
        if (doProps)  parts.Add($"{placed} props ({decor} walk-through)");
        if (doWalls)  parts.Add($"{walls} autotiled wall piece(s)");
        if (doGround) parts.Add($"ground {cols}×{rows} cells ({sizeX}×{sizeZ} u)" + (hasElevation ? " · elevated (gutters/steps + cliff collision)" : " · flat"));
        if (doSpawn)  parts.Add("spawn point");
        string verb = open ? "Merged into" : "Imported";
        var msg = $"{verb} '{safe}':\n• " + string.Join("\n• ", parts) + $"\n→ {scenePath}";
        if (missing.Count  > 0) msg += "\n\n⚠ Missing prefabs (skipped): " + string.Join(", ", missing);
        if (doGround && unmapped.Count > 0) msg += "\n\n⚠ Ground brushes not in the palette (left as base layer): " + string.Join(", ", unmapped);
        Debug.Log("[unwritten] " + msg.Replace("\n", " "));
        EditorUtility.DisplayDialog("Import Painted Map", msg, "OK");
    }

    // Remove a prior import's category objects from the scene so the ticked ones can be rebuilt cleanly. Matches by the
    // names the importer bakes: ground mesh = "Ground_*", props group = "Props", wall kit = "Wall", spawn = "Start".
    static void ClearImported(Scene scene, bool ground, bool props, bool walls, bool spawn)
    {
        foreach (var go in scene.GetRootGameObjects())
        {
            string n = go.name;
            if (ground && n.StartsWith("Ground_")) { Object.DestroyImmediate(go); continue; }
            if (props && n == "Props")             { Object.DestroyImmediate(go); continue; }
            if (walls && n == "Wall")              { Object.DestroyImmediate(go); continue; }
            if (spawn && n == "Start")             { Object.DestroyImmediate(go); continue; }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────
    // Bilinear sample of a cell-resolution weight field at fractional cell coords; clamps to the grid edge so the
    // border feathers out cleanly. Cell pitch (≈4u) becomes the gradient width → soft road/dirt edges like the recipe.
    static float Bilinear(float[,] f, float fx, float fz, int cols, int rows)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, cols - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, rows - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, cols - 1);
        int z1 = Mathf.Clamp(z0 + 1, 0, rows - 1);
        float tx = Mathf.Clamp01(fx - x0), tz = Mathf.Clamp01(fz - z0);
        float a = Mathf.Lerp(f[x0, z0], f[x1, z0], tx);
        float b = Mathf.Lerp(f[x0, z1], f[x1, z1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    // Like Bilinear, but any of the 4 corner cells at a level ≠ refLevel is replaced by the nearest SAME-level corner's
    // value before interpolating. Effect: weights never bleed across a height step (hard cut at the cliff), while
    // same-level cells still feather smoothly. refLevel is the height of the cell that owns this vertex.
    static float BilinearMasked(float[,] f, float fx, float fz, int cols, int rows, int[,] levels, int refLevel)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, cols - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, rows - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, cols - 1);
        int z1 = Mathf.Clamp(z0 + 1, 0, rows - 1);
        float tx = Mathf.Clamp01(fx - x0), tz = Mathf.Clamp01(fz - z0);

        // Bilinear influence of each corner → pick the same-level corner with the most influence as the fallback value.
        float w00 = (1 - tx) * (1 - tz), w10 = tx * (1 - tz), w01 = (1 - tx) * tz, w11 = tx * tz;
        float refVal = f[x0, z0]; float bestW = -1f; bool found = false;
        void Consider(int cx, int cz, float ww)
        { if (levels[cx, cz] == refLevel && ww > bestW) { bestW = ww; refVal = f[cx, cz]; found = true; } }
        Consider(x0, z0, w00); Consider(x1, z0, w10); Consider(x0, z1, w01); Consider(x1, z1, w11);
        if (!found) return Bilinear(f, fx, fz, cols, rows);   // no same-level corner (shouldn't happen) → plain blend

        float v00 = levels[x0, z0] == refLevel ? f[x0, z0] : refVal;
        float v10 = levels[x1, z0] == refLevel ? f[x1, z0] : refVal;
        float v01 = levels[x0, z1] == refLevel ? f[x0, z1] : refVal;
        float v11 = levels[x1, z1] == refLevel ? f[x1, z1] : refVal;
        float a = Mathf.Lerp(v00, v10, tx);
        float b = Mathf.Lerp(v01, v11, tx);
        return Mathf.Lerp(a, b, tz);
    }

    static BrushPalette LoadPalette()
    {
        var guids = AssetDatabase.FindAssets("t:BrushPalette");
        return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<BrushPalette>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    static GameObject LoadProp(string name) => PropResolver.Load(name);   // resolves by name across Props subfolders

    // ── WALL AUTOTILING ──────────────────────────────────────────────────────────────────────────────────
    // The modular Pxltgr wall kit. Straights tile the runs; towers cap corners/ends/junctions (dodging the kit's
    // unknown corner-orientation, same reasoning as RecipeGenerator); an entrance opens a gate.
    static readonly string[] WallStraightNames = { "Wall_part2", "Wall_part1" };
    static readonly string[] WallTowerNames    = { "Wall_tower1", "Wall_tower2" };
    const string WallEntranceName = "Wall_entrance";

    // Assemble the kit over the painted wall cells. Each cell's 4-neighbour pattern picks the piece:
    //   • 2 opposite neighbours (E+W or N+S) → a STRAIGHT run, scaled to fill the cell, yawed along the run
    //   • a straight cell that's ALSO a road cell → a GATE (entrance) — i.e. paint a road through the wall
    //   • anything else (corner / end / T / cross / isolated) → a TOWER post
    // Pieces seat bottom-on-ground (Y0) and stay SOLID so they block the player + pathfinder. Returns the count.
    static int AutotileWalls(HashSet<(int cx, int cz)> cells, float[,] pathF, Transform wallRoot,
                             float sizeX, float sizeZ, float cellSize, int cols, int rows, int mask, float baseScale)
    {
        if (cells == null || cells.Count == 0) return 0;
        GameObject straight = null; foreach (var n in WallStraightNames) { straight = LoadProp(n); if (straight != null) break; }
        GameObject tower    = null; foreach (var n in WallTowerNames)    { tower    = LoadProp(n); if (tower != null) break; }
        var entrance = LoadProp(WallEntranceName);
        if (straight == null && tower == null)
        { Debug.LogWarning("[unwritten] Wall kit not found (Wall_part*/Wall_tower* in Assets/1_Authored/Prefabs/Props); walls skipped."); return 0; }

        // Measure one straight piece at base scale → run length + which local axis it runs along, so we can scale it
        // to fill a cell and yaw it along the run (same trick as RecipeGenerator.BuildPerimeterWall).
        float segLen = cellSize; bool runAxisIsX = true;
        if (straight != null)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(straight);
            probe.transform.localScale = Vector3.one * Mathf.Max(0.01f, baseScale);
            probe.transform.rotation = Quaternion.identity;
            if (PropPlacement.TryWorldBounds(probe, out var pb))
            { runAxisIsX = pb.size.x >= pb.size.z; segLen = Mathf.Max(0.1f, runAxisIsX ? pb.size.x : pb.size.z); }
            Object.DestroyImmediate(probe);
        }
        // Scale UP (only) so a straight piece spans the whole cell — no gaps between cells. Clamp so a large cell
        // doesn't stretch the wall grotesquely tall.
        float scale = baseScale * Mathf.Clamp(cellSize / segLen, 1f, 2.2f);
        float yawX = runAxisIsX ? 0f : 90f;   // run along world X (east-west)
        float yawZ = runAxisIsX ? 90f : 0f;   // run along world Z (north-south)

        // Classify a wall cell by its 4-neighbour pattern: 1 = straight run along X (E+W), 2 = straight along Z (N+S),
        // 3 = tower (corner / end / junction / isolated), 0 = not a wall cell. Used both to pick the piece AND to decide
        // how far a straight must reach toward a neighbouring tower so the two actually touch (see below).
        int KindOf(int cx, int cz)
        {
            if (!cells.Contains((cx, cz))) return 0;
            bool n = cells.Contains((cx, cz - 1)), s = cells.Contains((cx, cz + 1));
            bool e = cells.Contains((cx + 1, cz)), w = cells.Contains((cx - 1, cz));
            if (e && w && !n && !s) return 1;
            if (n && s && !e && !w) return 2;
            return 3;
        }

        int placed = 0;
        foreach (var (cx, cz) in cells)
        {
            int kind = KindOf(cx, cz);
            float wx = (cx + 0.5f) * cellSize - sizeX * 0.5f;
            float wz = (cz + 0.5f) * cellSize - sizeZ * 0.5f;

            if (kind == 1 || kind == 2)
            {
                bool runX = kind == 1;
                float yaw = runX ? yawX : yawZ;

                // Road through the wall → swap the straight for a gate opening (centred, no reach-out).
                if (entrance != null && pathF[cx, cz] > 0.5f) { SeatWall(entrance, wx, wz, yaw, scale, wallRoot, mask); placed++; continue; }
                if (straight == null) { SeatWall(tower, wx, wz, 0f, scale, wallRoot, mask); placed++; continue; }

                // A one-cell straight is centred in its cell and abuts its straight neighbours perfectly. But a TOWER
                // sits small in the middle of ITS cell, so a straight that merely fills its own cell stops a half-cell
                // short of the tower's centre → the visible gap. Fix: where the run ends at a tower (the run-axis
                // neighbour isn't another colinear straight) stretch THAT end out to the tower's cell-centre, so the
                // straight slides under the tower base and the seam closes.
                int pcx = runX ? cx + 1 : cx, pcz = runX ? cz : cz + 1;   // +run neighbour (E / S)
                int ncx = runX ? cx - 1 : cx, ncz = runX ? cz : cz - 1;   // −run neighbour (W / N)
                float extPos = KindOf(pcx, pcz) != kind ? cellSize * 0.5f : 0f;
                float extNeg = KindOf(ncx, ncz) != kind ? cellSize * 0.5f : 0f;

                SeatStraight(straight, runX, runAxisIsX, extPos, extNeg, wx, wz, yaw, scale, cellSize, wallRoot, mask);
                placed++;
            }
            else
            {
                var piece = tower != null ? tower : straight;             // corner / end / junction / isolated → post
                if (piece == null) continue;
                SeatWall(piece, wx, wz, 0f, scale, wallRoot, mask);
                placed++;
            }
        }
        return placed;
    }

    // Instantiate a wall piece, scale + yaw it, seat its bounds-bottom on the ground (Y0), parent + layer it. Kept
    // SOLID (colliders intact) so the wall blocks the player and reads as an obstacle to the pathfinder.
    static void SeatWall(GameObject prefab, float x, float z, float yaw, float scale, Transform parent, int mask)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        PropPlacement.PlaceOnGround(go, new Vector3(x, 0f, z), mask);
        go.transform.SetParent(parent, true);
        WorldBuildUtil.SetLayerRecursive(go, 0);
    }

    // Seat a straight piece like SeatWall, but stretch it along its run axis by extPos/extNeg (world units) so it reaches
    // into a neighbouring tower instead of stopping a half-cell short of it. Length is added by scaling ONLY the long
    // local axis (height/thickness stay put), and the centre is shifted so the growth lands on the correct end(s).
    // Assumes a one-cell straight already spans ~cellSize at uniformScale (true for the preferred Wall_part2 kit piece).
    static void SeatStraight(GameObject prefab, bool runX, bool runAxisIsX, float extPos, float extNeg,
                             float wx, float wz, float yaw, float uniformScale, float cellSize, Transform parent, int mask)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var ls = Vector3.one * Mathf.Max(0.01f, uniformScale);
        float lenMul = (cellSize + extPos + extNeg) / cellSize;
        if (runAxisIsX) ls.x *= lenMul; else ls.z *= lenMul;       // long local axis == the run axis (after yaw)
        go.transform.localScale = ls;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        float shift = (extPos - extNeg) * 0.5f;                    // move centre toward the longer end
        float sx = wx + (runX ? shift : 0f);
        float sz = wz + (runX ? 0f : shift);
        PropPlacement.PlaceOnGround(go, new Vector3(sx, 0f, sz), mask);
        go.transform.SetParent(parent, true);
        WorldBuildUtil.SetLayerRecursive(go, 0);
    }

    static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
