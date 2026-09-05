using UnityEngine;

/// <summary>
/// The live-3D player visual — Animator-driven. It rides the real
/// <see cref="Player3DController"/> (snaps to its feet each frame, hides the player's own sprite) and, using the
/// SHARED <see cref="PlayerStanceBrain"/>, decides the action stance and routes it through the single choke point
/// <see cref="StanceController"/> — which drives this object's <see cref="Character3DPose"/> (an
/// <see cref="IStancePose"/>). Movement/combat stay the real controller's; this is purely presentation.
///
/// <para>Runs at −100 (before StanceController −50 and the Animator) so its intent is fed first — same ordering
/// contract as the billboard brain. Wire it with <c>Tools ▸ unwritten ▸ Player ▸ Use 3D Character</c>; reversible.</para>
/// </summary>
[DefaultExecutionOrder(-100)]
public class Character3DVisual : MonoBehaviour
{
    [Tooltip("The real player to ride. Auto-found at Start if empty.")]
    public Player3DController player;
    [Tooltip("Ground speed (units/sec) above which the player counts as moving.")]
    public float walkThreshold = 0.12f;
    [Tooltip("Extra world-Y offset applied ONLY while a Mixamo one-shot clip plays (Quaternius clips unaffected). " +
             "Negative sinks the model so the Mixamo rig's stretched foot hides UNDER the floor, like Mixamo's own " +
             "preview. Tune live in Play; one value grounds every Mixamo clip because they share one baked baseline.")]
    public float mixamoFootOffsetY = 0f;

    StanceController stance;
    Character3DPose pose;
    /// <summary>The player's stance pose (feet-seated) — the anchor for its overhead chat bubble, so chat sits at the
    /// same head clearance as the NPCs' barks. This visual is unique to the player, so it's the reliable way to reach
    /// the PLAYER's pose (a bare Character3DPose search could hit a townsperson once NPCs spawn).</summary>
    public IStancePose Pose => pose;
    Player3DAttacker attacker;
    CharacterController cc;
    SpriteRenderer hiddenPlayerSprite;
    Character subscribed;
    int groundMask;
    Vector3 lastPlayerPos;
    float hurtUntil;
    bool wasDead;

    void Awake()
    {
        stance = GetComponent<StanceController>();
        if (stance == null) stance = gameObject.AddComponent<StanceController>();
        stance.BecomePlayer();                 // register as StanceController.Player + hold Idle (no self-drive)
        pose = GetComponent<Character3DPose>();
        if (pose == null) pose = GetComponentInChildren<Character3DPose>();
    }

    void Start()
    {
        if (player == null) player = FindAnyObjectByType<Player3DController>();
        if (player == null) { GameLog.Warn(LogCat.Unwritten, "Character3DVisual found no Player3DController."); return; }

        cc = player.GetComponent<CharacterController>();
        int gl = LayerMask.NameToLayer("Ground");
        groundMask = gl >= 0 ? (1 << gl) : 0;

        // Tell the world how tall the player ACTUALLY is. Its root carries a build scale (0.7) that has nothing to do
        // with its body size (the model is a separately-fitted object), so anchors derived from that root put the
        // player's mid-body at 0.28 instead of 0.164 — which is where a ranged mob was aiming. Prefer the body's own
        // measured height over the authored one so the two can't drift.
        float measured = pose != null ? pose.VisualWorldHeight : 0f;
        UnitBody.Set(player.gameObject, measured > 1e-3f ? measured : CharacterSpec.Active.bodyHeight);
        attacker = player.GetComponent<Player3DAttacker>();
        stance.SetGaitSource(player);
        hiddenPlayerSprite = player.GetComponent<SpriteRenderer>();
        if (hiddenPlayerSprite != null) hiddenPlayerSprite.enabled = false;

        subscribed = Character.Instance;
        if (subscribed != null) subscribed.OnDamaged += OnDamaged;

        lastPlayerPos = player.transform.position;
        transform.position = GroundedPos(lastPlayerPos);
    }

    void OnDamaged(int dmg)
    {
        // How long the flinch reads for comes from the CLIP, not a number typed here — the same length the movement
        // stop uses, so what you see and how long you're pinned can never drift apart.
        if (stance != null && (Character.Instance == null || !Character.Instance.IsDead))
            hurtUntil = Time.time + stance.ActionDuration(StanceId.Hurt);
    }

    // Seat the model's feet on the Ground layer (same truth the townsfolk use). The Quaternius
    // pivot is at the feet, so grounding places the origin on the surface; a jump/fall lifts by how far the
    // player's root sits above its grounded resting height (0 at rest).
    Vector3 GroundedPos(Vector3 p)
    {
        if (cc == null) return p;
        if (GroundSnap.TryGroundY(p, groundMask, out float groundY))
        {
            float lossy = player != null ? player.transform.lossyScale.y : 1f;
            float restingRootY = groundY + cc.skinWidth - (cc.center.y - cc.height * 0.5f) * lossy;
            float above = Mathf.Max(0f, p.y - restingRootY);
            p.y = groundY + above;
            return p;
        }
        p.y += cc.center.y - cc.height * 0.5f;
        return p;
    }

    void LateUpdate()
    {
        if (player == null || pose == null) return;

        Vector3 pos = player.transform.position;
        Vector3 grounded = GroundedPos(pos);
        if (pose.CurrentIsMixamo) grounded.y += mixamoFootOffsetY;
        transform.position = grounded;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float speed = (pos - lastPlayerPos).magnitude / dt;
        lastPlayerPos = pos;

        // Death overrides everything: Die plays once and holds its final (lying) frame (non-looping state).
        bool dead = Character.Instance != null && Character.Instance.IsDead;
        if (dead) { stance.CancelActionOverride(); stance.DriveStance(StanceId.Die); wasDead = true; return; }
        if (wasDead) { stance.ClearAction(); wasDead = false; }

        // Shared brain: same decision the billboard uses. A full 3D controller always supports Run/Jump beats (the
        // clip a beat plays is authored per-stance in the Stance Composer; capability here is structural, not clip).
        // hasSprint = a distinct Sprint clip is authored; otherwise the brain sprints on the Run clip (the old
        // full-speed run "moved" to the Sprint gear). StanceCatalog.Get is a cheap array index — no per-frame alloc.
        var sprintBinding = StanceCatalog.Get(StanceId.Sprint);
        bool hasSprint = sprintBinding != null && !string.IsNullOrEmpty(sprintBinding.clip);
        var d = PlayerStanceBrain.Decide(player, attacker, speed, walkThreshold, hurtUntil,
                                         hasRun: true, hasJump: true, hasSprint: hasSprint);
        if (d.hasAction) stance.DriveStance(d.id, d.window, d.release);
        else stance.ClearAction();
        if (d.applyFacing) pose.SetFacing(d.facing);
    }

    void OnDestroy()
    {
        if (subscribed != null) subscribed.OnDamaged -= OnDamaged;
        if (hiddenPlayerSprite != null) hiddenPlayerSprite.enabled = true;
    }
}
