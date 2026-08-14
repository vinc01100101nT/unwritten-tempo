using UnityEngine;

/// <summary>
/// Ambient-life brain for a wandering townsfolk billboard (liveliness layers 1 + 3). It is a deliberately
/// DUMB cousin of <see cref="MonsterAI3D"/>: no aggro, no combat — it just strolls to random points near where
/// it spawned, pauses, and occasionally throws a canned "bark" when the player passes. "Lively" here = motion +
/// believable purpose + reactivity, NOT intelligence (RO felt alive with almost no NPC AI). No Claude/LLM here.
///
/// Reuse, not reinvention:
///   • Movement rides the SAME <see cref="PathAgent3D"/> the mobs use (routes around props, crowd-spreads,
///     gravity-grounded); the live-3D <see cref="Character3DPose"/> self-animates Walk/Idle from that motion.
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

    [Header("Wander")]
    [Tooltip("Seconds to linger at a spot before strolling to the next one.")]
    public float pauseMin = 1.5f, pauseMax = 5f;

    [Header("Barks (reactive chatter when the player is near)")]
    [Tooltip("Bark only when the player is within this distance (world units).")]
    public float barkRadius = 4.5f;
    [Tooltip("Cooldown between this NPC's barks once one fires.")]
    public float barkCooldownMin = 10f, barkCooldownMax = 24f;

    [Header("Level of detail")]
    [Tooltip("Beyond this distance from the player the brain + movement + animation freeze (perf). Fed to the " +
             "shared SimulationLod switch, which mobs use too.")]
    public float simulateRadius = 35f;

    [Header("Unstick")]
    [Tooltip("Give up on a stroll target and pick a new one if it isn't reached within this long — frees " +
             "townsfolk that walked into a wall on a blocked/straight-line route.")]
    public float maxWanderSeconds = 5f;

    PathAgent3D agent;
    // The visual is resolved through the IStancePose seam — a townsperson's live-3D model (Character3DPose) with no
    // hardcoded type here. visualBehaviour is that same object typed as a Behaviour (for the LOD enable/disable);
    // visualAnimator + visualDriver are the 3D pieces to freeze when far.
    IStancePose visual;
    Behaviour visualBehaviour;
    Animator visualAnimator;
    Behaviour visualDriver;
    float nextWander, nextBark, nextResolveCheck;
    bool paused;                               // true while lingering at an arrived spot (waiting out the pause before the next stroll)
    float wanderDeadline, nextProgressCheck;   // stuck-detection: time budget + periodic progress sampling
    Vector3 lastProgressPos;
    SimulationLod lod;                         // the SHARED simulate-near / freeze-far switch (mobs use the same one)

    // Footsteps run through the SAME frame-locked plant detector the player uses (FootstepTracker.DetectPlants):
    // townsfolk are live-3D (a self-animating Character3DPose), so each NPC's steps fire on its OWN walk/run animation
    // crossing an authored foot-plant frame — never a distance odometer — so its SFX + ripple land on the visibly-
    // planting foot with no foot-slide. Per-NPC gait state + a cached (zero-alloc) plant sink.
    Character3DPose pose3d;
    readonly FootstepTracker.Gait npcGait = new FootstepTracker.Gait();
    System.Action<Vector3, float> onFootPlant;

    // Borrow the SAME clips the player walks on, so a town shares one consistent footstep voice (zero new audio
    // assets). Resolved once for the whole town straight off Player3DController.
    static bool sSfxResolved;
    static SfxSet sPlayerFootstep;
    static SfxSet sWaterFootstep;   // the player's puddle-splash step, swapped in when an NPC is over a puddle

    static bool ResolveFootstepSfx()
    {
        if (sSfxResolved) return true;
        var pc = FindAnyObjectByType<Player3DController>();
        if (pc == null) return false;
        sPlayerFootstep = pc.footstepSfx;
        sWaterFootstep = pc.waterFootstepSfx;
        sSfxResolved = true;
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
        pose3d = GetComponentInChildren<Character3DPose>(true);    // the self-animating pose we read foot-plants from
        onFootPlant = OnFootPlant;                                 // cache once → per-frame detection allocates no closure

        // The freeze-far switch. It owns the staggered distance test and disables the mover/animator/visual driver;
        // this brain only asks whether it's simulating, and re-seeds the gait when it thaws.
        lod = GetComponent<SimulationLod>() ?? gameObject.AddComponent<SimulationLod>();
        lod.simulateRadius = simulateRadius;
        lod.Freeze(agent, visualBehaviour, visualAnimator, visualDriver);
        lod.OnSimulatingChanged += OnSimulatingChanged;
    }

    // Re-seed on THAW so re-entering range can't fire a phantom footstep from a position we left long ago; halt on
    // FREEZE so a frozen NPC doesn't resume mid-stride. Same two beats the old inline gate did.
    void OnSimulatingChanged(bool on)
    {
        if (on) npcGait.Seed(transform.position);
        else if (agent != null) agent.Halt();
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
    public void Init(Vector3 homePoint, float wander, float moveSpeed, bool barksOn, Transform playerT)
    {
        home = homePoint;
        wanderRadius = Mathf.Max(0.5f, wander);
        barksEnabled = barksOn;
        player = playerT;

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
        nextResolveCheck = Time.time + Random.value * 0.4f;
        npcGait.Seed(transform.position);
    }

    void Update()
    {
        // --- Load-order recovery (staggered, not per-frame; stops once both are resolved) ---
        if ((player == null || !sSfxResolved) && Time.time >= nextResolveCheck)
        {
            nextResolveCheck = Time.time + 0.4f + Random.value * 0.2f;

            // If AmbientLife.Start ran before the (additively-loaded) player existed, re-acquire it here. Bounded
            // cost — only runs while player is still null, on this slow cadence, never per-frame.
            if (player == null)
            {
                var pc = FindAnyObjectByType<Player3DController>();
                if (pc != null) player = pc.transform;
            }

            // Resolve the shared footstep SFX once the player exists (load order can spawn NPCs before the
            // additive player). The step CADENCE isn't resolved here anymore — it comes from each NPC's own walk/run
            // animation via FootstepTracker.DetectPlants, exactly like the player.
            if (!sSfxResolved) ResolveFootstepSfx();
        }

        // --- Frozen because the player is far away (the shared switch owns the distance test) ---
        if (lod != null && !lod.Simulating) return;

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
                SpeechBubble.ShowOverHead(visual, line, 2.6f);   // shared anchor: bubble bottom just above the head
                nextBark = Time.time + Random.Range(barkCooldownMin, barkCooldownMax);
            }
            else nextBark = Time.time + 1f;   // out of range — re-check soon, don't burn the full cooldown
        }

        Footsteps();
    }

    // Layer-1 polish: spatial footstep SFX + puddle ripple, fired the instant this NPC's OWN walk/run animation
    // crosses an authored foot-plant frame — the SAME FootstepTracker.DetectPlants the player runs, so a townsperson's
    // steps are glued to its visibly-planting feet at any speed (no foot-slide, no timer to retune). The per-plant
    // work happens in OnFootPlant. Cheap: near the camera only, and detection is a couple of subtracts + a phase read.
    void Footsteps()
    {
        var tracker = FootstepTracker.Instance;
        if (pose3d == null || tracker == null) return;
        tracker.DetectPlants(pose3d, transform.position, npcGait, onFootPlant);
    }

    // One foot-plant: feed the shared ring (wet-ground ripples / snow prints under townsfolk too), then play the
    // player's footstep clip POSITIONALLY (Sfx.PlayAt) so you hear WHERE the townsperson stepped — the engine fades
    // distant ones, so a busy plaza isn't a wall of stomps. Swaps in the splash step when the foot is over a puddle,
    // the same test the player uses. Zero-GC: IsPuddleAt is a single fbm, the SFX voice + ripple slot are pooled.
    void OnFootPlant(Vector3 foot, float sizeScale)
    {
        FootstepTracker.Instance?.EmitStep(foot, sizeScale);
        if (sPlayerFootstep == null || sPlayerFootstep.Empty) return;
        bool onWater = sWaterFootstep != null && !sWaterFootstep.Empty
                       && WetGround.Instance != null && WetGround.Instance.IsPuddleAt(foot);
        Sfx.PlayAt(onWater ? sWaterFootstep : sPlayerFootstep, foot);
    }

    static Vector3 RandomOffset(float radius)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        return new Vector3(r.x, 0f, r.y);
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

}
