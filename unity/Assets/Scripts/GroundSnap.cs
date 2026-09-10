using UnityEngine;

/// <summary>
/// The one runtime way to ask "where is the GROUND directly under this point?" — a downward raycast against the
/// Ground layer ONLY (never props / snow caps / triggers), matching the project's world-ground standard so every
/// unit seats its feet on the same surface. It's the per-frame twin of the build-time
/// <c>WorldBuildUtil.SurfacePoint</c>: the live-3D player (<see cref="Character3DVisual"/>) and townsfolk
/// (<see cref="TownNPC3DVisual"/>) both ground themselves through it, so the foot-on-floor truth lives in ONE
/// place instead of drifting between hand-copied raycasts.
///
/// <para>PERFORMANCE (PERFORMANCE.md): a plain stack-only <see cref="Physics.Raycast"/> — no allocation — safe to
/// call once per moving unit per frame. Callers gate it to MOVING units near the camera (idle Y is already
/// settled), so a still/distant crowd costs nothing.</para>
/// </summary>
public static class GroundSnap
{
    // The per-frame foot-seat convention shared by the player avatar and townsfolk: start the ray a little ABOVE
    // the query point (so we catch ground we're standing slightly inside) and cast this far in total.
    public const float DefaultRayUp = 2f;
    public const float DefaultMaxDistance = 8f;

    /// <summary>THE Ground layer, as a mask, worked out once. Every caller here used to look the layer up itself,
    /// which is the same answer written out in several places — and a place that spells the layer's name wrongly
    /// fails by quietly finding no ground at all, rather than by complaining.
    ///
    /// <para>0 when this project has no Ground layer, which every query below already treats as "no ground here"
    /// and answers false for.</para></summary>
    public static int GroundMask
    {
        get
        {
            if (groundMask == -1)
            {
                int layer = LayerMask.NameToLayer("Ground");
                groundMask = layer >= 0 ? (1 << layer) : 0;
            }
            return groundMask;
        }
    }

    // -1 = not looked up yet. The answer cannot change while the game runs, so it is asked once.
    static int groundMask = -1;

    /// <summary>Foot-seat query with the shared per-frame convention (start 2u above, 8u total cast).</summary>
    public static bool TryGroundY(Vector3 p, int groundMask, out float y)
        => TryGroundY(p, groundMask, DefaultRayUp, DefaultMaxDistance, out y);

    /// <summary>Raycast straight down for the Ground surface under <paramref name="p"/>. Returns true and the
    /// surface height in <paramref name="y"/> when a Ground-layer hit is found; false (and <paramref name="y"/> =
    /// the input height) when there's no ground (off-map) or no Ground layer in the mask — let the caller decide
    /// its fallback. <paramref name="rayUp"/> = how far above <paramref name="p"/> to start; <paramref name="maxDistance"/>
    /// = the TOTAL cast length from that start (so it reaches <c>maxDistance - rayUp</c> below p). Spawn/teleport
    /// callers can widen both.</summary>
    public static bool TryGroundY(Vector3 p, int groundMask, float rayUp, float maxDistance, out float y)
    {
        if (groundMask != 0 &&
            Physics.Raycast(p + Vector3.up * rayUp, Vector3.down, out var hit, maxDistance, groundMask,
                            QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
            return true;
        }
        y = p.y;
        return false;
    }
}
