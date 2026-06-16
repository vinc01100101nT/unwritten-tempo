using UnityEngine;

/// <summary>
/// Camera-relative depth sorter for the 2.5D (tilted, orbiting) camera. Replaces
/// <see cref="YDepthSorter"/> when the view is not straight top-down.
///
/// Each frame, projects this object's base position onto the camera's horizontal forward
/// direction and maps it to a <c>sortingOrder</c> within the entity band. Closer to the
/// camera = HIGHER order = drawn in front. This keeps player / mobs / props occluding
/// correctly regardless of camera yaw or pitch.
///
/// The camera's <see cref="Camera.transparencySortAxis"/> (the tie-breaker for equal orders)
/// is kept in sync by <see cref="CameraFollow3D"/> — a single per-frame write on the camera —
/// so this component never touches global/camera state, only its own sprites.
///
/// Two modes (auto-detected, mirroring <see cref="YDepthSorter"/>):
///   • SELF  — SpriteRenderer on this object (player, mob): sort it by its own base.
///   • GROUP — no SpriteRenderer here, but child sprites exist (multi-tile prop): sort the
///     whole group as ONE unit by the prop's base (its transform), so a tree never splits.
///
/// Only XZ matters for horizontal depth, so the sprite's foot HEIGHT is irrelevant — the
/// base position (this transform) is the sort point in both modes.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class CameraDepthSorter : MonoBehaviour
{
    [Tooltip("Extra world-units nudge on the sort line. Negative = treated as closer to the " +
             "camera, so it draws in front a touch sooner. Usually 0.")]
    public float sortOffset = 0f;

    SpriteRenderer self;
    SpriteRenderer[] group;
    Camera cam;

    void Awake() => Cache();
    void OnEnable() => Cache();

    void Cache()
    {
        self = GetComponent<SpriteRenderer>();
        group = self == null ? GetComponentsInChildren<SpriteRenderer>(true) : null;
        cam = Camera.main;
    }

    void LateUpdate()
    {
        if (!Application.isPlaying && self == null && (group == null || group.Length == 0)) Cache();
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // Camera's horizontal forward = the depth axis. (The < threshold guard only trips at a
        // near-perfectly top-down pitch, which the CameraFollow3D height/distance clamps avoid.)
        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        forward.Normalize();

        float depth = Vector3.Dot(transform.position, forward) + sortOffset;
        int order = DepthSortRuntime.OrderForDepth(depth);

        if (self != null)
        {
            if (self.sortingOrder != order) self.sortingOrder = order;
            return;
        }

        if (group == null) return;
        foreach (var r in group)
            if (r != null && r.sortingOrder != order) r.sortingOrder = order;
    }
}
