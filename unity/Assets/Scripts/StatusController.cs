using UnityEngine;

/// <summary>The RO-style status ailments. The first three are damage-over-time; the rest are control /
/// debuffs. Built as one generic system so adding Curse/Sleep/etc. later is just another enum entry +
/// a row in <see cref="StatusController"/>, not a new bespoke mechanic per status.</summary>
public enum Ailment
{
    Poison,   // DoT — green
    Bleed,    // DoT — red
    Burn,     // DoT — fire-orange
    Stun,     // can't act / move (yellow)
    Freeze,   // can't act / move (blue) — a distinct hard-CC
    Slow,     // move-speed reduction (magnitude = speed multiplier 0..1)
    Silence,  // can't cast spells
    Blind,    // accuracy (HIT) penalty (magnitude = HIT points lost)
}

/// <summary>
/// One unified status-ailment host for ANY combatant — mob or player. It owns the timers for every
/// <see cref="Ailment"/>, ticks the damage-over-time ones, and exposes the control flags the rest of the
/// game reads (<see cref="CannotAct"/>, <see cref="MoveMult"/>, <see cref="Silenced"/>,
/// <see cref="HitPenalty"/>).
///
/// It auto-detects what it's attached to in <see cref="Awake"/>:
///   • a MOB (<see cref="Health"/> + <see cref="MonsterAI3D"/>) — DoT ticks damage the Health, and hard-CC
///     (Stun/Freeze/Slow) is delegated to the proven <see cref="MonsterAI3D"/> freeze/slow so there's one
///     movement-CC code path, not two fighting each other;
///   • the PLAYER (<see cref="Player3DController"/>) — DoT ticks damage <see cref="Character"/>, and the
///     controller / spells read the flags directly (the player had no CC before this).
///
/// PERFORMANCE: fixed-size arrays allocated once; <see cref="Update"/> loops the small fixed set and only
/// touches anything while an ailment is live — no per-frame allocation, no Find/GetComponent in the loop.
/// </summary>
[DisallowMultipleComponent]
public class StatusController : MonoBehaviour
{
    const int N = 8;                 // = number of Ailment values
    const float DotInterval = 0.8f;  // seconds between damage-over-time ticks

    static readonly Color PoisonColor = new Color(0.55f, 0.85f, 0.35f);
    static readonly Color BleedColor  = new Color(1f, 0.3f, 0.3f);
    static readonly Color BurnColor   = new Color(1f, 0.55f, 0.25f);
    static readonly Color StunColor   = new Color(1f, 0.9f, 0.3f);
    static readonly Color FreezeColor = new Color(0.6f, 0.85f, 1f);
    static readonly Color SilenceColor= new Color(0.7f, 0.5f, 1f);
    static readonly Color BlindColor  = new Color(0.7f, 0.7f, 0.75f);

    readonly float[] until    = new float[N];   // when each ailment expires
    readonly float[] nextTick = new float[N];   // next DoT tick time (DoTs only)
    readonly float[] mag      = new float[N];   // per-ailment magnitude (DoT dmg, slow factor, or HIT loss)

    // Haste — a timed move-SPEED BUFF (multiplier > 1). Separate channel from the Slow ailment (which can only
    // ever slow, since its factor is Clamp01'd), so a buff and a slow can coexist and multiply. Folds into MoveMult.
    float hasteUntil;
    float hasteMult = 1f;
    static readonly Color HasteColor = new Color(0.4f, 1f, 0.7f);

    Health health;          // mob: the thing DoTs damage
    MonsterAI3D ai;          // mob: the brain we delegate Stun/Freeze/Slow to
    bool isPlayer;           // player: DoTs hit Character.Instance; flags read by the controller/spells

    /// <summary>The player's status host, so spells/attacker can check Silence/Blind without a lookup.</summary>
    public static StatusController Player { get; private set; }

    void Awake()
    {
        health = GetComponent<Health>();
        ai     = GetComponent<MonsterAI3D>();
        isPlayer = GetComponent<Player3DController>() != null;
        if (isPlayer) Player = this;
    }

    void OnDestroy() { if (Player == this) Player = null; }

    bool Active(Ailment a) => Time.time < until[(int)a];
    static bool IsDot(Ailment a) => a == Ailment.Poison || a == Ailment.Bleed || a == Ailment.Burn;

    // ---- the flags the rest of the game reads (player side) -------------------
    /// <summary>Frozen or stunned — the player can't move or act this frame.</summary>
    public bool CannotAct => Active(Ailment.Stun) || Active(Ailment.Freeze);
    /// <summary>Move-speed multiplier: 0 while hard-CC'd, else the Slow factor (≤1) × the Haste buff (≥1). So a
    /// speed buff makes this &gt;1 (and a buff + slow multiply). Read by the controller to scale move speed, the
    /// footstep cadence, and the Walk/Run animation fps (<see cref="Player3DController.GaitSpeedMult"/>).</summary>
    public float MoveMult
    {
        get
        {
            if (CannotAct) return 0f;
            float m = Active(Ailment.Slow) ? Mathf.Clamp01(mag[(int)Ailment.Slow]) : 1f;
            if (Time.time < hasteUntil) m *= hasteMult;
            return m;
        }
    }
    /// <summary>True while spell-casting is blocked.</summary>
    public bool Silenced => Active(Ailment.Silence);
    /// <summary>HIT (accuracy) points to subtract from this unit's attacks while blinded.</summary>
    public int HitPenalty => Active(Ailment.Blind) ? Mathf.RoundToInt(mag[(int)Ailment.Blind]) : 0;

    /// <summary>Inflict (or refresh) an ailment for <paramref name="seconds"/>. <paramref name="magnitude"/>
    /// means: damage-per-tick for DoTs, the speed multiplier (0..1) for Slow, or HIT lost for Blind.</summary>
    public void Apply(Ailment a, float seconds, float magnitude)
    {
        if (seconds <= 0f) return;
        int i = (int)a;
        bool wasActive = Active(a);
        until[i] = Mathf.Max(until[i], Time.time + seconds);

        if (IsDot(a))
        {
            mag[i] = Mathf.Max(1f, magnitude);
            if (!wasActive) nextTick[i] = Time.time + DotInterval;   // first tick after one interval
            if (!wasActive) Announce(a);
            return;
        }

        switch (a)
        {
            case Ailment.Stun:
                if (ai != null) ai.Stun(seconds);                    // mob: reuse the freeze + head-swirl
                else AnnounceIfNew(a, wasActive, "Stun!", StunColor);
                break;
            case Ailment.Freeze:
                if (ai != null) ai.Stagger(seconds);                 // mob: freeze movement/attacks
                AnnounceIfNew(a, wasActive, "Frozen!", FreezeColor);
                break;
            case Ailment.Slow:
                mag[i] = Mathf.Clamp01(magnitude <= 0f ? 0.5f : magnitude);
                if (ai != null) ai.Slow(mag[i], seconds);            // mob: reuse the chill
                AnnounceIfNew(a, wasActive, "Slow!", FreezeColor);
                break;
            case Ailment.Silence:
                AnnounceIfNew(a, wasActive, "Silenced!", SilenceColor);
                break;
            case Ailment.Blind:
                mag[i] = Mathf.Max(1f, magnitude <= 0f ? 30f : magnitude);
                AnnounceIfNew(a, wasActive, "Blind!", BlindColor);
                break;
        }
    }

    /// <summary>Grant (or refresh) a move-SPEED buff: multiply move speed by <paramref name="multiplier"/> (≥1 to
    /// speed up) for <paramref name="seconds"/>. Folds into <see cref="MoveMult"/> on its own channel, so it lifts
    /// the run/walk animation + footstep cadence (via <see cref="Player3DController.GaitSpeedMult"/>) and stacks
    /// multiplicatively with a Slow. A new call replaces the current buff (latest wins).</summary>
    public void Haste(float multiplier, float seconds)
    {
        if (seconds <= 0f) return;
        bool wasActive = Time.time < hasteUntil;
        hasteMult = Mathf.Max(0.01f, multiplier);
        hasteUntil = Time.time + seconds;
        if (!wasActive) DamagePopup.SpawnText(transform.position + Vector3.up * 1.0f, "Haste!", HasteColor);
    }

    void AnnounceIfNew(Ailment a, bool wasActive, string label, Color c)
    {
        if (!wasActive) DamagePopup.SpawnText(transform.position + Vector3.up * 1.0f, label, c);
    }

    void Announce(Ailment a) => DamagePopup.SpawnText(transform.position + Vector3.up * 1.0f, a.ToString() + "!", ColorOf(a));

    static Color ColorOf(Ailment a)
    {
        switch (a)
        {
            case Ailment.Poison: return PoisonColor;
            case Ailment.Bleed:  return BleedColor;
            case Ailment.Burn:   return BurnColor;
            default:             return Color.white;
        }
    }

    void Update()
    {
        // Only the damage-over-time ailments need per-frame attention; the rest are read on demand via the
        // flags above. Tick each live DoT on its own cadence and apply it to whatever we're attached to.
        TickDot(Ailment.Poison);
        TickDot(Ailment.Bleed);
        TickDot(Ailment.Burn);
    }

    void TickDot(Ailment a)
    {
        int i = (int)a;
        if (Time.time >= until[i] || Time.time < nextTick[i]) return;
        nextTick[i] = Time.time + DotInterval;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(mag[i]));
        Color c = ColorOf(a);

        if (isPlayer)
        {
            if (Character.Instance != null) Character.Instance.TakeTrueDamage(dmg, c);
        }
        else if (health != null)
        {
            health.TakeTrueDamage(dmg, c);
        }
    }
}

/// <summary>Convenience front door: find-or-add the <see cref="StatusController"/> on a unit and apply an
/// ailment. Mobs/player get one in their own Awake, so this normally just fetches it; the find-or-add keeps
/// callers robust if a unit was built before the system existed.</summary>
public static class Status
{
    public static StatusController Of(GameObject go)
    {
        if (go == null) return null;
        var s = go.GetComponent<StatusController>();
        return s != null ? s : go.AddComponent<StatusController>();
    }

    public static void Apply(GameObject go, Ailment a, float seconds, float magnitude = 0f)
    {
        var s = Of(go);
        if (s != null) s.Apply(a, seconds, magnitude);
    }
}
