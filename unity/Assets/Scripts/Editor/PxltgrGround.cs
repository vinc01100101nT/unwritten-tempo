using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared <b>walkable-floor</b> helper for generated maps. Every map's ground is the single blended grass→dirt
/// mesh built by <see cref="PxltgrGroundBlend"/>; this class supplies the one piece both the mesh and the
/// routing need: a flat floor <see cref="BoxCollider"/> (top at y=0) on the Ground layer that the player snaps
/// to and the pathfinder rays against, plus a scene-scoped <see cref="Pathfinder3DConfig"/>. Kept separate so
/// the blended builder stays focused on the mesh. (The old tiled grass-plot builder was retired when the world
/// moved to one blended ground style — see <see cref="RecipeGenerator"/>.)
/// </summary>
public static class PxltgrGround
{
    /// <summary>
    /// Add the single flat floor <see cref="BoxCollider"/> (top at y=0) under <paramref name="root"/> on
    /// <paramref name="groundLayer"/>, plus a scene-scoped <see cref="Pathfinder3DConfig"/> if the scene lacks
    /// one. <paramref name="sizeX"/>/<paramref name="sizeZ"/> is the plot footprint.
    /// </summary>
    public static void AddWalkableFloor(Scene scene, GameObject root, Vector3 center,
                                        float sizeX, float sizeZ, int groundLayer)
    {
        var floor = new GameObject("Ground_Floor") { layer = groundLayer };
        floor.transform.SetParent(root.transform, false);
        var fbox = floor.AddComponent<BoxCollider>();
        fbox.center = new Vector3(center.x, -0.5f, center.z);    // top at y=0 (ground surface)
        fbox.size   = new Vector3(sizeX + 2f, 1f, sizeZ + 2f);

        // Routing: Ground layer = walkable, everything else = wall. Add only if THIS scene lacks a config
        // (scene-scoped, not FindObjectOfType — maps are built additively, so another loaded scene's config
        // must not make us skip adding one here).
        bool hasConfig = false;
        foreach (var r in scene.GetRootGameObjects())
            if (r.GetComponentInChildren<Pathfinder3DConfig>(true) != null) { hasConfig = true; break; }
        if (!hasConfig)
        {
            var cfgGO = new GameObject("Pathfinder3DConfig");
            SceneManager.MoveGameObjectToScene(cfgGO, scene);
            var cfg = cfgGO.AddComponent<Pathfinder3DConfig>();
            cfg.heightAware = true; cfg.groundMask = 1 << groundLayer; cfg.maxStep = 0.6f;
        }

        Physics.SyncTransforms();   // so surface raycasts hit the floor we just made
    }
}
