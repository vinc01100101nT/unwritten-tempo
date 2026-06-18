using UnityEngine;

/// <summary>
/// One-stop, compute-once-per-frame source for the main camera's horizontal basis. The 2.5D world
/// has MANY components that each need "which way is the camera facing on the ground plane?" every
/// frame — billboards, the camera-relative depth sorter, and the facing solver. Left alone, each of
/// N mobs recomputed the same normalize/cross math (and called <see cref="Camera.main"/>, a tagged
/// lookup) in its own LateUpdate — O(N) identical work per frame.
///
/// This caches the result keyed on <see cref="Time.frameCount"/>, so the first reader each frame pays
/// for it and everyone else gets the cached value — O(1) per frame regardless of mob count. Falls back
/// to world axes when there's no camera yet (e.g. before the Systems scene boots), matching the old
/// per-component fallbacks. Works in edit mode too (frameCount ticks), so [ExecuteAlways] sorters still
/// preview correctly.
/// </summary>
public static class CameraRig
{
    static int frame = -1;
    static Vector3 fwd = Vector3.forward;
    static Vector3 right = Vector3.right;
    static Camera cam;

    /// <summary>Camera horizontal forward (XZ, normalized). World-forward when no camera.</summary>
    public static Vector3 ForwardXZ { get { Refresh(); return fwd; } }
    /// <summary>Camera horizontal right (XZ, normalized). World-right when no camera.</summary>
    public static Vector3 RightXZ { get { Refresh(); return right; } }
    /// <summary>The cached main camera (re-fetched when null, e.g. after a scene swap), or null.</summary>
    public static Camera Cam { get { Refresh(); return cam; } }

    static void Refresh()
    {
        if (frame == Time.frameCount) return;   // already computed for this frame
        frame = Time.frameCount;

        if (cam == null) cam = Camera.main;      // Unity-null check re-fetches after a scene reload
        if (cam == null) { fwd = Vector3.forward; right = Vector3.right; return; }

        Vector3 f = cam.transform.forward; f.y = 0f;
        fwd = f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;

        Vector3 r = cam.transform.right; r.y = 0f;
        right = r.sqrMagnitude < 1e-6f ? Vector3.right : r.normalized;
    }
}
