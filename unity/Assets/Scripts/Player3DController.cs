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
    [Tooltip("TRAVEL speed: base ground translation (world units, pre-UnitScale). Changing this moves you faster/slower " +
             "WITHOUT scaling the animation — GaitSpeedMult divides it back out, so the legs keep their baked cadence.")]
    public float moveSpeed = 1f;
    [Tooltip("GAIT speed: a baseline multiplier that scales movement AND animation together (the permanent twin of the " +
             "DevSettings Haste buff). Stacks multiplicatively with live Slow/Haste, so legs + footsteps churn with it.")]
    public float gaitSpeed = 1.5f;
    /// <summary>The player's CURRENT run speed in world units/sec — the same hand-tuned baseline the walk uses, scaled
    /// by <see cref="UnitScale.ForPlayer"/>, the <see cref="gaitSpeed"/> baseline, and the live Slow/Haste move buff
    /// (but NOT the Shift-walk slowdown). The dodge roll (E) reads this so it's always exactly 2× the current run.</summary>
    public float CurrentRunSpeed => moveSpeed * UnitScale.ForPlayer * gaitSpeed * (status != null ? status.MoveMult : 1f);
    [Tooltip("Speed multiplier while holding Shift to walk — slower than the default run, and plays the Walk animation.")]
    public float walkSpeedMult = 0.45f;
    /// <summary>True while moving with Shift held (walk, not run). Read by <see cref="Character3DVisual"/>'s stance brain to
    /// choose the Walk vs Run animation.</summary>
    public bool WalkingSlow { get; private set; }
    /// <summary>Live ratio of the player's ACTUAL move speed to its tuned baseline (1 = normal, &gt;1 = sped up by a
    /// buff, &lt;1 = slowed). The single knob that binds gait to movement speed: the footstep cadence divides by it
    /// and the stance visual multiplies the Walk/Run clip fps by it, so a speed buff makes the steps and the run/walk
    /// animation visibly faster (and a Slow ailment slower) without touching the hand-tuned baselines.</summary>
    public float GaitSpeedMult { get; private set; } = 1f;
    public float arriveDistance = 0.12f;
    [Tooltip("Clearance kept from props when routing.")]
    public float agentRadius = 0.35f;
    [Tooltip("Downward acceleration (units/s²). Now ACCUMULATES into a vertical velocity while airborne so " +
             "walking off a ledge free-falls (accelerates) instead of descending at a flat rate; reset on landing.")]
    public float gravity = 12f;
    [Tooltip("Downward speed (units/s) the player must exceed before the falling pose plays — keeps small " +
             "steps/slopes from flickering the in-air animation.")]
    public float fallAnimSpeed = 1.5f;
    [Tooltip("During attack-move, auto-engage a mob within this range.")]
    public float acquireRange = 5f;

    // ── Collision capsule (CharacterController) — hand-tuned for the 2.5D avatar ──────────────────
    // Deliberately SHORT so the capsule's top clears overhead prop geometry (overhanging roof eaves,
    // tree leaves) while the bottom still bumps + climbs ground-level geometry (walls, stairs, tree
    // roots). Applied in Awake so every player gets it without a rebuild and it survives map loads;
    // also the shared default the player builders bake in (see ApplyDefaultCapsule). Prop colliders
    // stay the exact visible mesh — the oversize-collision feel was THIS capsule being full-height.
    public const float DefaultCapsuleRadius = 0.15f;
    public const float DefaultCapsuleHeight = 0.5f;
    public const float DefaultCapsuleStepOffset = 0.3f;
    public const float DefaultCapsuleSlopeLimit = 45f;

    // set skin width to 0.02f --vince
    public const float DefaultCapsuleSkinWidth = 0.02f;   // low so the capsule bottom rests ~on the ground (min hover)
    // Centered capsule (center.y = 0). The VISUAL is feet-seated by Character3DVisual regardless; the CC is kept
    // centered on the root by choice. Lowering skinWidth (above) is what keeps the capsule bottom touching ground.

    // i changed collider Y to 0.25f, blame me here when encountering weird collisions with props. -vince
    public static readonly Vector3 DefaultCapsuleCenter = new Vector3(0f, 0.25f, 0f);

    [Header("Collision capsule (applied in Awake — tuned to clear overhead leaves/eaves)")]
    public float capsuleRadius = DefaultCapsuleRadius;
    public float capsuleHeight = DefaultCapsuleHeight;
    public Vector3 capsuleCenter = DefaultCapsuleCenter;   // single source: mirror the const, don't re-type
    public float capsuleStepOffset = DefaultCapsuleStepOffset;
    public float capsuleSlopeLimit = DefaultCapsuleSlopeLimit;
    public float capsuleSkinWidth = DefaultCapsuleSkinWidth;

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
    [Tooltip("Seconds between footstep sounds while RUNNING (normal movement).")]
    public float stepInterval = 0.42f;
    [Tooltip("Seconds between footstep sounds while WALKING (Shift held) — usually larger than the run gap since walking is slower.")]
    public float walkStepInterval = 0.62f;
    float nextStep;
    bool wasStepping;   // were we moving last footstep tick? — so a fresh walk can delay its first step sound to match the ripple

    [Header("Auto-combat (T to toggle)")]
    [Tooltip("While auto-combat roams, it engages the nearest mob within this range and walks to it. " +
             "Larger = ranges farther from where you stand to find fights.")]
    public float roamRange = 14f;

    /// <summary>True after pressing A, until the next click (the cursor shows the crosshair).</summary>
    public bool AttackArmed { get; private set; }
    /// <summary>True while roam auto-combat is running (drives the on-screen indicator).</summary>
    public bool AutoActive => order == Order.AutoCombat;

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

    // Charge / gap-closer dash state (driven via BeginDash — a reusable controller API for dash skills).
    bool dashing;
    Transform dashTarget;
    float dashSpeed, dashEndTime, dashArriveRange;
    System.Action dashOnArrive;
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
        // were retired — one runner interprets any skill. Which the player has UNLOCKED and BOUND to the hotbar
        // is still the progression layer (SkillRoster/HotbarBindings), not a class gate.
        EnsureComposedSkills();
    }

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
            runner.level = 1;
            added = true;
        }
        if (added) PlayerSkills.Refresh();   // re-resolve id → runner so the hotbar/menu see the new actives
    }

    /// <summary>Push the serialized capsule fields onto the CharacterController. Called in Awake so the
    /// tuned (short) capsule is enforced on every player — even one built before this — and re-applies
    /// across map loads. Tune via the Inspector fields (or the Default* consts) and it sticks.</summary>
    public void ApplyCapsule()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (controller == null) return;
        controller.radius = capsuleRadius;
        controller.height = capsuleHeight;
        controller.center = capsuleCenter;
        controller.stepOffset = capsuleStepOffset;
        controller.slopeLimit = capsuleSlopeLimit;
        controller.skinWidth = capsuleSkinWidth;
    }

#if UNITY_EDITOR
    // Live-apply the serialized capsule fields to the CharacterController as you tune them in the Inspector —
    // including in Play mode (Awake ran once and won't re-read them). Editor-only; no runtime cost.
    void OnValidate()
    {
        if (GetComponent<CharacterController>() != null) ApplyCapsule();
    }
#endif

    /// <summary>Write the canonical tuned capsule onto a CharacterController — the one place the player
    /// builders (Build Player / NinjaGreen / Sscary) call so a freshly-built player matches the runtime
    /// default in edit mode too. Keeps all four sites from drifting.</summary>
    public static void ApplyDefaultCapsule(CharacterController cc)
    {
        if (cc == null) return;
        cc.radius = DefaultCapsuleRadius;
        cc.height = DefaultCapsuleHeight;
        cc.center = DefaultCapsuleCenter;
        cc.stepOffset = DefaultCapsuleStepOffset;
        cc.slopeLimit = DefaultCapsuleSlopeLimit;
        cc.skinWidth = DefaultCapsuleSkinWidth;
    }

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
            jumping = false; eDashing = false;   // …and any hop/dash in progress
            SetDashInvuln(false);                // …so a dash cut short by death doesn't leave us invulnerable
            controller.Move(Vector3.down * gravity * Time.deltaTime);
            return;
        }

        // Rooted: hard-CC (stun / freeze) OR a skill's self-root (cast wind-up + aftercast) — no input, no orders, no
        // movement — gravity only — until it lifts. Cancels an in-flight swing and any charge dash so nothing fires.
        if (status != null && status.ActionLocked)
        {
            if (attacker != null && attacker.Swinging) attacker.Cancel();
            dashing = false; dashOnArrive = null; dashTarget = null;
            controller.Move(Vector3.down * gravity * Time.deltaTime);
            return;
        }

        // A charge (gap-closer) overrides normal control: rush the target, then the skill's on-arrive
        // callback strikes. Skips input/orders until it lands (or times out).
        if (dashing)
        {
            bool dashMoving = StepDash();
            Footsteps(dashMoving);
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
        if (wasd.sqrMagnitude > 0.001f)
        {
            if (order != Order.Idle) Stop();   // drop any routed move/attack/auto so WASD wins
            move = wasd;
        }
        else move = Tick();

        bool moving = move.sqrMagnitude > 0.001f;
        if (moving) worldFacing = move;
        WalkingSlow = moving && HeldShift();            // Shift = walk: slower move + Walk animation (read by the stance visual)
        float baseSpeed = moveSpeed * UnitScale.ForPlayer * (WalkingSlow ? walkSpeedMult : 1f); // TRAVEL baseline (run vs walk) — NOT animation-scaling
        float gait = gaitSpeed * (status != null ? status.MoveMult : 1f);                        // GAIT baseline × live Slow/Haste — animation-scaling
        float spd = baseSpeed * gait;
        // Bind gait to ACTUAL speed: the gait factor over the travel baseline. Drives both the footstep
        // cadence (below) and the Walk/Run animation fps (stance visual) so they speed up/slow down together.
        GaitSpeedMult = baseSpeed > 0.0001f ? spd / baseSpeed : 1f;
        ApplyGravityAndMove(move * spd);   // accumulating free-fall (sets `falling` for the in-air pose)

        Footsteps(moving && !falling);   // no footsteps while free-falling through the air
        if (autoIndicator != null) autoIndicator.Show(AutoActive);   // show the "AUTO" tag only while roaming
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

    // Tick footstep sounds while walking. nextStep is a persistent cooldown — we DON'T reset it when
    // momentarily stopped, so a stand/chase stutter (e.g. pacing a kiting mob at range-edge) can't
    // machine-gun the step SFX every other frame; steps still fire at most once per stepInterval.
    void Footsteps(bool moving)
    {
        if (!moving) { wasStepping = false; return; }

        // Hold off the FIRST step sound of a fresh walk by the SAME gait-time the ripple delays its first print, so the
        // sound stays glued to the splash. delay is in gait-time stride units → divide by gait for real seconds (the
        // same scaling the interval below uses). Only when a delay is set, so the no-delay machine-gun guard is untouched.
        if (!wasStepping)
        {
            wasStepping = true;
            float delay = FootstepTracker.Instance != null ? FootstepTracker.Instance.firstStepDelay : 0f;
            if (delay > 0f) nextStep = Time.time + delay / Mathf.Max(0.05f, GaitSpeedMult);
        }

        if (Time.time < nextStep) return;

        // Splash instead of the dry step when the feet are over a visible rain puddle (the WetGround shader's
        // own puddle mask answers "are we on water?" — see WetGround.IsPuddleAt). Falls back to the normal set.
        bool onWater = waterFootstepSfx != null && !waterFootstepSfx.Empty
                       && WetGround.Instance != null && WetGround.Instance.IsPuddleAt(transform.position);
        Sfx.Play(onWater ? waterFootstepSfx : footstepSfx);
        // Tighten/loosen the gap with the live move speed: a speed buff (GaitSpeedMult > 1) shortens the interval
        // so steps land more often; a Slow (< 1) widens it. At normal speed (1) this is the hand-tuned interval.
        float interval = (WalkingSlow ? walkStepInterval : stepInterval) / Mathf.Max(0.05f, GaitSpeedMult);
        nextStep = Time.time + interval;   // slower gait → wider gap
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
        if (KeyEsc()) AttackArmed = false;

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
        if (attackTarget == null || attackTarget.IsDead)
        {
            attackTarget = NearestMonsterWithin(roamRange);
            if (targetMarker != null) targetMarker.Follow(attackTarget != null ? attackTarget.transform : null);
            routing = false; path.Clear();
            if (attackTarget == null) return Vector3.zero;   // nothing in range — stand and wait for a wanderer
        }
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
        dashing = false; dashOnArrive = null; dashTarget = null;   // a charge is cancelled by Stop / S
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
        dashing = true; dashTarget = target; dashSpeed = speed;
        dashEndTime = Time.time + maxTime; dashArriveRange = arriveRange; dashOnArrive = onArrive;
    }

    // Advance the charge one frame. Returns true while still dashing; on arrival/timeout it stops and
    // fires the on-arrive callback (the strike). Exactly one controller.Move per frame, never two.
    bool StepDash()
    {
        bool arrived = dashTarget == null || Time.time >= dashEndTime;
        if (!arrived)
        {
            Vector3 to = dashTarget.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist <= dashArriveRange) arrived = true;
            else
            {
                Vector3 dir = to / Mathf.Max(dist, 1e-4f);
                worldFacing = dir;
                controller.Move((dir * dashSpeed + Vector3.down * gravity) * Time.deltaTime);
            }
        }
        if (arrived)
        {
            dashing = false;
            var cb = dashOnArrive; dashOnArrive = null; dashTarget = null;
            cb?.Invoke();
            return false;
        }
        return true;
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
    /// <summary>The living mob under the cursor (camera raycast), or null. Static so GameCursor3D shares it.</summary>
    public static Health MonsterUnderCursor(Camera cam)
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

    static bool KeyA()
    {
        if (Hotkeys.Typing) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.A);
    }

    static bool KeyT()
    {
        if (Hotkeys.Typing) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.T);
    }

    static bool KeyS()
    {
        if (Hotkeys.Typing) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.sKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.S);
    }

    static bool KeyEsc()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Escape);
    }

    // WASD movement uses HELD reads (isPressed / GetKey), not the edge reads above, so motion
    // continues for as long as the key is down.
    static bool HeldW()
    {
        if (Hotkeys.Typing) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.wKey.isPressed) return true;
#endif
        return Input.GetKey(KeyCode.W);
    }

    static bool HeldA()
    {
        if (Hotkeys.Typing) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.aKey.isPressed) return true;
#endif
        return Input.GetKey(KeyCode.A);
    }

    static bool HeldS()
    {
        if (Hotkeys.Typing) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.sKey.isPressed) return true;
#endif
        return Input.GetKey(KeyCode.S);
    }

    static bool HeldD()
    {
        if (Hotkeys.Typing) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.dKey.isPressed) return true;
#endif
        return Input.GetKey(KeyCode.D);
    }

    // Shift = walk (held). Either Shift key downshifts run → walk.
    static bool HeldShift()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed)) return true;
#endif
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

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
