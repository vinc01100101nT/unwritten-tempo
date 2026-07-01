using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The Swordsman's defensive active — <b>Guard</b> (skill slot <b>3</b>): a brief stance that cuts all
/// incoming damage (physical AND magic) by <see cref="damageReduction"/> for <see cref="duration"/>
/// seconds, with a shield-bubble FX. Spends MP, own cooldown. Unlike Bash/Charge it needs no target —
/// it's a self-buff, so it always fires when off cooldown and affordable.
///
/// Like the other actives this is a thin input/MP/cooldown wrapper implementing <see cref="IHotbarSkill"/>,
/// so it appears on the HUD hotbar automatically (slot 3). The actual mitigation lives in
/// <see cref="Character.BeginGuard"/>.
/// </summary>
public class Player3DGuard : MonoBehaviour, IHotbarSkill, IModifiableSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    [Tooltip("Key that triggers Guard (skill slot 3).")]
    public KeyCode key = KeyCode.Alpha3;
    [Tooltip("Mana spent per use.")]
    public int manaCost = 4;
    [Tooltip("Seconds between uses.")]
    public float cooldown = 9f;
    [Tooltip("How long the stance lasts.")]
    public float duration = 3.5f;
    [Range(0f, 1f)]
    [Tooltip("Fraction of incoming damage prevented while the stance is up (0.6 = take 40%).")]
    public float damageReduction = 0.6f;

    [Header("Shield VFX (Effect Library — looping bubble that tracks the player while up)")]
    [Tooltip("Effect-library id for the shield bubble. Empty = none.")]
    public string shieldVfxId = "shield.magic-blue";
    public float shieldScale = 1f;
    public float shieldYOffset = 0.35f;

    [Header("Audio")]
    public SfxSet guardSfx;   // shield clang on activation
    public SfxSet failSfx;    // not enough mana

    float nextUse;
    float lastDuration;        // the cooldown actually applied last use (for the hotbar's sweep fraction)
    int castCount;             // for the Proc-phase "every Nth cast is free" modifier
    Vfx.EffectHandle shield;
    SkillModLoadout loadout;   // six-phase modifier loadout (lazily registered, born with one)

    // Guard's modifier-eligibility identity (Charter §12): a melee support self-buff — NO offense tag, so it
    // can never roll a damage/on-hit mod (those require offense). Its pool is the cooldown/self/heal mods:
    // relentless (faster cd), bulwark (stronger shield), mending (heal on cast), rhythm (free cast).
    const string SkillId = "guard";
    static readonly string[] Tags = { SkillTag.Melee, SkillTag.Physical, SkillTag.Support };

    public string SkillModId => SkillId;
    public string[] SkillModTags => Tags;

    void EnsureRegistered()
    {
        if (loadout != null || Character.Instance == null) return;
        loadout = Character.Instance.SkillMods.Register(SkillId, Tags);
    }

    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);
    static readonly Color GuardColor = new Color(0.5f, 0.8f, 1f);

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        EnsureRegistered();   // keeps the loadout registered; triggering is via the hotbar binding (HotbarInput)
    }

    /// <summary>Raise the Guard stance now — shared by the keypress and the HUD hotbar click. Returns
    /// true only if it actually triggered (off cooldown + enough MP).</summary>
    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (Time.time < nextUse) return false;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            nextUse = Time.time + 0.4f; lastDuration = 0.4f;   // brief lockout so it can't spam the popup
            return false;
        }

        EnsureRegistered();
        var ctx = SkillContext.Default(1f, 1f, 0f);
        loadout?.ApplyAll(ref ctx);                       // fold this skill's modifiers into the cast

        castCount++;
        bool free = ctx.EveryNthFree > 0 && castCount % ctx.EveryNthFree == 0;   // rhythm: every Nth cast skips cooldown
        lastDuration = free ? 0f : cooldown * ctx.CooldownMult;   // relentless shortens it
        nextUse = Time.time + lastDuration;

        // bulwark can only strengthen the stance (take the better of the skill's own values and the mod's).
        float guardSecs = Mathf.Max(duration, ctx.ShieldSecs);
        float guardCut = Mathf.Max(damageReduction, ctx.ShieldCut);
        Character.Instance.BeginGuard(guardSecs, guardCut);
        if (ctx.LifeFracMaxHP > 0f)                       // mending: heal on cast (the support-only mod)
            Character.Instance.Heal(Mathf.RoundToInt(Character.Instance.MaxHP * ctx.LifeFracMaxHP));
        Sfx.Play(guardSfx);
        DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "Guard", GuardColor);

        ShowShield(guardSecs);
        return true;
    }

    // Attach a looping shield bubble that tracks the player for the stance's duration (re-activating refreshes
    // the timer). Vfx.Attach tracks rather than parents, so it doesn't inherit the billboard's breathe/scale.
    void ShowShield(float seconds)
    {
        if (string.IsNullOrEmpty(shieldVfxId)) return;
        if (shield != null) shield.Stop();
        shield = Vfx.Attach(shieldVfxId, transform, new Vector3(0f, shieldYOffset, 0f), shieldScale);
        CancelInvoke(nameof(HideShield));
        Invoke(nameof(HideShield), Mathf.Max(0.1f, seconds));
    }

    void HideShield()
    {
        if (shield != null) { shield.Stop(); shield = null; }
    }

    // ---- IHotbarSkill (drives the HUD hotbar slot) ----
    public int SlotOrder => 2;            // bar order: Bash(1) · Charge(2) · Guard(3) · Bolt(Q)
    public string ShortLabel => "Guard";
    public string KeyLabel => "3";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => GuardColor;   // defensive blue

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && key == KeyCode.Alpha3 && Keyboard.current.digit3Key.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(key);
    }
}
