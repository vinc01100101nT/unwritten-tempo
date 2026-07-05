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
/// <item>Props: <see cref="PropPlacement.PlaceOnGround"/> — bounds-driven seating (Y0, any pivot), scaled to the
/// palette's <c>defaultScale</c> and rotated to the painted yaw.</item>
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
    const string PropsDir  = "Assets/Prefabs/Props";
    const string ScenesDir = "Assets/Scenes";

    // ── painter JSON schema (matches the exporter in tools/map-painter/index.html) ───────────────────────
    [System.Serializable] class Vec2     { public float x, z; }
    [System.Serializable] class GroundCell { public string brush; public int cx, cz; }
    [System.Serializable] class ObjectCell { public string brush, prefab; public float x, z, yaw; }
    [System.Serializable] class PaintedMap
    {
        public int version;
        public string name;
        public float cellSize = 4f;
        public Vec2 size;
        public int cols, rows;
        public GroundCell[] ground;
        public ObjectCell[] objects;
    }

    string _jsonPath;
    string _mapName = "Prontera";
    float  _scale = 0.3f;
    bool   _scaleFromPalette = true;

    [MenuItem("Tools/unwritten/World/Import Painted Map…", priority = 181)]
    static void Open()
    {
        var w = GetWindow<MapPainterImporter>(true, "Import Painted Map");
        w.minSize = new Vector2(440, 230);
        w._jsonPath = DefaultJsonPath();
        w.SyncScaleFromPalette();
        w.Show();
    }

    // tools/map-painter/map.json sits beside the painter, two levels above the Unity project's Assets folder.
    static string DefaultJsonPath()
    {
        string p = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "tools", "map-painter", "map.json"));
        return File.Exists(p) ? p : "";
    }

    void SyncScaleFromPalette()
    {
        if (!_scaleFromPalette) return;
        var pal = LoadPalette();
        if (pal != null) _scale = pal.defaultScale;
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Bake a hand-painted map.json into Assets/Scenes/<name>.unity.", EditorStyles.wordWrappedLabel);
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

        _mapName = EditorGUILayout.TextField(new GUIContent("Scene name", "Saved as Assets/Scenes/<name>.unity"), _mapName);

        _scaleFromPalette = EditorGUILayout.Toggle(new GUIContent("Scale from palette",
            "Use the Brush Palette's defaultScale (the size props were measured + painted at)."), _scaleFromPalette);
        using (new EditorGUI.DisabledScope(_scaleFromPalette))
            _scale = EditorGUILayout.FloatField("Prop scale", _scale);
        if (_scaleFromPalette) SyncScaleFromPalette();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_jsonPath) || !File.Exists(_jsonPath) || string.IsNullOrWhiteSpace(_mapName)))
            if (GUILayout.Button("Import & Bake Scene", GUILayout.Height(34)))
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

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Import Painted Map", "No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers.", "OK"); return; }
        int mask = 1 << groundLayer;

        string safe = Sanitize(string.IsNullOrWhiteSpace(_mapName) ? "PaintedMap" : _mapName);
        string scenePath = $"{ScenesDir}/{safe}.unity";

        var palette = LoadPalette();
        float scale = Mathf.Max(0.01f, _scale);

        // ── new scene ────────────────────────────────────────────────────────────────────────────────
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        RigBuilder25D.BuildLight();   // authoring Sun (disabled at Play; Systems owns the runtime light)

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
                switch (GroundChannel(c.brush))
                {
                    case 1: pathF [c.cx, c.cz] = 1f; break;   // road/path → RED
                    case 3: stoneF[c.cx, c.cz] = 1f; break;   // stone     → GREEN
                    case 4: rockF [c.cx, c.cz] = 1f; break;   // rock      → BLUE
                    case 2: dirtF [c.cx, c.cz] = 1f; break;   // dirt/water → ALPHA
                    case 0: break;                            // grass → base layer
                    default: unmapped.Add(c.brush); dirtF[c.cx, c.cz] = 1f; break;
                }
            }

        void Sample(float wx, float wz, out Vector4 w)
        {
            float fx = (wx + sizeX * 0.5f) / cellSize - 0.5f;   // world → painter cell coords (cell centres)
            float fz = (wz + sizeZ * 0.5f) / cellSize - 0.5f;
            w = new Vector4(Bilinear(pathF,  fx, fz, cols, rows), Bilinear(stoneF, fx, fz, cols, rows),
                            Bilinear(rockF,  fx, fz, cols, rows), Bilinear(dirtF,  fx, fz, cols, rows));
        }

        var ground = GroundBlendBuilder.LayPlotSampled(
            scene, $"Ground_{safe}", Vector3.zero, sizeX, sizeZ, groundLayer, Sample,
            GroundTex(palette, "grass", "ground_grass"),
            GroundTex(palette, "dirt",  "ground_soil"),
            GroundTex(palette, "path",  "ground_road"),
            GroundTex(palette, "stone", "ground_stones"),
            GroundTex(palette, "rock",  "ground_rock"));
        if (ground == null)
        {
            EditorUtility.DisplayDialog("Import Painted Map",
                "Couldn't build the ground — need the Pxltgr 'ground_grass' prefab in Assets/Prefabs/Props and the "
                + "'Unwritten/GroundBlend' shader. Run Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first.", "OK");
            EditorSceneManager.CloseScene(scene, true);
            return;
        }
        Physics.SyncTransforms();   // make the floor queryable for the prop drop-rays

        // ── OBJECTS: one parent node per brush; seat each prop at its painted XZ + yaw ────────────────────
        var propsRoot = new GameObject("Props");
        SceneManager.MoveGameObjectToScene(propsRoot, scene);
        var groups = new Dictionary<string, Transform>();
        var missing = new HashSet<string>();
        // Per-brush front correction: the painter's facing arrow is "where the front should point", but a mesh whose
        // local front isn't +Z lands rotated. The palette's forwardOffset aligns the real front to the painted arrow.
        var fwdOffset = new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
        if (palette != null && palette.brushes != null)
            foreach (var br in palette.brushes)
                if (br != null && !string.IsNullOrEmpty(br.id)) fwdOffset[br.id] = br.forwardOffset;
        var wallCells = new HashSet<(int cx, int cz)>();   // every painted wall cell → autotiled below
        int placed = 0, decor = 0;

        if (map.objects != null)
            foreach (var o in map.objects)
            {
                if (o == null) continue;
                // Wall brush is AUTOTILED: collect its cells (ignore the painted prefab/yaw) and assemble the kit later.
                if (!string.IsNullOrEmpty(o.brush) && o.brush.ToLowerInvariant() == "wall")
                {
                    int wcx = Mathf.FloorToInt(o.x / cellSize), wcz = Mathf.FloorToInt(o.z / cellSize);
                    if (wcx >= 0 && wcx < cols && wcz >= 0 && wcz < rows) wallCells.Add((wcx, wcz));
                    continue;
                }
                if (string.IsNullOrEmpty(o.prefab)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{o.prefab}.prefab");
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
                go.transform.localScale = Vector3.one * scale;
                float off = (!string.IsNullOrEmpty(o.brush) && fwdOffset.TryGetValue(o.brush, out var fo)) ? fo : 0f;
                go.transform.rotation = Quaternion.Euler(0f, o.yaw + off, 0f);
                PropPlacement.PlaceOnGround(go, new Vector3(o.x - sizeX * 0.5f, 0f, o.z - sizeZ * 0.5f), mask);
                go.transform.SetParent(parent, true);
                WorldBuildUtil.SetLayerRecursive(go, 0);                 // Default — never the Ground layer
                if (PropPlacement.IsWalkThroughDecor(o.prefab)) { PropPlacement.StripColliders(go); decor++; }
                placed++;
            }

        // ── WALLS: autotile the painted wall cells into the modular kit (straights + corner/end towers + gates) ─
        int walls = AutotileWalls(wallCells, pathF, propsRoot.transform, sizeX, sizeZ, cellSize, cols, rows, mask, scale);

        // ── SPAWN (boot/arrival point; warps added later via MapLinker) ──────────────────────────────────
        RegionBuilder.MakeSpawn(scene, "Start", WorldBuildUtil.SurfacePoint(Vector3.zero, mask));

        // ── SAVE + REGISTER ──────────────────────────────────────────────────────────────────────────────
        if (!Directory.Exists(ScenesDir)) AssetDatabase.CreateFolder("Assets", "Scenes");
        var open = SceneManager.GetSceneByPath(scenePath);
        if (open.IsValid() && open.isLoaded && open != scene) EditorSceneManager.CloseScene(open, true);
        EditorSceneManager.SaveScene(scene, scenePath);
        WorldBuildUtil.RegisterInBuildSettings(scenePath);
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        var msg = $"Imported '{safe}':\n• {placed} props ({decor} walk-through)\n• {walls} autotiled wall piece(s)\n• ground {cols}×{rows} cells ({sizeX}×{sizeZ} u)\n→ {scenePath}";
        if (missing.Count  > 0) msg += "\n\n⚠ Missing prefabs (skipped): " + string.Join(", ", missing);
        if (unmapped.Count > 0) msg += "\n\n⚠ Ground brushes with no shader channel (drawn as dirt): " + string.Join(", ", unmapped);
        Debug.Log("[unwritten] " + msg.Replace("\n", " "));
        EditorUtility.DisplayDialog("Import Painted Map", msg, "OK");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────
    // Which GroundBlend layer a painted ground brush drives:
    //   0 = grass base · 1 = road (.r) · 2 = dirt (.a) · 3 = stone (.g) · 4 = rock (.b)
    static int GroundChannel(string brush)
    {
        if (string.IsNullOrEmpty(brush)) return 0;
        string b = brush.ToLowerInvariant();
        if (b == "grass") return 0;
        if (b.Contains("path") || b.Contains("road")) return 1;
        if (b.Contains("stone")) return 3;
        if (b.Contains("rock")) return 4;
        if (b == "dirt" || b == "soil" || b == "water") return 2;
        return -1;   // unknown → caller routes to dirt + warns
    }

    // Resolve the ground_* texture prop name for a painter brush id from the palette, else the supplied fallback.
    static string GroundTex(BrushPalette pal, string brushId, string fallback)
    {
        if (pal != null && pal.brushes != null)
            foreach (var br in pal.brushes)
                if (br != null && br.id == brushId && !string.IsNullOrEmpty(br.groundTex)) return br.groundTex;
        return fallback;
    }

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

    static BrushPalette LoadPalette()
    {
        var guids = AssetDatabase.FindAssets("t:BrushPalette");
        return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<BrushPalette>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    static GameObject LoadProp(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{name}.prefab");

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
    static int AutotileWalls(HashSet<(int cx, int cz)> cells, float[,] pathF, Transform propsRoot,
                             float sizeX, float sizeZ, float cellSize, int cols, int rows, int mask, float baseScale)
    {
        if (cells == null || cells.Count == 0) return 0;
        GameObject straight = null; foreach (var n in WallStraightNames) { straight = LoadProp(n); if (straight != null) break; }
        GameObject tower    = null; foreach (var n in WallTowerNames)    { tower    = LoadProp(n); if (tower != null) break; }
        var entrance = LoadProp(WallEntranceName);
        if (straight == null && tower == null)
        { Debug.LogWarning("[unwritten] Wall kit not found (Wall_part*/Wall_tower* in Assets/Prefabs/Props); walls skipped."); return 0; }

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

        var wallRoot = new GameObject("Wall");
        wallRoot.transform.SetParent(propsRoot, false);

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
                if (entrance != null && pathF[cx, cz] > 0.5f) { SeatWall(entrance, wx, wz, yaw, scale, wallRoot.transform, mask); placed++; continue; }
                if (straight == null) { SeatWall(tower, wx, wz, 0f, scale, wallRoot.transform, mask); placed++; continue; }

                // A one-cell straight is centred in its cell and abuts its straight neighbours perfectly. But a TOWER
                // sits small in the middle of ITS cell, so a straight that merely fills its own cell stops a half-cell
                // short of the tower's centre → the visible gap. Fix: where the run ends at a tower (the run-axis
                // neighbour isn't another colinear straight) stretch THAT end out to the tower's cell-centre, so the
                // straight slides under the tower base and the seam closes.
                int pcx = runX ? cx + 1 : cx, pcz = runX ? cz : cz + 1;   // +run neighbour (E / S)
                int ncx = runX ? cx - 1 : cx, ncz = runX ? cz : cz - 1;   // −run neighbour (W / N)
                float extPos = KindOf(pcx, pcz) != kind ? cellSize * 0.5f : 0f;
                float extNeg = KindOf(ncx, ncz) != kind ? cellSize * 0.5f : 0f;

                SeatStraight(straight, runX, runAxisIsX, extPos, extNeg, wx, wz, yaw, scale, cellSize, wallRoot.transform, mask);
                placed++;
            }
            else
            {
                var piece = tower != null ? tower : straight;             // corner / end / junction / isolated → post
                if (piece == null) continue;
                SeatWall(piece, wx, wz, 0f, scale, wallRoot.transform, mask);
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
