using UnityEngine;

/// <summary>
/// 8-direction cousin of <see cref="Facing3D"/>: maps a world-space move/aim direction to one of 8
/// compass octants that are RELATIVE to the main camera, so a character's frames track the camera as it
/// orbits. Used by the Sscary billboard player (see <see cref="Player3DController"/>'s Scary path), whose
/// art is drawn at 6 visual angles (front, back, and the four diagonals — the left diagonals are exact
/// mirrors of the right ones). The controller resolves these 8 octants onto the 4 drawn clips
/// (down / up / right-down / right-up) plus a horizontal flip for the western half.
///
/// Octant order (the returned int), camera-relative:
///   0 = N  (up / away)      1 = NE     2 = E  (screen-right)   3 = SE
///   4 = S  (down / toward)  5 = SW     6 = W  (screen-left)    7 = NW
/// Defaults to S (toward the camera) for a zero direction, matching Facing3D's "face the camera when idle".
/// </summary>
public static class Facing8
{
    /// <summary>The camera-relative octant (0..7, see the class doc for the order) for a world direction.</summary>
    public static int OctantFromWorldDir(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return 4;   // S — face the camera when standing still

        // Same shared, compute-once-per-frame camera basis every billboard/sorter reads.
        float f = Vector3.Dot(dir, CameraRig.ForwardXZ);   // + = away from camera (up the screen → N)
        float r = Vector3.Dot(dir, CameraRig.RightXZ);     // + = screen-right (→ E)

        // angle 0 = N, +90 = E, ±180 = S, -90 = W. Round to the nearest 45° octant, wrap to 0..7.
        float deg = Mathf.Atan2(r, f) * Mathf.Rad2Deg;
        int idx = Mathf.RoundToInt(deg / 45f);
        return ((idx % 8) + 8) % 8;
    }
}
