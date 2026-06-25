using UnityEngine;

/// <summary>
/// Ambient-life brain for a wandering townsfolk billboard (liveliness layers 1 + 3). It is a deliberately
/// DUMB cousin of <see cref="MonsterAI3D"/>: no aggro, no combat — it just strolls to random points near where
/// it spawned, pauses, and occasionally throws a canned "bark" when the player passes. "Lively" here = motion +
/// believable purpose + reactivity, NOT intelligence (RO felt alive with almost no NPC AI). No Claude/LLM here.
///
/// Reuse, not reinvention:
///   • Movement rides the SAME <see cref="PathAgent3D"/> the mobs use (routes around props, crowd-spreads,
///     gravity-grounded), so the visual <see cref="BakedBillboard8"/> self-animates Walk/Idle from the motion.
///   • Barks reuse the pooled <see cref="DamagePopup.SpawnBubble"/> (zero per-bark allocation).
///   • Crowd pass-through reuses <see cref="UnitCrowd"/> so the player walks THROUGH townsfolk (no getting stuck).
///
/// PERFORMANCE (PERFORMANCE.md — hard constraint): simulate-near / freeze-far. Beyond <see cref="simulateRadius"/>
/// from the player the brain stops AND the movement/animation components are disabled, so a town full of NPCs
/// costs almost nothing while you're across the map. The LOD test itself runs on a staggered ~0.4s cadence
/// (random phase per NPC) — never a per-frame distance scan for every townsperson. Nothing here allocates.
/// </summary>
[RequireComponent(typeof(PathAgent3D))]
public class TownNPC : MonoBehaviour
{
    // ---- configured by AmbientLife at spawn (see Init) ----
    Vector3 home;
    float wanderRadius = 6f;
    string[] barkPool;
    bool barksEnabled;
    Transform player;
    LayerMask groundMask;   // Ground layer only — what we raycast-snap the feet to (see ClampToGround)

    [Header("Wander")]
    [Tooltip("Seconds to linger at a spot before strolling to the next one.")]
    public float pauseMin = 1.5f, pauseMax = 5f;

    [Header("Barks (reactive chatter when the player is near)")]
    [Tooltip("Bark only when the player is within this distance (world units).")]
    public float barkRadius = 4.5f;
    [Tooltip("Cooldown between this NPC's barks once one fires.")]
    public float barkCooldownMin = 10f, barkCooldownMax = 24f;

    [Header("Level of detail")]
    [Tooltip("Beyond this distance from the player the brain + movement + animation freeze (perf).")]
    public float simulateRadius = 35f;

    [Header("Unstick")]
    [Tooltip("Give up on a stroll target and pick a new one if it isn't reached within this long — frees " +
             "townsfolk that walked into a wall on a blocked/straight-line route.")]
    public float maxWanderSeconds = 5f;

    PathAgent3D agent;
    BakedBillboard8 visual;
    float nextWander, nextBark, nextLodCheck;
    float wanderDeadline, nextProgressCheck;   // stuck-detection: time budget + periodic progress sampling
    Vector3 lastProgressPos;
    Vector3 lastStepPos;        // footstep odometer: accumulate ground distance, emit a step each sStride
    float stepDist;             // distance walked (horizontal) since the last footstep SFX fired
    bool simulating = true;
    bool gaitMatched;   // have we copied the player's fine-tuned walk/run cadence onto this NPC yet?

    // Player gait tuning, resolved ONCE for the whole town (the player's billboard + its real Walk/Run speeds).
    // Static so N townsfolk don't each scan; re-resolves if the cached visual was destroyed (scene reload).
    static BakedBillboard8 sPlayerVisual;
    static float sWalkRef, sRunRef;

    // Footsteps: borrow the SAME clips + cadence the player walks on, so a town shares one consistent footstep
    // voice (zero new audio assets). Resolved alongside the gait, once for the whole town. sStride is how far a
    // unit travels per step at the player's tuned cadence (stepInterval × run speed) — driving NPC steps off
    // DISTANCE travelled rather than a timer keeps them in sync with each NPC's own rolled speed (no foot-slide).
    static SfxSet sPlayerFootstep;
    static float sStride;

    static bool ResolvePlayerGait()
    {
        if (sPlayerVisual != null) return true;
        var pv = FindFirstObjectByType<BakedPlayerVisual>();
        var pc = FindFirstObjectByType<Player3DController>();
        if (pv == null || pc == null) return false;
        sPlayerVisual = pv.GetComponent<BakedBillboard8>();
        if (sPlayerVisual == null) return false;
        float runSpeed = pc.moveSpeed * UnitScale.ForPlayer;     // the player's normal (run-clip) ground speed
        sRunRef  = runSpeed;
        sWalkRef = runSpeed * pc.walkSpeedMult;                  // the player's Shift-walk (walk-clip) ground speed
        sPlayerFootstep = pc.footstepSfx;
        sStride = Mathf.Max(0.1f, pc.stepInterval * runSpeed);   // world distance the player covers per step
        return true;
    }

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        visual = GetComponentInChildren<BakedBillboard8>(true) ?? GetComponent<BakedBillboard8>();
    }

    /// <summary>Set this townsperson up. Called by <see cref="AmbientLife"/> right after spawn so all the
    /// per-NPC randomization (speed, home, pools, phase offsets) lives in one place.</summary>
    public void Init(Vector3 homePoint, float wander, float moveSpeed, string[] barks, bool barksOn, Transform playerT, LayerMask ground)
    {
        home = homePoint;
        wanderRadius = Mathf.Max(0.5f, wander);
        barkPool = barks;
        barksEnabled = barksOn && barks != null && barks.Length > 0;
        player = playerT;
        groundMask = ground;

        agent.moveSpeed = moveSpeed;
        // Spread apart from other units, but townsfolk don't chase anyone — no avoidIgnore target.
        agent.avoidMask = UnitCrowd.Mask;
        UnitCrowd.Register(gameObject);   // player + mobs pass through townsfolk (no stacking / getting stuck)

        // Desync the herd: random first-step delay + random LOD phase, so a freshly spawned crowd doesn't all
        // step / LOD-check on the same frame. The FIRST bark is allowed almost immediately (small random
        // delay) — otherwise you'd have to loiter for a full 10–24s cooldown before hearing a single line;
        // the long cooldown only governs REPEAT barks once the first has fired.
        nextWander = Time.time + Random.value * pauseMax;
        nextBark = Time.time + 0.5f + Random.value * 2.5f;
        nextLodCheck = Time.time + Random.value * 0.4f;
        lastStepPos = transform.position;
        stepDist = Random.value;   // desync first step across the crowd so they don't all stomp in lockstep
    }

    void Update()
    {
        // --- LOD gate (staggered, not per-frame) ---
        if (Time.time >= nextLodCheck)
        {
            nextLodCheck = Time.time + 0.4f + Random.value * 0.2f;

            // Recover from a load-order miss: if AmbientLife.Start ran before the (additively-loaded) player
            // existed, re-acquire it here. Bounded cost — only runs while player is still null, on this slow
            // cadence, never per-frame.
            if (player == null)
            {
                var pc = FindFirstObjectByType<Player3DController>();
                if (pc != null) player = pc.transform;
            }

            // Inherit the player's fine-tuned Walk/Run cadence so townsfolk don't foot-slide. Once, lazily, the
            // first time the player is available — load order can spawn NPCs before the (additive) player exists.
            if (!gaitMatched && visual != null && ResolvePlayerGait())
            {
                visual.MatchGaitTuning(sPlayerVisual, sWalkRef, sRunRef);
                gaitMatched = true;
            }

            bool near = player == null ||
                        (player.position - transform.position).sqrMagnitude <= simulateRadius * simulateRadius;
            if (near != simulating) SetSimulating(near);
        }
        if (!simulating) return;

        // --- Layer 1: wander. Stroll to a random point near home, then pause. Same verb the mobs use. ---
        // Pick a fresh target when we've arrived and rested, OR when we're plainly stuck (pushing a wall): a
        // blocked goal makes PathAgent3D straight-line into geometry and never "arrive", so without this an NPC
        // would shove the wall forever (the bug in the screenshot).
        if (agent.Arrived && Time.time >= nextWander)
        {
            WanderTo(home + RandomOffset(wanderRadius));   // free roam anywhere near home
        }
        else if (!agent.Arrived && IsStuck())
        {
            WanderTo(home + RandomOffset(wanderRadius * 0.35f));   // unstick: head back toward open home ground
        }

        // --- Layer 3: reactive barks when the player passes by ---
        if (barksEnabled && player != null && Time.time >= nextBark)
        {
            if ((player.position - transform.position).sqrMagnitude <= barkRadius * barkRadius)
            {
                string line = barkPool[Random.Range(0, barkPool.Length)];
                // White speech bubble just above the head. VisualWorldHeight is the sprite's real world height
                // (feet ≈ this transform), so head + a small gap places the bubble's bottom over the head.
                float head = visual != null ? visual.VisualWorldHeight : 1f;
                SpeechBubble.Show(transform, head - 0.2f, line, 2.6f);   // bubble rides this unit's head // i tuned this to 0.2 --vince
                nextBark = Time.time + Random.Range(barkCooldownMin, barkCooldownMax);
            }
            else nextBark = Time.time + 1f;   // out of range — re-check soon, don't burn the full cooldown
        }

        Footsteps();
    }

    // Layer-1 polish: spatial footstep SFX. Drive steps off DISTANCE walked (an odometer) rather than a timer, so
    // they stay locked to this NPC's own rolled speed and never foot-slide. Plays the player's footstep clips
    // POSITIONALLY (Sfx.PlayAt) so you hear WHERE a townsperson is walking — and the engine fades distant ones, so
    // a busy plaza isn't a wall of stomps. Cheap: one Vector3 subtract per moving townsperson, near the camera only.
    void Footsteps()
    {
        if (sStride <= 0f || sPlayerFootstep == null || sPlayerFootstep.Empty || agent == null || !agent.Moving)
        {
            lastStepPos = transform.position;   // keep the odometer pinned to "now" while halted (no phantom step on resume)
            return;
        }

        Vector3 p = transform.position;
        stepDist += new Vector2(p.x - lastStepPos.x, p.z - lastStepPos.z).magnitude;   // horizontal travel only
        lastStepPos = p;
        if (stepDist >= sStride)
        {
            stepDist = 0f;
            Sfx.PlayAt(sPlayerFootstep, p);
        }
    }

    static Vector3 RandomOffset(float radius)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        return new Vector3(r.x, 0f, r.y);
    }

    // Issue a new stroll target and (re)arm the stuck watchdog: a time budget to reach it + a progress baseline.
    void WanderTo(Vector3 target)
    {
        agent.MoveTo(target);
        nextWander = Time.time + Random.Range(pauseMin, pauseMax);
        wanderDeadline = Time.time + maxWanderSeconds;
        lastProgressPos = transform.position;
        nextProgressCheck = Time.time + 0.7f;
    }

    // "Stuck" = blew the time budget, or sampled almost no forward progress since the last check (~0.7s) despite
    // being on the move — i.e. grinding against a wall. Cheap: no raycasts, just a periodic distance sample.
    bool IsStuck()
    {
        if (Time.time >= wanderDeadline) return true;
        if (Time.time < nextProgressCheck) return false;
        nextProgressCheck = Time.time + 0.7f;
        float moved2 = (transform.position - lastProgressPos).sqrMagnitude;
        lastProgressPos = transform.position;
        return moved2 < 0.18f * 0.18f;   // <0.18u in 0.7s while "walking" → not actually getting anywhere
    }

    // Pin the feet to the GROUND surface (Ground layer only — never props/snow caps), matching the project's
    // world-ground standard so townsfolk sit on the floor exactly like the player. Runs in LateUpdate (after
    // PathAgent3D's gravity Move) and only while walking — when idle the Y is already settled, so this costs
    // one raycast per *moving* townsperson per frame, near the camera only.
    void LateUpdate()
    {
        if (!simulating || groundMask.value == 0 || agent == null || !agent.enabled || !agent.Moving) return;
        Vector3 p = transform.position;
        if (GroundSnap.TryGroundY(p, groundMask, out float groundY))   // shared Ground-layer foot-seat
        {
            p.y = groundY;              // root ON the ground = the sandbox basis feetOffset was tuned for
            transform.position = p;
        }
    }

    // Flip between full simulation and frozen. Freezing disables the mover + the billboard animator so a
    // distant town is nearly free; re-entering range re-enables them (Awake already ran, so it just resumes).
    void SetSimulating(bool on)
    {
        simulating = on;
        if (on) lastStepPos = transform.position;   // reset the footstep odometer so re-entering range can't fire a phantom step
        if (!on) agent.Halt();
        if (agent != null) agent.enabled = on;
        if (visual != null) visual.enabled = on;
    }
}
