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
    float comboDamageMult = 1f;         // ×ramp for the CURRENT cast when fired from a combo chain (1 = solo). Set per
                                        // combo step; solo Cast/CastTargeted reset it to 1 so a normal cast is unscaled.
    Coroutine castRoutine;              // active cast wind-up (null = not casting); blocks re-cast + drives the CastBar
    VfxSequenceRunner castAura;         // the global OnCastStart aura looping for this wind-up; MUST be Stopped at every exit
    Vfx.EffectHandle castCircle;        // the per-skill cast circle (def.castCircleId) for this wind-up; MUST be Stopped at every exit
    VfxContext lastCtx;                 // captured per cast so OnTick can read the Single-scope target unit
    System.Action<VfxTick> onTick;      // cached hook (no per-play delegate alloc)
    System.Action<Vector3> onGround;    // cached ground-confirm hook
    System.Action<Transform> onUnit;    // cached single-target confirm hook
    System.Func<string, float> stayFor; // cached per-tickId Stay Duration resolver (built once; no per-cast alloc)
    // Resolve a looping step's Stay Duration from its tick binding (by tickId). Handed to the VFX context so the
    // runner supplies each step its own stay; the VFX never stores this per-use gameplay number.
    System.Func<string, float> StayFor => stayFor ??= tid => { var b = def.BindingFor(tid); return b != null ? b.stayDuration : 0f; };
    Player3DController body;            // caster's mover — turned to face the cast point on release
    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);

    void Awake()
    {
        onTick = OnTick;
        onGround = Cast;
        onUnit = CastTargeted;
        body = GetComponent<Player3DController>();
    }

    // If we're torn down mid-cast (disabled, scene swap, death), Unity stops the coroutine but won't run its cleanup,
    // so drop the shared bar + kill the looping aura here or they'd hang on screen.
    void OnDisable()
    {
        if (castRoutine != null || castAura != null || castCircle != null) EndCast();
    }

    // ---- activation ---------------------------------------------------------

    public bool Activate()
    {
        if (def == null || def.vfx == null) return false;
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

        // The cast reticle is DERIVED from the ticks (def.Reticle), not a separate aim field: any Single-target tick ⇒
        // pick a unit; else any Ground tick ⇒ a ground reticle; else Instant (self / no aim).
        switch (def.Reticle)
        {
            case CastReticle.Unit:
                // Single-target: land on ONE valid unit, validated against the union of the single-target ticks'
                // affect kinds (warns in-game on an illegal / absent pick).
                var mask = def.SingleTargetMask;
                if (mask == SkillTargets.None)
                { BattleNotice.Warn("No valid target"); return false; }
                if (UnitTargeter.Active) { UnitTargeter.Cancel(); return false; }   // re-press cancels aiming
                UnitTargeter.Begin(transform, def.castRange, mask, onUnit);
                return true;   // MP/cooldown spent on confirm (CastTargeted), so cancelling costs nothing

            case CastReticle.Ground:
                if (GroundTargeter.Active) { GroundTargeter.Cancel(); return false; }   // re-press cancels aiming
                GroundTargeter.Begin(transform, def.castRange, onGround,
                                     def.castCircleId, def.castCircleTint, def.LargestAreaRadius, def.IsProjectileAim);
                return true;   // MP/cooldown are spent on confirm (Cast), so cancelling costs nothing

            default: // Instant (self / no aim)
                Cast(transform.position);
                return true;
        }
    }

    // Ground/Self confirm: spend, gate cooldown, play the sequence centred on `center`.
    void Cast(Vector3 center)
    {
        if (!Spend()) return;
        comboDamageMult = 1f;               // solo cast — no combo ramp
        // origin = the CASTER (Origin anchor), target = the aimed point (Target anchor). Self cast: center IS the
        // caster, so they coincide. Ground cast: they DIFFER, so an Origin-anchored step (e.g. Blink's departure
        // flash before the teleport) plays at the caster's spot, not the destination. VfxContext.At collapsed both.
        // follow = the CASTER so an Attach step (a self aura / dash trail) RIDES the player and fires its gameplay
        // tick — like VfxCue sets follow = actor. PlayAt/Carrier steps ignore follow, so ground AoEs are unaffected.
        lastCtx = new VfxContext { origin = transform.position, target = center, follow = transform, scaleMult = 1f, stayForTick = StayFor };
        StartCastOrPlay();
    }

    // Target confirm: origin = caster, target/single-unit = the chosen unit (a mob, or the player for a self-cast).
    void CastTargeted(Transform unit)
    {
        if (unit == null || !Spend()) return;
        comboDamageMult = 1f;               // solo cast — no combo ramp
        lastCtx = new VfxContext
        {
            origin = transform.position,
            target = unit.position,
            follow = transform,
            targetUnit = unit,
            scaleMult = 1f,
            stayForTick = StayFor,
        };
        StartCastOrPlay();
    }

    // ---- combo-chain firing -------------------------------------------------
    // A ComboRunner drives a chain by firing each member skill here at ONE pre-resolved aim (no per-skill reticle),
    // bypassing the per-skill cooldown (the chain owns cadence via its shared cooldown) while STILL paying mana and
    // respecting hard-CC / silence. `dmgMult` is the slot's position ramp (1×/1.5×/2.5×/4×) applied to this cast's hits.

    /// <summary>Fire this skill from a combo at a ground point (Ground/Self members). Returns true if it fired.</summary>
    public bool ComboCastGround(Vector3 center, float dmgMult)
    {
        if (!ComboSpend()) return false;
        comboDamageMult = Mathf.Max(0f, dmgMult);
        // origin = caster, target = aimed point (see Cast) — keeps Origin/Target distinct on a ground combo member.
        lastCtx = new VfxContext { origin = transform.position, target = center, follow = transform, scaleMult = 1f, stayForTick = StayFor };
        StartCastOrPlay();
        return true;
    }

    /// <summary>Fire this skill from a combo onto a chosen unit (Single/Target members). Returns true if it fired.</summary>
    public bool ComboCastUnit(Transform unit, float dmgMult)
    {
        if (unit == null || !ComboSpend()) return false;
        comboDamageMult = Mathf.Max(0f, dmgMult);
        lastCtx = new VfxContext
        {
            origin = transform.position,
            target = unit.position,
            follow = transform,
            targetUnit = unit,
            scaleMult = 1f,
            stayForTick = StayFor,
        };
        StartCastOrPlay();
        return true;
    }

    // Combo spend: mana only — NO per-skill cooldown gate (the chain's shared cooldown is the real gate) and it does
    // not touch nextUse. Still refuses under death / hard-CC / silence, and warns + fails on empty MP (fizzling the chain).
    bool ComboSpend()
    {
        if (def == null || def.vfx == null || Character.Instance == null || Character.Instance.IsDead) return false;
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
        SkillEffectApplier.ApplyBurst(tick.point, radius, scope, targets, b.effects,
                                      lastCtx.targetUnit, 0f, 0, comboDamageMult,
                                      def.icon, def.displayName,
                                      aimPoint: lastCtx.target,   // Teleport/Dash go to the AIM point, not the tick's VFX anchor
                                      caster: Character.Instance); // explicit caster (local player today; a per-connection entity once server-authoritative)
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
