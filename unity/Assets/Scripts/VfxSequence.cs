using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// A DATA-DRIVEN recipe for one composed visual effect — the "look" of a skill beat surfaced out of code and
/// into an asset you can tune in the inspector. A sequence is a FLAT list of <see cref="VfxStep"/>s on a
/// stopwatch: each step says "at this moment, play this library effect / shake / sound, at this anchor". It
/// owns PRESENTATION only (particles, screen-shake, sound) — NEVER damage, element, or multipliers.
///
/// <para>The GAMEPLAY meets the sequence at named <b>ticks</b>. A step can expose a <see cref="VfxStep.tickId"/>
/// — a stable component name (e.g. "meteor_ball", "fire_pillar") that fires per its <see cref="VfxStep.tickWhen"/>
/// rule (Start / End / every <see cref="VfxStep.tickInterval"/> across the step's active window — a Carrier's flight
/// or a looping effect's Stay Duration). A <see cref="SkillDef"/> in the Skill Composer binds each tickId to WHAT it does
/// (damage/heal/status, element, multiplier). The Vfx Composer only composes the look + exposes the ticks.</para>
///
/// <para>Play one with <see cref="VfxDirector.Play"/>, passing a <see cref="VfxContext"/> (where in the world)
/// and an optional <c>onTick</c> callback (the skill's hook — it receives each <see cref="VfxTick"/>).</para>
///
/// Effect ids resolve against the same <see cref="EffectLibrary"/> as <see cref="Vfx"/>. Create an asset via
/// <c>Assets ▸ Create ▸ unwritten ▸ Vfx Sequence</c>, or generate the built-in ones with
/// <c>Tools ▸ unwritten ▸ Art ▸ Build Default Vfx Sequences</c>.
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Vfx Sequence", fileName = "VfxSequence")]
public class VfxSequence : ScriptableObject
{
    [Tooltip("The composed effect, as a flat timeline of steps. Order is cosmetic; each step fires on its own " +
             "schedule (a fixed delay, or when a named event fires).")]
    public List<VfxStep> steps = new List<VfxStep>();

    [Tooltip("Loop/restart point of the composition, in seconds. In the composer PREVIEW the timeline restarts here; " +
             "at RUNTIME a skill that repeats this sequence (e.g. Meteor's shower) uses it as the restart interval — " +
             "shorten it to make the effect repeat faster in-game. Any effect whose clip is still playing keeps " +
             "rendering and overlaps into the next loop. 0 = auto (full length; no early restart).")]
    [FormerlySerializedAs("previewLoopEnd")]
    public float loopEnd;

    /// <summary>Authored restart interval in seconds for a skill that REPEATS this sequence (the same point the
    /// composer's loop-end marker sits at). 0 when unset — the caller should fall back to its own default.</summary>
    public float RestartInterval => loopEnd;
}

/// <summary>What a single step does.</summary>
public enum VfxStepKind
{
    /// <summary>One-shot burst at a world point (hits, explosions, magic circles).</summary>
    PlayAt,
    /// <summary>Looping effect that tracks the context's follow target (auras, shields). Stopped when the sequence ends.</summary>
    Attach,
    /// <summary>A traveling projectile-carried effect that flies from a launch point to the anchor, then raises its
    /// <see cref="VfxStep.emitEventOnEnd"/> (and its tick) ON ARRIVAL (a meteor fall, a homing bolt). Reuses <see cref="Projectile3D"/>.</summary>
    Carrier,
    /// <summary>Camera screen-shake.</summary>
    Shake,
    /// <summary>A positional one-shot sound at the anchor.</summary>
    Sound,
}

/// <summary>Where a step is anchored in the world (plus its own <see cref="VfxStep.offset"/>).</summary>
public enum VfxAnchor
{
    /// <summary>The caster's position — sampled LIVE at the moment the step fires (the caster's live transform,
    /// <see cref="VfxContext.follow"/>), so a delayed step during a moving cast (dash, knockback) stamps where the
    /// caster is NOW, not where they cast. Falls back to the cast-time <see cref="VfxContext.origin"/> for
    /// point-only contexts with no follow transform.</summary>
    Origin,
    /// <summary>The impact / aim point (<see cref="VfxContext.target"/>).</summary>
    Target,
}

/// <summary>When a step starts.</summary>
public enum VfxSchedule
{
    /// <summary>A fixed number of seconds after the sequence began.</summary>
    AtTime,
    /// <summary>When a named event fires (a carrier arrival, or another step's <see cref="VfxStep.emitEventOnEnd"/>).</summary>
    OnEvent,
}

/// <summary>WHEN a step raises its gameplay tick. The single knob for tick placement (replaces the old
/// count/spread model): the flight/effect length comes from elsewhere (the Carrier's arrival, or a looping
/// PlayAt/Attach's Stay Duration), so the VFX only declares the RULE, never a per-skill count.</summary>
public enum TickTiming
{
    /// <summary>Exactly one tick the instant the step starts (a PlayAt/Attach spawn, or a Carrier's launch).</summary>
    Start,
    /// <summary>Exactly one tick when the step ends (a Carrier's arrival; a PlayAt/Attach's natural end, or its
    /// Stay Duration when looping).</summary>
    End,
    /// <summary>A tick every <see cref="VfxStep.tickInterval"/> seconds for as long as the step is active (a Carrier's
    /// whole flight; a PlayAt/Attach's natural lifetime, or its Stay Duration when looping).</summary>
    Interval,
}

/// <summary>One entry on a <see cref="VfxSequence"/> timeline. Gameplay-neutral: it can only spawn a library
/// effect, shake the camera, or play a sound — and optionally RAISE a named event (visual chaining) and/or a
/// gameplay <b>tick</b> that a <see cref="SkillDef"/> binds damage/heal/status onto.</summary>
[System.Serializable]
public class VfxStep
{
    [Tooltip("Author note — what this step is (ignored at runtime).")]
    public string note;

    [Header("When")]
    public VfxSchedule schedule = VfxSchedule.AtTime;
    [Tooltip("AtTime: seconds after the sequence starts.")]
    public float delay;
    [Tooltip("OnEvent: run this step when this event tag fires (e.g. \"impact\").")]
    public string startEvent;
    [Tooltip("OnEvent only: anchor this step to the EXACT point the emitter reported (e.g. a Carrier's actual " +
             "scattered landing) instead of re-resolving anchor+offset from the context. Off = use anchor/offset " +
             "as normal. Only matters when the emitter moved off the plain anchor (scatter, homing).")]
    public bool snapToEventPoint;

    [Header("What")]
    public VfxStepKind kind = VfxStepKind.PlayAt;
    public VfxAnchor anchor = VfxAnchor.Target;
    [Tooltip("World-space nudge from the anchor point.")]
    public Vector3 offset;
    [Tooltip("PlayAt / Attach: jitter the spawn point by a random amount per axis (magnitude up to " +
             "0.2 units on x/y/z) so repeated plays don't stack in the exact same spot. Off = deterministic.")]
    public bool scatter;
    [Tooltip("Euler rotation for the spawned effect (PlayAt).")]
    public Vector3 rotationEuler;

    [Header("Effect (PlayAt / Attach / Carrier)")]
    [Tooltip("EffectLibrary id, e.g. \"aoe.ground-explosion\".")]
    public string effectId;
    [Tooltip("Scale multiplier on the effect's authored scale.")]
    public float scale = 1f;
    [Tooltip("Playback-speed multiplier (PlayAt only).")]
    public float speed = 1f;
    [Tooltip("Seconds to cut off the END of a PlayAt effect — removes trailing dead time (empty tail after the " +
             "visible burst) so the beat ends sooner. 0 = play the full clip.")]
    public float trimEnd;
    [Tooltip("Seconds to fast-forward INTO the clip on spawn — the effect appears already mid-animation instead " +
             "of from frame 0 (skip a slow intro / desync a salvo). PlayAt + Carrier's travel effect. 0 = from the start.")]
    public float clipStart;
    [Tooltip("Colour tint (white = no tint).")]
    public Color tint = Color.white;
    [Tooltip("Play At / Attach only: keep the effect LOOPING until the skill's Stay Duration elapses (fallback 3s, " +
             "set in the Skill Composer). Off = play once for the effect's natural length, then end. Carrier ignores " +
             "this — it always ends on arrival.")]
    public bool looping = false;

    [Header("Event (visual chaining — gameplay-neutral)")]
    [Tooltip("Raise this event tag the moment the step STARTS (the effect is spawned / launched). OnEvent steps " +
             "keyed to it also start, and the skill's onTick callback receives it as a VfxTick.")]
    public string emitEventOnStart;
    [Tooltip("Raise this event tag when the step ENDS — a Carrier on ARRIVAL, a PlayAt when its effect finishes " +
             "its lifetime, a Shake after its duration, an Attach when the sequence stops. Use this to chain an " +
             "impact after a visual completes (e.g. a fall → explosion).")]
    [FormerlySerializedAs("emitEvent")]
    public string emitEventOnEnd;

    [Header("Tick (gameplay-neutral: WHEN a component fires, not WHAT it does)")]
    [Tooltip("Stable tick name the Skill Composer binds gameplay onto (e.g. \"meteor_ball\", \"fire_pillar\"). " +
             "Empty = pure-visual step (no gameplay). This is the ONLY join key between VFX and Skill.")]
    public string tickId;
    [Tooltip("WHEN this step raises its gameplay tick. Start = once at the start; End = once at the end (Carrier " +
             "arrival, or a PlayAt/Attach's natural end / Stay Duration); Interval = every tickInterval seconds while " +
             "the step is active. Empty tickId = no gameplay tick. The length is NOT authored here — it comes from " +
             "the flight (Carrier) or the Stay Duration (looping PlayAt/Attach), so one VFX suits every skill.")]
    public TickTiming tickWhen = TickTiming.Start;
    [Tooltip("Interval mode only: seconds between ticks while the step is active. The skill applies ONE burst per tick.")]
    public float tickInterval = 0.2f;

    [Header("Carrier")]
    [Tooltip("Launch from the CASTER's live position (ctx.origin) instead of a fixed offset above the landing. " +
             "On = a real projectile that leaves the caster and flies the actual (variable) gap to the target — set " +
             "anchor to Target so it LANDS on the impact. Off = fall-in / drop model (launch is a fixed offset from " +
             "where it lands, e.g. a meteor from straight above).")]
    public bool carrierFromCaster;
    [Tooltip("Launch point offset. Off carrierFromCaster: offset from the LANDING anchor (e.g. (0,9,0) = fall from " +
             "9 units up). On carrierFromCaster: offset from the CASTER (e.g. (0,1,0) = leave the hand 1 unit up).")]
    public Vector3 carrierFrom = new Vector3(0f, 9f, 0f);
    [Tooltip("Random horizontal spread of the launch point, so a salvo reads as a shower.")]
    public float carrierScatter;
    public float carrierSpeed = 16f;
    [Tooltip("Rotate the flying effect to FACE its travel direction (launch → land) instead of its authored " +
             "orientation. Off = straight-down look; on = the trajectory tilts to match a horizontally-offset launch.")]
    public bool carrierFaceTravel;
    public float carrierArriveRadius = 0.4f;
    [Tooltip("Safety cap: fly no longer than this before force-arriving.")]
    public float carrierMaxLife = 3f;

    [Header("Shake")]
    public float shakeStrength = 0.3f;
    public float shakeDuration = 0.18f;

    [Header("Sound")]
    public SfxSet sound;
    [Tooltip("Seconds to play the sound before it's faded/cut out — BOUNDS a long or looping clip (e.g. a " +
             "waterfall loop) so it ends with the beat instead of bleeding past the loop / overlapping into the " +
             "next repeat. 0 = play the full clip (previous behaviour).")]
    public float soundDuration;
    [Tooltip("Ease-IN: seconds to ramp the sound up from silence at the start (smoothstep). 0 = start at full volume.")]
    public float soundFadeIn;
    [Tooltip("Ease-OUT: seconds to ramp the sound down to silence at the end (smoothstep), landing on soundDuration " +
             "(or the clip end). 0 = hard cut / natural end.")]
    public float soundFadeOut;

    /// <summary>True if this step exposes a gameplay tick a <see cref="SkillDef"/> can bind to.</summary>
    public bool HasTick => !string.IsNullOrEmpty(tickId);
    /// <summary>True for step kinds whose lifetime CAN loop (and therefore honour <see cref="looping"/> +
    /// the skill's Stay Duration). A Carrier ends on arrival, so it's never a looping kind.</summary>
    public bool CanLoop => kind == VfxStepKind.PlayAt || kind == VfxStepKind.Attach;
}

/// <summary>Where in the world a <see cref="VfxSequence"/> plays. The skill fills this per cast; the sequence's
/// per-step anchors resolve against it. <see cref="scaleMult"/> scales every effect (e.g. a crit punch, AoE sizing).</summary>
public struct VfxContext
{
    /// <summary>Caster / spawn origin at CAST time — the fallback for anchor <see cref="VfxAnchor.Origin"/> when
    /// no <see cref="follow"/> transform is present. When follow IS present, Origin samples it live at fire time.</summary>
    public Vector3 origin;
    /// <summary>Impact / aim point (anchor <see cref="VfxAnchor.Target"/>).</summary>
    public Vector3 target;
    /// <summary>Unit an <see cref="VfxStepKind.Attach"/> step follows (auras/shields). Null for point effects.</summary>
    public Transform follow;
    /// <summary>The cast's designated target for a single-target tick binding (the one entity it lands on,
    /// range-independent). Null = single-scope bindings fall back to Area.</summary>
    public Transform targetUnit;
    /// <summary>Stage 2 of the aim: where a RELOCATING effect (Dash / Teleport) sends whoever it lands on. Its own
    /// field, deliberately NOT <see cref="target"/> — once a cast can pick an entity AND a point ("dash the mob I
    /// clicked over there"), the thing aimed AT and the spot it's sent TO are two different values. <see cref="target"/>
    /// keeps its one meaning: where the VFX plays.</summary>
    public Vector3 effectPoint;
    /// <summary>Uniform scale applied to every effect in the sequence (1 = authored).</summary>
    public float scaleMult;
    /// <summary>TRUE when this play is a REPEAT of a cast that already started, not the cast's first play. A repeat
    /// replays the whole skill body, so without this the effects that SET UP the run — the stacking states it is
    /// spending — would be re-applied on every beat and the run could never end.</summary>
    public bool isRepeat;
    /// <summary>World Y of the FLOOR under the actor. A character's <c>transform.position</c> is its billboard
    /// VISUAL CENTRE, but the Vfx Composer authors every effect from the ground (the ruler stands on the floor),
    /// so cues resolve this and anchor there — otherwise a ground-authored aura rides up to the character's head.
    /// 0 by default (world ground standard is Y0); <c>VfxCue</c> raycasts the real floor and fills it.</summary>
    public float groundY;
    /// <summary>True when the caller holds this play alive (a status aura via <c>VfxCue.Begin</c>) and will
    /// <c>Stop()</c> it explicitly. An <see cref="VfxStepKind.Attach"/> step then loops until stopped and the
    /// runner never auto-tears-down. False (the default, one-shot <c>Fire</c>) = the Attach plays its authored
    /// lifetime once, then ends with the sequence.</summary>
    public bool persistent;
    /// <summary>Skill-supplied PER-STEP cap (seconds) for a LOOPING PlayAt/Attach step, keyed by the step's tickId:
    /// how long it stays before it ends and its End tick fires. Returns ≤0 → use the runtime's default fallback
    /// (3s). Null → all looping steps use the fallback. The VFX owns the loop RULE; the skill owns HOW LONG each use
    /// of it lasts (per step) — so one VFX serves every skill.</summary>
    public System.Func<string, float> stayForTick;

    /// <summary>A fixed-point context (origin == target == point, scale 1).</summary>
    public static VfxContext At(Vector3 point) =>
        new VfxContext { origin = point, target = point, effectPoint = point, follow = null, scaleMult = 1f };
}

/// <summary>One gameplay beat raised by a <see cref="VfxSequence"/> at runtime — the join between the VFX
/// timeline and the skill. Carries WHICH component ticked (<see cref="tickId"/>) and WHERE
/// (<see cref="point"/>) so the skill can resolve targets there. The skill decides WHAT it does.
///
/// <para>It also carries THE AIM THE PLAY WAS STARTED WITH (<see cref="aimUnit"/> / <see cref="aimPoint"/>), copied
/// from that play's own context. Without it a skill has to read the aim off a shared field, which is wrong the moment
/// one cast has more than one play in flight: a repeating cast that re-aims between beats would resolve a still-running
/// beat's effects against the NEWER aim. Every beat now answers for the play it belongs to.</para></summary>
public readonly struct VfxTick
{
    public readonly string tickId;
    public readonly Vector3 point;
    /// <summary>The unit this play was aimed at (null for a pure ground cast) — the entity a Single-target effect lands on.</summary>
    public readonly Transform aimUnit;
    /// <summary>Where this play's relocating effects send what they catch — the aimed spot, not the tick's own point.</summary>
    public readonly Vector3 aimPoint;
    /// <summary>TRUE when the play that raised this beat is a REPEAT of an already-running cast. See
    /// <see cref="VfxContext.isRepeat"/>.</summary>
    public readonly bool isRepeat;

    public VfxTick(string tickId, Vector3 point, Transform aimUnit, Vector3 aimPoint, bool isRepeat)
    {
        this.tickId = tickId; this.point = point; this.aimUnit = aimUnit; this.aimPoint = aimPoint;
        this.isRepeat = isRepeat;
    }
}
