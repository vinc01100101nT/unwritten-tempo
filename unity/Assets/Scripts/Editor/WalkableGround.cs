using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared <b>walkable-floor</b> helper for generated maps. A FLAT map's ground is the single blended grass→dirt
/// mesh built by <see cref="GroundBlendBuilder"/>; this class supplies the one piece both the mesh and the
/// routing need: a flat floor <see cref="BoxCollider"/> (top at y=0) on the Ground layer that the player snaps
/// to and the pathfinder rays against, plus a scene-scoped <see cref="Pathfinder3DConfig"/>. An ELEVATED map
/// instead carries its collision on the shaped mesh itself (a <see cref="MeshCollider"/> the builder adds), so it
/// only needs the shared <see cref="EnsurePathfinderConfig"/> — the config-adding logic lives here so both paths
/// stay identical. (The old tiled grass-plot builder was retired when the world moved to one blended ground style
/// — see <see cref="RecipeGenerator"/>.)
/// </summary>
public static class WalkableGround
{
    /// <summary>
    /// Add the single flat floor <see cref="BoxCollider"/> (top at y=0) under <paramref name="root"/> on
    /// <paramref name="groundLayer"/>, plus a scene-scoped <see cref="Pathfinder3DConfig"/> if the scene lacks
    /// one. <paramref name="sizeX"/>/<paramref name="sizeZ"/> is the plot footprint. For FLAT ground only —
    /// elevated ground skips the flat box (its shaped MeshCollider is the floor) and calls
    /// <see cref="EnsurePathfinderConfig"/> directly.
    /// </summary>
    public static void AddWalkableFloor(Scene scene, GameObject root, Vector3 center,
                                        float sizeX, float sizeZ, int groundLayer)
    {
        var floor = new GameObject("Ground_Floor") { layer = groundLayer };
        floor.transform.SetParent(root.transform, false);
        var fbox = floor.AddComponent<BoxCollider>();
        fbox.center = new Vector3(center.x, -0.5f, center.z);    // top at y=0 (ground surface)
        fbox.size   = new Vector3(sizeX + 2f, 1f, sizeZ + 2f);

        EnsurePathfinderConfig(scene, groundLayer);
        Physics.SyncTransforms();   // so surface raycasts hit the floor we just made
    }

    /// <summary>
    /// Add a scene-scoped <see cref="Pathfinder3DConfig"/> (height-aware, step-gated by
    /// <see cref="GroundElevation.MaxClimbStep"/>) if THIS scene lacks one. Scene-scoped rather than
    /// <c>FindObjectOfType</c> because maps load additively — another loaded scene's config must not make us skip
    /// adding one here. Shared by the flat floor and the elevated MeshCollider path so both agree on the routing.
    /// </summary>
    public static void EnsurePathfinderConfig(Scene scene, int groundLayer)
    {
        foreach (var r in scene.GetRootGameObjects())
            if (r.GetComponentInChildren<Pathfinder3DConfig>(true) != null) return;

        var cfgGO = new GameObject("Pathfinder3DConfig");
        SceneManager.MoveGameObjectToScene(cfgGO, scene);
        var cfg = cfgGO.AddComponent<Pathfinder3DConfig>();
        cfg.heightAware = true; cfg.groundMask = 1 << groundLayer; cfg.maxStep = GroundElevation.MaxClimbStep;
    }
}
