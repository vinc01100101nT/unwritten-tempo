using System.Collections;
using UnityEngine;

/// <summary>
/// 3D melee swing executor — lean billboard melee (no legacy 2D mouse
/// helpers, which <see cref="Player3DController"/> handles via a camera raycast). The controller
/// owns the target; this only knows how to swing: it gates on <see cref="cooldown"/> and applies
/// damage at the END of a short <see cref="windupTime"/>, so a swing can be cancelled before it
/// lands. Uses <see cref="Character"/>'s attack if present, else <see cref="damage"/> (the spike
/// has no Character, so it falls back).
/// </summary>
public class Player3DAttacker : MonoBehaviour
{
    [Tooltip("How close (world units, XZ) you must be to land a hit. This is the FULL-SIZE reach; the live reach " +
             "is this × UnitScale.MeleeReachFactor (see EffectiveRange), so it shrinks with resized units.")]
    public float range = 0.5f;
    [Tooltip("Once in range, stay 'engaged' (stand & swing) until the target passes range×this. Prevents the " +
             "stand-vs-chase decision from thrashing frame-to-frame against a target that hovers at the range " +
             "edge (e.g. a kiting caster) — which otherwise machine-guns the step SFX and freezes the pose.")]
    public float rangeHysteresis = 1.4f;
    [Tooltip("Seconds between auto-attacks.")]
    public float cooldown = 0.6f;
    [Tooltip("Delay between a swing starting and the hit landing (cancellable window).")]
    public float windupTime = 0.18f;
    [Tooltip("Fraction (0..1) of the swing at which the basic-attack hit LANDS. The animation still plays the " +
             "FULL swing for the same cadence; the damage just connects at this point — 0.7 = 70% in, the " +
             "natural contact moment — instead of right at the end.")]
    [Range(0.1f, 1f)] public float hitTiming = 0.70f;
    [Tooltip("Fallback damage when there's no Character (e.g. the standalone spike).")]
    public int damage = 3;
    [Tooltip("The weapon's attack ELEMENT (RO property), applied to basic swings and skill strikes vs the " +
             "target's armor element. Neutral = no interaction; a fire sword would set this to Fire.")]
    public Element meleeElement = Element.Neutral;

    [Header("Slash VFX (Effect Library — the swing arc at the target)")]
    [Tooltip("Effect-library id for the swing/slash. Empty = none.")]
    public string slashVfxId = "";
    public float slashVfxScale = 1f;

    [Header("Impact VFX (3D particle from the Effect Library — Vfx.PlayAt)")]
    [Tooltip("Effect-library id spawned at the target on a LANDED basic hit. Empty = none.")]
    public string hitVfxId = "hit.holy";
    [Tooltip("Base size of the impact VFX on a normal hit.")]
    public float hitVfxScale = 1f;
    [Tooltip("Size multiplier applied on a CRITICAL hit — the crit reads as a bigger burst.")]
    public float critVfxScale = 1.7f;

    [Header("Audio (used when the equipped weapon has none of its own)")]
    public SfxSet defaultAttackSfx;   // swing
    public SfxSet defaultHitSfx;      // impact

    float nextSwing;
    Coroutine swing;
    bool engaged;                         // sticky in-range state (hysteresis) for the stand-vs-chase decision
    Health engagedTarget;                 // who `engaged` refers to, so a new target re-evaluates from scratch
    public bool Swinging => swing != null;

    /// <summary>
    /// Time-scale of the attack MOTION — 1 / attack-speed, so a faster ASPD shrinks every part of
    /// the swing (windup, slash FX, the held pose) the same way it shrinks the cooldown. &lt;1 means
    /// the animation plays quicker. The pose hold in <see cref="Player3DController"/> reads this too,
    /// so the whole attack stays in sync with the swing rate.
    /// </summary>
    public static float MotionScale =>
        Character.Instance != null ? 1f / Mathf.Max(0.01f, Character.Instance.AttackSpeedMult) : 1f;

    /// <summary>How long one whole swing lasts (start → next swing can begin), scaled by attack-speed. The attack
    /// animation is sped to play fully within this window (see BakedPlayerVisual); the hit lands partway through at
    /// <see cref="hitTiming"/> (default 70%) — the contact moment. Equals the swing interval, so DPS is unchanged —
    /// only WHEN inside the window the hit connects moves.</summary>
    public float SwingTime => Mathf.Max(0.05f, cooldown * MotionScale);

    /// <summary>The authored <see cref="range"/> rescaled to the CURRENT unit size (see
    /// <see cref="UnitScale.MeleeReachFactor"/>). When both player and mobs render at authored scale this equals
    /// <see cref="range"/> exactly; once mobs are shrunk (e.g. MobRelative 0.3) the reach shrinks with them, so the
    /// player closes in and swings at contact instead of from a fixed 1.4-unit gap that dwarfs a tiny billboard.</summary>
    public float EffectiveRange => range * UnitScale.MeleeReachFactor;

    /// <summary>Strict planar range — used to actually LAND a hit (TryAttack / the windup check).</summary>
    public bool InRange(Health t) => t != null && !t.IsDead && PlanarDist(t) <= EffectiveRange;

    /// <summary>Sticky version of <see cref="InRange"/> for the controller's stand-vs-chase decision:
    /// turns true at <see cref="range"/> and stays true until the target passes range×<see cref="rangeHysteresis"/>.
    /// Landing a hit still uses the strict <see cref="InRange"/>, so the extra margin never extends reach.</summary>
    public bool EngagedWith(Health t)
    {
        if (t == null || t.IsDead) { engaged = false; engagedTarget = null; return false; }
        if (t != engagedTarget) { engaged = false; engagedTarget = t; }   // new target → must reach `range` afresh
        float r = EffectiveRange;
        float d = PlanarDist(t);
        if (d <= r) engaged = true;
        else if (d > r * Mathf.Max(1f, rangeHysteresis)) engaged = false;
        return engaged;
    }

    float PlanarDist(Health t)
    {
        Vector3 a = transform.position, b = t.transform.position;
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public void TryAttack(Health t)
    {
        if (Time.time < nextSwing || swing != null || !InRange(t)) return;
        // AttackSpeed shortens the gap between swings AND the motion itself (faster = higher mult),
        // so the visible swing keeps pace with the cadence instead of lagging at high ASPD.
        nextSwing = Time.time + cooldown * MotionScale;
        Sfx.Play(WeaponSfx(w => w.attackSfx, defaultAttackSfx));   // swing
        swing = StartCoroutine(SwingThenHit(t));
    }

    // The equipped weapon's set if it has clips, else the player default.
    static SfxSet WeaponSfx(System.Func<Item, SfxSet> pick, SfxSet fallback)
    {
        var w = Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
        var s = w != null ? pick(w) : null;
        return s != null && !s.Empty ? s : fallback;
    }

    IEnumerator SwingThenHit(Health t)
    {
        // The whole swing spans SwingTime (scales with attack-speed; BakedPlayerVisual speeds the clip to fill it).
        // The hit lands at hitTiming OF that window — the contact moment, ~70% in — not at the very end. We then
        // hold `swing` for the remainder so the animation finishes and cadence (= SwingTime) is unchanged.
        float scale = MotionScale;
        float swingTime = SwingTime;
        float impactAt = swingTime * Mathf.Clamp01(hitTiming);
        yield return new WaitForSeconds(impactAt);

        // RO-style commit: once the swing has STARTED (gated on InRange in TryAttack), the blow LANDS regardless of how
        // far the target has since walked — the attack is animation-locked, not re-checked at impact. We only skip a
        // target that no longer exists or already died during the windup. (Melee is decided at swing start.)
        if (t != null && !t.IsDead)
        {
            // Roll the hit through Character so CritChance/CritDamage apply (spike has no Character → flat damage).
            bool crit = false;
            int power = Character.Instance != null ? Character.Instance.RollDamage(out crit, t.luk) : damage;   // t.luk = crit shield

            // Slash FX at the target (a bit above its feet), billboarded by SlashEffect so it faces the camera.
            Vector3 fxPos = t.transform.position + Vector3.up * 0.4f;
            if (!string.IsNullOrEmpty(slashVfxId)) Vfx.PlayAt(slashVfxId, fxPos, Quaternion.identity, slashVfxScale);

            // Pass our HIT so the target rolls it against its FLEE (RO accuracy); −1 in the Character-less spike.
            int hit = Character.Instance != null ? Character.Instance.Hit : -1;
            if (hit >= 0 && StatusController.Player != null) hit = Mathf.Max(0, hit - StatusController.Player.HitPenalty);  // Blind
            int dealt = t.TakeDamage(power, crit, hit, meleeElement);
            if (dealt > 0)
            {
                // Impact SOUND now plays FROM the mob in Health.TakeDamage (received_hit/crit, spatial) — no 2D play here.
                // 3D impact burst at the target; a crit reads as a bigger punch (neutral element → no tint).
                // Anchor at the raw target PIVOT (not fxPos's +0.4) so the height comes entirely from the
                // effect's tuned def.offset — matching the EffectTuner's "Spawn on Mob" anchor. Adding fxPos's
                // +0.4 here double-counted with the offset and floated the hit above the mob.
                if (!string.IsNullOrEmpty(hitVfxId))
                    Vfx.PlayAt(hitVfxId, t.transform.position, Vfx.VfxParams.Scaled(hitVfxScale * (crit ? critVfxScale : 1f)));
            }
        }

        // Let the rest of the swing play out before freeing the slot, so the pose/animation completes and the next
        // swing still starts one SwingTime after this one began (DPS unchanged).
        float rest = swingTime - impactAt;
        if (rest > 0f) yield return new WaitForSeconds(rest);
        swing = null;
    }

    public void Cancel() { if (swing != null) { StopCoroutine(swing); swing = null; } engaged = false; engagedTarget = null; }

    /// <summary>Execute a SKILL strike (e.g. the Swordsman's Bash) on <paramref name="t"/>: a heavier,
    /// always-landing hit that reuses the normal swing's windup → slash-FX → damage pipeline, then
    /// staggers the target. Independent of the auto-swing cooldown (the skill owns its own), but shares
    /// the <c>swing</c> flag so the controller shows the attack pose and no double-swing overlaps.</summary>
    public void Strike(Health t, float damageMult, float fxScale, SfxSet swingSfx, SfxSet hitSfx, float staggerSecs)
        => Strike(t, SkillContext.Default(damageMult, fxScale, staggerSecs), swingSfx, hitSfx);

    /// <summary>Context-aware skill strike: same windup→slash-FX→damage pipeline, but the cast's
    /// <see cref="SkillContext"/> (built from the skill's modifier loadout) shapes damage, element, the
    /// on-hit status (stagger/slow), lifesteal, and cleave. The plain overload above just feeds a default
    /// context, so existing callers (e.g. Charge) are unchanged.</summary>
    public void Strike(Health t, in SkillContext ctx, SfxSet swingSfx, SfxSet hitSfx)
    {
        if (t == null || t.IsDead || swing != null) return;
        // SelfEffect that fires on CAST (no hit needed) — the Bulwark self-shield mod.
        if (ctx.ShieldSecs > 0f && ctx.ShieldCut > 0f && Character.Instance != null)
            Character.Instance.BeginGuard(ctx.ShieldSecs, ctx.ShieldCut);
        Sfx.Play(swingSfx != null && !swingSfx.Empty ? swingSfx : WeaponSfx(w => w.attackSfx, defaultAttackSfx));
        swing = StartCoroutine(StrikeRoutine(t, ctx, hitSfx));
    }

    static readonly Collider[] cleaveBuf = new Collider[16];

    // Coroutines can't take `in`/`ref` params, so the context is copied by value here (a small struct).
    IEnumerator StrikeRoutine(Health t, SkillContext ctx, SfxSet hitSfx)
    {
        float scale = MotionScale;
        yield return new WaitForSeconds(windupTime * scale);
        swing = null;
        if (t == null || t.IsDead) yield break;
        if (PlanarDist(t) > EffectiveRange * 1.6f) yield break;   // target slipped out of the skill's (extended) reach

        bool crit = false;
        int basePower = Character.Instance != null ? Character.Instance.RollDamage(out crit, t.luk, ctx.ForceCrit) : damage;
        int power = Mathf.RoundToInt(basePower * Mathf.Max(1f, ctx.DamageMult));
        Element element = ctx.ConvertElement ? ctx.NewElement : meleeElement;

        Vector3 fxPos = t.transform.position + Vector3.up * 0.4f;
        if (!string.IsNullOrEmpty(slashVfxId)) Vfx.PlayAt(slashVfxId, fxPos, Quaternion.identity, slashVfxScale * Mathf.Max(0.1f, ctx.FxScale));

        // Skills always LAND (no FLEE roll, hit −1), but they still respect DEF unless they actually crit —
        // so pass the real rolled `crit` (which also colours the number gold + bypasses DEF), RO-style.
        // MultiHit (Flurry mod) splits the strike into N hits sharing the total — visual flair, same DPS;
        // a single TakeDamage when HitCount is 1 (the default), so the common path is unchanged.
        int hitCount = Mathf.Max(1, ctx.HitCount);
        int perHit = Mathf.Max(1, Mathf.RoundToInt(power / (float)hitCount));
        int dealt = 0;
        for (int h = 0; h < hitCount && !t.IsDead; h++)
            dealt += t.TakeDamage(perHit, crit, -1, element);
        if (dealt > 0)
        {
            // Impact SOUND now plays FROM the mob in Health.TakeDamage (received_hit_skill, spatial) — no 2D play here.
            ApplyOnHit(t, ctx);
            if (ctx.LifestealFracDealt > 0f && Character.Instance != null)
                Character.Instance.LeechHeal(Mathf.RoundToInt(dealt * ctx.LifestealFracDealt));
        }

        // Cleave (Targeting phase): also strike up to ExtraTargets OTHER mobs within CleaveRadius. Runs once
        // per cast (~every few s), so the NonAlloc overlap + per-hit GetComponentInParent are fine here.
        if (ctx.ExtraTargets > 0 && ctx.CleaveRadius > 0f)
        {
            int n = Physics.OverlapSphereNonAlloc(t.transform.position, ctx.CleaveRadius, cleaveBuf);
            int extra = 0;
            for (int i = 0; i < n && extra < ctx.ExtraTargets; i++)
            {
                var h = cleaveBuf[i].GetComponentInParent<Health>();
                if (h == null || h == t || h.IsDead || h.GetComponent<MonsterAI3D>() == null) continue;
                Vector3 p = h.transform.position + Vector3.up * 0.4f;
                if (!string.IsNullOrEmpty(slashVfxId)) Vfx.PlayAt(slashVfxId, p, Quaternion.identity, slashVfxScale * Mathf.Max(0.1f, ctx.FxScale));
                if (h.TakeDamage(power, crit, -1, element) > 0) ApplyOnHit(h, ctx);
                extra++;
            }
        }
    }

    void ApplyOnHit(Health h, in SkillContext ctx)
    {
        var ai = h.GetComponent<MonsterAI3D>();
        if (ai == null) return;
        // Knockback (Repelling mod) shoves from the player and carries its own freeze, so it subsumes the
        // separate Stagger; a plain Stagger applies only when there's no shove.
        if (ctx.KnockbackSpeed > 0f)
            ai.Knockback(transform.position, ctx.KnockbackSpeed, ctx.KnockbackSecs, Mathf.Max(ctx.StaggerSecs, ctx.KnockbackSecs));
        else if (ctx.StaggerSecs > 0f) ai.Stagger(ctx.StaggerSecs);
        if (ctx.SlowSecs > 0f && ctx.SlowFactor < 1f) ai.Slow(ctx.SlowFactor, ctx.SlowSecs);
        if (ctx.RootSecs > 0f) ai.Slow(0f, ctx.RootSecs);   // Root (Entangling mod) = a hard slow to ~0
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? EffectiveRange : range);
    }
}
