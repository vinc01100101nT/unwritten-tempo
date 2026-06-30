using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Mage spell — <b>Soul Strike</b> (slot <b>4</b>): the first <b>aim-first single-target</b> skill. Pressing it
/// (default <b>4</b>) enters AIM mode via <see cref="UnitTargeter"/> — a ring highlights the enemy under the
/// cursor (green = a legal target in range, red = too far); <b>left-click</b> locks that ONE enemy and fires a
/// homing bolt at it; <b>Esc</b> / re-press cancels for free. On impact it deals a heavy <see cref="damageMult"/>×
/// magic hit (vs the target's MDef) and has a chance to <b>stun</b>.
///
/// Contrast with the existing single-target spells (Fireball/Bolt) which auto-pick "under cursor, else nearest"
/// and fire instantly — Soul Strike makes you DELIBERATELY choose the victim first, so it's the template for
/// precise, high-commitment nukes. MP + cooldown are spent only on confirm (cancelling costs nothing), exactly
/// like Meteor's ground-aim. Implements <see cref="IHotbarSkill"/>; travel + impact use <see cref="Projectile3D"/>.
/// </summary>
public class Player3DSoulStrike : MonoBehaviour, IHotbarSkill, IModifiableSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    public KeyCode key = KeyCode.Alpha4;
    public int manaCost = 12;
    public float cooldown = 4f;
    [Tooltip("Max distance (world units, XZ) the locked target may be from the player.")]
    public float range = 9f;
    [Tooltip("Damage as a multiple of a normal magic hit (single-target, so it hits harder than Fireball).")]
    public float damageMult = 2.6f;

    [Header("Stun on hit (RO single-target nuke flavour)")]
    [Range(0f, 1f)]
    [Tooltip("Chance on impact to stun the target (0 = never).")]
    public float stunChance = 0.35f;
    [Tooltip("Stun duration in seconds.")]
    public float stunSeconds = 1.2f;

    [Header("Projectile FX (sliced off a sheet at runtime, like Fireball)")]
    public Texture2D projectileSheet;
    public int projFrameSize = 16;
    public int projFrames = 4;
    public float projFps = 18f;
    public float projSpeed = 16f;       // fast — it reads as a focused "snipe", not a lobbed fireball
    public float projScale = 1.2f;
    [Header("Impact FX (FX/Elemental/Explosion)")]
    public Texture2D impactSheet;
    public int impactFrameSize = 40;
    public int impactFrames = 9;
    public float impactScale = 1.1f;

    [Header("Audio")]
    public SfxSet castSfx;
    public SfxSet failSfx;   // not enough mana

    float nextUse, lastDuration;
    int castCount;             // for the Proc-phase "every Nth cast is free" modifier
    SkillModLoadout loadout;   // six-phase modifier loadout (lazily registered, born with one)
    static readonly Color NoManaColor   = new Color(0.55f, 0.6f, 1f);
    static readonly Color SoulColor     = new Color(0.7f, 0.55f, 1f);   // arcane violet

    // Soul Strike's modifier-eligibility identity (Charter §12): a magic, ranged, single-target nuke — the same
    // magic pool Fireball draws from (cooldown/reach/amp/element/stagger/slow/lifesteal/proc).
    const string SkillId = "soul_strike";
    static readonly string[] Tags = { SkillTag.Magic, SkillTag.Ranged, SkillTag.Offense, SkillTag.SingleTarget };

    public string SkillModId => SkillId;
    public string[] SkillModTags => Tags;

    void EnsureRegistered()
    {
        if (loadout != null || Character.Instance == null) return;
        loadout = Character.Instance.SkillMods.Register(SkillId, Tags);
    }

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        EnsureRegistered();   // keeps the loadout registered; triggering is via the hotbar binding (HotbarInput)
    }

    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (StatusController.Player != null && StatusController.Player.Silenced)
        { DamagePopup.SpawnText(transform.position, "Silenced", new Color(0.7f, 0.5f, 1f)); return false; }
        if (Time.time < nextUse) return false;
        if (UnitTargeter.Active) { UnitTargeter.Cancel(); return false; }   // re-press cancels aiming

        // Enter AIM mode: ring the enemy under the cursor; left-click locks + fires (CastAt), Esc cancels.
        // MP/cooldown are only spent on confirm, so cancelling costs nothing. A reach mod widens the aim range.
        EnsureRegistered();
        var aimCtx = SkillContext.Default(damageMult, 1f, 0f);
        loadout?.ApplyAll(ref aimCtx);
        UnitTargeter.Begin(transform, range * aimCtx.RangeMult, CastAt);
        return true;
    }

    // Called by UnitTargeter when the player confirms a living target.
    void CastAt(Health target)
    {
        if (target == null || target.IsDead) return;
        if (Character.Instance == null || Character.Instance.IsDead || Time.time < nextUse) return;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            return;
        }

        EnsureRegistered();
        var ctx = SkillContext.Default(damageMult, 1f, 0f);
        loadout?.ApplyAll(ref ctx);                       // fold this skill's modifiers into the cast

        castCount++;
        bool free = ctx.EveryNthFree > 0 && castCount % ctx.EveryNthFree == 0;   // Proc: every Nth cast skips cooldown
        lastDuration = free ? 0f : cooldown / Mathf.Max(0.01f, Character.Instance.CastSpeedMult) * ctx.CooldownMult;   // DEX casts faster
        nextUse = Time.time + lastDuration;
        Sfx.Play(castSfx);

        // Capture the cast's shaped values for the on-arrive detonation (locals, not the whole struct).
        var t = target;
        Element element = ctx.ConvertElement ? ctx.NewElement : Element.Neutral;
        float dmgMult = ctx.DamageMult, lifesteal = ctx.LifestealFracDealt;
        float staggerSecs = ctx.StaggerSecs, slowFactor = ctx.SlowFactor, slowSecs = ctx.SlowSecs;
        Vector3 from = transform.position + Vector3.up * 0.5f;
        Projectile3D.Spawn(projectileSheet, projFrameSize, projFrames, projFps, projScale,
            from, t.transform, t.transform.position + Vector3.up * 0.4f, projSpeed, 0.5f, 3f, () =>
            {
                if (t == null || t.IsDead || Character.Instance == null) return;
                int power = Character.Instance.RollMagicDamage(out bool crit);
                int dealt = t.TakeMagicDamage(Mathf.RoundToInt(power * dmgMult), crit, element);
                if (!t.IsDead)
                {
                    var ai = t.GetComponent<MonsterAI3D>();
                    if (ai != null)
                    {
                        if (staggerSecs > 0f) ai.Stagger(staggerSecs);
                        if (slowSecs > 0f && slowFactor < 1f) ai.Slow(slowFactor, slowSecs);
                    }
                }
                if (lifesteal > 0f && dealt > 0) Character.Instance.LeechHeal(Mathf.RoundToInt(dealt * lifesteal));
                if (!t.IsDead && Random.value < stunChance) Status.Apply(t.gameObject, Ailment.Stun, stunSeconds);
            },
            impactSheet, impactFrameSize, impactFrames, 22f, impactScale);
    }

    // ---- IHotbarSkill ----
    public int SlotOrder => 3;            // after Fireball(0) / FrostNova(1) / Meteor(2) in the Mage kit
    public string ShortLabel => "Soul";
    public string KeyLabel => "4";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => SoulColor;

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && key == KeyCode.Alpha4 && Keyboard.current.digit4Key.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(key);
    }
}
