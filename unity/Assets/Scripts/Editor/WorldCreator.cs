using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The Unity end of the World Creator pipeline (see the "World Creator — Spec" doc). It digests a
/// <c>world.json</c> authored in the browser grid tool and, in one <b>Build World</b> action, bakes every
/// map and wires every warp — making the JSON the single source of truth for world topology.
///
/// <para><b>Two passes.</b> (1) For each map cell it clones the matching <i>template</i> <see cref="MapRecipe"/>
/// (Town/Field/Dungeon/Special), overrides name/seed/size, and bakes a scene via <see cref="RecipeGenerator"/>.
/// (2) It groups the warps by scene and places a paired <see cref="Portal3D"/> + arrival <see cref="SpawnPoint"/>
/// on both ends, each where the one door rule puts it (<see cref="WorldLayoutFile.DoorPoint"/>). A hand-made map is
/// never baked; there pass 2 only moves a door the layout has somewhere else, or adds one the scene lacks.</para>
///
/// <para><b>The layout is always <see cref="WorldLayoutFile"/></b>, read from disk when a build starts — never a copy
/// loaded earlier, and never another file.</para>
///
/// <para><b>Why not reuse <see cref="RegionBuilder.EnsurePortal"/>?</b> That dedupes by destination (one portal
/// per target per scene), but the model allows up to 3 warps between the same two maps. So each warp gets a
/// UNIQUE paired spawn id — <c>w{id}_from_{map}</c>, from the door's permanent id in the layout — and we use the
/// lower-level <c>MakePortal</c>/<c>MakeSpawn</c>.</para>
///
/// Scene name = the map <c>id</c>: a town, city or special map's name in snake_case ("Old Oak" → <c>old_oak.unity</c>),
/// a field's or dungeon floor's Ragnarok-style id (<c>lst_fild01</c>, <c>olk_dun01</c>), so the world map, the file and
/// <c>/tp</c> all use one id. A door's
/// colour comes from the destination map's <c>type</c>, never its name.
/// </summary>
public class WorldCreator : EditorWindow
{
    // ── JSON contract (mirrors the HTML tool's v2 export; parsed by JsonUtility) ──────────────────────────
    // Free-form: each map has a world-space top-left pos (x→east, y→south) + an INTEGER size [w,h]. Every
    // portal carries explicit edge anchors on both ends (no grid adjacency is assumed any more).
    [Serializable] class WorldJson  { public int version; public int revision; public MapJson[] maps; public PortalJson[] portals; }
    [Serializable] class MapJson    { public string id; public float[] pos; public int[] size; public string type; public int seed; public string pathTile; public float pathWidth; public string biome; public string cliffs; }
    // id = the door's permanent number: object names and arrival ids are built from it, never from its place in the list.
    [Serializable] class PortalJson { public int id; public string a; public string b; public AnchorJson anchorA; public AnchorJson anchorB; }
    // at / arriveAt = the door's and the arrival point's exact spot [x east, z north] in metres from the map's centre.
    // Both are optional; a missing one reads as null or empty, which WorldLayoutFile treats as "not given".
    [Serializable] class AnchorJson { public string edge; public float offset; public float[] at; public float[] arriveAt; }

    static readonly string[] Types = { "town", "field", "dungeon", "special" };

    // Each map is centred at the origin in its own scene, spanning sizeX (east/+X) × sizeZ (north/+Z).
    const string ScenesDir = "Assets/Scenes";

    /// <summary>Where the scene of map <paramref name="id"/> is saved.</summary>
    internal static string ScenePathOf(string id) => $"{ScenesDir}/{SceneName(id ?? "")}.unity";

    /// <summary>True for the whole of a build. A build places doors FROM the layout, so the scene saves it makes must
    /// never be read back into the layout as doors somebody moved (<see cref="WorldDoorSync"/>).</summary>
    internal static bool Building { get; private set; }

    static float SizeX(MapJson m) => m.size != null && m.size.Length >= 2 && m.size[0] > 0 ? m.size[0] : WorldLayoutFile.DefaultSize;
    static float SizeZ(MapJson m) => m.size != null && m.size.Length >= 2 && m.size[1] > 0 ? m.size[1] : WorldLayoutFile.DefaultSize;

    // The layout, read from disk now. Null with a reason when it cannot be used.
    static WorldJson ReadLayout(out string problem)
    {
        WorldJson w;
        try { w = JsonUtility.FromJson<WorldJson>(WorldLayoutFile.ReadText()); }
        catch (Exception e) { problem = "can't read " + WorldLayoutFile.Path + ": " + e.Message; return null; }
        if (w == null || w.maps == null) { problem = "no maps in " + WorldLayoutFile.Path; return null; }
        problem = PortalIdProblem(w);
        return problem == null ? w : null;
    }

    // Doors are named by their id, so two doors sharing one (or a file written before doors had ids, where every id
    // reads as 0) would be given the same name.
    static string PortalIdProblem(WorldJson w)
    {
        if (w.portals == null) return null;
        var seen = new HashSet<int>();
        foreach (var p in w.portals)
            if (p != null && !seen.Add(p.id))
                return $"two doors share the id {p.id} ('{p.a}' ↔ '{p.b}' is the second). Every door needs its own \"id\" — " +
                       "save the layout again from the World Creator tool.";
        return null;
    }

    static string OtherLayoutRefusal(string jsonPath) => WorldLayoutFile.IsThisFile(jsonPath) ? null
        : $"ERROR: the world is built from one file only, {WorldLayoutFile.Path}. '{jsonPath}' is a different file, so nothing was done.";

    // ── window state ──────────────────────────────────────────────────────────────────────────────────────
    WorldJson world;
    string sourcePath;
    Vector2 scroll;
    readonly List<string> errors = new List<string>();
    readonly List<string> warnings = new List<string>();
    string summary = "";

    // type → template recipe (persisted across sessions by GUID in EditorPrefs)
    readonly Dictionary<string, MapRecipe> templates = new Dictionary<string, MapRecipe>();
    // The painted world-map picture the layout was drawn on (persisted by GUID). Set = the world map shows it.
    Texture2D picture;
    const string PictureKey = "unwritten.worldcreator.picture";

    [MenuItem("Tools/unwritten/World/World Creator…", priority = 188)]
    static void Open()
    {
        var w = GetWindow<WorldCreator>(false, "World Creator");
        w.minSize = new Vector2(40f, 460f);
        w.LoadTemplatePrefs();
        w.Show();
    }

    void OnEnable() => LoadTemplatePrefs();

    // ── template persistence ──────────────────────────────────────────────────────────────────────────────
    static string PrefKey(string type) => "unwritten.worldcreator.template." + type;

    void LoadTemplatePrefs()
    {
        templates.Clear();
        foreach (var t in Types)
        {
            string guid = EditorPrefs.GetString(PrefKey(t), "");
            if (string.IsNullOrEmpty(guid)) { templates[t] = null; continue; }
            string p = AssetDatabase.GUIDToAssetPath(guid);
            templates[t] = string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<MapRecipe>(p);
        }
        string pg = EditorPrefs.GetString(PictureKey, "");
        string pp = string.IsNullOrEmpty(pg) ? "" : AssetDatabase.GUIDToAssetPath(pg);
        picture = string.IsNullOrEmpty(pp) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(pp);
    }

    void SetTemplate(string type, MapRecipe recipe)
    {
        templates[type] = recipe;
        string guid = recipe != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe)) : "";
        EditorPrefs.SetString(PrefKey(type), guid);
    }

    // ── GUI ───────────────────────────────────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("World Creator", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Digests a world.json from the browser tool → bakes maps + wires warps.", EditorStyles.miniLabel);
        EditorGUILayout.Space();

        // 1) Load
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(world == null ? "Load world.json…" : "Reload world.json…", GUILayout.Height(26)))
                LoadJson();
            if (world != null && GUILayout.Button("Re-validate", GUILayout.Width(100), GUILayout.Height(26)))
                Validate();
        }
        if (!string.IsNullOrEmpty(sourcePath))
            EditorGUILayout.LabelField(sourcePath, EditorStyles.miniLabel);

        EditorGUILayout.Space();

        // 2) Templates
        EditorGUILayout.LabelField("Type → template recipe", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Each map clones the matching template, then overrides name/seed/size.", EditorStyles.miniLabel);
        foreach (var t in Types)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var picked = (MapRecipe)EditorGUILayout.ObjectField(Cap(t), templates.TryGetValue(t, out var cur) ? cur : null,
                    typeof(MapRecipe), false);
                if (EditorGUI.EndChangeCheck()) SetTemplate(t, picked);
            }
        }

        EditorGUI.BeginChangeCheck();
        picture = (Texture2D)EditorGUILayout.ObjectField("World map picture", picture, typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetString(PictureKey, picture != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picture)) : "");
        EditorGUILayout.LabelField("Set = the in-game world map shows this picture as it is; empty = snapshots.", EditorStyles.miniLabel);

        EditorGUILayout.Space();

        // 3) Summary + validation
        if (world != null)
        {
            if (!string.IsNullOrEmpty(summary))
                EditorGUILayout.HelpBox(summary, MessageType.Info);
            foreach (var w in warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
            foreach (var e in errors)   EditorGUILayout.HelpBox(e, MessageType.Error);
        }
        else
        {
            EditorGUILayout.HelpBox("Load a world.json exported from the browser World Creator to begin.", MessageType.Info);
        }

        EditorGUILayout.Space();

        // 4) Build
        bool canBuild = world != null && errors.Count == 0;
        using (new EditorGUI.DisabledScope(!canBuild))
        {
            if (GUILayout.Button("⚒ Build World", GUILayout.Height(34)))
                BuildWorld();
            if (GUILayout.Button(picture != null ? "🗺 Bake World Map only (the picture + every map)" : "🗺 Bake World Map only (re-snapshot existing scenes)"))
                BakeWorldMap(CollectEntries(), picture, true);
        }
        if (world != null && errors.Count > 0)
            EditorGUILayout.LabelField("Fix the errors above to enable Build World.", EditorStyles.miniLabel);

        EditorGUILayout.EndScrollView();
    }

    // ── load + validate ───────────────────────────────────────────────────────────────────────────────────
    void LoadJson()
    {
        string start = string.IsNullOrEmpty(sourcePath) ? "" : Path.GetDirectoryName(sourcePath);
        string p = EditorUtility.OpenFilePanel("Select world.json", start, "json");
        if (string.IsNullOrEmpty(p)) return;
        try
        {
            world = JsonUtility.FromJson<WorldJson>(File.ReadAllText(p));
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("World Creator", "Couldn't parse that file as world.json:\n\n" + e.Message, "OK");
            world = null; return;
        }
        if (world == null || world.maps == null)
        {
            EditorUtility.DisplayDialog("World Creator", "That file has no 'maps' — not a world.json.", "OK");
            world = null; return;
        }
        sourcePath = p;
        Validate();
    }

    static bool HasAnchors(PortalJson p) => p.anchorA != null && !string.IsNullOrEmpty(p.anchorA.edge)
                                         && p.anchorB != null && !string.IsNullOrEmpty(p.anchorB.edge);

    // One end of a door: where it stands in map m, and where somebody coming through it arrives. The one door rule.
    static void DoorSpots(MapJson m, AnchorJson an, out Vector2 door, out Vector2 arrive)
    {
        WorldLayoutFile.DoorPoint(an.edge, an.offset, SizeX(m), SizeZ(m), an.at, out float dx, out float dz);
        WorldLayoutFile.ArrivalPoint(an.edge, an.offset, SizeX(m), SizeZ(m), an.at, an.arriveAt, out float ax, out float az);
        door = new Vector2(dx, dz); arrive = new Vector2(ax, az);
    }

    // Each map's door spots (world-XZ), fed to RecipeGenerator so roads reach them.
    static Dictionary<string, List<Vector2>> ComputeExits(WorldJson world, Dictionary<string, MapJson> byId)
    {
        var exits = new Dictionary<string, List<Vector2>>();
        if (world.portals == null) return exits;
        void Add(string id, AnchorJson an)
        {
            DoorSpots(byId[id], an, out var door, out _);
            if (!exits.TryGetValue(id, out var l)) exits[id] = l = new List<Vector2>();
            l.Add(door);
        }
        foreach (var p in world.portals)
        {
            if (!byId.ContainsKey(p.a) || !byId.ContainsKey(p.b) || !HasAnchors(p)) continue;
            Add(p.a, p.anchorA);
            Add(p.b, p.anchorB);
        }
        return exits;
    }

    void Validate()
    {
        errors.Clear(); warnings.Clear(); summary = "";
        if (world == null) return;

        var byId = new Dictionary<string, MapJson>();
        var typeCount = new Dictionary<string, int>();
        foreach (var t in Types) typeCount[t] = 0;

        foreach (var m in world.maps)
        {
            if (string.IsNullOrEmpty(m.id)) { errors.Add("A map has no id."); continue; }
            if (byId.ContainsKey(m.id)) { errors.Add($"Duplicate map id '{m.id}'."); continue; }
            byId[m.id] = m;
            if (m.pos == null || m.pos.Length < 2) errors.Add($"Map '{m.id}' has no valid pos [x,y].");
            if (m.size == null || m.size.Length < 2 || m.size[0] <= 0 || m.size[1] <= 0)
                errors.Add($"Map '{m.id}' has no valid integer size [w,h].");
            if (Array.IndexOf(Types, m.type) < 0) errors.Add($"Map '{m.id}' has unknown type '{m.type}'.");
            else typeCount[m.type]++;
        }

        // templates assigned for every type actually used
        foreach (var t in Types)
            if (typeCount[t] > 0 && (!templates.TryGetValue(t, out var tpl) || tpl == null))
                errors.Add($"No template recipe assigned for type '{t}' ({typeCount[t]} map(s) use it). Drag one into the slot above.");

        string idProblem = PortalIdProblem(world);
        if (idProblem != null) errors.Add(char.ToUpper(idProblem[0]) + idProblem.Substring(1));

        // portals (every warp is explicitly anchored on both ends)
        int warps = 0;
        var pairCount = new Dictionary<string, int>();   // unordered pair key → warp count
        var hasWarp = new HashSet<string>();
        if (world.portals != null)
            foreach (var pt in world.portals)
            {
                if (!byId.ContainsKey(pt.a) || !byId.ContainsKey(pt.b))
                { errors.Add($"Portal references missing map: '{pt.a}' ↔ '{pt.b}'."); continue; }
                if (!HasAnchors(pt))
                { errors.Add($"Portal '{pt.a}'↔'{pt.b}' is missing edge anchors. Re-export from the World Creator tool."); continue; }
                warps++;
                hasWarp.Add(pt.a); hasWarp.Add(pt.b);
                string pairKey = string.CompareOrdinal(pt.a, pt.b) < 0 ? pt.a + "|" + pt.b : pt.b + "|" + pt.a;
                pairCount.TryGetValue(pairKey, out int c); pairCount[pairKey] = c + 1;
            }
        foreach (var kv in pairCount)
            if (kv.Value > 3) warnings.Add($"{kv.Value} warps between {kv.Key.Replace("|", " ↔ ")} (3 is a sensible max).");

        foreach (var m in world.maps)
            if (!string.IsNullOrEmpty(m.id) && byId.Count > 1 && !hasWarp.Contains(m.id))
                warnings.Add($"Map '{m.id}' has no warps — it'll be unreachable (stranded).");

        summary = $"{world.maps.Length} maps  ·  {warps} warps\n"
                + $"town {typeCount["town"]} · field {typeCount["field"]} · dungeon {typeCount["dungeon"]} · special {typeCount["special"]}";
    }

    // The free-form world rects (scene name + world-space x/y/w/h + type) the world-atlas baker composites.
    List<WorldAtlasBaker.Entry> CollectEntries() => CollectEntries(world);

    static List<WorldAtlasBaker.Entry> CollectEntries(WorldJson world)
    {
        var entries = new List<WorldAtlasBaker.Entry>();
        if (world == null) return entries;
        foreach (var m in world.maps)
        {
            if (m.pos == null || m.pos.Length < 2) continue;
            entries.Add(new WorldAtlasBaker.Entry {
                scene = SceneName(m.id), type = m.type,
                x = m.pos[0], y = m.pos[1], w = SizeX(m), h = SizeZ(m),
            });
        }
        return entries;
    }

    // ── build ─────────────────────────────────────────────────────────────────────────────────────────────
    void BuildWorld()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Build World", "Exit Play mode first, then re-run.", "OK"); return; }
        if (!WorldLayoutFile.IsThisFile(sourcePath))
        {
            EditorUtility.DisplayDialog("Build World", "The world is built from one file only:\n\n" + WorldLayoutFile.Path
                + "\n\nThe file loaded in this window is a different one:\n\n" + sourcePath
                + "\n\nLoad the file above, then build.", "OK");
            return;
        }
        // The file as it is on disk now, not the copy this window read when it was loaded.
        var fresh = ReadLayout(out string problem);
        if (fresh == null) { EditorUtility.DisplayDialog("Build World", "The world layout can't be used: " + problem, "OK"); return; }
        world = fresh;
        Validate();
        if (errors.Count > 0) { EditorUtility.DisplayDialog("Build World", "Fix the validation errors first.", "OK"); return; }

        if (!EditorUtility.DisplayDialog("Build World",
            $"This will (re)generate {world.maps.Length} scene(s) in {ScenesDir}/ and wire all warps.\n\n"
            + "⚠ It OVERWRITES those scene files — any hand-edits in them are lost. Continue?", "Build", "Cancel"))
            return;

        string result = Build(templates, null, true, true, picture);
        if (result == null) return;
        if (result.StartsWith("ERROR")) { EditorUtility.DisplayDialog("Build World", result, "OK"); return; }
        EditorUtility.DisplayDialog("World built", result
            + "\n\nEach warp is a paired Portal3D + arrival SpawnPoint; portal colour follows the destination "
            + "(town=yellow, field=green …).\n\n"
            + "Make sure Systems is set up (Setup Systems Scene), then PLAY from any map — walk into a pad to "
            + "travel, press N for the World Map.", "OK");
    }

    /// <summary>Dialog-free build for runs without the user at the controls (called through the live editor).
    /// <paramref name="jsonPath"/> must be the world layout file (<see cref="WorldLayoutFile.Path"/>); any other file is
    /// refused. Maps whose id is in <paramref name="keepAsIs"/> are hand-made: they are never regenerated, and the only
    /// thing done in one is to move a door the layout has somewhere else or add a door the scene lacks. The scenes the
    /// user has open, and the active one, are left as they were. Returns a summary, or an error line.</summary>
    public static string BuildCore(string jsonPath, string townRecipe, string fieldRecipe, string dungeonRecipe,
                                   string specialRecipe, string[] keepAsIs)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "ERROR: exit Play mode first.";
        string refusal = OtherLayoutRefusal(jsonPath);
        if (refusal != null) return refusal;
        var tpl = new Dictionary<string, MapRecipe>
        {
            ["town"] = AssetDatabase.LoadAssetAtPath<MapRecipe>(townRecipe),
            ["field"] = AssetDatabase.LoadAssetAtPath<MapRecipe>(fieldRecipe),
            ["dungeon"] = AssetDatabase.LoadAssetAtPath<MapRecipe>(dungeonRecipe),
            ["special"] = string.IsNullOrEmpty(specialRecipe) ? null : AssetDatabase.LoadAssetAtPath<MapRecipe>(specialRecipe),
        };
        return Build(tpl, new HashSet<string>(keepAsIs ?? new string[0]), false, false)
               ?? "ERROR: a map failed to bake — see the Console.";
    }

    // One map's recipe: a clone of its type's template with the layout's own settings on it — never the template
    // asset itself. The caller destroys the clone.
    static MapRecipe CloneFor(MapJson m, Dictionary<string, MapRecipe> templates, out bool mapPicksRoad)
    {
        var clone = UnityEngine.Object.Instantiate(templates[m.type]);
        clone.mapName = SceneName(m.id);
        clone.sizeX = SizeX(m); clone.sizeZ = SizeZ(m);
        clone.randomizeSeed = false;
        clone.seed = m.seed != 0 ? m.seed : StableSeed(m.id);
        mapPicksRoad = TryParsePathTile(m.pathTile, out var pt);
        if (mapPicksRoad) clone.pathTile = pt;
        if (m.pathWidth > 0f) clone.pathWidth = m.pathWidth;
        if (TryParseBiome(m.biome, out var bm)) clone.biome = bm;   // per-cell theme override (else the template's biome)
        if (m.cliffs == "on") clone.cliffs = CliffChoice.On;         // per-map cliffs (else as the template says)
        else if (m.cliffs == "off") clone.cliffs = CliffChoice.Off;
        return clone;
    }

    /// <summary>Dialog-free: lay the ground of the named maps again from the layout, and nothing else — props, doors
    /// and arrival points stay as they are (<see cref="RecipeGenerator.RebuildGround"/>). For a change to how ground is
    /// shaped. <paramref name="jsonPath"/> must be the world layout file; any other is refused. The scenes the user has
    /// open, and the active one, are left as they were. Returns a summary, or an error line.</summary>
    public static string RebuildGroundCore(string jsonPath, string townRecipe, string fieldRecipe, string dungeonRecipe,
                                           string specialRecipe, string[] mapIds)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "ERROR: exit Play mode first.";
        string refusal = OtherLayoutRefusal(jsonPath);
        if (refusal != null) return refusal;
        var w = ReadLayout(out string layoutProblem);
        if (w == null) return "ERROR: " + layoutProblem;
        var byId = new Dictionary<string, MapJson>();
        foreach (var m in w.maps) byId[m.id] = m;
        var exitsByMap = ComputeExits(w, byId);
        var done = new List<string>();
        foreach (var id in mapIds)
        {
            // Loaded afresh for every map: closing the previous map's scene lets the editor unload them.
            var tpl = new Dictionary<string, MapRecipe>
            {
                ["town"] = AssetDatabase.LoadAssetAtPath<MapRecipe>(townRecipe),
                ["field"] = AssetDatabase.LoadAssetAtPath<MapRecipe>(fieldRecipe),
                ["dungeon"] = AssetDatabase.LoadAssetAtPath<MapRecipe>(dungeonRecipe),
                ["special"] = string.IsNullOrEmpty(specialRecipe) ? null : AssetDatabase.LoadAssetAtPath<MapRecipe>(specialRecipe),
            };
            if (!byId.TryGetValue(id, out var m)) return $"ERROR: no map '{id}' in {jsonPath} (rebuilt so far: {string.Join(", ", done)})";
            if (!tpl.TryGetValue(m.type ?? "", out var t) || t == null) return $"ERROR: no template recipe for type '{m.type}' (map '{id}').";
            var clone = CloneFor(m, tpl, out bool mapPicksRoad);
            try
            {
                var exits = exitsByMap.TryGetValue(id, out var ex) ? ex : null;
                string problem = RecipeGenerator.RebuildGround(clone, exits, mapPicksRoad);
                if (problem != null) return $"ERROR: {problem} (rebuilt so far: {string.Join(", ", done)})";
                done.Add(id);
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }
        return $"Rebuilt the ground of {done.Count} map(s): {string.Join(", ", done)}.";
    }

    // One end of a door, as pass 2 places it in its map.
    struct DoorEnd
    {
        public string name;                                  // the door object: Portal_w{id}_to_{the other map}
        public string arrivalId;                             // this map's arrival point for it: w{id}_from_{the other map}
        public string target, targetType, targetArrivalId;   // where walking in leads
        public Vector2 door, arrive;                         // x east, z north, metres from the map's centre
    }

    static DoorEnd EndOf(int id, MapJson here, AnchorJson anchor, MapJson there)
    {
        DoorSpots(here, anchor, out var door, out var arrive);
        return new DoorEnd
        {
            name = WorldDoorSync.DoorName(id, there.id), arrivalId = WorldDoorSync.ArrivalId(id, there.id),
            target = SceneName(there.id), targetType = there.type, targetArrivalId = WorldDoorSync.ArrivalId(id, here.id),
            door = door, arrive = arrive,
        };
    }

    // The one build, always from the layout file as it is on disk when the build starts. PASS 1 bakes every map from its
    // template clone, PASS 2 places the paired portals and arrival points. leaveScenesOpen = the window's behaviour (each
    // scene opened as the only scene); false = work additively and close each scene again, so whatever the user had
    // open stays open and unchanged. Returns a summary, an "ERROR: …" line, or null when a map failed to bake.
    static string Build(Dictionary<string, MapRecipe> templates, HashSet<string> keep, bool leaveScenesOpen, bool interactive,
                        Texture2D picture = null)
    {
        var world = ReadLayout(out string problem);
        if (world == null) return "ERROR: " + problem;
        keep = keep ?? new HashSet<string>();
        foreach (var m in world.maps)
            if (!keep.Contains(m.id) && (!templates.TryGetValue(m.type ?? "", out var t) || t == null))
                return $"ERROR: no template recipe for type '{m.type}' (map '{m.id}').";

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        int mask = groundLayer >= 0 ? 1 << groundLayer : ~0;

        var byId = new Dictionary<string, MapJson>();
        foreach (var m in world.maps) byId[m.id] = m;
        var exitsByMap = ComputeExits(world, byId);
        var wasActive = SceneManager.GetActiveScene();

        var scenePathById = new Dictionary<string, string>();
        int built = 0;
        Building = true;
        try
        {
            for (int i = 0; i < world.maps.Length; i++)
            {
                var m = world.maps[i];
                if (keep.Contains(m.id)) continue;
                EditorUtility.DisplayProgressBar("Build World", $"Baking '{m.id}' ({i + 1}/{world.maps.Length})", (float)i / world.maps.Length);
                var clone = CloneFor(m, templates, out bool mapPicksRoad);
                try
                {
                    var exits = exitsByMap.TryGetValue(m.id, out var ex) ? ex : null;
                    var res = RecipeGenerator.Generate(clone, exits, mapPicksRoad, leaveScenesOpen);
                    if (!res.ok)
                    {
                        EditorUtility.ClearProgressBar();
                        if (interactive) EditorUtility.DisplayDialog("Build World", $"Failed to bake '{m.id}'. Check the Console.", "OK");
                        return null;
                    }
                    scenePathById[m.id] = res.path;
                    built++;
                }
                finally { UnityEngine.Object.DestroyImmediate(clone); }
            }

            var endsByMap = new Dictionary<string, List<DoorEnd>>();
            void AddEnd(string id, DoorEnd e) { if (!endsByMap.TryGetValue(id, out var l)) endsByMap[id] = l = new List<DoorEnd>(); l.Add(e); }

            // A door is placed only when BOTH of its maps have a scene: baked in this build, or already on disk. A door
            // into a map with no scene (a shadow place nobody has built) would send every player who walks in nowhere.
            bool HasScene(string id) => scenePathById.ContainsKey(id) || File.Exists(ScenePathOf(id));
            var skipped = new List<string>();

            if (world.portals != null)
                foreach (var p in world.portals)
                {
                    if (p == null || !byId.ContainsKey(p.a) || !byId.ContainsKey(p.b) || !HasAnchors(p)) continue;
                    if (!HasScene(p.a) || !HasScene(p.b)) { skipped.Add($"{p.a}↔{p.b}"); continue; }
                    AddEnd(p.a, EndOf(p.id, byId[p.a], p.anchorA, byId[p.b]));
                    AddEnd(p.b, EndOf(p.id, byId[p.b], p.anchorB, byId[p.a]));
                }

            int portalCount = 0, handMadeDoors = 0;
            var notes = new List<string>();
            foreach (var m in world.maps)
            {
                if (!endsByMap.TryGetValue(m.id, out var ends) || ends.Count == 0) continue;
                if (keep.Contains(m.id)) { handMadeDoors += SyncHandMadeMap(m, ends, groundLayer, wasActive, notes); continue; }

                EditorUtility.DisplayProgressBar("Build World", $"Wiring warps in '{m.id}'", 0.6f);
                var scene = EditorSceneManager.OpenScene(scenePathById[m.id], leaveScenesOpen ? OpenSceneMode.Single : OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                Physics.SyncTransforms();

                foreach (var e in ends)
                    RegionBuilder.MakeSpawn(scene, e.arrivalId, WorldBuildUtil.SurfacePoint(new Vector3(e.arrive.x, 0f, e.arrive.y), mask));
                foreach (var e in ends)
                {
                    RegionBuilder.MakePortal(scene, e.name, WorldBuildUtil.SurfacePoint(new Vector3(e.door.x, 0f, e.door.y), mask),
                                             e.target, e.targetArrivalId, e.targetType);
                    portalCount++;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                if (!leaveScenesOpen)
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if (wasActive.IsValid() && wasActive.isLoaded) SceneManager.SetActiveScene(wasActive);
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();

            // Also bake the world-map atlas so the World Map HUD (N) is ready straight away. The baker opens every map
            // as the only open scene, so it runs only from the window, never behind the user's back.
            // A painted picture needs no scene opened, so it is baked either way.
            bool baked = picture != null || leaveScenesOpen;
            if (baked) BakeWorldMap(CollectEntries(world), picture, false);
            // THE DOORS BETWEEN MAPS go into the atlas every time, from this same layout — the game finds its way
            // between maps by them (quest arrows) and draws them, so they must never be a bake behind the maps.
            WorldAtlasBaker.WriteLinks(LinksOf(world));

            string summary = $"Generated {built} maps and {portalCount} portals from {Path.GetFileName(WorldLayoutFile.Path)} (revision {world.revision})"
                           + (baked ? ", and baked the world map" : " (the world map is NOT re-baked)")
                           + (skipped.Count > 0 ? $"; {skipped.Count} door(s) not placed because a map they join has no scene: {string.Join(", ", skipped)}" : "")
                           + (handMadeDoors > 0 ? $"; put {handMadeDoors} door(s) of hand-made maps where the layout has them" : "")
                           + (notes.Count > 0 ? "; " + string.Join("; ", notes) : "")
                           + (keep.Count > 0 ? $"; hand-made, never regenerated: {string.Join(", ", keep)}." : ".");
            Debug.Log("[unwritten] World Creator: " + summary);
            return summary;
        }
        finally { Building = false; EditorUtility.ClearProgressBar(); }
    }

    // PASS 2 in a hand-made map. Its scene is the user's work, so exactly two things may happen in it: a door (or its
    // arrival point) that stands somewhere other than where the layout has it is moved there, and a door the scene
    // lacks is added. Either needs ground under BOTH the door's spot and its arrival spot; without it the door is left
    // alone and named in <paramref name="notes"/>. What needs doing is read from the scene FILE first, so a map with
    // nothing to change is never opened. Returns how many doors were moved or added.
    static int SyncHandMadeMap(MapJson m, List<DoorEnd> ends, int groundLayer, Scene wasActive, List<string> notes)
    {
        string path = ScenePathOf(m.id);
        if (!File.Exists(path)) return 0;
        var saved = new WorldDoorSync.SavedScene(File.ReadAllText(path));
        var work = new List<(DoorEnd end, bool door, bool doorSaved, bool arrival, bool arrivalSaved)>();
        foreach (var e in ends)
        {
            var d = saved.RootXZ(e.name, out float dx, out float dz);
            var a = saved.RootXZ("SpawnPoint_" + e.arrivalId, out float ax, out float az);
            if (d == WorldDoorSync.SavedScene.Result.Unreadable || a == WorldDoorSync.SavedScene.Result.Unreadable)
            {
                notes.Add($"{e.name} in the hand-made map {m.id} was left alone: where it or its arrival point stands cannot be " +
                          "read from the scene file (it sits under another object, is a prefab instance, or shares its name)");
                continue;
            }
            bool doorSaved = d == WorldDoorSync.SavedScene.Result.Found, arrivalSaved = a == WorldDoorSync.SavedScene.Result.Found;
            bool door = !doorSaved || WorldDoorSync.Far(dx, dz, e.door.x, e.door.y);
            bool arrival = !arrivalSaved || WorldDoorSync.Far(ax, az, e.arrive.x, e.arrive.y);
            if (door || arrival) work.Add((e, door, doorSaved, arrival, arrivalSaved));
        }
        if (work.Count == 0) return 0;

        if (groundLayer < 0)
        {
            notes.Add($"{work.Count} door(s) of the hand-made map {m.id} were left alone: the project has no Ground layer to find the ground with");
            return 0;
        }
        var scene = SceneManager.GetSceneByPath(path);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            notes.Add($"the hand-made map {m.id} is open with unsaved changes, so {work.Count} door(s) the layout has somewhere " +
                      "else were not moved — save it and build again");
            return 0;
        }

        EditorUtility.DisplayProgressBar("Build World", $"Setting the doors of '{m.id}'", 0.6f);
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        int changed = 0;
        try
        {
            SceneManager.SetActiveScene(scene);
            Physics.SyncTransforms();
            foreach (var w in work)
            {
                var e = w.end;
                string arrivalName = "SpawnPoint_" + e.arrivalId;
                var doorGo = RootNamed(scene, e.name);
                var arrivalGo = RootNamed(scene, arrivalName);
                if ((doorGo != null) != w.doorSaved || (arrivalGo != null) != w.arrivalSaved
                    || (doorGo == null && NamedAnywhere(scene, e.name)) || (arrivalGo == null && NamedAnywhere(scene, arrivalName)))
                {
                    notes.Add($"{e.name} in the hand-made map {m.id} was left alone: the open scene and its file disagree about it");
                    continue;
                }
                if (!GroundUnder(scene, e.door, 1 << groundLayer, out var doorPos) || !GroundUnder(scene, e.arrive, 1 << groundLayer, out var arrivePos))
                {
                    notes.Add($"{e.name} in the hand-made map {m.id} was left alone: there is no ground under the door spot " +
                              $"({e.door.x:0.##}, {e.door.y:0.##}) or the arrival spot ({e.arrive.x:0.##}, {e.arrive.y:0.##}) the layout gives");
                    continue;
                }
                if (w.door)
                {
                    if (doorGo != null) doorGo.transform.position = doorPos;
                    else RegionBuilder.MakePortal(scene, e.name, doorPos, e.target, e.targetArrivalId, e.targetType);
                }
                if (w.arrival)
                {
                    if (arrivalGo != null) arrivalGo.transform.position = arrivePos;
                    else RegionBuilder.MakeSpawn(scene, e.arrivalId, arrivePos);
                }
                changed++;
            }
            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    notes.Add($"the hand-made map {m.id} could not be saved after its doors were set");
            }
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            if (wasActive.IsValid() && wasActive.isLoaded) SceneManager.SetActiveScene(wasActive);
        }
        return changed;
    }

    static GameObject RootNamed(Scene scene, string name)
    {
        foreach (var go in scene.GetRootGameObjects()) if (go.name == name) return go;
        return null;
    }

    static bool NamedAnywhere(Scene scene, string name)
    {
        foreach (var go in scene.GetRootGameObjects())
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return true;
        return false;
    }

    // The ground of THIS scene under a spot. Every map sits at the origin of its own scene, so with several scenes open
    // a plain ray would land on another map's ground.
    static bool GroundUnder(Scene scene, Vector2 spot, int groundMask, out Vector3 point)
    {
        point = default;
        bool any = false;
        foreach (var hit in Physics.RaycastAll(new Vector3(spot.x, 60f, spot.y), Vector3.down, 200f, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.gameObject.scene != scene) continue;
            if (!any || hit.point.y > point.y) point = hit.point;
            any = true;
        }
        return any;
    }

    static void BakeWorldMap(List<WorldAtlasBaker.Entry> entries, Texture2D picture, bool interactive)
    {
        if (picture != null) WorldAtlasBaker.BakePainted(entries, picture, interactive);
        else WorldAtlasBaker.Bake(entries, interactive);
    }

    /// <summary>Dialog-free: bake the in-game world map from the world layout and the painted picture it was drawn on.
    /// <paramref name="jsonPath"/> must be the world layout file; any other is refused. Opens no scene. Returns a
    /// summary, or an error line.</summary>
    public static string BakeWorldMapCore(string jsonPath, string picturePath)
    {
        string refusal = OtherLayoutRefusal(jsonPath);
        if (refusal != null) return refusal;
        var w = ReadLayout(out string problem);
        if (w == null) return "ERROR: " + problem;
        var pic = AssetDatabase.LoadAssetAtPath<Texture2D>(picturePath);
        if (pic == null) return "ERROR: no picture at " + picturePath;
        var entries = CollectEntries(w);
        WorldAtlasBaker.BakePainted(entries, pic, false);
        WorldAtlasBaker.WriteLinks(LinksOf(w));
        return $"Baked the world map: {pic.name} with {entries.Count} maps.";
    }

    /// <summary>Dialog-free: write the world map's copy of the doors again from the world layout, and nothing else — no
    /// scene is opened, no map is built, the picture is not touched. Returns a summary, or an error line.</summary>
    public static string BakeDoorMirror()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "ERROR: exit Play mode first.";
        var w = ReadLayout(out string problem);
        if (w == null) return "ERROR: " + problem;
        int expected = 0;
        foreach (var link in LinksOf(w)) if (!string.IsNullOrEmpty(link.a) && !string.IsNullOrEmpty(link.b)) expected++;
        int written = WorldAtlasBaker.WriteLinks(LinksOf(w));
        if (written != expected) return "ERROR: there is no world map to write the doors into — bake the world map first.";
        return $"Wrote {written} door(s) into the world map from the world layout (revision {w.revision}).";
    }

    /// <summary>The layout's doors as the atlas keeps them: the two maps, the door's id, and where it stands in each.</summary>
    static IEnumerable<WorldAtlas.Link> LinksOf(WorldJson world)
    {
        if (world?.portals == null) yield break;
        var byId = new Dictionary<string, MapJson>();
        foreach (var m in world.maps) if (m != null && !string.IsNullOrEmpty(m.id)) byId[m.id] = m;
        foreach (var p in world.portals)
        {
            if (p == null) continue;
            var link = new WorldAtlas.Link { a = p.a, b = p.b, id = p.id };
            if (HasAnchors(p) && byId.TryGetValue(p.a ?? "", out var mapA) && byId.TryGetValue(p.b ?? "", out var mapB))
            {
                DoorSpots(mapA, p.anchorA, out var doorA, out _);
                DoorSpots(mapB, p.anchorB, out var doorB, out _);
                link.ax = doorA.x; link.az = doorA.y; link.bx = doorB.x; link.bz = doorB.y;
            }
            yield return link;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────
    static string SceneName(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
        return id.Replace(' ', '_');
    }

    // Deterministic per-id seed (FNV-1a) so a map with no explicit seed still reproduces the same layout.
    static int StableSeed(string s)
    {
        unchecked { uint h = 2166136261; foreach (char c in s) { h ^= c; h *= 16777619; } return (int)(h & 0x7fffffff); }
    }

    static bool TryParsePathTile(string s, out PathTile tile)
    {
        switch ((s ?? "").Trim().ToLowerInvariant())
        {
            case "road":   tile = PathTile.Road;   return true;
            case "soil":   tile = PathTile.Soil;   return true;
            case "stones": tile = PathTile.Stones; return true;
            case "rock":   tile = PathTile.Rock;   return true;
            default:       tile = PathTile.Road;   return false;
        }
    }

    // Any biome by its own name (case-insensitive), so a biome appended to the list needs no change here.
    // None is the creature-side "any biome" value and never a map's biome.
    static bool TryParseBiome(string s, out Biome biome)
    {
        string name = (s ?? "").Trim();
        if (System.Enum.TryParse(name, true, out biome) && biome != Biome.None
            && string.Equals(biome.ToString(), name, System.StringComparison.OrdinalIgnoreCase))   // names only, not numbers
            return true;
        biome = Biome.Grassland;
        return false;
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
}
