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

    // This map's connected-area labels (see Pathfinder3D.LabelAreas), held so they go when the map does.
    object areaLabels;

    void Awake() => Apply();
    void OnEnable() => Apply();

    // In Start, not Awake: a server sets each map down in its own place after the map has loaded and before its
    // first frame, so only from here on are its positions final.
    void Start()
    {
        if (!Application.isPlaying || groundMask.value == 0) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool any = false;
        var bounds = new Bounds();
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || (groundMask.value & (1 << c.gameObject.layer)) == 0) continue;
                if (any) bounds.Encapsulate(c.bounds); else { bounds = c.bounds; any = true; }
            }
        if (!any) return;
        areaLabels = Pathfinder3D.LabelAreas(bounds);
        GameLog.Info(LogCat.Unwritten, string.Concat("map '", gameObject.scene.name, "' route areas labelled in ",
                                                     sw.ElapsedMilliseconds.ToString(), " ms"));
    }

    void OnDestroy() => Pathfinder3D.ForgetAreas(areaLabels);

    void Apply()
    {
        Pathfinder3D.HeightAware = heightAware;
        if (!heightAware) return;

        if (groundMask.value == 0)
        {
            GameLog.Warn(LogCat.Unwritten, "Pathfinder3DConfig: groundMask is empty — every cell will read " +
                             "as void and nothing is walkable. Assign the Ground layer.");
            return;
        }
        Pathfinder3D.GroundMask = groundMask;
        Pathfinder3D.ObstacleMask = ~groundMask;   // obstacles = everything that isn't ground (units excepted by the pathfinder)
    }
}
