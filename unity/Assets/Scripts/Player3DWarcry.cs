using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The Swordsman's AoE active — <b>Warcry</b> (skill slot <b>4</b>): a player-centered shout that damages
/// AND knocks back every mob within <see cref="radius"/>, staggering them briefly. No target needed — it's
/// a point-blank area effect, so it fires whenever off-cooldown and affordable.
///
/// Like the other actives it's a thin input/MP/cooldown wrapper implementing <see cref="IHotbarSkill"/>
/// (so it appears on the HUD hotbar automatically). The scan reuses the <see cref="MonsterAI3D.Active"/>
/// registry + cached <c>.Health</c> (no OverlapSphere/GetComponent), and the shove is delegated to
/// <see cref="MonsterAI3D.Knockback"/>. It fires at most every few seconds, so it's not a per-frame hot path.
/// </summary>
public class Player3DWarcry : MonoBehaviour, IHotbarSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    [Tooltip("Key that triggers Warcry (skill slot 4).")]
    public KeyCode key = KeyCode.Alpha4;
    [Tooltip("Mana spent per use.")]
    public int manaCost = 7;
    [Tooltip("Seconds between uses.")]
    public float cooldown = 10f;
    [Tooltip("Radius of the shout (world units, XZ).")]
    public float radius = 3.5f;
    [Tooltip("Damage to every mob in range, as a multiple of a normal swing.")]
    public float damageMult = 1.4f;

    [Header("Knockback")]
    [Tooltip("Initial shove speed (eases out over knockbackTime). Distance ≈ ½·speed·time ≈ 1.4 units.")]
    public float knockbackSpeed = 13f;
    [Tooltip("How long the shove lasts.")]
    public float knockbackTime = 0.22f;
    [Tooltip("Seconds each hit mob is frozen (≥ knockbackTime so it doesn't fight the slide).")]
    public float staggerSecs = 0.5f;

    [Header("Shockwave FX (FX/Magic/Aura, sliced at runtime)")]
    public Texture2D shockSheet;
    public int shockFrameSize = 24;
    public int shockFrames = 5;
    public float shockFps = 18f;
    public float shockScale = 4.5f;

    [Header("Audio")]
    public SfxSet warcrySfx;   // the shout / shockwave boom
    public SfxSet failSfx;     // not enough mana

    float nextUse;
    float lastDuration;        // the cooldown actually applied last use (for the hotbar's sweep fraction)

    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);
    static readonly Color WarcryColor = new Color(1f, 0.7f, 0.3f);

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        if (DevSettings.Blocking) return;                 // cursor over the dev panel — don't leak the keypress
        if (!Pressed() || PointerOverUI()) return;
        Activate();
    }

    /// <summary>Let out the Warcry now — shared by the keypress and the HUD hotbar click. Damages + shoves
    /// every mob in range. Returns true if it fired (off cooldown + enough MP); it can fire into empty space.</summary>
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
        Sfx.Play(warcrySfx);
        DamagePopup.SpawnText(transform.position + Vector3.up * 0.6f, "Warcry!", WarcryColor);
        SlashEffect.Spawn(shockSheet, shockFrameSize, shockFrames, shockFps, transform.position + Vector3.up * 0.2f, shockScale);

        Vector3 me = transform.position;
        float r2 = radius * radius;

        // Collect every in-range mob FIRST, then hit them in a second pass. We must NOT damage while
        // iterating MonsterAI3D.Active: a one-shot kill removes the mob from Active mid-loop (Health.Die →
        // DeathDissolve disables MonsterAI3D → OnDisable → Active.Remove), which shifts the indices and makes
        // an index loop skip the next mob — so ~half a one-shot AoE was being missed. The snapshot is immune.
        targets.Clear();
        var list = MonsterAI3D.Active;            // registry scan — no OverlapSphere, cached .Health
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;
            if (h == null || h.IsDead) continue;
            Vector3 d = ai.transform.position - me; d.y = 0f;   // planar — ignore height
            if (d.sqrMagnitude <= r2) targets.Add(ai);
        }

        for (int i = 0; i < targets.Count; i++)
        {
            var ai = targets[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            int power = Mathf.RoundToInt(Character.Instance.RollDamage(out bool crit) * damageMult);
            ai.Health.TakeDamage(power, crit, -1);   // AoE always lands (no FLEE roll)
            ai.Knockback(me, knockbackSpeed, knockbackTime, staggerSecs);
        }
        return true;
    }

    // Reused scratch for the two-pass AoE scan (allocation-free; single-threaded, one player).
    static readonly System.Collections.Generic.List<MonsterAI3D> targets = new System.Collections.Generic.List<MonsterAI3D>();

    // ---- IHotbarSkill (drives the HUD hotbar slot) ----
    public int SlotOrder => 3;            // bar order: Bash(1) · Charge(2) · Guard(3) · Warcry(4) · Bolt(Q)
    public string ShortLabel => "Warcry";
    public string KeyLabel => "4";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => WarcryColor;   // rallying orange

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && key == KeyCode.Alpha4 && Keyboard.current.digit4Key.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(key);
    }
}
