using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The 2.5D player. WASD gives direct, camera-relative movement; the mouse keeps the Dota-style
/// orders. Right-click is the camera orbit (<see cref="CameraFollow3D"/>):
///   • W A S D             → move (camera-relative; overrides any active click order).
///   • Left-click ground   → move there (A* around props).
///   • Left-click a mob    → attack it (close in, then auto-swing).
///   • T                   → hand the fight to the auto-pilot AI (press again, or S, to take it back).
/// NOTE: the old A (arm attack-click → ATTACK-MOVE) and S (STOP) keyboard hotkeys are currently
/// commented out in <see cref="ReadInput"/> because WASD reclaimed those keys — the Stop() /
/// attack-arm functions are kept intact so they can be re-bound later.
/// Movement routes via <see cref="Pathfinder3D"/> + a CharacterController. This is the movement/combat
/// BRAIN only — presentation is the live-3D <see cref="Character3DVisual"/> that rides it (it reads this
/// controller's state and hides nothing here). Exposes <see cref="AttackArmed"/> and a static
/// <see cref="MonsterUnderCursor"/> for <see cref="GameCursor3D"/>.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public partial class Player3DController : MonoBehaviour
{
    // TRAVEL, GAIT and the gear fractions all come from the shared CharacterSpec, so the player, the townsfolk and the
    // shadow mobs cannot disagree about how fast a character travels or how quickly its legs churn. Tune them there.

    /// <summary>TRAVEL speed: base ground translation before unit scale. Moving this changes speed WITHOUT scaling the
    /// animation — <see cref="GaitSpeedMult"/> divides it back out so the legs keep their baked cadence.</summary>
    public float moveSpeed => CharacterSpec.Active.moveSpeed;
    /// <summary>GAIT speed: the baseline that scales movement AND animation together, so legs and footsteps churn
    /// with it. Stacks multiplicatively with live Slow/Haste.</summary>
    public float gaitSpeed => CharacterSpec.Active.gaitSpeed;
    /// <summary>The player's CURRENT run speed in world units/sec — the same hand-tuned baseline the walk uses, scaled
    /// by <see cref="UnitScale.ForPlayer"/>, the <see cref="gaitSpeed"/> baseline, and the live Slow/Haste move buff
    /// (but NOT the Shift-walk slowdown). The dodge roll (E) reads this so it's always exactly 2× the current run.</summary>
    public float CurrentRunSpeed => moveSpeed * UnitScale.ForPlayer * gaitSpeed * (status != null ? status.MoveMult : 1f);
    /// <summary>Run speed WITHOUT the movement gate (<see cref="StatusController.MoveMult"/> drops to 0 under an
    /// aftercast root and to &lt;1 under Slow). A skill glide captures its speed from THIS so its own
    /// release-animation lock can't zero it — the glide is a fixed A→B move, not gated locomotion.</summary>
    public float RawRunSpeed => moveSpeed * UnitScale.ForPlayer * gaitSpeed;
    /// <summary>Travel speed of the RUN gear — the default jog you move at with no modifier held. THE single owner
    /// of that composition (base × unit scale × gear tier × gait), so anything that must move "like the player"
    /// reads this instead of re-deriving the multiplier chain and drifting from it. A chasing 3D shadow mob runs
    /// at exactly this, because a shadow is a player-sized unit and must stride like one.</summary>
    public float RunGearSpeed => moveSpeed * UnitScale.ForPlayer * runSpeedMult * gaitSpeed;
    /// <summary>Travel speed of the WALK gear (Shift held) — the strolling pace. Same single-owner rule as
    /// <see cref="RunGearSpeed"/>; the ambient townsfolk and a wandering shadow both move at exactly this.</summary>
    public float WalkGearSpeed => moveSpeed * UnitScale.ForPlayer * walkSpeedMult * gaitSpeed;
    /// <summary>Speed fraction while holding Shift to walk — the shared walk gear.</summary>
    public float walkSpeedMult => CharacterSpec.Active.walkGearMult;
    /// <summary>True while moving with Shift held (walk, not run). Read by <see cref="Character3DVisual"/>'s stance brain to
    /// choose the Walk vs Run animation.</summary>
    public bool WalkingSlow { get; private set; }
    /// <summary>Live ratio of the player's ACTUAL move speed to its tuned baseline (1 = normal, &gt;1 = sped up by a
    /// buff, &lt;1 = slowed). The single knob that binds gait to movement speed: the footstep cadence divides by it
    /// and the stance visual multiplies the Walk/Run clip fps by it, so a speed buff makes the steps and the run/walk
    /// animation visibly faster (and a Slow ailment slower) without touching the hand-tuned baselines.</summary>
    public float GaitSpeedMult { get; private set; } = 1f;

    // ── Sprint (V) + stamina ──────────────────────────────────────────────────
    /// <summary>Run gear as a fraction of Sprint — both its travel speed AND its leg cadence. The shared run gear;
    /// Sprint (V) is the full-speed run on top of it.</summary>
    public float runSpeedMult => CharacterSpec.Active.runGearMult;
    [Tooltip("Stamina drained per second while V-sprinting.")]
    public float sprintDrainPerSec = 25f;
    [Tooltip("Stamina recharged per second while standing idle (fast).")]
    public float staminaRegenIdle = 30f;
    [Tooltip("Stamina recharged per second while walking (Shift) — normal.")]
    public float staminaRegenWalk = 18f;
    [Tooltip("Stamina recharged per second while running / attacking / anything that isn't idle-or-walk — slow.")]
    public float staminaRegenOther = 8f;
    [Tooltip("After draining to empty, sprint stays LOCKED until stamina climbs back to this fraction of max.")]
    [Range(0f, 1f)] public float sprintUnlockFrac = 0.2f;
    /// <summary>True while the fast Sprint gear is engaged (V toggled on, moving, stamina left). Read by the stance
    /// brain to drive the Sprint pose and by the speed calc for the full-speed gear.</summary>
    public bool Sprinting { get; private set; }
    bool sprintToggled;   // V desire (toggle); cleared when stamina empties
    bool sprintLocked;    // drained to 0 → blocked until stamina recovers to sprintUnlockFrac

    public float arriveDistance = 0.12f;
    [Tooltip("Clearance kept from props when routing.")]
    /// <summary>Routing footprint — READ from the shared CharacterSpec, the same width every townsperson and shadow
    /// mob is given. It used to be its own serialized field that merely happened to match.</summary>
    public float agentRadius => CharacterSpec.Active.routingRadius;
    [Tooltip("Downward acceleration (units/s²). Now ACCUMULATES into a vertical velocity while airborne so " +
             "walking off a ledge free-falls (accelerates) instead of descending at a flat rate; reset on landing.")]
    /// <summary>Fall rate — READ from the world's one owner, so the player and every NPC/mob fall identically.</summary>
    public float gravity => GroundElevation.Gravity;
    [Tooltip("Downward speed (units/s) the player must exceed before the falling pose plays — keeps small " +
             "steps/slopes from flickering the in-air animation.")]
    public float fallAnimSpeed = 1.5f;
    [Tooltip("During attack-move, auto-engage a mob within this range.")]
    public float acquireRange = 5f;

    // ── Collision capsule ─────────────────────────────────────────────────────────────────────────
    // The shape now lives on the shared CharacterSpec, because the player, the townsfolk and the shadow
    // mobs are the same body and must collide the same. It used to be hand-tuned constants HERE while both
    // NPC builders ran a copy-pasted formula off the body height — the same character with collision shapes
    // 2.5x apart. Still deliberately SHORT so the capsule's top clears overhead prop geometry (roof eaves,
    // tree leaves) while the bottom bumps + climbs ground-level geometry; that tuning moved, it didn't change.

    [Header("Target magnetism (soft-snap)")]
    [Tooltip("A mob whose on-screen sprite is within this many pixels of the cursor counts as hovered/" +
             "clickable, so you don't have to pixel-aim a small billboard. 0 = off (exact aim only).")]
    public float snapRadiusPixels = 72f;
    static float snapRadius = 72f;   // static mirror so the static MonsterUnderCursor can read it

    [Header("Footsteps")]
    public SfxSet footstepSfx;
    [Tooltip("Played instead of the normal step when walking through a rain puddle (WetGround must be active and " +
             "the feet over a visible puddle). Leave empty to always use the normal footstep.")]
    public SfxSet waterFootstepSfx;
    [Tooltip("One-shot splash played when a jump LANDS in a rain puddle (same puddle test as the water step). " +
             "Leave empty for a silent landing.")]
    public SfxSet waterJumpSfx;
    // No step INTERVAL fields anymore: the footstep sound fires on the real foot-plant FRAME via
    // FootstepTracker.PlayerStepped (frame-locked to the walk/run animation), not a per-second timer. See OnPlayerStep.

    [Header("Auto-pilot AI (T to toggle)")]
    [Tooltip("While the auto-pilot AI roams, it engages the nearest mob within this range and walks to it. " +
             "Larger = ranges farther from where you stand to find fights.")]
    public float roamRange = 14f;

    /// <summary>True after pressing A, until the next click (the cursor shows the crosshair).</summary>
    public bool AttackArmed { get; private set; }
    /// <summary>True while the player's AUTO-PILOT AI is fighting for them (drives the on-screen tag). The same
    /// name the creatures' own fighting goes by: an auto-pilot AI picks a target, closes, swings, and chooses which
    /// skill to cast — and it is one idea whether it is steering a creature or standing in for a person.</summary>
    public bool AutoActive => order == Order.AutoPilot;

    /// <summary>The mob the player is currently engaged with — the one wearing the target arrow — or null.
    /// Read by quick cast (<see cref="UnitTargeter"/>) so a pressed skill can fire at it without a reticle.</summary>
    public Health AttackTarget => attackTarget != null && !attackTarget.IsDead ? attackTarget : null;

    enum Order { Idle, Move, AttackMove, Attack, AutoPilot, MoveIntoRange }
    Order order = Order.Idle;
    Vector3 movePoint;
    Health attackTarget;
    bool resumeAttackMove;
    AutoPilotIndicator autoIndicator;

    CharacterController controller;
    Player3DAttacker attacker;
    StatusController status;   // stun/freeze (no act), slow (speed), silence/blind — read here + by the spells/attacker

    // Charge / gap-closer dash state (driven via BeginDash / BeginGlideTo — a reusable controller API for dash skills).
    bool dashing;
    Transform dashTarget;          // a moving target (charge into a mob); null when dashing to a fixed point
    Vector3 dashPoint;             // fixed world destination (Dash skill); used when dashToPoint
    bool dashToPoint;
    float dashSpeed, dashEndTime, dashArriveRange;
    // Dash-SKILL easing (BeginGlideTo only): shape the glide speed. dashTotalDist = the full A→B distance captured at
    // launch; dashEaseSeconds = how long each accelerate/decelerate ramp lasts (time-based → a fixed-feel window at
    // any distance, converted to a world-distance window via the dash speed).
    bool dashEaseIn, dashEaseOut;
    float dashTotalDist, dashEaseSeconds;
    System.Action dashOnArrive;
    Vector3? dashFaceAt;           // hold this facing for the whole glide (a hop that ends in a strike); null = face travel
    /// <summary>True while a Dash SKILL is gliding the player A→B (vs a charge/E-dash roll). The stance brain reads
    /// this to play NO animation — a skill glide is pure movement, not a roll or a run.</summary>
    public bool SkillGliding => dashing && dashToPoint;
    [Tooltip("Dash SKILL travel speed as a multiple of the player's CURRENT run speed — the visible fast glide to the " +
             "aimed point (Blink teleports; Dash rushes there). Scales with move-speed buffs/slows via CurrentRunSpeed.")]
    public float skillDashSpeedMult = 8f;
    [Tooltip("Dash SKILL safety cap (seconds) — the dash ends even if it can't reach the point (blocked by a wall).")]
    public float skillDashMaxTime = 1f;
    TargetIndicator targetMarker;
    Camera cam;
    Vector3 buildScale;   // authored localScale; UnitScale.ForPlayer is applied on top each frame
    Vector3 baseScale;
    readonly List<Vector2> path = new List<Vector2>();
    int wp; bool routing;
    float nextChaseRepath;
    // Held in the SHARED holder every character uses — a creature's brain holds the same one — so "point at this"
    // cannot mean two different things on two kinds of character. Starts pointed the way a fresh body faces.
    CharacterBrain.Facing facing = MakeFacing(Vector3.back);
    static CharacterBrain.Facing MakeFacing(Vector3 dir) { var f = new CharacterBrain.Facing(); f.Toward(dir); return f; }

    // Free-fall: vertical velocity that ACCUMULATES under gravity while airborne (so a ledge walk-off
    // accelerates downward with real air time), reset to a small stick value on landing. `falling` gates
    // the in-air (jump-descent) animation — true only once we're descending faster than fallAnimSpeed, so
    // ordinary steps/slopes don't flicker it. Driven by ApplyGravityAndMove on the normal-movement path.
    float vy;
    bool falling;
    /// <summary>True while the player is in an accelerating free-fall (read by the visual to show the
    /// jump clip's descent frames). Distinct from <see cref="IsAirborne"/> (the scripted hop arc).</summary>
    public bool IsFalling => falling;

    /// <summary>Last move/aim direction in WORLD space (XZ meaningful). Read by the minimap to orient the
    /// player arrow, and by the stance visual for facing. Fixed-north top-down: world +X = map right, world +Z = map up.</summary>
    public Vector3 WorldFacing => facing.World;

    /// <summary>Turn the player to look at a world point on the XZ plane (skills call this so the caster faces
    /// where it cast). A point on top of the player (self-cast) is a no-op — the planar direction is ~zero.</summary>
    public void FaceWorldPoint(Vector3 point)
    {
        Vector3 d = point - transform.position; d.y = 0f;
        facing.Toward(d);   // facing derived camera-relative in Update
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        ApplyCapsule();   // enforce the tuned (short) collision capsule so it clears overhead eaves/leaves
        attacker = GetComponent<Player3DAttacker>();
        status = Status.Of(gameObject);   // registers as StatusController.Player for the spells' silence check
        snapRadius = snapRadiusPixels;   // publish to the static cursor pick
        buildScale = transform.localScale;
        baseScale = buildScale;
        targetMarker = TargetIndicator.Create();
        targetMarker.gameObject.AddComponent<Billboard>();   // face the camera so the arrow isn't edge-on when orbiting
        // "AUTO-PILOT" screen tag, hidden until toggled
        autoIndicator = AutoPilotIndicator.Create();
        UnitCrowd.Register(gameObject);                      // pass through mobs (so we can't hop on top of them)

        // The active-skill pool is now DATA: every composed SkillDef (Skill Composer output, under
        // Resources/Skills) gets a SkillRunner on the player. The legacy hand-written Player3D* skill classes
        // were retired — one runner interprets any skill. Which skills are BOUND to the hotbar comes from the
        // equipped weapon (Item.grantedSkillIds) / the Grimoire, via HotbarBindings — not a class gate.
        EnsureComposedSkills();
        // Press-to-talk (F) with ambient townsfolk. Same idempotent self-attach pattern — every player gets it.
        if (GetComponent<PlayerInteractor>() == null) gameObject.AddComponent<PlayerInteractor>();
    }

    // The footstep sound rides the ONE central player-step signal (fired on the real foot-plant frame) instead of a
    // local timer, so the sound, the puddle ripple and the snow print all land on the same frame from one source.
    void OnEnable()  { FootstepTracker.PlayerStepped += OnPlayerStep; }
    void OnDisable() { FootstepTracker.PlayerStepped -= OnPlayerStep; }

    // Say which skills are this player's and where their components go. Runs on Awake so a player built before a
    // skill was composed still picks it up without a re-bake. Cold path (once per spawn).
    //
    // NOTHING IS BUILT HERE any more: a skill gets a position, and its component is made the first time the player
    // presses it or one of their passives actually fires. The avatar and the server's fighter now bind the SAME list
    // in the SAME order — the catalog's — so a passive's position, which is its identity to the no-self-feeding
    // brake, means the same thing on the machine deciding a fight and the machine picturing it.
    void EnsureComposedSkills()
    {
        var me = Character.Instance;
        if (me != null) me.BindSkills(gameObject, me.UsableSkills());
    }

    // THE CHARACTER MAY NOT EXIST YET when this body wakes — the two are separate objects and Unity does not promise
    // an order between them. Binding is what gives this player their skills at all now, so a miss above must not be
    // final: this asks once more, one step later, when the character is certainly there. Binding twice is harmless —
    // it re-files the same positions and adopts the same components.
    void Start()
    {
        var me = Character.Instance;
        if (me != null && !me.HasSkills) me.BindSkills(gameObject, me.UsableSkills());
    }

    /// <summary>Push the SHARED character capsule onto this player's CharacterController. Called in Awake so the
    /// tuned (short) capsule is enforced on every player — even one built before this — and re-applies across map
    /// loads. Tune it on the CharacterSpec asset and every character changes together.</summary>
    public void ApplyCapsule()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        CharacterSpec.Active.ApplyCapsule(controller);
    }

#if UNITY_EDITOR
    // Live-apply the shared capsule as you tune the spec — including in Play mode (Awake ran once and won't re-read
    // it). Editor-only; no runtime cost.
    // NO OnValidate HERE. It used to write the capsule onto the CharacterController whenever anything on this
    // component was typed, which is a saved change — and it fires on every script recompile too, so the scene
    // was being rewritten by nobody. ApplyDefaultCapsule is still what the player builders call, so a freshly
    // built player has the right shape.
#endif

    /// <summary>Write the shared character capsule onto a CharacterController — the one place the player builders
    /// call so a freshly-built player matches the runtime shape in edit mode too.</summary>
    public static void ApplyDefaultCapsule(CharacterController cc) => CharacterSpec.Active.ApplyCapsule(cc);

    void Update()
    {
        if (cam == null) cam = Camera.main;
        baseScale = buildScale * UnitScale.ForPlayer;   // live: track the global unit-size dial
        transform.localScale = baseScale;               // the 3D visual rides this root; keep it on the size dial

        // The CharacterController is momentarily disabled during teleports/respawns. Never call Move() while it's
        // off — otherwise a controller left disabled (bad spawn, duplicate rig) spams "Move called on inactive
        // controller" every frame. Normal play never hits this (it's re-enabled before the next Update).
        // activeInHierarchy guards the SetActive(false) case (scene swap), which doesn't clear .enabled.
        if (controller == null || !controller.enabled || !controller.gameObject.activeInHierarchy) return;

        // Clear the free-fall pose each frame; only the normal-movement path (ApplyGravityAndMove) re-asserts
        // it, so locked/dash/jump states below can't leave a stale in-air pose showing.
        falling = false;

        // Dead: no input, no orders — keep the corpse pinned to the ground (gravity) until the death screen
        // respawns us. The lying pose is played by the stance visual.
        if (Character.Instance != null && Character.Instance.IsDead)
        {
            if (order != Order.Idle) Stop();
            // dying hands the fight back
        if (autoIndicator != null) autoIndicator.Show(false);
            dashing = false;    // death cancels a charge in progress
            jumping = false; jumpPhase = JumpPhase.None; jumpRollQueued = false; eDashing = false;   // …and any hop/dash in progress
            SetDashInvuln(false);                // …so a dash cut short by death doesn't leave us invulnerable
            controller.Move(Vector3.down * gravity * Time.deltaTime);
            return;
        }

        // Rooted: hard-CC (stun / freeze) OR a skill's self-root (cast wind-up + aftercast) — no input, no orders, no
        // movement — gravity only — until it lifts. Cancels an in-flight swing and any charge dash so nothing fires.
        if (status != null && status.ActionLocked)
        {
            // A Dash SKILL rushes the caster during the very aftercast root the skill just set (PlayCycles locks, THEN
            // ticks the effect), so let a dash-to-point finish here — it IS the aftercast movement. A hard-CC
            // (stun/freeze) still cancels it, like any other action, via CannotAct.
            if (dashing && dashToPoint && !status.CannotAct) { StepDash(); return; }
            // Only a HARD CC (stun/freeze) kills a swing already in flight. A skill's own aftercast root must NOT:
            // the swing is animation-locked and moves nothing, and the cast (a passive gap-closer, a self-buff) roots
            // us for a beat BEFORE its effect even ticks — cancelling here silently ate every basic attack that a
            // skill fired on top of.
            if (status.CannotAct && attacker != null && attacker.Swinging) attacker.Cancel();
            dashing = false; dashOnArrive = null; dashTarget = null; dashToPoint = false;
            controller.Move(Vector3.down * gravity * Time.deltaTime);
            return;
        }

        // A charge (gap-closer) overrides normal control: rush the target, then the skill's on-arrive
        // callback strikes. Skips input/orders until it lands (or times out).
        if (dashing)
        {
            StepDash();   // footstep SFX/prints ride the animation via FootstepTracker — no explicit tick here
            return;
        }

        // Locked DURING an attack swing — no movement by ANY means (WASD / click / routed orders / jump / dash are
        // all ignored). The swing animation plays to completion and damage lands at its end. Gravity only; we keep
        // facing fixed so the swing doesn't pivot.
        if (attacker != null && attacker.Swinging)
        {
            controller.Move(Vector3.down * gravity * Time.deltaTime);
            return;
        }

        // Traversal abilities (Space = forward hop, E = dash burst). Handled before normal input so an
        // active hop/dash overrides movement, and a freshly-pressed one starts THIS frame (no 1-frame lag).
        if (TickTraversalAbilities()) return;

        ReadInput();

        // WASD: direct, camera-relative movement. When any direction is held it OVERRIDES the
        // click-to-move / attack / auto order (cancelling it once) so the two schemes don't fight.
        Vector3 wasd = ReadWasd();
        Vector3 move;
        if (PlayerInteractor.Talking)
        {
            // Frozen in a townsfolk conversation (F-interact): hold position (gravity still applies below) and let
            // PlayerInteractor own the facing toward the NPC. Any WASD press ends the chat via PlayerInteractor.
            move = Vector3.zero;
        }
        else if (wasd.sqrMagnitude > 0.001f)
        {
            if (order != Order.Idle) Stop();   // drop any routed move/attack/auto so WASD wins
            move = wasd;
        }
        else move = Tick();

        // FLINCHED: a hit that cost HP stops the feet for as long as the hurt reaction plays. MOVEMENT ONLY — the
        // swing, the cast and pressing a skill all still run, which is exactly what separates a flinch from a stun.
        // Zeroed here rather than by returning early, so the stance reads "not moving" and input keeps being served;
        // a routed order survives and simply resumes when the stagger lifts.
        if (status != null && status.MovementFlinched) move = Vector3.zero;

        bool moving = move.sqrMagnitude > 0.001f;
        if (moving) facing.Toward(move);
        // Sprint gear (V toggle, stamina-gated) decided FIRST so it wins over Shift-walk; also drains/refills stamina.
        UpdateSprintAndStamina(moving);
        WalkingSlow = moving && HeldShift() && !Sprinting;   // Shift = walk (unless sprinting); read by the stance visual
        // THREE gears: Sprint = full (old run), Run = runSpeedMult (jog), Walk = walkSpeedMult.
        float travelTier = Sprinting ? 1f : (WalkingSlow ? walkSpeedMult : runSpeedMult);
        float baseSpeed = moveSpeed * UnitScale.ForPlayer * travelTier;                           // TRAVEL baseline per gear
        float gait = gaitSpeed * (status != null ? status.MoveMult : 1f);                        // GAIT baseline × live Slow/Haste — animation-scaling
        float spd = baseSpeed * gait;
        // Animation cadence per gear: Sprint & Walk keep the full gait feel; Run jogs (legs churn at runSpeedMult so the
        // half-speed travel doesn't foot-skate). Buffs/Slows ride on top via `gait`. Drives Walk/Run fps + footsteps.
        float animTier = (Sprinting || WalkingSlow) ? 1f : runSpeedMult;
        GaitSpeedMult = gait * animTier;
        ApplyGravityAndMove(move * spd);   // accumulating free-fall (sets `falling` for the in-air pose)
        if (autoIndicator != null) autoIndicator.Show(AutoActive);   // show the "AUTO" tag only while roaming
    }

    // Sprint desire (V toggle) + the stamina it spends/regenerates. Runs once per frame for the single player — no
    // allocation. Sprint engages only while moving with stamina left and not locked; draining to 0 forces Run and
    // locks re-sprint until stamina recovers to sprintUnlockFrac. Recharge rate depends on the current gear:
    // idle = fast, walk = normal, everything else (run/attack/etc.) = slow.
    void UpdateSprintAndStamina(bool moving)
    {
        if (Hotkeys.Down(KeyCode.V)) sprintToggled = !sprintToggled;   // the ONE sprint input site

        var ch = Character.Instance;
        float stam = ch != null ? ch.Stamina : 0f;
        float max = ch != null ? ch.maxStamina : 0f;

        bool wasSprinting = Sprinting;
        Sprinting = sprintToggled && moving && !sprintLocked && stam > 0f;

        float dt = Time.deltaTime;
        if (Sprinting)
        {
            stam -= sprintDrainPerSec * dt;
            if (stam <= 0f) { stam = 0f; sprintLocked = true; sprintToggled = false; Sprinting = false; }
        }
        else
        {
            float rate = !moving ? staminaRegenIdle
                       : (moving && HeldShift()) ? staminaRegenWalk
                       : staminaRegenOther;
            stam += rate * dt;
        }
        if (ch != null) ch.SetStamina(stam);
        if (sprintLocked && max > 0f && stam >= max * sprintUnlockFrac) sprintLocked = false;

        // WHICH GEAR THIS BODY IS IN, said out loud the frame it changes and never in between. Speed alone cannot
        // tell a sprint from a hasted run — both are simply fast — so the machine that owns the legs is the only one
        // that knows, and without this every other screen showed a sprinting person jogging.
        if (Sprinting != wasSprinting) GameNetwork.ReportSprinting(Sprinting);

        // Fire the OnSprint cue once, the frame sprinting kicks on (not per-frame). On a machine that has joined
        // somebody else's world this does nothing and the world says it instead, so the puff reaches everybody
        // watching rather than only the person sprinting — see VfxCue.
        if (Sprinting && !wasSprinting) VfxCue.Fire(VfxCueId.OnSprint, transform);
    }

    // Move horizontally while applying ACCUMULATING gravity, so stepping off higher ground free-falls with
    // real air time (the body accelerates) instead of the old constant-rate descent that looked glued to the
    // floor. On the ground we hold a small downward stick so CharacterController.isGrounded stays reliable;
    // airborne we integrate vy and, once descending past fallAnimSpeed, flag `falling` for the in-air pose.
    // WAS THE MOVE REFUSED, and for how long. NOT a bare change gate: a character grinding along a wall moves a
    // little on some frames and nothing on others — measured, 20 frames out of 512 — so a plain "did it change"
    // test would flip-flop and print twice a second, which is the very firehose this log exists to end. So the
    // refusal has to PERSIST before it is worth saying, and it is not repeated more often than once every few
    // seconds. Same hysteresis rule the performance guide gives for every per-frame boundary check.
    float blockedSince = -1f;
    float lastBlockedSaid = -99f;
    const float BlockedForSeconds = 0.5f;
    const float BlockedRepeatSeconds = 5f;

    void ApplyGravityAndMove(Vector3 horizVel)
    {
        if (controller.isGrounded && vy < 0f) vy = -2f;        // stick to the floor (keeps isGrounded true on slopes)
        else vy -= gravity * Time.deltaTime;                   // in the air → accelerate downward (free-fall)

        // ASKED TO MOVE, AND WHETHER IT HAPPENED. The single most expensive silence in this game: the character
        // asks to walk, something eats the step, and NOTHING says so — so "the player cannot move" has twice been
        // investigated from scratch by hand-built probes across two sessions. The answer was always right here.
        var before = transform.position;
        controller.Move((horizVel + Vector3.up * vy) * Time.deltaTime);
        falling = !controller.isGrounded && vy < -fallAnimSpeed;

        if (horizVel.sqrMagnitude <= 0.0001f) { blockedSince = -1f; return; }   // not trying to move — nothing to say

        var moved = transform.position - before;
        moved.y = 0f;
        if (moved.sqrMagnitude > 0.0000001f) { blockedSince = -1f; return; }    // it moved — nothing to say

        if (blockedSince < 0f) blockedSince = Time.time;
        if (Time.time - blockedSince < BlockedForSeconds) return;               // a single stuck frame is normal
        if (Time.time - lastBlockedSaid < BlockedRepeatSeconds) return;         // already said so recently
        lastBlockedSaid = Time.time;

        if (GameLog.On(LogCat.Hit))
            // perf-ok: only reachable after half a second of continuously refused movement, and at most once every
            // five seconds after that — so the string and the scan below run on a stall, never on a moving frame.
            GameLog.Info(LogCat.Hit, string.Concat(
                "MOVEMENT REFUSED for ", (Time.time - blockedSince).ToString("0.#"), "s — asked to move ",
                (horizVel.magnitude * Time.deltaTime).ToString("0.####"), " per frame and moved 0. Nearest solid: ",
                NearestSolidDescription(), ". Touching: ", controller.collisionFlags.ToString()));   // perf-ok: stall-only — gated on 0.5s of refused movement, then at most once per 5s
    }

    // Only ever called the frame movement STOPS or RESUMES working — never per frame — so an allocating scan here
    // costs nothing in the hot path. Says what is actually in the way, which is the whole point of the line.
    string NearestSolidDescription()
    {
        var here = transform.position;
        // perf-ok: called only from the movement-stall report above, which is rate-limited to once per 5s.
        var hits = Physics.OverlapSphere(here + Vector3.up * 0.5f, 1.5f);   // perf-ok: only runs on a movement stall, once per 5s
        float best = float.MaxValue; string name = "nothing within 1.5 units";
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            if (c == null || c.isTrigger || c.transform.IsChildOf(transform)) continue;
            float d = (c.bounds.ClosestPoint(here) - here).magnitude;
            // perf-ok: same — this whole function only runs when movement has been stuck for half a second.
            if (d < best) { best = d; name = c.name + " at " + d.ToString("0.##"); }   // perf-ok: same, stall-only
        }
        return name;
    }

    // Play the footstep sound on the REAL foot-plant. FootstepTracker fires PlayerStepped the instant the walk/run
    // animation crosses an authored plant frame (frame-locked, so it tracks gait speed for free) — no per-second timer,
    // no machine-gun guard needed, and the sound lands exactly with the visible foot and its puddle ripple.
    void OnPlayerStep(Vector3 world, float size)
    {
        // Splash instead of the dry step when the foot lands over a visible rain puddle (the WetGround shader's own
        // puddle mask answers "are we on water?" — see WetGround.IsPuddleAt). Falls back to the normal set.
        bool onWater = waterFootstepSfx != null && !waterFootstepSfx.Empty
                       && WetGround.Instance != null && WetGround.Instance.IsPuddleAt(world);
        Sfx.Play(onWater ? waterFootstepSfx : footstepSfx);
    }

    // ---- input: Dota controls -------------------------------------------------
    void ReadInput()
    {
        if (cam == null) return;
        if (GroundTargeter.Active) return;   // aiming a placed skill — the click confirms the target, not a move
        if (UnitTargeter.Active) return;     // aiming a single-target skill — the click locks the enemy, not a move

        // --- A / S keyboard hotkeys DISABLED: WASD reclaimed these keys for movement. The Stop()
        //     and attack-arm functions are kept intact below — un-comment (and rebind off A/S) to
        //     restore them later.
        // if (KeyS()) { Stop(); return; }      // S = STOP (cancel move / attack-move / swing)
        // T hands the fight to the auto-pilot AI, and takes it back
        if (KeyT()) { ToggleAuto(); return; }
        // if (KeyA()) AttackArmed = true;      // A = arm an attack-click (next left-click attacks)
        if (AttackArmed && KeyEsc()) { AttackArmed = false; Hotkeys.MarkCancelHandled(); }

        // Left-click drives the character (right-click is the camera orbit). Attacking now REQUIRES A:
        //   • plain left-click            → MOVE (even onto a mob — it no longer auto-attacks),
        //   • A-armed + click on a mob    → ATTACK it,
        //   • A-armed + click on ground   → ATTACK-MOVE there.
        // Ignore clicks that land on UI (e.g. the HUD) so they don't also order a move.
        if (LeftDown() && !PointerOverUI())
        {
            bool armed = AttackArmed; AttackArmed = false;
            if (armed)
            {
                var mob = MonsterUnderCursor(cam);
                if (mob != null) BeginAttack(mob, false);
                else if (GroundPoint(cam, out var gp)) BeginAttackMove(gp);
            }
            else
            {
                // Plain click: a mob → target-lock + attack it (shows the target indicator); else a ground move
                // (which clears any lock via ClearCombat).
                var mob = MonsterUnderCursor(cam);
                if (mob != null) BeginAttack(mob, false);
                else if (GroundPoint(cam, out var gp)) BeginMove(gp);
            }
        }
    }

    // WASD → a camera-relative XZ move direction (normalized), or zero when nothing is held. Built on
    // CameraRig's compute-once-per-frame basis so "W" is always away-from-camera even as it orbits, and
    // diagonals aren't faster (normalized). Held-key reads, so movement continues while the key is down.
    Vector3 ReadWasd()
    {
        float x = 0f, z = 0f;
#if UNITY_ANDROID
        // The on-screen stick feeds the same camera-relative direction the keys do, so everything downstream —
        // the gears, the facing, the stance, the footsteps — is the desktop's code untouched. The one difference
        // is that a stick is analog: its LENGTH is kept, so a half-push walks at half speed, where four keys can
        // only ever say "full speed" and must be normalized.
        Vector2 stick = TouchControls.MoveAxis;
        if (stick.sqrMagnitude > 1e-4f)
        {
            Vector3 tdir = CameraRig.ForwardXZ * stick.y + CameraRig.RightXZ * stick.x; tdir.y = 0f;
            float len = Mathf.Min(stick.magnitude, 1f);
            return tdir.sqrMagnitude > 1e-4f ? tdir.normalized * len : Vector3.zero;
        }
#endif
        if (HeldW()) z += 1f;
        if (HeldS()) z -= 1f;
        if (HeldD()) x += 1f;
        if (HeldA()) x -= 1f;
        if (x == 0f && z == 0f) return Vector3.zero;
        Vector3 dir = CameraRig.ForwardXZ * z + CameraRig.RightXZ * x; dir.y = 0f;
        return dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;
    }

    // ---- per-order behavior (returns the horizontal step direction) -----------
    Vector3 Tick()
    {
        switch (order)
        {
            case Order.Move:
                if (!routing) { order = Order.Idle; return Vector3.zero; }
                return FollowRoute();

            case Order.AttackMove:
                var near = NearestMonsterWithin(acquireRange);
                if (near != null) { BeginAttack(near, true); return AttackStep(); }
                if (!routing) { order = Order.Idle; return Vector3.zero; }
                return FollowRoute();

            case Order.Attack:
                return AttackStep();

            case Order.AutoPilot:
                return AutoStep();

            case Order.MoveIntoRange:
                return CloseTheGapStep();

            default:
                return Vector3.zero;
        }
    }

    // The player's AUTO-PILOT AI: re-acquire the nearest mob within roamRange forever (never goes Idle, never
    // leaves the AutoPilot order), walking to it and swinging in range. Mirrors AttackStep but loops
    // back to acquisition instead of stopping when a target dies. Any manual order (move/attack) or S
    // exits by switching the order out from under it.
    Vector3 AutoStep()
    {
        // A taunt outranks acquisition: while the lock holds, the taunter IS the target and the auto-pilot can't drift
        // to a nearer mob. Same rule a taunted mob obeys (MonsterAI3D.Taunted) — one aggro concept, both sides.
        if (Taunted && attackTarget != tauntSource)
        {
            attackTarget = tauntSource;
            if (targetMarker != null) targetMarker.Follow(tauntSource.transform);
            routing = false; path.Clear();
        }
        if (attackTarget == null || attackTarget.IsDead)
        {
            attackTarget = NearestMonsterWithin(roamRange);
            if (targetMarker != null) targetMarker.Follow(attackTarget != null ? attackTarget.transform : null);
            routing = false; path.Clear();
            if (attackTarget == null) return Vector3.zero;   // nothing in range — stand and wait for a wanderer
        }
        return PursueAttackTarget();
    }

    // The shared body of every basic-attack order (Attack + AutoPilot): announce the attack intent, then either stand
    // and swing (in reach) or route toward the target. ONE body so both orders behave identically and a rule added here
    // can't be forgotten in the other. The announce runs BEFORE reach is judged — that ordering is the whole point of
    // OnBasicAttackStart: a passive gap-closer moves us first, and the same frame's reach check sees the new position.
    Vector3 PursueAttackTarget()
    {
        if (attacker != null) attacker.AnnounceAttackIntent(attackTarget);
        if (attacker != null && attacker.EngagedWith(attackTarget))
        {
            routing = false; path.Clear();
            attacker.TryAttack(attackTarget);    // only actually swings when strictly InRange + off cooldown
            FaceTarget();
            return Vector3.zero;                 // in range: stand and swing
        }
        if (!routing || Time.time >= nextChaseRepath) { StartRoute(attackTarget.transform.position); nextChaseRepath = Time.time + 0.3f; }
        return routing ? FollowRoute() : Vector3.zero;
    }

    // ---- walking into a skill's range ---------------------------------------------------------------------------
    //
    // WHY THIS EXISTS AT ALL. A basic attack has always walked you to whatever you told it to hit and swung the
    // moment you were close enough, re-measuring the real gap every frame so a target that moves is followed. A
    // skill did not: it measured the gap ONCE, at the click, and refused. This is that same walk, told a distance
    // instead of a weapon's reach.
    //
    // IT LIVES HERE, ON THE MACHINE OF THE PERSON WALKING, for the same reason the attack pursuit does: this is the
    // only machine that decides where they are. The server never moves anybody, and it is only told a position ten
    // times a second, so a version of this waiting there would fire up to a tenth of a second past the line.
    Transform gapTarget;
    Health gapTargetHealth;        // resolved ONCE when the wait begins — never looked up per frame
    float gapWanted;
    System.Action onGapClosed;

    /// <summary>Walk to <paramref name="target"/> and run <paramref name="onArrive"/> the first frame the flat
    /// distance between the two bodies is <paramref name="range"/> or less. Replaces whatever order was running.
    /// A target that dies or disappears drops the whole thing, and so does any new order the player gives.</summary>
    public void BeginMoveIntoRange(Transform target, float range, System.Action onArrive)
    {
        if (target == null || onArrive == null) return;
        ClearCombat();
        DropTheWait();
        order = Order.MoveIntoRange;
        gapTarget = target;
        gapTargetHealth = target.GetComponentInParent<Health>();
        gapWanted = Mathf.Max(0f, range);
        onGapClosed = onArrive;
        routing = false; path.Clear(); nextChaseRepath = 0f;
        if (targetMarker != null) targetMarker.Follow(target);
    }

    /// <summary>Forget a held skill without running it — the player gave another order, the target died, or they
    /// pressed the same skill again. Leaves the order alone: every caller either sets its own or is already ending.</summary>
    public void DropTheWait()
    {
        gapTarget = null; gapTargetHealth = null; onGapClosed = null; gapWanted = 0f;
    }

    // One step of the walk. The gap is measured against where the target is THIS frame, so a target that walks away
    // is followed and one that walks toward us ends the wait early — the whole point of not deciding at the click.
    Vector3 CloseTheGapStep()
    {
        if (gapTarget == null || (gapTargetHealth != null && gapTargetHealth.IsDead))
        {
            DropTheWait();
            order = Order.Idle; routing = false; path.Clear();
            if (targetMarker != null) targetMarker.Follow(null);
            return Vector3.zero;
        }

        Vector3 gap = gapTarget.position - transform.position; gap.y = 0f;
        if (gap.sqrMagnitude <= gapWanted * gapWanted)
        {
            var run = onGapClosed;
            // CLEARED BEFORE IT RUNS. The skill about to start may begin an order of its own — a dash, a glide, a new
            // target — and a wait still holding the reins would take that away from it on the very next frame.
            DropTheWait();
            order = Order.Idle; routing = false; path.Clear();
            if (targetMarker != null) targetMarker.Follow(null);
            facing.Toward(gap);
            run();
            return Vector3.zero;
        }

        if (!routing || Time.time >= nextChaseRepath) { StartRoute(gapTarget.position); nextChaseRepath = Time.time + 0.3f; }
        return routing ? FollowRoute() : Vector3.zero;
    }

    // T: hand the fight to the auto-pilot AI, or take it back. Pressing it again (or S, or any manual order) stops it.
    void ToggleAuto()
    {
        if (order == Order.AutoPilot) { Stop(); return; }
        ClearCombat();              // drop any in-flight swing / stale target / marker
        AttackArmed = false;
        routing = false; path.Clear();
        // AutoStep acquires a target next tick
        order = Order.AutoPilot;
    }

    Vector3 AttackStep()
    {
        if (attackTarget == null || attackTarget.IsDead)
        {
            attackTarget = null; routing = false; path.Clear();
            if (targetMarker != null) targetMarker.Follow(null);
            if (resumeAttackMove) { BeginAttackMove(movePoint); return Vector3.zero; }   // keep clearing the area
            order = Order.Idle;
            return Vector3.zero;
        }
        return PursueAttackTarget();
    }

    // ---- orders ---------------------------------------------------------------
    void BeginMove(Vector3 point)       { ClearCombat(); order = Order.Move;       movePoint = point; StartRoute(point); }
    void BeginAttackMove(Vector3 point) { ClearCombat(); order = Order.AttackMove; movePoint = point; StartRoute(point); }

    void BeginAttack(Health mob, bool resume)
    {
        if (attacker != null) attacker.Cancel();
        DropTheWait();   // same rule as ClearCombat: a new order calls off a held skill
        order = Order.Attack; attackTarget = mob; resumeAttackMove = resume;
        routing = false; path.Clear();
        if (targetMarker != null) targetMarker.Follow(mob != null ? mob.transform : null);
    }

    /// <summary>Cancel any move/attack order (used on map arrival by PersistentPlayer3D).</summary>
    public void Halt() => Stop();

    // ---- the same orders a click gives, for a player who is not using a mouse (AgentLink) -------------------------
    // Each one is exactly what ReadInput runs after resolving the cursor, so a click and one of these cannot mean two
    // different things.

    /// <summary>What a plain left-click on the ground does: walk there.</summary>
    public void OrderMove(Vector3 point) => BeginMove(point);

    /// <summary>What a left-click on a mob does: lock on and fight it.</summary>
    public void OrderAttack(Health mob) { if (mob != null && !mob.IsDead) BeginAttack(mob, false); }

    /// <summary>What T does, but told which way instead of flipping.</summary>
    public void OrderAutoPilot(bool on) { if (on != AutoActive) ToggleAuto(); }

    /// <summary>Name of the order running right now — Idle, Move, AttackMove, Attack, AutoPilot or MoveIntoRange.</summary>
    public string OrderName => order.ToString();

    // ---- aggro (the player's half of the shared Taunt verb) -----------------------------------------------------
    Health tauntSource;

    /// <summary>True while a taunt is forcing this player onto <see cref="tauntSource"/> — the mirror of
    /// <c>MonsterAI3D.Taunted</c>. A dead taunter releases the lock immediately.
    ///
    /// <para>The WINDOW is the state store's (<see cref="UnitStates.Taunted"/>), shared with the mob side and with the
    /// skill door; only WHO taunted us is the rig's own half.</para></summary>
    bool Taunted
    {
        get
        {
            if (tauntSource == null || tauntSource.IsDead) return false;
            var st = Character.Instance != null ? Character.Instance.States : null;
            return st != null && st.Taunted;
        }
    }

    /// <summary>Remember who taunted this player and engage them at once — the auto-pilot can't retarget away while the
    /// lock holds. The player's twin of <c>MonsterAI3D.LockOnTaunter</c>, so a taunt authored on a skill behaves the
    /// same whoever it lands on. The window itself was already stamped by <see cref="Character.ForceAggro"/>.</summary>
    public void Taunt(Health source, float seconds)
    {
        if (source == null || seconds <= 0f) return;
        tauntSource = source;
        BeginAttack(source, false);   // false = don't resume attack-move after the kill; the taunt decided this target
    }

    /// <summary>Shove this player away from <paramref name="worldFrom"/> — the twin of a mob's knockback, so a
    /// Knockback / Pull effect works on either. Routed through the dash glide (the one A→B move primitive the rig
    /// owns) so it collides with props instead of phasing through them.</summary>
    public void Shove(Vector3 worldFrom, float speed, float seconds, float stagger)
    {
        if (speed <= 0f || seconds <= 0f) return;
        Vector3 dir = transform.position - worldFrom; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
        dir.Normalize();
        // Distance = the same speed × time a mob shove covers, so one authored number reads identically on both sides.
        // BeginGlideTo speaks in MULTIPLES of run speed, so convert the world-units/second the effect authored.
        float mult = speed / Mathf.Max(0.01f, RawRunSpeed);
        BeginGlideTo(transform.position + dir * (speed * seconds), mult, false, true, Mathf.Max(0.05f, seconds));
    }

    /// <summary>Re-publish <see cref="snapRadiusPixels"/> to the static the cursor pick reads, so the
    /// dev-settings panel can tune target magnetism live (the field is only mirrored in Awake otherwise).</summary>
    public void ApplySnapRadius() => snapRadius = snapRadiusPixels;

    void OnDestroy()
    {
        if (autoIndicator != null) Destroy(autoIndicator.gameObject);   // its own canvas — clean it up
    }

    void Stop()
    {
        AttackArmed = false; order = Order.Idle;
        routing = false; path.Clear(); attackTarget = null; resumeAttackMove = false;
        DropTheWait();
        dashing = false; dashOnArrive = null; dashTarget = null; dashToPoint = false;   // a charge is cancelled by Stop / S
        if (attacker != null) attacker.Cancel();   // drop an in-flight swing's damage
        if (targetMarker != null) targetMarker.Follow(null);
    }

    public bool Dashing => dashing;

    /// <summary>Start a charge/gap-closer dash toward <paramref name="target"/> at <paramref name="speed"/>.
    /// Overrides the current order; on arrival (within <paramref name="arriveRange"/>) or after
    /// <paramref name="maxTime"/> it fires <paramref name="onArrive"/> (the skill's strike). A reusable
    /// gap-closer primitive for dash-type skills.</summary>
    public void BeginDash(Transform target, float speed, float maxTime, float arriveRange, System.Action onArrive)
    {
        if (target == null) return;
        ClearCombat();
        order = Order.Idle; routing = false; path.Clear();
        dashing = true; dashTarget = target; dashToPoint = false; dashSpeed = speed;
        dashEndTime = Time.time + maxTime; dashArriveRange = arriveRange; dashOnArrive = onArrive;
        dashEaseIn = dashEaseOut = false;   // easing is a Dash-skill (glide) knob only; a charge runs at constant speed
    }

    /// <summary>Dash SKILL (Blink's visible sibling): rush the player to a FIXED world <paramref name="dest"/> — the
    /// aimed, range-clamped cast point — at <paramref name="speedMult"/> × current run speed, arriving at the spot
    /// instead of blinking there. Travels via the CharacterController so walls block it (that's the difference from
    /// Teleport). <paramref name="speedMult"/> ≤ 0 falls back to the player's <c>skillDashSpeedMult</c> default.
    /// No on-arrive callback (pure relocation). Shares the charge dash's step/state so cancels can't be forgotten.
    /// <paramref name="easeIn"/> starts the glide slow and accelerates; <paramref name="easeOut"/> decelerates to a
    /// gentle stop at the destination (both on = slow-fast-slow). Off = constant full speed (the previous behaviour).</summary>
    public void BeginGlideTo(Vector3 dest, float speedMult = 0f, bool easeIn = false, bool easeOut = false,
                             float easeSeconds = 0.3f, Vector3? faceAt = null)
    {
        // Set ONCE here, not per frame: a hop that ends in a strike must arrive already looking at what it is about to
        // hit, and must not swivel again on landing. StepDash honours this instead of facing its travel direction.
        dashFaceAt = faceAt;
        // A glide is MOVEMENT, not a new order: it keeps the current order, its target, the target marker AND any
        // in-flight swing (whose damage is already committed and lands mid-glide), so combat simply resumes on arrival
        // instead of dropping to Idle. Only the ROUTE is dropped — the path was computed from where we no longer are —
        // and a repath is forced for the next frame. (A charge, BeginDash, still takes over the order outright.)
        routing = false; path.Clear(); nextChaseRepath = 0f;
        dashing = true; dashToPoint = true; dashTarget = null; dashPoint = dest; dashOnArrive = null;
        dashEaseIn = easeIn; dashEaseOut = easeOut; dashEaseSeconds = Mathf.Max(0.01f, easeSeconds);
        Vector3 flat = dest - transform.position; flat.y = 0f;
        dashTotalDist = Mathf.Max(flat.magnitude, 1e-4f);
        // Per-skill dash speed wins; skillDashSpeedMult is the fallback default when the effect authors none (≤ 0).
        float mult = speedMult > 0f ? speedMult : skillDashSpeedMult;
        // RawRunSpeed (not CurrentRunSpeed): a release-animation aftercast root sets MoveMult=0, which would zero the
        // glide's speed. The glide is a fixed A→B move, so it must ignore that gate and always travel.
        dashSpeed = Mathf.Max(0.01f, mult) * RawRunSpeed;
        // Safety cap (stops a wall-blocked dash). Easing slows the glide, so a fixed 1s cap would chop a legit long
        // eased dash short of arrival — size the cap to the trip (2× the un-eased time + pad is a safe upper bound on
        // the eased time) when easing is on; otherwise keep the plain skillDashMaxTime.
        float nominal = dashTotalDist / Mathf.Max(dashSpeed, 0.01f);
        float cap = (easeIn || easeOut) ? Mathf.Max(skillDashMaxTime, 2f * nominal + 0.4f) : skillDashMaxTime;
        dashEndTime = Time.time + Mathf.Max(0.05f, cap);
        // Small arrive snap so the ease-OUT curve can play to its slow tail before the dash ends. At 0.15 the dash
        // stopped while still at ~0.9 speed on a short trip (the whole 0.2→0.5 tail lived inside the snap = hard stop).
        dashArriveRange = 0.05f;
    }

    // Advance the charge one frame. Returns true while still dashing; on arrival/timeout it stops and
    // fires the on-arrive callback (the strike). Exactly one controller.Move per frame, never two.
    bool StepDash()
    {
        // Destination is either a moving target (charge into a mob) or a fixed point (Dash skill).
        bool haveDest = dashToPoint || dashTarget != null;
        bool arrived = !haveDest || Time.time >= dashEndTime;
        if (!arrived)
        {
            Vector3 destPos = dashToPoint ? dashPoint : dashTarget.position;
            Vector3 to = destPos - transform.position; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = to / Mathf.Max(dist, 1e-4f);
            // Face the point we were told to face, if any — otherwise the way we're travelling. Held every frame of the
            // trip so the landing frame can't leave us looking down the travel line instead of at the target.
            // dashToPoint gates it: only a skill GLIDE ever supplies a face point, and this same step also drives the
            // charge-into-a-mob dash. Without the gate a cancelled glide's stale point would steer the next charge.
            if (dashToPoint && dashFaceAt.HasValue)
            {
                Vector3 look = dashFaceAt.Value - transform.position; look.y = 0f;
                facing.Toward(look);
            }
            else facing.Toward(dir);
            // CLAMP THE STEP TO WHAT'S LEFT. One frame of dash travel is routinely LONGER than the whole trip
            // (8× run speed ≈ 0.4 units/frame vs a ~0.3-unit lunge to a mob's side), so an unclamped step flies past
            // the destination, the arrive test (a 0.05 radius) never sees it, and the glide ping-pongs across the spot
            // until the safety cap expires — the player visibly vibrates for up to a second. Clamped, a step that would
            // reach or pass the point lands exactly on it and ends the dash, at any speed / distance / frame rate.
            float step = DashStepSpeed(dist) * Time.deltaTime;
            if (step >= dist - dashArriveRange) { step = dist; arrived = true; }
            controller.Move(dir * step + Vector3.down * gravity * Time.deltaTime);
        }
        if (arrived)
        {
            dashing = false; dashToPoint = false; dashFaceAt = null;
            var cb = dashOnArrive; dashOnArrive = null; dashTarget = null;
            cb?.Invoke();
            return false;
        }
        return true;
    }

    // The glide's speed this frame, shaped by the Dash-skill ease flags. dist = remaining horizontal distance to the
    // destination. Each ramp spans dashEaseSeconds of travel (TIME-based → same feel at any distance): the window in
    // world units = dashEaseSeconds × dashSpeed, clamped to the trip so a dash shorter than the window just eases the
    // whole way. Ease-in accelerates over the first window, ease-out decelerates over the last; a floor keeps it moving
    // so easing can never stall short of arrival. No easing = constant dashSpeed.
    float DashStepSpeed(float dist)
    {
        if (!dashToPoint || (!dashEaseIn && !dashEaseOut)) return dashSpeed;
        const float floor = 0.2f;
        float d = Mathf.Clamp(dashEaseSeconds * dashSpeed, 1e-4f, dashTotalDist);   // ramp length = the ease-window's worth of travel
        float f = 1f;
        if (dashEaseIn)  f = Mathf.Min(f, Mathf.Lerp(floor, 1f, Mathf.SmoothStep(0f, 1f, (dashTotalDist - dist) / d)));
        if (dashEaseOut) f = Mathf.Min(f, Mathf.Lerp(floor, 1f, Mathf.SmoothStep(0f, 1f, dist / d)));
        return dashSpeed * f;
    }

    void ClearCombat()
    {
        if (attacker != null) attacker.Cancel();
        attackTarget = null; resumeAttackMove = false;
        // A NEW ORDER CALLS OFF A HELD SKILL. Every order that starts by clearing combat is the player saying to do
        // something else instead, and a skill still waiting to fire behind that would go off during it.
        DropTheWait();
        if (targetMarker != null) targetMarker.Follow(null);
    }

    void FaceTarget()
    {
        if (attackTarget == null) return;
        Vector3 d = attackTarget.transform.position - transform.position; d.y = 0f;
        facing.Toward(d);   // facing derived camera-relative in Update
    }

    // Nearest living mob within radius (planar XZ). Scans the MonsterAI3D registry rather than a physics
    // OverlapSphere — the old fixed 32-collider buffer overflowed with props/terrain at large roamRange in
    // a dense scene, returning colliders in arbitrary order and silently dropping mobs (the auto-pilot then
    // just stood still). The registry is unbounded, prop-free, and cheaper.
    Health NearestMonsterWithin(float radius)
    {
        Health best = null; float bestSqr = radius * radius;
        Vector3 me = transform.position;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;                                  // cached on the mob — no per-scan GetComponent
            if (h == null || h.IsDead) continue;
            Vector3 d = ai.transform.position - me; d.y = 0f;   // planar distance — ignore height
            float sqr = d.sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }
        return best;
    }

    // ---- routing (Pathfinder3D) -----------------------------------------------
    // Whether the last route search failed — so the line in StartRoute fires on the change, not per search.
    bool lastRouteFailed;

    void StartRoute(Vector3 goal)
    {
        var from = new Vector2(transform.position.x, transform.position.z);
        var to = new Vector2(goal.x, goal.z);
        // Fills `path` in place (allocation-free); player uses the full default expansion budget.
        if (!Pathfinder3D.FindPath(from, to, agentRadius, path))
        {
            // NO ROUTE. The fallback below walks straight at the goal regardless, which is right — but it means a
            // failed search and a good one look identical from outside, so a character shuffling into a wall reads
            // as working. Change-gated: routing is re-asked several times a second while chasing.
            if (!lastRouteFailed)
            {
                lastRouteFailed = true;
                // A fault, not chatter — so it prints whatever the category switches say.
                GameLog.Warn(LogCat.Hit, string.Concat("NO ROUTE to ", to.ToString("0.##"),
                                                       " — walking straight at it instead. ",
                                                       Pathfinder3D.LastOutcome));
            }
            path.Clear(); path.Add(to);
        }
        else if (lastRouteFailed)
        {
            lastRouteFailed = false;
            if (GameLog.On(LogCat.Hit)) GameLog.Info(LogCat.Hit, "routing working again");
        }
        wp = 0; routing = path.Count > 0;
    }

    Vector3 FollowRoute()
    {
        if (wp >= path.Count) { routing = false; return Vector3.zero; }
        Vector3 to = WaypointDelta();
        if (to.sqrMagnitude <= arriveDistance * arriveDistance)
        {
            wp++;
            if (wp >= path.Count) { routing = false; return Vector3.zero; }
            to = WaypointDelta();
        }
        return to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.zero;
    }

    Vector3 WaypointDelta()
    {
        Vector2 c = path[wp];
        Vector3 d = new Vector3(c.x, transform.position.y, c.y) - transform.position; d.y = 0f;
        return d;
    }

    // ---- raycast + input helpers (new Input System OR legacy) -----------------
    static int cursorPickFrame = -1;
    static Health cursorPick;

    /// <summary>The living mob under the cursor (camera raycast), or null. Static so GameCursor3D shares it.
    /// Frame-memoized: it's read up to 3× per frame (GameCursor3D, UnitStatusPanel, click/target handlers), so the
    /// raycast + snap scan runs ONCE and the rest of the frame reads the cached pick. All callers pass the one
    /// gameplay camera, so the memo doesn't key on <paramref name="cam"/>.</summary>
    public static Health MonsterUnderCursor(Camera cam)
    {
        if (Time.frameCount == cursorPickFrame) return cursorPick;
        cursorPickFrame = Time.frameCount;
        cursorPick = PickMonsterUnderCursor(cam);
        return cursorPick;
    }

    static Health PickMonsterUnderCursor(Camera cam)
    {
        if (cam == null) return null;
        // The cursor is over a HUD element (e.g. an open Inventory or Character window): don't
        // pick the mob "behind" it. This single guard makes the sword cursor (GameCursor3D), the hover
        // panel (UnitStatusPanel), and click-to-attack all ignore mobs occluded by the UI.
        if (PointerOverUI()) return null;
        Vector2 mp = MousePos();

        // 1) Exact aim: a direct collider hit always wins (you're pointing right at it).
        var ray = cam.ScreenPointToRay(mp);
        if (Physics.Raycast(ray, out var hit, 500f))
        {
            var h = hit.collider.GetComponentInParent<Health>();
            if (h != null && !h.IsDead && h.GetComponent<MonsterAI3D>() != null) return h;
        }

        // 2) Soft-snap (magnetism): if no exact hit, grab the nearest LIVING mob whose on-screen sprite
        //    is within snapRadius pixels of the cursor — so small billboards don't demand pixel-perfect aim.
        if (snapRadius <= 0f) return null;
        Health best = null; float bestSqr = snapRadius * snapRadius;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;                                  // cached on the mob — no per-scan GetComponent
            if (h == null || h.IsDead) continue;
            // Aim at the sprite's CENTRE (mid-body). The mob's transform.position is its FEET on the ground;
            // UnitAnchor owns the feet→centre offset (size-scaled), so the snap circle overlaps the body at
            // any mob scale instead of demanding pixel-aim on the centre.
            Vector3 sp = cam.WorldToScreenPoint(UnitAnchor.Center(ai.transform));
            if (sp.z <= 0f) continue;                               // behind the camera
            float sqr = ((Vector2)sp - mp).sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }
        return best;
    }

    static int unitPickFrame = -1;
    static Health unitPick;

    /// <summary>The living UNIT under the cursor — a creature, another person, anything carrying health — or null.
    ///
    /// <para>Deliberately NOT the same question as <see cref="MonsterUnderCursor"/>, and deliberately not the same
    /// answer. That one means "what can I attack", and it is what turns the cursor into a sword and what a click
    /// swings at; widening it would make pointing at a person look and behave like pointing at an enemy. This one
    /// means "what am I looking at", and nothing acts on it — it only decides what is described on screen.</para>
    ///
    /// <para>Frame-memoized on its own stamp, for the same reason the other is: several readouts ask it in one
    /// frame and the scan should run once.</para></summary>
    public static Health UnitUnderCursor(Camera cam)
    {
        if (Time.frameCount == unitPickFrame) return unitPick;
        unitPickFrame = Time.frameCount;
        unitPick = PickUnitUnderCursor(cam);
        return unitPick;
    }

    static Health PickUnitUnderCursor(Camera cam)
    {
        if (cam == null) return null;
        if (PointerOverUI()) return null;
        Vector2 mp = MousePos();

        // 1) Exact aim wins, whatever it is standing in — a creature, a person, a villager.
        var ray = cam.ScreenPointToRay(mp);
        if (Physics.Raycast(ray, out var hit, 500f))
        {
            var h = hit.collider.GetComponentInParent<Health>();
            if (h != null && !h.IsDead) return h;
        }

        // 2) Soft-snap, over the two registries that hold what is on screen: the creatures, and the bodies this
        //    machine has built for other people. Both are plain lists kept by arrival and departure, so this walks
        //    what is here without searching the scene.
        if (snapRadius <= 0f) return null;
        Health best = null;
        float bestSqr = snapRadius * snapRadius;

        var mobs = MonsterAI3D.Active;
        for (int i = 0; i < mobs.Count; i++)
        {
            var ai = mobs[i];
            if (ai == null) continue;
            var h = ai.Health;
            if (h == null || h.IsDead) continue;
            Vector3 sp = cam.WorldToScreenPoint(UnitAnchor.Center(ai.transform));
            if (sp.z <= 0f) continue;
            float sqr = ((Vector2)sp - mp).sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }

        var folk = NetPlayer.WatchedBodies;
        for (int i = 0; i < folk.Count; i++)
        {
            var h = folk[i];
            if (h == null || h.IsDead) continue;
            Vector3 sp = cam.WorldToScreenPoint(UnitAnchor.Center(h.transform));
            if (sp.z <= 0f) continue;
            float sqr = ((Vector2)sp - mp).sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }

        return best;
    }

    /// <summary>Public wrapper for the cursor→ground raycast — used by ground-targeted skills (e.g. Meteor).</summary>
    public static bool GroundUnderCursor(Camera cam, out Vector3 point) => GroundPoint(cam, out point);

    static int groundMask = -1;   // cached "Ground" layer mask (-1 = unresolved, 0 = no Ground layer in scene)

    static bool GroundPoint(Camera cam, out Vector3 point)
    {
        point = default;
        if (cam == null) return false;
        var ray = cam.ScreenPointToRay(MousePos());
        if (groundMask == -1) { int l = LayerMask.NameToLayer("Ground"); groundMask = l >= 0 ? (1 << l) : 0; }
        // Raycast the GROUND layer ONLY. Hitting every collider let the cursor point snap onto a mob's
        // floating billboard quad (e.g. a dead mob) — placing a ground-targeted skill high + offset while it
        // still looked grounded from the top-down camera, so its frozen AoE ticks never reached the player.
        if (groundMask != 0 && Physics.Raycast(ray, out var hit, 500f, groundMask, QueryTriggerInteraction.Ignore))
        { point = hit.point; return true; }
        if (Mathf.Abs(ray.direction.y) > 1e-4f)         // fallback: flat ground plane at y≈0
        {
            float t = -ray.origin.y / ray.direction.y;
            if (t > 0f) { point = ray.GetPoint(t); return true; }
        }
        return false;
    }

    // Pointer reads route through PointerInput — the ONE gate for the mouse, the way Hotkeys is the one gate for
    // keys. It is also where a finger answers instead on a phone, so nothing here has to know about touch.
    static Vector2 MousePos() => PointerInput.Position;

    static bool LeftDown() => PointerInput.PrimaryDown;

    /// <summary>True when the cursor is over a UI element (HUD), so a click shouldn't also move the player.
    /// Delegates to <see cref="UiBlock"/> so uGUI windows AND IMGUI overlays (dev panels) both block.</summary>
    static bool PointerOverUI() => UiBlock.PointerOverUI();

    // All key reads route through Hotkeys — the ONE input choke point (typing guard + the new-or-legacy
    // backend dance live there, not per key here). Esc uses CancelDown: cancel must work while typing.
    static bool KeyA()   => Hotkeys.Down(KeyCode.A);
    static bool KeyT()   => Hotkeys.Down(KeyCode.T);
    static bool KeyS()   => Hotkeys.Down(KeyCode.S);
    static bool KeyEsc() => Hotkeys.CancelDown();

    // WASD movement uses HELD reads, not the edge reads above, so motion continues while the key is down.
    static bool HeldW() => Hotkeys.Held(KeyCode.W);
    static bool HeldA() => Hotkeys.Held(KeyCode.A);
    static bool HeldS() => Hotkeys.Held(KeyCode.S);
    static bool HeldD() => Hotkeys.Held(KeyCode.D);

    // Shift = walk (held). Either Shift key downshifts run → walk. (Typing-guarded now like the rest —
    // harmless, since Shift only modifies movement and the movement keys are already suppressed.)
    static bool HeldShift() => Hotkeys.Held(KeyCode.LeftShift) || Hotkeys.Held(KeyCode.RightShift);

    void OnDrawGizmosSelected()
    {
        if (routing && path.Count > 0)
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Vector3 prev = transform.position;
            for (int i = wp; i < path.Count; i++)
            {
                Vector3 p = new Vector3(path[i].x, transform.position.y, path[i].y);
                Gizmos.DrawLine(prev, p); Gizmos.DrawWireSphere(p, 0.1f); prev = p;
            }
        }
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, acquireRange);
    }
}
