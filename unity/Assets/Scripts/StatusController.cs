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

    // Persistent status-aura VFX per DoT — begun on apply (a looping Attach cue that tracks this unit) and stopped
    // when the ailment expires (see the sweep in Update / OnDestroy). Non-DoT ailments use one-shot cues instead.
    readonly VfxSequenceRunner[] aura = new VfxSequenceRunner[N];

    // Haste — a timed move-SPEED BUFF (multiplier > 1). Separate channel from the Slow ailment (which can only
    // ever slow, since its factor is Clamp01'd), so a buff and a slow can coexist and multiply. Folds into MoveMult.
    float hasteUntil;
    float hasteMult = 1f;
    static readonly Color HasteColor = new Color(0.4f, 1f, 0.7f);

    Health health;          // mob: the thing DoTs damage
    MonsterAI3D ai;          // mob: the brain we delegate Stun/Freeze/Slow to
    Player3DController player;// player: the mover — so the cast-start gate can see a mid-jump (no casting in the air)
    bool isPlayer;           // player: DoTs hit Character.Instance; flags read by the controller/spells
    float recoverUntil;      // aftercast root: fully action-locked until a skill's release animation finishes (folds into CannotAct)

    /// <summary>The player's status host, so spells/attacker can check Silence/Blind without a lookup.</summary>
    public static StatusController Player { get; private set; }

    void Awake()
    {
        health = GetComponent<Health>();
        ai     = GetComponent<MonsterAI3D>();
        player = GetComponent<Player3DController>();
        isPlayer = player != null;
        if (isPlayer) Player = this;
    }

    void OnDestroy()
    {
        if (Player == this) Player = null;
        for (int i = 0; i < N; i++) StopAura(i);
    }

    void StopAura(int i)
    {
        if (aura[i] == null) return;
        aura[i].Stop();
        aura[i] = null;
    }

    bool Active(Ailment a) => Time.time < until[(int)a];
    static bool IsDot(Ailment a) => a == Ailment.Poison || a == Ailment.Bleed || a == Ailment.Burn;

    // ---- the flags the rest of the game reads (player side) -------------------
    /// <summary>HARD crowd-control — frozen or stunned. This is the flag that INTERRUPTS a cast in progress, so it must
    /// NOT include a skill's own self-root (cast wind-up / aftercast) or the cast would abort itself. Use
    /// <see cref="ActionLocked"/> for the "can I move / start a skill?" gate.</summary>
    public bool CannotAct => Active(Ailment.Stun) || Active(Ailment.Freeze);

    /// <summary>The universal "the player can't do anything this frame" gate: hard-CC OR a self-imposed skill root (cast
    /// wind-up + aftercast recovery). Movement, MoveMult and skill activation all read THIS from one place — no
    /// scattered "am I casting / recovering?" checks. The cast-interrupt check deliberately reads <see cref="CannotAct"/>
    /// (hard-CC only) instead, so a cast never interrupts itself.</summary>
    public bool ActionLocked => CannotAct || Time.time < recoverUntil;

    /// <summary>The gate for STARTING a skill or combo: <see cref="ActionLocked"/> PLUS being mid-jump — you can't
    /// cast in the air. Kept SEPARATE from <see cref="ActionLocked"/> on purpose: the movement Update roots on
    /// ActionLocked BEFORE it runs the jump arc (<see cref="Player3DController"/>), so folding "airborne" into
    /// ActionLocked would freeze the jump. Only the cast/combo START paths read this; movement, MoveMult and the
    /// mid-cast wait keep reading ActionLocked.</summary>
    public bool CannotStartSkill => ActionLocked || (player != null && player.IsAirborne);

    /// <summary>Root the unit for <paramref name="seconds"/> as a skill's self-imposed lock (cast wind-up or RO-style
    /// aftercast delay). Pure gate: no popup, no VFX — unlike an Ailment. Extends (never shortens) any active lock.</summary>
    public void LockActions(float seconds)
    {
        if (seconds <= 0f) return;
        recoverUntil = Mathf.Max(recoverUntil, Time.time + seconds);
    }

    /// <summary>Release the self-imposed skill root immediately (does NOT touch hard-CC). Called when a cast wind-up
    /// ends — completion hands off to the aftercast lock, interruption frees the player at once.</summary>
    public void Unlock() => recoverUntil = 0f;

    /// <summary>Move-speed multiplier: 0 while rooted (hard-CC or skill lock), else the Slow factor (≤1) × the Haste
    /// buff (≥1). So a speed buff makes this &gt;1 (and a buff + slow multiply). Read by the controller to scale move
    /// speed, the footstep cadence, and the Walk/Run animation fps (<see cref="Player3DController.GaitSpeedMult"/>).</summary>
    public float MoveMult
    {
        get
        {
            if (ActionLocked) return 0f;
            float m = Active(Ailment.Slow) ? Mathf.Clamp01(mag[(int)Ailment.Slow]) : 1f;
            if (Time.time < hasteUntil) m *= hasteMult;
            return m;
        }
    }
    /// <summary>The live Haste move-speed multiplier (1 = none), or 1 once the buff has expired. Lets the dev
    /// panel show/drive the current speed buff on a slider.</summary>
    public float HasteMult => Time.time < hasteUntil ? hasteMult : 1f;
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
            if (!wasActive)
            {
                nextTick[i] = Time.time + DotInterval;   // first tick after one interval
                Announce(a);
                StartAura(a);                            // persistent aura for the ailment's duration
            }
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

        // One-shot cue on a NEW control/debuff (mirrors the announce gate). DoTs return above with their aura instead.
        if (!wasActive && CcCue(a, out var cue)) VfxCue.Fire(cue, transform);
    }

    // Begin the looping aura cue for a DoT, stopping any prior one so a refresh doesn't stack two auras.
    void StartAura(Ailment a)
    {
        int i = (int)a;
        if (aura[i] != null) aura[i].Stop();
        aura[i] = VfxCue.Begin(StartCue(a), transform);
    }

    static VfxCueId StartCue(Ailment a)
        => a == Ailment.Poison ? VfxCueId.OnPoisonStart
         : a == Ailment.Bleed  ? VfxCueId.OnBleedStart
         :                        VfxCueId.OnBurnStart;

    static VfxCueId TickCue(Ailment a)
        => a == Ailment.Poison ? VfxCueId.OnPoisonTick
         : a == Ailment.Bleed  ? VfxCueId.OnBleedTick
         :                        VfxCueId.OnBurnTick;

    // Map a control/debuff ailment to its one-shot "applied" cue. False for DoTs (they use auras).
    static bool CcCue(Ailment a, out VfxCueId cue)
    {
        switch (a)
        {
            case Ailment.Stun:    cue = VfxCueId.OnStun;    return true;
            case Ailment.Freeze:  cue = VfxCueId.OnFreeze;  return true;
            case Ailment.Slow:    cue = VfxCueId.OnSlow;    return true;
            case Ailment.Silence: cue = VfxCueId.OnSilence; return true;
            case Ailment.Blind:   cue = VfxCueId.OnBlind;   return true;
            default:              cue = default;            return false;
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
        if (!wasActive)
        {
            DamagePopup.SpawnText(transform.position + Vector3.up * 1.0f, "Haste!", HasteColor);
            VfxCue.Fire(VfxCueId.OnHaste, transform);
        }
    }

    /// <summary>Remove up to <paramref name="count"/> active ailments (DoTs and control/debuffs) — clears their
    /// timers and tears down any DoT aura. The Haste buff is left alone (it isn't an ailment). Returns how many
    /// were cleared. Reusable "cleanse" hook for skills like Mending Tide.</summary>
    public int Cleanse(int count)
    {
        if (count <= 0) return 0;
        int cleared = 0;
        for (int i = 0; i < N && cleared < count; i++)
        {
            if (Time.time >= until[i]) continue;   // not active
            until[i] = 0f;
            StopAura(i);
            cleared++;
        }
        return cleared;
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

        // Tear down any status aura whose ailment has expired (fixed small loop, no alloc; only DoT slots are ever set).
        for (int i = 0; i < N; i++)
            if (aura[i] != null && Time.time >= until[i]) StopAura(i);
    }

    void TickDot(Ailment a)
    {
        int i = (int)a;
        if (Time.time >= until[i] || Time.time < nextTick[i]) return;
        nextTick[i] = Time.time + DotInterval;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(mag[i]));
        Color c = ColorOf(a);
        VfxCue.Fire(TickCue(a), transform);   // one-shot puff per DoT tick

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
