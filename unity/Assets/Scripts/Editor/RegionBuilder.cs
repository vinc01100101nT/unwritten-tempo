using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click bakers for the M2 starting REGION — the skeleton loop: a safe <b>Town</b> hub linked to the
/// existing <b>NatureStage</b> field, with an RO-style <b>field boss</b> roaming the field. Mirrors
/// <see cref="PxltgrFieldBuilder"/>'s content-only pattern (terrain + Ground layer + pathfinding + spawns,
/// no player/camera — those live in the persistent <c>Systems</c> scene) and reuses the <see cref="WorldBuildUtil"/> helpers.
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
    const string FieldScenePath = "Assets/Scenes/NatureStage.unity";
    const string FieldScene = "NatureStage";
    const string TownScene  = "Town";
    const float  TownX = 30f;   // bare-hub plot size (world units); the furnished town uses FantasyTownBuilder
    const float  TownZ = 22f;

    // ── 1) TOWN HUB ────────────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/unwritten/World/Build Town Hub", priority = 220)]
    static void BuildTownHub()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Town Hub", "Exit Play mode first, then re-run.", "OK"); return; }

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Town Hub", "No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers.", "OK"); return; }
        int groundMask = 1 << groundLayer;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        Build25DSpike.BuildLight();   // authoring Sun (disabled at Play; Systems owns the runtime light)

        // Flat Pxltgr grass plot (central road) + one flat floor collider + a Pathfinder3DConfig.
        var ground = PxltgrGround.LayPlot(scene, "Ground_Town", Vector3.zero, TownX, TownZ, groundLayer, roadHalf: 1.6f);
        if (ground == null)
        {
            EditorUtility.DisplayDialog("Town Hub",
                "No 'ground_grass' prefab in Assets/Prefabs/Props.\n\nRun Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first.", "OK");
            EditorSceneManager.CloseScene(scene, true);
            return;
        }

        // The portal to the field sits toward the +X edge; spawns sit a few units back from it (so you don't
        // bounce straight back through on arrival, and don't boot on top of it).
        Vector3 portalPos = WorldBuildUtil.SurfacePoint(new Vector3(TownX * 0.5f - 3f,   0f, 0f), groundMask);
        Vector3 startPos  = WorldBuildUtil.SurfacePoint(Vector3.zero, groundMask);
        Vector3 backPos   = WorldBuildUtil.SurfacePoint(new Vector3(TownX * 0.5f - 6.5f, 0f, 0f), groundMask);

        MakeSpawn(scene, "Start", startPos);
        MakeSpawn(scene, "FromField", backPos);
        MakePortal(scene, "Portal_ToField", portalPos, FieldScene, "FromTown", new Color(0.4f, 0.8f, 1f));

        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var open = SceneManager.GetSceneByPath(TownScenePath);
        if (open.IsValid() && open.isLoaded && open != scene) EditorSceneManager.CloseScene(open, true);

        EditorSceneManager.SaveScene(scene, TownScenePath);
        WorldBuildUtil.RegisterInBuildSettings(TownScenePath);
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
    [MenuItem("Tools/unwritten/World/Link Field → Town (open field map)", priority = 222)]
    static void LinkFieldToTown()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Link Field → Town", "Exit Play mode first, then re-run.", "OK"); return; }

        var scene = SceneManager.GetActiveScene();
        if (scene.name == TownScene)
        { EditorUtility.DisplayDialog("Link Field → Town", "The active scene is Town. Open the FIELD map (NatureStage) first, then re-run.", "OK"); return; }

        var (addedSpawn, addedPortal) = LinkFieldBack(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Link Field → Town — done",
            $"Wired '{scene.name}' back to the Town:\n" +
            $"• SpawnPoint 'FromTown' {(addedSpawn ? "added" : "already present")}\n" +
            $"• Portal_ToTown → Town @ 'FromField' {(addedPortal ? "added" : "already present")}\n\n" +
            "The gold pad warps back to town. Move the pad/spawn where you want.", "OK");
        Debug.Log($"[unwritten] Linked field '{scene.name}' → Town.");
    }

    // ── 2b) RELINK BOTH WAYS + REFRESH WARP VFX ──────────────────────────────────────────────────────
    // Wires the full Town ↔ NatureStage travel loop AND re-skins every existing portal with the current
    // warp marker (the Magic circle VFX). Safe to re-run; preserves hand-finished art in both scenes.
    [MenuItem("Tools/unwritten/World/Relink Town ↔ Field + Refresh Warp VFX", priority = 221)]
    static void RelinkRegion()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Relink Region", "Exit Play mode first, then re-run.", "OK"); return; }

        // ── Town: ensure the portal to the field + refresh its VFX ──
        var town = EditorSceneManager.OpenScene(TownScenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(town);
        int gl = WorldBuildUtil.EnsureGroundLayer();
        int gm = gl >= 0 ? 1 << gl : ~0;
        Physics.SyncTransforms();
        Vector3 portalPos = WorldBuildUtil.SurfacePoint(new Vector3(TownX * 0.5f - 3f, 0f, 0f), gm);
        bool addedTownPortal = EnsurePortal(town, "Portal_ToField", portalPos, FieldScene, "FromTown", new Color(0.4f, 0.8f, 1f));
        int townFx = RefreshWarpFxInScene(town);
        EditorSceneManager.MarkSceneDirty(town);
        EditorSceneManager.SaveScene(town);

        // ── NatureStage: ensure the return portal + refresh its VFX ──
        var field = EditorSceneManager.OpenScene(FieldScenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(field);
        var (addedSpawn, addedFieldPortal) = LinkFieldBack(field);
        int fieldFx = RefreshWarpFxInScene(field);
        EditorSceneManager.MarkSceneDirty(field);
        EditorSceneManager.SaveScene(field);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Relink Region — done",
            "Town ↔ NatureStage are linked and every warp now uses the Magic circle VFX.\n\n" +
            $"Town: portal {(addedTownPortal ? "added" : "already present")}, {townFx} warp(s) refreshed.\n" +
            $"NatureStage: spawn {(addedSpawn ? "added" : "present")}, return portal {(addedFieldPortal ? "added" : "present")}, {fieldFx} warp(s) refreshed.\n\n" +
            "NatureStage is the open scene. Open Town and press Play to test the loop.", "OK");
        Debug.Log($"[unwritten] Relinked region; refreshed {townFx + fieldFx} warp VFX.");
    }

    // Strip any old warp marker children (cylinder "WarpPad" or a prior "WarpFX") off every Portal3D in the
    // scene and re-attach the current marker. Returns how many portals were refreshed.
    static int RefreshWarpFxInScene(Scene scene)
    {
        int n = 0;
        foreach (var p in Object.FindObjectsByType<Portal3D>(FindObjectsSortMode.None))
        {
            if (p.gameObject.scene != scene) continue;
            for (int i = p.transform.childCount - 1; i >= 0; i--)
            {
                var c = p.transform.GetChild(i);
                if (c.name == "WarpPad" || c.name == "WarpFX") Object.DestroyImmediate(c.gameObject);
            }
            // Normalise stale portals to the current standards: unit-scale root, fresh FX, fitted trigger.
            p.transform.localScale = Vector3.one;
            AddWarpFx(p.gameObject);
            if (p.TryGetComponent<BoxCollider>(out var box)) FitTriggerToFx(p.transform, box);
            n++;
        }
        return n;
    }

    [MenuItem("Tools/unwritten/World/Fit Portal Triggers (active scene)", priority = 235)]
    static void FitPortalTriggersMenu()
    {
        var scene = SceneManager.GetActiveScene();
        int n = 0;
        foreach (var p in Object.FindObjectsByType<Portal3D>(FindObjectsSortMode.None))
        {
            if (p.gameObject.scene != scene) continue;
            p.transform.localScale = Vector3.one;                       // undo any stray root scaling
            if (p.TryGetComponent<BoxCollider>(out var box)) FitTriggerToFx(p.transform, box);
            n++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[unwritten] Fitted {n} portal trigger(s) to their warp visuals in '{scene.name}'.");
        EditorUtility.DisplayDialog("Fit Portal Triggers",
            $"Resized {n} portal trigger(s) to hug their warp ring (root scale reset to 1,1,1).\n\n" +
            "Save the scene (Ctrl+S) to keep it.", "OK");
    }

    // Fit a portal's BoxCollider EXACTLY to its warp FX, and drop the FX so it sits on the ground (no float).
    // The ring is a particle effect, so we SIMULATE each system to a steady state and read the real particle
    // world positions/sizes (Renderer.bounds is unreliable for un-simulated particles) plus any mesh bounds.
    // Then we shift the WarpFX child down until the visual's bottom meets the portal origin (which is on the
    // terrain) and size the trigger to the measured bounds 1:1 (portal scale is 1). Falls back to the doorway
    // default only if nothing measurable exists.
    static void FitTriggerToFx(Transform portal, BoxCollider box)
    {
        // Find the direct WarpFX child so we can ground it.
        Transform fx = null;
        for (int i = 0; i < portal.childCount; i++)
            if (portal.GetChild(i).name == "WarpFX") { fx = portal.GetChild(i); break; }

        // STANDARD: every map's walkable ground is the "Ground" layer (flat at Y≈0). Snap the portal PIVOT
        // down onto that real ground first — so a portal that was hand-dragged onto a roof/prop (e.g. Town's
        // stray Y=0.79) drops back to the floor, and "grounded" below means the actual terrain everywhere.
        int gLayer = WorldBuildUtil.EnsureGroundLayer();
        if (gLayer >= 0)
        {
            Physics.SyncTransforms();
            Vector3 snapped = WorldBuildUtil.SurfacePoint(portal.position, 1 << gLayer);
            portal.position = new Vector3(portal.position.x, snapped.y, portal.position.z);
        }

        bool has = false; Bounds wb = default;
        void Add(Bounds b) { if (!has) { wb = b; has = true; } else wb.Encapsulate(b); }

        // Meshes (e.g. a distortion quad) give exact bounds directly.
        foreach (var mr in portal.GetComponentsInChildren<MeshRenderer>())
            if (mr.transform != portal) Add(mr.bounds);

        // Particles: simulate each system to a steady state, then collect every live particle (world pos + size).
        // We measure the RING ONLY — the big core particles — and ignore the thin sparks that shoot outward, so
        // grounding drops the visible ring to the floor instead of the spark cloud, and the box hugs the ring.
        var parts = new List<Vector4>();   // xyz = world pos, w = size
        float maxSize = 0f;
        ParticleSystem.Particle[] buf = null;
        foreach (var ps in portal.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Simulate(3f, false, true, false);                       // steady-state snapshot of this system
            int count = ps.particleCount;
            if (count == 0) { ps.Clear(); continue; }
            if (buf == null || buf.Length < count) buf = new ParticleSystem.Particle[count];
            int got = ps.GetParticles(buf);
            bool worldSpace = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
            for (int i = 0; i < got; i++)
            {
                Vector3 wp = worldSpace ? buf[i].position : ps.transform.TransformPoint(buf[i].position);
                float sz = buf[i].GetCurrentSize(ps);
                parts.Add(new Vector4(wp.x, wp.y, wp.z, sz));
                maxSize = Mathf.Max(maxSize, sz);
            }
            ps.Clear();                                                // leave the system reset, not mid-sim
        }

        float coreThreshold = 0.4f * maxSize;                          // keep the fat ring, drop the thin sparks
        foreach (var v in parts)
        {
            if (v.w < coreThreshold) continue;
            float r = Mathf.Max(0.01f, v.w * 0.5f);
            Add(new Bounds(new Vector3(v.x, v.y, v.z), new Vector3(r * 2f, r * 2f, r * 2f)));
        }

        if (!has || wb.size.x < 0.05f || wb.size.y < 0.05f)
        {
            box.center = TriggerCenter; box.size = TriggerSize;        // truly unmeasurable → safe doorway default
            return;
        }

        // Ground it: slide the FX down so the visual's bottom rests on the portal origin (terrain height).
        if (fx != null)
        {
            float floatGap = wb.min.y - portal.position.y;            // >0 ⇒ levitating above the ground
            fx.position -= new Vector3(0f, floatGap, 0f);
            wb.center   -= new Vector3(0f, floatGap, 0f);
        }

        // Exact 1:1 fit (no padding) — portal scale is 1 so world size maps straight to local box size.
        box.center = portal.InverseTransformPoint(wb.center);
        box.size = wb.size;
    }

    // ── 3) FIELD BOSS ──────────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/unwritten/Mobs & NPCs/Spawn Field Boss (open field map)", priority = 320)]
    static void SpawnFieldBoss()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Spawn Field Boss", "Exit Play mode first, then re-run.", "OK"); return; }

        var scene = SceneManager.GetActiveScene();
        if (scene.name == TownScene)
        { EditorUtility.DisplayDialog("Spawn Field Boss", "The active scene is Town (a safe hub). Open the FIELD map first.", "OK"); return; }

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        int groundMask = groundLayer >= 0 ? 1 << groundLayer : ~0;
        Physics.SyncTransforms();

        // Drop the boss in an open spot away from the default arrival so you walk into it, not onto it.
        Vector3 anchor = Vector3.zero;
        foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            if (sp.id == "Start") { anchor = sp.transform.position; break; }
        Vector3 pos = WorldBuildUtil.SurfacePoint(anchor + new Vector3(6f, 0f, 10f), groundMask);

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

    /// <summary>Wire an open FIELD scene back to the Town: ensure a "FromTown" SpawnPoint + a Portal3D →
    /// Town @ "FromField", placed just west of the field's default arrival. Idempotent. Returns what was
    /// added. Shared by the menu item and the Fantasy Town builder (which calls it on NatureStage so the
    /// whole travel loop is wired in one click). Caller saves the scene.</summary>
    public static (bool addedSpawn, bool addedPortal) LinkFieldBack(Scene scene)
    {
        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        int groundMask = groundLayer >= 0 ? 1 << groundLayer : ~0;
        Physics.SyncTransforms();

        // Anchor near the field's default arrival ("Start" if present, else origin).
        Vector3 anchor = Vector3.zero;
        foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            if (sp.id == "Start") { anchor = sp.transform.position; break; }

        Vector3 spawnPos  = WorldBuildUtil.SurfacePoint(anchor + new Vector3(-4.5f, 0f, 0f), groundMask);
        Vector3 portalPos = WorldBuildUtil.SurfacePoint(anchor + new Vector3(-8f, 0f, 0f), groundMask);

        bool addedSpawn  = EnsureSpawn(scene, "FromTown", spawnPos);
        bool addedPortal = EnsurePortal(scene, "Portal_ToTown", portalPos, TownScene, "FromField", new Color(1f, 0.85f, 0.4f));
        WorldBuildUtil.RegisterInBuildSettings(TownScenePath);   // so the field's portal can load Town by name
        return (addedSpawn, addedPortal);
    }

    // ── HAND-PLACE HELPER: snap dragged-in props onto the terrain ────────────────────────────────────
    [MenuItem("Tools/unwritten/Authoring/Drop Selected to Ground", priority = 411)]
    static void DropSelectedToGround()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        { EditorUtility.DisplayDialog("Drop to Ground", "Select one or more objects (e.g. houses you dragged in) first.", "OK"); return; }

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
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
    public static GameObject MakeSpawn(Scene scene, string id, Vector3 pos)
    {
        var go = new GameObject($"SpawnPoint_{id}");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = pos;
        go.AddComponent<SpawnPoint>().id = id;
        return go;
    }

    // Add a SpawnPoint only if this scene doesn't already have one with that id.
    public static bool EnsureSpawn(Scene scene, string id, Vector3 pos)
    {
        foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            if (sp.id == id && sp.gameObject.scene == scene) return false;
        MakeSpawn(scene, id, pos);
        return true;
    }

    // The visible warp marker: Hovl Studio's UPRIGHT "Portal" rings, colour-coded BY DESTINATION so a
    // doorway's colour tells you where it goes (see WarpKind / PortalPrefabFor below).
    const string PortalsDir   = "Assets/Art/Hovl Studio/Magic effects pack/Prefabs/Portals/";
    const float  WarpFxScale  = 0.5f;   // tune live in-scene; Hovl portals are ~2 units tall at scale 1
    const float  WarpFxHeight = 1.0f;   // lift the ring so it stands ON the ground at ~doorway height

    // The walk-through trigger volume — a doorway, NOT a room. Single source of truth so MakePortal and the
    // Relink-Region refresh keep every portal's trigger the same modest size (the old 3×2.4×3 was a giant cube).
    static readonly Vector3 TriggerSize   = new Vector3(1.5f, 2f, 1.5f);
    static readonly Vector3 TriggerCenter = new Vector3(0f, 1f, 0f);

    // Destination → portal colour. Source of truth is the kind of map the portal LEADS TO, classified from
    // the target scene name, so fresh bakes and re-links agree without passing a colour around.
    enum WarpKind { Town, Field, Dungeon, Special }

    static WarpKind ClassifyDestination(string sceneName)
    {
        var n = (sceneName ?? "").ToLowerInvariant();
        if (n.Contains("town"))    return WarpKind.Town;
        if (n.Contains("dungeon")) return WarpKind.Dungeon;
        if (n.Contains("special")) return WarpKind.Special;
        return WarpKind.Field;   // NatureStage & other overworld maps
    }

    static string PortalPrefabFor(WarpKind kind) => PortalsDir + kind switch
    {
        WarpKind.Town    => "Portal yellow.prefab",
        WarpKind.Field   => "Portal green.prefab",
        WarpKind.Special => "Portal red.prefab",
        WarpKind.Dungeon => "Portal purple.prefab",   // not in the Hovl pack yet → falls back to blue + warns
        _                => "Portal green.prefab",
    };

    static Color PortalTintFor(WarpKind kind) => kind switch
    {
        WarpKind.Town    => new Color(1f,   0.85f, 0.4f),   // yellow/gold
        WarpKind.Field   => new Color(0.4f, 0.9f,  0.4f),   // green
        WarpKind.Special => new Color(0.9f, 0.3f,  0.3f),   // red
        WarpKind.Dungeon => new Color(0.6f, 0.35f, 0.9f),   // purple
        _                => new Color(0.4f, 0.9f,  0.4f),
    };

    public static GameObject MakePortal(Scene scene, string name, Vector3 pos, string targetScene, string spawnId, Color padColor)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one;   // portal root is always unit-scale; the warp FBX/VFX fits at its native size

        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = TriggerCenter;
        box.size = TriggerSize;

        var portal = go.AddComponent<Portal3D>();
        portal.targetScene = targetScene;
        portal.spawnId = spawnId;

        AddWarpFx(go);
        FitTriggerToFx(go.transform, box);   // hug the warp ring exactly + drop it onto the ground
        return go;
    }

    // Attach the visible warp marker so the player can see the doorway in-game: the destination-coloured
    // upright Hovl "Portal" ring if it's present, otherwise a tinted cylinder fallback (hand-finished later).
    // The colour is driven by where the portal LEADS (its Portal3D.targetScene) — so this works the same for
    // fresh bakes and for RefreshWarpFxInScene re-attaching markers onto existing portals.
    static void AddWarpFx(GameObject portal)
    {
        var p3 = portal.GetComponent<Portal3D>();
        var kind = ClassifyDestination(p3 != null ? p3.targetScene : "");
        var tint = PortalTintFor(kind);

        // Chosen colour first; if that prefab is missing (e.g. no purple in the pack yet), fall back to blue.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PortalPrefabFor(kind))
                  ?? AssetDatabase.LoadAssetAtPath<GameObject>(PortalsDir + "Portal blue.prefab");
        if (prefab != null)
        {
            if (kind == WarpKind.Dungeon && prefab.name != "Portal purple")
                Debug.LogWarning("[unwritten] No 'Portal purple' in the Hovl pack yet; dungeon warp using blue. " +
                                 "Drop a purple portal prefab in Prefabs/Portals/ to enable the convention.");
            var fx = (GameObject)PrefabUtility.InstantiatePrefab(prefab, portal.scene);
            fx.name = "WarpFX";
            fx.transform.SetParent(portal.transform, false);
            fx.transform.localPosition = new Vector3(0f, WarpFxHeight, 0f);   // stand the ring up on the ground
            fx.transform.localScale = Vector3.one * WarpFxScale;
            return;
        }

        // Fallback: the old cylinder pad (kept so the bakers still work if the VFX pack is missing).
        Debug.LogWarning($"[unwritten] No Hovl portal prefab found in {PortalsDir}; using cylinder pad fallback.");
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "WarpPad";
        Object.DestroyImmediate(pad.GetComponent<Collider>());   // visual only — the trigger lives on the parent
        pad.transform.SetParent(portal.transform, false);
        pad.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        pad.transform.localScale = new Vector3(1.5f, 0.06f, 1.5f);
        var mr = pad.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
        mr.sharedMaterial = mat;
    }

    // Add a Portal3D only if this scene doesn't already have one pointing at the same target.
    public static bool EnsurePortal(Scene scene, string name, Vector3 pos, string targetScene, string spawnId, Color padColor)
    {
        foreach (var p in Object.FindObjectsByType<Portal3D>(FindObjectsSortMode.None))
            if (p.targetScene == targetScene && p.gameObject.scene == scene) return false;
        MakePortal(scene, name, pos, targetScene, spawnId, padColor);
        return true;
    }
}
