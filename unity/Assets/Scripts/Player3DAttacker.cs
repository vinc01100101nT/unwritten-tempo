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
    [Tooltip("How close (world units, XZ) you must be to land a hit.")]
    public float range = 1.4f;
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

    [Header("Slash FX (sliced off a horizontal sheet at runtime)")]
    public Texture2D slashSheet;
    public int slashFrameSize = 32;
    public int slashFrames = 4;
    public float slashFps = 22f;
    public float slashScale = 1.1f;

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

    /// <summary>Strict planar range — used to actually LAND a hit (TryAttack / the windup check).</summary>
    public bool InRange(Health t) => t != null && !t.IsDead && PlanarDist(t) <= range;

    /// <summary>Sticky version of <see cref="InRange"/> for the controller's stand-vs-chase decision:
    /// turns true at <see cref="range"/> and stays true until the target passes range×<see cref="rangeHysteresis"/>.
    /// Landing a hit still uses the strict <see cref="InRange"/>, so the extra margin never extends reach.</summary>
    public bool EngagedWith(Health t)
    {
        if (t == null || t.IsDead) { engaged = false; engagedTarget = null; return false; }
        if (t != engagedTarget) { engaged = false; engagedTarget = t; }   // new target → must reach `range` afresh
        float d = PlanarDist(t);
        if (d <= range) engaged = true;
        else if (d > range * Mathf.Max(1f, rangeHysteresis)) engaged = false;
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

        if (InRange(t))   // target still in reach when the blow connects (skip if it moved out / died during windup)
        {
            // Roll the hit through Character so CritChance/CritDamage apply (spike has no Character → flat damage).
            bool crit = false;
            int power = Character.Instance != null ? Character.Instance.RollDamage(out crit) : damage;

            // Slash FX at the target (a bit above its feet), billboarded by SlashEffect so it faces the camera.
            Vector3 fxPos = t.transform.position + Vector3.up * 0.4f;
            SlashEffect.Spawn(slashSheet, slashFrameSize, slashFrames, slashFps / Mathf.Max(0.01f, scale), fxPos, slashScale);

            // Pass our HIT so the target rolls it against its FLEE (RO accuracy); −1 in the Character-less spike.
            int hit = Character.Instance != null ? Character.Instance.Hit : -1;
            if (hit >= 0 && StatusController.Player != null) hit = Mathf.Max(0, hit - StatusController.Player.HitPenalty);  // Blind
            int dealt = t.TakeDamage(power, crit, hit, meleeElement);
            if (dealt > 0) Sfx.Play(WeaponSfx(w => w.hitSfx, defaultHitSfx));   // impact — only on a landed hit, not a MISS
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
    {
        if (t == null || t.IsDead || swing != null) return;
        Sfx.Play(swingSfx != null && !swingSfx.Empty ? swingSfx : WeaponSfx(w => w.attackSfx, defaultAttackSfx));
        swing = StartCoroutine(StrikeRoutine(t, Mathf.Max(1f, damageMult), Mathf.Max(0.1f, fxScale), hitSfx, staggerSecs));
    }

    IEnumerator StrikeRoutine(Health t, float damageMult, float fxScale, SfxSet hitSfx, float staggerSecs)
    {
        float scale = MotionScale;
        yield return new WaitForSeconds(windupTime * scale);
        swing = null;
        if (t == null || t.IsDead) yield break;
        if (PlanarDist(t) > range * 1.6f) yield break;   // target slipped out of the skill's (extended) reach

        bool crit;
        int basePower = Character.Instance != null ? Character.Instance.RollDamage(out crit) : damage;
        int power = Mathf.RoundToInt(basePower * damageMult);

        Vector3 fxPos = t.transform.position + Vector3.up * 0.4f;
        SlashEffect.Spawn(slashSheet, slashFrameSize, slashFrames, slashFps / Mathf.Max(0.01f, scale), fxPos, slashScale * fxScale);

        int dealt = t.TakeDamage(power, true, -1, meleeElement);   // skills always land; gold (crit-styled) number sells the impact
        if (dealt > 0)
        {
            Sfx.Play(hitSfx != null && !hitSfx.Empty ? hitSfx : WeaponSfx(w => w.hitSfx, defaultHitSfx));
            var ai = t.GetComponent<MonsterAI3D>();   // once per Bash (~every few s), not a per-frame hot path
            if (ai != null && staggerSecs > 0f) ai.Stagger(staggerSecs);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
