using UnityEngine;

/// <summary>
/// The shared animation driver for any live-3D character moved by a <see cref="PathAgent3D"/> — the AI twin of
/// <see cref="Character3DVisual"/> (which reads player input instead). It owns the three things EVERY agent-driven
/// character does identically: pick Walk vs Idle from the mover, yaw the model toward travel, and seat the feet on
/// the ground.
///
/// <para>Subclasses add only the vocabulary their role has, by overriding <see cref="DriveRoleAction"/>: a
/// townsperson has none (it only ever walks or stands — <see cref="TownNPC3DVisual"/>), a mob adds attack / hurt /
/// die (<see cref="MobShadow3DVisual"/>). The locomotion, facing and grounding below are NEVER re-implemented per
/// role — that split is what let the NPC and player footstep paths drift apart before, and this is the same
/// "one function per behaviour" contract applied to the visual layer.</para>
///
/// <para>Everything routes through the same choke point (<see cref="StanceController"/>) and render sink
/// (<see cref="Character3DPose"/>) the player uses, so an AI character animates through identical machinery.
/// Runs at −100 (before StanceController's −50) so its intent is fed first. Sibling refs cached in Awake;
/// nothing here allocates.</para>
/// </summary>
[DefaultExecutionOrder(-100)]
public abstract class Agent3DVisual : MonoBehaviour
{
    // NO RequireComponent for the mover HERE, on purpose — each role that HAS one declares it itself (the
    // townsperson and the shadow mob both do). A remote player is walked by the server rather than steered by a
    // mover, and requiring one would silently bolt a pathfinder onto a body that must never route anywhere.
    protected PathAgent3D agent;
    protected StanceController stance;
    protected Character3DPose pose;
    protected Transform model;    // the scaled visual model child (its origin is the feet pivot)

    // What counts as walking for a body with no mover to ask, in units per second. Well above the crawl a body
    // makes while easing the last centimetre onto a position it has nearly reached, well below a walk.
    const float MinTravelSpeed = 0.05f;

    // ---- the server's word about this body -------------------------------------------------------------
    // A body the server PLACES is TOLD two things rather than asked for them: how fast it is travelling, and what
    // action it began. Whatever is following the server for this body sets them.

    float toldGroundSpeed;
    bool speedIsTold;

    // The beat last played from the server's word, by the moment it began. The beat is a VALUE that is re-read
    // every frame rather than an event that fires once, so without this it would restart on every frame it held.
    double playedActionAt = double.NegativeInfinity;

    // THE POSE THIS BODY IS HOLDING, as the machine that owns it says. Separate from the beat above because the two
    // are different shapes: a beat plays once and finishes (a swing), while this LASTS until it is replaced (a jump
    // frozen in the air, a dodge, a flinch, a body lying dead). False means "nothing special" and the body falls back
    // to walking or standing as its speed says.
    protected bool toldHasPose;
    protected StanceId toldPose;
    protected float toldPoseWindow;
    protected bool toldPoseRelease;

    /// <summary>Which travel gear this body is in, as a fact rather than a guess. Speed alone cannot tell a sprint
    /// from a hasted run — both are simply fast.</summary>
    protected bool toldSprinting;

    // HOW FAR OFF THE FLOOR this body is, in world units. 0 for everything that walks — a townsperson, a creature,
    // a person standing or running — and positive only while somebody is in the air.
    float toldAbove;

    /// <summary>The owning machine's word on how far this body is off the floor. Told rather than measured for the
    /// same reason its speed is: a watched copy has no physics capsule, so it cannot tell the part of a reported
    /// height that is real lift from the part that is just where the capsule sits at rest.</summary>
    public void TellHeightAboveGround(float above) { toldAbove = above > 0f ? above : 0f; }

    /// <summary>The owning machine's word on what pose this body is holding, and which travel gear it is in. Call it
    /// every frame this body is being followed; a role that acts on it reads the fields above.
    ///
    /// <para>TOLD rather than worked out, for the same reason the speed above is: almost everything a person's body
    /// does is decided on their own machine and nothing else on the wire describes it, so a body nobody told would
    /// walk, swing, and do nothing else for the whole of its life.</para></summary>
    public void TellPose(bool has, StanceId pose, float window, bool release, bool sprinting)
    {
        toldHasPose = has;
        toldPose = pose;
        toldPoseWindow = window;
        toldPoseRelease = release;
        toldSprinting = sprinting;
    }

    /// <summary>The server's word on how fast this body is travelling on the ground, in units per second (0 =
    /// standing). Call it every frame this body is being followed.
    ///
    /// <para>TOLD rather than measured because measuring is wrong here, and measurably so: positions arrive ten
    /// times a second and <see cref="WatchedBody"/> closes 12/second of the gap toward them, so within each 100 ms
    /// gap the apparent speed decays to about 30% of the real one and jumps back when the next position lands —
    /// a threefold swing, ten times a second, straight across the walk/run threshold. A body that STOPS reads as
    /// still walking for about a third of a second.</para></summary>
    public void TellTravelSpeed(float groundSpeed)
    {
        toldGroundSpeed = groundSpeed;
        speedIsTold = true;
    }

    /// <summary>The server's word that this body BEGAN an action: which one, the moment it began on the clock both
    /// machines share, and the beat it has to finish inside. Safe to call every frame — one beat plays once.
    ///
    /// <para><paramref name="beatSeconds"/> is NOT the length of the animation. The server owns the beat — for a
    /// creature's bite, the interval until its next one, with its attack speed already folded in — and the art
    /// owns how long the swing takes. So the clip plays at its own natural length, capped so it cannot still be
    /// running when the next beat lands. Neither side has to know the other's number, and a hasted unit's swing
    /// shortens by itself.</para>
    ///
    /// <para>A beat that began before this body started watching is JOINED part-way through rather than skipped,
    /// however little of it is left. That is a decision, not an oversight: a body standing still while its damage
    /// number climbs is worse than a swing showing only its last frames.</para></summary>
    public void TellAction(StanceId action, double startedAt, float beatSeconds)
    {
        if (stance == null || startedAt <= playedActionAt) return;
        playedActionAt = startedAt;

        float natural = stance.ActionDuration(action);
        float play = beatSeconds > 0f ? Mathf.Min(natural, beatSeconds) : natural;
        if (play <= 0f) return;

        float elapsed = (float)(Mirror.NetworkTime.time - startedAt);
        stance.PlayActionFor(action, play, startedSecondsAgo: Mathf.Max(0f, elapsed));
    }
    int groundMask;
    Vector3 lastPos;
    bool seated;   // the feet have been placed at least once; a still unit then needs no further ground ray

    protected virtual void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        stance = GetComponent<StanceController>();
        pose = GetComponent<Character3DPose>() ?? GetComponentInChildren<Character3DPose>();
        model = pose != null ? pose.transform : null;
        int gl = LayerMask.NameToLayer("Ground");
        groundMask = gl >= 0 ? (1 << gl) : 0;
        lastPos = transform.position;
    }

    /// <summary>Coming back after a freeze (or a pooled reuse) resumes with a <see cref="lastPos"/> from wherever this
    /// unit stood when it stopped — and it may have been moved a long way in between. Re-baseline both facts: without
    /// the position reset the first frame back reads one enormous phantom step, which flashes the Run stance and snaps
    /// the model's facing along the jump; without clearing <see cref="seated"/> a unit recycled to a NEW spot would
    /// think its feet were already placed and never re-seat on the ground it actually landed on.</summary>
    protected virtual void OnEnable()
    {
        lastPos = transform.position;
        seated = false;
        // A POOLED BODY COMES BACK AS SOMEBODY ELSE. It may be handed to a different unit entirely, so last life's
        // told speed would have it walking on the spot, and last life's played beat would swallow the new unit's
        // first swing if that swing happened to be stamped earlier on the clock.
        speedIsTold = false;
        toldGroundSpeed = 0f;
        playedActionAt = double.NegativeInfinity;
        // …and last life's pose with them, or a body handed back out of the pool would come back mid-roll, or lying
        // dead, until whoever is following it got round to saying otherwise.
        toldHasPose = false;
        toldPoseRelease = false;
        toldPoseWindow = 0f;
        toldSprinting = false;
        toldAbove = 0f;
        // …and the leg cadence it was told, which lives on the stance choke point rather than here. Nothing pools a
        // watched PERSON today (their body is destroyed outright), so this changes nothing now — it is here so that
        // the day one is pooled, it cannot come back walking at the last person's cadence.
        if (stance != null) stance.TellGait(0f);
    }

    /// <summary>This body's pose sink — the same answer <see cref="Character3DVisual.Pose"/> gives for the avatar at
    /// this keyboard, under the same name, so anything that hangs something over a head (a speech bubble) asks one
    /// question of every character rather than one per kind of character. Null before the body is built.</summary>
    public IStancePose Pose => pose;

    /// <summary>Re-resolve the pose sink after the body is built at runtime (the model child doesn't exist yet when
    /// Awake runs on a mob that becomes a shadow mid-spawn).</summary>
    public void BindBody(Character3DPose builtPose)
    {
        pose = builtPose;
        model = pose != null ? pose.transform : null;
        if (stance == null) stance = GetComponent<StanceController>();
        seated = false;   // a brand-new body has never been placed on the ground, even if this unit hasn't moved
    }

    /// <summary>Drive a role-specific action stance this frame (attack / hurt / die). Return true to claim the
    /// frame — locomotion and facing are then skipped, because the role action outranks walking. Default: no
    /// role vocabulary at all.</summary>
    protected virtual bool DriveRoleAction() => false;

    /// <summary>"AM I WALKING, AND HOW FAST?" — the one question, answered from the best source this body has.
    ///
    /// <para>Three answers, in order of authority. TOLD: the server said, and that beats everything (see
    /// <see cref="TellTravelSpeed"/>). STEERED: a mover that is actually running knows, and the ground it covered
    /// gives the speed. NEITHER: a body that is placed with nothing to ask reads its own footfall.</para>
    ///
    /// <para>The middle case asks whether the mover is RUNNING, not whether it exists. A body the server places
    /// keeps its mover as a switched-off component, and a switched-off mover answers "not moving" forever — which
    /// is why a watched creature slid around in its standing pose.</para>
    ///
    /// <para>The square root is paid only by a body that is actually moving; a standing crowd compares squares.</para></summary>
    protected virtual bool Travelling(Vector3 groundTravel, float dt, out float groundSpeed)
    {
        if (speedIsTold)
        {
            groundSpeed = toldGroundSpeed;
            return toldGroundSpeed > MinTravelSpeed;
        }

        bool moving = agent != null && agent.isActiveAndEnabled
            ? agent.Moving
            : groundTravel.sqrMagnitude > MinTravelSpeed * MinTravelSpeed * dt * dt;

        groundSpeed = moving ? groundTravel.magnitude / dt : 0f;
        return moving;
    }

    /// <summary>Which locomotion gear to play at this measured ground speed. Base = always Walk, because a
    /// strolling townsperson has exactly one gear. A body that has the player's TWO gears overrides it with
    /// <see cref="PlayersGearFor"/>.</summary>
    protected virtual StanceId LocomotionStanceFor(float groundSpeed) => StanceId.Walk;

    /// <summary>Walk, Run or Sprint, chosen the way the PLAYER's own three gears are chosen — the thresholds read
    /// from the one place that holds them, so a body standing in for a player changes gear at the same speed the
    /// player does. Written once here because more than one role stands in for a player: the shadow that copies
    /// them, and another person's body watched over the wire.
    ///
    /// <para><paramref name="sprinting"/> is the sprint gear as a FACT, never a guess — and there is deliberately
    /// no speed threshold for it. A hasted run and a sprint are both simply fast, so a threshold would draw a
    /// speed-buffed jog as a sprint and there is no number that could tell them apart. The machine that owns the
    /// legs says which gear it is in; a body nobody has said anything about (a shadow copying you) never sprints,
    /// which is exactly what it did before.</para></summary>
    protected static StanceId PlayersGearFor(float groundSpeed, bool sprinting = false)
    {
        if (sprinting) return StanceId.Sprint;
        return groundSpeed >= CharacterSpec.Active.GearSwitchSpeed ? StanceId.Run : StanceId.Walk;
    }

    /// <summary>Which way this body should look this frame, or a zero vector for "leave it as it is".
    ///
    /// <para>Base answer: the line it just travelled — or, mid-glide, the point the glide named instead, because a
    /// hop that ends in a strike has to arrive already looking at what it will hit. A body that is not walking
    /// keeps whatever facing it had, which is what a standing townsperson has always done.</para>
    ///
    /// <para>A role whose facing comes from somewhere OTHER than its own travel overrides this — a body that is
    /// placed by a server is told which way it faces, and is told that even while it stands still.</para></summary>
    protected virtual Vector3 FacingThisFrame(Vector3 groundTravel, bool walking, Vector3 position)
    {
        if (!walking) return Vector3.zero;
        if (agent != null && agent.GlideFaceAt.HasValue)
        {
            Vector3 look = agent.GlideFaceAt.Value - position; look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) return look;
        }
        return groundTravel;
    }

    protected virtual void LateUpdate()
    {
        if (stance == null) return;

        // Travel this frame (LateUpdate runs after PathAgent3D's move). `moved` keeps the VERTICAL component so a
        // falling unit still re-seats; `d` drops it, because facing is a ground direction.
        Vector3 p = transform.position;
        Vector3 moved = p - lastPos;
        Vector3 d = moved; d.y = 0f;
        lastPos = p;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        bool walking = Travelling(d, dt, out float groundSpeed);

        if (!DriveRoleAction())
        {
            if (walking) stance.DriveStance(LocomotionStanceFor(groundSpeed));
            else stance.ClearAction();   // fall through to the authored Idle stance (selfDrive = false)

            // Facing is asked EVERY frame, not only while walking, because which way a body looks and whether it is
            // walking are two different questions — a person can stand their ground and turn. The default answer
            // below still says "nothing" while standing, so a townsperson behaves exactly as it always has.
            //
            // Pass a UNIT direction, not the raw per-frame delta (~0.01 long): Character3DPose only yaws when the
            // facing's sqrMagnitude clears 1e-4, and a tiny delta sits below that gate so the model never turns.
            Vector3 face = FacingThisFrame(d, walking, p);
            if (face.sqrMagnitude > 1e-8f && pose != null) pose.SetFacing(face.normalized);
        }

        // Seat the feet: share the player's ground raycast (GroundSnap — the single owner of "how high is the floor
        // here"), then pin the model origin (feet pivot) onto the hit. The player instead seats its physics root
        // (CharacterController + jump lift) — same raycast, different rig, so only the raycast is shared.
        //
        // ONLY WHEN THE UNIT ACTUALLY MOVED (or hasn't been seated yet). A standing unit's foot height is already
        // correct and the ground doesn't move, so re-casting for it was a physics ray per idle character per frame —
        // the exact cost GroundSnap's own contract says callers must avoid ("callers gate it to MOVING units... so a
        // still crowd costs nothing"). This caller was the one not honouring it, across a town of standing NPCs.
        // AND A BODY CAN BE OFF THE FLOOR. Pinning the feet ONTO the ground is right for everything that walks, and
        // wrong for the one role that can leave it: a person's watched copy is placed wherever the server says they
        // are, so a jumping player's copy rose at the root and had its visible model slammed back down every frame —
        // they jumped on their own screen and hopped nowhere on anybody else's. The lift is TOLD (0 for a
        // townsperson, a creature, and anybody standing), so nothing that walks changes by a hair.
        //
        // The raycast is still gated on having moved — a body in the air is moving every frame, so an airborne one
        // re-seats; a still crowd standing on the floor costs nothing, exactly as before.
        if (model != null && (!seated || moved.sqrMagnitude > 1e-10f)
            && GroundSnap.TryGroundY(transform.position, groundMask, out float groundY))
        {
            model.position = new Vector3(model.position.x, groundY + toldAbove, model.position.z);
            seated = true;
        }
    }
}
