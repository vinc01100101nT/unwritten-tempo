using UnityEngine;

/// <summary>
/// The live-3D player visual — Animator-driven. It rides the real
/// <see cref="Player3DController"/> (snaps to its feet each frame, hides the player's own sprite) and, using the
/// SHARED <see cref="CharacterBrain"/>, decides the action stance and routes it through the single choke point
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
    int groundMask;
    Vector3 lastPlayerPos;

    // THE ONE THING THAT DECIDES A FLINCH, for everybody. The door every real hit passes through sets it, and a
    // creature's body already read it here. This used to keep its own timer instead, started from a damage event and
    // measured against the hurt clip — a second owner of one fact, on the one body where the two could disagree.
    StatusController status;

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
        UnitBody.Set(player.gameObject, measured > 1e-3f ? measured : CharacterSpec.Active.bodyHeight,
                     pose != null ? pose.VisualWorldWidth : 0f);
        attacker = player.GetComponent<Player3DAttacker>();
        hiddenPlayerSprite = player.GetComponent<SpriteRenderer>();
        if (hiddenPlayerSprite != null) hiddenPlayerSprite.enabled = false;

        status = Status.Of(player.gameObject);

        lastPlayerPos = player.transform.position;
        transform.position = GroundedPos(lastPlayerPos);
    }

    // Seat the model's feet on the Ground layer (same truth the townsfolk use). The Quaternius
    // pivot is at the feet, so grounding places the origin on the surface; a jump/fall lifts by how far the
    // player's root sits above its grounded resting height (0 at rest).
    /// <summary>HOW FAR OFF THE FLOOR THIS BODY IS, in world units — 0 while standing or walking, positive for the
    /// whole of a jump or a fall. Worked out here because this is the one place that already separates the two
    /// things a physics root's height is made of: the capsule's own resting offset, which is not height at all, and
    /// the real lift above the ground.
    ///
    /// <para>It is READ by the position report, because no other machine can work it out. A watched copy of this
    /// person has no physics capsule to subtract, so a raw height told to it would leave everybody permanently
    /// hovering by the capsule's offset — and deriving it there instead would be a second owner of a number this
    /// one already holds.</para></summary>
    public float HeightAboveGround { get; private set; }

    Vector3 GroundedPos(Vector3 p)
    {
        if (cc == null) return p;
        if (GroundSnap.TryGroundY(p, groundMask, out float groundY))
        {
            float lossy = player != null ? player.transform.lossyScale.y : 1f;
            float restingRootY = groundY + cc.skinWidth - (cc.center.y - cc.height * 0.5f) * lossy;
            float above = Mathf.Max(0f, p.y - restingRootY);
            HeightAboveGround = above;
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

        // WHAT THIS BODY IS DOING, said in the plain terms every live-3D character is described in. This is the only
        // part of the whole decision that belongs to the person at the keyboard: it is read off their input, their
        // weapon and their traversal, and everything after it is the shared brain that a townsperson and a creature
        // wearing the same rig also run.
        var it = new CharacterBrain.Intent
        {
            facing        = player.WorldFacing,
            groundSpeed   = speed,
            sprinting     = player.Sprinting,
            walkingSlow   = player.WalkingSlow,
            dead          = Character.Instance != null && Character.Instance.IsDead,
            falling       = player.IsFalling,
            swinging      = attacker != null && attacker.Swinging,
            swing         = WeaponStances.AttackFor(EquippedWeapon(), Character.Instance != null && Character.Instance.SwingsRanged),
            swingWindow   = attacker != null ? attacker.SwingTime : 0f,
            airborne      = player.IsAirborne,
            airborneFreeze    = player.JumpClipFreeze,
            airborneReleasing = player.JumpClipReleasing,
            gliding       = player.SkillGliding,
            dashing       = player.Dashing || player.EDashing,
            dashWindow    = player.EDashing ? player.eDashDuration : 0.4f,
            flinching     = status != null && status.MovementFlinched,
            flinchCount   = status != null ? status.FlinchCount : 0,
        };

        // A full 3D controller always has run and jump beats — that capability is structural, not a clip. Whether a
        // DISTINCT sprint clip exists is asked of the one place that knows.
        var d = CharacterBrain.Decide(it, CharacterBrain.WalkThreshold, hasRun: true, hasJump: true,
                                      hasSprint: CharacterBrain.HasSprintClip());
        CharacterBrain.Apply(stance, pose, d);
    }

    // The equipped weapon TYPE (None when unarmed) — picks which swing this body plays. Reads the single owner.
    static WeaponType EquippedWeapon()
    {
        var it = Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
        return it != null ? it.weaponType : WeaponType.None;
    }

    void OnDestroy()
    {
        if (hiddenPlayerSprite != null) hiddenPlayerSprite.enabled = true;
    }
}
