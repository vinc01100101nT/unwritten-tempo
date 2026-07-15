using UnityEngine;

/// <summary>
/// Switches the static <see cref="Pathfinder3D"/> into height-aware (multi-level terrain) mode for the
/// scene this lives in. Because the pathfinder is a static service, this small component carries the
/// per-scene settings and pushes them in on load — drop one in any map that has elevation.
///
/// <see cref="groundMask"/> must name the layer(s) used by walkable ground (terrain tiers + ramps).
/// Obstacles are then "everything except ground" (props/walls), and units pass through each other as
/// before (the pathfinder already ignores CharacterControllers).
/// </summary>
[DisallowMultipleComponent]
public class Pathfinder3DConfig : MonoBehaviour
{
    [Tooltip("Turn on multi-level terrain pathing (per-cell ground height + step-gating).")]
    public bool heightAware = true;

    [Tooltip("Layer(s) that count as walkable GROUND (terrain tiers + ramps). REQUIRED when height-aware.")]
    public LayerMask groundMask;

    [Tooltip("Max walkable height change between adjacent cells (stair/ramp rise). Bigger = cliff = blocked.")]
    public float maxStep = GroundElevation.MaxClimbStep;

    void Awake() => Apply();
    void OnEnable() => Apply();

    void Apply()
    {
        Pathfinder3D.HeightAware = heightAware;
        Pathfinder3D.MaxStep = maxStep;
        if (!heightAware) return;

        if (groundMask.value == 0)
        {
            Debug.LogWarning("[unwritten] Pathfinder3DConfig: groundMask is empty — every cell will read " +
                             "as void and nothing is walkable. Assign the Ground layer.");
            return;
        }
        Pathfinder3D.GroundMask = groundMask;
        Pathfinder3D.ObstacleMask = ~groundMask;   // obstacles = everything that isn't ground (units excepted by the pathfinder)
    }
}
