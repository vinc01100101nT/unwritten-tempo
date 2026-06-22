using UnityEngine;

/// <summary>
/// Keeps player + mobs from physically colliding with (and climbing onto) <i>each other</i> while still
/// colliding with terrain and props. It puts every unit on a shared "Crowd" layer and, once, disables
/// Crowd↔Crowd collision in the physics matrix — the 3D twin of what the 2D <see cref="MonsterAI"/> did
/// with <c>Physics2D.IgnoreLayerCollision</c>. With CharacterControllers that otherwise climb onto one
/// another's capsules (the "hopping on heads" bug), pass-through is the clean fix; <see cref="PathAgent3D"/>'s
/// avoidance steering then spreads mobs out so they don't occupy the same spot.
///
/// The "Crowd" layer is CREATED at bake time (editor, in Build25DSpike via WorldBuildUtil.EnsureLayer);
/// at runtime this just looks it up and assigns it. If the project hasn't been re-baked yet the layer won't
/// exist and this safely no-ops (units keep the old solid-collision behavior).
/// </summary>
public static class UnitCrowd
{
    public const string LayerName = "Crowd";

    static int layer = -2;          // -2 = not looked up yet, -1 = missing
    static bool collisionDisabled;

    /// <summary>The Crowd layer index, or -1 if it doesn't exist (project not re-baked).</summary>
    public static int Layer
    {
        get { if (layer == -2) layer = LayerMask.NameToLayer(LayerName); return layer; }
    }

    /// <summary>A LayerMask selecting just the Crowd layer (0 when the layer is missing) — for the
    /// avoidance OverlapSphere so it only finds other units.</summary>
    public static int Mask => Layer >= 0 ? (1 << Layer) : 0;

    /// <summary>Put <paramref name="go"/> on the Crowd layer and (the first time) disable Crowd↔Crowd
    /// physics collision, so units pass through each other but still collide with the world. No-ops if the
    /// layer hasn't been created yet.</summary>
    public static void Register(GameObject go)
    {
        int l = Layer;
        if (l < 0 || go == null) return;
        go.layer = l;
        if (!collisionDisabled) { Physics.IgnoreLayerCollision(l, l, true); collisionDisabled = true; }
    }
}
