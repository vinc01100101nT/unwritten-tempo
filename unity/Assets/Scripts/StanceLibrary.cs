using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The vocabulary of POSE states a character can be in — the "stance" sibling of <see cref="VfxCueId"/>. Where a cue
/// is a fire-and-forget event (gameplay already happened, play a one-shot look), a stance is a POSE the character is
/// currently in. Two flavours share this one table:
/// <list type="bullet">
/// <item><b>Held stances</b> (Combat/Guard/Casting/Sit) — persistent baselines pushed/popped on a
/// <see cref="StanceController"/>; several can be active and the highest <see cref="StanceBinding.priority"/> wins.</item>
/// <item><b>Action stances</b> (locomotion + one-shots: Idle/Walk/Run/Attack/Jump/Roll/Hurt/PickUp/Die/Fall) — the
/// transient beats the gameplay brain drives one at a time (they beat any held stance while active).</item>
/// </list>
/// Both are authored in the <b>Stance Composer</b> and resolved by <see cref="StanceController"/> — the single choke
/// point that drives the <see cref="IStancePose"/> sink (<see cref="Character3DPose"/>).
///
/// APPEND-ONLY (like <see cref="VfxCueId"/> / <see cref="CharacterAction"/> / Biome): the int index is stored in the
/// <see cref="StanceLibrary"/> asset, so add new ids at the END and never reorder.
/// </summary>
public enum StanceId
{
    // --- Held stances (persistent baselines; pushed/popped; priority-resolved) ---
    Combat,   // weapon-ready idle held while a target is engaged (Baked idle cadence)
    Guard,    // defensive / blocking hold
    Casting,  // channel pose held during a cast wind-up (FitWindow → fits the cast-bar length)
    Sit,      // resting / seated

    // --- Action stances (transient; the gameplay brain drives one at a time; beat held stances) ---
    Idle,     // standing baseline when nothing else is active
    Walk,     // slow locomotion (paced by MoveGait)
    Run,      // fast locomotion (paced by MoveGait)
    Attack,   // melee swing (FitWindow → fps fits the attack-speed-scaled swing)
    Jump,     // the scripted hop
    Roll,     // dodge-roll / dash (FitWindow → fps fits the dash window)
    Hurt,     // damage flinch
    PickUp,   // collected loot
    Die,      // death (its action plays once and holds the final lying frame)
    Fall,     // free-fall pose (walked off a ledge) — holds the Jump clip frozen at Jump's airborneFreeze frame

    // --- Skill-release cues (transient one-shots; driven by SkillRunner at the RELEASE of a skill, AFTER any cast
    // wind-up). A SkillDef picks ONE of these as its "skill type" and this plays once when the skill fires. Three
    // variants per family so different skills can look distinct. Bind each to a clip in the Stance Composer.
    // The curated subset a skill may choose lives in SkillReleaseStances (the single owner of that list).
    AoeSpell1, AoeSpell2, AoeSpell3,                 // wide area-of-effect spell release
    SingleSpell1, SingleSpell2, SingleSpell3,        // single-target bolt/beam spell release
    PhysicalSkill1, PhysicalSkill2, PhysicalSkill3,  // weapon / physical skill swing

    // --- Attack variants by equipped weapon (the player brain picks ONE per basic attack via WeaponStances.AttackFor;
    // all play CharacterAction.Attack so billboards are unaffected). Author a clip per weapon in the Stance Composer.
    AttackBareHands,   // unarmed / fist / knuckle
    AttackSword,       // one/two-hand sword, axe, mace (generic melee swing)
    AttackDagger,      // dagger / katar
    AttackStaff,       // staff (magic bonk / channel jab)
    AttackLance,       // spear / polearm thrust
    AttackHandGun,     // pistol (reserved — Firearm currently maps to AttackRifle)
    AttackRifle,       // rifle / musket
    AttackBow,         // bow draw + loose

    // --- Sprint (V) — the fast locomotion gear above Run. Appended here to keep the enum APPEND-ONLY (stored int
    // index); the Stance Composer lists it in the Locomotion group. Defaults to the Run action/clip so it looks like
    // the old full-speed run out of the box; author a distinct clip to differentiate it. Paced by MoveGait like Walk/Run.
    Sprint,

    // --- Attack variants added after Sprint (APPEND-ONLY, see above); they belong with the weapon attack variants.
    // The scythe's reaping sweep.
    AttackScythe,
    // The shuriken's swing (STR at or above DEX).
    AttackShuriken,
    // The shuriken's throw (DEX above STR) — the blade leaves the hand.
    AttackShurikenThrow,
    // One weapon, one swing — each cloned from its Ragnarok Online sprite attack (2026-10-09). The two-handed sword
    // keeps AttackSword; these give the one-handed weapons that shared it, and the katar, fist and wand, their own.
    AttackOneHandSword,
    AttackAxe,
    AttackMace,
    AttackKatar,
    AttackKnuckle,
    AttackWand,
}

/// <summary>
/// WHERE a stance's live playback-speed multiplier comes from — the answer to "how do stat-dependent stances stay in
/// sync?". The stance NEVER stores the stat/window value (that would be a hand-synced copy — the SSOT bug); it names
/// only which OWNER to read, and <see cref="StanceController"/> reads the live value each frame. Final playback = the
/// clip's natural fps (owned by the <see cref="Character3DPose"/> Animator) × this source × the binding's <see cref="StanceBinding.speedMult"/>.
/// </summary>
public enum StanceSpeedSource
{
    Baked = 0,      // the baked action's authored fps (× speedMult) — no live link
    // 1,2 (was AttackSpeed / CastSpeed) retired: the actual swing / cast stances use FitWindow (they fit the
    // gameplay swing time / cast-bar window). The ASPD & cast-speed STATS are unchanged (Character.AttackSpeedMult /
    // CastTimeMult) — only the stance-animation source is gone. Ints kept stable so MoveGait/FitWindow assets survive.
    MoveGait = 3,   // × the player's GaitSpeedMult (locomotion stances)
    FitWindow = 4,  // fps fits the clip to a RUNTIME window: clipNaturalLength / window (window fed at drive time).
                    // For a swing/dodge whose duration is gameplay-driven (Attack = swing time, Roll = dash length),
                    // so the whole clip plays exactly within it. The window stays runtime (not stored) → SSOT-safe.
}

/// <summary>Binds ONE <see cref="StanceId"/> to the baked pose it plays — the stance analog of a
/// <see cref="VfxCueBinding"/>. Authored in the Stance Composer. Owns only COMPOSITION (which action, how its speed is
/// modulated, how it ranks, how it plays back); the fps stays owned by the animation clip
/// (<see cref="Character3DPose"/>) and any stat/window stays owned by its live source.</summary>
[System.Serializable]
public class StanceBinding
{
    public StanceId id;

    [Tooltip("Which CharacterAction this stance plays (e.g. Combat -> Idle, Attack -> Attack, Fall -> Jump). " +
             "If the sink has no clip for it, it falls back to Idle.")]
    public CharacterAction action = CharacterAction.Idle;

    [Tooltip("3D ONLY: the exact animation clip this stance plays on a live-3D character (Character3DPose). This is " +
             "the single owner of a stance's 3D clip — there is NO hardcoded action->clip fallback. Empty = the 3D " +
             "character just stands (no clip). 2D billboards ignore this and use 'action' (baked sprite sets).")]
    public string clip = "";

    [Tooltip("Where the LIVE playback-speed multiplier is read from each frame. Baked = the action's authored fps; " +
             "MoveGait reads GaitSpeedMult; FitWindow fits the clip to a runtime window (Attack = swing time, " +
             "Roll = dash length, Casting = cast-bar length).")]
    public StanceSpeedSource speedSource = StanceSpeedSource.Baked;

    [Tooltip("Hand-tuned constant multiplier on top of the live source (1 = just the source).")]
    public float speedMult = 1f;

    [Tooltip("For HELD stances only: higher wins when several are active at once (e.g. Casting > Combat). Action " +
             "stances are driven one at a time, so their priority is unused.")]
    public int priority;

    [Tooltip("JUMP only: seconds into the jump clip where the crouch/push-off ends. The player is ROOTED (no move) " +
             "from 0 to here, then launches. 0 = launch instantly.")]
    public float crouchEnd = 0f;

    [Tooltip("JUMP only: seconds into the jump clip where the airborne pose FREEZES. The clip plays 0→here then holds " +
             "on this frame for the whole airborne time (however long the fall lasts); on landing it resumes here→end " +
             "as the landing tail (player rooted for that tail). The Fall stance (ledge walk-off) reuses THIS value.")]
    public float airborneFreeze = 1.5f;

    [Tooltip("ROLL only: the clip FRAME from which the player is ROOTED (no move) until the clip ends — the roll's " +
             "recovery. Runtime turns this into a normalized point (frame ÷ the clip's own frame count) so it survives " +
             "the FitWindow time-rescale. 0 = no rooting (the player moves for the whole roll).")]
    [Min(0)] public int rootFromFrame = 0;

    [Tooltip("WALK/RUN/SPRINT only, MEASURED from the clip when it is picked in the Stance Composer — never typed: every " +
             "foot-plant in the cycle, ONE entry per footfall, each carrying the FRAME the foot lands AND which foot it " +
             "is. FootstepTracker fires a footstep (SFX + puddle ripple + snow print) each time the animation crosses a " +
             "plant's frame (turned into a 0→1 phase = frame ÷ the clip's frame count), and offsets the print to that " +
             "foot's side. This binding is the SINGLE OWNER of where feet land; FootstepTracker only reads it.")]
    public List<FootPlant> footPlants = new List<FootPlant>();

    [Tooltip("WALK/RUN/SPRINT only, MEASURED with the foot-plants — never typed: how fast this clip walks on each body " +
             "model at normal playback, in that model's own unscaled units a second. The legs play at the body's real " +
             "ground speed divided by this (times its world scale), so a planted foot stays still.")]
    public List<BodyStride> bodyStrides = new List<BodyStride>();

    [Tooltip("WALK/RUN/SPRINT only, MEASURED with the foot-plants: the middle of every body's clip speed divided by its " +
             "Humanoid scale. Used only for a body added after the clip was last measured, which is also reported.")]
    public float speedPerHumanScale;
}

/// <summary>How fast one locomotion clip walks on one body model: the model's Humanoid avatar name and the clip's
/// walking speed on it, in the model's own unscaled units a second.</summary>
[System.Serializable]
public class BodyStride
{
    public string avatar = "";
    public float speed;
}

/// <summary>One foot-plant in a walk/run cycle: the clip FRAME the foot goes flat, and WHICH foot it is. Authoring the
/// foot (rather than alternating blindly) is what lets the print land on the correct side from the very first step —
/// cycles don't always start on the left, and a stop/restart mustn't flip the sides.</summary>
[System.Serializable]
public class FootPlant
{
    [Min(0)] [Tooltip("Frame in the walk/run clip where this foot is flat on the ground.")]
    public int frame;
    [Tooltip("Which foot plants here — OFF = left (print offsets to the left of travel), ON = right.")]
    public bool right;
}

/// <summary>The built-in starting point for each stance — the SINGLE owner of stance defaults, shared by the Stance
/// Composer (seeds a freshly-added row) and <see cref="StanceCatalog"/> (synthesizes any id the asset hasn't authored
/// yet, so the game animates correctly even before the library is opened + saved). Author-time edits in the composer
/// override these per stance.</summary>
public static class StanceDefaults
{
    public static StanceBinding For(StanceId id)
    {
        var b = new StanceBinding { id = id, action = CharacterAction.Idle, speedMult = 1f };
        switch (id)
        {
            case StanceId.Combat:  b.action = CharacterAction.Idle;   b.speedSource = StanceSpeedSource.Baked; b.priority = 10; break;
            case StanceId.Guard:   b.action = CharacterAction.Idle;   b.priority = 20; break;
            case StanceId.Casting: b.action = CharacterAction.Cast;   b.speedSource = StanceSpeedSource.FitWindow;  b.priority = 30; break;
            case StanceId.Sit:     b.action = CharacterAction.Sit;    b.priority = 5;  break;
            case StanceId.Idle:    b.action = CharacterAction.Idle;   break;
            case StanceId.Walk:    b.action = CharacterAction.Walk;   b.speedSource = StanceSpeedSource.MoveGait;  break;
            case StanceId.Run:     b.action = CharacterAction.Run;    b.speedSource = StanceSpeedSource.MoveGait;  break;
            case StanceId.Sprint:  b.action = CharacterAction.Run;    b.speedSource = StanceSpeedSource.MoveGait;  break;   // clip defaults to Run; author a distinct sprint clip to differentiate
            case StanceId.Attack:  b.action = CharacterAction.Attack; b.speedSource = StanceSpeedSource.FitWindow; break;
            case StanceId.Jump:    b.action = CharacterAction.Jump;   b.crouchEnd = 0.25f; b.airborneFreeze = 1.5f; break;
            case StanceId.Roll:    b.action = CharacterAction.Roll;   b.speedSource = StanceSpeedSource.FitWindow; break;
            case StanceId.Hurt:    b.action = CharacterAction.Hurt;   break;
            case StanceId.PickUp:  b.action = CharacterAction.PickUp; break;
            case StanceId.Die:     b.action = CharacterAction.Die;    break;
            case StanceId.Fall:    b.action = CharacterAction.Jump;   break;   // freeze frame read live from the Jump binding's airborneFreeze

            // Skill-release cues: fit the clip to the runtime release window (SkillRunner feeds the natural length,
            // so it plays through exactly once). Spells default to the Cast action, physical skills to Attack.
            case StanceId.AoeSpell1: case StanceId.AoeSpell2: case StanceId.AoeSpell3:
            case StanceId.SingleSpell1: case StanceId.SingleSpell2: case StanceId.SingleSpell3:
                b.action = CharacterAction.Cast;   b.speedSource = StanceSpeedSource.FitWindow; break;
            case StanceId.PhysicalSkill1: case StanceId.PhysicalSkill2: case StanceId.PhysicalSkill3:
                b.action = CharacterAction.Attack; b.speedSource = StanceSpeedSource.FitWindow; break;

            // Weapon attack variants — same shape as the base Attack (swing fit to the attack-speed-scaled window).
            case StanceId.AttackBareHands: case StanceId.AttackSword: case StanceId.AttackDagger:
            case StanceId.AttackStaff:     case StanceId.AttackLance: case StanceId.AttackHandGun:
            case StanceId.AttackRifle:     case StanceId.AttackBow:   case StanceId.AttackScythe: case StanceId.AttackShuriken:
            case StanceId.AttackShurikenThrow:
            case StanceId.AttackOneHandSword: case StanceId.AttackAxe: case StanceId.AttackMace:
            case StanceId.AttackKatar:     case StanceId.AttackKnuckle: case StanceId.AttackWand:
                b.action = CharacterAction.Attack; b.speedSource = StanceSpeedSource.FitWindow; break;
        }
        return b;
    }
}

/// <summary>
/// The single asset that maps every <see cref="StanceId"/> to the baked pose it plays. The stance sibling of
/// <see cref="VfxCueLibrary"/> — resolved once at runtime by <see cref="StanceCatalog"/> and edited in the
/// <b>Stance Composer</b>. Lives at <c>Resources/StanceLibrary.asset</c> so the game loads it by name.
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Stance Library", fileName = "StanceLibrary")]
public class StanceLibrary : ScriptableObject
{
    public List<StanceBinding> stances = new List<StanceBinding>();

    [Tooltip("Each weapon type's own standing idle (whole body) and carry pose (the arms while walking, running or " +
             "sprinting). A weapon with no entry, or an empty clip, keeps the shared one.")]
    [UnityEngine.Serialization.FormerlySerializedAs("carryPoses")]
    public List<WeaponStanceBinding> weaponStances = new List<WeaponStanceBinding>();
}

/// <summary>Which arms a weapon's carry pose holds while its holder travels. An arm the pose does not hold swings with
/// the legs' own clip, the way an empty hand does.</summary>
public enum CarryArms { Both, Right, Left }

/// <summary>How one weapon type is held when its holder is not attacking. The standing idle replaces the whole body
/// of the shared Idle; the carry pose plays on the arms-only layer of the character controller over the shared
/// Idle/Walk/Run/Sprint legs. Attacks, skills, jumps, flinches and every other stance play their own whole-body clip
/// with both faded out.</summary>
[System.Serializable]
public class WeaponStanceBinding
{
    public WeaponType weapon;
    [Tooltip("The whole-body idle played while the holder stands still. Empty = the shared Idle.")]
    public string idleClip = "";
    [Tooltip("The still arm pose this weapon is carried in while walking, running or sprinting — and while standing " +
             "when there is no idle above. Empty = the shared arms.")]
    [UnityEngine.Serialization.FormerlySerializedAs("clip")]
    public string carryClip = "";
    [Tooltip("Which arms the carry pose holds. Both = the whole pose; Right or Left = only that arm, and the other " +
             "swings with the run like an empty hand.")]
    public CarryArms carryArms = CarryArms.Both;

    [Tooltip("The arms putting this weapon away, played over whatever the legs are doing. Empty = it simply moves.")]
    public string sheatheClip = "";
    [Tooltip("The arms taking this weapon out, played over whatever the legs are doing. Empty = it simply moves.")]
    public string unsheatheClip = "";
    [Tooltip("The frame of the put-away clip at which the right hand lets go of its weapon. -1 = at the end.")]
    public int sheatheRightFrame = -1;
    [Tooltip("The frame of the put-away clip at which the left hand lets go (a pair weapon's second copy). -1 = at the end.")]
    public int sheatheLeftFrame = -1;
    [Tooltip("The frame of the take-out clip at which the right hand takes its weapon. -1 = at the start.")]
    public int unsheatheRightFrame = -1;
    [Tooltip("The frame of the take-out clip at which the left hand takes its weapon. -1 = at the start.")]
    public int unsheatheLeftFrame = -1;

    [Tooltip("A flourish during the put-away: the frame of the put-away clip at which the weapon in the main hand starts " +
             "turning over in the fingers (a dagger spun before it is sheathed). The take-out — the put-away played " +
             "backwards — turns it back over the mirrored frames.")]
    public int spinStartFrame;
    [Tooltip("The frame of the put-away clip at which that turning stops. Must be after the start and before the hand " +
             "lets go.")]
    public int spinEndFrame;
    [Tooltip("How many whole turns the weapon makes between those two frames. 0 = it never turns.")]
    public float spinTurns;
    [Tooltip("How far the weapon rises off the fist while it turns, in the hand bone's own units, along the line it turns " +
             "about — enough for the blade to pass over the forearm. 0 = it turns inside the fist.")]
    public float spinLift;
}
