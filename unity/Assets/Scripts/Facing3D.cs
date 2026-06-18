using UnityEngine;

/// <summary>
/// Maps a world-space move/aim direction to a 4-direction billboard facing that's RELATIVE to the
/// main camera — so a character's frames track the camera as it orbits (moving "toward the camera"
/// = down frame, "away" = up, screen-left/right = left/right), even while idle. Returns a (x, z)
/// Vector2 matching the animators' ClipFor: y&gt;0 = up (away), y&lt;0 = down (toward), x = left/right.
/// </summary>
public static class Facing3D
{
    public static Vector2 FromWorldDir(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return Vector2.down;

        // Camera basis is computed ONCE per frame by CameraRig and shared by every caller — this used
        // to call Camera.main + re-normalize for each moving unit, every frame.
        Vector3 fwd = CameraRig.ForwardXZ;     // world-forward when there's no camera
        Vector3 right = CameraRig.RightXZ;

        float f = Vector3.Dot(dir, fwd);     // + = away from the camera (up the screen)
        float r = Vector3.Dot(dir, right);   // + = screen-right
        return Mathf.Abs(f) >= Mathf.Abs(r) ? new Vector2(0f, Mathf.Sign(f))
                                            : new Vector2(Mathf.Sign(r), 0f);
    }
}
