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
    Silence,  // can't cast spells
    // Slow and Blind were here. Both only ever moved a sub-stat — move speed and accuracy — so they are authored as
    // sub-stat debuffs now, and having them here as well was a second door to the same number.
    // ⚠️ This list is MIRRORED by the head of StateId, index for index; edit the two together.
}

/// <summary>
/// One unified status-ailment host for ANY combatant — mob or player. It owns the timers for every
/// <see cref="Ailment"/>, ticks the damage-over-time ones, and exposes the control flags the rest of the
/// game reads (<see cref="CannotAct"/>, <see cref="MoveMult"/>, <see cref="Silenced"/>,
/// <see cref="Silenced"/>).
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

    float nextRegenTick, nextManaTick;   // each pool's own deadline on this unit
    float hpAccum, manaAccum;            // the sub-1-point remainder each carries between ticks


    // Ailment + Haste STATE (which are active, their timers + magnitudes) now lives in the unified per-unit
    // UnitStates store (V3). This controller keeps the VFX auras, the mob-AI delegation, the DoT ticking, and the
    // action-lock — but reads/writes state through `states`. Ailment i maps 1:1 to StateId i (matching enum order);
    // Haste writes the MovementSpeed sub-stat pile. Newest-wins overwrite (the store's policy — the intended change from the old Max timer).
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
            if (boundUnit != null) return statesCache = boundUnit.States;
            if (isPlayer) { var c = Character.Instance; return c == null ? null : (statesCache = c.States); }
            return statesCache = GetComponent<UnitStates>() ?? gameObject.AddComponent<UnitStates>();
        }
    }

    // The lasting status-aura cue per DoT — announced on apply (every watching screen draws a glow that tracks this
    // unit) and ended when the ailment expires (see the sweep in Update / OnDestroy). Non-DoT ailments use one-shot
    // cues instead.
    readonly VfxCue.Aura[] aura = new VfxCue.Aura[N];

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
    ICombatUnit Unit => boundUnit != null ? boundUnit : (isPlayer ? (ICombatUnit)Character.Instance : health);

    // WHO this host belongs to, when it cannot be worked out by looking at its own GameObject. Both of the tests
    // below — "is there a rig on me" and "is there a Health on me" — describe a body standing in a scene, and the
    // server holds fighters that have no body of their own. Told beats guessed; nothing else changes.
    ICombatUnit boundUnit;

    /// <summary>Say which unit this host belongs to. The unit's own state store becomes this host's store, so an
    /// ailment written here and a buff read there can never be two different piles.</summary>
    public void BindUnit(ICombatUnit unit)
    {
        boundUnit = unit;
        statesCache = unit != null ? unit.States : null;
    }

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

    void StopAura(int i) => VfxCue.End(ref aura[i]);

    bool Active(Ailment a) { var st = states; return st != null && st.Has((StateId)(int)a); }   // Ailment i ↔ StateId i (matching enum order)
    public static bool IsDot(Ailment a) => a == Ailment.Poison || a == Ailment.Bleed || a == Ailment.Burn;

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

    /// <summary>Only the self-imposed half of <see cref="ActionLocked"/>: rooted by this unit's own skill — a wind-up, an
    /// aftercast, a run that holds its caster. What a creature's brain waits out before it walks or attacks again.</summary>
    public bool SkillRooted => Time.time < recoverUntil;
    /// <summary>Seconds left on the self-imposed root (0 when none) — for the log line that says why input is blocked.</summary>
    public float SkillRootSecondsLeft => Mathf.Max(0f, recoverUntil - Time.time);

    /// <summary>The gate for STARTING a skill or combo: <see cref="ActionLocked"/>, PLUS being mid-jump (you can't cast
    /// in the air), PLUS being <see cref="Taunted"/>. Kept SEPARATE from <see cref="ActionLocked"/> on purpose: the
    /// movement Update roots on ActionLocked BEFORE it runs the jump arc (<see cref="Player3DController"/>), so folding
    /// "airborne" into ActionLocked would freeze the jump. Only the cast/combo START paths read this; movement,
    /// MoveMult and the mid-cast wait keep reading ActionLocked.</summary>
    public bool CannotStartSkill => ActionLocked || Taunted || (player != null && player.IsAirborne);

    /// <summary>Is this unit taunted? A taunted unit can only swing — no skill it chooses to use, whoever it is and
    /// whatever it has. The rule belongs to the taunt, not to the kit it is refusing, so it is checked HERE at the one
    /// door every skill is STARTED through rather than per-caster. Movement and basic attacks are untouched: a taunt
    /// takes the options away, never the fight.
    ///
    /// <para>PASSIVES still fire. What a taunt removes is the target's choice, and a passive is not one — it answers a
    /// moment on its own. Turning them off here would silently stop a Momentum pile filling while a mob holds you,
    /// which is a different rule nobody asked for.</para></summary>
    public bool Taunted { get { var st = states; return st != null && st.Taunted; } }

    // ---- flinch: a hit that costs HP stops you WALKING, never swinging ---------
    // The share of max HP a unit must eat, while it can still be flinched, to earn a moment of footing.
    const float PityFraction = 0.2f;
    // How long that footing lasts. Starts when the hurt reaction ends, so a hit never rescues itself.
    const float PityWindowSeconds = 0.4f;
    // A billboard mob has no rig and so no hurt clip to measure. One number, here, rather than per mob.
    const float NoRigFlinchSeconds = 0.3f;

    float flinchUntil, flinchImmuneUntil, pityDamage, lastHpLossTime;
    StanceController stanceCache;

    // ---- combat or peace: where this unit's weapon is ------------------------------------------------------------
    // Decided on the server (ticked in Update's server block); every other machine is told it by the unit's network
    // shell and only draws it.

    /// <summary>This unit's weapon: on its body, coming out, in its hands, or going back.</summary>
    public readonly WeaponDraw Draw = new WeaponDraw();
    float lastBlowDealtTime = float.NegativeInfinity, lastDrawBuffTime;
    bool drawBuffEver;

    /// <summary>This unit's blow landed on someone — restarts the quiet spell the peace bonuses wait for.</summary>
    public void NoteDealtBlow() => lastBlowDealtTime = Time.time;

    /// <summary>Are the peace bonuses paying right now: weapon on the body, and a quiet spell since the last blow
    /// dealt or taken (<see cref="WeaponDrawMath.PeaceBonusesOn"/>).</summary>
    public bool PeaceBonusesOn => WeaponDrawMath.PeaceBonusesOn(Draw.IsSheathed, Time.time, Mathf.Max(lastHpLossTime, lastBlowDealtTime));

    /// <summary>Take the weapon out, at the speed its weight allows. Server only.</summary>
    public void RequestDraw() => Draw.RequestDraw(WeaponDrawMath.DrawSeconds(HeldWeaponWeight()), Time.time);

    /// <summary>Put the weapon away, at the speed its weight allows — the same time taking it out takes. Server only.</summary>
    public void RequestSheathe() => Draw.RequestSheathe(WeaponDrawMath.DrawSeconds(HeldWeaponWeight()), Time.time);

    /// <summary>The weapon on the body at once — a weapon swapped, a new life, a body that just arrived.</summary>
    public void ResetDraw() => Draw.Reset(Time.time);

    /// <summary>A different weapon is in hand: the new one starts on the body, and the old one's draw buff ends.</summary>
    public void WeaponSwapped()
    {
        ResetDraw();
        var st = states;
        if (st == null) return;
        var list = st.Active;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (i >= list.Count) continue;
            var def = SkillCatalog.Get(list[i].sourceSkillId);
            if (def != null && def.EntryFor(SkillTrigger.OnWeaponDrawn, null) != null) st.RemoveFrom(def.id);
        }
    }

    int HeldWeaponWeight()
    {
        var u = Unit;
        Item weapon = null;
        if (u is Character person) weapon = person.GetEquipped(EquipSlotType.Weapon);
        else if (u is Health creature && creature.Brain != null && creature.Brain.Equipment != null)
        {
            var held = creature.Brain.Equipment.Get(EquipSlotType.Weapon);
            weapon = held != null ? held.def : null;
        }
        return weapon != null ? weapon.weight : 0;
    }

    // THE "WEAPON DRAWN" MOMENT. Its buff comes from the unit's own passives that answer it — at most once per
    // spacing, so drawing over and over can never keep one up.
    void WeaponDrawn()
    {
        var u = Unit;
        if (u == null || u.IsDead) return;
        if (!WeaponDrawMath.DrawBuffDue(Time.time, lastDrawBuffTime, !drawBuffEver))
        {
            if (GameLog.On(LogCat.Passive))
                GameLog.Info(LogCat.Passive, string.Concat(SpawnIds.MapOf(u), " ", SpawnIds.Label(u), " drew without a draw buff — ",
                    (WeaponDrawMath.DrawBuffSpacingSeconds - (Time.time - lastDrawBuffTime)).ToString("0.#"), "s until the next"));
            return;
        }
        drawBuffEver = true;
        lastDrawBuffTime = Time.time;
        PassiveEvents.Fire(u, SkillTrigger.OnWeaponDrawn);
    }

    /// <summary>Is this unit's MOVEMENT stopped by a hit right now? Deliberately not part of
    /// <see cref="ActionLocked"/>: a flinched unit still swings, casts and presses skills — it just can't walk.</summary>
    public bool MovementFlinched => Time.time < flinchUntil;

    /// <summary>How many flinches this unit has started. Goes up by one each time a hit actually staggers it, so
    /// whatever draws the hurt reaction can tell a fresh hit from the one it is already showing and start over.</summary>
    public int FlinchCount { get; private set; }

    /// <summary>Every disable turned away — the bought window (a skill granted it), not the earned one.</summary>
    public bool DisablesBlocked { get { var st = states; return st != null && st.DisablesBlocked; } }

    /// <summary>Can't be flinched right now: either the footing earned by bleeding, or the bought window.</summary>
    public bool FlinchImmune => Time.time < flinchImmuneUntil || DisablesBlocked;

    /// <summary>When this unit last LOST HP. The cast wind-up reads it to know a hit landed mid-cast; stamped even
    /// while flinch-immune, because shrugging off the stagger is not the same as shrugging off the interruption.</summary>
    public float LastHpLossTime => lastHpLossTime;

    /// <summary>A hit that actually cost HP landed on this unit: stop its feet for as long as its hurt reaction plays,
    /// and count the damage toward the footing it earns for eating punishment. Called from the one damage door
    /// (<see cref="CombatAuthority"/>) so player and mob are flinched by the identical rule.
    ///
    /// <para>Damage-over-time never reaches here — it goes through the true-damage channel — so ticks neither stagger
    /// a unit nor build its footing. Nor does a hit a parry erased, a shield ate or a brace banked: those cost no HP,
    /// so the damage door reports 0 and this is never called.</para></summary>
    public void TookHit(int dealt)
    {
        if (dealt <= 0) return;
        lastHpLossTime = Time.time;
        // Immune: no stagger AND no credit. Damage eaten while it cannot stop you is not punishment, so it must not
        // buy the next window — that is what would otherwise chain one window straight into the next forever.
        if (FlinchImmune) return;

        flinchUntil = Mathf.Max(flinchUntil, Time.time + FlinchSeconds());
        FlinchCount++;

        int max = Unit != null ? Unit.MaxHP : 0;
        if (max <= 0) return;
        pityDamage += dealt;
        if (pityDamage < max * PityFraction) return;
        pityDamage = 0f;                                        // paid out; the excess is dropped, never banked
        flinchImmuneUntil = flinchUntil + PityWindowSeconds;     // begins once the hurt reaction has played out
    }

    /// <summary>Wipe the stagger and the footing meter — a unit coming back to life starts its next life clean, rather
    /// than inheriting the last hit of the one that killed it or a meter half-filled by it.</summary>
    public void ClearFlinch() { flinchUntil = 0f; flinchImmuneUntil = 0f; pityDamage = 0f; }

    /// <summary>How long one flinch lasts: exactly as long as this unit's hurt animation, read from the clip through
    /// the same resolver a skill's release animation roots the caster with. No hand-typed seconds anywhere.</summary>
    float FlinchSeconds()
    {
        // Resolved on first use and kept — but only once it is actually FOUND. Latching a null would be permanent:
        // the player's rig is spawned separately and may not exist the first time something hits them, and a mob's
        // 3D body is swapped in by its own visual, so an early miss must be retried rather than remembered.
        if (stanceCache == null)
            stanceCache = isPlayer ? StanceController.Player : GetComponentInChildren<StanceController>();
        return stanceCache != null ? stanceCache.ActionDuration(StanceId.Hurt) : NoRigFlinchSeconds;
    }

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
    /// speed, the footstep cadence, and the Walk/Run animation fps (<see cref="Player3DController.LocomotionAnimSpeedMult"/>).</summary>
    public float MoveMult { get { if (ActionLocked) return 0f; var st = states; return st != null ? st.MoveMult : 1f; } }   // store folds Slow × Haste; the action-lock still zeroes it
    /// <summary>The PACE an unmeasured walk, run or sprint plays at — Slow × Haste only, NOT zeroed by a root. Read
    /// through <see cref="MoveMult"/> instead, a press that rooted the caster in the same frame left the pace at 0,
    /// and while skill releases still followed it the NEXT release held the caster for 100× its length — two minutes
    /// for Petal Storm (2026-10-12).</summary>
    public float PaceMult { get { if (CannotAct) return 0f; var st = states; return st != null ? st.MoveMult : 1f; } }
    /// <summary>The live Haste move-speed multiplier (1 = none), or 1 once the buff has expired. Lets the dev
    /// panel show/drive the current speed buff on a slider.</summary>
    public float HasteMult { get { var st = states; var h = st != null ? st.Get(StateId.MovementSpeed) : null; return h != null ? h.Mult : 1f; } }
    /// <summary>True while spell-casting is blocked.</summary>
    public bool Silenced { get { var st = states; return st != null && st.Silenced; } }
    /// <summary>HIT (accuracy) points to subtract from this unit's attacks while blinded.</summary>

    /// <summary>Inflict (or refresh) an ailment for <paramref name="seconds"/>. <paramref name="magnitude"/>
    /// means: damage-per-tick for DoTs, the speed multiplier (0..1) for Slow, or HIT lost for Blind.</summary>
    /// <param name="inflictor">The unit putting it on, when known. A damage-over-time ailment remembers the newest one,
    /// and each tick credits them as the one who cost this unit health — so a kill by poison pays the poisoner.</param>
    public void Apply(Ailment a, float seconds, float magnitude, ICombatUnit inflictor = null)
    {
        if (seconds <= 0f || states == null) return;
        // Turned away at the door rather than suppressed after landing: an immune unit never gains the ailment at all,
        // so nothing lingers to resume the moment the window closes. Only the ailments that STOP you are refused —
        // poison still poisons, blind still blinds, slow still slows.
        if (DisablesBlocked && (a == Ailment.Stun || a == Ailment.Freeze || a == Ailment.Silence)) return;
        // A worn Steadfast shortens every ailment that lands, at the door — so the state, the aura and the mob's own
        // stun all run the same shorter window.
        seconds = ArmourWords.CutBy(seconds, states.WornDisableCut);
        if (seconds <= 0f) return;
        var id = (StateId)(int)a;
        bool wasActive = states.Has(id);

        if (IsDot(a))
        {
            var s = states.Apply(id, seconds, Mathf.Max(1f, magnitude));   // magnitude = damage per tick
            // THE NEWEST INFLICTOR owns the ticks from here on: the kill goes to whoever last put it on, the same
            // "last to cost it health" rule a blow follows.
            if (s != null && !PlayerUnits.Gone(inflictor)) s.appliedBy = inflictor;
            if (s != null && !wasActive)
            {
                s.nextTick = Time.time + DotInterval;   // first tick after one interval
                StartAura(a);                           // persistent aura for the ailment's duration
            }
            return;
        }

        // Control: every remaining ailment is BINARY — it either has you or it doesn't. The two that carried a number
        // (Slow's speed factor, Blind's accuracy loss) were sub-stat moves and are authored as sub-stat debuffs now.
        states.Apply(id, seconds, 0f);

        switch (a)
        {
            case Ailment.Stun:
                if (ai != null) ai.Stun(seconds);                    // mob: reuse the freeze
                break;
            case Ailment.Freeze:
                if (ai != null) ai.Stagger(seconds);                 // mob: freeze movement/attacks
                break;
        }

        // One-shot cue on a NEW control/debuff. DoTs return above with their aura instead.
        if (!wasActive && CcCue(a, out var cue)) VfxCue.Fire(cue, transform);
    }

    // Begin the looping aura cue for a DoT, stopping any prior one so a refresh doesn't stack two auras.
    void StartAura(Ailment a)
    {
        int i = (int)a;
        VfxCue.End(ref aura[i]);
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
            case Ailment.Silence: cue = VfxCueId.OnSilence; return true;
            default:              cue = default;            return false;
        }
    }

    /// <summary>Grant (or refresh) a move-SPEED buff: multiply move speed by <paramref name="multiplier"/> (≥1 to
    /// speed up) for <paramref name="seconds"/>. Folds into <see cref="MoveMult"/> on its own channel, so it lifts
    /// the run/walk animation + footstep cadence (via <see cref="Player3DController.LocomotionAnimSpeedMult"/>) and stacks
    /// multiplicatively with a Slow. A new call replaces the current buff (latest wins).</summary>
    public void Haste(float multiplier, float seconds)
    {
        if (seconds <= 0f || states == null) return;
        // This API speaks MULTIPLIERS (×1.3), the store keeps a BONUS FRACTION (0.3) — convert once, here. It goes through
        // the one sub-stat door, so "did this start" is judged on THIS haste's own pile, never on whatever else is
        // moving speed right now.
        float bonus = Mathf.Max(0.01f, multiplier) - 1f;
        // ×1 means "take the haste off": a zero bonus replaces the running one newest-wins, which the sub-stat door
        // would refuse as applying nothing.
        if (Mathf.Approximately(bonus, 0f)) { states.Apply(StateId.MovementSpeed, seconds, 0f); return; }
        bool started = states.ApplySubStat(SubStat.MovementSpeed, bonus, seconds);
        if (started && VfxCue.SubStatStartCue(SubStat.MovementSpeed, true, bonus, out var cue)) VfxCue.Fire(cue, transform);
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

    void Update()
    {
        if (states == null) return;   // player pre-Character load window — nothing can be active yet
        // Only the damage-over-time ailments need per-frame attention; the rest are read on demand via the
        // flags above. Tick each live DoT on its own cadence and apply it to whatever we're attached to.
        // A PLAYER'S MACHINE TICKS NOTHING THAT MOVES A NUMBER — no poison, no regeneration, no mana, no brace bill.
        // The server ticks its own copy of every unit and says what happened; ticking here as well would pay the
        // same regeneration once from each machine.
        // A unit that is a watched copy never ticks either — which also covers the moments between two connections,
        // when this machine is briefly joined to no world while its copies are still standing.
        if (!GameNetwork.TheWorldIsElsewhere && !UnitIsWatchedCopy())
        {
            TickDot(Ailment.Poison);
            TickDot(Ailment.Bleed);
            TickDot(Ailment.Burn);
            if (Draw.Tick(Time.time)) WeaponDrawn();
            TickRegen();
            TickMana();
            TickBrace();
            states.EndTauntIfTaunterGone();
        }

        // Tear down a DoT's aura once its state has lapsed (Running gates it, so states.Has is only hit for the handful
        // of active DoT slots — cheap, no alloc; only DoT slots ever hold an aura).
        for (int i = 0; i < N; i++)
            if (aura[i].Running && !states.Has((StateId)i)) StopAura(i);
    }

    bool UnitIsWatchedCopy()
    {
        var u = Unit;
        return u is Health creature ? creature.IsWatchedCopy : u is Character person && person.IsWatchedCopy;
    }

    void TickDot(Ailment a)
    {
        var s = states.Get((StateId)(int)a);
        if (s == null || Time.time < s.nextTick) return;   // not active (Get returns null once lapsed) or not yet due
        s.nextTick = Time.time + DotInterval;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(s.magnitude));
        VfxCue.Fire(TickCue(a), transform);   // one-shot puff per DoT tick

        var u = Unit;
        if (u != null) CombatAuthority.ApplyTrue(u, dmg, TrueDamageCauses.OfDot(a), s.appliedBy);
    }

    // Heal-over-time (Mending Tide). Lives here — the one per-unit ticker BOTH sides already run — so a regen cast on a
    // mob actually heals it. It used to tick inside Character.Update, which is why regen was a player-only state in
    // practice even though the store held it for anyone.
    void TickRegen()
    {
        // The timer runs for EVERY unit, always: natural regen from VIT has no state entry to hang a deadline on, and
        // hanging it on the timed entry meant regen only existed while a skill was running.
        if (Time.time < nextRegenTick) return;
        nextRegenTick = Time.time + UnitStates.RegenInterval;
        var u = Unit;
        // At full health there is nothing to restore — the same stop the mana tick below has always had. Paying into
        // a full bar still announced green digits to every screen watching, once per tick, per unit, forever.
        // NOTHING COMES BACK WHILE DEAD. Health and mana stay exactly where the death left them until the unit
        // stands up again.
        if (u == null || u.IsDead || u.CurrentHP >= u.MaxHP) { hpAccum = 0f; return; }
        // Both halves are HP per SECOND: what the unit's own VIT restores, plus whatever a timed regen change adds
        // (or, negative, takes away).
        // Peace doubles only the NATURAL rate; a healing skill heals what it says.
        int pay = Owed(ref hpAccum, WeaponDrawMath.RegenPerSecond(u.HPRegenPerSecond, PeaceBonusesOn, states.SubStatBonus(SubStat.HPRegen)));
        if (pay > 0) u.Heal(pay);
    }

    // The mana pool's mirror of the health tick, on the same unit and the same rule — INT's natural rate plus any
    // timed change. Here rather than on the player, so a mob's pool is refilled by the one implementation too.
    void TickMana()
    {
        if (Time.time < nextManaTick) return;
        nextManaTick = Time.time + UnitStates.RegenInterval;
        var u = Unit;
        if (u == null || u.IsDead || u.CurrentMP >= u.MaxMP) { manaAccum = 0f; return; }
        int pay = Owed(ref manaAccum, WeaponDrawMath.RegenPerSecond(u.MPRegenPerSecond, PeaceBonusesOn, states.SubStatBonus(SubStat.MPRegen)));
        if (pay > 0) u.GainMana(pay);
    }

    /// <summary>What this interval owes a pool, carrying the sub-1-point remainder to the next tick. A rate at or
    /// below zero pays nothing and forgets the remainder — a regen debuff must never refill what it weakens, and
    /// rounding UP instead would pay a level-1 mob a whole point for a rate of 0.004 a second.</summary>
    static int Owed(ref float accum, float ratePerSecond)
    {
        if (ratePerSecond <= 0f) { accum = 0f; return 0; }
        accum += ratePerSecond * UnitStates.RegenInterval;
        int pay = Mathf.FloorToInt(accum);
        accum -= pay;
        return pay;
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
        if (u != null) CombatAuthority.ApplyTrue(u, owed, TrueDamageCause.BraceDebt);
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

    public static void Apply(GameObject go, Ailment a, float seconds, float magnitude = 0f, ICombatUnit inflictor = null)
    {
        var s = Of(go);
        if (s != null) s.Apply(a, seconds, magnitude, inflictor);
    }
}
