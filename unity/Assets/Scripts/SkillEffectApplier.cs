using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONE place a data-authored <see cref="SkillEffect"/> (fired by a <see cref="SkillRunner"/> tick) becomes real
/// gameplay. It maps each intent onto the SAME combat sinks skills already call — a mob's <see cref="Health"/>, the
/// player's <see cref="Character"/>, the shared <see cref="Status"/> front door — so a data skill is just a new CALLER
/// of the existing damage/heal/status paths, never a second combat engine.
///
/// <para><b>Two passes per tick (caster side vs target side).</b> <see cref="SkillEffect.LandsOnCaster"/> partitions the
/// effect kinds. Pass 1 applies the CASTER effects (Dash / Guard / Buff / …) to the acting caster — once, regardless of
/// the tick's targeting, because "who does this tick hit" was never the question they answer. Pass 2 gathers the tick's
/// targets and applies the TARGET effects (Damage / Status / …) to them. No kind is in both passes, so a self-targeted
/// tick can't double-apply a caster effect and a mob-targeted tick can't swallow one.</para>
///
/// <para><b>Per-hit model (single-responsibility effects).</b> A binding is applied as a HIT LOOP whose length is the
/// Damage effect's <see cref="SkillEffect.hitCount"/> (1 if none). Each iteration is a "hit": the Damage effect lands
/// one strike, and every OTHER effect in the binding rolls its <see cref="SkillEffect.chance"/> THAT hit — a state, an
/// ailment, an execute, all on the same rule. Certain (chance 1, the default) simply means the roll always says yes.
/// So a stun-proc or execute never lives INSIDE the Damage effect; it's its own effect that triggers off the multi-hit.
/// Caster-side effects are applied once per landing, so their roll happens once no matter how many strikes there are.</para>
///
/// <para><b>Caster threading (network-readiness).</b> The acting caster is resolved ONCE at <see cref="ApplyBurst"/>
/// and threaded through the whole resolve, so no inner method reaches for the <c>Character.Instance</c> global. Today
/// the caster IS the local player; once combat is server-authoritative the net layer passes the acting connection's
/// entity and the entire burst scales off THAT entity's stats. Callers may omit it — it falls back to the local player.</para>
/// </summary>
public static class SkillEffectApplier
{
    // Seconds between the strikes of a multi-hit (hitCount > 1) flurry — the RO cascade timing.
    const float MultiHitInterval = 0.2f;
    // Reused snapshot of the mobs a burst reaches, so a multi-hit flurry can be handed to MultiHitBurst without a
    // per-cast scan alloc. Cleared at the start of each ApplyBurst; MultiHitBurst copies it before the caller reuses it.
    static readonly List<MonsterAI3D> burstTargets = new List<MonsterAI3D>(32);

    /// <summary>Apply the binding's effects — CASTER effects to the caster, TARGET effects to whoever the tick reaches
    /// (an AoE scan, or the cast's designated unit for Single). Zero-alloc: a squared-distance sweep over the small
    /// Active list, no physics / Find / GetComponent.
    /// <paramref name="caster"/> is the acting entity whose stats scale the roll (defaults to the local player).</summary>
    public static void ApplyBurst(Vector3 point, float radius, SkillScope scope, SkillTargets targets,
                                  List<SkillEffect> effects, Transform singleTarget,
                                  Sprite srcIcon = null, string srcName = null, string srcId = null,
                                  Vector3? aimPoint = null, Character caster = null, StackVars vars = default)
    {
        if (effects == null || effects.Count == 0) return;

        // Resolve the acting caster ONCE here (the local player today; a per-connection entity once server-authoritative),
        // so nothing downstream reaches for the Character.Instance global — the whole burst scales off THIS entity.
        var ch = caster ?? Character.Instance;

        // Where a relocation (Teleport / Dash) sends whoever it moves — the caster, or a mob when the author put the
        // effect on the target side: the AIM point, NOT the tick's VFX anchor point. The two differ once a ground cast
        // keeps origin(caster) ≠ target(aim), and a teleport must always go to the aimed spot regardless of which anchor
        // the VFX author chose for the visual. ONE destination per cast, read by both passes, so which side the author
        // pointed the effect at can't change where it lands. Defaults to point for old callers.
        Vector3 relocatePoint = aimPoint ?? point;

        // PASS 1 — CASTER effects (see SkillEffect.LandsOnCaster): they happen TO THE CASTER, so the tick's targeting has no say.
        // They run here exactly once, whatever the scope/affects are, and are excluded from the target pass below — so
        // "Single Target on a mob" can't swallow a Dash (the bug this split fixes), and a Self tick can't double-apply it.
        ApplyCasterEffects(effects, relocatePoint, singleTarget, srcIcon, srcName, srcId, ch, vars);

        // PASS 2 — TARGET effects: gather who the tick reaches, then run the hit loop on them.
        if (targets == SkillTargets.None) return;

        // SINGLE scope: ignore the radius, land straight on the cast's designated unit. Falls back to Area if unset.
        if (scope == SkillScope.Single && singleTarget != null)
        {
            var mob = singleTarget.GetComponentInParent<MonsterAI3D>();
            if (mob != null)
            {
                if ((targets & SkillTargets.Mobs) != 0 && mob.Health != null && !mob.Health.IsDead)
                {
                    burstTargets.Clear(); burstTargets.Add(mob);
                    DispatchMobHits(effects, burstTargets, ch, relocatePoint, srcIcon, srcName, srcId, vars);
                }
            }
            else if ((targets & SkillTargets.Player) != 0)
                ApplyEffectsToPlayer(effects, relocatePoint, srcIcon, srcName, srcId, ch, vars);
            return;
        }

        float r2 = Mathf.Max(0.05f, radius); r2 *= r2;

        if ((targets & SkillTargets.Mobs) != 0)
        {
            burstTargets.Clear();
            var list = MonsterAI3D.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var ai = list[i];
                if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
                Vector3 d = ai.transform.position - point; d.y = 0f;
                if (d.sqrMagnitude > r2) continue;
                burstTargets.Add(ai);
            }
            DispatchMobHits(effects, burstTargets, ch, relocatePoint, srcIcon, srcName, srcId, vars);
        }

        if ((targets & SkillTargets.Player) != 0)
        {
            // The player as a TARGET (a heal / a self-damage / an ailment on yourself) — deliberately NOT radius-gated:
            // no skill is a "hazard zone that hits the player if inside", and a ground cast's point is by definition
            // somewhere the caster isn't standing. Caster effects already ran in pass 1 and are not repeated here.
            if (ch != null && !ch.IsDead)
                ApplyEffectsToPlayer(effects, relocatePoint, srcIcon, srcName, srcId, ch, vars);
        }
    }

    // ---- where a relocation lands -------------------------------------------
    // How far from the target a relocation stops, as a fraction of the caster's melee reach — just INSIDE it, the same
    // "always in trading range" margin a melee mob holds at (MonsterAI3D's stop clamp), so the strict InRange check that
    // gates a swing passes on arrival. Player3DAttacker.EffectiveRange stays the single owner of the reach itself.
    const float LandingReachFactor = 0.9f;

    // How far around the target each successive landing moves, for a star of `corners` points: cross to the far side
    // (180°) plus a nudge of 180/corners. At 5 corners that's the original 216° — the spot sits 18° off your own line
    // to the target, so every hop passes within a tenth of a unit of the target and you cut straight THROUGH it.
    // `corners` hops then visit `corners` evenly-spaced bearings and close the loop, tracing a star on the ground
    // rather than walking the polygon's rim (5 × 216° = 1080°, three full laps; 7 × 205.71° = 1440°, four).
    //
    // ODD corner counts only: an even count doesn't come out to whole laps, so the figure never closes. The authored
    // value is forced odd rather than rejected — a shape that quietly fails to close is worse than one nudged by one.
    static float LandingStepDegrees(int corners)
    {
        int c = Mathf.Max(3, corners);
        if ((c & 1) == 0) c++;
        return 180f + 180f / c;
    }

    /// <summary>Where a RELOCATION (Dash / Teleport) actually LANDS. Aiming at a unit means "get to it", not "stand
    /// inside it": the mover arrives at ITS OWN melee reach, on the far side, rotated by <see cref="LandingStepDegrees"/>
    /// from wherever it currently stands — so it never ends up stacked on the target's spot, every dash reads as a cut
    /// THROUGH the target, and <paramref name="corners"/> in a row trace a star on the ground. Derived from the mover's
    /// CURRENT bearing rather than a stored step index: no sequence state to reset, an interrupted chain resumes from
    /// wherever it stopped. A pure ground aim — no unit given and no mob on the spot — is used verbatim.
    ///
    /// <para><paramref name="mover"/> is WHOEVER IS BEING MOVED, which is not always the local player: an effect
    /// authored on the TARGET side relocates a mob, and once combat is server-authoritative the caster is some other
    /// connection's entity. This used to read a static <c>Player3DAttacker</c> resolved from the local player, so both
    /// the reach and the bearing came from YOU no matter who was actually moving — teleporting a mob sent it to a spot
    /// computed from the player's position and the player's weapon range.</para></summary>
    static Vector3 RelocationDest(Vector3 aim, Transform unit, ICombatUnit mover, int corners)
    {
        Transform body = mover?.Body;
        if (body == null) return aim;

        float full = mover.MeleeReachFullSize;
        if (full <= 0f) return aim;   // reach unknown (avatar not spawned yet) — don't invent a landing spot

        // The thing we're landing next to: the cast's picked unit, else whatever mob the aim point is sitting on (a
        // Ground-delivered dash at a victim's feet aims at the mob's own position, with no unit pick to read).
        // Its SIZE matters: how close "just inside reach" is depends on both bodies, so resolve the target first and
        // blend against it. Using the mover's own pre-scaled reach here assumed the target was an ordinary sprite mob —
        // the same one-sided assumption that made the player and a shadow disagree about the identical pairing.
        Vector3 center;
        Transform targetT = unit;
        Health targetHealth = null;
        if (targetT == null)
        {
            // SEARCH radius assumes an ordinary sprite mob — this only asks "is a mob standing on the aimed spot", and
            // widening it would silently make ground-aimed dashes snap from further away than they used to.
            float searchR = full * UnitScale.MeleeBlend(mover.MeleeScale, UnitScale.MobScale) * LandingReachFactor;
            if (!MobAt(aim, searchR, out center, out targetHealth)) return aim;
        }
        else
        {
            center = targetT.position;
            targetHealth = targetT.GetComponentInParent<Health>();   // once per relocation cast, never per frame
        }

        // LANDING distance blends the mover against the body it actually found.
        float reach = full * UnitScale.MeleeBlend(mover.MeleeScale,
                                                  targetHealth != null ? targetHealth.MeleeScale : UnitScale.MobScale)
                           * LandingReachFactor;
        if (reach <= 0f) return aim;

        // Bearing of the MOVER as it stands now, measured around the target, then stepped. A mover standing exactly on
        // the target has no bearing to step from — fall back to its facing so the hop still has a direction.
        Vector3 from = body.position - center; from.y = 0f;
        if (from.sqrMagnitude < 1e-6f) from = body.forward;
        float a = Mathf.Atan2(from.z, from.x) + LandingStepDegrees(corners) * Mathf.Deg2Rad;
        return new Vector3(center.x + Mathf.Cos(a) * reach, center.y, center.z + Mathf.Sin(a) * reach);
    }

    /// <summary>A living mob picked at RANDOM from those within <paramref name="radius"/> of <paramref name="point"/>
    /// (planar), or null when the area is empty. The sibling of <see cref="MobAt"/> — same zero-alloc sweep over the
    /// small Active registry, but every candidate is equally likely instead of the nearest winning.
    ///
    /// <para>One pass, no temporary list: the i-th candidate found replaces the current pick with probability 1/i,
    /// which leaves every candidate equally likely. The die comes from <see cref="CombatRng"/> because it DECIDES a
    /// result — a server-authoritative build must be able to reproduce which target the dance chose.</para></summary>
    public static Transform RandomMobNear(Vector3 point, float radius)
    {
        var list = MonsterAI3D.Active;
        float r2 = Mathf.Max(0.01f, radius); r2 *= r2;
        Transform pick = null;
        int seen = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            Vector3 d = ai.transform.position - point; d.y = 0f;
            if (d.sqrMagnitude > r2) continue;
            seen++;
            if (seen == 1 || CombatRng.Range(0, seen) == 0) pick = ai.transform;
        }
        return pick;
    }

    // The living mob nearest `point` within `radius` (planar), if any. Same zero-alloc sweep over the small Active
    // registry the AoE gather uses — no physics query, no GetComponent; runs once per relocation cast.
    static bool MobAt(Vector3 point, float radius, out Vector3 pos, out Health hit)
    {
        pos = point; hit = null;
        var list = MonsterAI3D.Active;
        float best = radius * radius; bool found = false;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            Vector3 d = ai.transform.position - point; d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr > best) continue;
            best = sqr; pos = ai.transform.position; hit = ai.Health; found = true;
        }
        return found;
    }

    // ---- caster (pass 1) ----------------------------------------------------
    // Every CASTER-side effect in the list, applied ONCE to the acting caster. No hit loop: a caster effect isn't a
    // strike, so a multi-hit flurry must not dash you N times (target effects own hitCount, see HitLoops).
    static void ApplyCasterEffects(List<SkillEffect> effects, Vector3 point, Transform aimUnit,
                                   Sprite srcIcon, string srcName, string srcId, Character ch, in StackVars vars)
    {
        if (ch == null || ch.IsDead) return;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (SkillEffect.LandsOnCaster(e))
                ApplyOne(e, ch, 0, ch, point, aimUnit, srcIcon, srcName, srcId, vars);
        }
    }

    /// <summary>THE apply switch: ONE effect onto ONE unit — the player, a mob, anything that is an
    /// <see cref="ICombatUnit"/>. Every effect kind is implemented here exactly once, so every kind is available on
    /// every entity by construction.
    ///
    /// <para><b>This replaced three near-identical switches</b> (caster / mob target / player target). That split was
    /// the real reason a guard, shield, regen, parry, leech, reflect or stat buff could only ever land on the caster:
    /// not a rule anyone chose, just a case missing from two of the three copies. With one switch there is nowhere for
    /// a kind to be "forgotten" on one side of the fight.</para>
    ///
    /// <para><paramref name="ai"/> is the mob brain when the unit IS a mob (callers already hold it, so the mob-only
    /// sinks below cost no lookup) and null for the player. <paramref name="caster"/> is who is ACTING — every
    /// affinity/duration scale reads their ring investment, even when the effect lands on someone else, because the
    /// strength of a buff you place on an ally is yours to earn. <paramref name="relocatePoint"/> is where a Dash /
    /// Teleport sends whoever it moves.</para></summary>
    static void ApplyOne(SkillEffect e, ICombatUnit unit, int hit,
                         Character caster,
                         Vector3 relocatePoint, Transform aimUnit,
                         Sprite srcIcon, string srcName, string srcId, in StackVars vars)
    {
        if (e == null || unit == null) return;
        var states = unit.States;
        if (states == null) return;
        // The unit's BODY in the scene. Null only in the window before the player's avatar has spawned — the effects
        // that need a body (ailments, cleanse, relocation) sit on it, so they simply don't land yet.
        var body = unit.Body;

        // A durational effect tags its state entry with the SOURCE skill's icon + name so the buff tray / nameplate can
        // show it counting down. Set BEFORE the gameplay call (the store's Apply reads the latch), cleared AFTER.
        // The "what it does" line is built only for the PLAYER: a mob nameplate has no hover card, so building the
        // string for every mob of an AoE would be a per-hit allocation nobody reads.
        // Resolved ONCE here, and the ONLY duration this apply uses — the tray tag and the gameplay call now read the
        // same number, so a window the pile paid for can never land while the tray thinks the effect has no duration.
        // Asking the raw typed value was the bug: a number must resolve itself before anything interrogates it.
        float seconds = ScaledSeconds(e, caster, vars);
        bool tagDisplay = seconds > 0f;
        // A speed-earned count is deliberately UNCAPPED: the authored ceiling describes a pile the designer sized by
        // hand, and clamping an earned count to it would silently delete the charges a fast caster just paid for.
        if (tagDisplay) states.SetApplyContext(srcIcon, srcName,
                                               unit is Character ? SkillTooltip.DescribeEffect(e) : null,
                                               e.stacking, ResolveStacks(e, caster, vars),
                                               e.stacksFromAttackSpeed ? 0 : Mathf.Max(1, e.stackLimit.ResolveInt(vars)), srcId);
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                if (hit < EffectiveHits(e, caster, vars)) LandStrike(e, unit, caster, vars, overkill: false);
                break;
            case SkillEffectKind.Execute:
                if (Fires(e, hit, caster, vars)) ApplyExecute(e, unit, vars, caster);
                break;
            case SkillEffectKind.Heal:
                // Same one formula as the Damage side, so a lifesteal read off a pile can't drift from the hit that
                // read the same pile — one cast, one snapshot, two effects.
                // The pile-or-authored choice now lives in ScaledAmount, the same place every other flat number asks it.
                if (hit == 0) unit.Heal(ScaledAmount(e, caster, vars));
                break;
            case SkillEffectKind.Status:
                if (body != null && Fires(e, hit, caster, vars)) Status.Apply(body.gameObject, e.ailment, seconds, ScaledMagnitude(e, caster, vars));
                break;
            case SkillEffectKind.Cleanse:
                if (body != null && Fires(e, hit, caster, vars))
                {
                    var sc = Status.Of(body.gameObject);
                    if (sc != null) sc.Cleanse(Mathf.Max(1, StackedAmount(e, vars)));
                }
                break;
            // Both modifiers edit a pile that already exists, so they run ONCE per landing (hit 0) like every other
            // non-proc effect, and they name their own target — the tick's aim decides nothing here.
            case SkillEffectKind.ExtendDuration:
                if (hit == 0 && ModifierIsAuthored(e))
                    states.ExtendFrom(e.modifierSkillId, e.modifierStateId, ScaledModifierSeconds(e, caster, vars));
                break;
            case SkillEffectKind.AddStacks:
                if (hit == 0 && ModifierIsAuthored(e)) AddStacksToOtherSkill(e, states, caster, vars);
                break;
            // EVERY timed sub-stat change, buff and debuff alike, lands here — move speed, both regens, leech,
            // reflect, evasion, the lot. The authored magnitude is FLAT (not affinity-scaled): a support's value is
            // uptime, not a bigger number. A debuff is authored as a POSITIVE amount and negated here, so nobody has
            // to type a minus sign to make something worse.
            case SkillEffectKind.SubStatBuff:
            case SkillEffectKind.SubStatDebuff:
                if (Fires(e, hit, caster, vars))
                {
                    float amount = Mathf.Max(0f, StackedMagnitude(e, vars));
                    if (e.kind == SkillEffectKind.SubStatDebuff) amount = -amount;
                    states.ApplySubStat(e.subStat, amount, seconds);
                }
                break;
            case SkillEffectKind.Shield:
                if (Fires(e, hit, caster, vars)) states.BeginShield(ScaledAmount(e, caster, vars), seconds);
                break;
            case SkillEffectKind.Parry:
                if (Fires(e, hit, caster, vars)) states.BeginParry(seconds);   // charges ride the apply context (stacks-per-apply)
                break;
            case SkillEffectKind.Brace:
                // Only the window is authored; the pile fills itself from whatever hits arrive (UnitMitigation.Deferred).
                if (Fires(e, hit, caster, vars)) states.BeginBrace(seconds);
                break;
            case SkillEffectKind.BoostOutgoingDamage:
                // Charges ride the apply context like Parry's; each carries the authored bonus, which is FLAT for the
                // same reason a stat buff's is — a support's value is how many attacks it covers, not a bigger number.
                if (Fires(e, hit, caster, vars)) states.BeginOutgoingBoost(seconds, StackedMagnitude(e, vars));
                break;
            case SkillEffectKind.ImmuneToDisables:
                // No magnitude: being stopped is binary. The whole knob is how long it lasts.
                if (Fires(e, hit, caster, vars)) states.BeginDisableImmunity(seconds);
                break;
            case SkillEffectKind.ReduceOutgoingDamage:
                // The boost's mirror, charges and all — authored as the CUT, and flat for the same reason: how many
                // attacks it covers is the knob, not how big the number is.
                if (Fires(e, hit, caster, vars)) states.BeginOutgoingPenalty(seconds, StackedMagnitude(e, vars));
                break;
            case SkillEffectKind.Taunt:
                // Force this unit onto the caster. Both sides implement it (a mob locks its chase, a player's
                // auto-combat can't retarget away), so a taunt is authored once and works on whoever it lands on.
                if (Fires(e, hit, caster, vars)) unit.ForceAggro(caster, seconds);
                break;
            case SkillEffectKind.Knockback:
                // Shove AWAY from the caster (magnitude = speed).
                if (hit == 0) unit.Shove(ShoveOrigin(unit, caster), StackedMagnitude(e, vars),
                                         StackedShoveSeconds(e, vars), StackedShoveSeconds(e, vars));
                break;
            case SkillEffectKind.Pull:
                // Drag TOWARD the caster: shove away from a point mirrored through the target = toward the caster.
                if (hit == 0 && body != null)
                    unit.Shove(2f * body.position - ShoveOrigin(unit, caster), StackedMagnitude(e, vars),
                               StackedShoveSeconds(e, vars), StackedShoveSeconds(e, vars));
                break;
            // `unit` is whoever is being moved — the caster for a Caster-side effect, a mob for a Target-side one.
            // Both relocations share this body: they differ only in HOW the mover travels, never in where it lands
            // or what it leaves behind, so the landing spot and the afterimage are decided once for the pair.
            case SkillEffectKind.Teleport:
            case SkillEffectKind.Dash:
                if (hit != 0) break;
                // Snapshot BEFORE the move — the copy belongs to the spot being left, not the one being arrived at.
                float afterimage = e.afterimageSeconds.Resolve(vars);
                if (afterimage > 0f)
                    Afterimage.Capture(RenderedBody(unit), afterimage, e.afterimageTint);
                if (e.kind == SkillEffectKind.Teleport)
                    unit.TeleportTo(RelocationDest(relocatePoint, aimUnit, unit, e.starCorners.ResolveInt(vars)));   // instant blink
                else
                    // Face what the hop is AIMED AT for the whole trip, not the direction it flies. A relocation lands
                    // BESIDE its target (a star corner), so facing the travel line leaves the caster swinging at empty
                    // air on arrival — and nothing turns it back afterwards. No aimed unit = face travel, as before.
                {
                    unit.GlideTo(RelocationDest(relocatePoint, aimUnit, unit, e.starCorners.ResolveInt(vars)),   // A→B glide
                                 e.dashSpeedMult.Resolve(vars), e.dashEaseIn, e.dashEaseOut,
                                 Mathf.Max(0.05f, e.dashEaseSeconds.Resolve(vars)),
                                 aimUnit != null ? aimUnit.position : (Vector3?)null);
                    // AFTER the glide has started, never before: the hold ends when the unit stops relocating, and
                    // asking that a frame too early would end it instantly. The mover's own rendered model is what
                    // fades — for the player that is a separate object from the body being moved.
                    float opacity = Mathf.Clamp01(e.casterOpacity.Resolve(vars));
                    if (opacity < 0.999f)
                        CharacterFade.HoldWhileRelocating(unit, RenderedBody(unit), opacity);
                }
                break;
        }
        if (tagDisplay) states.ClearApplyContext();
    }

    /// <summary>The unit's RENDERED model — what you actually see, which is not always the body that moves. The
    /// player's model is a separate object riding its root (Character3DVisual is not parented under it), so its Body
    /// holds no renderers at all; every other character carries its model as a child of its body, so Body is already
    /// right for them. StanceController.Player IS the player's model, registered in Awake — a static field read, never
    /// a scene search. Anything that DRAWS a unit (a snapshot, a fade) must ask through here, or it silently finds
    /// nothing on the player.</summary>
    static Transform RenderedBody(ICombatUnit unit)
    {
        if (unit == null) return null;
        return ReferenceEquals(unit, Character.Instance) && StanceController.Player != null
             ? StanceController.Player.transform : unit.Body;
    }

    // Where a shove is measured FROM: the caster's body. Falls back to the target's own spot when there's no caster,
    // which makes the shove direction arbitrary rather than crashing — same fallback the mob-only version had.
    static Vector3 ShoveOrigin(ICombatUnit unit, Character caster)
    {
        if (caster != null && caster.Body != null) return caster.Body.position;
        return unit.Body != null ? unit.Body.position : unit.DamagePosition;
    }

    // The binding's strike count = the largest Damage hitCount (min 1). Each iteration is the trigger point where
    // proc effects roll.
    static int HitLoops(List<SkillEffect> effects, Character caster, in StackVars vars)
    {
        int loops = 1;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e != null && e.kind == SkillEffectKind.Damage) loops = Mathf.Max(loops, EffectiveHits(e, caster, vars));
        }
        return loops;
    }

    // A Damage effect's strike count = authored hitCount scaled by its hit-count affinity (ring investment).
    // hits = round(hitCount × mult). The ONE source of truth for both the flurry length (HitLoops) and each Damage's
    // per-strike cap, so they can't disagree. AffinityMult is stable within a cast (ring investment doesn't change).
    // Public so the tooltip shows the SAME live count the runtime will land (no parallel formula) — it omits the
    // caster, falling back to the local player, which is exactly whose tooltip it is.
    public static int EffectiveHits(SkillEffect e, Character caster = null, StackVars vars = default)
        => Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, e.hitCount.ResolveInt(vars)) * AffinityMult(e.hitCountAffinity, caster)));

    // Single hit (the common case): apply instantly to each mob, unchanged. Multi-hit (hitCount > 1): hand the mob
    // snapshot to MultiHitBurst so the strikes CASCADE over time and each mob shows ONE rising yellow combo total,
    // exactly like RO — instead of N overlapping numbers dumped in a single frame.
    static void DispatchMobHits(List<SkillEffect> effects, List<MonsterAI3D> mobs,
                                Character caster, Vector3 relocatePoint,
                                Sprite srcIcon = null, string srcName = null, string srcId = null,
                                StackVars vars = default)
    {
        if (mobs.Count == 0) return;
        if (HitLoops(effects, caster, vars) <= 1)
        {
            for (int i = 0; i < mobs.Count; i++)
                ApplyEffectsToMob(effects, mobs[i], relocatePoint, caster, srcIcon, srcName, srcId, vars);
            return;
        }
        // A flurry's later strikes land on LATER FRAMES, outside the caster's tick — so the causal chain must ride the
        // callback, or a passive's cascade hits would arrive "unowned" and could re-fire the very passive that caused them.
        ulong chain = PassiveEvents.CurrentChain;
        var flurryVars = vars;   // the cast's readings ride the callback too — later strikes must cash the SAME snapshot
        Vector3 flurryPoint = relocatePoint;   // and so does where this cast aimed — a later strike relocates to the same spot
        // A run that shows ONE total across its repeats rides the callback for the same reason the cause does: the
        // strikes land on later frames, long after the tick that started them has returned.
        var runTotal = RepeatsDamageTotal.Current;
        MultiHitBurst.Run(mobs, HitLoops(effects, caster, vars), MultiHitInterval,
            (ai, hit) =>
            {
                // The CAUSE rides the callback whole — chain and skill both. A flurry's later strike can be the killing
                // blow, and a kill that couldn't name the skill that landed it would be a kill by nobody.
                var prev = PassiveEvents.Push(chain, srcId);
                var prevTotal = RepeatsDamageTotal.Push(runTotal);
                try { return ApplyHitToMob(effects, ai, hit, caster, flurryPoint, srcIcon, srcName, srcId, flurryVars); }
                finally { RepeatsDamageTotal.Pop(prevTotal); PassiveEvents.Pop(prev); }   // the damage dealt feeds the rising total
            },
            showTotal: runTotal == null);   // one drawer of the yellow number, never both
    }

    // One beat of a multi-hit flurry on ONE mob: land every Damage effect (overkill-safe so the running total keeps
    // climbing even past the kill, RO-style) and roll the binding's procs THIS hit. Returns the damage dealt so the
    // yellow combo total can accumulate it.
    static int ApplyHitToMob(List<SkillEffect> effects, MonsterAI3D ai, int hit,
                             Character caster, Vector3 relocatePoint,
                             Sprite srcIcon = null, string srcName = null, string srcId = null,
                             StackVars vars = default)
    {
        if (ai == null) return 0;
        var h = ai.Health;
        if (h == null) return 0;
        int dealt = 0;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e == null) continue;
            if (e.kind == SkillEffectKind.Damage)
            {
                if (hit >= EffectiveHits(e, caster, vars)) continue;
                // Overkill-safe, so a multi-hit flurry keeps its numbers, hit sparks AND per-strike sound climbing past
                // the kill — the whole count runs to the end, RO-style.
                dealt += LandStrike(e, h, caster, vars, overkill: true);
            }
            else
                ApplyToTarget(e, h, hit, caster, relocatePoint, srcIcon, srcName, srcId, vars);   // procs / status / one-shots
        }
        return dealt;
    }

    // Does this effect happen on THIS hit? Every state and proc asks, once per hit of the binding's multi-hit Damage,
    // so a 3-hit strike offers a buff or an ailment three chances to land. A certain effect (chance 1, the default)
    // answers yes every time — which is why "guaranteed" needs no special case anywhere.
    //
    // A caster-side effect never sees a hit index above 0 (it is applied once per landing, before targets are gathered),
    // so this is one roll for it, not one per strike.
    //
    // A chance of 0 means the effect can never happen. It is a real authored value, not "unset" — tools/check-skill-data.py
    // reports it, because an effect nobody can ever see is a mistake, never a design.
    //
    // The odds have their OWN scaling channel, on every kind alike: invest in it and a gamble lands more often. It used
    // to borrow the output channel, which meant an ailment could scale how OFTEN it landed or how BIG it was but never
    // both, and which of the two you got depended on a number somewhere else in the effect.
    static bool Fires(SkillEffect e, int hitIndex, Character caster, in StackVars vars)
    {
        float odds = e.chance.Resolve(vars);
        if (odds <= 0f) return false;
        if (odds >= 1f) return true;
        return CombatRng.Chance(Mathf.Clamp01(odds * AffinityMult(e.chanceAffinity, caster)));
    }

    // ---- mob ----------------------------------------------------------------
    /// <summary>Run the hit loop for one MOB: each hit lands the Damage strike + rolls the binding's procs.</summary>
    public static void ApplyEffectsToMob(List<SkillEffect> effects, MonsterAI3D ai, Vector3 relocatePoint,
                                         Character caster = null,
                                         Sprite srcIcon = null, string srcName = null, string srcId = null, StackVars vars = default)
    {
        if (effects == null || ai == null) return;
        var h = ai.Health;
        if (h == null) return;
        int loops = HitLoops(effects, caster, vars);
        for (int n = 0; n < loops; n++)
            for (int i = 0; i < effects.Count; i++)
                ApplyToTarget(effects[i], h, n, caster, relocatePoint, srcIcon, srcName, srcId, vars);
    }

    // One TARGET-side effect onto a gathered unit. Caster-side effects already ran in pass 1 and must not land again —
    // the partition is checked HERE, once, for every target (mob or player), so the two passes can never both fire an
    // effect. Which side an effect is on is the AUTHOR's choice (SkillEffect.side); Auto keeps the kind's old side.
    // A relocation authored on the TARGET side sends whoever it caught to the cast's aimed point — the same point the
    // caster side reads, so one cast has one destination however the author pointed the effect. No aimed UNIT is given
    // here on purpose: the aimed unit IS the one being moved, and landing it beside itself would move it nowhere.
    static void ApplyToTarget(SkillEffect e, ICombatUnit unit, int hit,
                              Character caster, Vector3 relocatePoint,
                              Sprite srcIcon, string srcName, string srcId, in StackVars vars)
    {
        if (e == null || SkillEffect.LandsOnCaster(e)) return;
        ApplyOne(e, unit, hit, caster, relocatePoint, null, srcIcon, srcName, srcId, vars);
    }

    // Execute: a self-contained bonus hit scaled by the TARGET's MISSING HP (proc'd per hit by the caller). magnitude =
    // execute power: bonus = roll x magnitude x (1 - HP fraction), so it's tiny at full HP and huge near death.
    // One implementation for every unit — it used to be two copies (one reading a mob's HP, one the player's) that had
    // to be kept in step by hand.
    static void ApplyExecute(SkillEffect e, ICombatUnit unit, in StackVars vars, Character caster)
    {
        float power = e.magnitude.Resolve(vars);
        if (unit.MaxHP <= 0 || power <= 0f) return;
        float missing = 1f - Mathf.Clamp01((float)unit.CurrentHP / unit.MaxHP);
        if (missing <= 0f) return;
        int roll = RollPower(e, vars, AffinityMult(e.affinity, caster),
                             out bool crit, caster);
        int bonus = Mathf.RoundToInt(roll * power * missing);
        if (bonus <= 0) return;
        // Its own damage number, so its own charge — an execute is a second outgoing hit riding the first, not part of it.
        bonus = UnitOffense.SpendCharges(caster != null ? caster.States : null, bonus);
        CombatAuthority.Apply(unit, new DamageInfo(bonus, e.element, e.damageType == DamageType.Magic, crit,
                                                  attacker: caster));
    }

    // ---- player as a TARGET (pass 2) ----------------------------------------
    /// <summary>Run the hit loop for the PLAYER as a gathered TARGET (heal / self-damage / an ailment on yourself).
    /// Caster effects are NOT here — they run once in <see cref="ApplyBurst"/>'s caster pass. <paramref name="caster"/>
    /// is the acting entity (its own self-cast target); defaults to the local player.</summary>
    public static void ApplyEffectsToPlayer(List<SkillEffect> effects, Vector3 relocatePoint,
                                            Sprite srcIcon = null, string srcName = null, string srcId = null, Character caster = null,
                                            StackVars vars = default)
    {
        var ch = caster ?? Character.Instance;
        if (effects == null || ch == null) return;
        int loops = HitLoops(effects, ch, vars);
        for (int n = 0; n < loops; n++)
            for (int i = 0; i < effects.Count; i++)
                ApplyToTarget(effects[i], ch, n, ch, relocatePoint, srcIcon, srcName, srcId, vars);
    }

    /// <summary>ONE Damage strike onto one unit — the single place a skill's damage is rolled and applied, whether it
    /// lands instantly or as a beat of a multi-hit cascade (<paramref name="overkill"/> is the only difference between
    /// those two). Returns the damage dealt, so a rising combo total can accumulate it.
    ///
    /// <para>TWO ways a strike is paid for, and they differ only in WHICH roll sits underneath: a strike authored to
    /// RESOLVE AS A BASIC ATTACK is the caster's own swing, and everything else is the ordinary skill roll. Either way
    /// the effect's damage multiplier scales it — that number always multiplies, whatever it was built from.</para></summary>
    static int LandStrike(SkillEffect e, ICombatUnit unit, Character caster,
                          in StackVars vars, bool overkill)
    {
        // Ring investment and the combo ramp are both OUTER multipliers of the finished number, so they travel together
        // as one scale — the pile's contribution answers to them exactly as the caster's own damage does.
        float outer = AffinityMult(e.affinity, caster);

        if (e.resolveAsBasicAttack)
            // The caster swings; the skill only says how hard. The swing's own roll becomes the basis the damage
            // multiplier is applied to, and the pile meets the result — so the one thing the checkbox changes is WHICH
            // roll is underneath, which is the whole point of it.
            return BasicSwing.Land(caster, unit, e.damageMult, vars, outer, overkill,
                                   // This swing has no wind-up of its own, so it announces the intent here rather than
                                   // never — a passive that answers "about to swing" (the auto-lunge) rides a skill's
                                   // swings exactly as it rides an ordinary one.
                                   announceIntent: true);

        int power = RollPower(e, vars, outer, out bool crit, caster);
        // One strike, one charge. (The basic-attack branch above doesn't come back through here: the swing spends its
        // own, so nothing is charged twice.)
        power = UnitOffense.SpendCharges(caster != null ? caster.States : null, power);
        // −1 HIT = always-lands skill strike (no accuracy roll); DamageInfo defaults attackerHit to −1.
        return CombatAuthority.Apply(unit, new DamageInfo(power, e.element, e.damageType == DamageType.Magic, crit,
                                                         overkill: overkill, attacker: caster));
    }

    // Damage = (the caster's rolled hit × this effect's damage multiplier) met by the pile, all × ring × combo. The
    // roll is the BASIS the multiplier is applied to, which is what puts both sides of the operation in damage.
    // Crit-aware so a tick crits like any other skill. No allocation.
    static int RollPower(SkillEffect e, in StackVars vars, float outer, out bool crit, Character caster)
    {
        crit = false;
        var ch = caster ?? Character.Instance;
        if (ch == null) return 1;
        // Magic → MATK roll (unmissable). Physical → ATK roll. Crit type → ATK roll with a FORCED crit (its
        // armour-pierce is the deferred combat-pass TODO; the forced crit is the part we can honour now).
        int roll = e.damageType == DamageType.Magic
            ? ch.RollMagicDamage(out crit)
            : ch.RollDamage(out crit, 0, e.damageType == DamageType.Crit);
        return Mathf.Max(1, Mathf.RoundToInt(e.damageMult.Resolve(vars, outer, roll)));
    }

    // ---- affinity (ring-investment scaling) ---------------------------------
    // The multiplier for ONE affinity channel = 1 + Σ(normalised weight × the caster's gated ring % in that stat).
    // Empty/absent list ⇒ 1 (no scaling). Zero-alloc: one pass over the small authored list, no LINQ / temp collections.
    // The caster defaults to the local player, so the tooltip (which has no caster) reads the local ring investment.
    public static float AffinityMult(List<StatWeight> weights, Character caster = null)
    {
        if (weights == null || weights.Count == 0) return 1f;
        var ch = caster ?? Character.Instance;
        if (ch == null) return 1f;
        float sum = 0f, acc = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            float w = weights[i].weight;
            if (w <= 0f) continue;
            sum += w;
            acc += w * ch.RingBonus(weights[i].stat);
        }
        return sum > 0f ? 1f + acc / sum : 1f;
    }

    // The affinity list scales the effect's main output (heal amount, DoT damage, buff amount, slow amount); the
    // duration list scales its seconds. A Status PROC spends its affinity on the CHANCE (see Fires), so its magnitude
    // stays raw. Everything falls back to ×1 when no weights are authored.
    // A MODIFIER's two numbers are the same two channels every other effect has, just stored in their own fields
    // because they describe another skill's pile rather than this effect's own body: the seconds it adds are a
    // DURATION, the stacks it adds are its OUTPUT. Routed here so a modifier scales off the ring and off the cast's
    // pile exactly like everything else, instead of being the one kind whose numbers are frozen.
    static float ScaledModifierSeconds(SkillEffect e, Character caster, in StackVars vars) =>
        Mathf.Max(0f, e.modifierSeconds.Resolve(vars, AffinityMult(e.durationAffinity, caster)));
    // HOW MANY stacks a modifier hands over is a plain size, so the pile sizes it like every other output — that is
    // Perfect Form: a run of hits read as a count, times a third, floored, so three in a row adds one and six adds two.
    static int ScaledModifierStacks(SkillEffect e, Character caster, in StackVars vars) =>
        e.modifierStacks.ResolveInt(vars, AffinityMult(e.affinity, caster));

    // The kinds whose bonus is deliberately FLAT against ring investment (a stat buff, leech, reflect, the outgoing
    // charges) still answer to the cast's pile: that exemption was a decision about what your BUILD may lift, never
    // about what a cast EARNED this fight. So they take the pile's multiplier and skip affinity's.
    static float StackedMagnitude(SkillEffect e, in StackVars vars) =>
        e.magnitude.Resolve(vars);
    static int StackedAmount(SkillEffect e, in StackVars vars) =>
        e.amount.ResolveInt(vars);
    // A shove's travel time is its own field because it is MOVEMENT, not a state window — but it is still this
    // effect's duration, so the pile lifts it through the same channel every other duration uses.
    static float StackedShoveSeconds(SkillEffect e, in StackVars vars) =>
        Mathf.Max(0.01f, e.shoveSeconds.Resolve(vars));

    // The flat-amount sibling of ScaledMagnitude (a heal's HP, a shield's pool, a cleanse's count) — same two scalings.
    static int ScaledAmount(SkillEffect e, Character caster, in StackVars vars) =>
        e.amount.ResolveInt(vars, AffinityMult(e.affinity, caster));
    /// <summary>Feed ANOTHER skill's pile. What one of its charges is worth is read from THAT skill's own authored
    /// effect — the single definition of its charge — so a pile that is running and one that is not both end up with
    /// identical stacks, and a modifier never has to invent a number. A skill that grants nothing of that kind (or was
    /// deleted) simply gives nothing.</summary>
    // Modifier targets already reported, so a skill cast every few seconds says it once. Keyed by the named skill,
    // which is what the author has to go fix.
    static readonly HashSet<string> reportedModifiers = new HashSet<string>();

    /// <summary>SAY SO when a modifier never named WHICH charges it edits. Every value of that setting is a real one,
    /// so an unauthored modifier would silently edit the first thing in the list — the same silent-wrong-target failure
    /// the stack gate hit, and the reason both carry a "was this authored" marker. Refuses to act rather than act
    /// wrongly.</summary>
    static bool ModifierIsAuthored(SkillEffect e)
    {
        if (e.modifierStateIdSet && !string.IsNullOrEmpty(e.modifierSkillId)) return true;
        if (reportedModifiers.Add(e.modifierSkillId ?? ""))
            Debug.LogError("[Modifier] A skill edits another skill's charges but never names which skill or which " +
                           "charges, so it does nothing. Open it in the Skill Composer, finish the modifier, and save.");
        return false;
    }

    static void AddStacksToOtherSkill(SkillEffect e, UnitStates states, Character caster, in StackVars vars)
    {
        var granter = SkillCatalog.Get(e.modifierSkillId);
        SkillEffect grant = null;
        if (granter?.bindings != null)
            for (int b = 0; b < granter.bindings.Count && grant == null; b++)
            {
                var fx = granter.bindings[b]?.effects;
                if (fx == null) continue;
                for (int i = 0; i < fx.Count; i++)
                    if (fx[i] != null && fx[i].stacking
                        && SkillEffect.TryStateFor(fx[i], out var id) && id == e.modifierStateId)
                    { grant = fx[i]; break; }
            }
        if (grant == null) return;
        // What ONE added stack is worth is the GRANTER's business, so its numbers are read with no pile behind them:
        // this cast's stacks scale what THIS effect does (how many it adds, below), never what the other skill's
        // charges are each worth. Ring investment still applies — that is the granter's own scaling, not the cast's.
        states.AddStacksTo(e.modifierSkillId, e.modifierStateId, ScaledModifierStacks(e, caster, vars),
                           ScaledMagnitude(grant, caster, StackVars.None), ScaledSeconds(grant, caster, StackVars.None),
                           granter.icon, granter.displayName, null);
    }

    // The THREE numbers every effect kind is made of, each scaled the same two ways at the same one place: by ring
    // investment (what you built) and by the cast's pile (what you earned this fight). Every kind flows through these,
    // so neither scaling has to be taught to a single effect — and a new kind inherits both by existing.
    // An effect's OUTPUT: the authored base value met by the pile through the one shared formula, then scaled by ring
    // investment. The author's number is never ignored and never replaced — it is the base the pile builds on.
    static float ScaledMagnitude(SkillEffect e, Character caster, in StackVars vars) =>
        e.magnitude.Resolve(vars, AffinityMult(e.affinity, caster));
    static float ScaledSeconds(SkillEffect e, Character caster, in StackVars vars) =>
        e.seconds.Resolve(vars, AffinityMult(e.durationAffinity, caster));

    /// <summary>How many stacks this apply grants. Normally the authored number; when the effect is driven by attack
    /// speed it is instead HOW MANY TIMES THE CASTER WOULD SWING inside this effect's own window — the window it is
    /// actually getting (<see cref="ScaledSeconds"/>, so ring duration investment lengthens the window and earns the
    /// extra stacks that go with it) divided by the caster's live seconds-per-swing.
    ///
    /// <para>Rounded UP, which is what keeps two effects in exact proportion: taking half of a window is always the
    /// same number as halving that window's own count, so a 0.5 share is exactly half at every attack speed with
    /// nothing to keep in sync by hand.</para>
    ///
    /// <para>Falls back to the authored count when there is no swing rate to read (the avatar hasn't spawned) rather
    /// than granting nothing — a missing body must not silently erase a skill's charges.</para></summary>
    static int ResolveStacks(SkillEffect e, Character caster, in StackVars vars)
    {
        int authored = Mathf.Max(1, e.stacksPerApply.ResolveInt(vars));
        if (!e.stacksFromAttackSpeed) return authored;
        float swing = caster != null ? caster.SwingTime : 0f;
        if (swing <= 0f) return authored;
        return Mathf.Max(1, Mathf.CeilToInt(ScaledSeconds(e, caster, vars) / swing
                                            * Mathf.Max(0f, e.stackShare.Resolve(vars))));
    }
}
