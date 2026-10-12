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
    const float MinTravelSpeed = CharacterBrain.WalkThreshold;

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
    protected int toldPoseBeat;

    /// <summary>Which travel gear this body is in, as a fact rather than a guess. Speed alone cannot tell a sprint
    /// from a hasted run — both are simply fast.</summary>
    protected bool toldSprinting;

    // HOW FAR OFF THE FLOOR this body is, in world units. 0 for everything that walks — a townsperson, a creature,
    // a person standing or running — and positive only while somebody is in the air.
    float toldAbove;

    // The held pose last put on this body, so the push and the pop happen once each rather than every frame.
    bool showingHeld;
    StanceId shownHeld;

    /// <summary>The standing pose this body is holding, as the machine that owns it says — guarding, casting, sitting.
    /// Pushed onto the body's own stance choke point, the same layer the owner holds it on, so both resolve it against
    /// an action the identical way. ONE door for a watched person and a watched creature alike.
    ///
    /// <para>Pushed every frame rather than once: it is idempotent, it refreshes the window a fitted pose is stretched
    /// to, and it is what puts the pose on a body that finished loading after the pose had already started. A
    /// different one means the old one comes off first — held poses are pushed and popped by name, and a body left
    /// holding both lets the higher-ranking one win forever.</para></summary>
    public void TellHeld(bool has, StanceId id, float window)
    {
        if (stance == null) return;
        if (showingHeld && (!has || shownHeld != id)) { stance.Pop(shownHeld); showingHeld = false; }
        if (!has) return;
        stance.Push(id, window);
        showingHeld = true;
        shownHeld = id;
    }

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
    public void TellPose(bool has, StanceId pose, float window, bool release, int beat, bool sprinting)
    {
        toldHasPose = has;
        toldPose = pose;
        toldPoseWindow = window;
        toldPoseRelease = release;
        toldPoseBeat = beat;
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
        bool capped = beatSeconds > 0f && beatSeconds < natural;
        float play = capped ? beatSeconds : natural;
        if (play <= 0f) return;

        // A CAPPED PLAY MUST BE SPED UP TO FIT, whatever speed the stance normally takes. Without the fit, a stance
        // paced by its own gait (every skill release) ignored the window entirely and ran at its natural length —
        // so a watched Onslaught swung at normal pace while its damage numbers landed at the run's fast beat, and
        // each new beat restarted the clip before it finished. The caster's own screen always fit it.
        float elapsed = (float)(Mirror.NetworkTime.time - startedAt);
        stance.PlayActionFor(action, play, fitToWindow: capped, startedSecondsAgo: Mathf.Max(0f, elapsed));
    }
    int groundMask;
    Vector3 lastPos;
    bool seated;   // the feet have been placed at least once; a still unit then needs no further ground ray

    // FOOTSTEPS, for every body this drives. Fired the instant this body's OWN walk or run animation crosses an
    // authored foot-plant frame — never a per-second timer — so the steps are glued to the feet you can see planting,
    // at any speed, with nothing to retune. This used to be written out twice, once on a townsperson and once on a
    // creature, in two places that had to be kept in step by hand.
    // Per-body state and a cached sink, so the per-frame detection allocates nothing.
    readonly FootstepTracker.Gait gait = new FootstepTracker.Gait();
    System.Action<Vector3, float> onFootPlant;

    /// <summary>THIS BODY WAS PUT SOMEWHERE ELSE — spawned, revived, handed back out of a pool, or thawed after being
    /// frozen far away. Baseline everything that measures against where it was standing, or the first frame back
    /// reads one enormous stride: a phantom footstep from wherever it used to be, and feet placed on ground it is no
    /// longer over.</summary>
    public void ReseatAfterMove()
    {
        gait.Seed(transform.position);
        lastPos = transform.position;
        seated = false;
    }

    // One foot-plant: feed the shared ring (wet-ground ripples and snow prints under everybody), then play the shared
    // footstep clip POSITIONALLY so you hear WHERE the step happened — the engine fades distant ones, so a busy
    // plaza is not a wall of stomps. Swaps in the splash step over a puddle, the same test the player uses. The
    // sounds come from the shared character set, not off the player object, so a town has footsteps whether or not
    // anybody is playing. Zero-GC: the puddle test is a single fbm, and the voice and ripple slot are pooled.
    void OnFootPlant(Vector3 foot, float sizeScale)
    {
        FootstepTracker.Instance?.EmitStep(foot, sizeScale);
        var spec = CharacterSpec.Active;
        if (spec.footstepSfx == null || spec.footstepSfx.Empty) return;
        bool onWater = spec.waterFootstepSfx != null && !spec.waterFootstepSfx.Empty
                       && WetGround.Instance != null && WetGround.Instance.IsPuddleAt(foot);
        Sfx.PlayAt(onWater ? spec.waterFootstepSfx : spec.footstepSfx, foot);
    }

    protected virtual void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        stance = GetComponent<StanceController>();
        pose = GetComponent<Character3DPose>() ?? GetComponentInChildren<Character3DPose>();
        model = pose != null ? pose.transform : null;
        int gl = LayerMask.NameToLayer("Ground");
        groundMask = gl >= 0 ? (1 << gl) : 0;
        lastPos = transform.position;
        onFootPlant = OnFootPlant;   // cached once → the per-frame detection allocates no closure
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
        // A BODY HANDED BACK OUT OF A POOL COMES BACK SOMEWHERE ELSE, so last life's position would read as one
        // enormous step on its first frame and snap this unit to face along that jump. Re-baselined for the same
        // reason lastPos above is, and at the same moment.
        serverPosSeeded = false;
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
        if (stance != null) stance.TellLocomotionAnim(0f);
        gait.Seed(transform.position);
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

    /// <summary>ADD WHAT ONLY THIS KIND OF BODY KNOWS to the description the shared brain is about to read — which
    /// way it wants to look, whether it is swinging, flinching or dead. The base has already filled in everything
    /// that is true of EVERY body: how fast it is travelling, which gear it is in, and the line it travelled, which
    /// is the facing a body wants when it has no opinion of its own.
    ///
    /// <para>This is the ONLY thing a kind of body is allowed to own. Every rule that turns this description into a
    /// pose and a direction lives in <see cref="CharacterBrain"/> and is the same one the person at the keyboard
    /// runs. There used to be three of those rules — one per kind — and they drifted.</para></summary>
    protected virtual void DescribeSelf(ref CharacterBrain.Intent it) { }

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

    /// <summary>THE WHOLE DESCRIPTION of what this body is doing this frame — everything true of every body, then
    /// whatever only this kind knows. One function, so a creature and a townsperson cannot be described differently.
    ///
    /// <para>The travel line is the FACING a body falls back on, and it is only offered while the body is actually
    /// moving: a body standing still with no opinion keeps whatever facing it had, which is what a standing
    /// townsperson has always done. Mid-glide the glide's own aim point is offered instead, because a hop that ends
    /// in a strike has to arrive already looking at what it will hit.</para>
    ///
    /// <para>The gear is TOLD, never guessed from the speed: a hasted run and a sprint are both simply fast, and no
    /// threshold could tell them apart. Whichever machine owns the legs says which gear they are in.</para></summary>
    CharacterBrain.Intent DescribeThisFrame(Vector3 groundTravel, bool walking, float groundSpeed, Vector3 position)
    {
        var it = new CharacterBrain.Intent
        {
            groundSpeed = groundSpeed,
            sprinting = toldSprinting,
            // Nothing here has a keyboard, so unless this kind of body knows its own gear it is worked out from how
            // fast it is going — against the one gear-change speed every character shares.
            gearFromSpeed = true,
        };

        if (walking)
        {
            it.facing = groundTravel;
            if (agent != null && agent.GlideFaceAt.HasValue)
            {
                Vector3 look = agent.GlideFaceAt.Value - position; look.y = 0f;
                if (look.sqrMagnitude > 1e-4f) it.facing = look;
            }
        }

        DescribeSelf(ref it);
        return it;
    }


    // Where this body stood last frame, on the machine that OWNS it. Kept apart from lastPos because that one is
    // never reached on a machine with no screen — this driver returns before it updates it.
    Vector3 serverLastPos;
    bool serverPosSeeded;

    /// <summary>THE MACHINE THAT OWNS THE WORLD STILL HAS TO TURN THIS BODY, and it is the only thing it does with
    /// it. Everything in the frame below needs a model to drive and there is none there, so this driver would stop
    /// at its first line — leaving every body pointing whichever way it was spawned for the whole of its life.
    /// Which way a body is turned is the one thing about it every watching machine can see and none of them can
    /// work out alone, so it is decided here and travels with the body's position like every other fact about it.
    ///
    /// <para>Asked of the SAME function that answers it on a machine that can see, so the two can never drift into
    /// deciding a facing differently. Written straight on with no easing: every machine watching already glides
    /// toward each direction it is sent, so easing it here as well would only make the body turn late.</para></summary>
    void TurnTheBodyItself()
    {
        Vector3 p = transform.position;
        if (!serverPosSeeded) { serverLastPos = p; serverPosSeeded = true; }
        Vector3 d = p - serverLastPos; d.y = 0f;
        serverLastPos = p;

        bool walking = Travelling(d, Mathf.Max(Time.deltaTime, 1e-4f), out float groundSpeed);
        var it = DescribeThisFrame(d, walking, groundSpeed, p);
        var dec = CharacterBrain.Decide(it, CharacterBrain.WalkThreshold, hasRun: true, hasJump: true, hasSprint: false);
        if (!dec.applyFacing) return;

        transform.rotation = Quaternion.LookRotation(dec.facing);
    }

    protected virtual void LateUpdate()
    {
        if (GameServer.IsServer) { TurnTheBodyItself(); return; }

        if (stance == null) return;

        // Travel this frame (LateUpdate runs after PathAgent3D's move). `moved` keeps the VERTICAL component so a
        // falling unit still re-seats; `d` drops it, because facing is a ground direction.
        Vector3 p = transform.position;
        Vector3 moved = p - lastPos;
        Vector3 d = moved; d.y = 0f;
        lastPos = p;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        bool walking = Travelling(d, dt, out float groundSpeed);

        // WHAT THIS BODY IS DOING, then THE ONE BRAIN, then THE ONE PLACE THAT PUTS THE ANSWER ON. Exactly the three
        // steps the person at the keyboard goes through, running exactly the same middle step — which is the whole
        // point: these are the same rig with the same clips, so they flinch, die, swing, jump, dash, walk, run and
        // turn by one rule or they drift, and they did drift.
        var it = DescribeThisFrame(d, walking, groundSpeed, p);

        // A BODY WHOSE OWNER ALREADY DECIDED is not decided about again. Almost everything a person's body does is
        // known only on their own machine — they pressed jump, they rolled, they sat down — so what arrives here is
        // that machine's verdict, already run through this same brain. Deciding it a second time from the little
        // this machine can see would overrule somebody who actually knows.
        CharacterBrain.Decision dec;
        if (toldHasPose)
            dec = new CharacterBrain.Decision
            {
                hasAction = true, id = toldPose, window = toldPoseWindow, release = toldPoseRelease, beat = toldPoseBeat,
                cancelOneShot = toldPose == StanceId.Die,
                facing = it.facing.sqrMagnitude > 1e-6f ? it.facing.normalized : Vector3.zero,
                applyFacing = it.facing.sqrMagnitude > 1e-6f,
            };
        else
            dec = CharacterBrain.Decide(it, CharacterBrain.WalkThreshold, hasRun: true, hasJump: true,
                                        hasSprint: CharacterBrain.HasSprintClip());

        CharacterBrain.Apply(stance, pose, dec);

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

        // …and the steps, off the very frames this body just posed. Last, so it reads the pose this frame produced.
        var tracker = FootstepTracker.Instance;
        if (pose != null && tracker != null)
            tracker.DetectPlants(pose, transform.position, gait, onFootPlant);
    }
}
