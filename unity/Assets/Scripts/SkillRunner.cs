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

        // Single-target skills must land on ONE valid unit — they can NEVER be ground-cast (a ground point is not a
        // target). This overrides the authored aim: enter the unified reticle, which validates the hovered unit's
        // KIND against the skill's target mask (and warns in-game on an illegal pick). The bindings are the single
        // source of truth for who a skill can hit, so targeting is derived from them, not a separate aim field.
        if (def.IsSingleTarget)
        {
            var mask = def.SingleTargetMask;
            if (mask == SkillTargets.None)
            { BattleNotice.Warn("No valid target"); return false; }
            if (UnitTargeter.Active) { UnitTargeter.Cancel(); return false; }   // re-press cancels aiming
            UnitTargeter.Begin(transform, def.castRange, mask, onUnit);
            return true;   // MP/cooldown spent on confirm (CastTargeted), so cancelling costs nothing
        }

        switch (def.aim)
        {
            case SkillAim.Ground:
                if (GroundTargeter.Active) { GroundTargeter.Cancel(); return false; }   // re-press cancels aiming
                GroundTargeter.Begin(transform, def.castRange, onGround,
                                     def.castCircleId, def.castCircleTint, def.LargestAreaRadius);
                return true;   // MP/cooldown are spent on confirm (Cast), so cancelling costs nothing

            case SkillAim.Target:
                var mob = NearestMob(transform.position, def.castRange);
                if (mob == null) return false;
                CastTargeted(mob.transform);
                return true;

            default: // Self
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
        lastCtx = new VfxContext { origin = transform.position, target = center, follow = null, scaleMult = 1f };
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
            follow = null,
            targetUnit = unit,
            scaleMult = 1f,
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
        lastCtx = new VfxContext { origin = transform.position, target = center, follow = null, scaleMult = 1f };
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
            follow = null,
            targetUnit = unit,
            scaleMult = 1f,
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
        if (Character.Instance.WeaponRefundPct > 0f && def.manaCost > 0)
            Character.Instance.GainMana(Mathf.RoundToInt(def.manaCost * Character.Instance.WeaponRefundPct));   // weapon Refund affix
        return true;
    }

    // The single fork between an instant skill and one with a wind-up. A castTime of 0 fires immediately — NO cast
    // bar and NO OnCastStart cue (the cue marks a real wind-up). Otherwise a coroutine winds up first, then plays.
    void StartCastOrPlay()
    {
        // Turn the caster to face where it aimed — for every skill regardless of scope (ground/target/aoe).
        // A self-cast aims at the caster's own feet, so FaceWorldPoint no-ops (planar dir ~0): no exception needed.
        if (body != null) body.FaceWorldPoint(lastCtx.target);

        // The ring's self-voices (Fortify / Leech / Accelerate) trigger on every cast, self-gated on investment.
        RingVoices.ApplyCast(Character.Instance);

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

        VfxDirector.Play(def.vfx, lastCtx, onTick);
        int extra = Mathf.Max(0, def.repeatCount - 1);
        if (extra > 0 && isActiveAndEnabled) StartCoroutine(PlayExtraCycles(extra, lastCtx));
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
        if (Character.Instance.WeaponRefundPct > 0f && def.manaCost > 0)
            Character.Instance.GainMana(Mathf.RoundToInt(def.manaCost * Character.Instance.WeaponRefundPct));   // weapon Refund affix
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
        // Fold the outgoing reshapers for this hit: weapon affixes first, then the ring's voices on top
        // (the design's fold order). Both self-gate, so this is a no-op struct on a plain build.
        CastMods mods = default;
        WeaponAffixes.AddOutgoing(Character.Instance, ref mods);
        RingVoices.AddOutgoing(Character.Instance, ref mods);
        SkillReshape.GateTo(b, ref mods);   // drop reshapers this binding can't use (e.g. Repeat on a non-damaging tick)
        SkillEffectApplier.ApplyBurst(tick.point, b.radius, b.scope, b.targets, b.effects,
                                      lastCtx.targetUnit, mods.rawDamageMult, 0, comboDamageMult,
                                      def.icon, def.displayName, mods,
                                      aimPoint: lastCtx.target);   // Teleport/Dash go to the AIM point, not the tick's VFX anchor
    }

    static MonsterAI3D NearestMob(Vector3 from, float maxRange)
    {
        var list = MonsterAI3D.Active;
        MonsterAI3D best = null; float bestSq = maxRange * maxRange;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            Vector3 d = ai.transform.position - from; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq <= bestSq) { bestSq = sq; best = ai; }
        }
        return best;
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
