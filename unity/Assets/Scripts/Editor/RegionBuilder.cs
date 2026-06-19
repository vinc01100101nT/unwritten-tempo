using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click bakers for the M2 starting REGION — the skeleton loop: a safe <b>Town</b> hub linked to the
/// existing <b>NatureStage</b> field, with an RO-style <b>field boss</b> roaming the field. Mirrors
/// <see cref="NatureStageBuilder"/>'s content-only pattern (terrain + Ground layer + pathfinding + spawns,
/// no player/camera — those live in the persistent <c>Systems</c> scene) and reuses its helpers.
///
/// Travel contract (ids matched by <see cref="PersistentPlayer3D"/> on arrival):
/// <list type="bullet">
/// <item>Town has SpawnPoints <c>Start</c> (boot) + <c>FromField</c>, and a <see cref="Portal3D"/> → NatureStage @ <c>FromTown</c>.</item>
/// <item>Field gets SpawnPoint <c>FromTown</c> + a <see cref="Portal3D"/> → Town @ <c>FromField</c> (added non-destructively).</item>
/// </list>
/// </summary>
public static class RegionBuilder
{
    const string TownScenePath = "Assets/Scenes/Town.unity";
    const string TownGroundPrefab = "Assets/Prefabs/LowPolyNature/Ground_02.prefab";
    const string FieldScene = "NatureStage";
    const string TownScene  = "Town";

    // ── 1) TOWN HUB ────────────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/unwritten/Region/Build Town Hub", priority = 20)]
    static void BuildTownHub()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Town Hub", "Exit Play mode first, then re-run.", "OK"); return; }

        var groundSrc = AssetDatabase.LoadAssetAtPath<GameObject>(TownGroundPrefab);
        if (groundSrc == null)
        { EditorUtility.DisplayDialog("Town Hub", $"No ground prefab at:\n{TownGroundPrefab}\n\nRun Build Prefabs ▸ Low Poly Nature first.", "OK"); return; }

        int groundLayer = NatureStageBuilder.EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Town Hub", "No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers.", "OK"); return; }
        int groundMask = 1 << groundLayer;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        Build25DSpike.BuildLight();   // authoring Sun (disabled at Play; Systems owns the runtime light)

        var ground = (GameObject)PrefabUtility.InstantiatePrefab(groundSrc);
        ground.name = "Ground_Town";
        ground.transform.position = Vector3.zero;
        NatureStageBuilder.SetLayerRecursive(ground, groundLayer);
        NatureStageBuilder.EnsureMeshColliders(ground);

        var cfg = new GameObject("Pathfinder3DConfig").AddComponent<Pathfinder3DConfig>();
        cfg.heightAware = true; cfg.groundMask = groundMask; cfg.maxStep = 0.6f;

        Physics.SyncTransforms();
        var r = ground.GetComponentInChildren<Renderer>();
        Vector3 center = r != null ? r.bounds.center : Vector3.zero;
        // The portal to the field sits toward the +X edge; spawns sit a few units back from it (so you don't
        // bounce straight back through on arrival, and don't boot on top of it).
        Vector3 portalPos = NatureStageBuilder.SurfacePoint(center + new Vector3(8f, 0f, 0f), groundMask);
        Vector3 startPos  = NatureStageBuilder.SurfacePoint(center, groundMask);
        Vector3 backPos   = NatureStageBuilder.SurfacePoint(center + new Vector3(4.5f, 0f, 0f), groundMask);

        MakeSpawn(scene, "Start", startPos);
        MakeSpawn(scene, "FromField", backPos);
        MakePortal(scene, "Portal_ToField", portalPos, FieldScene, "FromTown", new Color(0.4f, 0.8f, 1f));

        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var open = SceneManager.GetSceneByPath(TownScenePath);
        if (open.IsValid() && open.isLoaded && open != scene) EditorSceneManager.CloseScene(open, true);

        EditorSceneManager.SaveScene(scene, TownScenePath);
        NatureStageBuilder.RegisterInBuildSettings(TownScenePath);
        EditorSceneManager.OpenScene(TownScenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Town Hub — built",
            $"Built {TownScenePath} (ground + Ground layer + pathfinding + spawns + a portal to the field).\n\n" +
            "NEXT:\n" +
            "1. Open NatureStage and run Region ▸ Link Field → Town (adds the return portal).\n" +
            "2. (optional) Region ▸ Spawn Field Boss while NatureStage is open.\n" +
            "3. Hand-finish the town art (houses / NPCs / props).\n\n" +
            "PLAY: open Town and press Play — Systems boots, the town loads, you spawn at 'Start'. Walk into the " +
            "cyan pad (+X) to warp to the field; walk into the field's pad to come back.", "OK");
        Debug.Log($"[unwritten] Town Hub built → {TownScenePath} (Ground layer {groundLayer}).");
    }

    // ── 2) LINK FIELD → TOWN ───────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/unwritten/Region/Link Field → Town (open field map)", priority = 21)]
    static void LinkFieldToTown()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Link Field → Town", "Exit Play mode first, then re-run.", "OK"); return; }

        var scene = SceneManager.GetActiveScene();
        if (scene.name == TownScene)
        { EditorUtility.DisplayDialog("Link Field → Town", "The active scene is Town. Open the FIELD map (NatureStage) first, then re-run.", "OK"); return; }

        int groundLayer = NatureStageBuilder.EnsureGroundLayer();
        int groundMask = groundLayer >= 0 ? 1 << groundLayer : ~0;
        Physics.SyncTransforms();

        // Anchor near the field's default arrival ("Start" if present, else origin).
        Vector3 anchor = Vector3.zero;
        foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            if (sp.id == "Start") { anchor = sp.transform.position; break; }

        Vector3 spawnPos  = NatureStageBuilder.SurfacePoint(anchor + new Vector3(-4.5f, 0f, 0f), groundMask);
        Vector3 portalPos = NatureStageBuilder.SurfacePoint(anchor + new Vector3(-8f, 0f, 0f), groundMask);

        bool addedSpawn  = EnsureSpawn(scene, "FromTown", spawnPos);
        bool addedPortal = EnsurePortal(scene, "Portal_ToTown", portalPos, TownScene, "FromField", new Color(1f, 0.85f, 0.4f));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Link Field → Town — done",
            $"Wired '{scene.name}' back to the Town:\n" +
            $"• SpawnPoint 'FromTown' {(addedSpawn ? "added" : "already present")}\n" +
            $"• Portal_ToTown → Town @ 'FromField' {(addedPortal ? "added" : "already present")}\n\n" +
            "The gold pad warps back to town. Move the pad/spawn where you want.", "OK");
        Debug.Log($"[unwritten] Linked field '{scene.name}' → Town.");
    }

    // ── 3) FIELD BOSS ──────────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/unwritten/Region/Spawn Field Boss (open field map)", priority = 22)]
    static void SpawnFieldBoss()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Spawn Field Boss", "Exit Play mode first, then re-run.", "OK"); return; }

        var scene = SceneManager.GetActiveScene();
        if (scene.name == TownScene)
        { EditorUtility.DisplayDialog("Spawn Field Boss", "The active scene is Town (a safe hub). Open the FIELD map first.", "OK"); return; }

        int groundLayer = NatureStageBuilder.EnsureGroundLayer();
        int groundMask = groundLayer >= 0 ? 1 << groundLayer : ~0;
        Physics.SyncTransforms();

        // Drop the boss in an open spot away from the default arrival so you walk into it, not onto it.
        Vector3 anchor = Vector3.zero;
        foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            if (sp.id == "Start") { anchor = sp.transform.position; break; }
        Vector3 pos = NatureStageBuilder.SurfacePoint(anchor + new Vector3(6f, 0f, 10f), groundMask);

        var boss = Build25DSpike.BuildMonster(MobRoster.Boss, pos);
        SceneManager.MoveGameObjectToScene(boss, scene);

        var banner = boss.AddComponent<BossBanner>();
        banner.bossName = MobRoster.Boss.name;

        // Make it a slow-respawning event, not trash: long delay + wide home so it roams.
        var respawn = boss.GetComponent<MobRespawn>();
        if (respawn != null) { respawn.respawnDelay = 120f; respawn.homeRadius = 10f; }

        Selection.activeGameObject = boss;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Field Boss — spawned",
            $"Spawned '{MobRoster.Boss.name}' in '{scene.name}' ({MobRoster.Boss.maxHealth} HP, ×{MobRoster.Boss.scale} size, " +
            "name + HP banner). It roams, aggros wide, and respawns 120s after death.\n\n" +
            "Move it where you want a boss to live. Re-run to add another.", "OK");
        Debug.Log($"[unwritten] Field boss '{MobRoster.Boss.name}' spawned in '{scene.name}'.");
    }

    // ── HAND-PLACE HELPER: snap dragged-in props onto the terrain ────────────────────────────────────
    [MenuItem("Tools/unwritten/Region/Drop Selected to Ground", priority = 23)]
    static void DropSelectedToGround()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        { EditorUtility.DisplayDialog("Drop to Ground", "Select one or more objects (e.g. houses you dragged in) first.", "OK"); return; }

        int groundLayer = NatureStageBuilder.EnsureGroundLayer();
        int groundMask = groundLayer >= 0 ? 1 << groundLayer : ~0;
        Physics.SyncTransforms();

        int moved = 0;
        foreach (var go in sel)
        {
            Vector3 p = go.transform.position;
            // Cast from well above so we land on the terrain even if the object is currently buried in it.
            if (Physics.Raycast(new Vector3(p.x, p.y + 100f, p.z), Vector3.down, out var hit, 400f, groundMask, QueryTriggerInteraction.Ignore))
            {
                Undo.RecordObject(go.transform, "Drop to Ground");
                go.transform.position = new Vector3(p.x, hit.point.y, p.z);   // Kenney props pivot at their base
                moved++;
            }
        }

        if (moved > 0 && !EditorApplication.isPlaying)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[unwritten] Dropped {moved}/{sel.Length} selected object(s) to the ground.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────
    static GameObject MakeSpawn(Scene scene, string id, Vector3 pos)
    {
        var go = new GameObject($"SpawnPoint_{id}");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = pos;
        go.AddComponent<SpawnPoint>().id = id;
        return go;
    }

    // Add a SpawnPoint only if this scene doesn't already have one with that id.
    static bool EnsureSpawn(Scene scene, string id, Vector3 pos)
    {
        foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            if (sp.id == id && sp.gameObject.scene == scene) return false;
        MakeSpawn(scene, id, pos);
        return true;
    }

    static GameObject MakePortal(Scene scene, string name, Vector3 pos, string targetScene, string spawnId, Color padColor)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = pos;

        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 1.2f, 0f);
        box.size = new Vector3(3f, 2.4f, 3f);

        var portal = go.AddComponent<Portal3D>();
        portal.targetScene = targetScene;
        portal.spawnId = spawnId;

        // A visible "warp pad" so the player can see the doorway in-game (art is hand-finished later).
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "WarpPad";
        Object.DestroyImmediate(pad.GetComponent<Collider>());   // visual only — the trigger lives on the parent
        pad.transform.SetParent(go.transform, false);
        pad.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        pad.transform.localScale = new Vector3(1.5f, 0.06f, 1.5f);
        var mr = pad.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", padColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", padColor);
        mr.sharedMaterial = mat;
        return go;
    }

    // Add a Portal3D only if this scene doesn't already have one pointing at the same target.
    static bool EnsurePortal(Scene scene, string name, Vector3 pos, string targetScene, string spawnId, Color padColor)
    {
        foreach (var p in Object.FindObjectsByType<Portal3D>(FindObjectsSortMode.None))
            if (p.targetScene == targetScene && p.gameObject.scene == scene) return false;
        MakePortal(scene, name, pos, targetScene, spawnId, padColor);
        return true;
    }
}
