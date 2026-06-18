using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The player's basic spell — a magic bolt cast on a key press (default <b>Q</b>). It's the
/// first thing that exercises the magic stats: it spends <see cref="Character.MaxMP"/> mana,
/// deals <see cref="Character.MagicAttack"/>-based damage (via <see cref="Character.RollMagicDamage"/>),
/// and is reduced by the target's <see cref="Health.magicDefense"/>. Targets the mob under the
/// cursor, else the nearest enemy within <see cref="range"/>.
///
/// This is deliberately a single hardcoded spell, NOT the every-10th-level actives draft (that's a
/// later phase) — just enough of a magic path to make MAtk / MP / MDef real foundation stats.
/// </summary>
public class Player3DCaster : MonoBehaviour
{
    [Tooltip("Mana spent per cast.")]
    public int manaCost = 6;
    [Tooltip("Seconds between casts.")]
    public float cooldown = 1.1f;
    [Tooltip("Max distance (world units, XZ) to a target.")]
    public float range = 7f;

    [Header("Bolt FX (sliced off a horizontal sheet at runtime, like the slash)")]
    public Texture2D boltSheet;
    public int boltFrameSize = 16;
    public int boltFrames = 5;
    public float boltFps = 18f;
    public float boltScale = 1.2f;

    [Header("Audio")]
    public SfxSet castSfx;
    public SfxSet failSfx;   // not enough mana

    float nextCast;
    Camera cam;

    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (Character.Instance == null || Character.Instance.IsDead) return;
        if (!CastKey() || PointerOverUI()) return;
        if (Time.time < nextCast) return;

        var target = Player3DController.MonsterUnderCursor(cam) ?? NearestMonsterWithin(range);
        if (target == null) return;                       // nothing to cast at — don't burn mana/cooldown
        if (PlanarDist(transform.position, target.transform.position) > range) return;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            nextCast = Time.time + 0.4f;                  // brief lockout so it can't spam the popup
            return;
        }

        nextCast = Time.time + cooldown / Mathf.Max(0.01f, Character.Instance.CastSpeedMult);   // DEX casts faster
        Sfx.Play(castSfx);

        Vector3 fxPos = target.transform.position + Vector3.up * 0.4f;
        SlashEffect.Spawn(boltSheet, boltFrameSize, boltFrames, boltFps, fxPos, boltScale);

        int power = Character.Instance.RollMagicDamage(out bool crit);
        target.TakeMagicDamage(power, crit);
    }

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

    static bool CastKey()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Q);
    }
}
