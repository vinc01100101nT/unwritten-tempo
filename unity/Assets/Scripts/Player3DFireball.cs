using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Mage spell — <b>Fireball</b> (slot <b>1</b>): a single-target magic projectile that flies to the mob
/// under the cursor (else the nearest within <see cref="range"/>) and detonates for <see cref="damageMult"/>×
/// magic damage on arrival. Thin input/MP/cooldown wrapper (the caster-class template) implementing
/// <see cref="IHotbarSkill"/>; the travel + impact are delegated to <see cref="Projectile3D"/>.
/// </summary>
public class Player3DFireball : MonoBehaviour, IHotbarSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    public KeyCode key = KeyCode.Alpha1;
    public int manaCost = 8;
    public float cooldown = 1.6f;
    public float range = 8f;
    [Tooltip("Damage as a multiple of a normal magic hit.")]
    public float damageMult = 1.8f;

    [Header("Burn (RO fire DoT applied on impact)")]
    [Tooltip("Seconds the Burn lingers after the hit (0 = no burn).")]
    public float burnSeconds = 3f;
    [Tooltip("Burn damage per tick.")]
    public int burnDamage = 3;

    [Header("Projectile FX (FX/Projectile/Fireball)")]
    public Texture2D projectileSheet;
    public int projFrameSize = 16;
    public int projFrames = 4;
    public float projFps = 16f;
    public float projSpeed = 12f;
    public float projScale = 1.2f;
    [Header("Impact FX (FX/Elemental/Explosion)")]
    public Texture2D impactSheet;
    public int impactFrameSize = 40;
    public int impactFrames = 9;
    public float impactScale = 1.0f;

    [Header("Audio")]
    public SfxSet castSfx;
    public SfxSet failSfx;

    float nextUse, lastDuration;
    Camera cam;
    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        if (DevSettings.Blocking) return;
        if (!Pressed() || PointerOverUI()) return;
        Activate();
    }

    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (StatusController.Player != null && StatusController.Player.Silenced)
        { DamagePopup.SpawnText(transform.position, "Silenced", new Color(0.7f, 0.5f, 1f)); return false; }
        if (Time.time < nextUse) return false;
        if (cam == null) cam = Camera.main;

        var target = Player3DController.MonsterUnderCursor(cam) ?? Nearest(range);
        if (target == null) return false;
        if (PlanarDist(transform.position, target.transform.position) > range) return false;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            nextUse = Time.time + 0.4f; lastDuration = 0.4f;
            return false;
        }

        lastDuration = cooldown / Mathf.Max(0.01f, Character.Instance.CastSpeedMult);   // DEX casts faster
        nextUse = Time.time + lastDuration;
        Sfx.Play(castSfx);

        var t = target;   // capture for the on-arrive detonation
        Vector3 from = transform.position + Vector3.up * 0.5f;
        Projectile3D.Spawn(projectileSheet, projFrameSize, projFrames, projFps, projScale,
            from, t.transform, t.transform.position + Vector3.up * 0.4f, projSpeed, 0.5f, 3f, () =>
            {
                if (t == null || t.IsDead || Character.Instance == null) return;
                int power = Character.Instance.RollMagicDamage(out bool crit);
                t.TakeMagicDamage(Mathf.RoundToInt(power * damageMult), crit, Element.Fire);
                if (burnSeconds > 0f && !t.IsDead) Status.Apply(t.gameObject, Ailment.Burn, burnSeconds, burnDamage);
            },
            impactSheet, impactFrameSize, impactFrames, 22f, impactScale);
        return true;
    }

    Health Nearest(float radius)
    {
        Health best = null; float bestSqr = radius * radius;
        Vector3 me = transform.position;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;
            if (h == null || h.IsDead) continue;
            float sqr = (h.transform.position - me).sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }
        return best;
    }

    // ---- IHotbarSkill ----
    public int SlotOrder => 0;
    public string ShortLabel => "Fireball";
    public string KeyLabel => "1";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => new Color(1f, 0.6f, 0.35f);   // fire orange
    public bool InClass(PlayerClass c) => c == PlayerClass.Mage;

    static float PlanarDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && key == KeyCode.Alpha1 && Keyboard.current.digit1Key.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(key);
    }
}
