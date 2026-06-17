using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Populates the open nature stage with billboard mobs (the wander/chase enemies built by
/// <see cref="Build25DSpike.BuildMonster"/>) so the forest is an actual encounter, not a walk. Mobs are
/// raycast-snapped onto the <c>Ground</c>-layer terrain, kept clear of the player spawn, and parented under
/// a re-runnable <c>Nature_Mobs</c> container. The combat loop is already wired by the rig: click a mob to
/// attack (A = attack-move, S = stop), mobs chase + bite back via <c>MonsterAI3D</c> / <c>Character</c>.
/// </summary>
public static class NatureMobs
{
    const string Container   = "Nature_Mobs";
    const float  MobDensity  = 0.012f;   // mobs per square world-unit (×80×80 ≈ 12)
    const float  SpawnClearR = 8f;       // keep mobs this far from the player spawn (no instant aggro)
    const float  MobSpacing  = 3f;       // min gap between mobs
    const float  MaxSlope    = 28f;      // don't spawn on cliffs (degrees)
    const float  Inset       = 2f;       // pull in from the terrain edge

    [MenuItem("Tools/unwritten/Populate Mobs (Nature)", priority = 8)]
    static void Populate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Populate Mobs", "Exit Play mode first, then re-run.", "OK"); return; }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Populate Mobs", "No 'Ground' layer. Build a Nature Stage first.", "OK"); return; }
        int groundMask = 1 << groundLayer;

        if (!TryGetGroundBounds(groundLayer, out var bounds))
        { EditorUtility.DisplayDialog("Populate Mobs",
            "No ground on the 'Ground' layer in the open scene.\n\nRun New Nature Stage first.", "OK"); return; }

        var tex = Build25DSpike.MonsterSheet();
        if (tex == null)
        { EditorUtility.DisplayDialog("Populate Mobs",
            "No Ninja Adventure monster sprite sheet found under Assets/Art/NinjaAdventure/Actor/Monster.", "OK"); return; }

        Physics.SyncTransforms();   // make terrain colliders queryable for the drop rays

        var prev = GameObject.Find(Container);
        if (prev != null) Object.DestroyImmediate(prev);
        var root = new GameObject(Container);
        Undo.RegisterCreatedObjectUndo(root, "Populate Mobs");

        // Player spawn = the billboard player if present, else terrain centre.
        var player = Object.FindFirstObjectByType<Player3DController>();
        Vector3 spawn = player != null ? player.transform.position : bounds.center;

        int target = Mathf.Max(1, Mathf.RoundToInt(bounds.size.x * bounds.size.z * MobDensity));
        Random.InitState((int)(System.DateTime.Now.Ticks & 0x7fffffff));

        int placed = 0, attempts = 0, maxAttempts = target * 16;
        var spots = new System.Collections.Generic.List<Vector3>();
        while (placed < target && attempts < maxAttempts)
        {
            attempts++;
            float x = Random.Range(bounds.min.x + Inset, bounds.max.x - Inset);
            float z = Random.Range(bounds.min.z + Inset, bounds.max.z - Inset);
            if (!Physics.Raycast(new Vector3(x, bounds.max.y + 50f, z), Vector3.down, out var hit, 200f,
                                 groundMask, QueryTriggerInteraction.Ignore))
                continue;
            if (Vector3.Angle(hit.normal, Vector3.up) > MaxSlope) continue;
            if (new Vector2(hit.point.x - spawn.x, hit.point.z - spawn.z).magnitude < SpawnClearR) continue;
            if (spots.Any(p => (p - hit.point).sqrMagnitude < MobSpacing * MobSpacing)) continue;

            var mob = Build25DSpike.BuildMonster(tex, hit.point);   // foot.y = ground height
            mob.transform.SetParent(root.transform, true);
            spots.Add(hit.point);
            placed++;
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Debug.Log($"[unwritten] Populated {placed} mobs onto the nature terrain.");
        EditorUtility.DisplayDialog("Populate Mobs — done",
            $"Spawned {placed} mobs under '{Container}'.\n\n" +
            "Press Play: LEFT-click a mob to attack (A = attack-move, S = stop). They wander, then " +
            "chase + bite when you get close.\n\n" +
            "Re-run for a new layout, or tweak MobDensity / SpawnClearR at the top of NatureMobs.cs.", "OK");
    }

    [MenuItem("Tools/unwritten/Clear Mobs (Nature)", priority = 9)]
    static void Clear()
    {
        var prev = GameObject.Find(Container);
        if (prev == null) { Debug.Log("[unwritten] No Nature_Mobs to clear."); return; }
        Undo.DestroyObjectImmediate(prev);
        EditorSceneManager.MarkSceneDirty(prev.scene);
    }

    static bool TryGetGroundBounds(int groundLayer, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.gameObject.layer != groundLayer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }
}
