using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONE place a data-authored <see cref="SkillEffect"/> (fired by a <see cref="SkillRunner"/> tick) becomes real
/// gameplay. It maps each intent onto the SAME combat sinks skills already call — a mob's <see cref="Health"/>, the
/// player's <see cref="Character"/>, the shared <see cref="Status"/> front door — so a data skill is just a new CALLER
/// of the existing damage/heal/status paths, never a second combat engine.
///
/// <para><b>Per-hit model (single-responsibility effects).</b> A binding is applied as a HIT LOOP whose length is the
/// Damage effect's <see cref="SkillEffect.hitCount"/> (1 if none). Each iteration is a "hit": the Damage effect lands
/// one strike, and every PROC effect in the binding (a Status or Execute with <see cref="SkillEffect.chance"/> &gt; 0)
/// rolls its chance THAT hit. Non-proc effects (chance == 0 — buffs, heals, a guaranteed status) apply once, on hit 0.
/// So a stun-proc or execute never lives INSIDE the Damage effect; it's its own effect that triggers off the multi-hit.</para>
/// </summary>
public static class SkillEffectApplier
{
    // Seconds between the strikes of a multi-hit (hitCount > 1) flurry — the RO cascade timing.
    const float MultiHitInterval = 0.2f;
    // Reused snapshot of the mobs a burst reaches, so a multi-hit flurry can be handed to MultiHitBurst without a
    // per-cast scan alloc. Cleared at the start of each ApplyBurst; MultiHitBurst copies it before the caller reuses it.
    static readonly List<MonsterAI3D> burstTargets = new List<MonsterAI3D>(32);

    /// <summary>Apply the binding's effects to whoever the tick reaches — an AoE scan (Area) or the cast's designated
    /// unit (Single). Zero-alloc: a squared-distance sweep over the small Active list, no physics / Find / GetComponent.</summary>
    public static void ApplyBurst(Vector3 point, float radius, SkillScope scope, SkillTargets targets,
                                  List<SkillEffect> effects, Transform singleTarget,
                                  float extraMult = 0f, int extraFlat = 0, float comboMult = 1f,
                                  Sprite srcIcon = null, string srcName = null,
                                  Vector3? aimPoint = null)
    {
        if (targets == SkillTargets.None || effects == null || effects.Count == 0) return;

        // Where a self-relocation (Teleport / Dash) sends the caster: the AIM point, NOT the tick's VFX anchor point.
        // The two differ once a ground cast keeps origin(caster) ≠ target(aim), and a teleport must always go to the
        // aimed spot regardless of which anchor the VFX author chose for the visual. Defaults to point for old callers.
        Vector3 selfPoint = aimPoint ?? point;

        // SINGLE scope: ignore the radius, land straight on the cast's designated unit. Falls back to Area if unset.
        if (scope == SkillScope.Single && singleTarget != null)
        {
            var mob = singleTarget.GetComponentInParent<MonsterAI3D>();
            if (mob != null)
            {
                if ((targets & SkillTargets.Mobs) != 0 && mob.Health != null && !mob.Health.IsDead)
                {
                    burstTargets.Clear(); burstTargets.Add(mob);
                    DispatchMobHits(effects, burstTargets, extraMult, extraFlat, comboMult);
                }
            }
            else if ((targets & SkillTargets.Player) != 0)
                ApplyEffectsToPlayer(effects, selfPoint, extraMult, extraFlat, comboMult, srcIcon, srcName);
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
            DispatchMobHits(effects, burstTargets, extraMult, extraFlat, comboMult);
        }

        if ((targets & SkillTargets.Player) != 0)
        {
            // Player-targeted effects are SELF effects (buff / guard / regen / cleanse / teleport) — they land on the
            // caster regardless of where a ground cast dropped its point (a teleport's destination IS the point, and
            // the caster is by definition NOT standing on it). No skill is a "hazard zone that hits the player if
            // inside", so there's nothing the radius gate legitimately guarded here.
            if (Character.Instance != null && !Character.Instance.IsDead)
                ApplyEffectsToPlayer(effects, selfPoint, extraMult, extraFlat, comboMult, srcIcon, srcName);
        }
    }

    // The binding's strike count = the largest Damage hitCount (min 1). Each iteration is the trigger point where
    // proc effects roll.
    static int HitLoops(List<SkillEffect> effects)
    {
        int loops = 1;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e != null && e.kind == SkillEffectKind.Damage) loops = Mathf.Max(loops, EffectiveHits(e));
        }
        return loops;
    }

    // A Damage effect's strike count = authored hitCount scaled by its hit-count affinity (ring investment).
    // hits = round(hitCount × mult). The ONE source of truth for both the flurry length (HitLoops) and each Damage's
    // per-strike cap, so they can't disagree. AffinityMult is stable within a cast (ring investment doesn't change).
    // Public so the tooltip shows the SAME live count the runtime will land (no parallel formula).
    public static int EffectiveHits(SkillEffect e)
        => Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, e.hitCount) * AffinityMult(e.hitCountAffinity)));

    // Single hit (the common case): apply instantly to each mob, unchanged. Multi-hit (hitCount > 1): hand the mob
    // snapshot to MultiHitBurst so the strikes CASCADE over time and each mob shows ONE rising yellow combo total,
    // exactly like RO — instead of N overlapping numbers dumped in a single frame.
    static void DispatchMobHits(List<SkillEffect> effects, List<MonsterAI3D> mobs,
                                float extraMult, int extraFlat, float comboMult)
    {
        if (mobs.Count == 0) return;
        if (HitLoops(effects) <= 1)
        {
            for (int i = 0; i < mobs.Count; i++)
                ApplyEffectsToMob(effects, mobs[i], extraMult, extraFlat, comboMult);
            return;
        }
        MultiHitBurst.Run(mobs, HitLoops(effects), MultiHitInterval,
            (ai, hit) => ApplyHitToMob(effects, ai, hit, extraMult, extraFlat, comboMult));
    }

    // One beat of a multi-hit flurry on ONE mob: land every Damage effect (overkill-safe so the running total keeps
    // climbing even past the kill, RO-style) and roll the binding's procs THIS hit. Returns the damage dealt so the
    // yellow combo total can accumulate it.
    static int ApplyHitToMob(List<SkillEffect> effects, MonsterAI3D ai, int hit,
                             float extraMult, int extraFlat, float comboMult)
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
                if (hit >= EffectiveHits(e)) continue;
                int power = RollPower(e, extraMult, extraFlat, comboMult, out bool crit);
                // Both magic and physical use an overkill-safe sink here, so a multi-hit flurry keeps its numbers,
                // hit sparks AND per-strike sound climbing past the kill — the whole count runs to the end, RO-style.
                dealt += e.damageType == DamageType.Magic
                    ? h.TakeMagicDamageOverkill(power, crit, e.element)
                    : h.TakeDamageOverkill(power, crit, e.element);
            }
            else
                ApplyOneToMob(e, ai, h, hit, extraMult, extraFlat, comboMult);   // procs / status / one-shots, unchanged
        }
        return dealt;
    }

    // A proc-type effect (chance > 0) rolls PER HIT; a non-proc effect (chance == 0) applies ONCE, on the first hit.
    // A STATUS proc puts its affinity on its CHANCE (a stun has no magnitude — its only strength is how often it lands,
    // e.g. Accuracy investment → higher stun chance). Other procs (Execute) put affinity on their own output (damage),
    // so their chance stays the authored value.
    static bool Fires(SkillEffect e, int hitIndex)
    {
        if (e.chance <= 0f) return hitIndex == 0;
        float c = e.kind == SkillEffectKind.Status ? Mathf.Clamp01(e.chance * AffinityMult(e.affinity)) : e.chance;
        return UnityEngine.Random.value < c;
    }

    // ---- mob ----------------------------------------------------------------
    /// <summary>Run the hit loop for one MOB: each hit lands the Damage strike + rolls the binding's procs.</summary>
    public static void ApplyEffectsToMob(List<SkillEffect> effects, MonsterAI3D ai,
                                         float extraMult = 0f, int extraFlat = 0, float comboMult = 1f)
    {
        if (effects == null || ai == null) return;
        var h = ai.Health;
        if (h == null) return;
        int loops = HitLoops(effects);
        for (int n = 0; n < loops; n++)
            for (int i = 0; i < effects.Count; i++)
                ApplyOneToMob(effects[i], ai, h, n, extraMult, extraFlat, comboMult);
    }

    static void ApplyOneToMob(SkillEffect e, MonsterAI3D ai, Health h, int hit,
                              float extraMult, int extraFlat, float comboMult)
    {
        if (e == null) return;
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                if (hit >= EffectiveHits(e)) return;   // this Damage's strikes are spent
                int power = RollPower(e, extraMult, extraFlat, comboMult, out bool crit);
                if (e.damageType == DamageType.Magic) h.TakeMagicDamage(power, crit, e.element);
                else h.TakeDamage(power, crit, -1, e.element);   // −1 HIT = always-lands skill hit (no accuracy roll)
                break;
            case SkillEffectKind.Execute:
                if (Fires(e, hit)) ApplyExecute(e, h, extraMult, extraFlat, comboMult);
                break;
            case SkillEffectKind.Status:
                if (Fires(e, hit)) Status.Apply(ai.gameObject, e.ailment, ScaledSeconds(e), ScaledMagnitude(e));
                break;
            case SkillEffectKind.Heal:
                if (hit == 0) h.Heal(ScaledAmount(e));
                break;
            case SkillEffectKind.Haste:
                if (Fires(e, hit)) { var s = Status.Of(ai.gameObject); if (s != null) s.Haste(ScaledMagnitude(e), ScaledSeconds(e)); }
                break;
            case SkillEffectKind.Taunt:
                if (hit == 0) ai.Taunt(ScaledSeconds(e));
                break;
            case SkillEffectKind.Vulnerability:
                if (hit == 0) h.BeginVulnerable(e.magnitude, ScaledSeconds(e));
                break;
            case SkillEffectKind.Knockback:
                // Shove the mob AWAY from the caster (magnitude = speed). Caster position is the origin so no cast
                // point needs threading through the hit loop; falls back to the mob's own spot if there's no caster.
                if (hit == 0) { var kc = Character.Instance; Vector3 kf = kc != null ? kc.transform.position : ai.transform.position; ai.Knockback(kf, e.magnitude, ScaledSeconds(e), 0f); }
                break;
            case SkillEffectKind.Pull:
                // Drag the mob TOWARD the caster: shove it away from a point mirrored through the mob = toward the caster.
                if (hit == 0) { var kc = Character.Instance; Vector3 kf = kc != null ? kc.transform.position : ai.transform.position; ai.Knockback(2f * ai.transform.position - kf, e.magnitude, ScaledSeconds(e), 0f); }
                break;
            // Guard / Teleport / Dash / Buff / Regen / Cleanse / ManaLeech / Reflect are self-only (they land on the caster, not a mob) — no-op here.
        }
    }

    // Execute: a self-contained bonus hit scaled by the target's MISSING HP (proc'd per hit by the caller). magnitude =
    // execute power: bonus = roll × magnitude × (1 − HP fraction), so it's tiny at full HP and huge near death.
    static void ApplyExecute(SkillEffect e, Health h, float extraMult, int extraFlat, float comboMult)
    {
        if (h.maxHealth <= 0 || e.magnitude <= 0f) return;
        float missing = 1f - Mathf.Clamp01((float)h.Current / h.maxHealth);
        if (missing <= 0f) return;
        int roll = RollPower(e, extraMult, extraFlat, comboMult, out bool crit);
        int bonus = Mathf.RoundToInt(roll * e.magnitude * missing);
        if (bonus <= 0) return;
        if (e.damageType == DamageType.Magic) h.TakeMagicDamage(bonus, crit, e.element);
        else h.TakeDamage(bonus, crit, -1, e.element);
    }

    // ---- player -------------------------------------------------------------
    /// <summary>Run the hit loop for the PLAYER. <paramref name="point"/> is the tick's world point — the teleport
    /// destination for a <see cref="SkillEffectKind.Teleport"/> effect.</summary>
    public static void ApplyEffectsToPlayer(List<SkillEffect> effects, Vector3 point,
                                            float extraMult = 0f, int extraFlat = 0, float comboMult = 1f,
                                            Sprite srcIcon = null, string srcName = null)
    {
        var ch = Character.Instance;
        if (effects == null || ch == null) return;
        int loops = HitLoops(effects);
        for (int n = 0; n < loops; n++)
            for (int i = 0; i < effects.Count; i++)
                ApplyOneToPlayer(effects[i], ch, point, n, extraMult, extraFlat, comboMult, srcIcon, srcName);
    }

    static void ApplyOneToPlayer(SkillEffect e, Character ch, Vector3 point, int hit,
                                 float extraMult, int extraFlat, float comboMult, Sprite srcIcon, string srcName)
    {
        if (e == null) return;
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                if (hit >= EffectiveHits(e)) return;
                int power = RollPower(e, extraMult, extraFlat, comboMult, out _);
                if (e.damageType == DamageType.Magic) ch.TakeMagicDamage(power, e.element);
                else ch.TakeDamage(power, -1, e.element);        // −1 HIT = always-lands (skill/environmental hit)
                break;
            case SkillEffectKind.Heal:
                if (hit == 0) ch.Heal(ScaledAmount(e));
                break;
            case SkillEffectKind.Status:
                if (Fires(e, hit) && StatusController.Player != null) StatusController.Player.Apply(e.ailment, ScaledSeconds(e), ScaledMagnitude(e));
                break;
            case SkillEffectKind.Haste:
                if (hit == 0 && StatusController.Player != null) { StatusController.Player.Haste(ScaledMagnitude(e), ScaledSeconds(e)); RegisterBuff(ch, srcIcon, srcName, ScaledSeconds(e), e); }
                break;
            case SkillEffectKind.Guard:
                if (hit == 0) { ch.BeginGuard(ScaledSeconds(e), e.magnitude); RegisterBuff(ch, srcIcon, srcName, ScaledSeconds(e), e); }
                break;
            case SkillEffectKind.Teleport:
                if (hit == 0)
                {
                    var pc = StatusController.Player != null ? StatusController.Player.GetComponent<Player3DController>() : null;
                    if (pc != null) pc.Teleport(point);   // instant blink to the aimed (range-clamped) point
                }
                break;
            case SkillEffectKind.Dash:
                if (hit == 0)
                {
                    var pc = StatusController.Player != null ? StatusController.Player.GetComponent<Player3DController>() : null;
                    if (pc != null) pc.BeginGlideTo(point);   // pure A→B glide to the aimed (range-clamped) point — no animation
                }
                break;
            case SkillEffectKind.Buff:
                if (hit == 0) { ApplyBuff(e, ch); RegisterBuff(ch, srcIcon, srcName, ScaledSeconds(e), e); }
                break;
            case SkillEffectKind.Regen:
                if (hit == 0) { ch.BeginRegen(Mathf.Max(1, Mathf.RoundToInt(ScaledMagnitude(e))), ScaledSeconds(e)); RegisterBuff(ch, srcIcon, srcName, ScaledSeconds(e), e); }
                break;
            case SkillEffectKind.Cleanse:
                if (hit == 0 && StatusController.Player != null) StatusController.Player.Cleanse(Mathf.Max(1, e.amount));
                break;
            case SkillEffectKind.ManaLeech:
                if (hit == 0) { ch.BeginManaLeech(ScaledSeconds(e), e.magnitude); RegisterBuff(ch, srcIcon, srcName, ScaledSeconds(e), e); }
                break;
            case SkillEffectKind.Reflect:
                if (hit == 0) { ch.BeginReflect(ScaledSeconds(e), e.magnitude); RegisterBuff(ch, srcIcon, srcName, ScaledSeconds(e), e); }
                break;
            // Execute / Taunt / Vulnerability / Knockback / Pull are aimed at mobs — no-op on the player.
        }
    }

    // Mirror a just-applied self-buff onto the on-screen buff bar, tagged with the SOURCE SKILL's icon + name so a
    // re-cast refreshes the same cell. Skipped when the skill provided no identity (e.g. a combo-fired effect).
    static void RegisterBuff(Character ch, Sprite srcIcon, string srcName, float seconds, SkillEffect e)
    {
        if (ch == null || string.IsNullOrEmpty(srcName)) return;
        ch.Buffs.Add(srcIcon, srcName, seconds, Color.white, SkillTooltip.DescribeEffect(e));
    }

    // Route a self-buff onto the right timed channel. The buff amount (multiplier) scales off the affinity list; the
    // seconds off the duration list. Move speed is NOT a buffStat — the Haste kind owns move speed (no dual path).
    static void ApplyBuff(SkillEffect e, Character ch)
    {
        // AMPLIFY buffs use a FLAT magnitude (NOT affinity-scaled): a DPS can't out-buff via their own offense. The
        // support's contribution is UPTIME (duration scales off sustain stats) and, on the Damage buff, the RAMP
        // ceiling (a longer buff climbs higher). See design "Support Scaling".
        float mult = e.magnitude <= 0f ? 1f : e.magnitude;
        float sec  = ScaledSeconds(e);
        switch (e.buffStat)
        {
            case BuffStat.Damage:      ch.BuffDamage(mult, sec, e.rampDelay, e.rampPerStep, e.rampInterval, e.rampCap); break;
            case BuffStat.AttackSpeed: ch.BuffAttackSpeed(mult, sec); break;
            case BuffStat.CastSpeed:   ch.BuffCastSpeed(mult, sec); break;
        }
    }

    // Damage value = the caster's rolled hit × (the effect's multiplier + per-level growth) × ring affinity × the combo
    // ramp, + a per-level flat bonus. Crit-aware so a tick crits like any other skill. No allocation.
    static int RollPower(SkillEffect e, float extraMult, int extraFlat, float comboMult, out bool crit)
    {
        crit = false;
        var ch = Character.Instance;
        if (ch == null) return 1;
        float mult = (e.damageMult <= 0f ? 1f : e.damageMult) + Mathf.Max(0f, extraMult);
        float aff = AffinityMult(e.affinity);   // ring-investment scaling term, layered on the base-stat roll
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
    public static float AffinityMult(List<StatWeight> weights)
    {
        if (weights == null || weights.Count == 0) return 1f;
        var ch = Character.Instance;
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
    static int   ScaledAmount(SkillEffect e)    => Mathf.Max(0, Mathf.RoundToInt(e.amount * AffinityMult(e.affinity)));
    static float ScaledMagnitude(SkillEffect e) => e.magnitude * (e.kind == SkillEffectKind.Status && e.chance > 0f ? 1f : AffinityMult(e.affinity));
    static float ScaledSeconds(SkillEffect e)   => e.seconds   * AffinityMult(e.durationAffinity);
}
