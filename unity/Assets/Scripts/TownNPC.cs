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
    // The visual is resolved through the IStancePose seam, so a townsperson works with EITHER a baked billboard
    // (BakedBillboard8) or a live-3D model (Character3DPose) with no code change here. visualBehaviour is the same
    // object typed as a Behaviour (for the LOD enable/disable); visualAnimator + visualDriver are the extra 3D-only
    // pieces to freeze when far (null for a billboard).
    IStancePose visual;
    Behaviour visualBehaviour;
    Animator visualAnimator;
    Behaviour visualDriver;
    float nextWander, nextBark, nextLodCheck;
    bool paused;                               // true while lingering at an arrived spot (waiting out the pause before the next stroll)
    float wanderDeadline, nextProgressCheck;   // stuck-detection: time budget + periodic progress sampling
    Vector3 lastProgressPos;
    Vector3 lastStepPos;        // footstep odometer: accumulate ground distance, emit a step each sStride
    float stepDist;             // distance walked (horizontal) since the last footstep SFX fired
    bool stepLeft;              // alternating foot for the ripple zig-zag; false == RIGHT, so a fresh walk steps right first
    bool simulating = true;

    // Footsteps: borrow the SAME clips the player walks on, so a town shares one consistent footstep voice (zero new
    // audio assets). Resolved once for the whole town straight off Player3DController. NPCs are BILLBOARDS with no
    // real loco Animator, so — unlike the player's frame-locked prints — they keep a DISTANCE odometer: sStride is how
    // far a unit travels per step, which stays in sync with each NPC's own rolled speed (no foot-slide). The player no
    // longer owns a step-interval (its sound is frame-driven now), so the stride derives from a local cadence const.
    const float NpcStepCadence = 0.42f;   // seconds/step the NPC stride distance is derived from (was the player's old run interval)
    static bool sGaitResolved;
    static SfxSet sPlayerFootstep;
    static SfxSet sWaterFootstep;   // the player's puddle-splash step, swapped in when an NPC is over a puddle
    static float sStride;

    static bool ResolvePlayerGait()
    {
        if (sGaitResolved) return true;
        var pc = FindFirstObjectByType<Player3DController>();
        if (pc == null) return false;
        float runSpeed = pc.moveSpeed * UnitScale.ForPlayer;     // the player's normal (run) ground speed
        sPlayerFootstep = pc.footstepSfx;
        sWaterFootstep = pc.waterFootstepSfx;
        sStride = Mathf.Max(0.1f, NpcStepCadence * runSpeed);    // world distance an NPC covers per step
        sGaitResolved = true;
        return true;
    }

    Transform talkTarget;   // set while the player is talking to us (F-interact): hold still + face them, no wander/bark

    // Live registry of townsfolk for the press-to-talk lookup. FindObjectsByType can't be used — townsfolk are
    // spawned with HideFlags.DontSaveInEditor, which that call silently omits — so we keep our own list.
    static readonly System.Collections.Generic.List<TownNPC> Active = new System.Collections.Generic.List<TownNPC>();

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        visual = GetComponentInChildren<IStancePose>(true);
        visualBehaviour = visual as Behaviour;
        visualAnimator = GetComponentInChildren<Animator>(true);   // 3D only; null for a billboard NPC
        visualDriver = GetComponent<TownNPC3DVisual>();            // 3D only; null for a billboard NPC
    }

    void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    /// <summary>The nearest townsperson to <paramref name="pos"/> within <paramref name="maxDist"/> (world units),
    /// or null. Scans the small live registry (≈ town population) — called on a keypress, never per frame.</summary>
    public static TownNPC Nearest(Vector3 pos, float maxDist)
    {
        TownNPC best = null;
        float bestSq = maxDist * maxDist;
        for (int i = 0; i < Active.Count; i++)
        {
            var n = Active[i];
            if (n == null) continue;
            float d = (n.transform.position - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = n; }
        }
        return best;
    }

    /// <summary>Enter a conversation: stop where we are and turn to face <paramref name="speaker"/> (the player).
    /// While talking we don't wander or bark — <see cref="EndTalk"/> resumes normal ambient life.</summary>
    public void BeginTalk(Transform speaker)
    {
        talkTarget = speaker;
        paused = false;
        if (agent != null) agent.Halt();
    }

    public void EndTalk() => talkTarget = null;

    /// <summary>Set this townsperson up. Called by <see cref="AmbientLife"/> right after spawn so all the
    /// per-NPC randomization (speed, home, pools, phase offsets) lives in one place.</summary>
    public void Init(Vector3 homePoint, float wander, float moveSpeed, bool barksOn, Transform playerT, LayerMask ground)
    {
        home = homePoint;
        wanderRadius = Mathf.Max(0.5f, wander);
        barksEnabled = barksOn;
        player = playerT;
        groundMask = ground;

        agent.moveSpeed = moveSpeed;
        // Spread apart from other units WHILE WALKING, but townsfolk don't chase anyone — no avoidIgnore target.
        agent.avoidMask = UnitCrowd.Mask;
        // Don't drift while paused: the player is on the Crowd layer, so idle separation would shove a standing
        // townsperson away when the player steps beside it (sliding in the Idle pose). Hold ground when halted.
        agent.separateWhenIdle = false;
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

            // Resolve the shared footstep cadence once the player exists (load order can spawn NPCs before the
            // additive player). No billboard gait-fps matching now — the player is live-3D and NPC billboards
            // self-animate at their baked cadence.
            if (!sGaitResolved) ResolvePlayerGait();

            bool near = player == null ||
                        (player.position - transform.position).sqrMagnitude <= simulateRadius * simulateRadius;
            if (near != simulating) SetSimulating(near);
        }
        if (!simulating) return;

        // --- Talking to the player (F-interact): hold still, turn to face them, skip wander/barks/footsteps. ---
        if (talkTarget != null)
        {
            Vector3 toSpeaker = talkTarget.position - transform.position; toSpeaker.y = 0f;
            if (toSpeaker.sqrMagnitude > 1e-4f && visual != null) visual.SetFacing(toSpeaker);
            return;
        }

        // --- Layer 1: wander → pause → wander. Stroll to a random point near home, stand a moment, then pick a new
        // one. The pause is timed from ARRIVAL (not departure): when the agent reports Arrived we start the linger
        // clock once, stand Idle through it, then set off again — so the pause always happens no matter how long the
        // walk took. Same verb the mobs use.
        if (agent.Arrived)
        {
            if (!paused)
            {
                paused = true;
                nextWander = Time.time + Random.Range(pauseMin, pauseMax);   // just arrived → linger this long
            }
            else if (Time.time >= nextWander)
            {
                paused = false;
                WanderTo(home + RandomOffset(wanderRadius));   // pause over → free roam anywhere near home
            }
        }
        else if (IsStuck())
        {
            // Pushing a wall (a blocked goal makes PathAgent3D straight-line into geometry and never "arrive") —
            // bail to open home ground instead of shoving forever.
            paused = false;
            WanderTo(home + RandomOffset(wanderRadius * 0.35f));
        }

        // --- Layer 3: reactive barks when the player passes by ---
        if (barksEnabled && player != null && Time.time >= nextBark)
        {
            if ((player.position - transform.position).sqrMagnitude <= barkRadius * barkRadius)
            {
                string line = NpcBarks.PickTownLine();   // time-of-day aware: morning / day / evening / night pool
                SpeechBubble.Show(transform, BubbleAnchorY(), line, 2.6f);   // bubble bottom just above the head
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
            stepLeft = false;                   // resume always steps RIGHT first (matches the player)
            return;
        }

        Vector3 p = transform.position;
        Vector3 dir = new Vector3(p.x - lastStepPos.x, 0f, p.z - lastStepPos.z);   // horizontal walk direction this slice
        stepDist += dir.magnitude;   // horizontal travel only
        lastStepPos = p;
        if (stepDist >= sStride)
        {
            stepDist = 0f;
            // Same puddle test the player uses: swap in the splash step when this NPC's feet are over a visible puddle.
            bool onWater = sWaterFootstep != null && !sWaterFootstep.Empty
                           && WetGround.Instance != null && WetGround.Instance.IsPuddleAt(p);
            Sfx.PlayAt(onWater ? sWaterFootstep : sPlayerFootstep, p);
            // Feed the shared footstep ring so wet-ground ripples / snow prints appear under townsfolk too. Offset the
            // print under the alternating foot (RIGHT first) like the player, so left/right ripples land consistently.
            var tracker = FootstepTracker.Instance;
            if (tracker != null)
            {
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
                Vector3 perp = new Vector3(dir.z, 0f, -dir.x);   // +perp == right of the walk line
                Vector3 foot = p + perp * (tracker.footSpacing * (stepLeft ? -1f : 1f));
                stepLeft = !stepLeft;
                tracker.EmitStep(foot);
            }
        }
    }

    static Vector3 RandomOffset(float radius)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        return new Vector3(r.x, 0f, r.y);
    }

    /// <summary>World-up offset (from this unit's feet origin) at which a speech bubble's BOTTOM sits just above the
    /// head. VisualWorldHeight is the model's full feet-to-crown height, so head + a small gap clears it. Shared by
    /// ambient barks and the F-interact dialog so both anchor identically.</summary>
    public float BubbleAnchorY()
    {
        float head = visual != null ? visual.VisualWorldHeight : 1f;
        return head + 0.05f;
    }

    /// <summary>The model's rendered world height (feet to crown) — used to size the F-interact camera framing so it
    /// scales with the character instead of a hard-coded distance.</summary>
    public float HeadHeight => visual != null ? visual.VisualWorldHeight : 0.3f;

    /// <summary>A short multi-line, time-of-day-appropriate conversation for the F-interact dialog box.</summary>
    public string[] DialogueLines() => NpcBarks.PickTownConversation();

    /// <summary>Name shown in the dialog box's speaker slot.</summary>
    public string SpeakerName => "Villager";

    // Issue a new stroll target and (re)arm the stuck watchdog: a time budget to reach it + a progress baseline.
    void WanderTo(Vector3 target)
    {
        agent.MoveTo(target);
        // NOTE: the linger clock (nextWander) is armed on ARRIVAL, not here — see the wander block in Update.
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
        if (on) { lastStepPos = transform.position; stepLeft = false; }   // reset odometer + foot so re-entering range starts on the right
        if (!on) agent.Halt();
        if (agent != null) agent.enabled = on;
        if (visualBehaviour != null) visualBehaviour.enabled = on;   // BakedBillboard8 sprite anim / Character3DPose pose driver
        if (visualAnimator != null) visualAnimator.enabled = on;     // 3D: stop the skinned-mesh animator (the real cost) when far
        if (visualDriver != null) visualDriver.enabled = on;         // 3D: stop feeding Walk/Idle intent when far
    }
}
