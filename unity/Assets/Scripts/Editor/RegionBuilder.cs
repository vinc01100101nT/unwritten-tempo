using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared world-authoring helpers used across the toolchain — NOT a map generator (that's the single
/// <see cref="RecipeGenerator"/> now). This holds the reusable building blocks the recipe generator and the
/// <see cref="WorldCreator"/> (Build World) depend on:
/// <list type="bullet">
/// <item><b>Spawns &amp; portals:</b> <see cref="MakeSpawn"/>/<see cref="EnsureSpawn"/>,
/// <see cref="MakePortal"/>/<see cref="EnsurePortal"/> — a <see cref="Portal3D"/> + arrival <see cref="SpawnPoint"/>,
/// with the warp ring colour auto-chosen from the destination map (town=yellow, field=green …).</item>
/// <item><b>Authoring:</b> Fit Portal Triggers, Drop Selected to Ground.</item>
/// </list>
/// Travel contract (ids matched by <see cref="PersistentPlayer3D"/> on arrival): a portal stores the target
/// scene name + the id of the <see cref="SpawnPoint"/> to arrive at; <see cref="WorldCreator"/> keys each warp's
/// paired arrival spawn id by source map (the w{id}_from_&lt;map&gt; scheme, id = the door's permanent id in the world layout).
/// </summary>
public static class RegionBuilder
{

    [MenuItem("Tools/unwritten/World/Fit Portal Triggers (active scene)", priority = 235)]
    static void FitPortalTriggersMenu()
    {
        var scene = SceneManager.GetActiveScene();
        int n = 0;
        foreach (var p in Object.FindObjectsByType<Portal3D>())
        {
            if (p.gameObject.scene != scene) continue;
            p.transform.localScale = Vector3.one;                       // undo any stray root scaling
            if (p.TryGetComponent<BoxCollider>(out var box)) FitTriggerToFx(p.transform, box);
            n++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[unwritten] Fitted {n} portal trigger(s) to the warp circle's footprint in '{scene.name}'.");
        EditorUtility.DisplayDialog("Fit Portal Triggers",
            $"Stood {n} portal(s) on the ground and gave each the warp circle's footprint as its trigger (root scale reset to 1,1,1).\n\n" +
            "Save the scene (Ctrl+S) to keep it.", "OK");
    }

    // Stand a door on the real ground and give it the circle's footprint as its trigger: the circle's width, and only
    // a sliver tall, so it is reached by feet standing on the circle and never by a jump over it (Portal3D).
    static void FitTriggerToFx(Transform portal, BoxCollider box)
    {
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
        box.center = TriggerCenter;
        box.size = TriggerSize;
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
                go.transform.position = new Vector3(p.x, hit.point.y, p.z);   // props pivot at their base
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
        foreach (var sp in Object.FindObjectsByType<SpawnPoint>())
            if (sp.id == id && sp.gameObject.scene == scene) return false;
        MakeSpawn(scene, id, pos);
        return true;
    }

    // The visible warp marker: the magic circle lying FLAT on the ground, colour-coded BY DESTINATION so a doorway's
    // colour tells you where it goes (see PortalPrefabFor below).
    const string PortalsDir = "Assets/1_Authored/Prefabs/VFX/Portals/";
    // TWICE A CHARACTER'S WIDTH ACROSS. The default body measures 0.34 m across at the shared body height
    // (BaseCharacter, measured 2026-10-21), and the circle's picture is 3 m across at scale 1.
    const float WarpCircleDiameter = 0.68f;
    const float WarpCirclePictureDiameter = 3f;

    // The walk-through trigger: the circle's footprint, a sliver tall — feet standing on the circle reach it, a jump
    // over it does not (Portal3D also asks that the walker is on the ground). One shape for every door.
    static readonly Vector3 TriggerSize   = new Vector3(WarpCircleDiameter, 0.2f, WarpCircleDiameter);
    static readonly Vector3 TriggerCenter = new Vector3(0f, 0.1f, 0f);

    // Destination → ring. The kind of map a door leads to comes from that map's TYPE in the world layout, never its
    // name (MapKinds — the same table the zone map and the arrival flash colour by).
    static string PortalPrefabFor(MapKind kind) => PortalsDir + kind switch
    {
        MapKind.Town    => "Warp circle yellow.prefab",
        MapKind.Field   => "Warp circle green.prefab",
        MapKind.Special => "Warp circle red.prefab",
        MapKind.Dungeon => "Warp circle purple.prefab",
        _               => "Warp circle green.prefab",
    };

    public static GameObject MakePortal(Scene scene, string name, Vector3 pos, string targetScene, string spawnId, string destinationType)
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

        DressPortal(go, destinationType);
        return go;
    }

    /// <summary>THE ONE LOOK OF A DOOR: the magic circle lying flat on the ground, coloured by the type of map it leads
    /// to. Takes off whatever the door wore before (an older disc or standing ring), lays the circle on, stands the door
    /// on the ground and gives it the circle's footprint as its trigger. Used when a door is made and to re-dress the doors
    /// already in a scene, so both always end up identical.
    ///
    /// <para>There is deliberately no flat stand-in when the ring cannot be found: that fallback is what quietly
    /// gave almost every door in the world a flat disc for months, after the ring's folder moved. A missing ring
    /// is now an error, and the door is left as it was.</para></summary>
    public static bool DressPortal(GameObject portal, string destinationType)
    {
        var kind = MapKinds.Of(destinationType);
        var path = PortalPrefabFor(kind);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"[unwritten] No warp ring at {path}; door '{portal.name}' left as it was.");
            return false;
        }

        for (int i = portal.transform.childCount - 1; i >= 0; i--)
        {
            var child = portal.transform.GetChild(i).gameObject;
            if (child.name == "WarpPad" || child.name == "WarpFX") Object.DestroyImmediate(child);
        }

        var fx = (GameObject)PrefabUtility.InstantiatePrefab(prefab, portal.scene);
        fx.name = "WarpFX";
        fx.transform.SetParent(portal.transform, false);
        fx.transform.localPosition = Vector3.zero;
        fx.transform.localRotation = Quaternion.identity;
        fx.transform.localScale = Vector3.one * (WarpCircleDiameter / WarpCirclePictureDiameter);

        var box = portal.GetComponent<BoxCollider>();
        if (box != null) FitTriggerToFx(portal.transform, box);
        return true;
    }

    // Add a Portal3D only if this scene doesn't already have one pointing at the same target.
    public static bool EnsurePortal(Scene scene, string name, Vector3 pos, string targetScene, string spawnId, string destinationType)
    {
        foreach (var p in Object.FindObjectsByType<Portal3D>())
            if (p.targetScene == targetScene && p.gameObject.scene == scene) return false;
        MakePortal(scene, name, pos, targetScene, spawnId, destinationType);
        return true;
    }
}
