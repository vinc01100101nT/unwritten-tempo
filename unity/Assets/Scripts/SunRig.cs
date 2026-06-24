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
    /// <summary>Upright rotation that turns a vertical billboard quad BROADSIDE to the light (its face along the
    /// light direction), so the light always sees the full sprite silhouette — the whole point of the proxy.</summary>
    public static Quaternion ShadowFacing { get { Refresh(); return shadowRot; } }

    static void Refresh()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;

        if (sun == null) sun = FindSun();          // Unity-null re-fetches after a scene/light swap
        if (sun == null) { valid = false; return; }

        Vector3 d = sun.transform.forward; d.y = 0f;
        valid = true;
        if (d.sqrMagnitude < 1e-5f) return;         // near-vertical (noon) → keep the last horizontal facing
        dirXZ = d.normalized;
        shadowRot = Quaternion.LookRotation(dirXZ, Vector3.up);
    }

    static Light FindSun()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun;
        var all = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);   // only runs while the cache is null
        Light best = null;
        for (int i = 0; i < all.Length; i++)
        {
            var l = all[i];
            if (l.type == LightType.Directional && (best == null || l.intensity > best.intensity)) best = l;
        }
        return best;
    }
}
