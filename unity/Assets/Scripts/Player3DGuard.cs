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
public class Player3DGuard : MonoBehaviour, IHotbarSkill
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

    [Header("Shield FX (FX/Magic/Shield, sliced at runtime)")]
    public Texture2D shieldSheet;
    public int shieldFrameSize = 24;
    public int shieldFrames = 6;
    public float shieldFps = 12f;
    public float shieldScale = 1.35f;
    public float shieldYOffset = 0.35f;

    [Header("Audio")]
    public SfxSet guardSfx;   // shield clang on activation
    public SfxSet failSfx;    // not enough mana

    float nextUse;
    float lastDuration;        // the cooldown actually applied last use (for the hotbar's sweep fraction)
    GuardShieldFX shield;

    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);
    static readonly Color GuardColor = new Color(0.5f, 0.8f, 1f);

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        if (DevSettings.Blocking) return;                 // cursor over the dev panel — don't leak the keypress
        if (!Pressed() || PointerOverUI()) return;
        Activate();
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

        lastDuration = cooldown;
        nextUse = Time.time + cooldown;
        Character.Instance.BeginGuard(duration, damageReduction);
        Sfx.Play(guardSfx);
        DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "Guard", GuardColor);

        EnsureShield();
        if (shield != null) shield.Show(duration);
        return true;
    }

    // The shield FX is created lazily on first use (and only if a sheet was assigned by Build Player).
    void EnsureShield()
    {
        if (shield != null || shieldSheet == null) return;
        shield = GuardShieldFX.Create(transform, shieldSheet, shieldFrameSize, shieldFrames,
                                      shieldFps, shieldScale, shieldYOffset);
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
