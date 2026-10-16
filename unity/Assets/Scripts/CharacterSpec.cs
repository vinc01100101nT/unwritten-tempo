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
    /// (with an empty civilian pool and no animator) if the asset is absent, so a missing asset degrades to "no townsfolk / no shadows"
    /// with a warning rather than a null crash.</summary>
    public static CharacterSpec Active
    {
        get
        {
            if (_active != null) return _active;
            _active = Resources.Load<CharacterSpec>(ResourcePath);
            if (_active == null)
            {
                GameLog.Warn(LogCat.Unwritten, "No Resources/CharacterSpec.asset — live-3D characters fall back to " +
                                 "code defaults and have no models to draw from.");
                _active = CreateInstance<CharacterSpec>();
            }
            return _active;
        }
    }

    /// <summary>Drop the cached spec so the next read reloads the asset. Call after editing it.</summary>
    public static void Reload() => _active = null;

    // ---- the cast -------------------------------------------------------------------------------------------------

    // The pool holds model NAMES, not object links. A direct link would drag every model in the cast into memory the
    // moment this settings asset loads — all of them, to wear one. A name costs nothing until something asks for it,
    // and the asset itself is fetched on demand through CharacterModels (the roster) → AddressablePrefabs (the cache).
    [Header("Cast (rigged model names — see CharacterModels for the roster)")]
    [Tooltip("Ordinary folk. Townsfolk draw from this pool. Entries are model names, e.g. 'Casual_Female'.")]
    public List<string> civilianModels = new List<string>();

    // NO SHADOW POOL HERE. Each shadow's body is written on its own creature entry (MonsterDef.modelName, picked in
    // the Mob Table from the whole roster), so a named Kashira wears the same body every life.

    [Tooltip("The shared Humanoid animator controller. Every model's embedded avatar retargets it, so one controller " +
             "animates the whole cast — player, townsfolk and shadows alike.")]
    public RuntimeAnimatorController animatorController;

    /// <summary>A random civilian model NAME, or null when the pool is empty. Load it via <see cref="CharacterModels"/>.</summary>
    public string RandomCivilian() => Pick(civilianModels);

    static string Pick(List<string> pool)
    {
        if (pool == null || pool.Count == 0) return null;
        // One pass to skip empty slots without allocating a filtered copy — an unassigned entry is an authoring slip,
        // not a reason to spawn nothing.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var g = pool[Random.Range(0, pool.Count)];
            if (!string.IsNullOrWhiteSpace(g)) return g;
        }
        for (int i = 0; i < pool.Count; i++) if (!string.IsNullOrWhiteSpace(pool[i])) return pool[i];
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
        cc.skinWidth = capsuleSkinWidth;
        // Step and slope are not this character's own: every body walks by the one rule (see GroundElevation).
        GroundElevation.ApplyWalkRules(cc);
    }

    // ---- the swing ------------------------------------------------------------------------------------------------

    [Header("The swing")]
    [Tooltip("Fraction (0..1) of the swing at which a basic attack LANDS. The animation still plays the full swing " +
             "for the same cadence; the damage just connects at this point — 0.7 = 70% in, the natural contact " +
             "moment — instead of right at the end.")]
    [Range(0.1f, 1f)] public float hitTiming = 0.70f;
    [Tooltip("Fraction (0..1) of the swing at which a weapon wielded in both hands (the katar, the axe) lands its SECOND " +
             "hit, from the other hand. Always after the first — 0.9 = the other hand's slash in their attacks.")]
    [Range(0.1f, 1f)] public float offHandHitTiming = 0.90f;
    [Tooltip("How hard that second hit lands, as a share of a full swing — 0.5 = half. It rolls its own miss and crit.")]
    [Range(0f, 1f)] public float offHandPower = 0.5f;

    // WHY THIS LIVES HERE: every fighter — a person or a creature — swings bare-handed by the same rule, so the
    // reach has ONE home. Creatures used to carry their own typed copy. The cadence is worked out from the weapon's
    // weight instead (UnitOffense.SwingSeconds).
    [Tooltip("Bare-handed reach, in world units at full size. A weapon's own attack range replaces it while one is " +
             "held — for a person and a creature alike.")]
    public float unarmedReach = 0.5f;

    [Tooltip("How far inside reach a fighter stops walking in: it stops at reach divided by this, then stays put " +
             "until the target moves past its full reach, and chases again. Stops the stand-vs-chase decision " +
             "flickering against somebody hovering at the edge of reach, which otherwise machine-guns the footsteps " +
             "and snaps the body between standing and walking — and never leaves anyone standing where their blow " +
             "cannot land. A creature that holds a shooting distance keeps shooting until the target is this many " +
             "times that distance away.")]
    [Range(1f, 3f)] public float stayEngagedFactor = 1.4f;

    // WHY IT LIVES HERE: this is one question — "am I close enough to stand and fight" — and every character asks it.
    // It used to be an authored field on the player's attacker and a number written into the shared rule for
    // creatures, so the person at the keyboard was tunable and the thing biting them was not.

    // WHY IT LIVES HERE rather than on the attacker where it started: the SERVER decides when a blow lands now, and
    // it has no attacker component to read — it holds a fighter, not an avatar. Two copies of this number would mean
    // the swing on your screen and the blow on the server drifting apart by however far they disagreed.

    // ---- travel ------------------------------------------------------------------------------------------------------

    [Header("Travel")]
    [Tooltip("THE movement speed: how fast a character runs — the default jog — in world units a second before unit " +
             "scale. The walk and the sprint are multiples of it, and the walk, run and sprint legs follow whatever " +
             "speed results — they are locked to the ground, so nothing else needs changing with it.")]
    public float moveSpeed = 1f;
    [Tooltip("Walk gear as a fraction of the run — the strolling pace. Townsfolk and a wandering creature use this.")]
    [Range(0.1f, 1f)] public float walkGearMult = 0.4f;
    [Tooltip("Sprint gear as a multiple of the run — the fastest gear a person has.")]
    [Range(1f, 4f)] public float sprintGearMult = 2f;

    /// <summary>Travel speed of the RUN gear — the base every other gear is a multiple of. THE single owner of that
    /// composition (base x unit scale), so nothing re-derives the chain and drifts from it.</summary>
    public float RunGearSpeed => moveSpeed * UnitScale.ForPlayer;

    /// <summary>Travel speed of the SPRINT gear — full speed.</summary>
    public float FullSpeed => RunGearSpeed * sprintGearMult;

    /// <summary>Travel speed of the WALK gear.</summary>
    public float WalkGearSpeed => RunGearSpeed * walkGearMult;

    /// <summary>The run gear as a share of full speed, for a mover that scales full speed by its gear.</summary>
    public float RunShareOfFull => 1f / Mathf.Max(1f, sprintGearMult);

    /// <summary>The walk gear as a share of full speed, for a mover that scales full speed by its gear.</summary>
    public float WalkShareOfFull => walkGearMult / Mathf.Max(1f, sprintGearMult);

    /// <summary>Ground speed at which a character switches from the Walk clip to the Run clip: midway between the two
    /// gears, so every character changes gear at the same pace.</summary>
    public float GearSwitchSpeed => 0.5f * (WalkGearSpeed + RunGearSpeed);


    // ---- footsteps -----------------------------------------------------------------------------------------------------

    [Header("Footsteps (shared by every character)")]
    public SfxSet footstepSfx;
    [Tooltip("Played instead of the normal step when walking through a puddle.")]
    public SfxSet waterFootstepSfx;
}
