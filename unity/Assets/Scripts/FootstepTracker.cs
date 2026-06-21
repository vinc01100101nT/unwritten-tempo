using UnityEngine;

/// <summary>
/// The single SOURCE OF TRUTH for where the player's feet land. There is only one character, so the gait —
/// alternating left/right prints, the sideways zig-zag, the jump-landing splash — is computed ONCE here and
/// shared by every effect that reacts to footsteps (<see cref="WetGround"/> puddle ripples,
/// <see cref="SnowGround"/> snow prints, and anything added later). Before this existed each effect re-found
/// the player and re-ran its own stride loop, which was duplicated work and could drift out of sync.
///
/// <para>It stores footstep EVENTS — world position + the real time it was stamped + a size scale — in a
/// 16-slot ring buffer. Consumers call <see cref="SampleInto"/> with THEIR OWN lifetime, which converts each
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
    [Tooltip("How far the player walks (world units) between footprints — the GAP between steps. Lower = tighter, " +
             "more frequent prints. Shared by puddle ripples and snow prints (one character, one gait).")]
    public float strideDist = 0.30f;
    [Tooltip("Sideways offset of each print from the walk line — left/right feet alternate by this (the zig-zag).")]
    public float footSpacing = 0.08f;
    [Tooltip("Forward/back nudge along the walk direction — raise to pull prints up under the feet (less trailing).")]
    public float stepOffset = 0.2f;
    [Range(1f, 6f)] [Tooltip("Size multiplier of the centred splash-print stamped when the player LANDS from a jump.")]
    public float landingScale = 2.5f;

    public const int MaxSteps = 16;

    /// <summary>Most-recent tracker, so effects can grab it without a hard reference.</summary>
    public static FootstepTracker Instance { get; private set; }

    // Each event: (worldX, worldZ, stampTime, sizeScale). Unstamped slots use a far-past stamp so they read dead.
    readonly Vector4[] events = new Vector4[MaxSteps];
    int head;

    Transform player;
    Player3DController playerCtrl;
    Vector3 lastStepPos;
    bool hasLast, leftFoot, wasAirborne;
    float nextFind;
    int lastTickFrame = -1;

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

        // Airborne: feet are off the ground → no prints. Hold the stride anchor so air travel doesn't bank up
        // into a step, and remember we left the ground (for the landing splash on touchdown).
        if (playerCtrl != null && playerCtrl.IsAirborne)
        {
            lastStepPos = p; hasLast = true; wasAirborne = true;
            return;
        }

        // Just landed → one big, player-CENTRED splash-print (no foot offset).
        if (wasAirborne)
        {
            wasAirborne = false;
            lastStepPos = p;
            Stamp(p.x, p.z, Mathf.Max(1f, landingScale));
            return;
        }

        if (!hasLast) { lastStepPos = p; hasLast = true; return; }

        Vector3 delta = p - lastStepPos;
        if (delta.sqrMagnitude >= strideDist * strideDist)
        {
            // Drop the print under the LEFT or RIGHT foot, alternating, offset sideways from the walk line — so
            // the trail zig-zags like real footsteps instead of a single centred furrow.
            Vector3 dir = delta; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 perp = new Vector3(dir.z, 0f, -dir.x);                       // 90° in the ground plane
            Vector3 foot = p + dir * stepOffset + perp * (footSpacing * (leftFoot ? -1f : 1f));
            leftFoot = !leftFoot;

            lastStepPos = p;
            Stamp(foot.x, foot.z, 1f);
        }
    }

    void Stamp(float x, float z, float size)
    {
        events[head] = new Vector4(x, z, Time.time, size);
        head = (head + 1) % MaxSteps;
    }
}
