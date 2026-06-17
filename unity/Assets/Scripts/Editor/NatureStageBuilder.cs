using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the <b>content-only</b> nature map <c>Assets/Scenes/NatureStage.unity</c>: the artist-sculpted
/// <c>Ground_01</c> terrain (baked-in hills = free elevation) on the <c>Ground</c> layer, height-aware
/// <see cref="Pathfinder3DConfig"/>, and a <see cref="SpawnPoint"/> — and nothing else. The player /
/// camera / HUD / cursor live in the persistent <c>Systems</c> scene (see <see cref="SystemsSceneBuilder"/>),
/// so this map never rebuilds them: on Play, Systems boots and loads this map underneath, and the player
/// arrives at the SpawnPoint. Fill it with <c>Scatter Nature</c> + <c>Populate Mobs</c>. Registered in
/// Build Settings so <see cref="MapManager"/> can load it by name. Safe to re-run (overwrites the scene).
/// </summary>
public static class NatureStageBuilder
{
    const string ScenePath  = "Assets/Scenes/NatureStage.unity";
    const string GroundPrefab = "Assets/Prefabs/LowPolyNature/Ground_01.prefab";

    /// <summary>Drop the looping forest ambience into the CURRENTLY OPEN map without rebuilding it —
    /// so you don't lose scattered props / mobs. Idempotent (upgrades an existing "Ambient" in place).</summary>
    [MenuItem("Tools/unwritten/Add Forest Ambience (day/night)", priority = 6)]
    static void AddAmbienceMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Add Forest Ambience", "Exit Play mode first, then re-run.", "OK"); return; }

        var scene = SceneManager.GetActiveScene();
        var amb = EnsureForestAmbience(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Add Forest Ambience",
            amb != null && amb.dayClip != null
                ? $"Added day/night forest ambience to '{scene.name}'. Press Play — it crossfades " +
                  "Forest Day ↔ Forest Night locked to the TimeOfDay clock (tune/scrub on GameSystems ▸ TimeOfDay)."
                : "Couldn't find the Forest Day/Night loops in Assets/Audio/TomMusic. Is the pack imported?", "OK");
    }

    // Create (or upgrade) the per-map looping ambience as a day/night crossfade. The DayNightAmbience
    // component owns its own looping AudioSources, which survive the Systems load (MapManager strips
    // AudioListeners/cameras/lights, not the map's AudioSources). Removes any stale single AudioSource
    // from the old forest-hum so day audio doesn't double up.
    static DayNightAmbience EnsureForestAmbience(Scene scene)
    {
        GameObject go = null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Ambient") { go = root; break; }

        if (go == null)
        {
            go = new GameObject("Ambient");
            SceneManager.MoveGameObjectToScene(go, scene);
        }
        else
            foreach (var old in go.GetComponents<AudioSource>())   // drop the legacy single-loop source
                Object.DestroyImmediate(old);

        var amb = go.GetComponent<DayNightAmbience>();
        if (amb == null) amb = go.AddComponent<DayNightAmbience>();
        amb.dayClip   = SfxAssets.Ambient("BGS Loops/Forest Day",   "Forest Day");
        amb.nightClip = SfxAssets.Ambient("BGS Loops/Forest Night", "Forest Night");
        amb.maxVolume = 0.22f;
        return amb;
    }

    [MenuItem("Tools/unwritten/New Nature Stage (low-poly ground)", priority = 5)]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Nature Stage", "Exit Play mode first, then re-run.", "OK"); return; }

        var groundSrc = AssetDatabase.LoadAssetAtPath<GameObject>(GroundPrefab);
        if (groundSrc == null)
        {
            EditorUtility.DisplayDialog("Nature Stage",
                $"No baked ground prefab at:\n{GroundPrefab}\n\n" +
                "Run Tools ▸ unwritten ▸ Build Prefabs ▸ Low Poly Nature first, then re-run.", "OK");
            return;
        }

        int groundLayer = EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Nature Stage", "No free layer slot to create a 'Ground' layer. Free one in Project Settings ▸ Tags and Layers, then re-run.", "OK"); return; }
        int groundMask = 1 << groundLayer;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        // A Sun so the map is LIT while you edit it. At Play it's disabled by MapManager (Systems owns
        // the single global light), so this never double-lights the scene — it's just for authoring.
        Build25DSpike.BuildLight();

        // The sculpted terrain as the floor, on the Ground layer so movement + pathfinding rays hit it.
        var ground = (GameObject)PrefabUtility.InstantiatePrefab(groundSrc);
        ground.name = "Ground_Nature";
        ground.transform.position = Vector3.zero;
        SetLayerRecursive(ground, groundLayer);
        EnsureMeshColliders(ground);

        // Height-aware routing so click-to-move follows the hills (cliffs block, gentle slopes walk).
        var cfg = new GameObject("Pathfinder3DConfig").AddComponent<Pathfinder3DConfig>();
        cfg.heightAware = true;
        cfg.groundMask = groundMask;
        cfg.maxStep = 0.6f;

        // The player arrives here (the persistent player in Systems snaps onto the terrain at this XZ).
        Physics.SyncTransforms();
        var center = ground.GetComponentInChildren<Renderer>() != null
            ? ground.GetComponentInChildren<Renderer>().bounds.center : Vector3.zero;
        var spawn = new GameObject("SpawnPoint").AddComponent<SpawnPoint>();
        spawn.id = "Start";
        spawn.transform.position = SurfacePoint(center, groundMask);

        EnsureForestAmbience(scene);   // looping forest BGS (per-map AudioSource)

        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded && alreadyOpen != scene)
            EditorSceneManager.CloseScene(alreadyOpen, true);

        EditorSceneManager.SaveScene(scene, ScenePath);
        RegisterInBuildSettings(ScenePath);            // so MapManager can LoadSceneAsync it by name
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Nature Stage — built",
            $"Built the content-only map {ScenePath} (terrain + Ground layer + pathfinding + SpawnPoint).\n\n" +
            "FILL IT: run Scatter Nature, then Populate Mobs.\n\n" +
            "PLAY: this map has no player/camera now — they live in Systems. Run 'Setup Systems Scene' once, " +
            "then press Play here: Systems boots and loads this map, and the player spawns at the SpawnPoint.\n\n" +
            "Tip: swap Ground_02 / Ground_03 or place several for a bigger base; move the SpawnPoint where you want.",
            "OK");
        Debug.Log($"[unwritten] Nature Stage (content map) built → {ScenePath} (Ground layer {groundLayer}).");
    }

    // Drop a point onto the terrain surface below 'p' (else keep p).
    static Vector3 SurfacePoint(Vector3 p, int groundMask)
    {
        if (Physics.Raycast(new Vector3(p.x, p.y + 60f, p.z), Vector3.down, out var hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point;
        return p;
    }

    static void RegisterInBuildSettings(string scenePath)
    {
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path == scenePath) return;   // already listed
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
        { new EditorBuildSettingsScene(scenePath, true) };
        EditorBuildSettings.scenes = list.ToArray();
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    // Find or create a user layer named "Ground" (user layers are slots 8..31). Returns its index, or -1.
    public static int EnsureGroundLayer()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return -1;
        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("layers");
        if (layers == null) return -1;

        for (int i = 8; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == "Ground") return i;
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue)) { p.stringValue = "Ground"; so.ApplyModifiedProperties(); return i; }
        }
        return -1;
    }

    // The baked prefab already carries MeshColliders, but guard for a raw/old prefab too.
    static void EnsureMeshColliders(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
    }
}
