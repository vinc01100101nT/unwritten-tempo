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
    // NO swing-length field here on purpose. How long a swing lasts is now two facts owned by two places that
    // already hold them: the BEAT it must finish inside belongs to the creature's attack cadence on the machine
    // that owns it, and how long the animation itself takes belongs to the clip. A number typed here was a third
    // answer that agreed with neither, and it could not follow an attack-speed buff.

    // NO freeze-distance field here on purpose. Every mob already has one on its brain (MonsterAI3D.simulateRadius),
    // which is the single owner; a second box on this component would put two editable numbers for one behaviour on
    // the same object, with whichever ran last silently winning.

    MonsterAI3D brain;
    Health health;
    // The one owner of "is this unit flinching" — the door every real hit passes through sets it, on the machine
    // that decided the blow AND on every machine the blow was announced to.
    StatusController status;
    bool built;
    bool building;   // a model is on its way — keeps a second call from starting a second load for the same mob

    // Footsteps run through the SAME frame-locked plant detector the player and the townsfolk use, so a shadow's
    // steps fire on ITS OWN walk/run animation crossing an authored plant frame — never a distance timer. Per-unit
    // gait state + a cached sink so the per-frame detection allocates nothing.
    readonly FootstepTracker.Gait gait = new FootstepTracker.Gait();
    System.Action<Vector3, float> onFootPlant;

    /// <summary>WHICH BODY THIS SHADOW WEARS, drawn from the shared cast's fighter pool. Empty until somebody has
    /// chosen — which on a machine that only WATCHES this creature means the choice has not arrived yet.
    ///
    /// <para>It is a per-creature IDENTITY, so exactly one machine may decide it: the one that owns the creature.
    /// Every machine used to draw its own, independently, from the same pool — so the same creature stood in front
    /// of two people wearing two different bodies, and neither of them was wrong from where they were sitting.</para></summary>
    public string ModelName { get; private set; }

    // Whether the machine that owns this creature has spoken about its body yet — separately from WHAT it said,
    // because "the owner will name it in a moment" and "nobody owns this, choose your own" are different answers
    // and both of them look like an empty name.
    bool modelIsTold;

    /// <summary>The owning machine's word on which body this creature wears. Called by whatever is following this
    /// creature for the server, both when the body is built and again if the name lands after it.
    ///
    /// <para>Calling it with nothing is meaningful and is not ignored: it says the owner is the one choosing, so
    /// this machine must wait rather than draw a body of its own. Until then the creature is simply still a
    /// sprite, which is a legal state it was already in.</para></summary>
    public void WearModel(string name)
    {
        modelIsTold = true;
        if (string.IsNullOrWhiteSpace(name) || name == ModelName) return;
        ModelName = name;
        BuildBody();
    }

    protected override void Awake()
    {
        base.Awake();
        brain = GetComponent<MonsterAI3D>();
        health = GetComponent<Health>();
        status = GetComponent<StatusController>();
        onFootPlant = OnFootPlant;

        // THE OWNER CHOOSES THE BODY, HERE, BEFORE THE CREATURE IS ANNOUNCED. This is a name out of a list and
        // nothing else — no model is fetched and no rig is assembled, so a machine with no screen pays a single
        // random pick for it. It is chosen in Awake rather than Start because both places that create a shadow add
        // this component and announce the creature on the very next line, and what crosses the wire is read off the
        // creature at that moment.
        if (GameServer.IsServer) ModelName = CharacterSpec.Active.RandomFighter();
    }

    void Start()
    {
        // THE RIG IS GAMEPLAY, THE MODEL IS PRESENTATION, and only one of them belongs on a machine with no screen.
        // A map server was fetching a character model and assembling an animated rig for every shadow creature it
        // held, for nobody at all. It still needs everything below the skin — the capsule this creature collides
        // with, the footprint it routes on, and the character travel gears that also decide its reach — because
        // those are facts about the creature rather than a picture of it.
        // A watching machine still waits for the model before it changes anything: until the model lands the
        // creature is simply still a sprite, which is a legal state it was already in, rather than nothing at all.
        if (GameServer.IsServer) { BecomeACharactersBody(); built = true; }
        else BuildBody();
        gait.Seed(transform.position);
    }

    /// <summary>Swap the billboard for a live-3D character body. Idempotent — a pooled mob that already has a body
    /// keeps it (rebuilding on every respawn would Instantiate a rig per death, exactly the churn the mob pool exists
    /// to avoid).
    ///
    /// <para>The model ARRIVES rather than being on hand: the cast holds names and the asset is fetched through the
    /// shared roster, so the swap itself happens in the callback. Until it lands the mob is simply still a billboard —
    /// a legal state it was already in the frame before, so nothing downstream sees a half-built shadow.</para></summary>
    void BuildBody()
    {
        if (built || building) return;

        // A shadow has its OWN look, drawn from the shared cast's FIGHTER pool. It used to CLONE the live player,
        // which made every shadow identical to you and to each other, tied a mob's appearance to a player (wrong the
        // moment more than one player shares a map), and made shadows silently fail to exist at all on any map where
        // no live 3D player had spawned yet. A shadow now depends on nothing but the spec.
        var spec = CharacterSpec.Active;
        if (string.IsNullOrWhiteSpace(ModelName))
        {
            // NOTHING TO WEAR YET. If the machine that owns this creature is the one choosing, waiting is the whole
            // of the job — it will say, and this creature stays a sprite in the meantime. Drawing one here instead
            // is exactly the bug this replaced: every watcher picked its own and no two agreed.
            if (modelIsTold) return;
            ModelName = spec.RandomFighter();
        }

        string modelName = ModelName;
        if (string.IsNullOrWhiteSpace(modelName))
        {
            GameLog.Warn(LogCat.Unwritten, "CharacterSpec has no fighter models — '" + name + "' stays a billboard.");
            enabled = false;
            return;
        }

        building = true;
        CharacterModels.Load(modelName, model =>
        {
            if (this == null) return;   // mob died/recycled while its model was loading
            building = false;
            if (model == null)
            {
                GameLog.Warn(LogCat.Unwritten, "No character model registered as '" + modelName + "' — '" + name +
                                 "' stays a billboard.");
                enabled = false;
                return;
            }
            SwapToBody(spec, model);
        });
    }

    /// <summary>Everything that makes this creature a CHARACTER rather than a sprite, except the picture: the
    /// billboard parts retired, the root unscaled, the shared collision capsule and routing footprint applied, and
    /// the character travel gears switched on.
    ///
    /// <para>Split out from the model swap because a headless map server needs all of it and none of the model.
    /// Every line here changes what the creature IS — how wide it is, what it can climb, how fast it walks, how far
    /// it can reach — so a server that skipped them would run a different creature from the one everybody sees.</para>
    ///
    /// <para>Idempotent: a watching machine runs it again when the model lands, which costs two null checks.</para></summary>
    void BecomeACharactersBody()
    {
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

        // HOW TALL THIS CREATURE IS, stated rather than guessed from its root — the model normally declares it, and
        // a headless server has no model. Anything that measures a unit by its body reads this: a projectile aims
        // at mid-body, and where it aims decides how far it flies and therefore when its damage lands. Left unsaid,
        // a server would time a caster's bolt against a different creature from the one everybody is watching.
        UnitBody.Set(gameObject, CharacterSpec.Active.bodyHeight);

        PointTheFreezeSwitchAt(null);

        // Travel at the shared CHARACTER gear speeds, not the mob dial. Without this a character-sized body moves
        // at the mob scale (0.30x) and visibly shuffles — and it is not only a look: the same answer decides how
        // far this creature can reach, so a server that skipped it would run creatures that fight differently from
        // the ones on screen.
        if (brain != null) brain.UsesCharacterGears = true;
    }

    // Which of this unit's parts stop when the player is far away. Every mob already HAS the switch (the brain
    // attaches it) and it already owns the distance — this only says WHICH components it stops, because a shadow's
    // costly parts are its rigged Animator and this driver rather than the sprite animator a billboard freezes.
    // The animator is null until a model lands, and on a headless server it never does.
    void PointTheFreezeSwitchAt(Animator bodyAnimator)
    {
        var lod = GetComponent<SimulationLod>() ?? gameObject.AddComponent<SimulationLod>();
        lod.Freeze(agent, bodyAnimator, this);
        if (brain != null) brain.Lod = lod;
    }

    // The in-place sprite→character swap, once the model is in hand. Presentation only: everything that changes
    // what this creature IS happened in BecomeACharactersBody, which a headless server runs on its own.
    void SwapToBody(CharacterSpec spec, GameObject model)
    {
        if (built) return;

        BecomeACharactersBody();

        // Fit the raw model to the shared body height, and declare that height so every anchor (floating numbers,
        // nameplate, projectile homing, cursor snap) measures this unit by its BODY rather than by its root scale.
        var builtPose = Character3DBody.Build(gameObject, model, spec.animatorController, spec.bodyHeight);
        if (builtPose == null) { enabled = false; return; }
        BindBody(builtPose);
        built = true;

        PointTheFreezeSwitchAt(builtPose.GetComponent<Animator>());
    }

    /// <summary>Die beats hurt; anything else falls through to the shared walk/idle. Two comparisons against state
    /// this mob already owns — no polling of other systems, no allocation.
    ///
    /// <para>THE SWING IS NOT HERE ANY MORE, deliberately. It arrives as a beat — which action, the moment it
    /// began, and the window it must finish inside — through <see cref="Agent3DVisual.TellAction"/>, from the
    /// creature's own brain on the machine that owns it and from the server's announcement on every machine
    /// watching. That layer outranks this one, so a swing plays over a walk without either having to know about
    /// the other, and one description of a swing serves every machine.</para></summary>
    protected override bool DriveRoleAction()
    {
        if (health == null) return false;

        if (health.IsDead)
        {
            // Death has to beat a swing that was still running when the killing blow landed, or a creature dies
            // mid-bite and finishes the bite lying down. The one-shot layer outranks this one, so it is cancelled
            // rather than merely out-driven.
            stance.CancelActionOverride();
            stance.DriveStance(StanceId.Die);
            return true;
        }

        // FLINCH, from the ONE thing that decides it: the door every real hit passes through sets this, and it is
        // set on a watched copy too because the blow was announced to this machine. It used to be inferred here
        // from the health number dropping, which made two owners of one reaction — and the inferred one was the
        // worse: a watched creature's health arrives ten times a second, so two hits inside the same tenth of a
        // second showed one flinch, and a creature that had earned its footing flinched anyway.
        if (status != null && status.MovementFlinched)
        {
            stance.DriveStance(StanceId.Hurt);
            return true;
        }

        return false;
    }

    /// <summary>Turn toward whoever this creature is after while it stands its ground, so a bite lands facing its
    /// target instead of facing wherever it last walked. Falls back to the shared answer — the line it travelled —
    /// the moment it is walking.
    ///
    /// <para>A COPY IS TOLD WHICH WAY IT FACES AND NEVER WORKS IT OUT, exactly as another player's watched body is:
    /// the machine that owns the creature has already turned its body, that direction travels with its position,
    /// and the body root here has already been turned to match — so it is read straight off the root rather than
    /// kept a second time. This used to answer NOTHING on a copy, because a copy chases nobody and so had nobody to
    /// turn to: every shadow anybody watched faced the direction it was summoned in for the whole of its life,
    /// including while it was biting somebody standing behind it.</para></summary>
    protected override Vector3 FacingThisFrame(Vector3 groundTravel, bool walking, Vector3 position)
    {
        if (brain != null && !brain.ThinksForItself) return transform.forward;

        if (walking) return base.FacingThisFrame(groundTravel, true, position);

        var prey = brain != null ? brain.PreyBody : null;
        if (prey == null) return Vector3.zero;

        Vector3 toPrey = prey.position - position; toPrey.y = 0f;
        return toPrey.sqrMagnitude > 1e-4f ? toPrey : Vector3.zero;
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

        // Drop the death pose and re-baseline the per-life state, so a recycled body doesn't hold last life's
        // swing or fire a phantom footstep from where it died.
        if (stance != null) { stance.ClearAction(); stance.CancelActionOverride(); }
        gait.Seed(transform.position);
    }

    /// <summary>Walk vs Run, switched at the shared gear-change speed rather than a hand-picked threshold — so every
    /// character changes gear at the same pace, whoever is driving it. A shadow copies a player, so it uses the
    /// player's own two gears; the rule lives in the base because another body stands in for a player too.</summary>
    protected override StanceId LocomotionStanceFor(float groundSpeed) => PlayersGearFor(groundSpeed);

    protected override void LateUpdate()
    {
        // A MACHINE WITH NO SCREEN STILL HAS TO TURN THIS BODY. Everything below needs a model to drive and there is
        // none here, so the shared driver stops at its first line — which left this body pointing whichever way it
        // was spawned for the whole of its life. Which way a creature is turned is the one thing about it that every
        // watching machine can see and none of them can work out alone, so it is decided here and travels with the
        // creature's position like every other fact about it.
        if (GameServer.IsServer) { TurnTheBodyItself(); return; }

        base.LateUpdate();

        // Footsteps: the SAME shared plant detector the player and townsfolk run, fired off this unit's own
        // walk/run animation frames. Cheap — a couple of subtracts and a phase read, no allocation.
        var tracker = FootstepTracker.Instance;
        if (built && tracker != null && pose != null)
            tracker.DetectPlants(pose, transform.position, gait, onFootPlant);
    }

    // Where this body stood last frame. Kept here because the shared driver's own copy of it is never reached on a
    // machine with no screen — that driver returns before it updates it.
    Vector3 serverLastPos;
    bool serverPosSeeded;

    /// <summary>A BODY HANDED BACK OUT OF THE POOL COMES BACK SOMEWHERE ELSE, so last life's position would read as
    /// one enormous step on its first frame and snap this creature to face along that jump. Re-baselined on every
    /// switch-on, for the same reason and at the same moment the shared driver re-baselines its own.</summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        serverPosSeeded = false;
    }

    /// <summary>SERVER ONLY: point this creature's body the way it is actually going, or at whoever it is fighting.
    ///
    /// <para>Asked of the SAME function that answers it on a machine that can see, so the two can never drift into
    /// deciding a facing differently. Written straight on with no easing: every machine watching already glides
    /// toward each direction it is sent, so easing it here as well would only make the creature turn late.</para></summary>
    void TurnTheBodyItself()
    {
        Vector3 p = transform.position;
        if (!serverPosSeeded) { serverLastPos = p; serverPosSeeded = true; }
        Vector3 d = p - serverLastPos; d.y = 0f;
        serverLastPos = p;

        bool walking = Travelling(d, Mathf.Max(Time.deltaTime, 1e-4f), out _);
        Vector3 face = FacingThisFrame(d, walking, p);
        face.y = 0f;
        if (face.sqrMagnitude <= 1e-8f) return;

        transform.rotation = Quaternion.LookRotation(face.normalized);
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
