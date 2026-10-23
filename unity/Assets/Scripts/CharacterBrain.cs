using UnityEngine;

/// <summary>
/// THE animation brain for EVERY live-3D character in this game — the person at the keyboard, another person watched
/// over the wire, a townsperson, and a creature wearing a character's body. Given a plain description of what a body
/// is doing, it answers which pose that body holds and which way it looks.
///
/// <para><b>Why there is only one.</b> These four are the same rig with the same clips, so every one of them should
/// flinch, die, swing, jump, dash, walk, run and turn by the identical rule. They did not: the person at the keyboard
/// ran this decision, a creature ran a shorter one of its own, a townsperson ran a third, and each had its own answer
/// for which way to look. Every one of those was a rule somebody had to remember to copy, and the copies drifted —
/// a creature bit you side-on while walking, and turned at a different speed from the person fighting it.</para>
///
/// <para><b>Nobody owns a behaviour any more; each kind owns only its REASON.</b> What fills in the description below
/// is the one thing that legitimately differs: the player's is filled from their input, a townsperson's from its
/// wander and whoever is talking to it, a creature's from what it is hunting, and a watched copy's from what the
/// machine that owns it said. Everything AFTER that is this one function.</para>
///
/// <para>Pure decision — no side effects and no allocation. <see cref="Apply"/> is the other half: the single place
/// the verdict is put onto a body, so the two halves cannot be done differently either.</para>
/// </summary>
public static class CharacterBrain
{
    /// <summary>WHAT A BODY IS DOING, in the plainest terms, filled in by whoever owns that body. Everything here is
    /// a fact about the body, never about who or what is driving it — which is what lets one brain serve all four.
    ///
    /// <para>Anything left alone means "no": a townsperson never swings, never jumps and never dies, so it fills in
    /// almost nothing and the answer it gets is walk or stand.</para></summary>
    public struct Intent
    {
        /// <summary>Which way this body wants to look, in world space. ZERO means "no opinion" — and no opinion means
        /// the line it just travelled, which is what a body walking somewhere wants.</summary>
        public Vector3 facing;
        /// <summary>Turn to <see cref="facing"/> at once this frame instead of at the body's turning speed — the
        /// frame an attack starts.</summary>
        public bool snapFacing;

        /// <summary>Ground speed in units per second, and which travel gear it is in. The gear is a FACT, never
        /// guessed from the speed: a hasted run and a sprint are both simply fast, and no number can tell them
        /// apart.</summary>
        public float groundSpeed;
        public bool sprinting;
        public bool walkingSlow;

        /// <summary>NOBODY TOLD ME MY GEAR — work it out from how fast I am going, against the one gear-change speed
        /// every character shares. Set by a body that is steered rather than driven: a creature, and another person's
        /// body watched over the wire, neither of which has a keyboard saying "sprint" or "walk".
        ///
        /// <para>Left alone by anyone who KNOWS: the person at this keyboard is told their gear by their own input,
        /// and a townsperson has exactly one gear and says so. A told gear always wins, because a hasted run and a
        /// sprint are both simply fast and no threshold could ever tell them apart.</para></summary>
        public bool gearFromSpeed;

        /// <summary>Lying dead. Beats everything, including a swing that was still running when the blow landed.</summary>
        public bool dead;

        /// <summary>Falling off something, as opposed to the scripted hop below.</summary>
        public bool falling;

        /// <summary>Mid-swing, which pose the swing plays, and the window it must finish inside.</summary>
        public bool swinging;
        public StanceId swing;
        public float swingWindow;

        /// <summary>In a scripted hop: the frame the clip freezes on while in the air, and whether it is now coming
        /// down and should play its landing tail.</summary>
        public bool airborne;
        public float airborneFreeze;
        public bool airborneReleasing;

        /// <summary>Being slid from A to B by a skill. It plays NO animation at all — the body holds whatever pose it
        /// had and simply travels, which is what makes a blink read as a blink instead of a sprint.</summary>
        public bool gliding;

        /// <summary>Rolling or dashing under its own power, and how long that lasts.</summary>
        public bool dashing;
        public float dashWindow;

        /// <summary>Flinching from a blow. Only ever shows on a body that is standing still: a hit never interrupts
        /// an action or a run, for anybody.</summary>
        public bool flinching;
        /// <summary>Which flinch this is (<see cref="StatusController.FlinchCount"/>). A new number means a new hit
        /// staggered the body, and the hurt reaction starts over from its first frame.</summary>
        public int flinchCount;
    }

    /// <summary>The verdict for this frame.</summary>
    public struct Decision
    {
        /// <summary>False means "no action" — clear the action layer so a held stance, or plain Idle, shows.</summary>
        public bool hasAction;
        public StanceId id;
        /// <summary>Seconds the clip is fitted to, for the stances whose length is decided by the game rather than by
        /// the art (a swing's window, a dash's length, the frame a jump freezes on).</summary>
        public float window;
        /// <summary>A frozen airborne clip is being let go to play its landing tail.</summary>
        public bool release;
        /// <summary>This body is lying dead, so a one-shot still running has to be cancelled rather than merely
        /// out-driven — otherwise it dies mid-swing and finishes the swing lying down.</summary>
        public bool cancelOneShot;
        /// <summary>Which beat of this stance this is. When it changes while the same stance is already showing, the
        /// clip starts over from its first frame — a second hit restarts the flinch instead of letting it run on.</summary>
        public int beat;

        /// <summary>How fast the body is travelling over the ground, in units per second — what a walk, run or sprint
        /// clip is played against so a planted foot stays still.</summary>
        public float groundSpeed;

        public Vector3 facing;
        public bool applyFacing;
        public bool snapFacing;
    }

    /// <summary>THE SPEED AT WHICH A BODY COUNTS AS MOVING, in units per second. One number for every character,
    /// because "is this thing walking" is one question — a creature crawling at a speed the person beside it would
    /// call standing still is exactly the kind of disagreement having two numbers produces. This is the number the
    /// person at the keyboard has always used.</summary>
    public const float WalkThreshold = 0.12f;

    /// <summary>Decide what this body holds and where it looks. <paramref name="walkThreshold"/> is the ground speed
    /// above which it counts as moving; the three capability flags say which clips this body actually has authored,
    /// so a rig with no distinct sprint clip runs instead of standing still.</summary>
    public static Decision Decide(in Intent it, float walkThreshold, bool hasRun, bool hasJump, bool hasSprint)
    {
        var d = new Decision { groundSpeed = it.groundSpeed };

        // WHICH WAY IT LOOKS is answered first and never skipped, because looking and moving are two different
        // questions — a body can stand its ground and turn to face somebody walking up to it. An opinion of zero
        // falls through to the travel line, which is filled in by whoever describes the body.
        Vector3 dir = it.facing; dir.y = 0f;
        d.applyFacing = dir.sqrMagnitude > 1e-6f;
        d.facing = d.applyFacing ? dir.normalized : Vector3.zero;
        d.snapFacing = d.applyFacing && it.snapFacing;

        // DEATH OUTRANKS EVERYTHING, including a swing still in the air.
        if (it.dead)
        {
            d.hasAction = true; d.id = StanceId.Die; d.cancelOneShot = true;
            return d;
        }

        // Walked off a ledge: hold the jump clip frozen at its authored airborne frame all the way down.
        if (it.falling && hasJump)
        {
            var jb = StanceCatalog.Get(StanceId.Jump);
            d.hasAction = true; d.id = StanceId.Fall; d.window = jb != null ? jb.airborneFreeze : 0f;
            return d;
        }

        // One-shots and held actions, highest first. While one runs, only the facing is fed through — never the
        // speed — so moving can never cancel it: a swing, a hop or a dash taken on the run keeps playing.
        if (it.swinging)  { d.hasAction = true; d.id = it.swing;       d.window = it.swingWindow; return d; }
        if (it.airborne)  { d.hasAction = true; d.id = StanceId.Jump;  d.window = it.airborneFreeze; d.release = it.airborneReleasing; return d; }
        if (it.gliding)   { d.hasAction = false; return d; }
        if (it.dashing)   { d.hasAction = true; d.id = StanceId.Roll;  d.window = it.dashWindow; return d; }

        // A flinch only shows on a body that is standing still — a hit never interrupts an action or a run.
        if (it.flinching && it.groundSpeed < walkThreshold) { d.hasAction = true; d.id = StanceId.Hurt; d.beat = it.flinchCount; return d; }

        // Locomotion. Run by default; the sprint gear upshifts and the slow-walk gear downshifts, and a rig with no
        // distinct sprint clip sprints on its run clip rather than falling back to standing.
        if (it.groundSpeed >= walkThreshold)
        {
            d.hasAction = true;
            bool slow = it.walkingSlow
                        || (it.gearFromSpeed && !it.sprinting && it.groundSpeed < CharacterSpec.Active.GearSwitchSpeed);
            d.id = it.sprinting ? (hasSprint ? StanceId.Sprint : StanceId.Run)
                 : slow ? StanceId.Walk
                 : (hasRun ? StanceId.Run : StanceId.Walk);
            return d;
        }

        // Standing: clear the action so a held stance shows, or plain Idle.
        d.hasAction = false;
        return d;
    }

    /// <summary>Put the verdict onto a body — the ONE place that happens, so the four kinds cannot apply the same
    /// answer in four different ways. Safe with a null pose (a machine with no screen has no model to turn).</summary>
    public static void Apply(StanceController stance, IStancePose pose, in Decision d)
    {
        if (stance == null) return;

        if (d.cancelOneShot) stance.CancelActionOverride();

        stance.SetGroundSpeed(d.groundSpeed);
        if (d.hasAction) stance.DriveStance(d.id, d.window, d.release, d.beat);
        else             stance.ClearAction();

        if (d.applyFacing && pose != null) pose.SetFacing(d.facing, d.snapFacing);
    }

    /// <summary>WHICH WAY A BODY WANTS TO LOOK, held by whoever decides it — the person's controller, a creature's
    /// brain. One holder, so "point at this" and "I have no opinion" cannot be written two different ways on two
    /// kinds of character, which is exactly how they came to be written before.
    ///
    /// <para>Zero means NO OPINION, and no opinion means the line the body is travelling. That is not the same as
    /// "face north": a body that has nothing to look at must keep whatever facing it had, or a standing character
    /// would snap to a compass direction the moment it stopped.</para></summary>
    public struct Facing
    {
        /// <summary>The direction, or zero for no opinion. Planar — which way a body leans is not a facing.</summary>
        public Vector3 World { get; private set; }

        /// <summary>Look that way. A direction of nothing is ignored rather than taken as an opinion, so pointing a
        /// body at the spot it is already standing on leaves it as it was.</summary>
        public void Toward(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) World = dir;
        }

        /// <summary>Look wherever I am going. Said by a body that has stopped caring about a target — a creature that
        /// gave up a chase, somebody who walked away from a conversation — so it stops staring at them.</summary>
        public void AtNothing() => World = Vector3.zero;

        /// <summary>Look that way, or at nothing if there is nothing to look at. One call for the common shape.</summary>
        public void TowardOrNothing(Vector3 dir)
        {
            dir.y = 0f;
            World = dir.sqrMagnitude > 1e-4f ? dir : Vector3.zero;
        }
    }

    /// <summary>Does this rig have a distinct sprint clip authored? Asked by every caller, so it is answered once.
    /// A cheap array index — no allocation, safe to call per frame.</summary>
    public static bool HasSprintClip()
    {
        var b = StanceCatalog.Get(StanceId.Sprint);
        return b != null && !string.IsNullOrEmpty(b.clip);
    }
}
