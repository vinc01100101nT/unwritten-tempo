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

    /// <summary>Seat a BODY standing at <paramref name="feet"/>: the ray starts one body height above the feet, never
    /// higher. Ground a body walks under (a bridge over a river bed, measured 0.96 above it in Parley) is part of the
    /// same ground, so a ray started above it lands on its top and lifts the drawn body there while the body itself
    /// stays below. Nothing solid can sit inside a body, so its own height is the highest a start can safely be.</summary>
    public static bool TryFeetY(Vector3 feet, int groundMask, out float y)
        => TryGroundY(feet, groundMask, CharacterSpec.Active.bodyHeight, DefaultMaxDistance, out y);

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

    // SurfaceNearestY reads at most this many stacked surfaces, each search starting this far under the last one.
    const int MaxLayers = 8;
    const float LayerGap = 0.01f;
    const float SurfaceSearchHeight = 200f;

    /// <summary>The Ground surface under <paramref name="p"/>'s spot whose height is CLOSEST to <paramref name="p"/>.y,
    /// searched from far above — for a point that may already be BELOW the ground (a ray started inside the ground
    /// passes through it unseen, so the short foot-seat cast above cannot recover one). Where the ground stacks
    /// (a cliff top over its foot) the nearest layer wins, so a spot aimed at the foot stays at the foot. False, and
    /// <paramref name="y"/> = the input height, when there is no ground under the spot.</summary>
    public static bool SurfaceNearestY(Vector3 p, out float y)
    {
        y = p.y;
        int mask = GroundMask;
        if (mask == 0) return false;
        // One cast reports at most one hit per collider, and the whole ground — a bridge over its river included — is
        // one collider, so the layers are walked down one cast at a time, each starting just under the last surface.
        float best = float.MaxValue, from = p.y + SurfaceSearchHeight, bottom = p.y - SurfaceSearchHeight;
        bool any = false;
        for (int layer = 0; layer < MaxLayers && from > bottom; layer++)
        {
            if (!Physics.Raycast(new Vector3(p.x, from, p.z), Vector3.down, out var hit, from - bottom, mask,
                                 QueryTriggerInteraction.Ignore)) break;
            any = true;
            float d = Mathf.Abs(hit.point.y - p.y);
            if (d < best) { best = d; y = hit.point.y; }
            from = hit.point.y - LayerGap;
        }
        return any;
    }
}
