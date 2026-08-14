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
/// one strike, and every PROC effect in the binding (a Status or Execute with <see cref="SkillEffect.chance"/> &gt; 0)
/// rolls its chance THAT hit. Non-proc effects (chance == 0 — buffs, heals, a guaranteed status) apply once, on hit 0.
/// So a stun-proc or execute never lives INSIDE the Damage effect; it's its own effect that triggers off the multi-hit.</para>
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
                                  float extraMult = 0f, int extraFlat = 0, float comboMult = 1f,
                                  Sprite srcIcon = null, string srcName = null, string srcId = null,
                                  Vector3? aimPoint = null, Character caster = null)
    {
        if (effects == null || effects.Count == 0) return;

        // Resolve the acting caster ONCE here (the local player today; a per-connection entity once server-authoritative),
        // so nothing downstream reaches for the Character.Instance global — the whole burst scales off THIS entity.
        var ch = caster ?? Character.Instance;

        // Where a self-relocation (Teleport / Dash) sends the caster: the AIM point, NOT the tick's VFX anchor point.
        // The two differ once a ground cast keeps origin(caster) ≠ target(aim), and a teleport must always go to the
        // aimed spot regardless of which anchor the VFX author chose for the visual. Defaults to point for old callers.
        Vector3 selfPoint = aimPoint ?? point;

        // PASS 1 — CASTER effects (see SkillEffect.LandsOnCaster): they happen TO THE CASTER, so the tick's targeting has no say.
        // They run here exactly once, whatever the scope/affects are, and are excluded from the target pass below — so
        // "Single Target on a mob" can't swallow a Dash (the bug this split fixes), and a Self tick can't double-apply it.
        ApplyCasterEffects(effects, selfPoint, singleTarget, extraMult, extraFlat, comboMult, srcIcon, srcName, srcId, ch);

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
                    DispatchMobHits(effects, burstTargets, extraMult, extraFlat, comboMult, ch, srcIcon, srcName, srcId);
                }
            }
            else if ((targets & SkillTargets.Player) != 0)
                ApplyEffectsToPlayer(effects, extraMult, extraFlat, comboMult, srcIcon, srcName, srcId, ch);
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
            DispatchMobHits(effects, burstTargets, extraMult, extraFlat, comboMult, ch, srcIcon, srcName, srcId);
        }

        if ((targets & SkillTargets.Player) != 0)
        {
            // The player as a TARGET (a heal / a self-damage / an ailment on yourself) — deliberately NOT radius-gated:
            // no skill is a "hazard zone that hits the player if inside", and a ground cast's point is by definition
            // somewhere the caster isn't standing. Caster effects already ran in pass 1 and are not repeated here.
            if (ch != null && !ch.IsDead)
                ApplyEffectsToPlayer(effects, extraMult, extraFlat, comboMult, srcIcon, srcName, srcId, ch);
        }
    }

    // ---- where a relocation lands -------------------------------------------
    // How far from the target a relocation stops, as a fraction of the caster's melee reach — just INSIDE it, the same
    // "always in trading range" margin a melee mob holds at (MonsterAI3D's stop clamp), so the strict InRange check that
    // gates a swing passes on arrival. Player3DAttacker.EffectiveRange stays the single owner of the reach itself.
    const float LandingReachFactor = 0.9f;

    // How far around the target each successive landing moves. 216° = cross to the far side (180°) plus a 36° nudge,
    // which is the SAME rule seen from the caster: the spot sits 18° off your own line to the target. Every hop
    // therefore passes within a tenth of a unit of the target — you cut straight through it — and because 5 × 216°
    // is exactly two full laps, five hops visit five evenly-spaced bearings and close the loop: the path traced on
    // the ground is a five-pointed star. Any multiple of 72° would land on the same five spots, but only this one
    // orders them into a star instead of walking the pentagon's rim.
    const float LandingStepDegrees = 216f;

    /// <summary>Where a RELOCATION (Dash / Teleport) actually LANDS. Aiming at a unit means "get to it", not "stand
    /// inside it": the mover arrives at ITS OWN melee reach, on the far side, rotated <see cref="LandingStepDegrees"/>
    /// from wherever it currently stands — so it never ends up stacked on the target's spot, every dash reads as a cut
    /// THROUGH the target, and five in a row trace a star on the ground. Derived from the mover's CURRENT bearing
    /// rather than a stored step index: no sequence state to reset, an interrupted chain resumes from wherever it
    /// stopped. A pure ground aim — no unit given and no mob on the spot — is used verbatim.
    ///
    /// <para><paramref name="mover"/> is WHOEVER IS BEING MOVED, which is not always the local player: an effect
    /// authored on the TARGET side relocates a mob, and once combat is server-authoritative the caster is some other
    /// connection's entity. This used to read a static <c>Player3DAttacker</c> resolved from the local player, so both
    /// the reach and the bearing came from YOU no matter who was actually moving — teleporting a mob sent it to a spot
    /// computed from the player's position and the player's weapon range.</para></summary>
    static Vector3 RelocationDest(Vector3 aim, Transform unit, ICombatUnit mover)
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
        float a = Mathf.Atan2(from.z, from.x) + LandingStepDegrees * Mathf.Deg2Rad;
        return new Vector3(center.x + Mathf.Cos(a) * reach, center.y, center.z + Mathf.Sin(a) * reach);
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
                                   float extraMult, int extraFlat, float comboMult,
                                   Sprite srcIcon, string srcName, string srcId, Character ch)
    {
        if (ch == null || ch.IsDead) return;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (SkillEffect.LandsOnCaster(e))
                ApplyOne(e, ch, 0, extraMult, extraFlat, comboMult, ch, point, aimUnit, srcIcon, srcName, srcId);
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
                         float extraMult, int extraFlat, float comboMult, Character caster,
                         Vector3 relocatePoint, Transform aimUnit,
                         Sprite srcIcon, string srcName, string srcId)
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
        bool tagDisplay = e.seconds > 0f;
        if (tagDisplay) states.SetApplyContext(srcIcon, srcName,
                                               unit is Character ? SkillTooltip.DescribeEffect(e) : null,
                                               e.stacking, e.stacksPerApply, e.stackLimit, srcId);
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                if (hit < EffectiveHits(e, caster))
                {
                    int power = RollPower(e, extraMult, extraFlat, comboMult, out bool crit, caster);
                    // −1 HIT = always-lands skill strike (no accuracy roll); DamageInfo defaults attackerHit to −1.
                    CombatAuthority.Apply(unit, new DamageInfo(power, e.element, e.damageType == DamageType.Magic, crit));
                }
                break;
            case SkillEffectKind.Execute:
                if (Fires(e, hit, caster)) ApplyExecute(e, unit, extraMult, extraFlat, comboMult, caster);
                break;
            case SkillEffectKind.Heal:
                if (hit == 0) unit.Heal(ScaledAmount(e, caster));
                break;
            case SkillEffectKind.Status:
                if (body != null && Fires(e, hit, caster)) Status.Apply(body.gameObject, e.ailment, ScaledSeconds(e, caster), ScaledMagnitude(e, caster));
                break;
            case SkillEffectKind.Haste:
                // Authored as a BONUS FRACTION (0.3 = +30%) → multiplier.
                if (body != null && Fires(e, hit, caster))
                {
                    var sc = Status.Of(body.gameObject);
                    if (sc != null) sc.Haste(1f + ScaledMagnitude(e, caster), ScaledSeconds(e, caster));
                }
                break;
            case SkillEffectKind.Cleanse:
                if (hit == 0 && body != null)
                {
                    var sc = Status.Of(body.gameObject);
                    if (sc != null) sc.Cleanse(Mathf.Max(1, e.amount));
                }
                break;
            case SkillEffectKind.Vulnerability:
                if (hit == 0) states.BeginVulnerable(ScaledMagnitude(e, caster), ScaledSeconds(e, caster));
                break;
            case SkillEffectKind.Guard:
                if (hit == 0) states.BeginGuard(ScaledSeconds(e, caster), ScaledMagnitude(e, caster));
                break;
            case SkillEffectKind.Buff:
                // Authored magnitude is a BONUS FRACTION (0.3 = +30%); ApplyStatBuff takes a multiplier. AMPLIFY buffs
                // are FLAT (not affinity-scaled) — a support's value is uptime + the ramp, not a bigger number.
                if (hit == 0) states.ApplyStatBuff(e.buffStat, 1f + Mathf.Max(0f, e.magnitude), ScaledSeconds(e, caster),
                                                   e.rampDelay, e.rampPerStep, e.rampInterval, e.rampCap);
                break;
            case SkillEffectKind.Regen:
                if (hit == 0) states.BeginRegen(Mathf.Max(1, Mathf.RoundToInt(ScaledMagnitude(e, caster))), ScaledSeconds(e, caster));
                break;
            case SkillEffectKind.ManaLeech:
                if (hit == 0) states.BeginManaLeech(ScaledSeconds(e, caster), e.magnitude);
                break;
            case SkillEffectKind.Reflect:
                if (hit == 0) states.BeginReflect(ScaledSeconds(e, caster), e.magnitude);
                break;
            case SkillEffectKind.Shield:
                if (hit == 0) states.BeginShield(ScaledAmount(e, caster), ScaledSeconds(e, caster));
                break;
            case SkillEffectKind.Parry:
                if (hit == 0) states.BeginParry(ScaledSeconds(e, caster));   // charges ride the apply context (stacks-per-apply)
                break;
            case SkillEffectKind.Taunt:
                // Force this unit onto the caster. Both sides implement it (a mob locks its chase, a player's
                // auto-combat can't retarget away), so a taunt is authored once and works on whoever it lands on.
                if (hit == 0) unit.ForceAggro(caster, ScaledSeconds(e, caster));
                break;
            case SkillEffectKind.Knockback:
                // Shove AWAY from the caster (magnitude = speed).
                if (hit == 0) unit.Shove(ShoveOrigin(unit, caster), e.magnitude, e.shoveSeconds, e.shoveSeconds);
                break;
            case SkillEffectKind.Pull:
                // Drag TOWARD the caster: shove away from a point mirrored through the target = toward the caster.
                if (hit == 0 && body != null)
                    unit.Shove(2f * body.position - ShoveOrigin(unit, caster), e.magnitude, e.shoveSeconds, e.shoveSeconds);
                break;
            case SkillEffectKind.Teleport:
                // `unit` is whoever is being moved — the caster for a Caster-side effect, a mob for a Target-side one.
                if (hit == 0) unit.TeleportTo(RelocationDest(relocatePoint, aimUnit, unit));   // instant blink
                break;
            case SkillEffectKind.Dash:
                if (hit == 0) unit.GlideTo(RelocationDest(relocatePoint, aimUnit, unit), // A→B glide
                                           e.dashSpeedMult, e.dashEaseIn, e.dashEaseOut, e.dashEaseSeconds);
                break;
        }
        if (tagDisplay) states.ClearApplyContext();
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
    static int HitLoops(List<SkillEffect> effects, Character caster)
    {
        int loops = 1;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e != null && e.kind == SkillEffectKind.Damage) loops = Mathf.Max(loops, EffectiveHits(e, caster));
        }
        return loops;
    }

    // A Damage effect's strike count = authored hitCount scaled by its hit-count affinity (ring investment).
    // hits = round(hitCount × mult). The ONE source of truth for both the flurry length (HitLoops) and each Damage's
    // per-strike cap, so they can't disagree. AffinityMult is stable within a cast (ring investment doesn't change).
    // Public so the tooltip shows the SAME live count the runtime will land (no parallel formula) — it omits the
    // caster, falling back to the local player, which is exactly whose tooltip it is.
    public static int EffectiveHits(SkillEffect e, Character caster = null)
        => Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, e.hitCount) * AffinityMult(e.hitCountAffinity, caster)));

    // Single hit (the common case): apply instantly to each mob, unchanged. Multi-hit (hitCount > 1): hand the mob
    // snapshot to MultiHitBurst so the strikes CASCADE over time and each mob shows ONE rising yellow combo total,
    // exactly like RO — instead of N overlapping numbers dumped in a single frame.
    static void DispatchMobHits(List<SkillEffect> effects, List<MonsterAI3D> mobs,
                                float extraMult, int extraFlat, float comboMult, Character caster,
                                Sprite srcIcon = null, string srcName = null, string srcId = null)
    {
        if (mobs.Count == 0) return;
        if (HitLoops(effects, caster) <= 1)
        {
            for (int i = 0; i < mobs.Count; i++)
                ApplyEffectsToMob(effects, mobs[i], extraMult, extraFlat, comboMult, caster, srcIcon, srcName, srcId);
            return;
        }
        // A flurry's later strikes land on LATER FRAMES, outside the caster's tick — so the causal chain must ride the
        // callback, or a passive's cascade hits would arrive "unowned" and could re-fire the very passive that caused them.
        ulong chain = PassiveEvents.CurrentChain;
        MultiHitBurst.Run(mobs, HitLoops(effects, caster), MultiHitInterval,
            (ai, hit) =>
            {
                ulong prev = PassiveEvents.PushChain(chain);
                try { return ApplyHitToMob(effects, ai, hit, extraMult, extraFlat, comboMult, caster, srcIcon, srcName, srcId); }
                finally { PassiveEvents.PopChain(prev); }   // the damage dealt feeds the mob's rising combo total
            });
    }

    // One beat of a multi-hit flurry on ONE mob: land every Damage effect (overkill-safe so the running total keeps
    // climbing even past the kill, RO-style) and roll the binding's procs THIS hit. Returns the damage dealt so the
    // yellow combo total can accumulate it.
    static int ApplyHitToMob(List<SkillEffect> effects, MonsterAI3D ai, int hit,
                             float extraMult, int extraFlat, float comboMult, Character caster,
                             Sprite srcIcon = null, string srcName = null, string srcId = null)
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
                if (hit >= EffectiveHits(e, caster)) continue;
                int power = RollPower(e, extraMult, extraFlat, comboMult, out bool crit, caster);
                // Both magic and physical use an overkill-safe sink here, so a multi-hit flurry keeps its numbers,
                // hit sparks AND per-strike sound climbing past the kill — the whole count runs to the end, RO-style.
                dealt += CombatAuthority.Apply(h, new DamageInfo(power, e.element, e.damageType == DamageType.Magic, crit, overkill: true));
            }
            else
                ApplyToTarget(e, h, hit, extraMult, extraFlat, comboMult, caster, srcIcon, srcName, srcId);   // procs / status / one-shots
        }
        return dealt;
    }

    // A proc-type effect (chance > 0) rolls PER HIT; a non-proc effect (chance == 0) applies ONCE, on the first hit.
    // A STATUS proc puts its affinity on its CHANCE (a stun has no magnitude — its only strength is how often it lands,
    // e.g. Accuracy investment → higher stun chance). Other procs (Execute) put affinity on their own output (damage),
    // so their chance stays the authored value.
    static bool Fires(SkillEffect e, int hitIndex, Character caster)
    {
        if (e.chance <= 0f) return hitIndex == 0;
        float c = e.kind == SkillEffectKind.Status ? Mathf.Clamp01(e.chance * AffinityMult(e.affinity, caster)) : e.chance;
        return CombatRng.Chance(c);
    }

    // ---- mob ----------------------------------------------------------------
    /// <summary>Run the hit loop for one MOB: each hit lands the Damage strike + rolls the binding's procs.</summary>
    public static void ApplyEffectsToMob(List<SkillEffect> effects, MonsterAI3D ai,
                                         float extraMult = 0f, int extraFlat = 0, float comboMult = 1f, Character caster = null,
                                         Sprite srcIcon = null, string srcName = null, string srcId = null)
    {
        if (effects == null || ai == null) return;
        var h = ai.Health;
        if (h == null) return;
        int loops = HitLoops(effects, caster);
        for (int n = 0; n < loops; n++)
            for (int i = 0; i < effects.Count; i++)
                ApplyToTarget(effects[i], h, n, extraMult, extraFlat, comboMult, caster, srcIcon, srcName, srcId);
    }

    // One TARGET-side effect onto a gathered unit. Caster-side effects already ran in pass 1 and must not land again —
    // the partition is checked HERE, once, for every target (mob or player), so the two passes can never both fire an
    // effect. Which side an effect is on is the AUTHOR's choice (SkillEffect.side); Auto keeps the kind's old side.
    static void ApplyToTarget(SkillEffect e, ICombatUnit unit, int hit,
                              float extraMult, int extraFlat, float comboMult, Character caster,
                              Sprite srcIcon, string srcName, string srcId)
    {
        if (e == null || SkillEffect.LandsOnCaster(e)) return;
        ApplyOne(e, unit, hit, extraMult, extraFlat, comboMult, caster, Vector3.zero, null, srcIcon, srcName, srcId);
    }

    // Execute: a self-contained bonus hit scaled by the TARGET's MISSING HP (proc'd per hit by the caller). magnitude =
    // execute power: bonus = roll x magnitude x (1 - HP fraction), so it's tiny at full HP and huge near death.
    // One implementation for every unit — it used to be two copies (one reading a mob's HP, one the player's) that had
    // to be kept in step by hand.
    static void ApplyExecute(SkillEffect e, ICombatUnit unit, float extraMult, int extraFlat, float comboMult, Character caster)
    {
        if (unit.MaxHP <= 0 || e.magnitude <= 0f) return;
        float missing = 1f - Mathf.Clamp01((float)unit.CurrentHP / unit.MaxHP);
        if (missing <= 0f) return;
        int roll = RollPower(e, extraMult, extraFlat, comboMult, out bool crit, caster);
        int bonus = Mathf.RoundToInt(roll * e.magnitude * missing);
        if (bonus <= 0) return;
        CombatAuthority.Apply(unit, new DamageInfo(bonus, e.element, e.damageType == DamageType.Magic, crit));
    }

    // ---- player as a TARGET (pass 2) ----------------------------------------
    /// <summary>Run the hit loop for the PLAYER as a gathered TARGET (heal / self-damage / an ailment on yourself).
    /// Caster effects are NOT here — they run once in <see cref="ApplyBurst"/>'s caster pass. <paramref name="caster"/>
    /// is the acting entity (its own self-cast target); defaults to the local player.</summary>
    public static void ApplyEffectsToPlayer(List<SkillEffect> effects,
                                            float extraMult = 0f, int extraFlat = 0, float comboMult = 1f,
                                            Sprite srcIcon = null, string srcName = null, string srcId = null, Character caster = null)
    {
        var ch = caster ?? Character.Instance;
        if (effects == null || ch == null) return;
        int loops = HitLoops(effects, ch);
        for (int n = 0; n < loops; n++)
            for (int i = 0; i < effects.Count; i++)
                ApplyToTarget(effects[i], ch, n, extraMult, extraFlat, comboMult, ch, srcIcon, srcName, srcId);
    }

    // Damage value = the caster's rolled hit × (the effect's multiplier + per-level growth) × ring affinity × the combo
    // ramp, + a per-level flat bonus. Crit-aware so a tick crits like any other skill. No allocation.
    static int RollPower(SkillEffect e, float extraMult, int extraFlat, float comboMult, out bool crit, Character caster)
    {
        crit = false;
        var ch = caster ?? Character.Instance;
        if (ch == null) return 1;
        float mult = (e.damageMult <= 0f ? 1f : e.damageMult) + Mathf.Max(0f, extraMult);
        float aff = AffinityMult(e.affinity, ch);   // ring-investment scaling term, layered on the base-stat roll
        // Magic → MATK roll (unmissable). Physical → ATK roll. Crit type → ATK roll with a FORCED crit (its
        // armour-pierce is the deferred combat-pass TODO; the forced crit is the part we can honour now).
        int roll = e.damageType == DamageType.Magic
            ? ch.RollMagicDamage(out crit)
            : ch.RollDamage(out crit, 0, e.damageType == DamageType.Crit);
        return Mathf.Max(1, Mathf.RoundToInt(roll * mult * aff * Mathf.Max(0f, comboMult)) + Mathf.Max(0, extraFlat));
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
    static int   ScaledAmount(SkillEffect e, Character caster)    => Mathf.Max(0, Mathf.RoundToInt(e.amount * AffinityMult(e.affinity, caster)));
    static float ScaledMagnitude(SkillEffect e, Character caster) => e.magnitude * (e.kind == SkillEffectKind.Status && e.chance > 0f ? 1f : AffinityMult(e.affinity, caster));
    static float ScaledSeconds(SkillEffect e, Character caster)   => e.seconds   * AffinityMult(e.durationAffinity, caster);
}
