using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Options window for the Map Painter pipeline: edits the <see cref="BrushPalette"/> (the brush→prefab/colour map),
/// <b>Auto-fills</b> it from the Props folder by naming convention (so new assets need ~zero manual work), and
/// <b>Exports</b> <c>prop-manifest.json</c> (brushes + measured prefab footprints) into <c>tools/map-painter/</c>
/// so the web painter always knows the real, current prefab sizes. This is the single maintenance touch-point.
/// </summary>
public class BrushPaletteWindow : EditorWindow
{
    const string PropsDir = "Assets/Prefabs/Props";
    const string DefaultPalettePath = "Assets/BrushPalette.asset";

    BrushPalette _palette;
    Vector2 _scroll;
    string _status = "";

    [MenuItem("Tools/unwritten/World/Brush Palette…", priority = 190)]
    static void Open() => GetWindow<BrushPaletteWindow>("Brush Palette");

    void OnEnable() { if (_palette == null) _palette = FindPalette(); }

    static BrushPalette FindPalette()
    {
        var guids = AssetDatabase.FindAssets("t:BrushPalette");
        return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<BrushPalette>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        _palette = (BrushPalette)EditorGUILayout.ObjectField("Palette", _palette, typeof(BrushPalette), false);

        if (_palette == null)
        {
            EditorGUILayout.HelpBox("No Brush Palette found. Create the default one to get started.", MessageType.Info);
            if (GUILayout.Button("Create Default Palette", GUILayout.Height(28))) CreateDefaultPalette();
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);
            return;
        }

        var so = new SerializedObject(_palette);
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("cellSize"));
        so.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Brushes", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var b in _palette.brushes)
        {
            if (b == null) continue;
            EditorGUILayout.BeginHorizontal("box");
            var swatch = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18));
            if (ColorUtility.TryParseHtmlString(b.colorHex, out var col)) EditorGUI.DrawRect(swatch, col);
            string detail = b.kind == BrushKind.Object
                ? $"{b.kind} · {b.category} · {b.prefabNames?.Count ?? 0} prefab(s)"
                : $"{b.kind} · tex '{(string.IsNullOrEmpty(b.groundTex) ? "—" : b.groundTex)}'";
            EditorGUILayout.LabelField($"{b.label}  ({b.id})", detail);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🔄 Auto-fill from Props", GUILayout.Height(30))) AutoFill();
        if (GUILayout.Button("📤 Export Manifest", GUILayout.Height(30))) ExportManifest();
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("➕ Add Missing Default Brushes", GUILayout.Height(24))) AddMissingDefaultBrushes();
        EditorGUILayout.LabelField("Tip: edit the palette asset directly for fine control (Inspector).", EditorStyles.miniLabel);

        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);
    }

    // The canonical default brush set — the single source for both "Create Default Palette" and the idempotent
    // "Add Missing Default Brushes" merge (so an existing palette gains new brushes without being recreated).
    static List<MapBrush> DefaultBrushes() => new List<MapBrush>
    {
        new MapBrush { id = "houses",   label = "Houses",   colorHex = "#CC3B3B", kind = BrushKind.Object,      category = PropCategory.Houses },
        new MapBrush { id = "trees",    label = "Trees",    colorHex = "#3FAF47", kind = BrushKind.Object,      category = PropCategory.Trees },
        new MapBrush { id = "wall",     label = "Wall",     colorHex = "#8A8A8A", kind = BrushKind.Object,      category = PropCategory.None },
        new MapBrush { id = "landmark", label = "Landmark", colorHex = "#C8922B", kind = BrushKind.Object,      category = PropCategory.Landmarks },
        new MapBrush { id = "grass",    label = "Grass",    colorHex = "#7CB342", kind = BrushKind.GroundPaint, groundTex = "ground_grass" },
        new MapBrush { id = "path",     label = "Path",     colorHex = "#E8D44D", kind = BrushKind.GroundPaint, groundTex = "ground_road" },
        new MapBrush { id = "stone",    label = "Stone",    colorHex = "#9C8F6E", kind = BrushKind.GroundPaint, groundTex = "ground_stones" },
        new MapBrush { id = "rock",     label = "Rock",     colorHex = "#6E7479", kind = BrushKind.GroundPaint, groundTex = "ground_rock" },
        new MapBrush { id = "dirt",     label = "Dirt",     colorHex = "#8B5A2B", kind = BrushKind.GroundPaint, groundTex = "ground_soil" },
        new MapBrush { id = "water",    label = "Water",    colorHex = "#3E7FD4", kind = BrushKind.GroundPaint, groundTex = "" },
    };

    // ── create the default palette ───────────────────────────────────────────────────────────────────────
    void CreateDefaultPalette()
    {
        var pal = CreateInstance<BrushPalette>();
        pal.cellSize = 4f;
        pal.brushes = DefaultBrushes();
        AssetDatabase.CreateAsset(pal, DefaultPalettePath);
        AssetDatabase.SaveAssets();
        _palette = pal;
        _status = $"Created {DefaultPalettePath}. Now click Auto-fill, then Export Manifest.";
        AutoFill();   // seed prefab names immediately
    }

    // Add any default brush whose id isn't already in the palette (preserves the order of the default set). Lets an
    // existing palette gain new brushes (e.g. stone/rock) without recreating it and losing edits/auto-fill.
    void AddMissingDefaultBrushes()
    {
        var defaults = DefaultBrushes();
        var defaultIds = new HashSet<string>();
        foreach (var d in defaults) defaultIds.Add(d.id);

        // Existing brushes keyed by id (so we preserve the user's edits + auto-filled prefab names).
        var byId = new Dictionary<string, MapBrush>();
        foreach (var b in _palette.brushes) if (b != null && !byId.ContainsKey(b.id)) byId[b.id] = b;

        int added = 0;
        var merged = new List<MapBrush>();
        foreach (var d in defaults)                       // default order first, reusing existing where present
            if (byId.TryGetValue(d.id, out var ex)) merged.Add(ex);
            else { merged.Add(d); added++; }
        foreach (var b in _palette.brushes)               // then any custom brushes the user added beyond the defaults
            if (b != null && !defaultIds.Contains(b.id)) merged.Add(b);

        _palette.brushes = merged;
        EditorUtility.SetDirty(_palette);
        AssetDatabase.SaveAssets();
        _status = added > 0
            ? $"Added {added} missing brush(es). Now click Export Manifest to update the painter."
            : "Palette already has every default brush.";
    }

    // ── auto-fill: classify Props by naming convention ───────────────────────────────────────────────────
    // Global excludes: collision hulls (UCX_) and non-LOD0 LODs. Per-brush keyword/exclude sets below.
    static readonly string[] GlobalExclude = { "ucx_", "_lod1", "_lod2", "_lod3" };

    void AutoFill()
    {
        var names = AllPropNames();
        int filled = 0;
        foreach (var b in _palette.brushes)
        {
            if (b == null || b.kind != BrushKind.Object) continue;
            var (keys, extraExclude) = KeywordsFor(b);
            if (keys.Length == 0) continue;

            var matched = new List<string>();
            foreach (var n in names)
            {
                string low = n.ToLowerInvariant();
                if (ContainsAny(low, GlobalExclude) || ContainsAny(low, extraExclude)) continue;
                if (ContainsAny(low, keys)) matched.Add(n);
            }
            matched.Sort();
            b.prefabNames = matched;
            filled += matched.Count;
        }
        EditorUtility.SetDirty(_palette);
        AssetDatabase.SaveAssets();
        _status = $"Auto-fill: classified {filled} prefab(s) across {_palette.brushes.Count} brush(es). Now Export Manifest.";
    }

    // Keyword + extra-exclude sets per brush (object brushes only). The wall kit is matched by id; the rest by
    // their PropCategory. Anything without a rule (rocks/carts/etc.) is just left uncovered — add a brush for it.
    static (string[] keys, string[] exclude) KeywordsFor(MapBrush b)
    {
        if (b.id == "wall")
            // Stone Wall_* kit only — exclude the separate WoodenWall_* palisade set (and its 001 duplicates),
            // whose names ("WoodenWall_Corner", "WoodenWall_TowerCorner") otherwise match wall_corner/wall_tower.
            return (new[] { "wall_part", "wall_tower", "wall_corner", "wall_gate", "wall_entrance" },
                    new[] { "woodenwall", "village" });
        switch (b.category)
        {
            case PropCategory.Houses:    return (new[] { "house" }, new[] { "door", "fireplace", "shutter" });
            case PropCategory.Trees:     return (new[] { "tree" },  new[] { "stump" });
            case PropCategory.Landmarks: return (new[] { "tavern", "windmill" },
                                                 new[] { "door", "interior", "blade", "curtain", "chandelier" });
            case PropCategory.Rocks:     return (new[] { "rock", "stone" }, new string[0]);
            case PropCategory.Bushes:    return (new[] { "bush" }, new string[0]);
            case PropCategory.Carts:     return (new[] { "cart" }, new string[0]);
            default:                     return (new string[0], new string[0]);
        }
    }

    // ── export the manifest the web painter reads ────────────────────────────────────────────────────────
    void ExportManifest()
    {
        var mf = new Manifest
        {
            version = 1,
            exportedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            cellSize = _palette.cellSize,
        };

        int measured = 0;
        foreach (var b in _palette.brushes)
        {
            if (b == null) continue;
            var mb = new MfBrush
            {
                id = b.id, label = b.label, color = b.colorHex,
                kind = b.kind == BrushKind.Object ? "object" : "ground",
                groundTex = b.groundTex ?? "",
            };
            if (b.kind == BrushKind.Object)
            {
                var names = (b.prefabNames != null && b.prefabNames.Count > 0)
                    ? b.prefabNames
                    : new List<string>(RecipeGenerator.CategoryNames(b.category));
                float maxX = 0f, maxZ = 0f;
                foreach (var n in names)
                {
                    if (!Footprint(n, 1f, out float fx, out float fz)) continue;   // native prefab size (no scaling)
                    mb.prefabs.Add(new MfPrefab { name = n, path = $"{PropsDir}/{n}.prefab", x = fx, z = fz });
                    maxX = Mathf.Max(maxX, fx); maxZ = Mathf.Max(maxZ, fz);
                    measured++;
                }
                mb.hasFootprint = mb.prefabs.Count > 0;
                mb.footprint = new MfFoot { x = maxX, z = maxZ };
            }
            mf.brushes.Add(mb);
        }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/map-painter"));
        Directory.CreateDirectory(dir);
        string outPath = Path.Combine(dir, "prop-manifest.json");
        File.WriteAllText(outPath, JsonUtility.ToJson(mf, true));
        AssetDatabase.Refresh();
        _status = $"Exported {mf.brushes.Count} brush(es), {measured} measured prefab(s) →\n{outPath}";
        Debug.Log($"[unwritten] Brush manifest exported: {outPath} ({mf.brushes.Count} brushes, {measured} prefabs).");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────
    static List<string> AllPropNames()
    {
        var list = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropsDir }))
            list.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
        return list;
    }

    static GameObject LoadProp(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{name}.prefab");

    static bool ContainsAny(string s, string[] tokens)
    {
        foreach (var t in tokens) if (!string.IsNullOrEmpty(t) && s.Contains(t)) return true;
        return false;
    }

    // Measure a prefab's world XZ footprint at the given scale (identity rotation), via the shared bounds helper.
    static bool Footprint(string name, float scale, out float fx, out float fz)
    {
        fx = fz = 0f;
        var pf = LoadProp(name);
        if (pf == null) return false;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        bool ok = PropPlacement.TryWorldBounds(go, out var bnds);
        if (ok) { fx = bnds.size.x; fz = bnds.size.z; }
        Object.DestroyImmediate(go);
        return ok;
    }

    // ── manifest DTOs (JsonUtility-serializable) ─────────────────────────────────────────────────────────
    [System.Serializable] class MfFoot   { public float x; public float z; }
    [System.Serializable] class MfPrefab { public string name; public string path; public float x; public float z; }
    [System.Serializable] class MfBrush
    {
        public string id, label, color, kind, groundTex;
        public bool hasFootprint;
        public MfFoot footprint = new MfFoot();
        public List<MfPrefab> prefabs = new List<MfPrefab>();
    }
    [System.Serializable] class Manifest
    {
        public int version;
        public string exportedAt;
        public float cellSize;
        public List<MfBrush> brushes = new List<MfBrush>();
    }
}
