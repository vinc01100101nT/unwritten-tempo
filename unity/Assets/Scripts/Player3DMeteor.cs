using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Mage spell — <b>Meteor</b> (slot <b>3</b>): a ground-targeted strike. A SHOWER of <see cref="ticks"/>
/// meteors falls from the sky onto the cursor point (clamped to <see cref="castRange"/>), one per tick. Each
/// meteor that lands explodes and deals ONE damage tick — a burst of <see cref="hitsPerTick"/> separate rolls
/// (each with a per-hit <b>stun</b> chance) plus its own rising yellow combo TOTAL, via <see cref="MultiHitBurst"/>.
/// The whole skill deals <see cref="damageMult"/>× magic damage split across every hit, so more meteors = more
/// visual flair, not more power. The falls ARE the telegraph. Implements <see cref="IHotbarSkill"/>; the falls +
/// impact FX use <see cref="Projectile3D"/>.
/// </summary>
public class Player3DMeteor : MonoBehaviour, IHotbarSkill, IModifiableSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    public KeyCode key = KeyCode.Alpha3;
    public int manaCost = 16;
    public float cooldown = 9f;
    [Tooltip("Max distance the impact point can be from the player (the reticle is clamped to this).")]
    public float castRange = 11f;
    [Tooltip("Impact AoE radius (the ground-target circle shows this).")]
    public float radius = 4.5f;
    [Tooltip("TOTAL damage to every mob in the blast, as a multiple of a normal magic hit (split across hits).")]
    public float damageMult = 2.4f;

    [Header("Multi-hit + status (RO-style: one meteor per tick, live per-tick combo total)")]
    [Tooltip("Number of meteors that fall = number of damage ticks. Each meteor that lands triggers one burst " +
             "of hits (and its own rising yellow total) on impact.")]
    public int ticks = 3;
    [Tooltip("Hits per tick (per meteor) — each is a SEPARATE roll (+ stun chance). Total hits = ticks × hitsPerTick.")]
    public int hitsPerTick = 5;
    [Tooltip("Seconds between hits within a tick.")]
    public float hitInterval = 0.2f;
    [Tooltip("Seconds between successive meteor IMPACTS — they start at staggered heights so they land this far " +
             "apart in time. ~ a tick's hit duration ((hitsPerTick−1)×hitInterval) keeps the bursts sequential.")]
    public float meteorInterval = 1.2f;
    [Range(0f, 1f)]
    [Tooltip("Chance PER HIT to stun a mob — independent roll each hit (so more hits = more stun chances).")]
    public float stunChance = 0.25f;
    [Tooltip("Stun duration (mob is frozen + shows a swirl).")]
    public float stunSeconds = 1.5f;

    [Header("VFX (composed in data)")]
    [Tooltip("The composed look of ONE meteor: fall + impact explosion + screen-shake, authored as a flat " +
             "timeline. Fires the \"impact\" event on landing, which this skill turns into a damage tick. " +
             "Unset → loaded from Resources/VfxSequences/meteor (build it with Tools ▸ unwritten ▸ Art ▸ " +
             "Build Default Vfx Sequences).")]
    public VfxSequence vfxSequence;
    [Tooltip("Height the impact point is lifted so the meteor lands ON the ground, not in it.")]
    public float landLift = 0.3f;

    [Header("Audio")]
    public SfxSet castSfx;
    public SfxSet failSfx;
    // Per-hit impact SOUND now plays FROM each struck mob in Health (received_hit_skill) — no per-beat set here.
    [Tooltip("Plays once per METEOR as it LAUNCHES — randomized Fireball variants. A fireball whoosh per meteor.")]
    public SfxSet meteorSfx;

    float nextUse, lastDuration;
    int castCount;             // for the Proc-phase "every Nth cast is free" modifier
    SkillModLoadout loadout;   // six-phase modifier loadout (lazily registered, born with one)
    SkillContext pendingCtx;   // the shaped cast captured at CastAt, consumed in DetonateTick (deferred meteor impacts)
    Vector3 detonateCenter;    // the shower's impact centre; each meteor's "impact" event detonates here
    System.Action<string> onVfxEvent;   // cached hook handed to VfxDirector (no per-play delegate alloc)
    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);
    static readonly Color MeteorColor = new Color(1f, 0.55f, 0.3f);

    // Meteor's modifier-eligibility identity (Charter §12): a magic, offensive AoE — draws the magic-offense pool
    // (cooldown/amp/element/slow/lifesteal/proc); melee-only mods are filtered out.
    const string SkillId = "meteor";
    static readonly string[] Tags = { SkillTag.Magic, SkillTag.Offense, SkillTag.Aoe };

    public string SkillModId => SkillId;
    public string[] SkillModTags => Tags;

    void EnsureRegistered()
    {
        if (loadout != null || Character.Instance == null) return;
        loadout = Character.Instance.SkillMods.Register(SkillId, Tags);
    }

    void Awake()
    {
        onVfxEvent = OnVfxEvent;
        if (vfxSequence == null) vfxSequence = Resources.Load<VfxSequence>("VfxSequences/meteor");
    }

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        EnsureRegistered();   // keeps the loadout registered; triggering is via the hotbar binding (HotbarInput)
    }

    // The sequence raises "impact" when a meteor lands (see the meteor VfxSequence); that's this skill's cue to
    // deal its damage tick. Presentation (fall/explosion/shake) lives entirely in the asset — this handles the
    // GAMEPLAY beat only. Cached as a field so handing it to VfxDirector.Play allocates nothing.
    void OnVfxEvent(string tag)
    {
        if (tag == "impact") DetonateTick(detonateCenter);
    }

    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (StatusController.Player != null && StatusController.Player.Silenced)
        { DamagePopup.SpawnText(transform.position, "Silenced", new Color(0.7f, 0.5f, 1f)); return false; }
        if (Time.time < nextUse) return false;
        if (GroundTargeter.Active) { GroundTargeter.Cancel(); return false; }   // re-press cancels aiming

        // Enter AIM mode: a circle follows the cursor; left-click drops the meteor (CastAt), Esc cancels.
        // MP/cooldown are only spent on confirm, so cancelling costs nothing. A reach mod extends the cast range.
        EnsureRegistered();
        var aimCtx = SkillContext.Default(damageMult, 1f, 0f);
        loadout?.ApplyAll(ref aimCtx);
        GroundTargeter.Begin(transform, radius, castRange * aimCtx.RangeMult, CastAt);
        return true;
    }

    // Called by GroundTargeter when the player confirms the impact point.
    void CastAt(Vector3 center)
    {
        if (Character.Instance == null || Character.Instance.IsDead || Time.time < nextUse) return;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            return;
        }

        EnsureRegistered();
        pendingCtx = SkillContext.Default(damageMult, 1f, 0f);
        loadout?.ApplyAll(ref pendingCtx);                // captured now; consumed per meteor in DetonateTick

        castCount++;
        bool free = pendingCtx.EveryNthFree > 0 && castCount % pendingCtx.EveryNthFree == 0;   // Proc: every Nth cast skips cooldown
        lastDuration = free ? 0f : cooldown * pendingCtx.CooldownMult;
        nextUse = Time.time + lastDuration;
        Sfx.Play(castSfx);
        DamagePopup.SpawnText(center + Vector3.up * 0.6f, "Meteor!", MeteorColor);   // once, at cast

        // Drop ONE meteor per tick, staggered in TIME (meteorInterval apart) so each meteor is its own fall
        // animation with its own fireball whoosh on launch. Each impact fires its OWN damage tick (DetonateTick),
        // so the numbers land exactly when that meteor hits.
        StartCoroutine(MeteorShower(center));
    }

    // Spawns the meteors one at a time, meteorInterval apart — staggered LAUNCHES (a distinct fireball sound +
    // fall animation per meteor) AND staggered impacts. Coroutines keep running when the component is merely
    // disabled (enabled=false on a class switch); they only stop if the player object itself is destroyed.
    IEnumerator MeteorShower(Vector3 center)
    {
        int meteors = Mathf.Max(1, ticks);
        detonateCenter = center;
        var ctx = VfxContext.At(center);
        ctx.target = center + Vector3.up * landLift;   // meteors land ON the ground, not in it
        var wait = new WaitForSeconds(Mathf.Max(0.05f, meteorInterval));
        for (int m = 0; m < meteors; m++)
        {
            Sfx.PlayAt(meteorSfx, center);   // 3D: fireball whoosh per staggered meteor (gameplay-paced, stays in code)
            // One sequence per meteor = its fall + impact explosion + shake (all data). Its "impact" event calls
            // back into OnVfxEvent → DetonateTick, so the damage lands exactly when this meteor hits.
            if (VfxDirector.Play(vfxSequence, ctx, onVfxEvent) == null)
            {
                // No sequence asset wired (build it: Tools ▸ unwritten ▸ Art ▸ Build Default Vfx Sequences).
                // Degrade gracefully: skip the visual but still deal the damage tick so the skill isn't a dud.
                Debug.LogWarning("[unwritten] Meteor has no VfxSequence — dealing damage without the fall/impact FX. " +
                                 "Run Tools ▸ unwritten ▸ Art ▸ Build Default Vfx Sequences.");
                DetonateTick(center);
            }
            if (m < meteors - 1) yield return wait;
        }
    }

    // One meteor's impact = ONE damage tick: a single burst of hitsPerTick separate rolls with its own rising
    // yellow combo total (overkill-safe → numbers keep showing on a mob that died partway) + one explosion pulse.
    void DetonateTick(Vector3 center)
    {
        if (Character.Instance == null) return;

        // Per-hit damage = the full meteor (damageMult) split across EVERY hit of ALL ticks (ticks × hitsPerTick),
        // so the 3-meteor shower deals the same total as one big strike — a visual split, not a power buff.
        // Fold in the loadout shaped at cast: amplify (crushing/heavy), element-convert, slow, and lifesteal.
        // ctx.DamageMult was already seeded from this skill's damageMult, then scaled by any amp mod — so it IS
        // the full multiplier (don't multiply by damageMult again, or it squares).
        var ctx = pendingCtx;
        float effMult = Mathf.Max(1f, ctx.DamageMult);
        Element element = ctx.ConvertElement ? ctx.NewElement : Element.Fire;
        float lifesteal = ctx.LifestealFracDealt, slowFactor = ctx.SlowFactor, slowSecs = ctx.SlowSecs;
        float extraStagger = ctx.StaggerSecs;   // concussive (a small freeze on top of the stun chance)
        int totalHits = Mathf.Max(1, ticks * hitsPerTick);
        MultiHitBurst.Run(center, radius, 1, hitsPerTick, hitInterval, 0f,   // 1 tick → inter-tick wait unused
            (ai, hitIndex) =>
            {
                if (Character.Instance == null) return 0;
                int power = Mathf.RoundToInt(Character.Instance.RollMagicDamage(out bool crit) * (effMult / totalHits));
                int dealt = ai.Health.TakeMagicDamageOverkill(power, crit, element);
                if (!ai.Health.IsDead && Random.value < stunChance) ai.Stun(stunSeconds);   // only stun the living
                if (!ai.Health.IsDead && extraStagger > 0f) ai.Stagger(extraStagger);   // concussive
                if (!ai.Health.IsDead && slowSecs > 0f && slowFactor < 1f) ai.Slow(slowFactor, slowSecs);   // chilling/withering
                if (lifesteal > 0f && dealt > 0) Character.Instance.LeechHeal(Mathf.RoundToInt(dealt * lifesteal));   // siphon
                return dealt;
            });
            // Explosion + shake are now the VfxSequence's "impact" step (data). Impact SOUND still plays FROM each
            // struck mob in Health (received_hit_skill, throttled to one/frame).
    }

    // ---- IHotbarSkill ----
    public int SlotOrder => 2;
    public string ShortLabel => "Meteor";
    public string KeyLabel => "3";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => MeteorColor;

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool Pressed()
    {
        if (Hotkeys.Typing) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && key == KeyCode.Alpha3 && Keyboard.current.digit3Key.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(key);
    }
}
