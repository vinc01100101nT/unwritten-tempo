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
/// on both ends, derived from each portal's edge + offset on the square plot rim.</para>
///
/// <para><b>Why not reuse <see cref="RegionBuilder.EnsurePortal"/>?</b> That dedupes by destination (one portal
/// per target per scene), but the model allows up to 3 warps between the same two maps. So each warp gets a
/// UNIQUE paired spawn id — <c>w{i}_from_{map}</c> — and we use the lower-level <c>MakePortal</c>/<c>MakeSpawn</c>.
/// Because each Build re-bakes scenes from scratch, there are no stale portals to dedupe against anyway.</para>
///
/// Scene name = the sanitized map <c>id</c>, so an id like <c>town_0</c> both becomes <c>Town_0.unity</c> and
/// drives the destination-coloured portal convention (ClassifyDestination matches "town"/"dungeon"/…).
/// </summary>
public class WorldCreator : EditorWindow
{
    // ── JSON contract (mirrors the HTML tool's v2 export; parsed by JsonUtility) ──────────────────────────
    // Free-form: each map has a world-space top-left pos (x→east, y→south) + an INTEGER size [w,h]. Every
    // portal carries explicit edge anchors on both ends (no grid adjacency is assumed any more).
    [Serializable] class WorldJson  { public int version; public MapJson[] maps; public PortalJson[] portals; }
    [Serializable] class MapJson    { public string id; public float[] pos; public int[] size; public string type; public int seed; public string pathTile; public float pathWidth; public string biome; }
    [Serializable] class PortalJson { public string a; public string b; public AnchorJson anchorA; public AnchorJson anchorB; }
    [Serializable] class AnchorJson { public string edge; public float offset; }

    static readonly string[] Types = { "town", "field", "dungeon", "special" };

    // Each map is centred at the origin in its own scene, spanning sizeX (east/+X) × sizeZ (north/+Z). A portal
    // sits on the rim; its arrival spawn a little inward so you don't instantly re-trigger it.
    const float DefaultSize = 60f;     // fallback when a map omits size (mirrors the HTML tool's DEFAULT_SIZE)
    const float EdgeMargin = 4f;
    const float SpawnInset = 3f;
    const string ScenesDir = "Assets/Scenes";

    static float SizeX(MapJson m) => m.size != null && m.size.Length >= 2 && m.size[0] > 0 ? m.size[0] : DefaultSize;
    static float SizeZ(MapJson m) => m.size != null && m.size.Length >= 2 && m.size[1] > 0 ? m.size[1] : DefaultSize;

    // ── window state ──────────────────────────────────────────────────────────────────────────────────────
    WorldJson world;
    string sourcePath;
    Vector2 scroll;
    readonly List<string> errors = new List<string>();
    readonly List<string> warnings = new List<string>();
    string summary = "";

    // type → template recipe (persisted across sessions by GUID in EditorPrefs)
    readonly Dictionary<string, MapRecipe> templates = new Dictionary<string, MapRecipe>();

    [MenuItem("Tools/unwritten/World/World Creator…", priority = 188)]
    static void Open()
    {
        var w = GetWindow<WorldCreator>(false, "World Creator");
        w.minSize = new Vector2(440f, 460f);
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
            if (GUILayout.Button("🗺 Bake World Map only (re-snapshot existing scenes)"))
                WorldAtlasBaker.Bake(CollectEntries());
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

    // Resolve a portal's edge + offset on BOTH maps from its explicit anchors. Single source so Build's
    // exit-points and the placed portals agree exactly.
    static void ResolvePortalEdges(PortalJson p, out string edgeA, out float offA, out string edgeB, out float offB)
    {
        edgeA = p.anchorA.edge; offA = p.anchorA.offset;
        edgeB = p.anchorB.edge; offB = p.anchorB.offset;
    }

    // Each map's warp doorway positions (world-XZ on the plot rim), fed to RecipeGenerator so roads reach them.
    Dictionary<string, List<Vector2>> ComputeExits(Dictionary<string, MapJson> byId)
    {
        var exits = new Dictionary<string, List<Vector2>>();
        if (world.portals == null) return exits;
        void Add(string id, string edge, float off)
        {
            var m = byId[id];
            Vector3 ep = EdgePoint(edge, off, SizeX(m) * 0.5f, SizeZ(m) * 0.5f);
            if (!exits.TryGetValue(id, out var l)) exits[id] = l = new List<Vector2>();
            l.Add(new Vector2(ep.x, ep.z));
        }
        foreach (var p in world.portals)
        {
            if (!byId.ContainsKey(p.a) || !byId.ContainsKey(p.b) || !HasAnchors(p)) continue;
            ResolvePortalEdges(p, out string ea, out float oa, out string eb, out float ob);
            Add(p.a, ea, oa);
            Add(p.b, eb, ob);
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
    List<WorldAtlasBaker.Entry> CollectEntries()
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
        Validate();
        if (errors.Count > 0) { EditorUtility.DisplayDialog("Build World", "Fix the validation errors first.", "OK"); return; }

        if (!EditorUtility.DisplayDialog("Build World",
            $"This will (re)generate {world.maps.Length} scene(s) in {ScenesDir}/ and wire all warps.\n\n"
            + "⚠ It OVERWRITES those scene files — any hand-edits in them are lost. Continue?", "Build", "Cancel"))
            return;

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        int mask = groundLayer >= 0 ? 1 << groundLayer : ~0;

        var byId = new Dictionary<string, MapJson>();
        foreach (var m in world.maps) byId[m.id] = m;
        // Portal positions per map → fed to the generator so its roads terminate at the doorways (Part 3).
        var exitsByMap = ComputeExits(byId);

        // PASS 1 — bake every map scene from its template clone.
        var scenePathById = new Dictionary<string, string>();
        try
        {
            for (int i = 0; i < world.maps.Length; i++)
            {
                var m = world.maps[i];
                EditorUtility.DisplayProgressBar("Build World", $"Baking '{m.id}' ({i + 1}/{world.maps.Length})", (float)i / world.maps.Length);
                var tpl = templates[m.type];
                var clone = UnityEngine.Object.Instantiate(tpl);   // never mutate the template asset
                try
                {
                    clone.mapName = SceneName(m.id);
                    clone.sizeX = SizeX(m); clone.sizeZ = SizeZ(m);
                    clone.randomizeSeed = false;
                    clone.seed = m.seed != 0 ? m.seed : StableSeed(m.id);
                    if (TryParsePathTile(m.pathTile, out var pt)) clone.pathTile = pt;
                    if (m.pathWidth > 0f) clone.pathWidth = m.pathWidth;
                    if (TryParseBiome(m.biome, out var bm)) clone.biome = bm;   // per-cell theme override (else the template's biome)
                    var exits = exitsByMap.TryGetValue(m.id, out var ex) ? ex : null;
                    var res = RecipeGenerator.Generate(clone, exits);
                    if (!res.ok) { EditorUtility.ClearProgressBar(); EditorUtility.DisplayDialog("Build World", $"Failed to bake '{m.id}'. Check the Console.", "OK"); return; }
                    scenePathById[m.id] = res.path;
                }
                finally { UnityEngine.Object.DestroyImmediate(clone); }
            }

            // PASS 2 — gather paired portals/spawns per scene, then write each scene once.
            var portalsByScene = new Dictionary<string, List<PortalSpec>>();
            var spawnsByScene  = new Dictionary<string, List<SpawnSpec>>();
            void AddPortal(string id, PortalSpec s) { if (!portalsByScene.TryGetValue(id, out var l)) portalsByScene[id] = l = new List<PortalSpec>(); l.Add(s); }
            void AddSpawn(string id, SpawnSpec s)   { if (!spawnsByScene.TryGetValue(id, out var l)) spawnsByScene[id] = l = new List<SpawnSpec>(); l.Add(s); }

            if (world.portals != null)
                for (int i = 0; i < world.portals.Length; i++)
                {
                    var p = world.portals[i];
                    if (!byId.ContainsKey(p.a) || !byId.ContainsKey(p.b) || !HasAnchors(p)) continue;
                    ResolvePortalEdges(p, out string edgeA, out float offA, out string edgeB, out float offB);

                    string sceneA = SceneName(p.a), sceneB = SceneName(p.b);
                    string arrA = $"w{i}_from_{p.b}";   // land here when arriving on A from B
                    string arrB = $"w{i}_from_{p.a}";   // land here when arriving on B from A

                    AddPortal(p.a, new PortalSpec { name = $"Portal_w{i}_to_{p.b}", target = sceneB, spawnId = arrB, edge = edgeA, offset = offA });
                    AddSpawn (p.a, new SpawnSpec  { id = arrA, edge = edgeA, offset = offA });
                    AddPortal(p.b, new PortalSpec { name = $"Portal_w{i}_to_{p.a}", target = sceneA, spawnId = arrA, edge = edgeB, offset = offB });
                    AddSpawn (p.b, new SpawnSpec  { id = arrB, edge = edgeB, offset = offB });
                }

            int portalCount = 0;
            foreach (var m in world.maps)
            {
                bool hasP = portalsByScene.TryGetValue(m.id, out var pl) && pl.Count > 0;
                bool hasS = spawnsByScene.TryGetValue(m.id, out var sl) && sl.Count > 0;
                if (!hasP && !hasS) continue;

                EditorUtility.DisplayProgressBar("Build World", $"Wiring warps in '{m.id}'", 0.6f);
                float halfX = SizeX(m) * 0.5f, halfZ = SizeZ(m) * 0.5f;
                var scene = EditorSceneManager.OpenScene(scenePathById[m.id], OpenSceneMode.Single);
                SceneManager.SetActiveScene(scene);
                Physics.SyncTransforms();

                if (hasS) foreach (var s in sl)
                {
                    Vector3 raw = EdgePoint(s.edge, s.offset, halfX, halfZ) + Inward(s.edge) * SpawnInset;
                    RegionBuilder.MakeSpawn(scene, s.id, WorldBuildUtil.SurfacePoint(raw, mask));
                }
                if (hasP) foreach (var p in pl)
                {
                    Vector3 raw = EdgePoint(p.edge, p.offset, halfX, halfZ);
                    RegionBuilder.MakePortal(scene, p.name, WorldBuildUtil.SurfacePoint(raw, mask), p.target, p.spawnId, Color.white);
                    portalCount++;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();

            // Also bake the world-map atlas so the World Map HUD (N) is ready straight away.
            WorldAtlasBaker.Bake(CollectEntries(), interactive: false);

            Debug.Log($"[unwritten] World Creator built {world.maps.Length} maps + {portalCount} portals from {Path.GetFileName(sourcePath)}.");
            EditorUtility.DisplayDialog("World built",
                $"Generated {world.maps.Length} maps and {portalCount} portals, and baked the world map.\n\n"
                + "Each warp is a paired Portal3D + arrival SpawnPoint; portal colour follows the destination "
                + "(town=yellow, field=green …).\n\n"
                + "Make sure Systems is set up (Setup Systems Scene), then PLAY from any map — walk into a pad to "
                + "travel, press N for the World Map.", "OK");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    struct PortalSpec { public string name, target, spawnId, edge; public float offset; }
    struct SpawnSpec  { public string id, edge; public float offset; }

    // ── geometry ──────────────────────────────────────────────────────────────────────────────────────────
    // A point on the named edge of a rectangular plot (centred at origin, halfX×halfZ), at offset 0..1 along
    // that edge. N/S run along X (use halfX); E/W run along Z (use halfZ). Mirrors the HTML tool's edgePoint.
    static Vector3 EdgePoint(string edge, float offset, float halfX, float halfZ)
    {
        float t = Mathf.Clamp01(offset);
        float rimX = halfX - EdgeMargin, rimZ = halfZ - EdgeMargin;
        float alongX = Mathf.Lerp(-rimX, rimX, t);
        float alongZ = Mathf.Lerp(-rimZ, rimZ, t);
        switch (edge)
        {
            case "N": return new Vector3(alongX, 0f, +rimZ);
            case "S": return new Vector3(alongX, 0f, -rimZ);
            case "E": return new Vector3(+rimX, 0f, alongZ);
            case "W": return new Vector3(-rimX, 0f, alongZ);
        }
        return new Vector3(alongX, 0f, +rimZ);
    }

    // Unit vector pointing from an edge toward the plot centre (for nudging the arrival spawn inward).
    static Vector3 Inward(string edge)
    {
        switch (edge)
        {
            case "N": return new Vector3(0f, 0f, -1f);
            case "S": return new Vector3(0f, 0f, +1f);
            case "E": return new Vector3(-1f, 0f, 0f);
            case "W": return new Vector3(+1f, 0f, 0f);
        }
        return Vector3.zero;
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

    static bool TryParseBiome(string s, out Biome biome)
    {
        switch ((s ?? "").Trim().ToLowerInvariant())
        {
            case "grassland": case "grass": biome = Biome.Grassland; return true;
            case "snow": case "ice":        biome = Biome.Snow;      return true;
            case "desert": case "sand":     biome = Biome.Desert;    return true;
            case "volcanic": case "lava":   biome = Biome.Volcanic;  return true;
            case "corrupted": case "void":  biome = Biome.Corrupted; return true;
            default:                        biome = Biome.Grassland; return false;
        }
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
}
