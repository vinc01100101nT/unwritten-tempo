using UnityEngine;

/// <summary>
/// The single SOURCE OF TRUTH for where the player's feet land. There is only one character, so the gait —
/// alternating left/right prints, the sideways zig-zag, the jump-landing splash — is computed ONCE here and
/// shared by every effect that reacts to footsteps (<see cref="WetGround"/> puddle ripples,
/// <see cref="SnowGround"/> snow prints, the footstep SFX, and anything added later). Before this existed each
/// effect re-found the player and re-ran its own stride loop, which was duplicated work and could drift out of sync.
///
/// <para><b>Detection is FRAME-LOCKED, not time-based.</b> A step fires the moment the live Walk/Run ANIMATION
/// crosses an authored foot-plant frame — the left/right plant frames are OWNED by the Walk/Run
/// <see cref="StanceBinding"/> (authored in the Stance Composer) and this only mirrors them into 0→1 cycle phases
/// (frame ÷ total frames), read against <see cref="Character3DPose.TryGetLocoPhase"/>. Because it rides the
/// animation's own clock, a Haste/Slow gait (which scales <c>anim.speed</c>, hence the animation's normalizedTime)
/// churns the prints exactly with the visible legs — with nothing to hand-sync. The old gait-TIME cadence
/// (strideSeconds) was a stand-in for this real signal and has been retired.</para>
///
/// <para>It stores footstep EVENTS — world position + the real time it was stamped + a size scale — in a
/// 24-slot ring split into a player region [0,16) and an NPC region [16,24) so townsfolk steps never evict the
/// player's. Consumers call <see cref="SampleInto"/> with THEIR OWN lifetime, which converts each
/// event's timestamp into a 0→1 age for that effect: a print can have faded from a 0.7s puddle ripple while
/// the same print is still healing in 8s of snow. That's how one set of positions feeds effects with totally
/// different durations.</para>
///
/// Zero per-frame allocation: the ring buffer is reused, the player/pose refs are cached, and <see cref="Tick"/> is
/// frame-guarded so it runs once regardless of how many effects sample it or what script order they run in.
/// </summary>
[DisallowMultipleComponent]
public class FootstepTracker : MonoBehaviour
{
    // The foot-plant frames are NOT owned here — they live on the Walk/Run StanceBinding (authored in the Stance
    // Composer's "L#/R#" button) so there's one owner of where feet land. This tracker only MIRRORS them into 0→1
    // cycle phases (frame ÷ the clip's frame count) via ResolvePhases; edit the frames in the composer, not here.
    [Tooltip("Sideways offset of each print from the walk line — left/right feet alternate by this (the zig-zag).")]
    public float footSpacing = 0.04f;
    [Tooltip("Forward/back nudge along the RUN direction — raise to pull prints up under the feet (less trailing).")]
    public float stepOffset = 0.07f;
    [Tooltip("Forward/back nudge for the WALK gait (Shift held) — wider than run since the walk stride reaches further.")]
    public float walkStepOffset = 0.09f;
    [Range(1f, 6f)] [Tooltip("Size multiplier of the centred splash-print stamped when the player LANDS from a jump.")]
    public float landingScale = 2.5f;
    [Range(0.05f, 6f)] [Tooltip("Size multiplier of each foot-plant print/splash while WALKING (Shift held).")]
    public float walkStepScale = 1f;
    [Range(0.05f, 6f)] [Tooltip("Size multiplier of each foot-plant print/splash while RUNNING.")]
    public float runStepScale = 1f;

    // The ring is split into two DISJOINT regions so NPC steps can never evict the player's: slots [0,PlayerSteps)
    // are the player's own ring (its full budget, unchanged), slots [PlayerSteps,MaxSteps) are shared by all NPCs.
    // The shaders draw every slot identically, so both still ripple — they just don't compete for the same slots.
    public const int MaxSteps = 24;
    const int PlayerSteps = 16;

    /// <summary>Most-recent tracker, so effects can grab it without a hard reference.</summary>
    public static FootstepTracker Instance { get; private set; }

    /// <summary>The ONE central footstep signal: fires (world position, size scale) every time a foot lands —
    /// player OR NPC, from the same <see cref="Stamp"/>/<see cref="EmitStep"/> that feed the ring. Effects that
    /// react per-step (splashes, VFX, …) subscribe here instead of re-running gait logic. Y is 0 (ground XZ).</summary>
    public static event System.Action<Vector3, float> Stepped;

    /// <summary>The PLAYER-ONLY step signal: fires only on the player's real foot plants (NOT NPC steps, and NOT the
    /// jump-landing splash). The footstep SFX rides this so the sound lands on the visible foot-plant frame; NPC steps
    /// don't machine-gun the player's ears. World position (Y=0) + size.</summary>
    public static event System.Action<Vector3, float> PlayerStepped;

    // Each event: (worldX, worldZ, stampTime, sizeScale). Unstamped slots use a far-past stamp so they read dead.
    readonly Vector4[] events = new Vector4[MaxSteps];
    int head;        // player write cursor, wraps within [0, PlayerSteps)
    int npcHead = PlayerSteps;   // NPC write cursor, wraps within [PlayerSteps, MaxSteps)

    Transform player;
    Player3DController playerCtrl;
    Character3DPose pose;          // owns the Animator whose Walk/Run frame we read for plant detection
    readonly Gait playerGait = new Gait();
    System.Action<Vector3, float> onPlayerPlant;   // cached (zero per-frame alloc) sink for the player's plants
    bool wasAirborne;
    float nextFind;
    int lastTickFrame = -1;

    /// <summary>Per-character gait state for frame-locked foot-plant detection — the PLAYER keeps one and every
    /// NPC keeps its own. Fed to <see cref="DetectPlants"/> each frame so the identical "the Walk/Run animation
    /// crossed an authored plant frame → stamp a step under that foot" logic drives every walker, with no
    /// per-character copy of the stride math. <see cref="Seed"/> it when the walker stops/teleports/re-enables so
    /// the next step re-seeds cleanly instead of firing a phantom print from a stale position.</summary>
    public sealed class Gait
    {
        internal float prevFrac;              // last frame's position in the loco cycle — a plant fires on crossing a plant phase
        internal Vector3 prevPos, lastStepPos;
        internal bool havePhase, hasPrev, hasLast;
        public void Reset() { havePhase = hasPrev = hasLast = false; }
        public void Seed(Vector3 p) { lastStepPos = prevPos = p; hasLast = hasPrev = true; havePhase = false; }
    }

    // Plant data MIRRORED from the Walk/Run bindings (the owner) — ONE entry per authored footfall: its 0→1 cycle
    // phase (frame ÷ clip frames) plus which foot (true = right). Rebuilt (allocated) only in ResolvePhases
    // (author-time / on refresh), never per frame. Defaults (before authoring) are a left→right pair.
    static readonly float[] DefaultRunPhase = { 0.1f, 0.6f };
    static readonly float[] DefaultWalkPhase = { 0.15f, 0.65f };
    static readonly bool[] DefaultRight = { false, true };   // first plant = left, second = right
    float[] runPhases = DefaultRunPhase;
    float[] walkPhases = DefaultWalkPhase;
    float[] sprintPhases = DefaultRunPhase;   // sprint is a fast run; defaults to the run plant pair until authored
    bool[] runRight = DefaultRight;
    bool[] walkRight = DefaultRight;
    bool[] sprintRight = DefaultRight;
    bool phasesReady;

    /// <summary>Re-read the foot-plants from the Walk/Run stance bindings (call after re-authoring them in the
    /// Stance Composer while in Play, so the change takes without a restart).</summary>
    public void RefreshPlantPhases() { phasesReady = false; }

    // Pull the foot-plants from the Walk/Run stance bindings (their single owner) and convert EACH frame to a 0→1
    // cycle phase via the clip's frame count, mirroring its authored foot alongside. If a clip/frame count can't be
    // resolved yet, or nothing is authored, fall back to a sane default pair so footsteps work before authoring.
    void ResolvePhases()
    {
        if (pose == null) return;
        Build(StanceId.Run, ref runPhases, ref runRight, DefaultRunPhase);
        Build(StanceId.Walk, ref walkPhases, ref walkRight, DefaultWalkPhase);
        Build(StanceId.Sprint, ref sprintPhases, ref sprintRight, DefaultRunPhase);
        phasesReady = true;
    }

    void Build(StanceId id, ref float[] phases, ref bool[] right, float[] fallbackPhase)
    {
        var b = StanceCatalog.Get(id);
        int total = b != null ? pose.NaturalFrameCount(b.action, b.clip) : 0;
        var plants = b != null ? b.footPlants : null;
        if (total <= 0 || plants == null || plants.Count == 0) { phases = fallbackPhase; right = DefaultRight; return; }
        phases = new float[plants.Count];
        right = new bool[plants.Count];
        for (int i = 0; i < plants.Count; i++)
        {
            var pl = plants[i];
            phases[i] = pl != null ? Mathf.Clamp01(pl.frame / (float)total) : 0f;
            right[i] = pl != null && pl.right;
        }
    }

    /// <summary>Find the live tracker, creating one if none exists (used on demand by the effects).</summary>
    public static FootstepTracker Ensure()
    {
        var t = Instance != null ? Instance : FindAnyObjectByType<FootstepTracker>();
        if (t == null) t = new GameObject("FootstepTracker") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<FootstepTracker>();
        return t;
    }

    void Awake()
    {
        Instance = this;
        onPlayerPlant = OnPlayerPlant;   // cache once so the per-frame detection path allocates no closure
        // Mark every slot dead (stamped far in the past) so nothing draws until the player actually steps.
        for (int i = 0; i < MaxSteps; i++) events[i] = new Vector4(0f, 0f, -1e9f, 0f);
    }

    // The player's plant sink: stamp under the planting foot into the player region and fire the player SFX signal.
    void OnPlayerPlant(Vector3 foot, float sizeScale) => Stamp(foot.x, foot.z, sizeScale, sfx: true);

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

    void LateUpdate() { Tick(); }   // self-drive too, so detection advances even with no consumers this frame

    // Advance detection at most once per frame: fire a print each time the Walk/Run animation crosses a foot-plant
    // frame, under the alternating foot; handle jump take-off (no prints airborne) and landing (one big centred splash).
    void Tick()
    {
        if (Time.frameCount == lastTickFrame) return;
        lastTickFrame = Time.frameCount;

        if ((playerCtrl == null || pose == null) && Time.time >= nextFind)
        {
            if (playerCtrl == null)
            {
                playerCtrl = FindAnyObjectByType<Player3DController>();
                player = playerCtrl != null ? playerCtrl.transform : null;
            }
            // Reach the PLAYER's pose through the player-only visual, NOT by searching for any Character3DPose: every
            // townsperson and shadow mob carries one too, so a bare search starts returning an NPC's pose the moment
            // ambient life spawns — and the player's own footsteps would then be driven off a stranger's walk cycle.
            // (Character3DVisual's own summary already warns about exactly this.)
            if (pose == null)
            {
                var pv = FindAnyObjectByType<Character3DVisual>();
                pose = pv != null ? pv.Pose as Character3DPose : null;
            }
            nextFind = Time.time + 1f;
        }
        if (player == null) return;

        Vector3 p = player.position;

        // Airborne: feet are off the ground → no prints. Seed the anchors so air travel doesn't skew the next print's
        // direction, and remember we left the ground (for the landing splash on touchdown). The rooted LAND-recovery
        // phase (JumpClipReleasing) already has the feet DOWN — exclude it so the splash stamps the frame of touchdown,
        // not after the landing-animation tail finishes.
        if (playerCtrl != null && playerCtrl.IsAirborne && !playerCtrl.JumpClipReleasing)
        {
            playerGait.Seed(p);
            wasAirborne = true;
            return;
        }

        // Just landed → one big, player-CENTRED splash-print (no foot offset). State-driven, not a foot plant, so it
        // stamps the VFX ring but does NOT fire PlayerStepped (no extra step SFX on touchdown).
        if (wasAirborne)
        {
            wasAirborne = false;
            playerGait.Seed(p);
            Stamp(p.x, p.z, Mathf.Max(1f, landingScale), sfx: false);
            return;
        }

        // Frame-locked foot-plant detection — the SAME shared path every NPC runs. A print fires when the walk/run
        // ANIMATION passes an authored plant frame, so the splash is glued to the visibly-planting foot at any gait
        // speed; a one-shot (attack/jump) suspends detection (TryGetLocoPhase returns false), so swinging never stamps.
        DetectPlants(pose, p, playerGait, onPlayerPlant);
    }

    /// <summary>Run the player's OWN frame-locked plant detection for any live-3D character (the player or an NPC),
    /// firing each detected foot-plant back through <paramref name="onPlant"/>(footWorld, sizeScale). This is the
    /// SINGLE source of truth for "the Walk/Run animation crossed an authored plant frame → step here, under this
    /// foot" — NPCs route through it instead of a distance odometer, so their footstep SFX and puddle ripple land on
    /// the visibly-planting foot exactly like the player's. <paramref name="charPose"/> is that character's own
    /// self-animating <see cref="Character3DPose"/>; the plant frames + foot offsets are OWNED here (shared by all).
    /// Pass a CACHED delegate for <paramref name="onPlant"/> (a field, not a lambda) to keep this zero-alloc.</summary>
    public void DetectPlants(Character3DPose charPose, Vector3 pos, Gait g, System.Action<Vector3, float> onPlant)
    {
        if (charPose == null) return;
        if (!g.hasLast) { g.lastStepPos = pos; g.hasLast = true; }
        if (!g.hasPrev) { g.prevPos = pos; g.hasPrev = true; }
        Vector3 frameDelta = pos - g.prevPos; frameDelta.y = 0f; g.prevPos = pos;

        // The gait CLIP that's actually playing (Character3DPose.Gait) decides which plants to detect against — NOT a
        // move-speed / blend threshold — so steps follow whatever Walk/Run clips are authored and their L/R frames, at
        // any speed. A one-shot (attack/jump) returns false here, so swinging in place never stamps a print.
        if (!charPose.TryGetLocoPhase(out float frac, out Character3DPose.LocoGait gait)
            || gait == Character3DPose.LocoGait.None)
        {
            g.havePhase = false;
            return;
        }
        // Need a previous sample before we can detect a crossing; seed it on the first moving frame.
        if (!g.havePhase) { g.prevFrac = frac; g.havePhase = true; return; }

        if (!phasesReady) ResolvePhases();   // mirror the authored plant frames the first time (pose is now known)
        bool sprint = gait == Character3DPose.LocoGait.Sprint;
        bool run = gait == Character3DPose.LocoGait.Run;
        bool runFamily = run || sprint;   // sprint shares run's forward nudge + print scale (it's a fast run)
        float[] phases = sprint ? sprintPhases : run ? runPhases : walkPhases;
        bool[] sides = sprint ? sprintRight : run ? runRight : walkRight;
        float stepScale = Mathf.Max(0.05f, runFamily ? runStepScale : walkStepScale);
        float fwd = runFamily ? stepOffset : walkStepOffset;
        for (int i = 0; i < phases.Length; i++)
            if (Crossed(g.prevFrac, frac, phases[i]))
            {
                Vector3 foot = FootOffset(pos, frameDelta, g.lastStepPos, i < sides.Length && sides[i], fwd);
                g.lastStepPos = pos;
                onPlant(foot, stepScale);
            }
        g.prevFrac = frac;
    }

    // Offset a print under the planting foot — forward along travel by fwd, and sideways from the walk line to the
    // AUTHORED side (right/left, not a blind alternation) so the trail zig-zags on the correct sides from the first step.
    Vector3 FootOffset(Vector3 p, Vector3 frameDelta, Vector3 lastStep, bool rightFoot, float fwd)
    {
        Vector3 dir = p - lastStep; dir.y = 0f;
        if (dir.sqrMagnitude <= 1e-6f) dir = frameDelta;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
        Vector3 perp = new Vector3(dir.z, 0f, -dir.x);                       // 90° in the ground plane (right of travel)
        return p + dir * fwd + perp * (footSpacing * (rightFoot ? 1f : -1f));
    }

    // Did the looping cycle move past plant phase p this frame? Handles the 0.98→0.02 wrap at the loop point.
    static bool Crossed(float a, float b, float p)
    {
        if (a <= b) return p > a && p <= b;   // no wrap: fired if p is in (a, b]
        return p > a || p <= b;               // wrapped past 1→0: p in (a, 1] or [0, b]
    }

    /// <summary>Stamp a footstep from a NON-player walker (e.g. a town NPC) into the SAME shared ring, so
    /// wet-ground ripples and snow prints appear under townsfolk too — not just the player. NPCs run their own
    /// stride odometer (see <see cref="TownNPC"/>), so they just hand us the world position; everyone shares
    /// the NPC region of the ring, which self-LODs to whoever stepped most recently. Fires <see cref="Stepped"/>
    /// (general VFX) but NOT <see cref="PlayerStepped"/> — NPC steps must not trigger the player's footstep SFX.</summary>
    public void EmitStep(Vector3 world, float size = 1f)
    {
        events[npcHead] = new Vector4(world.x, world.z, Time.time, size);
        npcHead = PlayerSteps + (npcHead - PlayerSteps + 1) % (MaxSteps - PlayerSteps);
        world.y = 0f;
        Stepped?.Invoke(world, size);
    }

    void Stamp(float x, float z, float size, bool sfx)
    {
        events[head] = new Vector4(x, z, Time.time, size);
        head = (head + 1) % PlayerSteps;
        Vector3 w = new Vector3(x, 0f, z);
        Stepped?.Invoke(w, size);
        if (sfx) PlayerStepped?.Invoke(w, size);
    }
}
