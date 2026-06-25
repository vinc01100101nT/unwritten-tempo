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
/// <para><b>Cast time (pre-renewal RO).</b> The bolt has a wind-up that shrinks with DEX:
/// <c>castTime = baseCastTime × Character.CastTimeMult</c> (= <c>1 − DEX/150</c>, instant at DEX 150).
/// During the wind-up the player is rooted (by <see cref="Player3DController"/>, which reads
/// <see cref="Casting"/>) and a <see cref="CastBar"/> floats over their head. The spell fires when the
/// bar fills. Mana is spent at cast START (RO behaviour) and is LOST if the cast is interrupted by:
/// taking damage, moving, or stun/freeze/silence. At DEX 150 the cast is instant (no bar, no root).</para>
///
/// This is deliberately a single hardcoded spell, NOT the every-10th-level actives draft (that's a
/// later phase) — just enough of a magic path to make MAtk / MP / MDef real foundation stats.
/// </summary>
public class Player3DCaster : MonoBehaviour, IHotbarSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    [Tooltip("Mana spent per cast (spent when the cast STARTS; lost if interrupted, RO-style).")]
    public int manaCost = 6;
    [Tooltip("Seconds between casts (cooldown AFTER a cast resolves; shortened by DEX via CastSpeedMult).")]
    public float cooldown = 1.1f;
    [Tooltip("Max distance (world units, XZ) to a target.")]
    public float range = 7f;

    [Header("Cast time (pre-renewal: actual = base × (1 − DEX/150))")]
    [Tooltip("Base wind-up in seconds at 0 DEX. Scaled down by DEX; reaches 0 (instant) at DEX 150.")]
    public float baseCastTime = 1.2f;
    [Tooltip("Casts at or below this many seconds fire instantly (no bar, no root) — covers the DEX-150 case.")]
    public float instantThreshold = 0.06f;
    [Tooltip("Height above the player the cast bar floats at.")]
    public float castBarHeight = 0.9f;

    [Header("Bolt FX (sliced off a horizontal sheet at runtime, like the slash)")]
    public Texture2D boltSheet;
    public int boltFrameSize = 16;
    public int boltFrames = 5;
    public float boltFps = 18f;
    public float boltScale = 1.2f;

    [Header("Audio")]
    public SfxSet castSfx;
    public SfxSet failSfx;   // not enough mana / interrupted

    float nextCast;
    float lastDuration;        // the cooldown actually applied last cast (for the hotbar's sweep fraction)
    Camera cam;

    // ---- cast (wind-up) state ----
    bool casting;
    float castStart, castEnd;
    Health castTarget;

    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);
    static readonly Color InterruptColor = new Color(0.85f, 0.55f, 0.55f);
    static readonly Color CastTint = new Color(0.55f, 0.7f, 1f);   // magic blue (matches Tint)

    /// <summary>True while a spell is winding up — read by <see cref="Player3DController"/> to root the
    /// player and to interrupt the cast when a move is ordered.</summary>
    public bool Casting => casting;

    void OnEnable()  { var ch = Character.Instance; if (ch != null) ch.OnDamaged += OnDamaged; }
    void OnDisable() { var ch = Character.Instance; if (ch != null) ch.OnDamaged -= OnDamaged; CancelSilently(); }

    // Character.Instance is set on Play; OnEnable may run before it exists, so (re)bind in Start too.
    void Start() { var ch = Character.Instance; if (ch != null) { ch.OnDamaged -= OnDamaged; ch.OnDamaged += OnDamaged; } }

    void OnDamaged(int dmg) { if (casting) Interrupt("Interrupted!"); }   // taking a hit cancels the cast

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) { CancelSilently(); return; }

        if (casting) { TickCast(); return; }   // winding up: don't read a new cast key until it resolves

        if (!CastKey() || PointerOverUI()) return;
        Activate();
    }

    /// <summary>Begin the bolt now — shared by the keypress and the HUD hotbar click. Returns true only if
    /// it actually started (or fired, for an instant cast).</summary>
    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (casting) return false;                                          // already winding up
        if (StatusController.Player != null && (StatusController.Player.Silenced || StatusController.Player.CannotAct))
        { DamagePopup.SpawnText(transform.position, "Silenced", new Color(0.7f, 0.5f, 1f)); return false; }
        if (Time.time < nextCast) return false;
        if (cam == null) cam = Camera.main;

        var target = Player3DController.MonsterUnderCursor(cam) ?? NearestMonsterWithin(range);
        if (target == null) return false;                 // nothing to cast at — don't burn mana/cooldown
        if (PlanarDist(transform.position, target.transform.position) > range) return false;

        if (!Character.Instance.SpendMana(manaCost))       // mana is spent up-front (RO), lost if interrupted
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            nextCast = Time.time + 0.4f; lastDuration = 0.4f;   // brief lockout so it can't spam the popup
            return false;
        }

        // Pre-renewal cast time: base wind-up scaled by DEX (instant at DEX 150).
        float castTime = baseCastTime * Character.Instance.CastTimeMult;
        if (castTime <= instantThreshold) { Resolve(target); return true; }   // instacast — no bar, no root

        casting = true;
        castTarget = target;
        castStart = Time.time;
        castEnd = Time.time + castTime;
        CastBar.Set(transform.position + Vector3.up * castBarHeight, 0f, ShortLabel, CastTint);
        return true;
    }

    // Advance the wind-up: refresh the bar, watch for interrupts, and fire when it completes.
    void TickCast()
    {
        // Target gone (died / despawned) → the cast fizzles.
        if (castTarget == null || castTarget.IsDead) { Interrupt("Failed"); return; }

        // Status interrupts: stun/freeze (CannotAct) or silence cancel an in-flight cast.
        var st = StatusController.Player;
        if (st != null && (st.CannotAct || st.Silenced)) { Interrupt("Interrupted!"); return; }

        float progress = Mathf.InverseLerp(castStart, castEnd, Time.time);
        CastBar.Set(transform.position + Vector3.up * castBarHeight, progress, ShortLabel, CastTint);

        if (Time.time >= castEnd)
        {
            var t = castTarget;
            EndCast();
            Resolve(t);
        }
    }

    // Fire the bolt at the target and start the cooldown. Called at cast completion (or directly for an
    // instant cast). The mana was already spent in Activate.
    void Resolve(Health target)
    {
        lastDuration = cooldown / Mathf.Max(0.01f, Character.Instance.CastSpeedMult);   // DEX shortens the cooldown
        nextCast = Time.time + lastDuration;

        if (target == null || target.IsDead) return;      // fizzle (cooldown still applies); no FX/damage

        Sfx.Play(castSfx);
        Vector3 fxPos = target.transform.position + Vector3.up * 0.4f;
        SlashEffect.Spawn(boltSheet, boltFrameSize, boltFrames, boltFps, fxPos, boltScale);

        int power = Character.Instance.RollMagicDamage(out bool crit, target.luk);   // target.luk = pre-renewal crit shield
        target.TakeMagicDamage(power, crit);
    }

    /// <summary>Cancel an in-flight cast with feedback (popup + sound). The mana spent at cast start is LOST
    /// (RO-style). Public so <see cref="Player3DController"/> can cancel on a move order.</summary>
    public void Interrupt(string reason)
    {
        if (!casting) return;
        EndCast();
        Sfx.Play(failSfx);
        DamagePopup.SpawnText(transform.position + Vector3.up * 0.6f, reason, InterruptColor);
        nextCast = Time.time + 0.25f; lastDuration = 0.25f;   // tiny lockout so it can't instantly re-cast
    }

    /// <summary>Move-order interrupt entry point for the controller.</summary>
    public void Interrupt() => Interrupt("Cancelled");

    // Drop the cast with no feedback (death / disable) — the bar just disappears.
    void CancelSilently() { if (casting) EndCast(); }

    void EndCast() { casting = false; castTarget = null; CastBar.Hide(); }

    // ---- IHotbarSkill (drives the HUD hotbar slot) ----
    public int SlotOrder => 4;            // after the Swordsman actives (Bash/Charge/Guard/Warcry); Bolt is the universal magic
    public string ShortLabel => "Bolt";
    public string KeyLabel => "Q";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextCast - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => new Color(0.55f, 0.7f, 1f);   // magic blue
    public bool InClass(PlayerClass c) => true;        // universal basic (both classes have the bolt)

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
