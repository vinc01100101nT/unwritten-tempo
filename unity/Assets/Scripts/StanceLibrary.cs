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
/// point that drives <see cref="BakedBillboard8"/>.
///
/// APPEND-ONLY (like <see cref="VfxCueId"/> / <see cref="CharacterAction"/> / Biome): the int index is stored in the
/// <see cref="StanceLibrary"/> asset, so add new ids at the END and never reorder.
/// </summary>
public enum StanceId
{
    // --- Held stances (persistent baselines; pushed/popped; priority-resolved) ---
    Combat,   // weapon-ready idle held while a target is engaged (breathes with AttackSpeed)
    Guard,    // defensive / blocking hold
    Casting,  // channel pose held during a cast wind-up (paced by CastSpeed)
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
    Fall,     // free-fall pose (reuses the Jump clip's descent tail — see StancePlayMode.FallTail)
}

/// <summary>
/// WHERE a stance's live playback-speed multiplier comes from — the answer to "how do stat-dependent stances stay in
/// sync?". The stance NEVER stores the stat/window value (that would be a hand-synced copy — the SSOT bug); it names
/// only which OWNER to read, and <see cref="StanceController"/> reads the live value each frame. Final playback = the
/// baked action fps (owned by <see cref="BakedBillboard8"/>) × this source × the binding's <see cref="StanceBinding.speedMult"/>.
/// </summary>
public enum StanceSpeedSource
{
    Baked,        // the baked action's authored fps (× speedMult) — no live link
    AttackSpeed,  // × Character.Instance.AttackSpeedMult  (>1 = faster; a combat/attack stance)
    CastSpeed,    // × Character.Instance.CastSpeedMult    (>1 = faster)
    MoveGait,     // × the player's GaitSpeedMult          (locomotion stances)
    FitWindow,    // fps fits the clip to a RUNTIME window: clipNaturalLength / window (window fed at drive time).
                  // For a swing/dodge whose duration is gameplay-driven (Attack = swing time, Roll = dash length),
                  // so the whole clip plays exactly within it. The window stays runtime (not stored) → SSOT-safe.
}

/// <summary>How a stance's clip is played back.</summary>
public enum StancePlayMode
{
    Normal,     // loop the clip (or, for Die's action, BakedBillboard8 auto-holds the last frame)
    FallTail,   // loop only the clip's TAIL (from fallPhase→end) as a sustained free-fall — reuses the Jump clip
}

/// <summary>Binds ONE <see cref="StanceId"/> to the baked pose it plays — the stance analog of a
/// <see cref="VfxCueBinding"/>. Authored in the Stance Composer. Owns only COMPOSITION (which action, how its speed is
/// modulated, how it ranks, how it plays back); the fps stays owned by the baked
/// <see cref="BakedBillboard8.ActionSet"/> and any stat/window stays owned by its live source.</summary>
[System.Serializable]
public class StanceBinding
{
    public StanceId id;

    [Tooltip("Which baked CharacterAction this stance plays (e.g. Combat -> Idle, Attack -> Attack, Fall -> Jump). " +
             "If the unit hasn't baked it, BakedBillboard8 falls back to Idle.")]
    public CharacterAction action = CharacterAction.Idle;

    [Tooltip("Where the LIVE playback-speed multiplier is read from each frame. Baked = the action's authored fps; " +
             "AttackSpeed/CastSpeed read Character.Instance; MoveGait reads GaitSpeedMult; FitWindow fits the clip to " +
             "a runtime window (Attack = swing time, Roll = dash length).")]
    public StanceSpeedSource speedSource = StanceSpeedSource.Baked;

    [Tooltip("Hand-tuned constant multiplier on top of the live source (1 = just the source).")]
    public float speedMult = 1f;

    [Tooltip("For HELD stances only: higher wins when several are active at once (e.g. Casting > Combat). Action " +
             "stances are driven one at a time, so their priority is unused.")]
    public int priority;

    [Tooltip("Normal = loop (Die auto-holds its last frame); FallTail = loop the clip's tail as a free-fall pose.")]
    public StancePlayMode playMode = StancePlayMode.Normal;

    [Tooltip("FallTail only: where in the clip the looped falling frames begin (0 = whole clip, 0.5 = its descent half).")]
    [Range(0f, 1f)] public float fallPhase = 0.5f;
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
            case StanceId.Combat:  b.action = CharacterAction.Idle;   b.speedSource = StanceSpeedSource.AttackSpeed; b.priority = 10; break;
            case StanceId.Guard:   b.action = CharacterAction.Idle;   b.priority = 20; break;
            case StanceId.Casting: b.action = CharacterAction.Cast;   b.speedSource = StanceSpeedSource.CastSpeed;   b.priority = 30; break;
            case StanceId.Sit:     b.action = CharacterAction.Sit;    b.priority = 5;  break;
            case StanceId.Idle:    b.action = CharacterAction.Idle;   break;
            case StanceId.Walk:    b.action = CharacterAction.Walk;   b.speedSource = StanceSpeedSource.MoveGait;  break;
            case StanceId.Run:     b.action = CharacterAction.Run;    b.speedSource = StanceSpeedSource.MoveGait;  break;
            case StanceId.Attack:  b.action = CharacterAction.Attack; b.speedSource = StanceSpeedSource.FitWindow; break;
            case StanceId.Jump:    b.action = CharacterAction.Jump;   break;
            case StanceId.Roll:    b.action = CharacterAction.Roll;   b.speedSource = StanceSpeedSource.FitWindow; break;
            case StanceId.Hurt:    b.action = CharacterAction.Hurt;   break;
            case StanceId.PickUp:  b.action = CharacterAction.PickUp; break;
            case StanceId.Die:     b.action = CharacterAction.Die;    break;
            case StanceId.Fall:    b.action = CharacterAction.Jump;   b.playMode = StancePlayMode.FallTail; b.fallPhase = 0.5f; break;
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
}
