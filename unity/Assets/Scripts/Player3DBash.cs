using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The Swordsman's signature active — <b>Bash</b> (skill slot <b>1</b>): a heavy, always-landing melee
/// strike that hits for a multiple of a normal swing and briefly <see cref="MonsterAI3D.Stagger"/>s the
/// target. Spends MP, gated by its own cooldown (shortened by AttackSpeed, like the swing). Targets the
/// mob under the cursor, else the nearest enemy within <see cref="range"/>.
///
/// This is the TEMPLATE for class active skills: an input/target/MP/cooldown wrapper (mirroring
/// <see cref="Player3DCaster"/>) that delegates the actual hit to <see cref="Player3DAttacker.Strike"/>.
/// Every future class kit = a few components like this on the player.
/// </summary>
[RequireComponent(typeof(Player3DAttacker))]
public class Player3DBash : MonoBehaviour, IHotbarSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    [Tooltip("Key that triggers Bash (skill slot 1).")]
    public KeyCode key = KeyCode.Alpha1;
    [Tooltip("Mana spent per use.")]
    public int manaCost = 5;
    [Tooltip("Seconds between uses (shortened by AttackSpeed).")]
    public float cooldown = 3.5f;
    [Tooltip("Max distance (world units, XZ) to a target.")]
    public float range = 2.2f;
    [Tooltip("Damage as a multiple of a normal swing.")]
    public float damageMult = 2.6f;
    [Tooltip("Slash-FX size multiplier (heavier-looking swing).")]
    public float fxScale = 1.6f;
    [Tooltip("Seconds the target is frozen on hit.")]
    public float staggerSecs = 0.4f;

    [Header("Audio")]
    public SfxSet bashSfx;   // heavy swing (falls back to the weapon/default swing)
    public SfxSet hitSfx;    // heavy impact (falls back to the weapon/default impact)
    public SfxSet failSfx;   // not enough mana

    float nextUse;
    float lastDuration;        // the cooldown actually applied last use (for the hotbar's sweep fraction)
    Camera cam;
    Player3DAttacker attacker;

    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);

    void Awake() => attacker = GetComponent<Player3DAttacker>();

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        if (DevSettings.Blocking) return;                 // cursor over the dev panel — don't leak the keypress
        if (!Pressed() || PointerOverUI()) return;
        Activate();
    }

    /// <summary>Fire Bash now — shared by the keypress and the HUD hotbar click. Same cooldown / MP /
    /// targeting rules either way. Returns true only if it actually struck.</summary>
    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (Time.time < nextUse) return false;
        if (cam == null) cam = Camera.main;

        var target = Player3DController.MonsterUnderCursor(cam) ?? NearestMonsterWithin(range);
        if (target == null) return false;                 // nothing to hit — don't burn MP/cooldown
        if (PlanarDist(transform.position, target.transform.position) > range) return false;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            nextUse = Time.time + 0.4f; lastDuration = 0.4f;   // brief lockout so it can't spam the popup
            return false;
        }

        lastDuration = cooldown / Mathf.Max(0.01f, Character.Instance.AttackSpeedMult);   // ASPD speeds it up
        nextUse = Time.time + lastDuration;
        attacker.Strike(target, damageMult, fxScale, bashSfx, hitSfx, staggerSecs);
        return true;
    }

    // ---- IHotbarSkill (drives the HUD hotbar slot) ----
    public int SlotOrder => 0;
    public string ShortLabel => "Bash";
    public string KeyLabel => "1";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => new Color(1f, 0.55f, 0.5f);   // heavy-strike red

    static readonly Collider[] hits = new Collider[32];

    Health NearestMonsterWithin(float radius)
    {
        Health best = null; float bestSqr = radius * radius;
        int n = Physics.OverlapSphereNonAlloc(transform.position, radius, hits);
        for (int i = 0; i < n; i++)
        {
            var h = hits[i].GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.GetComponent<MonsterAI3D>() == null) continue;
            float sqr = (h.transform.position - transform.position).sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }
        return best;
    }

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
