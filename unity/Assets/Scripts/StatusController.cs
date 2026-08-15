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

    // Ailment + Haste STATE (which are active, their timers + magnitudes) now lives in the unified per-unit
    // UnitStates store (V3). This controller keeps the VFX auras, the mob-AI delegation, the DoT ticking, and the
    // action-lock — but reads/writes state through `states`. Ailment i maps 1:1 to StateId i (matching enum order);
    // Haste is StateId.Haste. Newest-wins overwrite (the store's policy — the intended change from the old Max timer).
    UnitStates statesCache;
    /// <summary>The unit's ONE state store — the single source that BOTH gameplay and the buff tray read, so no state
    /// can be visible to one and not the other. The PLAYER is built from TWO GameObjects (the persistent Character data
    /// object + the spawned body that carries THIS controller), so the player shares <see cref="Character"/>'s store;
    /// otherwise a state written here (Haste, poison, slow…) would land in a store the tray + combat math never read.
    /// Mobs are one GameObject, so they own their store. Resolved lazily because the two player GameObjects' Awake order
    /// isn't guaranteed — by the first Update (all Awakes done) <see cref="Character.Instance"/> is set. Null only in the
    /// pre-Character load window, when no state can yet have been applied.</summary>
    UnitStates states
    {
        get
        {
            if (statesCache != null) return statesCache;
            if (isPlayer) { var c = Character.Instance; return c == null ? null : (statesCache = c.States); }
            return statesCache = GetComponent<UnitStates>() ?? gameObject.AddComponent<UnitStates>();
        }
    }

    // Persistent status-aura VFX per DoT — begun on apply (a looping Attach cue that tracks this unit) and stopped
    // when the ailment expires (see the sweep in Update / OnDestroy). Non-DoT ailments use one-shot cues instead.
    readonly VfxSequenceRunner[] aura = new VfxSequenceRunner[N];
    static readonly Color HasteColor = new Color(0.4f, 1f, 0.7f);

    Health health;          // mob: the thing DoTs damage
    MonsterAI3D ai;          // mob: the brain we delegate Stun/Freeze/Slow to
    Player3DController player;// player: the mover — so the cast-start gate can see a mid-jump (no casting in the air)
    bool isPlayer;           // player: DoTs hit Character.Instance; flags read by the controller/spells
    float recoverUntil;      // aftercast root: fully action-locked until a skill's release animation finishes (folds into CannotAct)

    /// <summary>The player's status host, so spells/attacker can check Silence/Blind without a lookup.</summary>
    public static StatusController Player { get; private set; }

    /// <summary>WHOEVER this controller is attached to, as the shared combat handle — the player's stats object or this
    /// mob's Health. Every per-tick effect below (DoT damage, heal-over-time) goes through this instead of branching on
    /// isPlayer, so a new one is written ONCE and works on both sides.</summary>
    ICombatUnit Unit => isPlayer ? Character.Instance : health;

    void Awake()
    {
        health = GetComponent<Health>();
        ai     = GetComponent<MonsterAI3D>();
        player = GetComponent<Player3DController>();
        isPlayer = player != null;
        if (isPlayer) Player = this;
        // `states` resolves lazily (see the property) — for the player it shares Character's store, which may not exist
        // yet this Awake. A mob resolves its own store on first touch. Nothing reads state before the first Update.
        if (!isPlayer) { var _ = states; }   // warm the mob's own store now (no Character dependency)
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

    bool Active(Ailment a) { var st = states; return st != null && st.Has((StateId)(int)a); }   // Ailment i ↔ StateId i (matching enum order)
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
    public float MoveMult { get { if (ActionLocked) return 0f; var st = states; return st != null ? st.MoveMult : 1f; } }   // store folds Slow × Haste; the action-lock still zeroes it
    /// <summary>The live Haste move-speed multiplier (1 = none), or 1 once the buff has expired. Lets the dev
    /// panel show/drive the current speed buff on a slider.</summary>
    public float HasteMult { get { var st = states; var h = st != null ? st.Get(StateId.Haste) : null; return h != null ? h.Mult : 1f; } }
    /// <summary>True while spell-casting is blocked.</summary>
    public bool Silenced { get { var st = states; return st != null && st.Silenced; } }
    /// <summary>HIT (accuracy) points to subtract from this unit's attacks while blinded.</summary>
    public int HitPenalty { get { var st = states; return st != null ? st.HitPenalty : 0; } }

    /// <summary>Inflict (or refresh) an ailment for <paramref name="seconds"/>. <paramref name="magnitude"/>
    /// means: damage-per-tick for DoTs, the speed multiplier (0..1) for Slow, or HIT lost for Blind.</summary>
    public void Apply(Ailment a, float seconds, float magnitude)
    {
        if (seconds <= 0f || states == null) return;
        var id = (StateId)(int)a;
        bool wasActive = states.Has(id);

        if (IsDot(a))
        {
            var s = states.Apply(id, seconds, Mathf.Max(1f, magnitude));   // magnitude = damage per tick
            if (s != null && !wasActive)
            {
                s.nextTick = Time.time + DotInterval;   // first tick after one interval
                Announce(a);
                StartAura(a);                           // persistent aura for the ailment's duration
            }
            return;
        }

        // Control / debuff: compute the per-ailment magnitude (Slow factor / Blind HIT loss; the rest are binary).
        float mg = a == Ailment.Slow  ? Mathf.Clamp01(magnitude <= 0f ? 0.5f : magnitude)
                 : a == Ailment.Blind ? Mathf.Max(1f, magnitude <= 0f ? 30f : magnitude)
                 : 0f;
        // Slow is AUTHORED as a target-speed factor (0.4 = moves at 40%) while the store keeps a bonus FRACTION
        // (how much speed is lost) — convert here, so the authored meaning and the tooltip stay as they are.
        states.Apply(id, seconds, a == Ailment.Slow ? 1f - mg : mg);

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
                // No mob delegation: the store's Slow entry (written above) drives move speed via MoveMult, which the
                // mob mover now reads — one source for player AND mob. Stun/Freeze still delegate (they gate the AI brain).
                AnnounceIfNew(a, wasActive, "Slow!", FreezeColor);
                break;
            case Ailment.Silence:
                AnnounceIfNew(a, wasActive, "Silenced!", SilenceColor);
                break;
            case Ailment.Blind:
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
        if (seconds <= 0f || states == null) return;
        bool wasActive = states.Has(StateId.Haste);
        // This API speaks MULTIPLIERS (×1.3), the store keeps a BONUS FRACTION (0.3) — convert once, here.
        states.Apply(StateId.Haste, seconds, Mathf.Max(0.01f, multiplier) - 1f);
        if (!wasActive)
        {
            DamagePopup.SpawnText(UnitAnchor.Overhead(transform), "Haste!", HasteColor);
            VfxCue.Fire(VfxCueId.OnHaste, transform);
        }
    }

    /// <summary>Remove up to <paramref name="count"/> active ailments (DoTs and control/debuffs) — clears their
    /// timers and tears down any DoT aura. The Haste buff is left alone (it isn't an ailment). Returns how many
    /// were cleared. Reusable "cleanse" hook for skills like Mending Tide.</summary>
    public int Cleanse(int count)
    {
        if (count <= 0 || states == null) return 0;
        int cleared = 0;
        for (int i = 0; i < N && cleared < count; i++)   // N = the 8 ailments (StateId 0..7); Haste/buffs aren't cleansed
        {
            var id = (StateId)i;
            if (!states.Has(id)) continue;   // not active
            states.Remove(id);
            StopAura(i);
            cleared++;
        }
        return cleared;
    }

    void AnnounceIfNew(Ailment a, bool wasActive, string label, Color c)
    {
        if (!wasActive) DamagePopup.SpawnText(UnitAnchor.Overhead(transform), label, c);
    }

    void Announce(Ailment a) => DamagePopup.SpawnText(UnitAnchor.Overhead(transform), a.ToString() + "!", ColorOf(a));

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
        if (states == null) return;   // player pre-Character load window — nothing can be active yet
        // Only the damage-over-time ailments need per-frame attention; the rest are read on demand via the
        // flags above. Tick each live DoT on its own cadence and apply it to whatever we're attached to.
        TickDot(Ailment.Poison);
        TickDot(Ailment.Bleed);
        TickDot(Ailment.Burn);
        TickRegen();
        TickBrace();

        // Tear down a DoT's aura once its state has lapsed (aura[i] != null gates it, so states.Has is only hit for
        // the handful of active DoT slots — cheap, no alloc; only DoT slots ever hold an aura).
        for (int i = 0; i < N; i++)
            if (aura[i] != null && !states.Has((StateId)i)) StopAura(i);
    }

    void TickDot(Ailment a)
    {
        var s = states.Get((StateId)(int)a);
        if (s == null || Time.time < s.nextTick) return;   // not active (Get returns null once lapsed) or not yet due
        s.nextTick = Time.time + DotInterval;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(s.magnitude));
        Color c = ColorOf(a);
        VfxCue.Fire(TickCue(a), transform);   // one-shot puff per DoT tick

        var u = Unit;
        if (u != null) CombatAuthority.ApplyTrue(u, dmg, c);
    }

    // Heal-over-time (Mending Tide). Lives here — the one per-unit ticker BOTH sides already run — so a regen cast on a
    // mob actually heals it. It used to tick inside Character.Update, which is why regen was a player-only state in
    // practice even though the store held it for anyone.
    void TickRegen()
    {
        var s = states.Get(StateId.Regen);
        if (s == null || Time.time < s.nextTick) return;
        s.nextTick = Time.time + UnitStates.RegenInterval;
        var u = Unit;
        if (u != null) u.Heal(s.intAmount);
    }

    // Brace: damage held back during the window comes due the moment it ENDS — as true damage, because the mitigation
    // that applied to each hit was already taken off when it was banked (UnitMitigation.Deferred); running it through
    // defence a second time would discount the same hit twice.
    //
    // The STORE decides what came due: it is the only thing that sees a delayed hit's own deadline pass, and it can
    // tell the two endings apart by construction — a stack a consumer CASHED is removed by that consumer and never
    // reaches the sweeper, while a stack that RAN OUT is collected on its way out. This side only pays the bill.
    //
    // Reading a running total (rather than watching a pile) is what makes several delaying skills work: each owes on
    // its own clock, and whatever expires this frame — from one pile or three — arrives here as one number to charge.
    void TickBrace()
    {
        int owed = states.TakeExpiredDebt();
        if (owed <= 0) return;
        var u = Unit;
        if (u != null) CombatAuthority.ApplyTrue(u, owed, BraceDueColor);
    }

    // The bill coming due gets its OWN colour, deliberately not Brace's blue: the blue numbers are damage you dodged
    // for now, this one is damage you are actually taking, and they must not read as the same event. Pushed toward
    // magenta rather than violet so it can't be mistaken for magic damage either (that's 0.7, 0.5, 1).
    static readonly Color BraceDueColor = new Color(0.8f, 0.25f, 0.9f);
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
