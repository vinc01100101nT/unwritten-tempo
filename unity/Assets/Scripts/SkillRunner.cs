using UnityEngine;

/// <summary>
/// The runtime interpreter for a data-authored <see cref="SkillDef"/> — the ONE component that turns any composed
/// skill into a playable hotbar active, replacing a hand-written C# skill class per spell. It plays the def's
/// <see cref="VfxSequence"/> and, on each gameplay <see cref="VfxTick"/> the sequence raises, looks up the matching
/// <see cref="SkillTickBinding"/> and applies its effects through <see cref="SkillEffectApplier"/> — the shared
/// combat path. The FINAL numbers are rolled off the live caster stats there, so this class stores no damage; the
/// asset only says WHAT each tick does; the power scales off the live caster stats (the ring), NOT a skill level.
///
/// <para>Attach one per skill on the player (a build tool does this). It implements <see cref="IHotbarSkill"/> so
/// the bar shows it automatically. Aim mode (<see cref="SkillDef.aim"/>) decides how the cast point is picked.</para>
/// </summary>
public class SkillRunner : MonoBehaviour, IHotbarSkill
{
    [Tooltip("The authored skill this runner plays. Set by the build tool or in the inspector.")]
    public SkillDef def;

    float nextUse, lastDuration;
    // WHICH authored trigger started the run in progress — set at every entry point (a press, a combo step, a passive
    // fire) and read by everything that must differ between them: which state gate applies, what the run spends, and
    // whether finishing it announces "the player cast this". Never null while a run is resolving.
    SkillTriggerEntry firedBy;
    float comboDamageMult = 1f;         // ×ramp for the CURRENT cast when fired from a combo chain (1 = solo). Set per
                                        // combo step; solo Cast/CastTargeted reset it to 1 so a normal cast is unscaled.
    Coroutine castRoutine;              // active cast wind-up (null = not casting); blocks re-cast + drives the CastBar
    VfxSequenceRunner castAura;         // the global OnCastStart aura looping for this wind-up; MUST be Stopped at every exit
    Vfx.EffectHandle castCircle;        // the per-skill cast circle (def.castCircleId) for this wind-up; MUST be Stopped at every exit
    VfxContext lastCtx;                 // captured per cast so OnTick can read the Single-scope target unit
    System.Action<VfxTick> onTick;      // cached hook (no per-play delegate alloc)
    System.Action<Vector3> onGround;    // cached ground-confirm hook
    System.Action<Transform> onUnit;    // cached single-target confirm hook
    Transform pendingUnit;              // stage-1 pick, held while stage 2 (the point) is being aimed. A field, not a
                                        // closure, so the confirm hooks above stay cached (no per-cast delegate alloc).
                                        // Cleared on every Activate and on teardown — a targeter's Cancel never calls back.
    System.Func<string, float> stayFor; // cached per-tickId Stay Duration resolver (built once; no per-cast alloc)
    // Resolve a looping step's Stay Duration from its tick binding (by tickId). Handed to the VFX context so the
    // runner supplies each step its own stay; the VFX never stores this per-use gameplay number.
    System.Func<string, float> StayFor => stayFor ??= tid => { var b = def.BindingFor(tid); return b != null ? b.stayDuration : 0f; };
    Player3DController body;            // caster's mover — turned to face the cast point on release
    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);

    // ---- state gate + no-self-feeding chain -----------------------------------------------------------------------
    // castChain = the causal chain this cast belongs to (0 for a player-pressed active — a press starts a fresh chain).
    // A passive receives its chain from the dispatch and stamps it onto everything its effects cause, so the brake can
    // see "this passive is already upstream of me" no matter how many frames later the damage lands.
    ulong castChain;
    bool consumedThisCast;              // the gate spends ONCE per cast, right after the effects have computed
    // The required state's stack count SNAPSHOT at cast start — taken before anything is spent, and reused by every
    // tick of the cast. A live re-read would pay the first tick off the full pile and the rest off an empty one, and
    // would also make a multi-tick finisher's damage depend on tick ORDER. (Locked order: snapshot → compute → consume.)
    int castStacks;

    /// <summary>The caster's unified state store — the ONE store the gate reads and spends from.</summary>
    public UnitStates CasterStates => Character.Instance != null ? Character.Instance.States : null;

    void Awake()
    {
        onTick = OnTick;
        onGround = Cast;
        onUnit = CastTargeted;
        body = GetComponent<Player3DController>();
    }

    // Every runner registers with the ONE dispatch (PassiveEvents) while it lives on the player — so a runner exists ⇔
    // the player owns that skill, no separate list to hand-sync. We register unconditionally (not just passives)
    // because `def` may be assigned AFTER OnEnable (runtime AddComponent then set-def); PassiveEvents.Fire filters to
    // the matching passive at dispatch, so a registered active is simply never matched. Cheap: the list is tiny.
    void OnEnable()
    {
        PassiveEvents.Register(this);
    }

    // If we're torn down mid-cast (disabled, scene swap, death), Unity stops the coroutine but won't run its cleanup,
    // so drop the shared bar + kill the looping aura here or they'd hang on screen. Also drop any passive registration.
    void OnDisable()
    {
        PassiveEvents.Unregister(this);
        pendingUnit = null;   // a half-aimed cast dies with the runner; never resume stage 2 against a stale pick
        if (castRoutine != null || castAura != null || castCircle != null) EndCast();
    }

    // ---- activation ---------------------------------------------------------

    public bool Activate()
    {
        if (def == null || def.vfx == null) return false;
        if (def.IsPassive) return false;   // no pressed entry — this skill only ever fires via PassiveEvents on its triggers
        firedBy = def.CastEntry;           // a press always runs the OnCast entry, whatever other triggers this skill has
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        // A skill committed to a combo chain is spent there — it can't be cast solo from the hotbar (the chain owns
        // it, and its cadence is the chain's shared cooldown). Character.Combos is the single owner of "who's committed".
        if (Character.Instance.Combos.IsCommitted(def.id))
        { BattleNotice.Warn("In a combo chain"); return false; }
        if (StatusController.Player != null && StatusController.Player.Silenced)
        { BattleNotice.Warn("Silenced", new Color(0.7f, 0.5f, 1f)); return false; }
        if (StatusController.Player != null && StatusController.Player.CannotStartSkill) return false;   // stunned / frozen / casting / mid-aftercast / mid-jump — can't start another skill
        if (castRoutine != null) return false;   // already winding up a cast — ignore until it resolves
        if (Time.time < nextUse) return false;
        // Ultimate: refuse (with a notice) before entering the reticle if the shared meter isn't charged — MP/charge
        // are only spent on confirm, so this early-out costs nothing.
        if (def.IsUltimate && !Character.Instance.UltReady(def.chargeCost))
        { BattleNotice.Warn("Not charged"); return false; }

        // The aim is a SEQUENCE derived from the ticks (SkillDef.NeedsUnitPick / NeedsAimPoint), never an authored
        // field: pick an entity (only when target effects need one), then pick a point (only when a ground tick or a
        // relocating effect needs one). Either stage can be absent — a self-dash asks only for the point, a plain nuke
        // only for the entity, "dash the mob I clicked over there" asks for both, in that order.
        // A re-press mid-aim cancels the whole sequence, including a stage-1 pick already banked.
        if (UnitTargeter.Active) { UnitTargeter.Cancel(); pendingUnit = null; return false; }
        if (GroundTargeter.Active) { GroundTargeter.Cancel(); pendingUnit = null; return false; }
        pendingUnit = null;

        if (def.NeedsUnitPick)
        {
            // Stage 1: land on ONE valid unit, validated against the union of the single-target ticks' affect kinds
            // (warns in-game on an illegal / absent pick).
            var mask = def.SingleTargetMask;
            if (mask == SkillTargets.None)
            { BattleNotice.Warn("No valid target"); return false; }
            UnitTargeter.Begin(transform, def.castRange, mask, onUnit);
            return true;   // MP/cooldown spent on the FINAL confirm, so cancelling any stage costs nothing
        }
        if (def.NeedsAimPoint) { BeginPointAim(); return true; }

        CastResolved(null, transform.position);   // no aim at all (a pure self buff)
        return true;
    }

    // Stage 2 of the aim. Entered straight from Activate (no entity to pick) or from the stage-1 confirm below.
    void BeginPointAim() =>
        GroundTargeter.Begin(transform, def.castRange, onGround,
                             def.castCircleId, def.castCircleTint, def.LargestAreaRadius, def.IsProjectileAim);

    // Stage-1 confirm: bank the entity, then either ask for the point or fire straight away.
    void CastTargeted(Transform unit)
    {
        if (unit == null) return;
        if (def.NeedsAimPoint) { pendingUnit = unit; BeginPointAim(); return; }
        CastResolved(unit, unit.position);
    }

    // Stage-2 confirm (also the only confirm for a point-only cast): fire with whatever stage 1 banked.
    void Cast(Vector3 point)
    {
        var unit = pendingUnit; pendingUnit = null;
        CastResolved(unit, point);
    }

    // The ONE solo-cast resolve: spend, gate cooldown, play the sequence. `unit` = the stage-1 pick (null when the
    // cast asked for no entity), `point` = the stage-2 point (the caster's own spot when it asked for none).
    void CastResolved(Transform unit, Vector3 point)
    {
        if (!Spend()) return;
        comboDamageMult = 1f;               // solo cast — no combo ramp
        lastCtx = BuildCtx(unit, point);
        StartCastOrPlay();
    }

    // The ONE place a cast's VfxContext is built — solo, combo and passive all come through here so no path can
    // forget a field (a missed effectPoint would silently dash the caster to the world origin).
    //   origin = the CASTER (Origin anchor).
    //   target = where the VFX plays (Target anchor): the picked entity if there is one, else the aimed point. A
    //            self cast collapses both onto the caster; a ground cast keeps them apart, so an Origin-anchored step
    //            (Blink's departure flash) plays at the caster's spot, not the destination.
    //   effectPoint = where a relocating effect SENDS what it caught — a separate question from where the VFX plays.
    //   follow = the CASTER so an Attach step (a self aura / dash trail) RIDES the player and fires its gameplay
    //            tick, like VfxCue sets follow = actor. PlayAt/Carrier steps ignore follow, so ground AoEs are unaffected.
    VfxContext BuildCtx(Transform unit, Vector3 point) => new VfxContext
    {
        origin = transform.position,
        target = unit != null ? unit.position : point,
        effectPoint = point,
        follow = transform,
        targetUnit = unit,
        scaleMult = 1f,
        stayForTick = StayFor,
    };

    // ---- combo-chain firing -------------------------------------------------
    // A ComboRunner drives a chain by firing each member skill here at ONE pre-resolved aim (no per-skill reticle),
    // bypassing the per-skill cooldown (the chain owns cadence via its shared cooldown) while STILL paying mana and
    // respecting hard-CC / silence. `dmgMult` is the slot's position ramp (1×/1.5×/2.5×/4×) applied to this cast's hits.

    /// <summary>Fire this skill from a combo at a ground point (Ground/Self members). Returns true if it fired.</summary>
    public bool ComboCastGround(Vector3 center, float dmgMult)
    {
        if (!ComboSpend()) return false;
        comboDamageMult = Mathf.Max(0f, dmgMult);
        // A combo resolves ONE aim for the whole chain, so a relocating member sends its catch to that same spot.
        lastCtx = BuildCtx(null, center);
        StartCastOrPlay();
        return true;
    }

    /// <summary>Fire this skill from a combo onto a chosen unit (Single/Target members). Returns true if it fired.
    /// A chain pre-resolves ONE aim, so there's no second stage here: a relocating member sends its catch to the
    /// picked unit's spot rather than asking the player for a separate point mid-chain.</summary>
    public bool ComboCastUnit(Transform unit, float dmgMult)
    {
        if (unit == null || !ComboSpend()) return false;
        comboDamageMult = Mathf.Max(0f, dmgMult);
        lastCtx = BuildCtx(unit, unit.position);
        StartCastOrPlay();
        return true;
    }

    // Combo spend: mana only — NO per-skill cooldown gate (the chain's shared cooldown is the real gate) and it does
    // not touch nextUse. Still refuses under death / hard-CC / silence, and warns + fails on empty MP (fizzling the chain).
    bool ComboSpend()
    {
        if (def == null || def.vfx == null || Character.Instance == null || Character.Instance.IsDead) return false;
        firedBy = def.CastEntry;   // a combo step is still the player casting this skill, just without its own reticle
        var sc = StatusController.Player;
        if (sc != null && (sc.CannotAct || sc.Silenced)) return false;
        if (!Character.Instance.SpendMana(def.manaCost)) { BattleNotice.Warn("No MP", NoManaColor); return false; }
        return true;
    }

    // The single fork between an instant skill and one with a wind-up. A castTime of 0 fires immediately — NO cast
    // bar and NO OnCastStart cue (the cue marks a real wind-up). Otherwise a coroutine winds up first, then plays.
    void StartCastOrPlay()
    {
        // Turn the caster to face where it aimed — for every skill regardless of scope (ground/target/aoe).
        // A self-cast aims at the caster's own feet, so FaceWorldPoint no-ops (planar dir ~0): no exception needed.
        if (body != null) body.FaceWorldPoint(lastCtx.target);

        if (def.castTime > 0f && isActiveAndEnabled)
            castRoutine = StartCoroutine(CastWindup());
        else
            PlayCycles();
    }

    // Wind up for castTime × the caster's DEX-driven CastTimeMult (instacast at 150 DEX), filling the shared CastBar
    // over the caster's head each frame. The OnCastStart cue is Begun as a PERSISTENT aura that loops for the whole
    // wind-up and is Stopped the instant it ends (complete or interrupt) — so its cue sequence should be authored as an
    // Attach/looping effect (a one-shot bound there would just flash). Interrupted by death / silence / hard-CC — MP is
    // already spent (RO-style, an interrupted cast is a wasted cast). No per-frame alloc: the CastBar is a cached
    // singleton and the label is a stable reference (it only rewrites the Text on change).
    System.Collections.IEnumerator CastWindup()
    {
        float mult = Character.Instance != null ? Character.Instance.CastTimeMult : 1f;
        float dur = def.castTime * mult;

        if (dur > 0f)
        {
            BeginCastVisuals();
            if (StanceController.Player != null) StanceController.Player.Push(StanceId.Casting, dur);   // hold the cast pose, stretched to span the whole wind-up (FitWindow)
            var sc = StatusController.Player;
            if (sc != null) sc.LockActions(dur);   // fully root for the wind-up (no move/attack/other skills); freed in EndCast on complete or interrupt
            string label = def.displayName;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                if (Character.Instance == null || Character.Instance.IsDead ||
                    (sc != null && (sc.Silenced || sc.CannotAct)))
                { EndCast(); yield break; }
                CastBar.Set(UnitAnchor.Head(transform), t / dur, label, def.tint);
                yield return null;
            }
        }

        EndCast();
        PlayCycles();
    }

    // The ONE cast-SETUP choke point (paired with EndCast): begin BOTH wind-up visuals together.
    //   • OnCastStart aura — on the CASTER (body charge-up glow), the global cue authored in the Vfx Cue Composer.
    //   • Cast circle — on the GROUND at the impact point (lastCtx.target), the per-skill effect from the Skill
    //     Composer. For a ground skill it lands exactly where the aim reticle was (one continuous ring); for a Self
    //     cast the target IS the caster's feet, so the offset is ~0. The caster is rooted here, so the offset holds.
    // They are INDEPENDENT (two anchors, both optional) but share ONE lifecycle — held for the wind-up, stopped
    // together in EndCast. Never the release sequence.
    void BeginCastVisuals()
    {
        castAura = VfxCue.Begin(VfxCueId.OnCastStart, transform);
        if (!string.IsNullOrEmpty(def.castCircleId))
            castCircle = Vfx.AttachGroundCircle(def.castCircleId, transform, lastCtx.target - transform.position,
                                                def.LargestAreaRadius, def.castCircleTint);
    }

    // The ONE cast-teardown choke point: stop the looping aura + circle, hide the bar, clear the casting guard.
    // Called on completion, on interrupt, and on OnDisable — so neither visual can outlive its wind-up.
    void EndCast()
    {
        if (castAura != null) { castAura.Stop(); castAura = null; }
        if (castCircle != null) { castCircle.Stop(); castCircle = null; }
        if (StanceController.Player != null) StanceController.Player.Pop(StanceId.Casting);   // release the cast pose (complete/interrupt/OnDisable)
        if (StatusController.Player != null) StatusController.Player.Unlock();   // free the wind-up root; on completion PlayCycles re-locks for the aftercast beat
        CastBar.Hide();
        castRoutine = null;
    }

    // Play the composed VFX once, then (if the def asks for a shower) replay the WHOLE cycle repeatCount times,
    // repeatInterval apart, at the SAME cast-time context — carrierScatter spreads the individual falls. Repetition
    // of the whole effect is owned HERE (gameplay), not the Vfx Composer (which stays WYSIWYG: one cycle = one fall).
    void PlayCycles()
    {
        consumedThisCast = false;   // this cast hasn't paid its state cost yet — it pays once, after its effects compute
        castStacks = StateGate.StackCount(firedBy, CasterStates);   // the pile this cast is about to cash, read BEFORE spending it
        bool pressed = firedBy != null && firedBy.trigger == SkillTrigger.OnCast;
        if (pressed) castChain = 0UL;   // a player-pressed cast starts a FRESH chain (nothing caused it)

        // The cast finished UNINTERRUPTED (an interrupt runs EndCast + bails before here) — announce it so any passive
        // keyed to this skill via OnSkillCastComplete<skill> fires.
        //
        // "That skill finished casting" means THE PLAYER CAST IT. A run of this same skill started by one of its own
        // non-OnCast triggers does NOT announce, because the granter of the charge it just spent is usually keyed to
        // this very skill: Lock Blades grants charges when Lunge completes, an auto-fired Lunge would re-announce, and
        // the pile would refill itself forever. Keying off the trigger that STARTED this run — not off whether the
        // skill has a pressed form — makes that impossible for every skill, not just this pair.
        if (pressed)
            PassiveEvents.Fire(SkillTrigger.OnSkillCastComplete, def.id);

        // The RELEASE moment: any cast wind-up (and its Casting pose) has already finished, so the release cue plays
        // now — casting first, release second. Instant skills reach here immediately.
        if (def.hasReleaseStance && StanceController.Player != null)
        {
            // Scale the release beat by the SAME attack-speed motion scale swings use (1 / AttackSpeedMult): a faster
            // caster plays the release animation faster AND is rooted for less time — one number drives both.
            float releaseDur = StanceController.Player.PlayActionFor(def.releaseStance, 0f, Player3DAttacker.MotionScale);
            // RO-style aftercast: fully root the caster for the (attack-speed-scaled) release animation's length.
            // Stacks on top of the skill's own cooldown; a skill with no release stance roots for nothing.
            if (StatusController.Player != null) StatusController.Player.LockActions(releaseDur);
        }

        // A projectile aims a DIRECTION, not a landing spot: the shot flies a FIXED travel distance (the reused
        // castRange = "travel range"), NOT to the cursor. Project the aim onto that range so the Carrier flies the
        // full distance and its spread-ticks hit along the whole path. Fan copies inherit this (PlayFan spreads at the
        // projected radius); repeat cycles inherit it via lastCtx.
        if (def.IsProjectileAim) lastCtx.target = ProjectToTravelRange(lastCtx.target);

        // A projectile skill fans N copies of its ONE composed projectile around the aim direction (authored skill-side,
        // like repeat). Non-projectile skills, or count 1, just play once.
        int fan = def.IsProjectileAim ? Mathf.Max(1, def.projectileCount) : 1;
        if (fan <= 1) VfxDirector.Play(def.vfx, lastCtx, onTick);
        else PlayFan(fan, def.projectileSpread);

        int extra = Mathf.Max(0, def.repeatCount - 1);
        if (extra > 0 && isActiveAndEnabled) StartCoroutine(PlayExtraCycles(extra, lastCtx));
    }

    // Push the aim point out to the skill's travel range (reused castRange) along the aim direction — a projectile
    // flies the full distance regardless of where the cursor sits. Falls back to the caster's facing if the cursor is
    // right on top of the caster, and to the raw aim distance if no range is set.
    Vector3 ProjectToTravelRange(Vector3 aim)
    {
        Vector3 me = transform.position;
        Vector3 dir = aim - me; dir.y = 0f;
        float d = dir.magnitude;
        Vector3 nrm = d < 0.05f ? new Vector3(transform.forward.x, 0f, transform.forward.z).normalized : dir / d;
        float range = def.castRange > 0f ? def.castRange : d;
        return new Vector3(me.x + nrm.x * range, aim.y, me.z + nrm.z * range);
    }

    // Play the VFX N times at once, each aimed along a different cone direction centred on the original aim — so one
    // composed projectile becomes a fanned volley. Each copy's Carrier flies its own line and spread-ticks along it.
    void PlayFan(int n, float spreadDeg)
    {
        Vector3 me = transform.position;
        Vector3 baseDir = lastCtx.target - me; baseDir.y = 0f;
        float dist = baseDir.magnitude;
        if (dist < 0.05f) { VfxDirector.Play(def.vfx, lastCtx, onTick); return; }
        Vector3 nrm = baseDir / dist;
        float half = Mathf.Max(0f, spreadDeg) * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float ang = n <= 1 ? 0f : Mathf.Lerp(-half, half, i / (float)(n - 1));
            Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * nrm;
            var c = lastCtx;                 // struct copy — rotate only this shot's aim point
            c.target = me + dir * dist;
            VfxDirector.Play(def.vfx, c, onTick);
        }
    }

    System.Collections.IEnumerator PlayExtraCycles(int extra, VfxContext ctx)
    {
        float interval = def.repeatInterval > 0f ? def.repeatInterval
                       : (def.vfx != null && def.vfx.RestartInterval > 0f ? def.vfx.RestartInterval : 0.35f);
        for (int i = 0; i < extra; i++)
        {
            // manual timer, not `new WaitForSeconds`: this coroutine starts per cast and the interval varies per skill
            for (float t = 0f; t < interval; t += Time.deltaTime) yield return null;
            VfxDirector.Play(def.vfx, ctx, onTick);
        }
    }

    bool Spend()
    {
        if (Character.Instance == null || Character.Instance.IsDead || Time.time < nextUse) return false;
        // The combo gate: a skill that requires a state doesn't run at all when unmet — checked BEFORE any cost is paid.
        // A press is always gated by the OnCast entry; the skill's other triggers have their own, separate gates.
        if (!StateGate.IsMet(def.CastEntry, CasterStates)) { BattleNotice.Warn("Not ready"); return false; }
        // Ultimate gates on the shared charge meter (checked again here — the reticle may have taken time to confirm).
        if (def.IsUltimate && !Character.Instance.UltReady(def.chargeCost))
        { BattleNotice.Warn("Not charged"); return false; }
        if (!Character.Instance.SpendMana(def.manaCost))
        {
            BattleNotice.Warn("No MP", NoManaColor);
            return false;
        }
        if (def.IsUltimate) Character.Instance.SpendUltCharge(def.chargeCost);   // ults SPEND charge…
        else Character.Instance.AddUltCharge(Character.UltChargePerCast);        // …every other cast BUILDS it
        lastDuration = def.cooldown;
        nextUse = Time.time + lastDuration;
        return true;
    }

    // The heart of the code/data split: each tick the sequence raises, resolve its binding and apply — through the
    // ONE shared combat path. Unbound ticks (pure-visual beats) are ignored. Skills DON'T level — power comes from
    // the ring (affinity), so there's no per-level extra to add here (extraMult/extraFlat pass 0).
    void OnTick(VfxTick tick)
    {
        var b = def.BindingFor(tick.tickId);
        if (b == null) return;

        // Map the consolidated per-tick targeting onto the shared burst params (scope/targets/radius). The applier
        // already routes each effect kind to the right sink per WHO is gathered, so nothing downstream changes.
        SkillScope scope; SkillTargets targets; float radius = b.radius;
        switch (b.targetMode)
        {
            case TargetMode.Self:
                scope = SkillScope.Area; targets = SkillTargets.Player; break;
            case TargetMode.SingleTarget:
                scope = SkillScope.Single; targets = b.affects; break;
            default: // Ground
                scope = SkillScope.Area; targets = b.affects;
                // Point = the caster acts AT the aimed spot (Dash goes there; an accompanying Heal lands on the caster).
                if (b.groundDelivery == GroundDelivery.Point) targets = SkillTargets.Player;
                // Projectile = the flying VFX step (authored in the VFX composer) raises its SPREAD TICKS along its
                // path; each tick lands here as an AoE gather at the tick's point — hit detection is DERIVED from the
                // ticks, not a hard-coded scan. radius = the projectile's hit width.
                break;
        }
        // Stamp this cast's causal chain for the whole burst, so any trigger the damage raises (OnHit / OnKill fire
        // synchronously inside the applier) can see which passives are already upstream and refuse to re-fire them.
        ulong prevChain = PassiveEvents.PushChain(castChain);
        try
        {
            // The stack payoff rides the burst's EXISTING extra-multiplier input (it adds to each effect's own
            // damageMult), so cashing a pile can't become a second damage path beside the normal roll.
            SkillEffectApplier.ApplyBurst(tick.point, radius, scope, targets, b.effects,
                                          lastCtx.targetUnit, firedBy != null ? firedBy.StackDamageBonus(castStacks) : 0f, 0, comboDamageMult,
                                          def.icon, def.displayName, def.id,   // def.id KEYS the pile: stacks belong to the skill that granted them
                                          aimPoint: lastCtx.effectPoint,   // Dash/Teleport go where stage 2 aimed, not to the tick's VFX anchor
                                          caster: Character.Instance); // explicit caster (local player today; a per-connection entity once server-authoritative)
        }
        finally { PassiveEvents.PopChain(prevChain); }

        // COMPUTE BEFORE CONSUME: the effects above read the LIVE store (Deathblow sees the whole Momentum pile while
        // rolling its damage); only now is the cost paid. Once per cast, not per tick.
        if (!consumedThisCast)
        {
            consumedThisCast = true;
            StateGate.Consume(firedBy, CasterStates);   // the entry that STARTED this run pays — a press and an auto-fire cost different things
        }
    }

    // ---- passive firing -----------------------------------------------------

    /// <summary>Fire this skill through one of its AUTOMATIC trigger entries — called by <see cref="PassiveEvents"/>
    /// when <paramref name="entry"/>'s moment happens and its gate is met. The skill body is identical to the pressed
    /// one (same VFX, ticks, effects); only the way it STARTED differs, which is why the entry is carried in rather
    /// than re-derived. So it runs the same cast path (<see cref="PlayCycles"/> → VFX → ticks → applier), with the aim
    /// resolved from the trigger context instead of a reticle:
    /// <list type="bullet">
    /// <item>Single-target skill (<see cref="CastReticle.Unit"/>) → lands on the triggering <paramref name="victim"/>
    /// entity (skipped if there's none this moment).</item>
    /// <item>Ground / AoE / projectile skill (<see cref="CastReticle.Ground"/>) → casts AT the victim's location (or the
    /// caster's spot if the moment has no victim, e.g. OnLowHP).</item>
    /// <item>SELF skill (<see cref="CastReticle.Instant"/>) → ALWAYS casts on the caster; the victim is ignored (a self
    /// buff must never be flung onto the mob).</item>
    /// </list>
    /// No mana / cooldown / wind-up — the trigger IS the gesture. It's a real cast, so its cost/cadence knobs stay ignored.</summary>
    public void FireAsPassive(SkillTriggerEntry entry, Transform victim, ulong chain = 0UL)
    {
        if (def == null || def.vfx == null || entry == null || Character.Instance == null || Character.Instance.IsDead) return;
        firedBy = entry;        // this run's gate, its stack cost, and "was this a player cast" all read from here
        comboDamageMult = 1f;   // passive fire — no combo ramp
        castChain = chain;      // this passive is in the chain; everything its effects cause carries that forward

        // Same two stages as a manual cast, answered by the trigger instead of by clicks: the entity is the victim,
        // the point is wherever the moment happened (the caster's own spot when the moment had no victim).
        Transform unit = null;
        if (def.NeedsUnitPick)
        {
            if (victim == null) return;               // single-target — needs an entity to land on
            unit = victim;
        }
        Vector3 point = def.NeedsAimPoint && victim != null ? victim.position : transform.position;

        lastCtx = BuildCtx(unit, point);
        if (body != null) body.FaceWorldPoint(lastCtx.target);   // face the auto-target, like a manual cast
        PlayCycles();                                 // the full active path: VFX + ticks + effects
    }

    // ---- IHotbarSkill -------------------------------------------------------
    public int SlotOrder => def != null ? def.slotOrder : 100;
    public string ShortLabel => def != null ? def.shortLabel : "Skill";
    public string KeyLabel => def != null ? def.keyLabel : "";
    public Sprite Icon => def != null ? def.icon : null;
    public int ManaCost => def != null ? def.manaCost : 0;
    public bool Affordable => Character.Instance != null && def != null && Character.Instance.CurrentMP >= def.manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => def != null ? def.tint : Color.white;
}
