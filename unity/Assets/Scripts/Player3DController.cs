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
///   • T                   → toggle roam auto-combat.
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

    [Header("Auto-combat (T to toggle)")]
    [Tooltip("While auto-combat roams, it engages the nearest mob within this range and walks to it. " +
             "Larger = ranges farther from where you stand to find fights.")]
    public float roamRange = 14f;

    /// <summary>True after pressing A, until the next click (the cursor shows the crosshair).</summary>
    public bool AttackArmed { get; private set; }
    /// <summary>True while roam auto-combat is running (drives the on-screen indicator).</summary>
    public bool AutoActive => order == Order.AutoCombat;

    /// <summary>The mob the player is currently engaged with — the one wearing the target arrow — or null.
    /// Read by quick cast (<see cref="UnitTargeter"/>) so a pressed skill can fire at it without a reticle.</summary>
    public Health AttackTarget => attackTarget != null && !attackTarget.IsDead ? attackTarget : null;

    enum Order { Idle, Move, AttackMove, Attack, AutoCombat }
    Order order = Order.Idle;
    Vector3 movePoint;
    Health attackTarget;
    bool resumeAttackMove;
    AutoCombatIndicator autoIndicator;

    // Click-to-pickup: a clicked item we walk to and collect on arrival; collecting then locks input briefly
    // (gravity only) while the PickUp gesture plays, so the player can't WASD out of the animation.
    WorldPickup pendingPickup;
    float pickupLockUntil;
    const float PickupLockSeconds = 0.6f;

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
    Vector3 worldFacing = Vector3.back;          // last move/aim world dir; facing derived camera-relative

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
    public Vector3 WorldFacing => worldFacing;

    /// <summary>Turn the player to look at a world point on the XZ plane (skills call this so the caster faces
    /// where it cast). A point on top of the player (self-cast) is a no-op — the planar direction is ~zero.</summary>
    public void FaceWorldPoint(Vector3 point)
    {
        Vector3 d = point - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 1e-4f) worldFacing = d;   // facing derived camera-relative in Update
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
        autoIndicator = AutoCombatIndicator.Create();        // "AUTO-COMBAT" screen tag, hidden until toggled
        UnitCrowd.Register(gameObject);                      // pass through mobs (so we can't hop on top of them)

        // The active-skill pool is now DATA: every composed SkillDef (Skill Composer output, under
        // Resources/Skills) gets a SkillRunner on the player. The legacy hand-written Player3D* skill classes
        // were retired — one runner interprets any skill. Which skills are BOUND to the hotbar comes from the
        // equipped weapon (Item.grantedSkillIds) / the Grimoire, via HotbarBindings — not a class gate.
        EnsureComposedSkills();
        // The combo executor rides on the player too (reads Character.Combos). Auto-attach so a player built before
        // combos existed picks it up with no re-bake — same idempotent pattern as the skills above.
        if (GetComponent<ComboRunner>() == null) gameObject.AddComponent<ComboRunner>();
        // Press-to-talk (F) with ambient townsfolk. Same idempotent self-attach pattern — every player gets it.
        if (GetComponent<PlayerInteractor>() == null) gameObject.AddComponent<PlayerInteractor>();
    }

    // The footstep sound rides the ONE central player-step signal (fired on the real foot-plant frame) instead of a
    // local timer, so the sound, the puddle ripple and the snow print all land on the same frame from one source.
    void OnEnable()  { FootstepTracker.PlayerStepped += OnPlayerStep; }
    void OnDisable() { FootstepTracker.PlayerStepped -= OnPlayerStep; }

    // Attach one SkillRunner per authored SkillDef (idempotent — skips defs already carried). Runs on Awake so
    // a player built before a skill was composed still picks it up without a re-bake. Cold path (once per spawn).
    void EnsureComposedSkills()
    {
        var have = new HashSet<SkillDef>();
        foreach (var r in GetComponents<SkillRunner>()) if (r.def != null) have.Add(r.def);

        bool added = false;
        var pool = SkillCatalog.All;
        for (int i = 0; i < pool.Count; i++)
        {
            var def = pool[i];
            if (def == null || have.Contains(def)) continue;
            var runner = gameObject.AddComponent<SkillRunner>();
            runner.def = def;
            added = true;
        }
        if (added) PlayerSkills.Refresh();   // re-resolve id → runner so the hotbar/menu see the new actives
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
    void OnValidate()
    {
        if (GetComponent<CharacterController>() != null) ApplyCapsule();
    }
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
            if (autoIndicator != null) autoIndicator.Show(false);   // dying cancels auto-combat
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

        // Click-to-pickup: collect a clicked item once we've walked into range, then lock input for a beat while
        // the PickUp gesture plays — gravity only, no WASD/clicks/orders.
        TryCollectPending();
        if (Time.time < pickupLockUntil)
        {
            controller.Move(Vector3.down * gravity * Time.deltaTime);
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

        bool moving = move.sqrMagnitude > 0.001f;
        if (moving) worldFacing = move;
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

        // Fire the OnSprint cue once, the frame sprinting kicks on (not per-frame).
        if (Sprinting && !wasSprinting) VfxCue.Fire(VfxCueId.OnSprint, transform);
    }

    // Move horizontally while applying ACCUMULATING gravity, so stepping off higher ground free-falls with
    // real air time (the body accelerates) instead of the old constant-rate descent that looked glued to the
    // floor. On the ground we hold a small downward stick so CharacterController.isGrounded stays reliable;
    // airborne we integrate vy and, once descending past fallAnimSpeed, flag `falling` for the in-air pose.
    void ApplyGravityAndMove(Vector3 horizVel)
    {
        if (controller.isGrounded && vy < 0f) vy = -2f;        // stick to the floor (keeps isGrounded true on slopes)
        else vy -= gravity * Time.deltaTime;                   // in the air → accelerate downward (free-fall)
        controller.Move((horizVel + Vector3.up * vy) * Time.deltaTime);
        falling = !controller.isGrounded && vy < -fallAnimSpeed;
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
        if (DevSettings.Blocking) return;    // cursor is over the dev-settings panel — don't leak clicks/keys into the world
        if (GroundTargeter.Active) return;   // aiming a placed skill — the click confirms the target, not a move
        if (UnitTargeter.Active) return;     // aiming a single-target skill — the click locks the enemy, not a move

        // --- A / S keyboard hotkeys DISABLED: WASD reclaimed these keys for movement. The Stop()
        //     and attack-arm functions are kept intact below — un-comment (and rebind off A/S) to
        //     restore them later.
        // if (KeyS()) { Stop(); return; }      // S = STOP (cancel move / attack-move / swing)
        if (KeyT()) { ToggleAuto(); return; }   // T toggles roam auto-combat
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
                // Plain click: a mob → target-lock + attack it (shows the target indicator); else a pickup
                // (walk to it + collect); else a ground move (which clears any lock via ClearCombat).
                var mob = MonsterUnderCursor(cam);
                if (mob != null) BeginAttack(mob, false);
                else
                {
                    var pick = PickupUnderCursor(cam);
                    if (pick != null) BeginPickup(pick);
                    else if (GroundPoint(cam, out var gp)) BeginMove(gp);
                }
            }
        }
    }

    // WASD → a camera-relative XZ move direction (normalized), or zero when nothing is held. Built on
    // CameraRig's compute-once-per-frame basis so "W" is always away-from-camera even as it orbits, and
    // diagonals aren't faster (normalized). Held-key reads, so movement continues while the key is down.
    Vector3 ReadWasd()
    {
        float x = 0f, z = 0f;
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

            case Order.AutoCombat:
                return AutoStep();

            default:
                return Vector3.zero;
        }
    }

    // Roam auto-combat: re-acquire the nearest mob within roamRange forever (never goes Idle, never
    // leaves the AutoCombat order), walking to it and swinging in range. Mirrors AttackStep but loops
    // back to acquisition instead of stopping when a target dies. Any manual order (move/attack) or S
    // exits by switching the order out from under it.
    Vector3 AutoStep()
    {
        // A taunt outranks acquisition: while the lock holds, the taunter IS the target and auto-combat can't drift
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

    // The shared body of every basic-attack order (Attack + AutoCombat): announce the attack intent, then either stand
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

    // T: flip roam auto-combat on/off. Pressing it again (or S, or any manual order) turns it off.
    void ToggleAuto()
    {
        if (order == Order.AutoCombat) { Stop(); return; }
        ClearCombat();              // drop any in-flight swing / stale target / marker
        AttackArmed = false;
        routing = false; path.Clear();
        order = Order.AutoCombat;   // AutoStep acquires a target next tick
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

    // Click-to-pickup: walk to the clicked item; TryCollectPending() collects it on arrival.
    void BeginPickup(WorldPickup p)
    {
        ClearCombat();   // clears any combat AND a prior pendingPickup
        pendingPickup = p;
        order = Order.Move; movePoint = p.transform.position; StartRoute(movePoint);
    }

    void TryCollectPending()
    {
        if (pendingPickup == null) return;               // Unity-null once destroyed
        Vector3 a = transform.position, b = pendingPickup.transform.position; a.y = 0f; b.y = 0f;
        // Loot reach == melee reach: you must be as close as you'd stand to swing (no looting from afar).
        // The pickup's own collectRadius stays the CLICK size; this gates the actual collect.
        float reach = attacker != null ? attacker.EffectiveRange : pendingPickup.collectRadius;
        if (Vector3.Distance(a, b) > reach) return;
        pendingPickup.Collect();                         // item/gold + fires WorldPickup.OnCollected (PickUp anim)
        pickupLockUntil = Time.time + PickupLockSeconds;
        pendingPickup = null;
        Stop();                                          // end routing; the lock holds us through the gesture
    }

    void BeginAttack(Health mob, bool resume)
    {
        if (attacker != null) attacker.Cancel();
        order = Order.Attack; attackTarget = mob; resumeAttackMove = resume;
        routing = false; path.Clear();
        if (targetMarker != null) targetMarker.Follow(mob != null ? mob.transform : null);
    }

    /// <summary>Cancel any move/attack order (used on map arrival by PersistentPlayer3D).</summary>
    public void Halt() => Stop();

    // ---- aggro (the player's half of the shared Taunt verb) -----------------------------------------------------
    Health tauntSource;
    float tauntUntil;

    /// <summary>True while a taunt is forcing this player onto <see cref="tauntSource"/> — the mirror of
    /// <c>MonsterAI3D.Taunted</c>. A dead taunter releases the lock immediately.</summary>
    bool Taunted => Time.time < tauntUntil && tauntSource != null && !tauntSource.IsDead;

    /// <summary>Force this player to engage <paramref name="source"/> for <paramref name="seconds"/> — auto-combat
    /// can't retarget away while it holds. The player's twin of <c>MonsterAI3D.Taunt</c>, so a taunt authored on a
    /// skill behaves the same whoever it lands on. Stacks by later end time, exactly like the mob side.</summary>
    public void Taunt(Health source, float seconds)
    {
        if (source == null || seconds <= 0f) return;
        tauntSource = source;
        tauntUntil = Mathf.Max(tauntUntil, Time.time + seconds);
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
        AttackArmed = false; order = Order.Idle; pendingPickup = null;
        routing = false; path.Clear(); attackTarget = null; resumeAttackMove = false;
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
                if (look.sqrMagnitude > 1e-4f) worldFacing = look;
            }
            else worldFacing = dir;
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
        attackTarget = null; resumeAttackMove = false; pendingPickup = null;
        if (targetMarker != null) targetMarker.Follow(null);
    }

    void FaceTarget()
    {
        if (attackTarget == null) return;
        Vector3 d = attackTarget.transform.position - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 1e-4f) worldFacing = d;   // facing derived camera-relative in Update
    }

    // Nearest living mob within radius (planar XZ). Scans the MonsterAI3D registry rather than a physics
    // OverlapSphere — the old fixed 32-collider buffer overflowed with props/terrain at large roamRange in
    // a dense scene, returning colliders in arbitrary order and silently dropping mobs (auto-combat then
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
    void StartRoute(Vector3 goal)
    {
        var from = new Vector2(transform.position.x, transform.position.z);
        var to = new Vector2(goal.x, goal.z);
        // Fills `path` in place (allocation-free); player uses the full default expansion budget.
        if (!Pathfinder3D.FindPath(from, to, agentRadius, path))
            { path.Clear(); path.Add(to); }
        wp = 0; routing = path.Count > 0;
    }

    Vector3 FollowRoute()
    {
        if (wp >= path.Count) { routing = false; return Vector3.zero; }
        Vector3 to = WaypointDelta();
        if (to.magnitude <= arriveDistance)
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
    /// Frame-memoized: it's read up to 3× per frame (GameCursor3D, MobHoverPanel, click/target handlers), so the
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
        // The cursor is over a HUD element (e.g. an open Inventory/Equipment/Character window): don't
        // pick the mob "behind" it. This single guard makes the sword cursor (GameCursor3D), the hover
        // panel (MobHoverPanel), and click-to-attack all ignore mobs occluded by the UI.
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

    /// <summary>Public wrapper for the cursor→ground raycast — used by ground-targeted skills (e.g. Meteor).</summary>
    public static bool GroundUnderCursor(Camera cam, out Vector3 point) => GroundPoint(cam, out point);

    /// <summary>Is there a collectable pickup under the cursor? Lets <see cref="GameCursor3D"/> show the grab hand.</summary>
    public static bool PickupHovered(Camera cam) => PickupUnderCursor(cam) != null;

    /// <summary>The world pickup under the cursor (camera raycast hits its trigger sphere), or null. Drives click-to-collect.</summary>
    static WorldPickup PickupUnderCursor(Camera cam)
    {
        if (cam == null) return null;
        var ray = cam.ScreenPointToRay(MousePos());
        return Physics.Raycast(ray, out var hit, 500f) ? hit.collider.GetComponentInParent<WorldPickup>() : null;
    }

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

    static Vector2 MousePos()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
        return Input.mousePosition;
    }

    static bool LeftDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
#endif
        return Input.GetMouseButtonDown(0);
    }

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
