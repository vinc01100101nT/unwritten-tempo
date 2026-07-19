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
    // Cropped wall-leftover meshes are baked here (regenerable output; NEVER the source prefab's mesh is touched).
    const string WallSliceDir = "Assets/2_Generated/Meshes/WallSlices";
    // Props are placed at their NATIVE prefab size (nothing rescales them). The modular WALL kit is the one
    // exception: it's tiled to fill grid cells, so it keeps its own structural fit-scale — unrelated to prop sizing.
    const float WallKitScale = 0.3f;

    // ── painter JSON schema (matches the exporter in tools/map-painter/index.html) ───────────────────────
    // `cut` marks a DIAGONAL half-cell (the painter's 📏 line tool): which corner's half-triangle is KEPT
    // (nw/ne/se/sw); `under` is what the cut-away half shows — its tile (ground) or its height notch (heights).
    [System.Serializable] class Vec2     { public float x, z; }
    [System.Serializable] class GroundCell { public string brush; public int cx, cz; public string cut; public string under; }
    [System.Serializable] class HeightCell { public int cx, cz, level; public string cut; public int under; }
    // Wall cells also carry the painter's per-stroke settings: `wall`/`tower` = chosen kit piece names ("" = default),
    // `thickness` = short-axis scale multiplier (1 = the piece's own thickness).
    [System.Serializable] class ObjectCell { public string brush, prefab; public float x, z, yaw; public string wall, tower; public float thickness; }
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
        w.minSize = new Vector2(40, 230);
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
        // `cut` is NOT flipped — same reasoning as yaw above. The z-reflection is an index conversion that keeps
        // north at the top of the game's north-up view, so a COMPASS-named kept corner ("nw" = north-west) is already
        // correct in world space. Flipping it n↔s sheared the band (one edge ran the wrong way — the zigzag bug).
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
        // A cell with a `cut` contributes its KEPT tile to the coarse field (so neighbours feather toward it as
        // usual); the hard triangle split itself is built by the elevated builder from the DiagonalCuts data.
        var pathF  = new float[cols, rows];   // .r road
        var stoneF = new float[cols, rows];   // .g stone
        var rockF  = new float[cols, rows];   // .b rock
        var dirtF  = new float[cols, rows];   // .a dirt
        var unmapped = new HashSet<string>();
        var cellCh = new int[cols, rows];     // resolved kept-tile channel per painted cell (-1/0 = base)
        var cuts = new GroundBlendBuilder.DiagonalCuts(cols, rows);
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
                cellCh[c.cx, c.cz] = ch;
                int d = CutIndex(c.cut);
                if (d != 0)
                {
                    cuts.dir[c.cx, c.cz]    = d;
                    cuts.keptCh[c.cx, c.cz] = ch;
                    if (!string.IsNullOrEmpty(c.under))
                    {
                        if (groundChannel.TryGetValue(c.under, out var uch)) cuts.underCh[c.cx, c.cz] = uch;
                        else unmapped.Add(c.under);
                    }
                    cuts.any = true;
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
        // A cut heights cell holds TWO notches: `level` for the kept half (→ the levels grid, which drives most
        // geometry) and `under` for the cut-away half (→ DiagonalCuts.underLevel).
        var levels = new int[cols, rows];
        var hCut = new bool[cols, rows];   // this cell's heights entry carried its own diagonal split
        bool hasElevation = false;
        if (map.heights != null)
            foreach (var h in map.heights)
            {
                if (h == null || h.cx < 0 || h.cx >= cols || h.cz < 0 || h.cz >= rows) continue;
                levels[h.cx, h.cz] = h.level;
                if (h.level != 0 || h.under != 0) hasElevation = true;
                int d = CutIndex(h.cut);
                if (d != 0)
                {
                    if (cuts.dir[h.cx, h.cz] == 0)   // height-only cut (uncut ground) → both halves show the cell's own tile
                    {
                        cuts.dir[h.cx, h.cz]    = d;
                        cuts.keptCh[h.cx, h.cz] = cellCh[h.cx, h.cz];
                        cuts.underCh[h.cx, h.cz] = cellCh[h.cx, h.cz];
                    }
                    cuts.underLevel[h.cx, h.cz] = h.under;
                    hCut[h.cx, h.cz] = true;
                    cuts.any = true;
                }
            }
        // Ground-cut cells with a FLAT (or absent) height: both halves sit at the same notch — two-tone top, no step.
        if (cuts.any)
        {
            for (int pz = 0; pz < rows; pz++)
                for (int px = 0; px < cols; px++)
                    if (cuts.dir[px, pz] != 0 && !hCut[px, pz]) cuts.underLevel[px, pz] = levels[px, pz];
            hasElevation = true;   // diagonal tops need the shaped builder even on an otherwise flat map (crisp 45° edges)
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
                    GroundTexFor(3, "ground_rock"),
                    cuts: cuts.any ? cuts : null)
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
        //    since the wall kit and its gate openings both read this set. Wall brush ignores the painted prefab/yaw, but
        //    keeps its per-cell stroke settings (chosen wall/tower piece + thickness) for AutotileWalls to honour.
        var wallCells = new Dictionary<(int cx, int cz), WallCell>();
        if (map.objects != null)
            foreach (var o in map.objects)
            {
                if (o == null || string.IsNullOrEmpty(o.brush) || o.brush.ToLowerInvariant() != "wall") continue;
                int wcx = Mathf.FloorToInt(o.x / cellSize), wcz = Mathf.FloorToInt(o.z / cellSize);
                if (wcx >= 0 && wcx < cols && wcz >= 0 && wcz < rows)
                    wallCells[(wcx, wcz)] = new WallCell { wall = o.wall, tower = o.tower, thickness = o.thickness > 0f ? o.thickness : 1f };
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
            walls = AutotileWalls(wallCells, pathF, wallsRoot.transform, sizeX, sizeZ, cellSize, cols, rows, mask, WallKitScale, safe);
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

    // Remove a prior import's category objects from the scene so the ticked ones can be rebuilt cleanly. Ground mesh =
    // "Ground_*", props group = "Props", wall kit = "Wall". The spawn is matched by its SpawnPoint COMPONENT id, NOT the
    // GameObject name — MakeSpawn names it "SpawnPoint_Start" (the name check "Start" never matched, so old Start spawns
    // piled up on every re-import). Only the "Start" arrival is wiped; portal arrival spawns (w{i}_from_*) are left.
    static void ClearImported(Scene scene, bool ground, bool props, bool walls, bool spawn)
    {
        foreach (var go in scene.GetRootGameObjects())
        {
            string n = go.name;
            if (ground && n.StartsWith("Ground_")) { Object.DestroyImmediate(go); continue; }
            if (props && n == "Props")             { Object.DestroyImmediate(go); continue; }
            if (walls && n == "Wall")              { Object.DestroyImmediate(go); continue; }
            if (spawn) { var sp = go.GetComponent<SpawnPoint>(); if (sp != null && sp.id == "Start") { Object.DestroyImmediate(go); continue; } }
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
    // The modular Pxltgr wall kit. Straights tile the runs (orthogonal AND 45° diagonal); towers cap
    // corners/ends/junctions; an entrance opens a gate. The painter stamps each cell with the author's chosen
    // wall/tower piece + a thickness multiplier; we honour those per cell, falling back to the kit defaults.
    static readonly string[] WallStraightNames = { "Wall_part2", "Wall_part1" };
    static readonly string[] WallTowerNames    = { "Wall_tower1", "Wall_tower2" };
    const string WallEntranceName = "Wall_entrance";
    // If towers face the wrong way in-scene, flip/adjust here (degrees added to the computed bisector). The bisector
    // math is verified against the spec (0°&90° corner → 45°); this is only for the piece's own "front" convention.
    const float WallTowerFacingOffset = 0f;

    struct WallCell { public string wall, tower; public float thickness; }

    // Kind of a wall cell from its neighbours (mirror of the painter's wallKindAt): "x"/"z" = orthogonal straight,
    // "ne"/"nw" = 45° diagonal run (NE–SW / NW–SE), "tower" = corner/end/junction/isolated, "" = not a wall cell.
    // Orthogonal rules IGNORE diagonal neighbours (parallel runs one cell apart mustn't demote each other); the
    // diagonal rule only fires when NO orthogonal neighbour touches, so a diagonal never fights an axis run.
    static string WallKind(Dictionary<(int, int), WallCell> cells, int cx, int cz)
    {
        if (!cells.ContainsKey((cx, cz))) return "";
        bool n = cells.ContainsKey((cx, cz - 1)), s = cells.ContainsKey((cx, cz + 1));
        bool e = cells.ContainsKey((cx + 1, cz)), w = cells.ContainsKey((cx - 1, cz));
        if (e && w && !n && !s) return "x";
        if (n && s && !e && !w) return "z";
        if (!n && !s && !e && !w)
        {
            bool ne = cells.ContainsKey((cx + 1, cz - 1)), nw = cells.ContainsKey((cx - 1, cz - 1));
            bool se = cells.ContainsKey((cx + 1, cz + 1)), sw = cells.ContainsKey((cx - 1, cz + 1));
            if (ne && sw && !nw && !se) return "ne";
            if (nw && se && !ne && !sw) return "nw";
        }
        return "tower";
    }

    // Assemble the kit over the painted wall cells. Pieces seat bottom-on-ground (Y0) and stay SOLID (block player +
    // pathfinder). Returns the piece count.
    static int AutotileWalls(Dictionary<(int cx, int cz), WallCell> cells, float[,] pathF, Transform wallRoot,
                             float sizeX, float sizeZ, float cellSize, int cols, int rows, int mask, float baseScale,
                             string mapName)
    {
        if (cells == null || cells.Count == 0) return 0;
        ClearMapSlices(mapName);   // drop THIS map's prior cropped-leftover meshes so the bake regenerates them fresh
        GameObject defStraight = null; foreach (var n in WallStraightNames) { defStraight = LoadProp(n); if (defStraight != null) break; }
        GameObject defTower    = null; foreach (var n in WallTowerNames)    { defTower    = LoadProp(n); if (defTower != null) break; }
        if (defStraight == null && defTower == null)
        { Debug.LogWarning("[unwritten] Wall kit not found (Wall_part*/Wall_tower* in Assets/1_Authored/Prefabs/Props); walls skipped."); return 0; }

        // Per-piece measurement cache: world run length + which local axis is the long (run) axis, at base scale.
        var measured = new Dictionary<GameObject, (float segLen, bool runAxisIsX)>();
        (float, bool) Measure(GameObject prefab)
        {
            if (measured.TryGetValue(prefab, out var m)) return m;
            float segLen = cellSize; bool runAxisIsX = true;
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            probe.transform.localScale = Vector3.one * Mathf.Max(0.01f, baseScale);
            probe.transform.rotation = Quaternion.identity;
            if (PropPlacement.TryWorldBounds(probe, out var pb))
            { runAxisIsX = pb.size.x >= pb.size.z; segLen = Mathf.Max(0.1f, runAxisIsX ? pb.size.x : pb.size.z); }
            Object.DestroyImmediate(probe);
            return measured[prefab] = (segLen, runAxisIsX);
        }
        // Resolve the piece the author chose for this cell, else the kit default.
        GameObject StraightPiece(WallCell wc) => (!string.IsNullOrEmpty(wc.wall) ? LoadProp(wc.wall) : null) ?? defStraight ?? defTower;
        GameObject TowerPiece   (WallCell wc) => (!string.IsNullOrEmpty(wc.tower) ? LoadProp(wc.tower) : null) ?? defTower ?? defStraight;

        // Run direction (in cell units, = world XZ since world+X=+cx, world+Z=+cz) and base run length per kind.
        (int dcx, int dcz, float baseLen) RunOf(string kind) => kind switch
        {
            "x"  => (1, 0, cellSize),
            "z"  => (0, 1, cellSize),
            "ne" => (1, -1, cellSize * 1.41421356f),   // NE–SW diagonal spans corner-to-corner (√2)
            "nw" => (1, 1, cellSize * 1.41421356f),     // NW–SE diagonal
            _    => (0, 0, 0f),
        };

        int placed = 0;

        // ── PASS 1: towers (caps at corners / ends / junctions / isolated posts) ──────────────────────────────
        foreach (var kv in cells)
        {
            var (tcx, tcz) = kv.Key;
            if (WallKind(cells, tcx, tcz) != "tower") continue;
            var piece = TowerPiece(kv.Value); if (piece == null) continue;
            float twx = (tcx + 0.5f) * cellSize - sizeX * 0.5f, twz = (tcz + 0.5f) * cellSize - sizeZ * 0.5f;
            float tthick = kv.Value.thickness > 0f ? kv.Value.thickness : 1f;
            SeatWall(piece, twx, twz, TowerYaw(cells, tcx, tcz) + WallTowerFacingOffset, baseScale, tthick, wallRoot, mask);
            placed++;
        }

        // ── PASS 2: straight runs. Collect each maximal colinear run of one kind+piece+thickness, then lay
        //    natural-size pieces end-to-end along it and CROP (mesh-slice) only the final piece where it overshoots
        //    the run's end — so bricks keep their true density (no squash) and consecutive pieces abut seam-free.
        //    A straight cell needs a same-kind neighbour on BOTH sides to classify as straight, so every run's two
        //    ends are towers; the run reaches half a cell past its end cells to tuck the seams under those towers.
        bool SameRun(int cx, int cz, string kind, GameObject piece, float thick)
        {
            if (!cells.TryGetValue((cx, cz), out var wc)) return false;
            if (WallKind(cells, cx, cz) != kind) return false;
            if (StraightPiece(wc) != piece) return false;
            return Mathf.Approximately(wc.thickness > 0f ? wc.thickness : 1f, thick);
        }
        var sliceCache = new Dictionary<string, Mesh>();
        Mesh GetSlice(GameObject piece, bool axisX, float frac)
        {
            var mf = piece.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return null;
            string ckey = piece.name + "|" + axisX + "|" + Mathf.RoundToInt(frac * 1000f);
            if (sliceCache.TryGetValue(ckey, out var have)) return have;
            var sliced = SliceMeshMinSide(mf.sharedMesh, axisX, frac);
            if (sliced != null)
            {
                EnsureFolder(WallSliceDir);
                string path = $"{WallSliceDir}/{Sanitize(mapName)}_{Sanitize(piece.name)}_{(axisX ? "x" : "z")}_{Mathf.RoundToInt(frac * 1000f)}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(sliced, path);
            }
            return sliceCache[ckey] = sliced;
        }
        // Lay natural-size pieces end-to-end from (sx,sz) along (ux,uz) for `runLen`, cropping only the final leftover.
        void LayRun(GameObject piece, float thick, float segLen, bool axisX, float yaw,
                    float sx, float sz, float ux, float uz, float runLen)
        {
            int whole = Mathf.FloorToInt(runLen / segLen + 1e-4f);                     // how many natural pieces fit
            for (int i = 0; i < whole; i++)
            {
                float d = (i + 0.5f) * segLen;
                SeatWallSegment(piece, null, axisX, yaw, sx + ux * d, sz + uz * d, baseScale, thick, wallRoot, mask);
                placed++;
            }
            float rem = runLen - whole * segLen;                                       // leftover cropped to fit the end
            if (rem > 0.02f)
            {
                var sliced = GetSlice(piece, axisX, rem / segLen);
                float d = whole * segLen + rem * 0.5f;
                SeatWallSegment(piece, sliced, axisX, yaw, sx + ux * d, sz + uz * d, baseScale, thick, wallRoot, mask);
                placed++;
            }
        }

        var runVisited = new HashSet<(int, int)>();
        foreach (var kv in cells)
        {
            var (cx0, cz0) = kv.Key;
            if (runVisited.Contains((cx0, cz0))) continue;
            string kind = WallKind(cells, cx0, cz0);
            if (kind != "x" && kind != "z" && kind != "ne" && kind != "nw") continue;

            var (dcx, dcz, step) = RunOf(kind);
            var runPiece = StraightPiece(kv.Value);
            float thick = kv.Value.thickness > 0f ? kv.Value.thickness : 1f;
            if (runPiece == null) { runVisited.Add((cx0, cz0)); continue; }

            int sx = cx0, sz = cz0;                                                    // walk back to the run's first cell
            while (SameRun(sx - dcx, sz - dcz, kind, runPiece, thick)) { sx -= dcx; sz -= dcz; }
            int n = 0, wx2 = sx, wz2 = sz;                                             // then forward, tallying its length
            while (SameRun(wx2, wz2, kind, runPiece, thick)) { runVisited.Add((wx2, wz2)); n++; wx2 += dcx; wz2 += dcz; }
            if (n == 0) continue;

            var (segLen, runAxisIsX) = Measure(runPiece);
            // Yaw that points the piece's LONG local axis along the world run direction (dcx,dcz). Unity Y-rotation
            // sends local +X → (cosθ,0,−sinθ) and local +Z → (sinθ,0,cosθ); solving each for the run vector gives:
            float yaw = runAxisIsX ? Mathf.Atan2(-dcz, dcx) * Mathf.Rad2Deg : Mathf.Atan2(dcx, dcz) * Mathf.Rad2Deg;

            float dlen = Mathf.Sqrt(dcx * dcx + dcz * dcz);
            float ux = dcx / dlen, uz = dcz / dlen;                                    // unit run direction (world XZ)
            float fx = (sx + 0.5f) * cellSize - sizeX * 0.5f;                          // first cell centre
            float fz = (sz + 0.5f) * cellSize - sizeZ * 0.5f;
            // A tower sits at its cell CENTRE and is smaller than a cell, so ending the run at the tower cell's near
            // edge (half a step out) leaves a visible gap between wall and tower. When an end abuts a tower, reach an
            // extra half-step to the tower's CENTRE so the wall overlaps it and the crop-seam hides inside the tower.
            // Open ends (no tower — a free run tip) stay at the cell edge; there's nothing to tuck into.
            float startExtra = WallKind(cells, sx - dcx, sz - dcz) == "tower" ? step * 0.5f : 0f;
            float endExtra   = WallKind(cells, wx2, wz2)           == "tower" ? step * 0.5f : 0f;
            float startX = fx - ux * (step * 0.5f + startExtra), startZ = fz - uz * (step * 0.5f + startExtra);
            float runLen = n * step + startExtra + endExtra;

            LayRun(runPiece, thick, segLen, runAxisIsX, yaw, startX, startZ, ux, uz, runLen);
        }

        // ── PASS 3: link orthogonally-ADJACENT towers with a wall. Two towers touching edge-to-edge have no straight
        //    cell between them, so no run fills the gap — a 2-cell wall, an L-corner, or a T/X junction otherwise
        //    renders as bare posts with a hole. Lay a piece spanning the two tower CENTRES (cropped to fit, both ends
        //    tucked inside a tower). Checking only +E / +S neighbours links each pair exactly once; the segment meets
        //    any run that already reached a shared tower centre, so there's nothing to overlap-cut.
        foreach (var kv in cells)
        {
            var (cx, cz) = kv.Key;
            if (WallKind(cells, cx, cz) != "tower") continue;
            foreach (var d in new[] { (dx: 1, dz: 0), (dx: 0, dz: 1) })
            {
                if (WallKind(cells, cx + d.dx, cz + d.dz) != "tower") continue;
                var piece = StraightPiece(kv.Value); if (piece == null) continue;
                float thick = kv.Value.thickness > 0f ? kv.Value.thickness : 1f;
                var (segLen, runAxisIsX) = Measure(piece);
                float ax = (cx + 0.5f) * cellSize - sizeX * 0.5f, az = (cz + 0.5f) * cellSize - sizeZ * 0.5f;
                float yaw = runAxisIsX ? Mathf.Atan2(-d.dz, d.dx) * Mathf.Rad2Deg : Mathf.Atan2(d.dx, d.dz) * Mathf.Rad2Deg;
                LayRun(piece, thick, segLen, runAxisIsX, yaw, ax, az, d.dx, d.dz, cellSize);   // centre-to-centre = one cell
            }
        }
        return placed;
    }

    // Tower facing = the outward bisector of the walls it supports. Computed by VECTOR SUM of unit directions toward
    // each wall neighbour (8-way), then facing AWAY from that sum (outward). Never (a+b)/2 on raw angles — that breaks
    // at the 0°/360° wrap; the vector sum is wrap-safe and gives a corner of a north(+Z) wall and east(+X) wall a 45°
    // (NE, outward) facing exactly as specified. If the sum cancels (a 4-way cross, or a tower straddling two colinear
    // walls) the bisector is undefined, so we face perpendicular to the first neighbour's axis — a straddling tower then
    // matches the walls' own facing (two "0°" walls → tower 0°), per spec. Yaw: local +Z is "front" (atan2(x, z)).
    static float TowerYaw(Dictionary<(int, int), WallCell> cells, int cx, int cz)
    {
        // Fixed compass scan order (N,NE,E,SE,S,SW,W,NW) so the degenerate fallback is deterministic.
        int[,] dirs = { { 0, 1 }, { 1, 1 }, { 1, 0 }, { 1, -1 }, { 0, -1 }, { -1, -1 }, { -1, 0 }, { -1, 1 } };
        float sx = 0f, sz = 0f; int fdx = 0, fdz = 0; bool haveFirst = false;
        for (int i = 0; i < 8; i++)
        {
            int dx = dirs[i, 0], dz = dirs[i, 1];
            if (!cells.ContainsKey((cx + dx, cz + dz))) continue;
            float inv = 1f / Mathf.Sqrt(dx * dx + dz * dz);
            sx += dx * inv; sz += dz * inv;
            if (!haveFirst) { fdx = dx; fdz = dz; haveFirst = true; }
        }
        if (!haveFirst) return 0f;                                            // isolated post
        if (sx * sx + sz * sz > 1e-4f) return Mathf.Atan2(-sx, -sz) * Mathf.Rad2Deg;   // face AWAY from the walls (outward)
        return Mathf.Atan2(-fdz, fdx) * Mathf.Rad2Deg;                        // cancel → perpendicular to the run axis
    }

    // Instantiate a wall piece, scale (uniform × separate thickness on the short horizontal axis) + yaw it, seat its
    // bounds-bottom on the ground (Y0), parent + layer it. Kept SOLID so it blocks the player + pathfinder. `thickness`
    // scales BOTH horizontal axes for towers (square-ish posts); straights use SeatWallSegment below.
    static void SeatWall(GameObject prefab, float x, float z, float yaw, float scale, float thickness, Transform parent, int mask)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        float s = Mathf.Max(0.01f, scale), t = Mathf.Max(0.05f, thickness);
        go.transform.localScale = new Vector3(s * t, s, s * t);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        PropPlacement.PlaceOnGround(go, new Vector3(x, 0f, z), mask);
        go.transform.SetParent(parent, true);
        WorldBuildUtil.SetLayerRecursive(go, 0);
    }

    // Seat one straight segment at its NATURAL size (no long-axis stretch): height + long axis at base scale, SHORT
    // horizontal axis scaled by `thickness`. (x,z) is the segment's world CENTRE. When `sliced` is non-null it's the
    // cropped-leftover mesh — assigned to BOTH the visual (MeshFilter) and the collider (MeshCollider) so the block
    // and the pathfinder match the trimmed brick. The source prefab's own mesh asset is never modified.
    static void SeatWallSegment(GameObject prefab, Mesh sliced, bool runAxisIsX, float yaw,
                                float x, float z, float baseScale, float thickness, Transform parent, int mask)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (sliced != null)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())   mf.sharedMesh = sliced;
            foreach (var mc in go.GetComponentsInChildren<MeshCollider>()) mc.sharedMesh = sliced;
        }
        float s = Mathf.Max(0.01f, baseScale), t = Mathf.Max(0.05f, thickness);
        go.transform.localScale = runAxisIsX ? new Vector3(s, s, s * t) : new Vector3(s * t, s, s);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        PropPlacement.PlaceOnGround(go, new Vector3(x, 0f, z), mask);
        go.transform.SetParent(parent, true);
        WorldBuildUtil.SetLayerRecursive(go, 0);
    }

    // Crop a wall mesh along its long local axis, KEEPING the min-side fraction `keepFrac` (0..1). Clips each triangle
    // against the plane coord==cut (Sutherland–Hodgman on the 3-gon → up to a quad → fan), interpolating pos/normal/
    // uv/tangent/colour on cut edges. Returns a NEW mesh (source untouched). The kept min side maps to the run-start
    // side, so the trimmed face lands at the run's end (tucked under the end tower).
    static Mesh SliceMeshMinSide(Mesh src, bool axisX, float keepFrac)
    {
        keepFrac = Mathf.Clamp01(keepFrac);
        var b = src.bounds;
        float lo = axisX ? b.min.x : b.min.z, hi = axisX ? b.max.x : b.max.z;
        float cut = lo + keepFrac * (hi - lo);

        var sv = src.vertices; var sn = src.normals; var su = src.uv; var st = src.tangents; var sc = src.colors;
        bool hasN = sn != null && sn.Length == sv.Length, hasU = su != null && su.Length == sv.Length;
        bool hasT = st != null && st.Length == sv.Length, hasC = sc != null && sc.Length == sv.Length;
        var tris = src.triangles;

        var oV = new List<Vector3>(); var oN = new List<Vector3>(); var oU = new List<Vector2>();
        var oT = new List<Vector4>(); var oC = new List<Color>();  var oTris = new List<int>();

        float Coord(Vector3 p) => axisX ? p.x : p.z;

        var poly = new List<(Vector3 p, Vector3 n, Vector2 uv, Vector4 t, Color c)>(4);
        (Vector3, Vector3, Vector2, Vector4, Color) Vtx(int i) =>
            (sv[i], hasN ? sn[i] : Vector3.up, hasU ? su[i] : Vector2.zero,
             hasT ? st[i] : new Vector4(1, 0, 0, -1), hasC ? sc[i] : Color.white);

        for (int i = 0; i < tris.Length; i += 3)
        {
            var v0 = Vtx(tris[i]); var v1 = Vtx(tris[i + 1]); var v2 = Vtx(tris[i + 2]);
            var tri = new[] { v0, v1, v2 };
            poly.Clear();
            for (int e = 0; e < 3; e++)
            {
                var cur = tri[e]; var nxt = tri[(e + 1) % 3];
                float dc = Coord(cur.Item1) - cut, dn = Coord(nxt.Item1) - cut;
                bool ci = dc <= 0f, ni = dn <= 0f;
                if (ci) poly.Add(cur);
                if (ci != ni)
                {
                    float f = dc / (dc - dn);
                    poly.Add((Vector3.Lerp(cur.Item1, nxt.Item1, f),
                              Vector3.Normalize(Vector3.Lerp(cur.Item2, nxt.Item2, f)),
                              Vector2.Lerp(cur.Item3, nxt.Item3, f),
                              Vector4.Lerp(cur.Item4, nxt.Item4, f),
                              Color.Lerp(cur.Item5, nxt.Item5, f)));
                }
            }
            if (poly.Count < 3) continue;
            int baseI = oV.Count;
            foreach (var v in poly) { oV.Add(v.p); oN.Add(v.n); oU.Add(v.uv); oT.Add(v.t); oC.Add(v.c); }
            for (int k = 1; k < poly.Count - 1; k++) { oTris.Add(baseI); oTris.Add(baseI + k); oTris.Add(baseI + k + 1); }
        }
        if (oV.Count == 0 || oTris.Count == 0) return null;

        var m = new Mesh { name = src.name + "_slice" };
        if (oV.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(oV);
        if (hasN) m.SetNormals(oN);
        if (hasU) m.SetUVs(0, oU);
        if (hasT) m.SetTangents(oT);
        if (hasC) m.SetColors(oC);
        m.SetTriangles(oTris, 0);
        m.RecalculateBounds();
        if (!hasN) m.RecalculateNormals();
        return m;
    }

    // Delete THIS map's previously baked leftover-slice meshes (namespaced by map name) so a re-bake starts clean.
    static void ClearMapSlices(string mapName)
    {
        if (!AssetDatabase.IsValidFolder(WallSliceDir)) return;
        string prefix = Sanitize(mapName) + "_";
        foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { WallSliceDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path).StartsWith(prefix)) AssetDatabase.DeleteAsset(path);
        }
    }

    // Create a folder chain under Assets/ if it doesn't exist (AssetDatabase needs each level made explicitly).
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    // Painter kept-corner name → DiagonalCuts.dir index (0 = no cut). Compass-named, so no flip needed on import
    // (the z-reflection keeps north at the top). Matches GroundBlendBuilder.EdgeIsKept.
    static int CutIndex(string cut)
    {
        switch (cut) { case "nw": return 1; case "ne": return 2; case "se": return 3; case "sw": return 4; default: return 0; }
    }

    static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
