using UnityEngine;

/// <summary>
/// Renders a mob as a live-3D character — a "Shadow of the Past" — instead of a billboard sprite. Add it to a mob
/// and at Play it swaps that mob's flat sprite for a rigged character body built from the SAME cast and the SAME
/// animation machinery the player uses (<see cref="Character3DBody"/> → <see cref="StanceController"/> →
/// <see cref="Character3DPose"/>).
///
/// <para>PURELY A VISUAL SWAP — this is the whole point. The mob keeps its <see cref="MonsterAI3D"/> brain,
/// <see cref="Health"/>, stat block, loot, nameplate and pooling untouched, so a shadow fights, levels, takes
/// status effects and dies by exactly the rules every other mob does. Nothing about the billboard path is deleted:
/// a mob without this component is still a sprite, and the two coexist in the same scene.</para>
///
/// <para>It adds the mob's action vocabulary on top of the shared <see cref="Agent3DVisual"/> locomotion (walk,
/// facing, foot-grounding): attack, hurt and die. Those three are all a mob's brain can express — everything
/// else it does is movement.</para>
///
/// <para>PERFORMANCE: building the body attaches a <see cref="SimulationLod"/> so this mob's mover + animator +
/// this driver freeze once the player is far away — the switch the townsfolk always had and mobs never did. The
/// Animator is additionally set to skip bone writes while off camera (see <see cref="Character3DBody"/>). Per-frame
/// cost after that is the shared base's one ground raycast plus this class's timer comparisons — no allocation.</para>
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PathAgent3D))]
public class MobShadow3DVisual : Agent3DVisual
{
    [Tooltip("Seconds the attack animation plays after this mob lands a bite. Keep it under the mob's Touch " +
             "Interval so the swing finishes before the next one starts.")]
    public float swingSeconds = 0.45f;

    [Tooltip("Seconds the flinch animation plays after this mob takes damage.")]
    public float hurtSeconds = 0.3f;

    // NO freeze-distance field here on purpose. Every mob already has one on its brain (MonsterAI3D.simulateRadius),
    // which is the single owner; a second box on this component would put two editable numbers for one behaviour on
    // the same object, with whichever ran last silently winning.

    MonsterAI3D brain;
    Health health;
    UnitEquipment equipment;
    Transform player;   // the chase target it turns to face — targeting, NOT where its looks or its numbers come from
    int lastHp;
    float hurtUntil, lastAttackSeen;
    bool built;

    // Footsteps run through the SAME frame-locked plant detector the player and the townsfolk use, so a shadow's
    // steps fire on ITS OWN walk/run animation crossing an authored plant frame — never a distance timer. Per-unit
    // gait state + a cached sink so the per-frame detection allocates nothing.
    readonly FootstepTracker.Gait gait = new FootstepTracker.Gait();
    System.Action<Vector3, float> onFootPlant;

    protected override void Awake()
    {
        base.Awake();
        brain = GetComponent<MonsterAI3D>();
        health = GetComponent<Health>();
        equipment = GetComponent<UnitEquipment>();
        onFootPlant = OnFootPlant;
    }

    void Start()
    {
        BuildBody();
        var pc = FindAnyObjectByType<Player3DController>();
        player = pc != null ? pc.transform : null;
        if (health != null) lastHp = health.Current;
        gait.Seed(transform.position);
    }

    /// <summary>Swap the billboard for a live-3D character body. Idempotent — a pooled mob that already has a body
    /// keeps it (rebuilding on every respawn would Instantiate a rig per death, exactly the churn the mob pool exists
    /// to avoid).</summary>
    void BuildBody()
    {
        if (built) return;

        // A shadow has its OWN look, drawn at random from the shared cast's FIGHTER pool. It used to CLONE the live
        // player, which made every shadow identical to you and to each other, tied a mob's appearance to a player
        // (wrong the moment more than one player shares a map), and made shadows silently fail to exist at all on any
        // map where no live 3D player had spawned yet. A shadow now depends on nothing but the spec.
        var spec = CharacterSpec.Active;
        GameObject model = spec.RandomFighter();
        if (model == null)
        {
            Debug.LogWarning("[unwritten] CharacterSpec has no fighter models — '" + name + "' stays a billboard.");
            enabled = false;
            return;
        }

        // --- Retire EVERY billboard part. A shadow keeps none of the sprite rig. ---
        // BillboardAnimator3D writes this root's localScale every frame (breathing squash) and AttackLunge writes it
        // per bite; both would deform the 3D model, and the squash "tell" is replaced by a real swing clip.
        // Destroying (not disabling) is deliberate: MonsterAI3D caches AttackLunge and guards with `!= null`, so it
        // cleanly stops punching. CameraDepthSorter only orders a SPRITE, so it's dead weight here.
        var billboardAnim = GetComponent<BillboardAnimator3D>();
        if (billboardAnim != null) { billboardAnim.enabled = false; Destroy(billboardAnim); }
        var billboardShadow = GetComponent<BillboardShadow>();
        if (billboardShadow != null) Destroy(billboardShadow);
        var lunge = GetComponent<AttackLunge>();
        if (lunge != null) Destroy(lunge);
        var depthSorter = GetComponent<CameraDepthSorter>();
        if (depthSorter != null) depthSorter.enabled = false;

        // The SpriteRenderer is only HIDDEN, never destroyed: DeathDissolve [RequireComponent]s it and drives the
        // death fade that hands the mob back to the pool. Removing it would break the death → respawn chain.
        var sprite = GetComponent<SpriteRenderer>();
        if (sprite != null) sprite.enabled = false;

        // --- Rebuild the root as a CHARACTER's root, not a sprite's. ---
        // THIS is what every earlier size fix got wrong. A billboard mob's root carries the SPRITE's authored scale
        // (e.g. 0.742) times the mob-size dial, and a CharacterController sized to match. A live-3D character needs
        // an UNSCALED root — exactly how AmbientLife builds a townsperson, which is the only live-3D AI character in
        // this game that has always been the right size. Hanging a character off the billboard's scaled root meant
        // every scale I computed was fighting a scale I'd left in place.
        transform.localScale = Vector3.one;

        // The ONE character rig — the same capsule and routing footprint the player and the townsfolk get. This was a
        // second copy of the townsfolk formula, which also silently omitted the slope limit that copy set.
        Character3DBody.ApplyRig(gameObject, agent);

        // Fit the raw model to the shared body height, and declare that height so every anchor (floating numbers,
        // nameplate, projectile homing, cursor snap) measures this unit by its BODY rather than by its root scale.
        var builtPose = Character3DBody.Build(gameObject, model, spec.animatorController, spec.bodyHeight);
        if (builtPose == null) { enabled = false; return; }
        BindBody(builtPose);
        built = true;

        // Re-point the freeze switch at a 3D body's pieces. Every mob already HAS the switch (the brain attaches it),
        // and it already owns the distance — this only swaps WHICH components stop, because a shadow's costly parts
        // are its rigged Animator and this driver, not the sprite animator a billboard mob freezes.
        var lod = GetComponent<SimulationLod>() ?? gameObject.AddComponent<SimulationLod>();
        lod.Freeze(agent, builtPose.GetComponent<Animator>(), this);
        if (brain != null)
        {
            brain.Lod = lod;
            // Travel at the shared CHARACTER gear speeds, not the mob dial. Without this a character-sized body moves
            // at the mob scale (0.30x) and visibly shuffles. The brain owns which gear (chase = run, wander = walk).
            brain.UsesCharacterGears = true;
        }
    }

    /// <summary>Die beats hurt beats attack; anything else falls through to the shared walk/idle. Each is a timer
    /// comparison against state the mob already owns — no polling of other systems, no allocation.</summary>
    protected override bool DriveRoleAction()
    {
        if (health == null) return false;

        if (health.IsDead)
        {
            stance.DriveStance(StanceId.Die);
            return true;
        }

        // Flinch on any HP drop. Read from Health.Current rather than a damage event so this stays a pure visual
        // observer — nothing in the combat path has to know a shadow exists.
        int hp = health.Current;
        if (hp < lastHp) hurtUntil = Time.time + hurtSeconds;
        lastHp = hp;

        if (Time.time < hurtUntil)
        {
            stance.DriveStance(StanceId.Hurt);
            return true;
        }

        // A bite the brain just landed — MonsterAI3D stamps the moment, the one general "it attacked" signal any
        // visual can read (the billboard's squash tell used to be the only consumer).
        if (brain != null && brain.LastAttackTime > lastAttackSeen) lastAttackSeen = brain.LastAttackTime;
        if (Time.time < lastAttackSeen + swingSeconds)
        {
            // The swing pose is picked by the EQUIPPED WEAPON, through the same resolver the player's brain uses —
            // so a shadow holding a sword swings the sword stance, not a generic one.
            stance.DriveStance(WeaponStances.AttackFor(EquippedWeaponType()), swingSeconds);
            if (player != null && pose != null)
            {
                Vector3 toPlayer = player.position - transform.position; toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 1e-4f) pose.SetFacing(toPlayer.normalized);
            }
            return true;
        }

        return false;
    }

    /// <summary>Show or hide the 3D body. The billboard death path hides the SPRITE, which a shadow doesn't render —
    /// so without this a dead shadow's corpse stands around fully lit until it respawns.</summary>
    public void SetBodyVisible(bool on)
    {
        if (model == null) return;
        var rends = model.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++) rends[i].enabled = on;
    }

    /// <summary>Restore this unit's presentation after a revive. Called by <see cref="MobRespawn.Reactivate"/>, the
    /// single revive path, INSTEAD of its billboard restore — a shadow owns its own body, and letting that path
    /// switch the SpriteRenderer back on is what made respawned shadows pop back as sprites.</summary>
    public void OnRevived()
    {
        SetBodyVisible(true);

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = false;   // a shadow NEVER shows the sprite, in any lifecycle state

        // Drop the death pose and re-baseline the per-life state, so a recycled body doesn't flinch from last
        // life's HP or fire a phantom footstep from where it died.
        if (stance != null) stance.ClearAction();
        if (health != null) lastHp = health.Current;
        hurtUntil = 0f;
        lastAttackSeen = 0f;
        gait.Seed(transform.position);
    }

    /// <summary>Walk vs Run, switched at the shared gear-change speed rather than a hand-picked threshold — so every
    /// character changes gear at the same pace, whoever is driving it.</summary>
    protected override StanceId LocomotionStanceFor(float groundSpeed)
        => groundSpeed >= CharacterSpec.Active.GearSwitchSpeed ? StanceId.Run : StanceId.Walk;

    /// <summary>This unit's equipped weapon type (None when unarmed) — the mob's own gear, read through the same
    /// UnitEquipment every mob carries.</summary>
    WeaponType EquippedWeaponType()
    {
        var w = equipment != null ? equipment.Get(EquipSlotType.Weapon) : null;
        return w != null && w.def != null ? w.def.weaponType : WeaponType.None;
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();

        // Footsteps: the SAME shared plant detector the player and townsfolk run, fired off this unit's own
        // walk/run animation frames. Cheap — a couple of subtracts and a phase read, no allocation.
        var tracker = FootstepTracker.Instance;
        if (built && tracker != null && pose != null)
            tracker.DetectPlants(pose, transform.position, gait, onFootPlant);
    }

    // Steps come from the SHARED character sound set, not off the player object — a shadow's feet are its own.
    void OnFootPlant(Vector3 foot, float sizeScale)
    {
        FootstepTracker.Instance?.EmitStep(foot, sizeScale);
        var spec = CharacterSpec.Active;
        if (spec.footstepSfx == null || spec.footstepSfx.Empty) return;
        bool onWater = spec.waterFootstepSfx != null && !spec.waterFootstepSfx.Empty
                       && WetGround.Instance != null && WetGround.Instance.IsPuddleAt(foot);
        Sfx.PlayAt(onWater ? spec.waterFootstepSfx : spec.footstepSfx, foot);
    }
}
