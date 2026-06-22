using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Permanent guard so a portal can NEVER be saved floating above the ground again. Before any scene is
/// written to disk, every <see cref="Portal3D"/> in it is snapped straight down onto the "Ground" layer
/// (the universal Y≈0 floor — see the World ground standard). This kills the recurring "I dragged a portal
/// in the Scene view and its pivot caught a roof/prop collider, so it ended up at Y=0.79" problem with ZERO
/// manual steps: you just press Ctrl+S like always, and any stray portal is quietly put back on the floor.
///
/// It only moves a portal that is actually off the ground (tolerance 1mm), and only along Y, so a correctly
/// placed portal is never touched. Runs per saving scene, ignores prop/trigger colliders (Ground mask only).
/// </summary>
[InitializeOnLoad]
static class PortalGroundGuard
{
    static PortalGroundGuard()
    {
        EditorSceneManager.sceneSaving -= OnSceneSaving;   // avoid double-subscribe on domain reload
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    static void OnSceneSaving(Scene scene, string path)
    {
        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        if (groundLayer < 0) return;                        // no Ground layer → nothing to snap to
        int mask = 1 << groundLayer;
        Physics.SyncTransforms();

        int snapped = 0;
        foreach (var portal in Object.FindObjectsByType<Portal3D>(FindObjectsSortMode.None))
        {
            if (portal.gameObject.scene != scene) continue;
            var t = portal.transform;
            var from = new Vector3(t.position.x, t.position.y + 60f, t.position.z);
            if (!Physics.Raycast(from, Vector3.down, out var hit, 200f, mask, QueryTriggerInteraction.Ignore))
                continue;                                   // no ground beneath it → leave it alone
            if (Mathf.Abs(t.position.y - hit.point.y) <= 0.001f) continue;   // already grounded
            t.position = new Vector3(t.position.x, hit.point.y, t.position.z);
            snapped++;
        }

        if (snapped > 0)
            Debug.Log($"[unwritten] Ground guard: dropped {snapped} floating portal(s) onto the ground in " +
                      $"'{scene.name}' before saving. (Snap is automatic — no action needed.)");
    }
}
