using UnityEngine;

/// <summary>
/// Puddle FOOTSTEP SPLASHES — the pooled droplet burst kicked up when a foot lands in standing water.
/// Subscribes to the ONE central footstep signal (<see cref="FootstepTracker.Stepped"/>), the same positions
/// that drive the wet-ground ripples and snow prints, so the splash stays glued to the visibly-stepping feet
/// without re-finding the player or re-running any gait logic.
///
/// <para>Division of labour with <see cref="WetGround"/>: that shader already draws the flat, expanding ripple
/// RING on the puddle surface — this component only adds the VERTICAL droplet spray the screen-space shader
/// can't do. No dual path: the ring stays the shader's job, the kicked-up water is this one's.</para>
///
/// <para>Fires only where there is an actual puddle right now (<see cref="WetGround.IsPuddleAt"/> — which is
/// already gated to High tier + visible coverage), so dry ground and Low/Medium tiers cost nothing but the
/// event hop. The splash prefab is resolved by id from the EffectLibrary and spawned through the pooled
/// <see cref="Vfx"/> choke, scaled by the step size (a jump-landing throws a bigger splash). Drop ANY splash
/// prefab into <c>Assets/Art/VFX/water/</c> + Rebuild Effect Library and it's picked up as <c>water.&lt;name&gt;</c>
/// — no code edit. Until the id is registered this component is simply inert (no errors).</para>
/// </summary>
[DisallowMultipleComponent]
public class WaterStep : MonoBehaviour
{
    [Tooltip("EffectLibrary id of the splash burst. NamuFX stylized splashes register as 'water.splash-a/-b/-multiple'; " +
             "the built-in placeholder is 'water.splash'. Drop any prefab into a 'water' pack folder + Rebuild.")]
    public string splashId = "water.splash-a";
    [Range(0.01f, 6f)] [Tooltip("Master size multiplier applied ON TOP of the per-step size (a normal step = 1, a " +
             "jump-landing is larger). Raise for chunkier splashes; the NamuFX splashes are authored large, so dial " +
             "WAY down (~0.05–0.2) for foot-sized puddle splashes.")]
    public float scale = 1f;
    [Tooltip("World-space lift so the splash sits on the water surface rather than buried in the ground.")]
    public float yOffset = 0.02f;
    [Range(0, 32)] [Tooltip("Instances to pre-warm into the pool at start so the first splash doesn't hitch.")]
    public int prewarm = 6;

    // Self-bootstrap so nobody has to hand-add this to a scene: if the game starts and no WaterStep exists yet,
    // spawn one that survives scene loads. Mirrors FootstepTracker.Ensure / WetGround.Ensure. No-ops when the
    // Systems scene already carries one (SystemsSceneBuilder adds it), so there's never a duplicate.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<WaterStep>() != null) return;
        var go = new GameObject("WaterStep");
        DontDestroyOnLoad(go);
        go.AddComponent<WaterStep>();
    }

    void Start()
    {
        if (prewarm > 0 && EffectCatalog.Has(splashId)) Vfx.Prewarm(splashId, prewarm);
    }

    void OnEnable()  { FootstepTracker.Stepped += OnStep; }
    void OnDisable() { FootstepTracker.Stepped -= OnStep; }

    // Cold path: fires a few times/second per walker, never per-frame. Zero-GC — IsPuddleAt is a single fbm
    // (no alloc), VfxParams is a struct, and the spawn is pooled.
    void OnStep(Vector3 world, float size)
    {
        var wet = WetGround.Instance;
        if (wet == null || !wet.IsPuddleAt(world)) return;   // no visible puddle here → nothing to splash
        if (!EffectCatalog.Has(splashId)) return;            // prefab not imported yet → stay inert
        world.y += yOffset;
        Vfx.PlayAt(splashId, world, Vfx.VfxParams.Scaled(Mathf.Max(0.01f, scale * size)));
    }
}
