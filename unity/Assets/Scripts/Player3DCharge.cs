using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The Swordsman's gap-closer — <b>Charge</b> (skill slot <b>2</b>): a fast dash to a target (even a
/// fleeing kiter), landing a staggering hit on arrival. Spends MP, own cooldown. Targets the mob under
/// the cursor, else the nearest enemy within <see cref="acquireRange"/> (much longer than melee — that's
/// the point).
///
/// Like <see cref="Player3DBash"/>, this is a thin input/target/MP/cooldown wrapper (the class-active
/// template). The movement is delegated to <see cref="Player3DController.BeginDash"/> and the on-arrival
/// hit to <see cref="Player3DAttacker.Strike"/>.
/// </summary>
[RequireComponent(typeof(Player3DController))]
[RequireComponent(typeof(Player3DAttacker))]
public class Player3DCharge : MonoBehaviour, IHotbarSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    [Tooltip("Key that triggers Charge (skill slot 2).")]
    public KeyCode key = KeyCode.Alpha2;
    [Tooltip("Mana spent per use.")]
    public int manaCost = 6;
    [Tooltip("Seconds between uses.")]
    public float cooldown = 6f;
    [Tooltip("How far away a target can be to charge it (world units, XZ).")]
    public float acquireRange = 7f;
    [Tooltip("Dash speed during the charge.")]
    public float dashSpeed = 14f;
    [Tooltip("Safety cap: end the dash after this long even if the target keeps running.")]
    public float dashMaxTime = 0.55f;
    [Tooltip("Stop the dash (and strike) once within this distance of the target.")]
    public float arriveRange = 1.3f;
    [Tooltip("Arrival-hit damage as a multiple of a normal swing.")]
    public float damageMult = 2.0f;
    [Tooltip("Slash-FX size multiplier on the arrival hit.")]
    public float fxScale = 1.4f;
    [Tooltip("Seconds the target is frozen on arrival.")]
    public float staggerSecs = 0.5f;

    [Header("Audio")]
    public SfxSet chargeSfx;   // launch whoosh
    public SfxSet hitSfx;      // arrival impact (falls back to the weapon/default impact)
    public SfxSet failSfx;     // not enough mana

    float nextUse;
    float lastDuration;        // the cooldown actually applied last use (for the hotbar's sweep fraction)
    Camera cam;
    Player3DController controller;
    Player3DAttacker attacker;

    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);

    void Awake()
    {
        controller = GetComponent<Player3DController>();
        attacker = GetComponent<Player3DAttacker>();
    }

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        if (DevSettings.Blocking) return;
        if (!Pressed() || PointerOverUI()) return;
        Activate();
    }

    /// <summary>Fire Charge now — shared by the keypress and the HUD hotbar click. Returns true only if
    /// the dash actually launched.</summary>
    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (Time.time < nextUse || controller.Dashing) return false;   // off cooldown + not already charging
        if (cam == null) cam = Camera.main;

        var target = Player3DController.MonsterUnderCursor(cam) ?? Nearest(acquireRange);
        if (target == null) return false;
        if (PlanarDist(transform.position, target.transform.position) > acquireRange) return false;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            nextUse = Time.time + 0.4f; lastDuration = 0.4f;
            return false;
        }

        nextUse = Time.time + cooldown; lastDuration = cooldown;
        Sfx.Play(chargeSfx);

        var t = target;   // capture for the on-arrive strike
        controller.BeginDash(t.transform, dashSpeed, dashMaxTime, arriveRange,
            () => attacker.Strike(t, damageMult, fxScale, null, hitSfx, staggerSecs));
        return true;
    }

    // ---- IHotbarSkill (drives the HUD hotbar slot) ----
    public int SlotOrder => 1;
    public string ShortLabel => "Charge";
    public string KeyLabel => "2";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => new Color(1f, 0.75f, 0.4f);   // gap-closer amber

    // Nearest living mob within radius — via the MonsterAI3D registry (cached .Health, squared distance),
    // the perf-correct target scan (NOT a fixed-buffer OverlapSphere, which drops mobs in dense scenes).
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

    static float PlanarDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && key == KeyCode.Alpha2 && Keyboard.current.digit2Key.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(key);
    }
}
