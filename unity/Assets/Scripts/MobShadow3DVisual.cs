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

    /// <summary>WHICH BODY THIS SHADOW WEARS — the one written on its creature entry (<see cref="MonsterDef.modelName"/>,
    /// authored in the Mob Table). Empty on a machine that only WATCHES this creature until the server's name for it
    /// has arrived.
    ///
    /// <para>The server reads it off the creature entry as the creature is made and sends that name to every
    /// watcher, so no machine ever chooses a body of its own and the same creature wears the same body in front of
    /// everybody.</para></summary>
    public string ModelName { get; private set; }

    // The creature entry this shadow's outfit colours are read from — this machine's own copy of the same asset the
    // server made the creature from. Held rather than copied, so every time the colours are put on they are the
    // entry's colours as they are now, never a copy taken earlier.
    MonsterDef lookOf;

    // The colouring on the drawn body. Null until a model has been built, and never made on a machine with no screen.
    CharacterDye3D dye;

    /// <summary>Take this shadow's look from its creature entry. Called once by <see cref="MobSpawner"/>, the one door
    /// every creature is made through, on every machine. On the server it also names the body, before the creature
    /// is announced; a watching machine is told the body by the server instead (<see cref="WearModel"/>) and only
    /// keeps the entry for its colours.</summary>
    public void WearLookOf(MonsterDef def)
    {
        lookOf = def;
        if (GameServer.IsServer) ModelName = def != null ? def.modelName : null;
    }

    /// <summary>The server's word on which body this creature wears. Called by whatever is following this creature
    /// for the server, every time it takes this body on — when the body is first built, when the name lands after
    /// it, and when a spare body is handed back from the pool.
    ///
    /// <para>Calling it with nothing means the name has not arrived yet: the creature stays a sprite, which is a
    /// legal state it was already in, until it does. Calling it with the name the body already wears puts the
    /// colours on again, every time — a recycled body never keeps the colours it was last shown in.</para></summary>
    public void WearModel(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name == ModelName) { ShowLook(); return; }
        ModelName = name;
        BuildBody();
    }

    // Put this creature entry's outfit colours on the drawn body. The colours are stamped with the body they were
    // chosen on, and a body that is not that one shows its own plain colours (CharacterDye3D.ShowLook).
    void ShowLook()
    {
        if (dye != null) dye.ShowLook(LookOfEntry());
    }

    string LookOfEntry() => lookOf != null
        ? CharacterDye3D.PackLook(lookOf.modelName, lookOf.dyeSources, lookOf.dyeTargets)
        : "";

    protected override void Awake()
    {
        base.Awake();
        brain = GetComponent<MonsterAI3D>();
        health = GetComponent<Health>();
        status = GetComponent<StatusController>();
        // NO BODY IS CHOSEN HERE. It is written on the creature entry and handed over by WearLookOf, on the line
        // after this component is added and before the creature is announced.
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
        ReseatAfterMove();
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

        // A shadow has its OWN look, written on its creature entry. It used to CLONE the live player, which made
        // every shadow identical to you and to each other, and later drew a body at random, which gave a named
        // Kashira a different face every life.
        //
        // NOTHING TO WEAR YET means the server's name for this creature has not arrived: it stays a sprite until
        // WearModel brings it. Choosing one here instead is exactly the bug the name on the wire replaced.
        var spec = CharacterSpec.Active;
        string modelName = ModelName;
        if (string.IsNullOrWhiteSpace(modelName)) return;

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
        // The breathing and the bite write this root's scale and would deform the 3D model (a real swing clip replaces
        // the bite). The camera-facing turn is the one that matters most: it rewrites this object's rotation every
        // frame, so a live-3D creature watched by anybody faced the camera for its whole life instead of where the
        // server turned it. Removed rather than switched off, so the revive path — which switches a mob's parts back
        // on wholesale — cannot bring one back by mistake. The sprite is only hidden (see DropFlatPicture).
        MobBody.DropFlatPicture(gameObject, beforeItWakes: false);

        // --- Rebuild the root as a CHARACTER's root, not a sprite's. ---
        // THIS is what every earlier size fix got wrong. A billboard mob's root carries the SPRITE's authored scale
        // (e.g. 0.742) times the mob-size dial, and a CharacterController sized to match. A live-3D character needs
        // an UNSCALED root — exactly how Townsperson builds a townsperson, which is the only live-3D AI character in
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
        // ITS OUTFIT COLOURS, from its creature entry — the same colouring every player's body is drawn with.
        // perf-ok: once per body built; a pooled body keeps it for life and is re-coloured through WearModel.
        dye = CharacterDye3D.AddShowing(builtPose.gameObject, LookOfEntry());
        // THE WEAPON IT WEARS, IN ITS HAND — the same socket the player's body carries, reading this creature's worn
        // set instead of the player's. Only a drawn body gets one; the server builds no model to hold anything.
        // perf-ok: once per body built; a pooled body keeps it for life.
        var socket = builtPose.gameObject.AddComponent<WeaponSocket>();
        socket.ShowWeaponOf(GetComponent<UnitEquipment>());
        built = true;

        PointTheFreezeSwitchAt(builtPose.GetComponent<Animator>());
    }

    /// <summary>Everything only a CREATURE knows, and nothing else: which way its brain says to look, whether it is
    /// flinching, and whether it is dead. Two comparisons against state this creature already owns — nothing is
    /// polled and nothing allocates.
    ///
    /// <para>Every rule that turns these facts into a pose and a direction — death outranking a swing still in the
    /// air, a flinch only showing on a body that is standing still, which gear the legs churn at, how fast it
    /// turns — is the shared <see cref="CharacterBrain"/>, the same one the person at the keyboard runs. This type
    /// used to carry its own shorter copy of that, which is why a creature bit you side-on while walking.</para>
    ///
    /// <para>THE SWING IS NOT HERE, deliberately. It arrives as a beat — which action, the moment it began, and the
    /// window it must finish inside — through <see cref="Agent3DVisual.TellAction"/>, from the creature's own brain
    /// on the machine that owns it and from the server's announcement on every machine watching. That layer plays a
    /// beat that began BEFORE this machine started watching, joining it part-way through, which is something a
    /// per-frame description cannot express.</para></summary>
    protected override void DescribeSelf(ref CharacterBrain.Intent it)
    {
        // A COPY IS TOLD WHICH WAY IT FACES AND NEVER WORKS IT OUT, exactly as another person's watched body is: the
        // machine that owns the creature has already turned its body, that direction travels with its position, and
        // the body root here has already been turned to match.
        if (brain != null && !brain.ThinksForItself) it.facing = transform.forward;
        // THE BRAIN SAYS, AND THIS ONLY DRAWS IT — the identical arrangement the person at the keyboard has, where
        // their controller owns which way they are pointed and their visual reads it. A creature that is standing
        // and fighting names its target; anything else names nothing, and nothing means the line it travelled.
        else if (brain != null && brain.WorldFacing.sqrMagnitude > 1e-4f) it.facing = brain.WorldFacing;

        if (health != null && health.IsDead) it.dead = true;

        // FLINCH, from the ONE thing that decides it: the door every real hit passes through sets this, and it is
        // set on a watched copy too because the blow was announced to this machine. It used to be inferred here from
        // the health number dropping, which made two owners of one reaction.
        if (status != null && status.MovementFlinched) it.flinching = true;
        if (status != null) it.flinchCount = status.FlinchCount;
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
        ReseatAfterMove();
    }

}
