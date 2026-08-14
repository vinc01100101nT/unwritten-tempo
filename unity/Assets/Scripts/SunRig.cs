using UnityEngine;

/// <summary>
/// Compute-once-per-frame source for the main directional light's HORIZONTAL direction — the sun/moon
/// equivalent of <see cref="CameraRig"/>. Many billboard shadow proxies (<see cref="BillboardShadow"/>)
/// each need "which way is the light travelling on the ground plane?" every frame to turn broadside to it;
/// this caches the result keyed on <see cref="Time.frameCount"/> so only the first reader pays for it.
///
/// <para>Finds the sun via <see cref="RenderSettings.sun"/> first (the lighting window's "Sun Source"),
/// falling back to the brightest directional <see cref="Light"/> in the scene. Re-fetched when the cached
/// light goes null (e.g. after a scene/day-night swap).</para>
/// </summary>
public static class SunRig
{
    static int frame = -1;
    static Light sun;
    static Vector3 dirXZ = Vector3.forward;
    static Quaternion shadowRot = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    static bool valid;

    /// <summary>True once a directional light has been found (so a proxy knows it can cast a meaningful shadow).</summary>
    public static bool HasSun { get { Refresh(); return valid; } }
    /// <summary>The light's travel direction projected on the ground plane (normalized). World-forward if unknown.</summary>
    public static Vector3 DirXZ { get { Refresh(); return dirXZ; } }
    /// <summary>Rotation that turns a billboard quad to FULLY FACE the light in 3D (quad normal along the light
    /// direction), so the light sees the full sprite silhouette at ANY elevation: the quad stands up for a low
    /// sun (long cast streak) and lies flat for a steep/overhead sun (silhouette projects straight down under the
    /// feet). One caster that morphs — so the cast shadow is always full and never separates into pieces.</summary>
    public static Quaternion ShadowFacing { get { Refresh(); return shadowRot; } }

    static void Refresh()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;

        if (sun == null) sun = FindSun();          // Unity-null re-fetches after a scene/light swap
        if (sun == null) { valid = false; return; }
        valid = true;

        Vector3 ld = sun.transform.forward;
        if (ld.sqrMagnitude < 1e-5f) return;        // degenerate light → keep the last facing
        ld.Normalize();

        Vector3 flat = ld; flat.y = 0f;
        if (flat.sqrMagnitude > 1e-5f) dirXZ = flat.normalized;

        // Full-3D face-the-light. Near-vertical light makes world-up parallel to the look dir (LookRotation
        // degenerate), so fall back to a horizontal reference for the roll — the silhouette doesn't care.
        Vector3 refUp = Mathf.Abs(ld.y) > 0.99f ? Vector3.forward : Vector3.up;
        shadowRot = Quaternion.LookRotation(ld, refUp);
    }

    static Light FindSun()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun;
        var all = Object.FindObjectsByType<Light>();   // only runs while the cache is null
        Light best = null;
        for (int i = 0; i < all.Length; i++)
        {
            var l = all[i];
            if (l.type == LightType.Directional && (best == null || l.intensity > best.intensity)) best = l;
        }
        return best;
    }
}
