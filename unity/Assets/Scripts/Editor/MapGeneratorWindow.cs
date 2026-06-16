using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools ▸ unwritten ▸ Generate Town. Pick a <see cref="MapRecipe"/>, hit Generate, and it:
///  1. runs <see cref="TownGenerator"/> to get a <see cref="TownLayout"/>,
///  2. creates a fresh scene with a Grid + Ground/Roads/Buildings/Obstacles/Decor/Overhead tilemaps,
///  3. commits the layout's tiles, bands the layers for the editor preview,
///  4. drops a Portal + SpawnPoint at each gate, NPC-spawn markers, and a MonsterSpawner per monster cell.
///
/// Generate builds an UNSAVED preview scene — nothing hits disk. Generate again to replace it (the old
/// unsaved preview is discarded automatically — no Project-window cleanup), or hit "Save Town to
/// Assets/Scenes" to keep it and register it in Build Settings. Then hand-finish: wire each Portal's
/// Target Scene, and use Bake Buildings to Objects to drag houses around. At Play, DepthSortRuntime bakes
/// the Buildings/Obstacles tilemaps into foot-sorted props.
/// </summary>
public class MapGeneratorWindow : EditorWindow
{
    MapRecipe recipe;

    const string RandomizePrefKey = "unwritten.generateTown.randomizeSeed";
    bool randomizeSeed = true;
    Scene lastGenerated;   // the current preview scene (unsaved until you hit Save)

    [MenuItem("Tools/unwritten/Generate Town")]
    static void Open() => GetWindow<MapGeneratorWindow>("Generate Town");

    void OnEnable() => randomizeSeed = EditorPrefs.GetBool(RandomizePrefKey, true);

    void OnGUI()
    {
        EditorGUILayout.LabelField("Town Map Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Pick a MapRecipe and Generate — it builds an UNSAVED preview scene (nothing hits disk yet) " +
            "with the town baked into Ground/Roads/Buildings/Obstacles/Decor/Overhead tilemaps + a Portal " +
            "& SpawnPoint per gate.\n\n" +
            "Don't like it? Just Generate again — the old preview is discarded automatically, no cleanup.\n" +
            "Like it? Hit \"Save Town to Assets/Scenes\" to keep it (and register it in Build Settings), " +
            "then wire each Portal's Target Scene and Play.", MessageType.Info);

        recipe = (MapRecipe)EditorGUILayout.ObjectField("Recipe", recipe, typeof(MapRecipe), false);

        EditorGUI.BeginChangeCheck();
        randomizeSeed = EditorGUILayout.ToggleLeft(
            "Randomize seed each Generate (unique town every time)", randomizeSeed);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(RandomizePrefKey, randomizeSeed);
        if (recipe != null)
            EditorGUILayout.LabelField("Seed", randomizeSeed ? "rolled fresh on Generate" : recipe.seed.ToString());

        using (new EditorGUI.DisabledScope(recipe == null))
            if (GUILayout.Button("Generate Preview", GUILayout.Height(32)))
            {
                // Defer OUT of OnGUI: Generate creates/closes/replaces scenes and may open save dialogs,
                // which pump editor events and call ExitGUI mid-layout — that unbalances IMGUI's
                // layout-group stack ("EndLayoutGroup: BeginLayoutGroup must be called first."). Running on
                // the next editor tick keeps all of that outside the active GUI pass.
                var r = recipe;
                bool rnd = randomizeSeed;
                EditorApplication.delayCall += () => Generate(r, rnd);
            }

        EditorGUILayout.Space();

        bool hasUnsavedPreview = lastGenerated.IsValid() && lastGenerated.isLoaded
                                 && string.IsNullOrEmpty(lastGenerated.path);
        using (new EditorGUI.DisabledScope(!hasUnsavedPreview))
            if (GUILayout.Button("Save Town to Assets/Scenes", GUILayout.Height(24)))
                EditorApplication.delayCall += SaveGenerated;   // deferred out of OnGUI — see Generate button

        if (GUILayout.Button("Open Scenes Folder"))
            OpenScenesFolder();

        if (hasUnsavedPreview)
            EditorGUILayout.HelpBox(
                "Current preview is UNSAVED. Generate again to replace it, or Save to keep it.",
                MessageType.Warning);
    }

    void Generate(MapRecipe recipe, bool randomizeSeed)
    {
        if (recipe.theme == null)
        {
            EditorUtility.DisplayDialog("Generate Town", "This recipe has no TownTheme assigned.", "OK");
            return;
        }

        if (randomizeSeed)
        {
            recipe.seed = System.Guid.NewGuid().GetHashCode();   // fresh & unique each Generate…
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);              // …recorded on the recipe so the town stays reproducible
        }

        var layout = TownGenerator.Generate(recipe);

        // Discard the old throwaway preview(s) FIRST: Unity refuses to create a new ADDITIVE scene while
        // ANY untitled (unsaved, path-less) scene is loaded — "Cannot create a new scene additively with
        // an untitled scene unsaved." The catch: after a domain reload the window is recreated, so
        // `lastGenerated` resets to invalid while the old untitled preview is STILL loaded — closing only
        // the tracked preview missed that orphan and Generate threw. So close EVERY loaded untitled scene;
        // in this tool an untitled scene is always a disposable preview, so we never nag to save them.
        // We can't close the LAST open scene (Unity always keeps ≥1). When a real (named) scene is also
        // open it holds the editor alive, so we can close all untitled previews; otherwise we leave the
        // final untitled scene for NewScene(Single) to replace atomically (Single never hits this error).
        bool hasNamedScene = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isLoaded && !string.IsNullOrEmpty(s.path)) { hasNamedScene = true; break; }
        }

        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            var s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded || !string.IsNullOrEmpty(s.path)) continue;   // keep named scenes
            if (SceneManager.sceneCount <= 1) break;                      // never close the final scene
            EditorSceneManager.CloseScene(s, true);
        }

        // Offer to save any OTHER open scene with unsaved work. The disposable previews are already closed
        // above, so this only prompts for your real (named) scenes. With no named scene loaded there is
        // nothing real to save — only a throwaway preview remains — so we skip the prompt entirely (and
        // avoid nagging to save that throwaway). Cancel = abort, change nothing.
        if (hasNamedScene && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        // Remember every scene still open, so we can close them once the preview exists — a leftover
        // SAVED town renders its tilemaps on top of the new preview, and because each has a different
        // shape the overlap looks exactly like broken / sharp edges.
        var others = new List<Scene>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isLoaded) others.Add(s);
        }

        // If an untitled preview is still loaded, it's the only scene (kept alive above): replace it in
        // one shot with Single mode, which sidesteps the additive-untitled restriction. Otherwise add the
        // preview alongside the named scenes additively, then hide those named scenes.
        bool onlyUntitledLeft = !hasNamedScene
            && SceneManager.sceneCount == 1
            && string.IsNullOrEmpty(SceneManager.GetSceneAt(0).path);
        var scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            onlyUntitledLeft ? NewSceneMode.Single : NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);   // new objects land in this scene by default
        lastGenerated = scene;

        if (!onlyUntitledLeft)
            foreach (var s in others)         // hide the leftovers so ONLY the preview draws
                if (s.isLoaded && s != scene) EditorSceneManager.CloseScene(s, true);

        // --- Grid + layers ---
        var grid = new GameObject("Grid", typeof(Grid)).GetComponent<Grid>();
        SceneManager.MoveGameObjectToScene(grid.gameObject, scene);

        // No colliders on the VISUAL layers — collision is the single pre-baked "Collision" tilemap below
        // (merged CompositeCollider2D), so we never carry hundreds of per-cell box colliders.
        var ground    = NewLayer(grid, "Ground", false);
        var obstacles = NewLayer(grid, "Obstacles", false);
        var buildings = NewLayer(grid, "Buildings", false);
        var decor     = NewLayer(grid, "Decor", false);
        var overhead  = NewLayer(grid, "Overhead", false);

        // --- commit tiles ---
        // Grass base + dirt PATCHES + dirt ROADS all go on the ONE Ground tilemap, so the shared Dirt
        // RuleTile auto-tiles against the FULL contiguous dirt shape. (Roads used to be a separate
        // tilemap; a RuleTile only sees neighbours on its OWN tilemap, so splitting dirt across two
        // tilemaps made each see half the shape → the middle tile landed at visual edges = hard edges.)
        CommitArray(ground, layout, layout.ground);
        CommitArray(ground, layout, layout.road);
        CommitArray(obstacles, layout, layout.tree);
        CommitArray(decor, layout, layout.decor);
        CommitArray(overhead, layout, layout.overhead);
        foreach (var kv in layout.building)
            buildings.SetTile(new Vector3Int(kv.Key.x, kv.Key.y, 0), kv.Value);

        // --- pre-baked collision (merged, static, computed once) ---
        // Materialise the per-prop collision footprint (Auto/Custom/None) + the boundary — all in
        // HALF-cells — into ONE Collision object whose CompositeCollider2D merges the half-size boxes
        // into a few polygons. No on-Play collider generation — see CollisionLayerTools / DepthSortRuntime.
        var collision = CollisionLayerTools.GetOrCreate(grid);
        CollisionLayerTools.Paint(grid, collision, layout.collision);

        // A RuleTile set during a scripted SetTile loop resolves against the neighbours that exist AT
        // THAT MOMENT and isn't re-evaluated when later neighbours are added — so early cells freeze with
        // the middle tile where an edge belongs. The Tile Palette avoids this by refreshing every stroke;
        // we replicate it with one final full refresh so every RuleTile re-resolves against the COMPLETE
        // shape (exactly like hand-painting). This is THE fix for the hard "middle-tile-at-edge" seams.
        foreach (var tm in grid.GetComponentsInChildren<Tilemap>(true))
            tm.RefreshAllTiles();

        // band for the editor preview (runtime re-bands on Play anyway)
        foreach (var tm in grid.GetComponentsInChildren<Tilemap>(true))
            DepthSortRuntime.BandTilemap(tm);

        // --- entities ---
        foreach (var g in layout.gates)
        {
            MakePortal(grid, scene, g);
            MakeSpawnPoint(grid, scene, g.id, g.cell - g.outward);   // arrival just inside the gate
        }
        for (int i = 0; i < layout.npcSpawns.Count; i++)
            MakeMarker(grid, scene, "NPCSpawn_" + i, layout.npcSpawns[i]);

        var monPrefab = recipe.theme.monsterPrefabs.Count > 0 ? recipe.theme.monsterPrefabs[0] : null;
        for (int i = 0; i < layout.monsterSpawns.Count; i++)
            MakeSpawner(grid, scene, "MonsterSpawner_" + i, layout.monsterSpawns[i], monPrefab);

        // --- pre-bake visuals NOW (not at Play) ---
        // Convert the Obstacles/Buildings tilemaps into static, foot-sorted prop objects with fixed sort
        // orders at GENERATE time, so the saved scene loads with zero per-Play bake work or per-frame
        // sorting. DepthSortRuntime's play-time bake then sees the renderers already disabled and skips.
        DepthSortRuntime.BakeObstacles();

        // --- preview only: nothing is written to disk until you hit "Save Town to Assets/Scenes" ---
        Debug.Log($"[unwritten] Generated PREVIEW of '{recipe.mapName}' ({recipe.width}×{recipe.height}, seed {recipe.seed}).  " +
                  $"{layout.gates.Count} gate(s), {layout.npcSpawns.Count} NPC marker(s), {layout.monsterSpawns.Count} spawner(s).  " +
                  "Unsaved — Generate again to replace it, or click 'Save Town to Assets/Scenes' to keep it.");

        Repaint();   // ran deferred (delayCall), so nudge the window to refresh Save-button state + warning
    }

    /// <summary>Persist the current unsaved preview to Assets/Scenes/&lt;mapName&gt;.unity and register it in Build Settings.</summary>
    void SaveGenerated()
    {
        if (!lastGenerated.IsValid() || !lastGenerated.isLoaded) return;

        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        string name = recipe != null ? recipe.mapName : lastGenerated.name;
        string path = "Assets/Scenes/" + Sanitize(name) + ".unity";

        if (System.IO.File.Exists(path) &&
            !EditorUtility.DisplayDialog("Overwrite town?",
                $"'{path}' already exists.\n\nOverwrite it with this town?", "Overwrite", "Cancel"))
            return;

        EditorSceneManager.SaveScene(lastGenerated, path);
        AddToBuildSettings(path);

        var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);   // flashes it in the Project window so you don't have to hunt for it

        Debug.Log($"[unwritten] Saved town → {path} and added to Build Settings.  ⚠ Wire each Portal's Target Scene, then press Play.");

        Repaint();   // ran deferred (delayCall) — the preview now has a path, so refresh the Save button
    }

    /// <summary>Select + ping Assets/Scenes in the Project window for quick access.</summary>
    static void OpenScenesFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Scenes");
        Selection.activeObject = folder;
        EditorGUIUtility.PingObject(folder);
    }

    // ---- helpers -------------------------------------------------------------

    static Tilemap NewLayer(Grid grid, string name, bool collider)
    {
        var go = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
        go.transform.SetParent(grid.transform, false);
        if (collider) go.AddComponent<TilemapCollider2D>();   // editor-preview solidity; runtime bake replaces it
        return go.GetComponent<Tilemap>();
    }

    static void CommitArray(Tilemap tm, TownLayout L, TileBase[] cells)
    {
        for (int y = 0; y < L.height; y++)
            for (int x = 0; x < L.width; x++)
            {
                var t = cells[L.Idx(x, y)];
                if (t != null) tm.SetTile(new Vector3Int(x, y, 0), t);
            }
    }

    static Vector3 CellCenter(Grid grid, Vector2Int c)
        => grid.GetCellCenterWorld(new Vector3Int(c.x, c.y, 0));

    static void MakePortal(Grid grid, Scene scene, Gate g)
    {
        var go = new GameObject("Portal_" + g.id, typeof(BoxCollider2D), typeof(Portal));
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = CellCenter(grid, g.cell);
        var col = go.GetComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = Vector2.one;
        go.GetComponent<Portal>().spawnId = g.id;   // targetScene left empty for you to wire
    }

    static void MakeSpawnPoint(Grid grid, Scene scene, string id, Vector2Int cell)
    {
        var go = new GameObject("SpawnPoint_" + id, typeof(SpawnPoint));
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = CellCenter(grid, cell);
        go.GetComponent<SpawnPoint>().id = id;
    }

    static void MakeMarker(Grid grid, Scene scene, string name, Vector2Int cell)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = CellCenter(grid, cell);
    }

    static void MakeSpawner(Grid grid, Scene scene, string name, Vector2Int cell, GameObject prefab)
    {
        var go = new GameObject(name, typeof(MonsterSpawner));
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = CellCenter(grid, cell);
        go.GetComponent<MonsterSpawner>().monsterPrefab = prefab;
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in scenes) if (s.path == path) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "GeneratedTown";
        foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return name;
    }
}
