using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// THE single source of truth for every live-3D character in the game — the player, the townsfolk, and the 3D shadow
/// mobs. Anything that is true of "a character body" as such (how tall it stands, how wide it collides, how fast its
/// gears travel, how quickly its legs churn, which models it can be drawn from, what its feet sound like) is authored
/// HERE, once, and every unit type READS it. No unit keeps its own copy.
///
/// <para><b>Why it exists.</b> The same facts used to live in three places that silently disagreed. The collision
/// capsule is the clearest case: the player carried hand-tuned constants (radius 0.15, height 0.5) while townsfolk and
/// shadow mobs each ran a COPY-PASTED formula off the body height (radius = height x 0.18 = 0.059). Same body, same
/// rendered height, collision shapes 2.5x apart — and the shadow's copy of the formula silently omitted the slope
/// limit the townsfolk copy set. Height was worse than a copy: it was measured by a scene search that Unity documents
/// as ordering-dependent, so a shadow built after the townsfolk could size itself against a TOWNSPERSON.</para>
///
/// <para><b>What this deliberately does NOT own.</b> Stances and cues are already single-sourced — the Stance Composer
/// writes one <see cref="StanceLibrary"/> asset read through <see cref="StanceCatalog"/>, and the Cue Composer writes
/// one <see cref="VfxCueLibrary"/> read through <see cref="VfxCueCatalog"/>. Mirroring either here would be a second
/// authoring path for the same data, which is the exact failure this class exists to prevent. It also does not own
/// per-unit IDENTITY (which model a unit drew) or per-unit INTENT (input for the player, a brain for an NPC) — those
/// are the things that legitimately differ. Shared SPECS, per-unit identity.</para>
///
/// <para>PERFORMANCE: loaded once from <c>Resources/CharacterSpec.asset</c> and cached in a static, exactly like
/// <see cref="StanceCatalog"/>. Every accessor below is a field read — nothing here allocates or searches the scene.
/// When the asset is missing, a code-default instance stands in so the game still runs.</para>
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Character Spec", fileName = "CharacterSpec")]
public class CharacterSpec : ScriptableObject
{
    const string ResourcePath = "CharacterSpec";

    static CharacterSpec _active;

    /// <summary>The live spec every character reads. Loaded once and cached; falls back to a code-default instance
    /// (with empty model pools) if the asset is absent, so a missing asset degrades to "no townsfolk / no shadows"
    /// with a warning rather than a null crash.</summary>
    public static CharacterSpec Active
    {
        get
        {
            if (_active != null) return _active;
            _active = Resources.Load<CharacterSpec>(ResourcePath);
            if (_active == null)
            {
                Debug.LogWarning("[unwritten] No Resources/CharacterSpec.asset — live-3D characters fall back to " +
                                 "code defaults and have no models to draw from.");
                _active = CreateInstance<CharacterSpec>();
            }
            return _active;
        }
    }

    /// <summary>Drop the cached spec so the next read reloads the asset. Call after editing it.</summary>
    public static void Reload() => _active = null;

    // ---- the cast -------------------------------------------------------------------------------------------------

    [Header("Cast (rigged Quaternius models — Assets/1_Authored/Art/Characters/Models)")]
    [Tooltip("Ordinary folk. Townsfolk draw from this pool.")]
    public List<GameObject> civilianModels = new List<GameObject>();

    [Tooltip("Soldiers, knights, and other fighters. Shadow mobs draw from this pool, so a Shadow of the Past reads " +
             "as a fallen combatant rather than a chef.")]
    public List<GameObject> fighterModels = new List<GameObject>();

    [Tooltip("The shared Humanoid animator controller. Every model's embedded avatar retargets it, so one controller " +
             "animates the whole cast — player, townsfolk and shadows alike.")]
    public RuntimeAnimatorController animatorController;

    /// <summary>A random civilian model, or null when the pool is empty.</summary>
    public GameObject RandomCivilian() => Pick(civilianModels);

    /// <summary>A random fighter model, or null when the pool is empty.</summary>
    public GameObject RandomFighter() => Pick(fighterModels);

    static GameObject Pick(List<GameObject> pool)
    {
        if (pool == null || pool.Count == 0) return null;
        // One pass to skip empty slots without allocating a filtered copy — an unassigned entry is an authoring slip,
        // not a reason to spawn nothing.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var g = pool[Random.Range(0, pool.Count)];
            if (g != null) return g;
        }
        for (int i = 0; i < pool.Count; i++) if (pool[i] != null) return pool[i];
        return null;
    }

    // ---- body ------------------------------------------------------------------------------------------------------

    [Header("Body")]
    [Tooltip("Rendered height in world units, feet to crown. EVERY live-3D character is fitted to this — the player, " +
             "a townsperson and a shadow all stand exactly this tall, because they are the same body.")]
    public float bodyHeight = 0.327f;

    // ---- collision + routing ----------------------------------------------------------------------------------------

    [Header("Collision capsule (one shape for every character)")]
    [Tooltip("Deliberately SHORT so the capsule's top clears overhead prop geometry (roof eaves, tree leaves) while " +
             "the bottom still bumps and climbs ground-level geometry.")]
    public float capsuleHeight = 0.5f;
    public float capsuleRadius = 0.15f;
    [Tooltip("Capsule centre height above the root. The VISUAL is seated on the ground separately.")]
    public float capsuleCenterY = 0.25f;
    [Tooltip("Tallest step that can be climbed. Shares the pathfinder's cliff/stair threshold so what a character " +
             "can walk up and what the router thinks it can walk up are the same number.")]
    public float capsuleStepOffset = GroundElevation.MaxClimbStep;
    public float capsuleSlopeLimit = 45f;
    [Tooltip("Low, so the capsule bottom rests on the ground instead of hovering.")]
    public float capsuleSkinWidth = 0.02f;

    [Tooltip("Footprint the router steers this character around obstacles with.")]
    public float routingRadius = 0.35f;

    /// <summary>Write the shared capsule onto <paramref name="cc"/>. THE one place a character's collision shape is
    /// decided — the player's controller, the townsfolk builder and the shadow builder all call this, so the three can
    /// no longer drift. Caller must handle enabling/disabling around the write if the controller is live.</summary>
    public void ApplyCapsule(CharacterController cc)
    {
        if (cc == null) return;
        cc.height = capsuleHeight;
        cc.radius = capsuleRadius;
        cc.center = new Vector3(0f, capsuleCenterY, 0f);
        cc.stepOffset = capsuleStepOffset;
        cc.slopeLimit = capsuleSlopeLimit;
        cc.skinWidth = capsuleSkinWidth;
    }

    // ---- travel + gait ------------------------------------------------------------------------------------------------

    [Header("Travel + gait")]
    [Tooltip("Base ground translation before unit scale (world units). Changing this moves a character faster WITHOUT " +
             "scaling the animation.")]
    public float moveSpeed = 2f;
    [Tooltip("Baseline that scales movement AND animation together, so legs and footsteps churn with it.")]
    public float gaitSpeed = 1.5f;
    [Tooltip("Walk gear as a fraction of full speed — the strolling pace. Townsfolk and a wandering shadow use this.")]
    public float walkGearMult = 0.2f;
    [Tooltip("Run gear as a fraction of full speed — the default jog. A chasing shadow uses this.")]
    [Range(0.1f, 1f)] public float runGearMult = 0.5f;

    /// <summary>Travel speed of the WALK gear. THE single owner of that composition (base x unit scale x gear x gait),
    /// so nothing re-derives the chain and drifts from it.</summary>
    public float WalkGearSpeed => moveSpeed * UnitScale.ForPlayer * walkGearMult * gaitSpeed;

    /// <summary>Travel speed of the RUN gear — same single-owner rule as <see cref="WalkGearSpeed"/>.</summary>
    public float RunGearSpeed => moveSpeed * UnitScale.ForPlayer * runGearMult * gaitSpeed;

    /// <summary>Ground speed at which a character switches from the Walk clip to the Run clip: midway between the two
    /// gears, so every character changes gear at the same pace.</summary>
    public float GearSwitchSpeed => 0.5f * (WalkGearSpeed + RunGearSpeed);

    /// <summary>Leg cadence for a character travelling under <paramref name="liveMoveMult"/> (its own live Slow/Haste)
    /// in a gear whose animation tier is <paramref name="gearAnimTier"/>. ONE rule for every character: the player's
    /// controller folds exactly this, and an AI unit now folds it too instead of holding a value frozen at spawn.</summary>
    public float GaitMult(float liveMoveMult, float gearAnimTier) => gaitSpeed * liveMoveMult * gearAnimTier;

    // ---- footsteps -----------------------------------------------------------------------------------------------------

    [Header("Footsteps (shared by every character)")]
    public SfxSet footstepSfx;
    [Tooltip("Played instead of the normal step when walking through a puddle.")]
    public SfxSet waterFootstepSfx;
}
