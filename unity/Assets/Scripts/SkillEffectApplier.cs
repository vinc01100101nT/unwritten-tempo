using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONE place a data-authored <see cref="SkillEffect"/> (fired by a <see cref="SkillRunner"/> tick) becomes real
/// gameplay. It maps each intent onto the SAME combat sinks skills already call — a mob's <see cref="Health"/>, the
/// player's <see cref="Character"/>, the shared <see cref="Status"/> front door — so a data skill is just a new CALLER
/// of the existing damage/heal/status paths, never a second combat engine.
///
/// <para><b>Two passes per tick (caster side vs target side).</b> <see cref="SkillEffect.IsSelfVerb"/> partitions the
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

        // PASS 1 — CASTER effects (see SkillEffect.IsSelfVerb): they happen TO THE CASTER, so the tick's targeting has no say.
        // They run here exactly once, whatever the scope/affects are, and are excluded from the target pass below — so
        // "Single Target on a mob" can't swallow a Dash (the bug this split fixes), and a Self tick can't double-apply it.
        ApplyCasterEffects(effects, selfPoint, singleTarget, srcIcon, srcName, srcId, ch);

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

    static Player3DAttacker casterAtk;   // resolved lazily from the player rig; re-resolved if it's ever lost

    /// <summary>Where a caster RELOCATION (Dash / Teleport) actually LANDS. Aiming at a unit means "get to it", not
    /// "stand inside it": the caster arrives at melee reach, on the far side, rotated <see cref="LandingStepDegrees"/>
    /// from wherever it currently stands — so it never ends up stacked on the target's own spot, every dash reads as a
    /// cut THROUGH the target, and five in a row trace a star on the ground. Deliberately derived from the caster's
    /// CURRENT bearing rather than a stored step index: there is no sequence state to reset, an interrupted chain just
    /// resumes from wherever it stopped, and the pattern is the same on every unit-aimed relocation. A pure ground aim
    /// — no unit given and no mob standing on the spot — is used verbatim.</summary>
    static Vector3 RelocationDest(Vector3 aim, Transform unit)
    {
        if (casterAtk == null)
        {
            var pl = StatusController.Player;
            if (pl != null) casterAtk = pl.GetComponent<Player3DAttacker>();
            if (casterAtk == null) return aim;
        }
        float reach = casterAtk.EffectiveRange * LandingReachFactor;

        // The thing we're landing next to: the cast's picked unit, else whatever mob the aim point is sitting on (a
        // Ground-delivered dash at a victim's feet aims at the mob's own position, with no unit pick to read).
        Vector3 center;
        if (unit != null) center = unit.position;
        else if (!MobAt(aim, reach, out center)) return aim;

        // Bearing of the caster AS IT STANDS NOW, measured around the target, then stepped. A caster standing exactly
        // on the target has no bearing to step from — fall back to its facing so the hop still has a direction.
        Vector3 from = casterAtk.transform.position - center; from.y = 0f;
        if (from.sqrMagnitude < 1e-6f) from = casterAtk.transform.forward;
        float a = Mathf.Atan2(from.z, from.x) + LandingStepDegrees * Mathf.Deg2Rad;
        return new Vector3(center.x + Mathf.Cos(a) * reach, center.y, center.z + Mathf.Sin(a) * reach);
    }

    // The living mob nearest `point` within `radius` (planar), if any. Same zero-alloc sweep over the small Active
    // registry the AoE gather uses — no physics query, no GetComponent; runs once per relocation cast.
    static bool MobAt(Vector3 point, float radius, out Vector3 pos)
    {
        pos = point;
        var list = MonsterAI3D.Active;
        float best = radius * radius; bool found = false;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            Vector3 d = ai.transform.position - point; d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr > best) continue;
            best = sqr; pos = ai.transform.position; found = true;
        }
        return found;
    }

    // ---- caster (pass 1) ----------------------------------------------------
    // Every CASTER-side effect in the list, applied ONCE to the acting caster. No hit loop: a caster effect isn't a
    // strike, so a multi-hit flurry must not dash you N times (target effects own hitCount, see HitLoops).
    static void ApplyCasterEffects(List<SkillEffect> effects, Vector3 point, Transform aimUnit,
                                   Sprite srcIcon, string srcName, string srcId, Character ch)
    {
        if (ch == null || ch.IsDead) return;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e != null && SkillEffect.IsSelfVerb(e.kind)) ApplyOneToCaster(e, ch, point, aimUnit, srcIcon, srcName, srcId);
        }
    }

    /// <summary>One caster-side effect. <paramref name="point"/> is the AIM point — the destination a Dash/Teleport
    /// moves the caster to (resolved through <see cref="RelocationDest"/>, which lands it BESIDE an aimed unit rather
    /// than on top of it). <paramref name="aimUnit"/> is the cast's picked unit, if it had one.</summary>
    static void ApplyOneToCaster(SkillEffect e, Character ch, Vector3 point, Transform aimUnit, Sprite srcIcon, string srcName, string srcId)
    {
        // A durational self-effect tags its state entry with the SOURCE skill's icon + name + "what it does", so the
        // buff tray shows the same icon the hotbar uses, counting down. Set BEFORE the gameplay call (the store's
        // Apply reads the latch), cleared AFTER — the store is the single display source.
        bool tagDisplay = e.seconds > 0f;
        if (tagDisplay) ch.States.SetApplyContext(srcIcon, srcName, SkillTooltip.DescribeEffect(e),
                                                  e.stacking, e.stacksPerApply, e.stackLimit, srcId);
        switch (e.kind)
        {
            case SkillEffectKind.Guard:
                ch.BeginGuard(ScaledSeconds(e, ch), ScaledMagnitude(e, ch));
                break;
            case SkillEffectKind.Teleport:
                {
                    var pc = StatusController.Player != null ? StatusController.Player.GetComponent<Player3DController>() : null;
                    if (pc != null) pc.Teleport(RelocationDest(point, aimUnit));   // instant blink to the aimed (range-clamped) point
                }
                break;
            case SkillEffectKind.Dash:
                {
                    var pc = StatusController.Player != null ? StatusController.Player.GetComponent<Player3DController>() : null;
                    // pure A→B glide to the aimed (range-clamped) point — no animation
                    if (pc != null) pc.BeginGlideTo(RelocationDest(point, aimUnit), e.dashSpeedMult, e.dashEaseIn, e.dashEaseOut, e.dashEaseSeconds);
                }
                break;
            case SkillEffectKind.Buff:
                ApplyBuff(e, ch);
                break;
            case SkillEffectKind.Regen:
                ch.BeginRegen(Mathf.Max(1, Mathf.RoundToInt(ScaledMagnitude(e, ch))), ScaledSeconds(e, ch));
                break;
            case SkillEffectKind.Cleanse:
                if (StatusController.Player != null) StatusController.Player.Cleanse(Mathf.Max(1, e.amount));
                break;
            case SkillEffectKind.ManaLeech:
                ch.BeginManaLeech(ScaledSeconds(e, ch), e.magnitude);
                break;
            case SkillEffectKind.Reflect:
                ch.BeginReflect(ScaledSeconds(e, ch), e.magnitude);
                break;
            case SkillEffectKind.Shield:
                ch.BeginShield(ScaledAmount(e, ch), ScaledSeconds(e, ch));
                break;
            case SkillEffectKind.Parry:
                ch.BeginParry(ScaledSeconds(e, ch));   // charges ride the apply context above (stacks-per-apply)
                break;
        }
        if (tagDisplay) ch.States.ClearApplyContext();
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
                ApplyOneToMob(e, ai, h, hit, extraMult, extraFlat, comboMult, caster, srcIcon, srcName, srcId);   // procs / status / one-shots, unchanged
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
                ApplyOneToMob(effects[i], ai, h, n, extraMult, extraFlat, comboMult, caster, srcIcon, srcName, srcId);
    }

    static void ApplyOneToMob(SkillEffect e, MonsterAI3D ai, Health h, int hit,
                              float extraMult, int extraFlat, float comboMult, Character caster,
                              Sprite srcIcon = null, string srcName = null, string srcId = null)
    {
        if (e == null) return;
        // A durational debuff (Status / Haste / Vulnerability writes a state entry) carries the source skill's icon for
        // the mob's nameplate AND that effect's stacking rules. Set BEFORE the switch (the store's Apply reads this
        // context), cleared AFTER. Description is null (the nameplate has no hover card) — no per-mob string alloc.
        bool tagDisplay = e.kind == SkillEffectKind.Status || e.kind == SkillEffectKind.Haste || e.kind == SkillEffectKind.Vulnerability;
        if (tagDisplay) h.States.SetApplyContext(srcIcon, srcName, null, e.stacking, e.stacksPerApply, e.stackLimit, srcId);
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                if (hit >= EffectiveHits(e, caster)) return;   // this Damage's strikes are spent
                int power = RollPower(e, extraMult, extraFlat, comboMult, out bool crit, caster);
                // −1 HIT = always-lands skill hit (no accuracy roll); DamageInfo defaults attackerHit to −1.
                CombatAuthority.Apply(h, new DamageInfo(power, e.element, e.damageType == DamageType.Magic, crit));
                break;
            case SkillEffectKind.Execute:
                if (Fires(e, hit, caster)) ApplyExecute(e, h, extraMult, extraFlat, comboMult, caster);
                break;
            case SkillEffectKind.Status:
                if (Fires(e, hit, caster)) Status.Apply(ai.gameObject, e.ailment, ScaledSeconds(e, caster), ScaledMagnitude(e, caster));
                break;
            case SkillEffectKind.Heal:
                if (hit == 0) h.Heal(ScaledAmount(e, caster));
                break;
            case SkillEffectKind.Haste:
                if (Fires(e, hit, caster)) { var s = Status.Of(ai.gameObject); if (s != null) s.Haste(ScaledMagnitude(e, caster), ScaledSeconds(e, caster)); }
                break;
            case SkillEffectKind.Taunt:
                if (hit == 0) ai.Taunt(ScaledSeconds(e, caster));
                break;
            case SkillEffectKind.Vulnerability:
                if (hit == 0) h.BeginVulnerable(ScaledMagnitude(e, caster), ScaledSeconds(e, caster));
                break;
            case SkillEffectKind.Knockback:
                // Shove the mob AWAY from the caster (magnitude = speed). Caster position is the origin so no cast
                // point needs threading through the hit loop; falls back to the mob's own spot if there's no caster.
                if (hit == 0) { Vector3 kf = caster != null ? caster.transform.position : ai.transform.position; ai.Knockback(kf, e.magnitude, e.shoveSeconds, 0f); }
                break;
            case SkillEffectKind.Pull:
                // Drag the mob TOWARD the caster: shove it away from a point mirrored through the mob = toward the caster.
                if (hit == 0) { Vector3 kf = caster != null ? caster.transform.position : ai.transform.position; ai.Knockback(2f * ai.transform.position - kf, e.magnitude, e.shoveSeconds, 0f); }
                break;
            // Guard / Teleport / Dash / Buff / Regen / Cleanse / ManaLeech / Reflect are self-only (they land on the caster, not a mob) — no-op here.
        }
        if (tagDisplay) h.States.ClearApplyContext();
    }

    // The PLAYER-side twin of ApplyExecute: the same missing-HP curve turned on the caster (a risk effect — the closer
    // to death you are, the more it bites). Same formula, different HP source, so the two can't drift.
    static void ApplyExecuteToPlayer(SkillEffect e, Character ch, float extraMult, int extraFlat, float comboMult)
    {
        if (ch.MaxHP <= 0 || e.magnitude <= 0f) return;
        float missing = 1f - Mathf.Clamp01((float)ch.CurrentHP / ch.MaxHP);
        if (missing <= 0f) return;
        int bonus = Mathf.RoundToInt(RollPower(e, extraMult, extraFlat, comboMult, out _, ch) * e.magnitude * missing);
        if (bonus <= 0) return;
        CombatAuthority.Apply(ch, new DamageInfo(bonus, e.element, e.damageType == DamageType.Magic));
    }

    // Execute: a self-contained bonus hit scaled by the target's MISSING HP (proc'd per hit by the caller). magnitude =
    // execute power: bonus = roll × magnitude × (1 − HP fraction), so it's tiny at full HP and huge near death.
    static void ApplyExecute(SkillEffect e, Health h, float extraMult, int extraFlat, float comboMult, Character caster)
    {
        if (h.maxHealth <= 0 || e.magnitude <= 0f) return;
        float missing = 1f - Mathf.Clamp01((float)h.Current / h.maxHealth);
        if (missing <= 0f) return;
        int roll = RollPower(e, extraMult, extraFlat, comboMult, out bool crit, caster);
        int bonus = Mathf.RoundToInt(roll * e.magnitude * missing);
        if (bonus <= 0) return;
        CombatAuthority.Apply(h, new DamageInfo(bonus, e.element, e.damageType == DamageType.Magic, crit));
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
                ApplyOneToPlayer(effects[i], ch, n, extraMult, extraFlat, comboMult, srcIcon, srcName, srcId);
    }

    static void ApplyOneToPlayer(SkillEffect e, Character ch, int hit,
                                 float extraMult, int extraFlat, float comboMult, Sprite srcIcon, string srcName, string srcId)
    {
        if (e == null) return;
        // Caster effects already ran in pass 1 — skip them here so a self-targeted tick doesn't even churn the display
        // latch below for an effect this switch can't handle. The predicate (not a hand-copied kind list) keeps the two
        // passes from ever drifting apart.
        if (SkillEffect.IsSelfVerb(e.kind)) return;
        // A durational self-effect (seconds > 0) landing this cast tags its state entry with the SOURCE skill's icon +
        // name + "what it does", so the buff tray shows the same icon the hotbar uses, counting down. Set BEFORE the
        // gameplay call (the store's Apply reads the latch), clear AFTER — the store is the single display source now.
        // Set for EVERY hit, not just the first: a Status PROC can land on any hit of a flurry, and it must carry the
        // same stacking rules each time (a proc that stacks must stack on hit 3 exactly as it would on hit 0).
        bool tagDisplay = e.seconds > 0f;
        if (tagDisplay) ch.States.SetApplyContext(srcIcon, srcName, SkillTooltip.DescribeEffect(e),
                                                  e.stacking, e.stacksPerApply, e.stackLimit, srcId);
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                if (hit >= EffectiveHits(e, ch)) { if (tagDisplay) ch.States.ClearApplyContext(); return; }
                int power = RollPower(e, extraMult, extraFlat, comboMult, out _, ch);
                // −1 HIT = always-lands (skill/environmental hit); DamageInfo defaults attackerHit to −1.
                CombatAuthority.Apply(ch, new DamageInfo(power, e.element, e.damageType == DamageType.Magic));
                break;
            case SkillEffectKind.Heal:
                if (hit == 0) ch.Heal(ScaledAmount(e, ch));
                break;
            case SkillEffectKind.Status:
                if (Fires(e, hit, ch) && StatusController.Player != null) StatusController.Player.Apply(e.ailment, ScaledSeconds(e, ch), ScaledMagnitude(e, ch));
                break;
            case SkillEffectKind.Haste:
                if (hit == 0 && StatusController.Player != null) StatusController.Player.Haste(ScaledMagnitude(e, ch), ScaledSeconds(e, ch));
                break;
            case SkillEffectKind.Vulnerability:
                // Self armour-fray — the "fragile" half of a power-up (Overclock's 2× incoming). Writes the SAME state
                // a mob's BeginVulnerable does, at the same convention (magnitude = the EXTRA fraction), so one debuff
                // means one thing on either side of the fight.
                if (hit == 0 && ScaledSeconds(e, ch) > 0f)
                    ch.States.Apply(StateId.Vulnerability, ScaledSeconds(e, ch), Mathf.Max(0f, ScaledMagnitude(e, ch)));
                break;
            case SkillEffectKind.Execute:
                if (Fires(e, hit, ch)) ApplyExecuteToPlayer(e, ch, extraMult, extraFlat, comboMult);
                break;
            // Taunt is deliberately absent: it forces a MOB's aggro onto the caster, and there is no such thing as
            // "the player's aggro" — on yourself it would be a knob that means nothing. Don't "complete" it here.
            // Knockback / Pull are absent for a different reason: the shove sink is a mob mover (MonsterAI3D.Knockback);
            // the player has no equivalent movement primitive yet, so they'd be silent no-ops until one exists.
            // Guard / Teleport / Dash / Buff / Regen / Cleanse / ManaLeech / Reflect / Shield are CASTER effects: they
            // live in ApplyOneToCaster and ran in pass 1 of ApplyBurst. Deliberately absent here — the two switches are
            // a partition (see SkillEffect.IsSelfVerb), so a Self-targeted tick can't apply them a second time.
        }
        if (tagDisplay) ch.States.ClearApplyContext();
    }

    // Route a self-buff onto the right timed channel. The buff amount (multiplier) scales off the affinity list; the
    // seconds off the duration list. Move speed is NOT a buffStat — the Haste kind owns move speed (no dual path).
    static void ApplyBuff(SkillEffect e, Character ch)
    {
        // AMPLIFY buffs use a FLAT magnitude (NOT affinity-scaled): a DPS can't out-buff via their own offense. The
        // support's contribution is UPTIME (duration scales off sustain stats) and, on the Damage buff, the RAMP
        // ceiling (a longer buff climbs higher). See design "Support Scaling".
        float mult = e.magnitude <= 0f ? 1f : e.magnitude;
        float sec  = ScaledSeconds(e, ch);
        switch (e.buffStat)
        {
            case BuffStat.Damage:      ch.BuffDamage(mult, sec, e.rampDelay, e.rampPerStep, e.rampInterval, e.rampCap); break;
            case BuffStat.AttackSpeed: ch.BuffAttackSpeed(mult, sec); break;
            case BuffStat.CastSpeed:   ch.BuffCastSpeed(mult, sec); break;
        }
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
