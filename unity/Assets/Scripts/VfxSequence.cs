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
/// — a stable component name (e.g. "meteor_ball", "fire_pillar") that fires <see cref="VfxStep.tickCount"/> times,
/// <see cref="VfxStep.tickInterval"/> apart, at the step's gameplay moment (a Carrier's arrival, or any other
/// step's start). A <see cref="SkillDef"/> in the Skill Composer binds each tickId to WHAT it does
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
    /// <summary>The caster / spawn origin (<see cref="VfxContext.origin"/>).</summary>
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
    [Tooltip("How many times this step raises its tick. 1 = one-shot (a meteor's single land). N = a lingering " +
             "component's beats (a fire pillar ticking damage). 0 or empty tickId = no gameplay tick.")]
    public int tickCount = 1;
    [Tooltip("Seconds between ticks (0 = all at once). tickCount × tickInterval = the component's active window. " +
             "This is the 'repeat over time' owner — the skill applies ONE burst per tick.")]
    public float tickInterval = 0f;
    [Tooltip("Auto-fill tickCount from the effect's duration: tickCount = floor(duration / tickInterval). Keeps a " +
             "lingering component ticking for its whole visible lifetime without hand-counting.")]
    public bool spreadTicks = false;

    [Header("Carrier")]
    [Tooltip("Launch point offset from the anchor (e.g. (0,9,0) = fall from 9 units up).")]
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
    public bool HasTick => !string.IsNullOrEmpty(tickId) && tickCount > 0;
}

/// <summary>Where in the world a <see cref="VfxSequence"/> plays. The skill fills this per cast; the sequence's
/// per-step anchors resolve against it. <see cref="scaleMult"/> scales every effect (e.g. a crit punch, AoE sizing).</summary>
public struct VfxContext
{
    /// <summary>Caster / spawn origin (anchor <see cref="VfxAnchor.Origin"/>).</summary>
    public Vector3 origin;
    /// <summary>Impact / aim point (anchor <see cref="VfxAnchor.Target"/>).</summary>
    public Vector3 target;
    /// <summary>Unit an <see cref="VfxStepKind.Attach"/> step follows (auras/shields). Null for point effects.</summary>
    public Transform follow;
    /// <summary>The cast's designated target for a single-target tick binding (the one entity it lands on,
    /// range-independent). Null = single-scope bindings fall back to Area.</summary>
    public Transform targetUnit;
    /// <summary>Uniform scale applied to every effect in the sequence (1 = authored).</summary>
    public float scaleMult;
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

    /// <summary>A fixed-point context (origin == target == point, scale 1).</summary>
    public static VfxContext At(Vector3 point) =>
        new VfxContext { origin = point, target = point, follow = null, scaleMult = 1f };
}

/// <summary>One gameplay beat raised by a <see cref="VfxSequence"/> at runtime — the join between the VFX
/// timeline and the skill. Carries WHICH component ticked (<see cref="tickId"/>) and WHERE
/// (<see cref="point"/>) so the skill can resolve targets there. The skill decides WHAT it does.</summary>
public readonly struct VfxTick
{
    public readonly string tickId;
    public readonly Vector3 point;
    public VfxTick(string tickId, Vector3 point) { this.tickId = tickId; this.point = point; }
}
