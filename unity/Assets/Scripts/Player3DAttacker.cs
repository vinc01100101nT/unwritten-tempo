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
             "is this blended with the TARGET's size at each hit test (see RangeAgainst), so it tracks both bodies.")]
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
    [Header("Ranged (Bow / Firearm) — arrow projectile")]
    [Tooltip("Sprite-sheet for the arrow that flies to the target when a ranged weapon is equipped. Null = " +
             "the blow lands instantly (no visible projectile), same as melee.")]
    public Texture2D arrowSheet;
    public int arrowFrameSize = 16;
    public int arrowFrames = 1;
    public float arrowFps = 12f;
    [Tooltip("Arrow travel speed (world units/s). Higher = the hit connects sooner after the release frame.")]
    public float arrowSpeed = 14f;
    public float arrowScale = 1f;
    [Header("Audio (used when the equipped weapon has none of its own)")]
    public SfxSet defaultAttackSfx;   // swing
    public SfxSet defaultHitSfx;      // impact

    float nextSwing;
    bool attemptOpen;                     // an announced attack that hasn't started or been cancelled yet (see AnnounceAttackIntent)
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
    /// animation is sped to play fully within this window (see Character3DPose); the hit lands partway through at
    /// <see cref="hitTiming"/> (default 70%) — the contact moment. Equals the swing interval, so DPS is unchanged —
    /// only WHEN inside the window the hit connects moves.</summary>
    public float SwingTime => UnitOffense.AttackInterval(cooldown,
        Character.Instance != null ? Character.Instance.AttackSpeedMult : 1f);   // same rule a mob's cadence uses

    /// <summary>The FULL-SIZE reach before unit-scaling: the equipped weapon's <see cref="Item.attackRange"/> when it
    /// sets one (Weapon slot, &gt; 0 — a bow/spear reaches farther than a fist), else the attacker's own default
    /// <see cref="range"/> (unarmed / the standalone spike with no Character). Read at use-time from the equipped
    /// weapon — same pattern as <see cref="MeleeElement"/>, the audio lookup, and the stance brain — so there's no per-equip copy to
    /// keep in sync; the Item stays the single owner of weapon reach.</summary>
    public float BaseRange
    {
        get
        {
            var w = Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
            return WeaponReach.Of(w, range);   // the SHARED rule — a mob resolves its reach through this same one

        }
    }

    /// <summary>Reach against an ORDINARY SPRITE MOB — the common pairing, kept only for the callers that have no
    /// target to measure against (the pickup radius, the editor gizmo). Every actual hit test uses
    /// <see cref="RangeAgainst"/> instead, which blends against the target that is really there.</summary>
    public float EffectiveRange => BaseRange * UnitScale.MeleeReachFactor;

    /// <summary>The player's reach AGAINST A SPECIFIC TARGET — the only correct form, because a melee blend is a fact
    /// about the PAIR of bodies, not about either one alone. A shadow mob is built to the same body as the player, so
    /// that pair blends to full reach; an ordinary shrunken sprite mob blends down exactly as before.
    ///
    /// <para><see cref="EffectiveRange"/> assumes an ordinary sprite mob and now exists only for the callers that have
    /// NO target to measure against (the pickup radius, the editor gizmo). Every real hit test uses this.</para></summary>
    public float RangeAgainst(Health t)
        => BaseRange * UnitScale.MeleeBlend(UnitScale.CharacterScale,
                                            t != null ? t.MeleeScale : UnitScale.MobScale);

    /// <summary>The attack ELEMENT applied to basic swings and (as the default) skill strikes: the equipped weapon's
    /// <see cref="Item.element"/> — the weapon is the single owner of its element — or Neutral when unarmed / there's
    /// no Character (the standalone spike). Read at use-time from the equipped weapon, same pattern as
    /// <see cref="BaseRange"/> and the audio/stance lookups, so there's no per-equip copy to keep in sync.</summary>
    public Element MeleeElement
    {
        get
        {
            var w = Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
            return w != null ? w.element : Element.Neutral;
        }
    }

    /// <summary>True when the equipped weapon is a physical RANGED type (Bow/Firearm), so a basic attack lobs a
    /// visible arrow that carries the blow to the target instead of connecting instantly. Read at use-time from the
    /// equipped weapon — same pattern as <see cref="MeleeElement"/>/<see cref="BaseRange"/> — so there's no per-equip
    /// copy to keep in sync; the Item's <see cref="Item.weaponType"/> stays the single owner.</summary>
    public bool IsRanged
    {
        get
        {
            var w = Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
            return w != null && (w.weaponType == WeaponType.Bow || w.weaponType == WeaponType.Firearm);
        }
    }

    /// <summary>Strict planar range — used to actually LAND a hit (TryAttack / the windup check).</summary>
    public bool InRange(Health t) => t != null && !t.IsDead && PlanarDist(t) <= RangeAgainst(t);

    /// <summary>Sticky version of <see cref="InRange"/> for the controller's stand-vs-chase decision:
    /// turns true at <see cref="range"/> and stays true until the target passes range×<see cref="rangeHysteresis"/>.
    /// Landing a hit still uses the strict <see cref="InRange"/>, so the extra margin never extends reach.</summary>
    public bool EngagedWith(Health t)
    {
        if (t == null || t.IsDead) { engaged = false; engagedTarget = null; return false; }
        if (t != engagedTarget) { engaged = false; engagedTarget = t; }   // new target → must reach `range` afresh
        float r = RangeAgainst(t);
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

    /// <summary>Announce that we WANT to basic-attack <paramref name="t"/> — the one fire-site of
    /// <see cref="SkillTrigger.OnBasicAttackStart"/>. Called by the controller whenever a basic-attack target is held,
    /// BEFORE reach is judged (<see cref="EngagedWith"/>/<see cref="InRange"/>), so a passive that reacts to the intent
    /// — a gap-closer, a pre-swing self-buff, a shout — resolves while the attack is still pending instead of after it
    /// already hit or was refused for being out of reach.
    ///
    /// <para><b>The announce and the swing that follows are ONE attempt.</b> Announcing OPENS the attempt; it closes
    /// when the swing actually starts (<see cref="TryAttack"/>) or when the attempt is abandoned
    /// (<see cref="Cancel"/> — hard CC, a new order, combat ending). Exactly one announce can be open at a time, so a
    /// pre-attack passive fires once per swing BY CONSTRUCTION rather than on a timer of its own. An earlier version
    /// used a parallel intent clock; because a gap-closer's travel time pushed every swing past that clock's expiry,
    /// it had always elapsed by the time control returned and so gated nothing — the passive re-fired continuously,
    /// paced by its own dash rather than by attack speed.</para></summary>
    public void AnnounceAttackIntent(Health t)
    {
        if (t == null || t.IsDead || swing != null || attemptOpen || Time.time < nextSwing) return;
        attemptOpen = true;
        PassiveEvents.Fire(SkillTrigger.OnBasicAttackStart, null, t.transform);
    }

    public void TryAttack(Health t)
    {
        if (Time.time < nextSwing || swing != null || !InRange(t)) return;
        attemptOpen = false;   // the announced attack is happening — this attempt is resolved
        // AttackSpeed shortens the gap between swings AND the motion itself (faster = higher mult),
        // so the visible swing keeps pace with the cadence instead of lagging at high ASPD.
        nextSwing = Time.time + cooldown * MotionScale;
        Sfx.Play(WeaponSfx(w => w.attackSfx, defaultAttackSfx));   // swing
        // OnAttack (attacker-side "Active Verb") is dormant — re-enable once we compose per-weapon swing vfx/sfx.
        // VfxCue.Fire(VfxCueId.OnAttack, transform);
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
        // The whole swing spans SwingTime (scales with attack-speed; Character3DPose speeds the clip to fill it).
        // The hit lands at hitTiming OF that window — the contact moment, ~70% in — not at the very end. We then
        // hold `swing` for the remainder so the animation finishes and cadence (= SwingTime) is unchanged.
        float scale = MotionScale;
        float swingTime = SwingTime;
        float impactAt = swingTime * Mathf.Clamp01(hitTiming);
        // Manual timer, not `new WaitForSeconds`: this coroutine starts EVERY swing during auto-combat,
        // and the duration varies with attack speed so a cached wait can't cover it (PERFORMANCE.md).
        for (float el = 0f; el < impactAt; el += Time.deltaTime) yield return null;

        // RO-style commit: once the swing has STARTED (gated on InRange in TryAttack), the blow LANDS regardless of how
        // far the target has since walked — the attack is animation-locked, not re-checked at impact. We only skip a
        // target that no longer exists or already died during the windup. (Melee is decided at swing start.)
        if (t != null && !t.IsDead)
        {
            if (IsRanged)
            {
                // Ranged weapon: release an arrow that homes onto the target; the blow lands when it arrives (which
                // re-checks validity — the target may die mid-flight). The target rides the projectile's HitPayload
                // into the CACHED LandArrow handler — no per-shot closure.
                // release from mid-body (feet-pivoted)
                Vector3 from = UnitAnchor.Center(transform);
                Projectile3D.Spawn(arrowSheet, arrowFrameSize, arrowFrames, arrowFps, arrowScale,
                    from, t.transform, UnitAnchor.Center(t.transform), arrowSpeed, 0.35f, 3f, LandArrow,
                    this, t, 0, Element.Neutral);
            }
            else LandBasicHit(t);
        }

        // Let the rest of the swing play out before freeing the slot, so the pose/animation completes and the next
        // swing still starts one SwingTime after this one began (DPS unchanged).
        float rest = swingTime - impactAt;
        for (float el = 0f; el < rest; el += Time.deltaTime) yield return null;
        swing = null;
    }

    /// <summary>Apply one basic-attack hit to <paramref name="t"/>: roll damage/crit/HIT through Character (spike
    /// falls back to flat <see cref="damage"/>), deal it, and fire the receiver-side impact cue. Called directly at
    /// the melee contact moment, or from the arrow's on-arrive for ranged weapons — so it re-checks validity (the
    /// target may have died during windup or arrow flight).</summary>
    void LandBasicHit(Health t)
    {
        if (t == null || t.IsDead) return;
        // Our HIT, rolled against the target's FLEE below; −1 in the Character-less spike (always lands).
        int hit = Character.Instance != null ? Character.Instance.Hit : -1;
        if (hit >= 0 && StatusController.Player != null) hit = Mathf.Max(0, hit - StatusController.Player.HitPenalty);  // Blind

        // ORDER: crit → accuracy → magnitude. Crit first because a crit can't be evaded; accuracy next, ONCE, here on
        // the attacker (its result travels in DamageInfo — the target must not re-roll a random check); the damage
        // number last, and only if the strike actually connects, so a whiff costs no roll.
        bool crit = Character.Instance != null && Character.Instance.RollCrit(t.luk);   // t.luk = crit shield
        bool missed = Combat.Missed(crit, hit, t.flee);
        int power = missed ? 0
                  : Character.Instance != null ? Character.Instance.RollDamageMagnitude(crit)
                                               : damage;
        int dealt = CombatAuthority.Apply(t, new DamageInfo(power, MeleeElement, false, crit, attackerHit: hit, missed: missed));
        if (dealt > 0)
        {
            // Impact SOUND plays FROM the mob in Health.TakeDamage (received_hit/crit, spatial) — no 2D play here.
            // Impact BURST is a receiver-side Passive Verb cue (Vfx Cue Composer), fired AT the target with no
            // attacker/direction (Origin == Target == receiver), just like OnReceiveHeal / OnHitBySkill —
            // the crit's bigger punch lives in the basic_crit sequence (×1.7), not a caller-side scale fudge.
            VfxCue.Fire(crit ? VfxCueId.OnHitByCriticalAttack : VfxCueId.OnHitByNormalAttack, t.transform);
            // A real basic attack connected — announce it for OnBasicAttackDealt passives (Momentum, Perfect Form).
            PassiveEvents.Fire(SkillTrigger.OnBasicAttackDealt, null, t.transform);
        }
    }

    /// <summary>Abandon the current attack: drop an in-flight swing and CLOSE the attempt an
    /// <see cref="AnnounceAttackIntent"/> opened, so a cancelled attack always resolves (a stuck-open attempt would
    /// silence the pre-attack passive forever). Whatever the announce already spent stays spent — see StateGate.</summary>
    public void Cancel()
    {
        if (swing != null) { StopCoroutine(swing); swing = null; }
        attemptOpen = false;
        engaged = false; engagedTarget = null;
    }

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

    // Cached arrow on-arrive — one delegate ever; the shot's attacker + target travel in the HitPayload.
    // LandBasicHit re-rolls damage/crit at arrival and re-checks the target, exactly as the old closure did.
    static readonly System.Action<Projectile3D.HitPayload> LandArrow = p =>
        ((Player3DAttacker)p.source).LandBasicHit(p.target);

    static readonly Collider[] cleaveBuf = new Collider[16];

    // Coroutines can't take `in`/`ref` params, so the context is copied by value here (a small struct).
    IEnumerator StrikeRoutine(Health t, SkillContext ctx, SfxSet hitSfx)
    {
        float scale = MotionScale;
        // manual timer for the same reason as SwingThenHit: per-cast coroutine, variable duration
        float windup = windupTime * scale;
        for (float el = 0f; el < windup; el += Time.deltaTime) yield return null;
        swing = null;
        if (t == null || t.IsDead) yield break;
        if (PlanarDist(t) > RangeAgainst(t) * 1.6f) yield break;   // target slipped out of the skill's (extended) reach

        bool crit = false;
        int basePower = Character.Instance != null ? Character.Instance.RollDamage(out crit, t.luk, ctx.ForceCrit) : damage;
        int power = Mathf.RoundToInt(basePower * Mathf.Max(1f, ctx.DamageMult));
        Element element = ctx.ConvertElement ? ctx.NewElement : MeleeElement;

        // Skill strike VFX is authored in the skill's own composed sequence (SkillDef.vfx → VfxDirector), not here.
        // Skills always LAND (no FLEE roll, hit −1), but they still respect DEF unless they actually crit —
        // so pass the real rolled `crit` (which also colours the number gold + bypasses DEF), RO-style.
        // MultiHit (Flurry mod) splits the strike into N hits sharing the total — visual flair, same DPS;
        // a single TakeDamage when HitCount is 1 (the default), so the common path is unchanged.
        int hitCount = Mathf.Max(1, ctx.HitCount);
        int perHit = Mathf.Max(1, Mathf.RoundToInt(power / (float)hitCount));
        int dealt = 0;
        for (int h = 0; h < hitCount && !t.IsDead; h++)
            dealt += CombatAuthority.Apply(t, new DamageInfo(perHit, element, false, crit, attackerHit: -1));
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
                if (CombatAuthority.Apply(h, new DamageInfo(power, element, false, crit, attackerHit: -1)) > 0) ApplyOnHit(h, ctx);
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
        // Slow / Root write the unified store's Slow state (drives MoveMult, the single mob-speed source). SlowFactor is
        // a TARGET SPEED (0.6 = moves at 60%); the store keeps a bonus FRACTION, so the slow is 1 − that.
        if (ctx.SlowSecs > 0f && ctx.SlowFactor < 1f) h.States.Apply(StateId.Slow, ctx.SlowSecs, 1f - ctx.SlowFactor);
        if (ctx.RootSecs > 0f) h.States.Apply(StateId.Slow, ctx.RootSecs, 1f);   // Root (Entangling mod) = slowed 100% = full stop
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? EffectiveRange : range);
    }
}
