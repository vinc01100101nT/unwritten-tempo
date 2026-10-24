using UnityEngine;

/// <summary>
/// The runtime interpreter for a data-authored <see cref="SkillDef"/> — the ONE component that turns any composed
/// skill into a playable hotbar active, replacing a hand-written C# skill class per spell. It plays the def's
/// <see cref="VfxSequence"/> and, on each gameplay <see cref="VfxTick"/> the sequence raises, looks up the matching
/// <see cref="SkillTickBinding"/> and applies its effects through <see cref="SkillEffectApplier"/> — the shared
/// combat path. The FINAL numbers are rolled off the live caster stats there, so this class stores no damage; the
/// asset only says WHAT each tick does; the power scales off the live caster stats (the ring), NOT a skill level.
///
/// <para>Attach one per skill on the player (a build tool does this). It implements <see cref="IHotbarSkill"/> so
/// the bar shows it automatically. Aim mode (<see cref="SkillDef.aim"/>) decides how the cast point is picked.</para>
/// </summary>
public class SkillRunner : MonoBehaviour, IHotbarSkill
{
    [Tooltip("The authored skill this runner plays. Set by the build tool or in the inspector.")]
    public SkillDef def;

    float nextUse, lastDuration;

    // WHEN THIS SKILL IS NEXT READY, as a moment on the world's clock — the one value that means anything once
    // it leaves this process. It is what gets saved, what crosses to another server, and what a map that went to
    // sleep wakes up holding. `nextUse` above is its per-frame MIRROR and nothing else: the gate and the hotbar
    // ring ask several times a frame, and a float compare is what PERFORMANCE.md allows there where reading a
    // date is not. Set them together, always; the moment is the owner and the float is derived from it.
    System.DateTime dueAtUtc = System.DateTime.MinValue;

    /// <summary>When this skill is ready again, on the world's clock. <see cref="System.DateTime.MinValue"/>
    /// when it has never been cast, which is the same fact as "ready now" and is stored as neither.</summary>
    public System.DateTime DueAtUtc => dueAtUtc;

    /// <summary>Put a cooldown back exactly where it was, from a moment that outlived this process — a save
    /// being loaded, or a character arriving from another server. Rebuilds the per-frame mirror against THIS
    /// machine's timer, which is the whole reason the moment is stored instead of a countdown.</summary>
    // The connection the last press of this skill was sent on, until the server answers it. Null = nothing waiting.
    Mirror.NetworkConnection awaitingOn;

    /// <summary>The server answered this skill's last press — cast or refused. Any cooldown it started arrived first.</summary>
    public void ServerAnsweredPress() => awaitingOn = null;

    // The run of beats this cast started on this machine, so a press the server refused can be stopped.
    Coroutine runRoutine;

    /// <summary>Is a run of beats of this skill still going on this machine — and how many charges does it see left?
    /// For the log line that says why input is blocked.</summary>
    public bool RunInProgress => runRoutine != null;
    public int RunChargesSeen => StateGate.StackCount(def != null ? def.CastEntry : null, CasterStates);

    /// <summary>THE SERVER REFUSED THE LAST PRESS — the skill did not run there, so the picture of it here stops: its run
    /// of beats ends and the hold it put on the caster lets go. Without this a run that lasts while a pile only the
    /// server spends (a charge-driven run) never ended, and held its caster still for good.</summary>
    public void ServerRefusedPress()
    {
        if (runRoutine != null) { StopCoroutine(runRoutine); runRoutine = null; }
        if (castRoutine != null || castAura.Running || castCircle != null) EndCast();
        if (def != null && def.holdCasterForRun) Status?.Unlock();
    }

    public void ResumeCooldown(System.DateTime moment)
    {
        dueAtUtc = moment;
        float left = WorldClock.SecondsUntil(moment);
        lastDuration = def != null ? def.cooldown : left;
        nextUse = Time.time + left;
    }

    /// <summary>A skill's effect made this skill ready again (<see cref="SkillEffectKind.ResetCooldown"/>) — server
    /// only. The cooldown ends now, and the owner is told it ended now, so their bar shows it ready. The shared ultimate
    /// cooldown is left running.</summary>
    public void ResetCooldown()
    {
        if (!GameServer.IsServer || def == null) return;
        if (Time.time >= nextUse) return;
        ForgetCooldown();
        GameNetwork.TellOwnerCooldownStarted(Caster, def.id, WorldClock.UtcNow);
    }

    // The animation this skill's release plays: its authored cue, or one of the two releases the weapon in hand owns.
    StanceId ReleaseStance => UsesWeaponSkillAnimation
        ? WeaponStances.SkillAnimationFor(Caster.HeldWeapon, def.weaponSkillAnimation) : def.releaseStance;

    bool UsesWeaponSkillAnimation => def.weaponSkillAnimation != WeaponSkillAnimation.None && !CasterGone;

    /// <summary>Make this skill ready now, as if it had never been cast — a creature starting a new life.</summary>
    public void ForgetCooldown()
    {
        dueAtUtc = System.DateTime.MinValue;
        nextUse = 0f;
        lastDuration = 0f;
    }

    /// <summary>What would stop a cast of this skill starting right now — the questions <see cref="CastFromRequest"/>
    /// asks, minus the aim.</summary>
    public enum CastBlock { None, NothingToPlay, Passive, CasterGone, WindingUp, Cooldown, Silenced, CannotStart, Pile, Mana }

    /// <summary>Why a cast could not START right now, asked WITHOUT trying one — so a creature choosing between its
    /// skills skips the ones that would be refused. <see cref="CastBlock.None"/> = it could. Allocates nothing, so it can
    /// be asked on every swing; turning the answer into words is <see cref="DescribeBlock"/>'s job, and only a log asks.</summary>
    public CastBlock BlockedBy
    {
        get
        {
            if (def == null || def.vfx == null) return CastBlock.NothingToPlay;
            if (def.IsPassive) return CastBlock.Passive;
            if (CasterGone || Caster.IsDead) return CastBlock.CasterGone;
            if (castRoutine != null) return CastBlock.WindingUp;
            if (Time.time < nextUse || SharedHeld) return CastBlock.Cooldown;
            var gate = Status;
            if (gate != null && gate.Silenced) return CastBlock.Silenced;
            if (gate != null && gate.CannotStartSkill) return CastBlock.CannotStart;
            if (!StateGate.IsMet(def.CastEntry, CasterStates)) return CastBlock.Pile;
            if (Caster.CurrentMP < def.manaCost) return CastBlock.Mana;
            return CastBlock.None;
        }
    }

    /// <summary>True when nothing in <see cref="BlockedBy"/> stops a cast.</summary>
    public bool CouldCastNow => BlockedBy == CastBlock.None;

    /// <summary>The words for <paramref name="block"/>, with this skill's own numbers — for the log only. Worded as
    /// the refusal a real attempt writes, so a skipped skill and a refused cast read alike.</summary>
    public string DescribeBlock(CastBlock block)
    {
        switch (block)
        {
            case CastBlock.None: return "ready";
            case CastBlock.NothingToPlay: return "the skill has nothing to play";
            case CastBlock.Passive: return "it is a passive — it only fires on its own triggers";
            case CastBlock.CasterGone: return "the caster is gone or dead";
            case CastBlock.WindingUp: return "already winding up a cast";
            case CastBlock.Cooldown: return string.Concat("still on cooldown — ", CooldownRemaining.ToString("0.#"), "s left");
            case CastBlock.Silenced: return "silenced";
            case CastBlock.CannotStart: return "stunned, frozen, taunted, already casting, or mid-jump";
            case CastBlock.Pile: return "the pile this skill spends is not there yet";
            case CastBlock.Mana:
                return string.Concat("not enough mana — ", Caster.CurrentMP.ToString(), " held, ", def.manaCost.ToString(), " needed");
            default: return block.ToString();
        }
    }

    // WHICH authored trigger started the run in progress — set at every entry point (a press, a passive
    // fire) and read by everything that must differ between them: which state gate applies, what the run spends, and
    // whether finishing it announces "the player cast this". Never null while a run is resolving.
    SkillTriggerEntry firedBy;
    Coroutine castRoutine;              // active cast wind-up (null = not casting); blocks re-cast + drives the CastBar
    // RELEASE EARLY (SkillDef.releaseEarly): the server's wind-up start and length, and whether a release was asked
    // for and when. The server decides the scale from its own clock; the owner's machine only sends the request.
    float windupStartedAt, windupSeconds, releasedAt;
    bool releaseAsked;
    // The damage scale this cast's strikes carry (StrikeMath.ReleaseScale) — 1 for every cast that is not one.
    float castReleaseScale = 1f;
    // ON THE OWNER'S MACHINE: the wind-up the server announced (seconds, and its start on the shared clock), so the
    // owner's bar shows the server's numbers rather than a sum worked out here. Not told yet = an empty bar.
    bool windupTold;
    double toldStartedAt;
    float toldSeconds;
    int pendingMana;                    // this cast's cost, checked at the press and taken at the release (never both)
    VfxCue.Aura castAura;               // the server's announced OnCastStart aura for this wind-up; MUST be Ended at every exit
    // The pose held through this skill's wind-up: the first part of the weapon's skill animation when that animation
    // has a mark to hold at, else the weapon's own cast pose. Chosen once when the wind-up starts, so the push, the pop
    // and every screen told about it name the same pose.
    StanceId castPose;
    StanceId PickCastPose()
    {
        if (def.hasReleaseStance && UsesWeaponSkillAnimation)
        {
            var b = StanceCatalog.Get(ReleaseStance);
            if (b != null && b.holdAtFrame > 0) return ReleaseStance;
        }
        return WeaponStances.CastFor(def != null ? def.weaponType : WeaponType.None);
    }
    Vfx.EffectHandle castCircle;        // the caster's own ring (def.castCircleId) on their screen; MUST be Stopped at every exit
    VfxContext lastCtx;                 // captured per cast so OnTick can read the Single-scope target unit
    System.Action<VfxTick> onTick;      // cached hook (no per-play delegate alloc)
    System.Action<Vector3> onGround;    // cached ground-confirm hook
    System.Action<Transform> onUnit;    // cached single-target confirm hook
    Transform pendingUnit;              // stage-1 pick, held while stage 2 (the point) is being aimed. A field, not a
                                        // closure, so the confirm hooks above stay cached (no per-cast delegate alloc).
                                        // Cleared on every Activate and on teardown — a targeter's Cancel never calls back.
    System.Func<string, float> stayFor; // cached per-tickId Stay Duration resolver (built once; no per-cast alloc)
    // Resolve a looping step's Stay Duration from its tick binding (by tickId). Handed to the VFX context so the
    // runner supplies each step its own stay; the VFX never stores this per-use gameplay number.
    System.Func<string, float> StayFor => stayFor ??= tid => def.StayForTick(tid);
    System.Func<string, ProjectileFan> fanFor;   // cached per-tickId projectile fan resolver (built once; no per-cast alloc)
    // Resolve a carrier step's volley from its tick binding (by tickId) — the same per-step shape as Stay Duration.
    // Only a tick authored as Projectile delivery fans; anything else launches the one carrier the VFX composed, so a
    // fall-in meteor can never be multiplied by a count meant for a bolt.
    System.Func<string, ProjectileFan> FanFor => fanFor ??= tid => def.FanForTick(tid);
    Player3DController body;            // caster's mover — turned to face the cast point on release

    // ---- state gate + no-self-feeding chain -----------------------------------------------------------------------
    // castChain = the causal chain this cast belongs to (0 for a player-pressed active — a press starts a fresh chain).
    // A passive receives its chain from the dispatch and stamps it onto everything its effects cause, so the brake can
    // see "this passive is already upstream of me" no matter how many frames later the damage lands.
    ulong castChain;
    bool consumedThisCast;              // the gate spends ONCE per cast, right after the effects have computed
    // The required state's stack count SNAPSHOT at cast start — taken before anything is spent, and reused by every
    // tick of the cast. A live re-read would pay the first tick off the full pile and the rest off an empty one, and
    // would also make a multi-tick finisher's damage depend on tick ORDER. (Locked order: snapshot → compute → consume.)
    int castStacks;
    // The same pile read the OTHER way — its stacks' summed VALUE, as one number whatever channel they use (a
    // percentage pile is already a decimal: +30% arrives as 0.3). Float, because that reading can be fractional.
    float castCarried;
    // Whether THIS cast is efficient (SkillEfficiency) — rolled once at its release, read by every tick it runs.
    bool castEfficient;
    // The MP and HP this cast gave up to its all-in costs (Spend All Mana, Drop Health To One), paid ONCE, by the first
    // tick that carries one — so every later tick, spread tick and repeat of the cast reads the same amount, and a
    // repeat never spends the MP regained since. -1 = this cast has not paid them yet.
    int castGivenUp = -1;

    // WHOSE RUN THIS IS. A server holds one character per person signed in, so "the player" is not a fact it can
    // read off a global — it has to be told. Bound once when the fighter is built; left unbound on a screen, where
    // there is exactly one character and it is the one at this keyboard. Same shape as Character.BindStatusHost,
    // deliberately: the two answer the same kind of question.
    ICombatUnit boundCaster;

    /// <summary>Say whose character this runner casts for. One-way for the life of the runner.
    ///
    /// <para>THE ONE PLACE BOTH RUNNER-CREATION PATHS MEET, which is why a cooldown that outlived the process is
    /// picked up here. A runner is built either when a character binds its whole skill list or lazily the first
    /// time one skill is used; putting this in either alone would restore half of them.</para></summary>
    public void BindCaster(ICombatUnit c)
    {
        boundCaster = c;
        // Only a PERSON carries cooldowns that outlived a process — a creature's life starts every skill ready.
        if (c is Character person && def != null && person.TakePendingCooldown(def.id, out var dueAtUtc))
        {
            ResumeCooldown(dueAtUtc);
            // AND THE OWNER IS TOLD, so a cooldown carried across a relog or a door shows on their bar instead of
            // reading as ready until a press is refused. Says nothing off a server.
            GameNetwork.TellOwnerCooldownStarted(c, def.id, dueAtUtc);
        }
    }

    /// <summary>The unit this run belongs to — a person or a creature — and the one every roll, gate and cost reads.</summary>
    public ICombatUnit Caster => boundCaster != null ? boundCaster : Character.Instance;

    /// <summary>Is the caster missing — never bound, or destroyed? An interface reference cannot answer that with a
    /// plain null check (see <see cref="PlayerUnits.Gone"/>).</summary>
    bool CasterGone => PlayerUnits.Gone(Caster);

    /// <summary>TRUE when this runner casts for the person sitting at this machine, so it is the one that may draw
    /// a cast bar, push a pose, or say the skill's name out loud. False on a server, and false for a character the
    /// server holds on somebody else's behalf.</summary>
    bool Mine => Character.Instance != null && ReferenceEquals(Caster, Character.Instance);

    /// <summary>TRUE when this machine only WATCHES the character casting — the server holds the real one, so
    /// nothing here may touch health, states or piles. The same answer <see cref="Character.IsWatchedCopy"/> gives
    /// the damage door, asked once for a whole cast instead of once per blow.</summary>
    bool Watching => GameNetwork.TheWorldIsElsewhere || (!CasterGone && Caster.IsWatchedCopy);

    /// <summary>The status host of whoever is casting — the caster's own, never a global.</summary>
    StatusController Status => CasterGone ? null : Caster.StatusHost;

    /// <summary>The caster's unified state store — the ONE store the gate reads and spends from.</summary>
    public UnitStates CasterStates => CasterGone ? null : Caster.States;

    void Awake()
    {
        onTick = OnTick;
        onGround = Cast;
        onUnit = CastTargeted;
        body = GetComponent<Player3DController>();
    }

    // A runner no longer announces itself to a process-wide table. Its caster HOLDS its skills in a fixed order
    // (UnitSkills — a person's and an armed creature's alike), which is both the list a passive moment is dispatched
    // against and the thing that gives each passive its place in the no-self-feeding brake. One list per unit rather
    // than one per machine, because a server holds a character for everybody signed in. The caster owns the POSITIONS; a runner like this
    // one is built the first time that position is genuinely used, and its caster is bound at that moment.

    // If we're torn down mid-cast (disabled, scene swap, death), Unity stops the coroutine but won't run its cleanup,
    // so drop the shared bar + kill the looping aura here or they'd hang on screen.
    void OnDisable()
    {
        pendingUnit = null;   // a half-aimed cast dies with the runner; never resume stage 2 against a stale pick
        pendingMana = 0;      // …and an unfinished wind-up is never charged for
        awaitingOn = null;
        runRoutine = null;   // Unity stopped it with the component; a stopped run is not a run in progress
        if (castRoutine != null || castAura.Running || castCircle != null) EndCast();
        // its yellow totals leave with it (a runner that never struck looks nothing up)
        if (castTotals.Count > 0) StrikeTotals.Forget(castTotals, SkillNumber);
        if (landingTotals.Count > 0) StrikeTotals.Forget(landingTotals, SkillNumber);
    }

    // ---- activation ---------------------------------------------------------

    /// <summary>May this skill START right now — asked identically wherever a cast begins, so the machine deciding a
    /// cast and the machine picturing it can never disagree about whether it was allowed. Sets the entry this run
    /// answers to as a side effect, because a press always runs the OnCast entry.</summary>
    bool CanStart()
    {
        if (def == null || def.vfx == null) return Refused("the skill has nothing to play");
        if (def.IsPassive) return Refused("it is a passive — it only fires on its own triggers");
        firedBy = def.CastEntry;           // a press always runs the OnCast entry, whatever other triggers this skill has
        if (CasterGone || Caster.IsDead) return Refused("the caster is gone or dead");
        if (Mine && TeleportLook.OwnWaitingToAppear) return Refused("away on a teleport");
        var gate = Status;
        if (gate != null && gate.Silenced)
        { if (Mine) OverheadNotice.Warn("Silenced"); return Refused("silenced"); }
        if (gate != null && gate.CannotStartSkill) return Refused("stunned, frozen, already casting, or mid-jump");
        if (castRoutine != null) return Refused("already winding up a cast");
        if (Time.time < nextUse) return Refused("still on cooldown");
        if (SharedHeld) return Refused("every ultimate shares a cooldown, and it is still running");
        // A PRESS THE SERVER HAS NOT ANSWERED YET. This machine starts no cooldown, so without this a second press
        // inside the round trip would send a second cast and play its picture again. Tied to the connection it was
        // sent on, so a press lost to a dropped connection can never lock the skill for the next one.
        if (awaitingOn != null && awaitingOn == Mirror.NetworkClient.connection)
            return Refused("waiting for the server to answer the last press");

        if (GameLog.On(LogCat.Cast)) GameLog.Info(LogCat.Cast, string.Concat(CasterName(), " casts ", SkillName()));
        return true;
    }

    // EVERY "no" ABOVE SAYS WHY. A refused cast used to be six silent `return false`s that looked identical from
    // outside, so a play-test could only report "nothing happened" and had no way to find out which of the six it
    // was. Returns false so it reads as the answer at each call site rather than as a statement beside it.
    bool Refused(string why)
    {
        lastRefusal = why;
        if (GameLog.On(LogCat.Cast))
            GameLog.Info(LogCat.Cast, string.Concat(CasterName(), " could not cast ", SkillName(), " — ", why));
        return false;
    }

    // THE LAST REASON THIS RUNNER SAID NO. Kept because the line above only reaches the log when the cast category
    // is switched on, and the server's door has to be able to say the reason OUT LOUD whatever the log settings are —
    // a refusal nobody can see is the whole bug this exists to end.
    string lastRefusal;

    string SkillName() => def != null ? def.id : "(no skill)";

    // "field_15 Alpha #1" — the map and the spawn_id, so a cast line names the same unit the hit lines do.
    string CasterName() => string.Concat(SpawnIds.MapOf(Caster), " ", SpawnIds.Label(Caster));

    /// <summary>Cast this skill for the character the server holds, with the aim already decided by the person who
    /// asked for it. The SERVER'S entry point, and deliberately the same body a press runs — the aiming is all that
    /// differs, because the reticle happened on their machine and the answer arrived here.</summary>
    public bool CastFromRequest(Transform aimedAt, Vector3 point, out string why)
    {
        why = null;
        // The Release is never cast itself: the press arrives as the combination it made.
        if (def != null && def.isOrbRelease) { why = "the Release is pressed as the combination the orbs make"; return false; }
        if (!CanStart()) { why = lastRefusal; return false; }
        // A skill that asks the caster to PICK something must have been given one; without it the cast would silently
        // fall back to an area sweep the person never aimed. Asked BEFORE anything is paid, so a cast that cannot
        // happen costs neither the mana nor the cooldown.
        if (def.NeedsUnitPick && aimedAt == null) { why = "it needs a target picked and none arrived"; return false; }
        if (!Spend()) { why = lastRefusal; return false; }
        lastCtx = BuildCtx(def.NeedsUnitPick ? aimedAt : null, def.NeedsAimPoint ? point : transform.position);
        StartCastOrPlay();
        return true;
    }

    public bool Activate()
    {
        if (def != null && def.isOrbRelease) return PressRelease(null);
        if (!CanStart()) return false;
        // A weapon skill needs the weapon in hand. Pressed with it away, the press is held, the weapon comes out, and
        // the press runs again — aimed fresh — once the server says it is in hand.
        if (Mine && def.weaponType != WeaponType.None && !WeaponDrawClient.Ready)
        {
            (Caster as Character)?.StopSprinting();
            WeaponDrawClient.HoldPress(this);
            return false;
        }

        // The aim is a SEQUENCE derived from the ticks (SkillDef.NeedsUnitPick / NeedsAimPoint), never an authored
        // field: pick an entity (only when target effects need one), then pick a point (only when a ground tick or a
        // relocating effect needs one). Either stage can be absent — a self-dash asks only for the point, a plain nuke
        // only for the entity, "dash the mob I clicked over there" asks for both, in that order.
        // A re-press mid-aim cancels the whole sequence, including a stage-1 pick already banked.
        if (UnitTargeter.Active) { UnitTargeter.Cancel(); pendingUnit = null; return false; }
        if (GroundTargeter.Active) { GroundTargeter.Cancel(); pendingUnit = null; return false; }
        // A SECOND PRESS CALLS OFF A WALK, exactly as it cancels an aim still on screen — the two are the same
        // gesture from the player's seat: "never mind".
        if (waitingForRange)
        {
            waitingForRange = false;
            (Caster as Character)?.StopWalkingIntoRange();
            pendingUnit = null;
            return false;
        }
        pendingUnit = null;

        if (def.NeedsUnitPick)
        {
            // Stage 1: land on ONE valid unit, validated against the union of the single-target ticks' affect kinds
            // (warns in-game on an illegal / absent pick).
            var mask = def.SingleTargetMask;
            if (mask == SkillTargets.None)
            { OverheadNotice.Warn("No valid target"); return false; }
            UnitTargeter.Begin(transform, def.castRange, mask, onUnit);
            return true;   // MP/cooldown spent on the FINAL confirm, so cancelling any stage costs nothing
        }
        if (def.NeedsAimPoint) { BeginPointAim(); return true; }

        CastResolved(null, transform.position);   // no aim at all (a pure self buff)
        return true;
    }

    /// <summary>PRESS IT AT <paramref name="target"/>, with no aim on screen — what auto-combat does with its slots. The
    /// same press as <see cref="Activate"/> in every way that decides anything (the same gates, the same spend, the same
    /// request to the server), only aimed for the player: a single-target skill at the creature, a ground skill at where
    /// it stands, a self skill at nobody. Never walks: a target out of the skill's range is no press at all, so the hunt
    /// keeps its own legs. FALSE when it was not pressed.</summary>
    public bool ActivateAt(Transform target)
    {
        if (target == null || def == null) return false;
        if (def.isOrbRelease) return PressRelease(target);
        // ANYTHING ALREADY ON SCREEN WINS: an aim the player started, or a walk into range, is theirs.
        if (UnitTargeter.Active || GroundTargeter.Active || waitingForRange) return false;
        // A weapon skill with the weapon away is left for the hunt, which takes the weapon out on its own way in.
        if (Mine && def.weaponType != WeaponType.None && !WeaponDrawClient.Ready) return false;
        if (def.NeedsUnitPick && (def.SingleTargetMask & SkillTargets.Mobs) == 0) return false;
        if ((def.NeedsUnitPick || def.NeedsAimPoint) && TooFarToCast(target)) return false;
        if (!CanStart()) return false;
        pendingUnit = null;
        if (def.NeedsUnitPick) CastResolved(target, target.position);
        else if (def.NeedsAimPoint) CastResolved(null, target.position);
        else CastResolved(null, transform.position);
        return true;
    }

    // Stage 2 of the aim. Entered straight from Activate (no entity to pick) or from the stage-1 confirm below.
    void BeginPointAim() =>
        GroundTargeter.Begin(transform, def.castRange, onGround,
                             def.CastCircle, def.castCircleTint, def.LargestAreaRadius, def.IsProjectileAim);

    // Stage-1 confirm: bank the entity, then either ask for the point or fire straight away.
    void CastTargeted(Transform unit)
    {
        if (unit == null) return;
        if (def.NeedsAimPoint) { pendingUnit = unit; BeginPointAim(); return; }

        // TOO FAR IS A WALK, NOT A REFUSAL — the same answer a basic attack has always given. The gap is re-measured
        // every frame while walking, so a target that moves is followed and the cast fires on the first frame the
        // real distance is inside the skill's range, instead of on a distance measured once at the click.
        //
        // A cast that also asks for a POINT is deliberately left alone: that point was aimed from where the caster
        // stood, so walking somewhere else and then firing it would put the skill down where nobody aimed.
        var person = Caster as Character;
        if (TooFarToCast(unit) && person != null
            && person.WalkIntoRange(unit.transform, def.castRange, () => { waitingForRange = false; CastResolved(unit, unit.position); }))
        {
            waitingForRange = true;
            return;
        }

        CastResolved(unit, unit.position);
    }

    // A cast this runner is holding until its caster has walked close enough. Only ever true on the machine of the
    // person who pressed it — a server holds no legs, so WalkIntoRange refuses there and this never turns on.
    bool waitingForRange;

    // Is the picked unit outside what this skill can reach. Flat distance, squared on both sides, and answered as
    // NO by any skill that authored no range at all.
    bool TooFarToCast(Transform unit)
    {
        if (unit == null || def == null || def.castRange <= 0f) return false;
        Vector3 gap = unit.position - transform.position; gap.y = 0f;
        return gap.sqrMagnitude > def.castRange * def.castRange;
    }

    // Stage-2 confirm (also the only confirm for a point-only cast): fire with whatever stage 1 banked.
    void Cast(Vector3 point)
    {
        var unit = pendingUnit; pendingUnit = null;
        // Said here as well as refused by the server, so a press at a spot nobody can stand on spends nothing and
        // plays nothing. The server asks the same question and its answer is the one that counts.
        if (unit == null && def.MovesCasterToAimPoint && !Pathfinder3D.CanLandAt(transform.position, point, SkillEffectApplier.TripRadius))
        { OverheadNotice.Warn("Can't land there"); return; }
        CastResolved(unit, point);
    }

    // The ONE solo-cast resolve: spend, gate cooldown, play the sequence. `unit` = the stage-1 pick (null when the
    // cast asked for no entity), `point` = the stage-2 point (the caster's own spot when it asked for none).
    void CastResolved(Transform unit, Vector3 point)
    {
        if (!Spend()) return;

        // SAID TO THE SERVER, ONCE, for the whole skill. Everything the skill does is decided there against the
        // character it holds for this person; what happens below is the picture of it, started now so the game feels
        // exactly as immediate as it always has.
        if (GameNetwork.SendCastToServer(def, unit, point)) awaitingOn = Mirror.NetworkClient.connection;

        lastCtx = BuildCtx(unit, point);
        StartCastOrPlay();
    }

    // The ONE place a cast's VfxContext is built — a press and a passive both come through here so no path can
    // forget a field (a missed effectPoint would silently dash the caster to the world origin).
    //   origin = the CASTER (Origin anchor).
    //   target = where the VFX plays (Target anchor): the picked entity if there is one, else the aimed point. A
    //            self cast collapses both onto the caster; a ground cast keeps them apart, so an Origin-anchored step
    //            (Blink's departure flash) plays at the caster's spot, not the destination.
    //   effectPoint = where a relocating effect SENDS what it caught — a separate question from where the VFX plays.
    //   follow = the CASTER so an Attach step (a self aura / dash trail) RIDES the player and fires its gameplay
    //            tick, like VfxCue sets follow = actor. PlayAt/Carrier steps ignore follow, so ground AoEs are unaffected.
    VfxContext BuildCtx(Transform unit, Vector3 point) => new VfxContext
    {
        origin = transform.position,
        target = unit != null ? unit.position : point,
        effectPoint = point,
        follow = transform,
        targetUnit = unit,
        scaleMult = 1f,
        stayForTick = StayFor,
        fanForTick = FanFor,
    };

    // The single fork between an instant skill and one with a wind-up. A castTime of 0 fires immediately — NO cast
    // bar and NO OnCastStart cue (the cue marks a real wind-up). Otherwise a coroutine winds up first, then plays.
    void StartCastOrPlay()
    {
        // Turn the caster to face where it aimed — for every skill regardless of scope (ground/target/aoe), on this
        // first frame only. A self-cast aims at the caster's own feet, so it changes nothing.
        if (body != null) body.FaceAtAttackStart(lastCtx.target);

        if (def.HasWindup && isActiveAndEnabled)
            castRoutine = StartCoroutine(CastWindup());
        else
            PlayCycles();
    }

    // Wind up for castTime × the caster's DEX-driven CastTimeMult (instacast at 150 DEX), filling the shared CastBar
    // over the caster's head each frame. The OnCastStart cue is Begun as a PERSISTENT aura that loops for the whole
    // wind-up and is Stopped the instant it ends (complete or interrupt) — so its cue sequence should be authored as an
    // Attach/looping effect (a one-shot bound there would just flash). Interrupted by death / silence / hard-CC — MP is
    // already spent (RO-style, an interrupted cast is a wasted cast). No per-frame alloc: the CastBar is a cached
    // singleton and the label is a stable reference (it only rewrites the Text on change).
    System.Collections.IEnumerator CastWindup()
    {
        float mult = CasterGone ? 1f : Caster.CastTimeMult;
        float dur = UnitStatMath.CastWindupSeconds(def.castTime, mult, def.fixedCastTime);
        float startedAt = Time.time;   // any HP lost after this instant breaks the wind-up
        windupStartedAt = startedAt; windupSeconds = dur; releaseAsked = false; windupTold = false;
        // THE SERVER HOLDS A WINDING-UP PLAYER IN PLACE: its movement check refuses travel while this is set
        // (NetPlayer.MoveWasPossible), so a client that ignores its own root cannot walk through a cast.
        if (!Watching && Caster is Character windingUp) windingUp.WindupRunner = this;

        if (dur > 0f)
        {
            BeginCastVisuals();
            // THE WIND-UP, TO EVERY MACHINE WATCHING THE CASTER. Nothing about a cast reached a watcher until it
            // was already going off, so somebody else's four-second spell was four seconds of them standing still
            // followed by an explosion. Sent from the server, which is where the wind-up that decides anything
            // runs; the caster's own machine draws its bar from this very coroutine, a line below.
            // …AND WHERE IT IS AIMED, so the ring that opens on the ground under a wind-up appears on every screen
            // and not only the caster's. A watcher holds the same authored skill and reads its own ring off that;
            // the one thing it cannot know is the spot, which is why the spot is what travels.
            GameNetwork.AnnounceCastWindup(Caster, def, dur, lastCtx.target);
            // HOLD THE CAST POSE for the whole wind-up: the skill's weapon's own (WeaponStances.CastFor) — stretched to
            // the cast bar, or stopped part-way and held like the bow's draw.
            castPose = PickCastPose();
            if (Mine && StanceController.Player != null) StanceController.Player.Push(castPose, dur);
            // A CREATURE holds the same pose for the same window, told to every screen watching it.
            GameNetwork.AnnounceHeldOnCaster(Caster, true, castPose, dur);
            var sc = Status;
            if (sc != null) sc.LockActions(dur);   // fully root for the wind-up (no move/attack/other skills); freed in EndCast on complete or interrupt
            string label = def.displayName;
            for (float t = 0f; ; t += Time.deltaTime)
            {
                // THE OWNER'S MACHINE follows the server's wind-up once told it; until then it waits out its own.
                bool ownerTold = Watching && windupTold;
                double toldElapsed = ownerTold ? Mirror.NetworkTime.time - toldStartedAt : 0d;
                if (ownerTold ? toldElapsed >= toldSeconds : t >= dur) break;
                // Broken by death, silence, hard CC — and now by any hit that actually cost HP, which is the whole
                // reason a long wind-up is a gamble. The earned footing does NOT save a cast (shrugging off a stagger
                // is not the same as keeping your concentration); only the bought window does, which is what makes
                // that window worth buying.
                if (CasterGone || Caster.IsDead ||
                    (sc != null && (sc.Silenced || sc.CannotAct ||
                                    (!sc.DisablesBlocked && sc.LastHpLossTime > startedAt))))
                { pendingMana = 0; EndCast(); yield break; }
                // RELEASE EARLY: the player pressed a movement key. Their machine plays the release and asks the
                // server, which fires the skill scaled by how much of ITS wind-up was filled.
                if (Mine && Watching && def.releaseEarly && !releaseAsked && MovementKeyPressed()) AskRelease();
                if (releaseAsked)
                {
                    float scale = Watching ? 1f : StrikeMath.ReleaseScale(windupStartedAt, releasedAt, windupSeconds);
                    EndCast();
                    PlayCycles(scale);
                    yield break;
                }
                if (Mine) CastBar.Set(transform, ownerTold ? (float)(toldElapsed / toldSeconds) : 0f, label, def.tint);
                yield return null;
            }
        }

        EndCast();
        PlayCycles(def.releaseEarly && !Watching ? StrikeMath.ReleaseScale(startedAt, startedAt + dur, dur) : 1f);
    }

    static bool MovementKeyPressed() =>
        Hotkeys.Down(GameKeys.MoveForward) || Hotkeys.Down(GameKeys.MoveLeft)
        || Hotkeys.Down(GameKeys.MoveBack) || Hotkeys.Down(GameKeys.MoveRight);

    // The owner's machine: end its own picture of the wind-up and ask the server to fire. Intent only — the server
    // measures the moment on its own clock (ReleaseWindup).
    void AskRelease()
    {
        releaseAsked = true;
        releasedAt = Time.time;
        GameNetwork.SendCastRelease(def);
    }

    /// <summary>SERVER: the owner asked to release this skill's wind-up now. Ignored unless this skill is winding up,
    /// allows releasing early, and has not already been released.</summary>
    public bool ReleaseWindup()
    {
        if (Watching || castRoutine == null || def == null || !def.releaseEarly || releaseAsked) return false;
        releaseAsked = true;
        releasedAt = Time.time;
        return true;
    }

    /// <summary>THE OWNER'S MACHINE: the server's wind-up for this skill began at <paramref name="startedAt"/> on the
    /// shared clock and lasts <paramref name="seconds"/> — the bar shows that from now on.</summary>
    public void TellWindup(double startedAt, float seconds)
    {
        if (castRoutine == null || seconds <= 0f) return;
        windupTold = true;
        toldStartedAt = startedAt;
        toldSeconds = seconds;
    }

    // The ONE cast-SETUP choke point (paired with EndCast): begin BOTH wind-up visuals together.
    //   • OnCastStart aura — on the CASTER (body charge-up glow), the global cue authored in the Vfx Cue Composer.
    //     Announced by the server to every screen, the caster's own included; a player's machine announces nothing.
    //   • Cast circle — on the GROUND at the impact point (lastCtx.target), the per-skill effect from the Skill
    //     Composer. For a ground skill it lands exactly where the aim reticle was (one continuous ring); for a Self
    //     cast the target IS the caster's feet, so the offset is ~0. The caster is rooted here, so the offset holds.
    //     Drawn on the caster's own screen only; every other screen draws its own from the announced wind-up.
    // They are INDEPENDENT (two anchors, both optional) but share ONE lifecycle — held for the wind-up, stopped
    // together in EndCast. Never the release sequence.
    void BeginCastVisuals()
    {
        // The skill's weapon picks the look when it has its own (the Bow's water charge), else the shared one.
        castAura = VfxCue.Begin(VfxCueId.OnCastStart, transform, def != null ? def.weaponType : WeaponType.None);
        if (Mine && !string.IsNullOrEmpty(def.CastCircle))
            castCircle = Vfx.AttachGroundCircle(def.CastCircle, transform, lastCtx.target - transform.position,
                                                def.LargestAreaRadius, def.castCircleTint);
    }

    // The ONE cast-teardown choke point: stop the looping aura + circle, hide the bar, clear the casting guard.
    // Called on completion, on interrupt, and on OnDisable — so neither visual can outlive its wind-up.
    void EndCast()
    {
        VfxCue.End(ref castAura);
        // THE RING GOES THE INSTANT THE CAST RELEASES. A soft stop let its particles fade for seconds while the effect
        // kept following the caster, so the ring slid along under a player walking away after the release.
        if (castCircle != null) { castCircle.Stop(immediate: true); castCircle = null; }
        if (Mine && StanceController.Player != null) StanceController.Player.Pop(castPose);   // release the cast pose (complete/interrupt/OnDisable)
        if (!CasterGone) GameNetwork.AnnounceHeldOnCaster(Caster, false, castPose, 0f);
        var unlock = Status;
        if (unlock != null) unlock.Unlock();   // free the wind-up root; on completion PlayCycles re-locks for the aftercast beat
        if (!CasterGone && Caster is Character windingUp && ReferenceEquals(windingUp.WindupRunner, this))
            windingUp.WindupRunner = null;
        releaseAsked = false;
        windupTold = false;
        if (Mine) CastBar.Hide(transform);
        // AND EVERY MACHINE WATCHING THIS CASTER LETS GO OF ITS BAR TOO. This is the one place a wind-up ends —
        // finished, interrupted, or the runner switched off — so saying it here is saying it for all three.
        GameNetwork.AnnounceCastWindupEnded(Caster);
        castRoutine = null;
    }

    // Play the composed VFX once, then (if the def asks for a shower) replay the WHOLE cycle repeatCount times,
    // repeatInterval apart, at the SAME cast-time context — carrierScatter spreads the individual falls. Repetition
    // of the whole effect is owned HERE (gameplay), not the Vfx Composer (which stays WYSIWYG: one cycle = one fall).
    // Which units this cast's strikes have struck, for its yellow totals: emptied at every cast, so a cast's number
    // starts from zero. The same for one landing, emptied at every landing. Made once per skill and reused.
    readonly System.Collections.Generic.HashSet<ulong> castTotals = new System.Collections.Generic.HashSet<ulong>(),
                                                      landingTotals = new System.Collections.Generic.HashSet<ulong>();
    // This skill's place in the skill list, which names its totals (StrikeTotals.CurrentSkill). Looked up once.
    int skillNumber = -1;
    int SkillNumber => skillNumber >= 0 ? skillNumber : (skillNumber = SkillCatalog.NumberOf(def));

    void PlayCycles(float releaseScale = 1f)
    {
        castReleaseScale = releaseScale;
        StrikeTotals.Forget(castTotals, SkillNumber);
        // THE RELEASE: the skill is actually going off, so this is where its mana is taken. Only a press ever
        // left a cost here, so a run started by one of the skill's own moments still fires free.
        if (pendingMana > 0 && !CasterGone)
        {
            Caster.SpendMana(pendingMana);
            pendingMana = 0;
        }
        consumedThisCast = false;   // this cast hasn't paid its state cost yet — it pays once, after its effects compute
        castGivenUp = -1;           // nor its all-in costs — paid by the first tick that carries one
        castStacks = StateGate.StackCount(firedBy, CasterStates);   // the pile this cast is about to cash, read BEFORE spending it
        castCarried = StateGate.CarriedTotal(firedBy, CasterStates);   // same snapshot, same moment — the payoff picks which one counts
        bool pressed = firedBy != null && firedBy.trigger == SkillTrigger.OnCast;
        if (pressed) castChain = 0UL;   // a player-pressed cast starts a FRESH chain (nothing caused it)

        // THE CAST'S EFFICIENCY, rolled ONCE here by the machine that owns the world — every tick and every repeat of
        // this cast reads the same answer. Only a trademark skill can roll, whatever weapon is in hand (SkillEfficiency).
        castEfficient = !Watching && !CasterGone
                     && SkillEfficiency.Procs(Trademarks.Qualifies(def), CombatRng.Value01());

        // The skill call, the moment the skill actually fires — here, at the one place every way of firing (a press,
        // one of the skill's own automatic triggers) passes through, so no entry point can forget it. The server
        // chooses the words (SkillShout) and says them to everybody watching the caster, the caster included. Said
        // from here rather than beside the effect, because a repeating skill announces its effect once per fall and
        // would have called its own name five times over.
        GameNetwork.AnnounceSkillCall(Caster, def, castEfficient);

        // The cast finished UNINTERRUPTED (an interrupt runs EndCast + bails before here) — announce it so any passive
        // keyed to this skill via OnSkillCastComplete<skill> fires.
        //
        // "That skill finished casting" means THE PLAYER CAST IT. A run of this same skill started by one of its own
        // non-OnCast triggers does NOT announce, because the granter of the charge it just spent is usually keyed to
        // this very skill: Lock Blades grants charges when Lunge completes, an auto-fired Lunge would re-announce, and
        // the pile would refill itself forever. Keying off the trigger that STARTED this run — not off whether the
        // skill has a pressed form — makes that impossible for every skill, not just this pair.
        if (pressed)
        {
            PassiveEvents.Fire(Caster, SkillTrigger.OnSkillCastComplete, def.id);
        }

        // The RELEASE moment: any cast wind-up (and its Casting pose) has already finished, so the release cue plays
        // now — casting first, release second. Instant skills reach here immediately.
        float releaseDur = 0f;
        if (def.hasReleaseStance && Mine && StanceController.Player != null)
        {
            // THE RELEASE IS ONE SWING LONG — the same beat a creature's release is given just below, so a person and a
            // creature time a skill by one rule. Attack speed shortens it (and the root with it); movement speed,
            // slows, shoes and which gear the legs are in have no say, because the clip is FIT to the window rather
            // than paced by the walking gait it used to follow (which once held a caster still for two minutes).
            // A WEAPON'S SKILL ANIMATION is the exception: it plays at its own length — the rest of it after a cast, all
            // of it when instant — and roots the caster for exactly that.
            // Its stance's speed source decides how fast (Attack Speed = sped up the way a swing is).
            releaseDur = UsesWeaponSkillAnimation
                ? StanceController.Player.PlayActionFor(ReleaseStance)
                : StanceController.Player.PlayActionFor(ReleaseStance, Caster.SwingTime, 1f, fitToWindow: true);
            // RO-style aftercast: fully root the caster for the (attack-speed-scaled) release animation's length.
            // Stacks on top of the skill's own cooldown; a skill with no release stance roots for nothing.
            var aftercast = Status;
            if (aftercast != null) aftercast.LockActions(releaseDur);
        }
        // A CREATURE'S RELEASE: the same stance, told to every screen watching it. Its beat is its swing — the length
        // every action a creature takes is sized to — because the machine deciding it has no animation to measure.
        if (def.hasReleaseStance && !Mine && !Watching && !CasterGone)
            GameNetwork.AnnounceActionOnCaster(Caster, ReleaseStance, UsesWeaponSkillAnimation ? 0f : Caster.SwingTime);

        // A projectile aims a DIRECTION, not a landing spot: the shot flies a FIXED travel distance (the reused
        // castRange = "travel range"), NOT to the cursor. Project the aim onto that range so the Carrier flies the
        // full distance and its spread-ticks hit along the whole path. Fan copies inherit this (PlayFan spreads at the
        // projected radius); repeat cycles inherit it via lastCtx.
        if (def.IsProjectileAim) lastCtx.target = ProjectToTravelRange(lastCtx.target);

        // The sequence plays ONCE. A tick that fans launches several copies of ITS OWN carrier step (see the fan
        // resolver above) — replaying the whole sequence per copy would have fanned every other step along with it.
        PlaySequence(ref lastCtx);

        // A charge-driven run has no fixed length — it goes until the pile is empty — so the count is only consulted
        // for the plain fixed-repeat case.
        // A run that grows with cast speed plays more beats the faster its caster casts, under its own ceiling.
        int count = def.repeatScaleWithCastSpeed && !CasterGone
            ? OrbMath.RepeatsWithCastSpeed(def.repeatCount, 1f - Caster.CastTimeMult, def.repeatMaxCount)
            : def.repeatCount;
        int extra = def.repeatCostsCharge || def.repeatWhilePileHeld ? int.MaxValue : Mathf.Max(0, count - 1);
        if (extra > 0 && isActiveAndEnabled)
        {
            HoldForBeat(BeatInterval());   // the opening beat; every later one re-takes the hold inside the loop
            runRoutine = StartCoroutine(PlayExtraCycles(extra, lastCtx));
        }
    }

    /// <summary>THE ONE PLACE A PLAY OF THIS CAST STARTS — its opening beat and each of its repeats alike.
    ///
    /// <para>The machine that owns the world picks this play's SEED, stamps it on, tells every machine watching the
    /// caster, and then plays. A machine that is only watching plays NOTHING here: it waits to be told, and plays
    /// from that. Where a scattered shot lands is where its damage is measured from, so a watcher rolling its own
    /// would draw a shower falling in places the server never touched.</para></summary>
    void PlaySequence(ref VfxContext ctx)
    {
        if (Watching) return;
        ctx.seed = CombatRng.NewSeed();
        // A PROJECTILE IS SHOWN FLYING ITS FULL RANGE: the server measures its hits along the projected path, so every
        // screen is told that same far end, never the spot that was clicked.
        // …and a skill aimed at a unit with no spot of its own is shown at that unit, so a screen that cannot find the
        // unit still draws the effect where it landed, never on the caster.
        Vector3 shown = def.IsProjectileAim || (ctx.targetUnit != null && !def.NeedsAimPoint) ? ctx.target : ctx.effectPoint;
        GameNetwork.AnnounceCastPlayed(Caster, def, ctx.targetUnit, shown, ctx.seed);
        VfxDirector.Play(def.vfx, ctx, onTick);
    }

    // Push the aim point out to the skill's travel range (reused castRange) along the aim direction — a projectile
    // flies the full distance regardless of where the cursor sits. Falls back to the caster's facing if the cursor is
    // right on top of the caster, and to the raw aim distance if no range is set.
    Vector3 ProjectToTravelRange(Vector3 aim)
    {
        Vector3 me = transform.position;
        Vector3 dir = aim - me; dir.y = 0f;
        float d = dir.magnitude;
        Vector3 nrm = d < 0.05f ? new Vector3(transform.forward.x, 0f, transform.forward.z).normalized : dir / d;
        float range = def.castRange > 0f ? def.castRange : d;
        return new Vector3(me.x + nrm.x * range, aim.y, me.z + nrm.z * range);
    }

    // Reused so filtering a repeat's payload costs no garbage on the combat path — a beat can fire several times a
    // second for the length of a run.
    readonly System.Collections.Generic.List<SkillEffect> repeatEffects = new System.Collections.Generic.List<SkillEffect>(8);

    /// <summary>The binding's effects MINUS the ones that hand out a stacking state. Those establish what the run
    /// spends, so they belong to the cast's opening play; re-running them each beat would refill the pile the beats
    /// are draining and the run could never finish. Everything else — the strike, the relocation, one-shot states —
    /// runs on every beat exactly as authored.</summary>
    System.Collections.Generic.List<SkillEffect> WithoutStateGrants(System.Collections.Generic.List<SkillEffect> effects)
    {
        repeatEffects.Clear();
        if (effects == null) return repeatEffects;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e == null) continue;
            if (e.stacking && SkillEffect.TryStateFor(e, out _)) continue;
            repeatEffects.Add(e);
        }
        return repeatEffects;
    }

    // The beat, resolved PER BEAT so a speed buff landing mid-run quickens the rest of it. Following swing speed means
    // the run keeps the caster's own rhythm at any attack speed; the beats-per-swing multiple divides that rhythm
    // (2 = twice as fast as you swing). Falls back to the authored seconds if the body hasn't spawned, so a missing
    // rig can't stall the run. One resolver, so the hold below covers exactly the beat the run is about to play.
    float BeatInterval()
    {
        float authored = def.repeatInterval > 0f ? def.repeatInterval
                       : (def.vfx != null && def.vfx.RestartInterval > 0f ? def.vfx.RestartInterval : 0.35f);
        if (def.repeatScaleWithAttackSpeed && !CasterGone)
        {
            float swing = Caster.SwingTime;
            if (swing > 0f) return swing / Mathf.Max(1, def.repeatBeatsPerSwing);
        }
        return authored;
    }

    // A run that HOLDS its caster re-takes the root at the start of every beat, sized to that beat. A run has no known
    // total length (a charge-driven one lasts as long as its pile), so a single lock up front is impossible; and
    // because a lock is only ever extended, never shortened, a hold that stops being re-taken simply lapses when the
    // run stops — however it stopped. Nothing to undo, and no way to leave the caster rooted after the last beat.
    void HoldForBeat(float seconds)
    {
        var hold = def.holdCasterForRun ? Status : null;
        if (hold != null) hold.LockActions(seconds);
    }

    System.Collections.IEnumerator PlayExtraCycles(int extra, VfxContext ctx)
    {
        float runStart = Time.time;
        for (int i = 0; i < extra; i++)
        {
            float interval = BeatInterval();
            // manual timer, not `new WaitForSeconds`: this coroutine starts per cast and the interval varies per skill
            ctx.isRepeat = true;   // every play from here on is a REPEAT, not the cast's opening play
            for (float t = 0f; t < interval; t += Time.deltaTime) yield return null;
            // Only a run that STEERS or PAYS needs a live caster to read; a plain fixed repeat (a meteor shower) keeps
            // its old behaviour of finishing on its own, so nothing existing changes.
            if ((def.repeatReaim || def.repeatCostsCharge || def.repeatWhilePileHeld)
                && (CasterGone || Caster.IsDead)) break;
            // A RUN THAT LASTS WHILE ITS PILE IS HELD ends the moment that pile is gone, or at its ceiling, whichever
            // comes first — so lengthening the pile lengthens the run, but never past the authored maximum.
            if (def.repeatWhilePileHeld && !RunPileStillHeld(runStart)) break;

            // RE-AIM walks the run from one target to the next: the search is centred on the CURRENT target, so each
            // hop's range is measured from where the last one landed, never from the caster or the original aim.
            // An empty search ENDS the run — with nothing left in reach there is nothing to walk to.
            if (def.repeatReaim)
            {
                Vector3 from = ctx.targetUnit != null ? ctx.targetUnit.position : ctx.target;
                var next = SkillEffectApplier.RandomOpponentNear(from, def.repeatReaimRange, Caster);
                if (next == null) break;
                ctx = BuildCtx(next, next.position);   // the ONE place an aim is assembled — no field can be missed
                ctx.isRepeat = true;                    // BuildCtx answers the aim, not which play this is
            }

            // A charge-driven run pays for EVERY beat, not once for the whole cast: re-arming the flag lets the
            // existing consume site (OnTick, after the effects have computed) charge this beat too. Out of charges
            // ends the run — the gate is what the run is spending, so an unpayable beat must not fire.
            if (def.repeatCostsCharge)
            {
                if (StateGate.StackCount(firedBy, CasterStates) <= 0) break;
                consumedThisCast = false;
            }

            // Restart the release animation ON the beat, fit to this beat's length, so every repeat shows the whole
            // animation instead of one play stretched across the run. Forced to fit because the release stance is
            // usually authored to a gait, which would otherwise ignore the window and drift out of step with the beat.
            // MINE ONLY, like the opening play a few lines up. Without that word this reached for the one stance
            // controller on the machine BY NAME — so a repeating skill run for somebody else, on a machine that also
            // has a player standing on it, played its animation on the wrong body.
            if (def.hasReleaseStance && def.releaseStanceReplayPerRepeat && Mine && StanceController.Player != null)
                StanceController.Player.PlayActionFor(ReleaseStance, interval, 1f, fitToWindow: true);
            else if (def.hasReleaseStance && def.releaseStanceReplayPerRepeat && !Mine && !CasterGone)
                GameNetwork.AnnounceActionOnCaster(Caster, ReleaseStance, interval);

            PlaySequence(ref ctx);
            HoldForBeat(interval);   // hold through THIS beat; the next one re-takes it, and the last one just lapses
        }
        // THE EXTRA BEATS a pile pays for, read ONCE now and spent whole: stacks gained during them add none, so a run
        // that refills its own payer (a volley landing basic attacks that feed it) still ends.
        int bonus = SpendBonusBeats();
        for (int k = 0; k < bonus; k++)
        {
            float interval = def.repeatBonusInterval;
            ctx.isRepeat = true;
            for (float t = 0f; t < interval; t += Time.deltaTime) yield return null;
            if (CasterGone || Caster.IsDead) break;
            PlaySequence(ref ctx);
            HoldForBeat(interval);
        }
        // However the run stopped — last charge spent, nothing left in reach, caster down — it is over here, so the
        // piles it set up are over too.
        EndRunPiles();
        // AND THE RUN ITSELF IS OVER: without this the runner reported a run in progress forever after its first one,
        // which turned away every orb press after a Staff run (orbs wait for your own run to end).
        runRoutine = null;
    }

    /// <summary>End every pile the cast's opening play granted on the caster. A charge-driven run has no length of its
    /// own — it lasts exactly as long as its pile lets it — so a stack still sitting there after the run stopped is a
    /// window nobody is paying for any more, and it would ride into the next fight. Mirrors
    /// <see cref="WithoutStateGrants"/>: the same stacking grants that the opening play alone hands out are the ones
    /// that end with it. A plain fixed repeat is untouched — its length is authored, not bought, so its states keep
    /// their own clocks.</summary>
    // Is the pile this run lasts for still held, and the run still under its ceiling?
    bool RunPileStillHeld(float runStart)
    {
        if (def.repeatMaxSeconds > 0f && Time.time - runStart >= def.repeatMaxSeconds) return false;
        var states = CasterStates;
        return def.repeatPileStateIdSet && states != null && states.StacksFrom(def.id, def.repeatPileStateId) > 0;
    }

    // How many extra beats the named pile pays for — read once and spent whole. 0 on a machine that only watches,
    // which plays nothing anyway.
    int SpendBonusBeats()
    {
        if (Watching || CasterGone || string.IsNullOrEmpty(def.repeatBonusSkillId) || !def.repeatBonusStateIdSet) return 0;
        var states = CasterStates;
        return states != null ? states.ConsumeFrom(def.repeatBonusSkillId, def.repeatBonusStateId, 0, all: true) : 0;
    }

    void EndRunPiles()
    {
        if (!(def.repeatCostsCharge || def.repeatWhilePileHeld) || Watching) return;
        var states = CasterStates;
        if (states == null || def.bindings == null) return;
        for (int b = 0; b < def.bindings.Count; b++)
        {
            var effects = def.bindings[b] != null ? def.bindings[b].effects : null;
            if (effects == null) continue;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null || !e.stacking) continue;
                // Only what THIS skill granted: the pair (granter, state) names one pile, so another skill's buff and
                // any debt an incoming hit created are left alone.
                if (SkillEffect.TryStateFor(e, out var id)) states.ConsumeFrom(def.id, id, 0, all: true);
            }
        }
    }


    bool Spend()
    {
        // EVERY "no" HERE SAYS WHY TOO, for the same reason CanStart's do: these three were silent, and a cast
        // stopped by one of them looked from every seat exactly like a cast that was never sent.
        if (CasterGone || Caster.IsDead) return Refused("the caster is gone or dead");
        if (Time.time < nextUse) return Refused("still on cooldown");
        if (SharedHeld) return Refused("every ultimate shares a cooldown, and it is still running");
        // The stack gate: a skill that requires a state doesn't run at all when unmet — checked BEFORE any cost is paid.
        // A press is always gated by the OnCast entry; the skill's other triggers have their own, separate gates.
        if (!StateGate.IsMet(def.CastEntry, CasterStates))
        {
            if (Mine) OverheadNotice.Warn("Not ready");
            return Refused("the pile this skill spends is not there yet");
        }
        // CHECKED here, PAID at the release (PayPendingMana): a wind-up that gets broken costs you the attempt and
        // the cooldown, never the mana — you never got the skill off.
        if (Caster.CurrentMP < def.manaCost)
        {
            if (Mine) OverheadNotice.Warn("No MP");
            return Refused("not enough mana — " + Caster.CurrentMP + " held, " + def.manaCost + " needed");
        }
        // A PLAYER'S MACHINE STARTS NO COOLDOWN AND BILLS NO MANA. Everything above only compared numbers the server
        // sent, to decide what to show; the cast itself goes to the server, which starts the cooldown and says when it
        // ends (GameNetwork.TellOwnerCooldownStarted → ResumeCooldown).
        if (Watching) return true;

        pendingMana = def.manaCost;
        // A GM TESTING WITH COOLDOWNS OFF (/nocooldown): the skill is ready again the moment it is cast.
        bool noCooldowns = Caster is Character gmTester && gmTester.NoCooldowns;
        lastDuration = noCooldowns ? 0f : def.cooldown;
        // BOTH, TOGETHER. The moment is the one that survives being saved, slept or moved to another server;
        // the float beside it is the copy the per-frame gate and the hotbar ring read, because a date is too
        // expensive to ask for at that rate. They are set here and nowhere else apart from ResumeCooldown.
        dueAtUtc = WorldClock.UtcNow.AddSeconds(lastDuration);
        nextUse = Time.time + lastDuration;
        GameNetwork.TellOwnerCooldownStarted(Caster, def.id, dueAtUtc);
        // AN ULTIMATE ALSO STARTS THE SHARED COOLDOWN: every ultimate this unit has waits on one moment from here.
        if (def.IsUltimate && Caster.Skills != null && !noCooldowns)
            GameNetwork.TellOwnerCooldownStarted(Caster, SharedCooldown.UltimateKey, Caster.Skills.StartSharedUltimate());
        // THE LAST STAFF COMBINATION cast, for Weathering to make ready again. The resetting skill never counts itself.
        if (def.IsOrbCombo && !def.ResetsLastCombination && Caster.Skills != null) Caster.Skills.LastCombinationId = def.id;
        return true;
    }

    // Is this an ultimate still held by the shared cooldown (SharedCooldown)?
    bool SharedHeld => def != null && def.IsUltimate && !CasterGone && Caster.Skills != null
                       && Caster.Skills.SharedUltimateRemaining > 0f;

    // The heart of the code/data split: each tick the sequence raises, resolve its binding and apply — through the
    // ONE shared combat path. Unbound ticks (pure-visual beats) are ignored. Skills DON'T level — power comes from
    // the ring (affinity), so there's no per-level extra to add here (extraMult/extraFlat pass 0).
    void OnTick(VfxTick tick)
    {
        var b = def.BindingFor(tick.tickId);
        if (b == null) return;

        // NOTHING HAPPENS HERE ON A MACHINE THAT IS ONLY WATCHING. The world lives on the server, so a skill on a
        // player's own screen is a PICTURE of a cast, not the cast itself: the effect plays, the pose plays, and the
        // server decides every single thing the skill does — its damage, its buffs, its holds, its charges, its
        // dashes — against the character it holds for that person.
        //
        // The gate is on the CASTER, not on each effect, which is the whole point: a skill used to send only the
        // damage half across and settle the other forty-five effects in the game here, on a copy nobody else can
        // see. That is why a brace held nothing and a buff protected nobody.
        if (Watching) return;

        // A REPEAT replays the whole skill body, which would otherwise re-apply the very states the run is spending —
        // the pile refills faster than the beats drain it and the run never ends. So the states a cast SETS UP are
        // granted by its opening play only; a repeat runs everything else (the strike, the hop) unchanged.
        var effects = tick.isRepeat ? WithoutStateGrants(b.effects) : b.effects;

        // Map the consolidated per-tick targeting onto the shared burst params (scope/targets/radius). The applier
        // already routes each effect kind to the right sink per WHO is gathered, so nothing downstream changes.
        SkillScope scope; SkillTargets targets; float radius = b.radius;
        Vector3 burstPoint = tick.point;
        switch (b.targetMode)
        {
            case TargetMode.Self:
                scope = SkillScope.Area; targets = SkillTargets.Player; break;
            case TargetMode.SingleTarget:
                scope = SkillScope.Single; targets = b.affects; break;
            default: // Ground
                scope = SkillScope.Area; targets = b.affects;
                // AROUND THE CASTER: the splash is centred on where the caster stands NOW, whatever the tick's anchor.
                if (b.groundDelivery == GroundDelivery.AroundCaster && !CasterGone && Caster.Body != null)
                    burstPoint = Caster.Body.position;
                // Point = the caster acts AT the aimed spot (Dash goes there; an accompanying Heal lands on the caster).
                if (b.groundDelivery == GroundDelivery.Point) targets = SkillTargets.Player;
                // Projectile = the flying VFX step (authored in the VFX composer) raises its SPREAD TICKS along its
                // path; each tick lands here as an AoE gather at the tick's point — hit detection is DERIVED from the
                // ticks, not a hard-coded scan. radius = the projectile's hit width.
                break;
        }
        // Stamp this cast's causal chain for the whole burst, so any trigger the damage raises (OnDamageDealt / OnKill fire
        // synchronously inside the applier) can see which passives are already upstream and refuse to re-fire them.
        if (castGivenUp < 0 && SkillEffectApplier.HasAllInCost(effects))
            castGivenUp = SkillEffectApplier.PayAllInCosts(effects, Caster);
        var castVars = new StackVars(castStacks, castCarried);
        if (castGivenUp > 0) castVars = castVars.WithGivenUp(castGivenUp);

        var prevCause = PassiveEvents.Push(castChain, def.id);
        // EVERY BLOW THIS TICK LANDS TRAVELS AS ONE MESSAGE (BlowBatch) — a rain over a pack, a strike's hits.
        var batch = BlowBatch.Begin();
        try
        {
            // The cast hands DOWN what the pile measures and nothing more: which effects that pile pays, and how, is
            // authored per NUMBER, so cashing stacks composes with the damage effect
            // instead of being a second damage channel bolted onto the skill.
            // WHICH YELLOW TOTAL these strikes add into: the whole cast's (a new cast starts from zero), or this beat's
            // alone when the skill totals per beat — or, for a skill that does not total its repeats, this landing's
            // alone, so a multi-hit's total is that one strike's hits. Before, such a strike named no total, and a skill
            // pressed again inside a second (Silver Lining, no cooldown) added every cast into one climbing number.
            bool wholeCast = def.showRepeatsTotalDamage && !def.repeatTotalPerBeat;
            if (!wholeCast) StrikeTotals.Forget(landingTotals, SkillNumber);
            using var beatTotals = new StrikeTotals.BeatScope(wholeCast ? castTotals : landingTotals, SkillNumber);
            SkillEffectApplier.ApplyBurst(burstPoint, radius, scope, targets, effects,
                                          // The aim rides the TICK, not a field on this runner: one cast can have
                                          // several plays in flight, and a re-aiming repeat would otherwise resolve a
                                          // still-running beat against whatever the newest beat picked.
                                          tick.aimUnit,
                                          def.Symbol, def.displayName, def.id,   // def.id KEYS the pile: stacks belong to the skill that granted them
                                          aimPoint: tick.aimPoint,   // Dash/Teleport go where THIS play aimed, not to the tick's VFX anchor
                                          caster: Caster, // the character this run belongs to: the person at this keyboard, or the one the server holds for them
                                          // Both readings of the cashed pile, measured once at cast start, handed to
                                          // EVERY effect this burst runs — so a skill's damage and its lifesteal read
                                          // the SAME pile, and the first one can't spend it out from under the second.
                                          vars: castVars,
                                          efficient: castEfficient,
                                          release: castReleaseScale);

            // A BOUNCING TICK carries on from the unit it just landed on — its target effects jump to the nearest
            // enemies it has not hit, each worth less than the last (Flights). Every play starts its own chain: a repeat
            // is another strike. Only a Single Target tick bounces, so one tick can never start more than one chain.
            if (b.bounceJumps > 0 && b.targetMode == TargetMode.SingleTarget && tick.aimUnit != null)
            {
                var firstVictim = SkillEffectApplier.UnitStandingOn(tick.aimUnit);
                if (firstVictim != null && Sides.Hostile(Caster, firstVictim))
                    Flights.StartSkillBounce(Caster, firstVictim, b, effects, castVars, castChain,
                                             def.id, def.Symbol, def.displayName, castEfficient, castReleaseScale);
            }
        }
        finally { batch.Dispose(); PassiveEvents.Pop(prevCause); }

        // COMPUTE BEFORE CONSUME: the effects above read the LIVE store (Deathblow sees the whole Momentum pile while
        // rolling its damage); only now is the cost paid. Once per cast, not per tick.
        if (!consumedThisCast)
        {
            consumedThisCast = true;
            StateGate.Consume(firedBy, CasterStates);   // the entry that STARTED this run pays — a press and an auto-fire cost different things
        }
    }

    // ---- passive firing -----------------------------------------------------

    /// <summary>Fire this skill through one of its AUTOMATIC trigger entries — called by <see cref="PassiveEvents"/>
    /// when <paramref name="entry"/>'s moment happens and its gate is met. The skill body is identical to the pressed
    /// one (same VFX, ticks, effects); only the way it STARTED differs, which is why the entry is carried in rather
    /// than re-derived. So it runs the same cast path (<see cref="PlayCycles"/> → VFX → ticks → applier), with the aim
    /// resolved from the trigger context instead of a reticle:
    /// <list type="bullet">
    /// <item>Single-target skill (<see cref="CastReticle.Unit"/>) → lands on the triggering <paramref name="victim"/>
    /// entity (skipped if there's none this moment).</item>
    /// <item>Ground / AoE / projectile skill (<see cref="CastReticle.Ground"/>) → casts AT the victim's location (or the
    /// caster's spot if the moment has no victim, e.g. OnLowHP).</item>
    /// <item>SELF skill (<see cref="CastReticle.Instant"/>) → ALWAYS casts on the caster; the victim is ignored (a self
    /// buff must never be flung onto the mob).</item>
    /// </list>
    /// No mana / cooldown / wind-up — the trigger IS the gesture. It's a real cast, so its cost/cadence knobs stay ignored.
    /// <para>Returns whether it actually ran, so a caller that paid for it (an item) can keep the payment when it
    /// did not.</para></summary>
    public bool FireAsPassive(SkillTriggerEntry entry, Transform victim, ulong chain = 0UL)
    {
        if (def == null || def.vfx == null || entry == null || CasterGone || Caster.IsDead) return false;
        firedBy = entry;        // this run's gate, its stack cost, and "was this a player cast" all read from here
        castChain = chain;      // this passive is in the chain; everything its effects cause carries that forward

        // Same two stages as a manual cast, answered by the trigger instead of by clicks: the entity is the victim,
        // the point is wherever the moment happened (the caster's own spot when the moment had no victim).
        Transform unit = null;
        if (def.NeedsUnitPick)
        {
            if (victim == null) return false;         // single-target — needs an entity to land on
            unit = victim;
        }
        Vector3 point = def.NeedsAimPoint && victim != null ? victim.position : transform.position;

        lastCtx = BuildCtx(unit, point);
        if (body != null) body.FaceWorldPoint(lastCtx.target);   // face the auto-target, like a manual cast
        PlayCycles();                                 // the full active path: VFX + ticks + effects
        return true;
    }

    // ---- the Staff's Release -------------------------------------------------------
    // The Release plays nothing of its own: it presses whichever combination the caster's three orbs make, and the bar
    // shows that combination's icon, cost and cooldown. Which one is read again only when the caster's states change.
    int releaseSeen = -1;
    SkillDef releaseCombo;

    SkillDef ReleaseCombo
    {
        get
        {
            var st = CasterStates;
            if (st == null) return null;
            if (st.ChangeCount != releaseSeen) { releaseSeen = st.ChangeCount; releaseCombo = OrbCombos.ComboFor(Caster); }
            return releaseCombo;
        }
    }

    SkillRunner ReleaseRunner
    {
        get
        {
            var c = ReleaseCombo;
            return c != null && !CasterGone && Caster.Skills != null ? Caster.Skills.RunnerById(c.id) : null;
        }
    }

    /// <summary>What this button stands for right now: for the Release, the combination the orbs make (or the Release
    /// itself while fewer than three are held); for any other skill, itself.</summary>
    public SkillDef ShownDef => def != null && def.isOrbRelease ? (ReleaseCombo ?? def) : def;

    // A refused Release says why, short, on the stacking notice; the press goes nowhere and costs nothing.
    bool PressRelease(Transform at)
    {
        var combo = ReleaseCombo;
        if (combo == null) { if (Mine) OverheadNotice.Warn("Hold three orbs"); return false; }
        if (!OrbCombos.Opened(Caster, combo)) { if (Mine) OverheadNotice.Warn("Not learned: " + combo.displayName); return false; }
        var runner = ReleaseRunner;
        if (runner == null) { if (Mine) OverheadNotice.Warn("Not learned: " + combo.displayName); return false; }
        return at != null ? runner.ActivateAt(at) : runner.Activate();
    }

    // ---- IHotbarSkill -------------------------------------------------------
    public int SlotOrder => def != null ? def.slotOrder : 100;
    public string ShortLabel => ShownDef != null ? ShownDef.HotbarLabel : SkillDef.DefaultShortLabel;
    public string KeyLabel => def != null ? def.keyLabel : "";
    public string Symbol => ShownDef != null ? ShownDef.Symbol : "";
    public Sprite Icon => SkillSymbol.IconFor(ShownDef);
    public Color SymbolColor => SkillSymbol.ColorFor(def);
    public int ManaCost => ShownDef != null ? ShownDef.manaCost : 0;
    public bool Affordable => !CasterGone && ShownDef != null && Caster.CurrentMP >= ShownDef.manaCost;
    // An ultimate shows whichever is longer: its own cooldown or the shared one — the one that actually holds it.
    float SharedRemaining => def != null && def.IsUltimate && !CasterGone && Caster.Skills != null
                             ? Caster.Skills.SharedUltimateRemaining : 0f;
    public float CooldownRemaining
    {
        get
        {
            if (def != null && def.isOrbRelease) { var r = ReleaseRunner; return r != null ? r.CooldownRemaining : 0f; }
            return Mathf.Max(Mathf.Max(0f, nextUse - Time.time), SharedRemaining);
        }
    }
    public float CooldownFraction01
    {
        get
        {
            if (def != null && def.isOrbRelease) { var r = ReleaseRunner; return r != null ? r.CooldownFraction01 : 0f; }
            float own = Mathf.Max(0f, nextUse - Time.time), shared = SharedRemaining;
            if (shared > own) return Mathf.Clamp01(shared / SharedCooldown.UltimateSeconds);
            return lastDuration <= 0.001f ? 0f : Mathf.Clamp01(own / lastDuration);
        }
    }
    public Color Tint => def != null ? def.tint : Color.white;
}
