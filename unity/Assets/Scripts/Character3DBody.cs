using UnityEngine;

/// <summary>
/// The ONE way to build a live-3D character body under a root object: clone a rigged Quaternius model, fit it to a
/// world height, swap it onto the shared Humanoid controller, and attach the pose sink + stance choke point the
/// player uses. Every live-3D unit is built through here — the player's townsfolk (<see cref="AmbientLife"/>) and
/// the 3D shadow mobs (<see cref="MobShadow3DVisual"/>) — so there is exactly one recipe for "a character body",
/// not one per unit type that must be hand-synced.
///
/// <para>SCALE: the model is measured BEFORE parenting (renderer bounds are world-space, so measuring after would
/// return the parent-scaled height) and its local scale then divides out the root's own lossy scale. That's what
/// lets the same builder serve an UNSCALED root (townsfolk) and a SCALED one (a mob root carries the scale its size
/// was fitted to, <see cref="MobBody"/>) without either caller knowing about the other's rig.</para>
///
/// <para>OFF-SCREEN COST: the Animator is set to stop writing bone transforms while the model is off camera
/// (<see cref="AnimatorCullingMode.CullUpdateTransforms"/>). The state machine still runs, so a swing that started
/// on-screen finishes correctly and can never stick on a frame — only the per-bone work is skipped. This is the
/// half of "don't animate what you can't see" that a distance check can't do: a unit can be close enough to
/// simulate and still be behind the camera.</para>
/// </summary>
public static class Character3DBody
{
    /// <summary>The height EVERY live-3D character is built to, read from the shared <see cref="CharacterSpec"/>.
    ///
    /// <para>This used to measure the live player with a scene search. That search is ordering-dependent (Unity's own
    /// deprecation notice says so), and once townsfolk exist there are several characters to find — so a shadow built
    /// after the townsfolk could size itself against a TOWNSPERSON instead of the player. An authored number cannot
    /// pick the wrong object.</para></summary>
    public static float BodyHeight => CharacterSpec.Active.bodyHeight;

    /// <summary>Give <paramref name="root"/> the shared character RIG: the one collision capsule every character uses
    /// and, when it routes, the shared footprint. Both used to be a copy-pasted formula off the body height — one copy
    /// in the townsfolk builder, another in the shadow builder, and hand-tuned constants on the player, all disagreeing.
    /// This is now the only place a character's collision shape is decided.</summary>
    public static void ApplyRig(GameObject root, PathAgent3D agent = null)
    {
        if (root == null) return;
        var spec = CharacterSpec.Active;

        // A CharacterController validates its dimensions on assignment and rejects a capsule that doesn't fit its
        // current scale, so toggle it off around the write and restore whatever state the caller had it in.
        var cc = root.GetComponent<CharacterController>();
        if (cc != null)
        {
            bool wasEnabled = cc.enabled;
            cc.enabled = false;
            spec.ApplyCapsule(cc);
            cc.enabled = wasEnabled;
        }
        if (agent != null) agent.agentRadius = Mathf.Max(0.05f, spec.routingRadius);
    }

    /// <summary>Build the visual body under <paramref name="root"/> and return its <see cref="Character3DPose"/>
    /// (the render sink), or null if <paramref name="model"/> was null. The caller owns the root, its mover, and its
    /// brain — this owns only the body: the scaled model child, its Animator, the pose sink, and the
    /// <see cref="StanceController"/> choke point on the root that drives them.</summary>
    /// <param name="root">The unit's unscaled-or-scaled root. Gets the StanceController.</param>
    /// <param name="model">A rigged character model (Assets/1_Authored/Art/Characters/Models).</param>
    /// <param name="controller">The shared Humanoid controller every model's embedded avatar retargets.</param>
    /// <param name="targetHeight">World height (feet to crown) to fit the model to. The model is measured at scale one
    /// and fitted to this, which is the right treatment for a raw model ASSET — the only kind of source there is now.
    /// (There used to be a second mode that copied a live character's exact world scale, for when a shadow mob cloned
    /// the player's body. Shadows draw their own model from the shared cast, so nothing clones a live character any
    /// more and that mode is gone. Do not reintroduce fitting on an already-correct source: re-deriving a fit from a
    /// corrected size overwrites the very correction, which is what rendered a shadow 10x too large, twice.)</param>
    public static Character3DPose Build(GameObject root, GameObject model, RuntimeAnimatorController controller,
                                        float targetHeight)
    {
        if (root == null || model == null) return null;

        var vis = Object.Instantiate(model, root.transform);
        vis.hideFlags = HideFlags.DontSaveInEditor;   // runtime spawn — never bake a stray copy into a saved scene
        vis.transform.localPosition = Vector3.zero;

        // FIT TO HEIGHT. Measured at scale one so the reading is the model's true native height, then divided by the
        // root's own scale so the same builder serves an UNSCALED root (townsfolk, shadows) and a scaled one.
        Vector3 rootScale = root.transform.lossyScale;
        vis.transform.localScale = Vector3.one;
        float native = MeasureHeight(vis);
        vis.transform.localScale = Vector3.one * (native > 1e-3f ? targetHeight / (native * NonZero(rootScale.y)) : 1f);

        var anim = vis.GetComponent<Animator>() ?? vis.AddComponent<Animator>();
        if (controller != null) anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        var pose = vis.GetComponent<Character3DPose>() ?? vis.AddComponent<Character3DPose>();

        // Stance choke point on the ROOT (it resolves the pose sink from the child). selfDrive OFF so a still unit
        // holds the authored Idle stance like the player, rather than the sink's movement-sampled idle.
        var stance = root.GetComponent<StanceController>() ?? root.AddComponent<StanceController>();
        stance.selfDrive = false;

        // Tell the world how tall this unit ACTUALLY is. A live-3D character's root scale is not its size (the body is
        // this fitted child), so without this every anchor — floating numbers, nameplates, cast bar, target marker,
        // projectile homing, cursor snap — would derive the height from the root and land far above the head.
        UnitBody.Set(root, targetHeight, pose.VisualWorldWidth);

        return pose;
    }

    static float NonZero(float v) => Mathf.Abs(v) > 1e-5f ? v : 1f;

    /// <summary>World-space rendered height (union of every renderer's bounds) at the object's CURRENT scale. Call
    /// on a freshly instantiated, unparented model to get its native height.</summary>
    public static float MeasureHeight(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 0f;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b.size.y;
    }
}
