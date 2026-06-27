using UnityEngine;

/// <summary>
/// The single SOURCE OF TRUTH for where the player's feet land. There is only one character, so the gait —
/// alternating left/right prints, the sideways zig-zag, the jump-landing splash — is computed ONCE here and
/// shared by every effect that reacts to footsteps (<see cref="WetGround"/> puddle ripples,
/// <see cref="SnowGround"/> snow prints, and anything added later). Before this existed each effect re-found
/// the player and re-ran its own stride loop, which was duplicated work and could drift out of sync.
///
/// <para>It stores footstep EVENTS — world position + the real time it was stamped + a size scale — in a
/// 24-slot ring split into a player region [0,16) and an NPC region [16,24) so townsfolk steps never evict the
/// player's. Consumers call <see cref="SampleInto"/> with THEIR OWN lifetime, which converts each
/// event's timestamp into a 0→1 age for that effect: a print can have faded from a 0.7s puddle ripple while
/// the same print is still healing in 8s of snow. That's how one set of positions feeds effects with totally
/// different durations.</para>
///
/// Zero per-frame allocation: the ring buffer is reused, the player ref is cached, and <see cref="Tick"/> is
/// frame-guarded so it runs once regardless of how many effects sample it or what script order they run in.
/// </summary>
[DisallowMultipleComponent]
public class FootstepTracker : MonoBehaviour
{
    [Tooltip("Seconds (at normal gait) between RUN footprints — the cadence of the feet, NOT a ground distance. " +
             "Stamps are paced on gait-time scaled by the player's GaitSpeedMult (exactly like the footstep SFX and the " +
             "walk/run animation fps), so a Haste/Slow churns the prints with the legs AND — crucially — a TRAVEL-only " +
             "speed change (moveSpeed) leaves the cadence alone, just like the legs, so ripples stay glued to the feet.")]
    public float strideSeconds = 0.42f;
    [Tooltip("Seconds between WALK footprints (Shift held) — wider than the run cadence since the walk cycle plants " +
             "slower. Mirrors the SFX walk/run step-interval split so prints match whichever gait is playing.")]
    public float walkStrideSeconds = 0.62f;
    [Tooltip("Sideways offset of each print from the walk line — left/right feet alternate by this (the zig-zag).")]
    public float footSpacing = 0.04f;
    [Tooltip("Forward/back nudge along the RUN direction — raise to pull prints up under the feet (less trailing).")]
    public float stepOffset = 0.07f;
    [Tooltip("Forward/back nudge for the WALK gait (Shift held) — wider than run since the walk stride reaches further.")]
    public float walkStepOffset = 0.09f;
    [Range(1f, 6f)] [Tooltip("Size multiplier of the centred splash-print stamped when the player LANDS from a jump.")]
    public float landingScale = 2.5f;
    [Range(0f, 0.6f)] [Tooltip("Gait-time delay (in stride units) held off before the FIRST ripple of a fresh walk. " +
             "0 = the old behaviour (the first print fires instantly on the first moving frame); raise it to let the leg " +
             "swing forward before the splash lands. Only delays the first step after a stop — the cadence is unchanged.")]
    public float firstStepDelay = 0.14f;

    // The ring is split into two DISJOINT regions so NPC steps can never evict the player's: slots [0,PlayerSteps)
    // are the player's own ring (its full budget, unchanged), slots [PlayerSteps,MaxSteps) are shared by all NPCs.
    // The shaders draw every slot identically, so both still ripple — they just don't compete for the same slots.
    public const int MaxSteps = 24;
    const int PlayerSteps = 16;

    /// <summary>Most-recent tracker, so effects can grab it without a hard reference.</summary>
    public static FootstepTracker Instance { get; private set; }

    // Each event: (worldX, worldZ, stampTime, sizeScale). Unstamped slots use a far-past stamp so they read dead.
    readonly Vector4[] events = new Vector4[MaxSteps];
    int head;        // player write cursor, wraps within [0, PlayerSteps)
    int npcHead = PlayerSteps;   // NPC write cursor, wraps within [PlayerSteps, MaxSteps)

    Transform player;
    Player3DController playerCtrl;
    Vector3 lastStepPos;
    Vector3 prevPos;
    float stepPhase;   // gait-scaled time accumulator: advances by GaitSpeedMult·dt while moving, fires a stamp at stride
    bool hasLast, hasPrev, wasMoving, leftFoot, wasAirborne;
    float nextFind;
    int lastTickFrame = -1;

    const float MoveEps = 0.05f;   // world u/s under which the player counts as standing still (no prints)

    /// <summary>Find the live tracker, creating one if none exists (used on demand by the effects/DevSettings).</summary>
    public static FootstepTracker Ensure()
    {
        var t = Instance != null ? Instance : FindFirstObjectByType<FootstepTracker>();
        if (t == null) t = new GameObject("FootstepTracker").AddComponent<FootstepTracker>();
        return t;
    }

    void Awake()
    {
        Instance = this;
        // Mark every slot dead (stamped far in the past) so nothing draws until the player actually steps.
        for (int i = 0; i < MaxSteps; i++) events[i] = new Vector4(0f, 0f, -1e9f, 0f);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Fill <paramref name="dest"/> (length 16) with this consumer's view of the footsteps:
    /// (worldX, worldZ, age01, sizeScale), where age = elapsed/<paramref name="life"/>. Steps older than
    /// <paramref name="life"/> come back with age ≥ 1 so the shader treats them as dead. Runs <see cref="Tick"/>
    /// first (frame-guarded), so callers don't depend on script execution order.</summary>
    public void SampleInto(Vector4[] dest, float life)
    {
        Tick();
        life = Mathf.Max(0.05f, life);
        float now = Time.time;
        for (int i = 0; i < MaxSteps; i++)
        {
            Vector4 e = events[i];
            float age = (now - e.z) / life;
            dest[i] = new Vector4(e.x, e.y, age >= 1f ? 2f : age, e.w);   // .xy world XZ, .z age, .w size
        }
    }

    void LateUpdate() { Tick(); }   // self-drive too, so the gait advances even with no consumers this frame

    // Advance the gait at most once per frame: detect strides by distance and stamp a print under the
    // alternating foot; handle jump take-off (no prints airborne) and landing (one big centred splash).
    void Tick()
    {
        if (Time.frameCount == lastTickFrame) return;
        lastTickFrame = Time.frameCount;

        if (playerCtrl == null && Time.time >= nextFind)
        {
            playerCtrl = FindFirstObjectByType<Player3DController>();
            player = playerCtrl != null ? playerCtrl.transform : null;
            nextFind = Time.time + 1f;
        }
        if (player == null) return;

        Vector3 p = player.position;

        // Airborne: feet are off the ground → no prints. Hold the stride anchors so air travel doesn't bank up
        // into a step, reset the cadence, and remember we left the ground (for the landing splash on touchdown).
        if (playerCtrl != null && playerCtrl.IsAirborne)
        {
            lastStepPos = p; prevPos = p; hasLast = true; hasPrev = true;
            wasMoving = false; stepPhase = 0f; wasAirborne = true;
            return;
        }

        // Just landed → one big, player-CENTRED splash-print (no foot offset).
        if (wasAirborne)
        {
            wasAirborne = false;
            lastStepPos = p; prevPos = p; wasMoving = false; stepPhase = 0f;
            Stamp(p.x, p.z, Mathf.Max(1f, landingScale));
            return;
        }

        if (!hasLast) { lastStepPos = p; hasLast = true; }
        if (!hasPrev) { prevPos = p; hasPrev = true; }

        // Pace the prints on GAIT-TIME, not ground distance: this is the same clock the footstep SFX and the
        // walk/run animation fps run on, so the splashes stay locked to the visibly-stepping feet. A travel-only
        // speed change (moveSpeed) raises ground speed WITHOUT touching GaitSpeedMult — and the legs keep their
        // baked cadence — so the ripples must too. (The old distance odometer fired faster the faster you slid,
        // which is exactly the drift being fixed.)
        float dt = Time.deltaTime;
        Vector3 frameDelta = p - prevPos; frameDelta.y = 0f; prevPos = p;
        float speed = dt > 1e-5f ? frameDelta.magnitude / dt : 0f;
        bool moving = speed > MoveEps;

        // Standing still resets the gait AND the foot, so every fresh walk after a pause starts on the RIGHT foot
        // (leftFoot == false → +perp). Keeps the player's first ripple consistent with the NPCs'.
        if (!moving) { wasMoving = false; stepPhase = 0f; leftFoot = false; return; }

        float gait = playerCtrl != null ? Mathf.Max(0.05f, playerCtrl.GaitSpeedMult) : 1f;
        float stride = (playerCtrl != null && playerCtrl.WalkingSlow ? walkStrideSeconds : strideSeconds);
        stride = Mathf.Max(0.02f, stride);

        // Fire immediately on the first moving frame after a pause so a step isn't swallowed; thereafter advance
        // the gait phase and stamp each time it laps the stride (keeping the remainder for a steady cadence).
        // On a FRESH walk, seed the phase short of the stride by firstStepDelay so the first ripple is held off that
        // much gait-time (0 → fires instantly, the old behaviour); thereafter the phase advances normally.
        stepPhase = wasMoving ? stepPhase + dt * gait : stride - Mathf.Clamp(firstStepDelay, 0f, stride);
        wasMoving = true;
        if (stepPhase >= stride)
        {
            stepPhase -= stride;

            // Drop the print under the LEFT or RIGHT foot, alternating, offset sideways from the walk line — so
            // the trail zig-zags like real footsteps instead of a single centred furrow.
            Vector3 dir = p - lastStepPos; dir.y = 0f;
            if (dir.sqrMagnitude <= 1e-6f) dir = frameDelta;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 perp = new Vector3(dir.z, 0f, -dir.x);                       // 90° in the ground plane
            float fwd = (playerCtrl != null && playerCtrl.WalkingSlow) ? walkStepOffset : stepOffset;
            Vector3 foot = p + dir * fwd + perp * (footSpacing * (leftFoot ? -1f : 1f));
            leftFoot = !leftFoot;

            lastStepPos = p;
            Stamp(foot.x, foot.z, 1f);
        }
    }

    /// <summary>Stamp a footstep from a NON-player walker (e.g. a town NPC) into the SAME shared ring, so
    /// wet-ground ripples and snow prints appear under townsfolk too — not just the player. NPCs run their own
    /// stride odometer (see <see cref="TownNPC"/>), so they just hand us the world position; everyone shares
    /// the one 16-slot buffer, which self-LODs to whoever stepped most recently.</summary>
    public void EmitStep(Vector3 world, float size = 1f)
    {
        events[npcHead] = new Vector4(world.x, world.z, Time.time, size);
        npcHead = PlayerSteps + (npcHead - PlayerSteps + 1) % (MaxSteps - PlayerSteps);
    }

    void Stamp(float x, float z, float size)
    {
        events[head] = new Vector4(x, z, Time.time, size);
        head = (head + 1) % PlayerSteps;
    }
}
