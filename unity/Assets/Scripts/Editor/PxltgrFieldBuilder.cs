using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the <b>content-only</b> open field <c>Assets/Scenes/NatureStage.unity</c> entirely from the Pxltgr
/// pack — a flat <see cref="PxltgrGround"/> grass plot on the <c>Ground</c> layer, a <see cref="SpawnPoint"/>,
/// and looping forest ambience. Replaces the retired <c>NatureStageBuilder</c> (which used the deleted
/// LowPolySimpleNature pack), so the field now matches the Pxltgr town for a consistent art style.
///
/// The scene file keeps the name <c>NatureStage</c> so the existing travel-loop wiring (Town's portal target,
/// <see cref="FantasyTownBuilder"/>, <see cref="RegionBuilder"/>) still resolves it by name with no churn.
///
/// The player / camera / HUD / cursor live in the persistent <c>Systems</c> scene, so this map never rebuilds
/// them: on Play, Systems boots and loads this map underneath, and the player arrives at the SpawnPoint. Fill
/// it with <c>Scatter Nature (Pxltgr)</c> + <c>Populate Mobs</c>. Safe to re-run (overwrites the scene).
/// </summary>
public static class PxltgrFieldBuilder
{
    const string ScenePath = "Assets/Scenes/NatureStage.unity";
    const float  FieldX = 60f;   // plot size (world units) — bigger than town for a roaming field
    const float  FieldZ = 60f;

    [MenuItem("Tools/unwritten/World/New Field (Pxltgr grass)", priority = 200)]
    static void BuildTiled() => Build(false);

    [MenuItem("Tools/unwritten/World/New Field (Blended grass→dirt)", priority = 202)]
    static void BuildBlended() => Build(true);

    // blended=false → the classic grid of swapped tiles (PxltgrGround); blended=true → a single mesh whose
    // dirt patches feather into the grass via vertex-alpha (PxltgrGroundBlend). Same scene path/wiring either way.
    static void Build(bool blended)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Pxltgr Field", "Exit Play mode first, then re-run.", "OK"); return; }

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Pxltgr Field", "No free layer slot to create a 'Ground' layer. Free one in Project Settings ▸ Tags and Layers, then re-run.", "OK"); return; }
        int groundMask = 1 << groundLayer;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        // A Sun so the map is LIT while you edit it. At Play it's disabled by MapManager (Systems owns the
        // single global light), so this never double-lights the scene — it's just for authoring.
        Build25DSpike.BuildLight();

        // Flat Pxltgr ground (tiled or blended) + one flat floor collider on the Ground layer + a Pathfinder3DConfig.
        var ground = blended
            ? PxltgrGroundBlend.LayPlot(scene, "Ground_Field", Vector3.zero, FieldX, FieldZ, groundLayer)
            : PxltgrGround.LayPlot(scene, "Ground_Field", Vector3.zero, FieldX, FieldZ, groundLayer);
        if (ground == null)
        {
            EditorUtility.DisplayDialog("Pxltgr Field",
                "No 'ground_grass' prefab in Assets/Prefabs/Props.\n\n" +
                "Run Tools ▸ unwritten ▸ Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first, then re-run.", "OK");
            EditorSceneManager.CloseScene(scene, true);
            return;
        }

        // The player arrives here (the persistent player in Systems snaps onto the ground at this XZ).
        var spawn = new GameObject("SpawnPoint").AddComponent<SpawnPoint>();
        SceneManager.MoveGameObjectToScene(spawn.gameObject, scene);
        spawn.id = "Start";
        spawn.transform.position = WorldBuildUtil.SurfacePoint(Vector3.zero, groundMask);

        EnsureForestAmbience(scene);   // looping day/night forest BGS (per-map AudioSource)

        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded && alreadyOpen != scene)
            EditorSceneManager.CloseScene(alreadyOpen, true);

        EditorSceneManager.SaveScene(scene, ScenePath);
        WorldBuildUtil.RegisterInBuildSettings(ScenePath);   // so MapManager / portals can load it by name
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Pxltgr Field — built",
            $"Built the content-only field {ScenePath} ({(blended ? "blended grass→dirt mesh" : "Pxltgr grass tiles")} + Ground layer + pathfinding + SpawnPoint + forest ambience).\n\n" +
            "FILL IT: run Scatter Nature (Pxltgr), then Populate Mobs.\n\n" +
            "PLAY: this map has no player/camera — they live in Systems. Run 'Setup Systems Scene' once, then " +
            "press Play: Systems boots and loads this map, and the player spawns at the SpawnPoint.", "OK");
        Debug.Log($"[unwritten] Pxltgr Field built → {ScenePath} (Ground layer {groundLayer}).");
    }

    // ── FOREST AMBIENCE (moved from the retired NatureStageBuilder) ────────────────────────────────────
    /// <summary>Drop the looping forest ambience into the CURRENTLY OPEN map without rebuilding it — so you
    /// don't lose scattered props / mobs. Idempotent (upgrades an existing "Ambient" in place).</summary>
    [MenuItem("Tools/unwritten/World/Add Forest Ambience (day/night)", priority = 201)]
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

    // Create (or upgrade) the per-map looping ambience as a day/night crossfade. The DayNightAmbience component
    // owns its own looping AudioSources, which survive the Systems load (MapManager strips AudioListeners/
    // cameras/lights, not the map's AudioSources). Removes any stale single AudioSource so day audio doesn't double.
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
}
