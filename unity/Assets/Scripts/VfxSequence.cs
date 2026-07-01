using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A DATA-DRIVEN recipe for one composed visual effect — the "look" of a skill beat surfaced out of code and
/// into an asset you can tune in the inspector. A sequence is a FLAT list of <see cref="VfxStep"/>s on a
/// stopwatch: each step says "at this moment, play this library effect / shake / sound, at this anchor". It
/// owns PRESENTATION only (particles, screen-shake, sound) — never damage. Gameplay stays in the skill and
/// meets the sequence at named EVENT tags (see <see cref="VfxStep.emitEvent"/> / <see cref="VfxStep.startEvent"/>).
///
/// <para>Play one with <see cref="VfxDirector.Play"/>, passing a <see cref="VfxContext"/> (where in the world)
/// and an optional <c>onEvent</c> callback (the skill's hook — e.g. deal damage when the "impact" tag fires).</para>
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
}

/// <summary>What a single step does.</summary>
public enum VfxStepKind
{
    /// <summary>One-shot burst at a world point (hits, explosions, magic circles).</summary>
    PlayAt,
    /// <summary>Looping effect that tracks the context's follow target (auras, shields). Stopped when the sequence ends.</summary>
    Attach,
    /// <summary>A traveling projectile-carried effect that flies from a launch point to the anchor, then raises its
    /// <see cref="VfxStep.emitEvent"/> ON ARRIVAL (a meteor fall, a homing bolt). Reuses <see cref="Projectile3D"/>.</summary>
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
    /// <summary>When a named event fires (a carrier arrival, or another step's <see cref="VfxStep.emitEvent"/>).</summary>
    OnEvent,
}

/// <summary>One entry on a <see cref="VfxSequence"/> timeline. All gameplay-neutral: it can only spawn a
/// library effect, shake the camera, or play a sound — and optionally RAISE a named event that the skill (or
/// later steps) can react to.</summary>
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

    [Header("What")]
    public VfxStepKind kind = VfxStepKind.PlayAt;
    public VfxAnchor anchor = VfxAnchor.Target;
    [Tooltip("World-space nudge from the anchor point.")]
    public Vector3 offset;
    [Tooltip("Euler rotation for the spawned effect (PlayAt).")]
    public Vector3 rotationEuler;

    [Header("Effect (PlayAt / Attach / Carrier)")]
    [Tooltip("EffectLibrary id, e.g. \"aoe.ground-explosion\".")]
    public string effectId;
    [Tooltip("Scale multiplier on the effect's authored scale.")]
    public float scale = 1f;
    [Tooltip("Playback-speed multiplier (PlayAt only).")]
    public float speed = 1f;
    [Tooltip("Colour tint (white = no tint).")]
    public Color tint = Color.white;

    [Header("Event")]
    [Tooltip("Raise this event tag when the step fires — on ARRIVAL for a Carrier, on START otherwise. The " +
             "skill's onEvent callback receives it (to run damage), and OnEvent steps keyed to it also start.")]
    public string emitEvent;

    [Header("Carrier")]
    [Tooltip("Launch point offset from the anchor (e.g. (0,9,0) = fall from 9 units up).")]
    public Vector3 carrierFrom = new Vector3(0f, 9f, 0f);
    [Tooltip("Random horizontal spread of the launch point, so a salvo reads as a shower.")]
    public float carrierScatter;
    public float carrierSpeed = 16f;
    public float carrierArriveRadius = 0.4f;
    [Tooltip("Safety cap: fly no longer than this before force-arriving.")]
    public float carrierMaxLife = 3f;

    [Header("Shake")]
    public float shakeStrength = 0.3f;
    public float shakeDuration = 0.18f;

    [Header("Sound")]
    public SfxSet sound;
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
    /// <summary>Uniform scale applied to every effect in the sequence (1 = authored).</summary>
    public float scaleMult;

    /// <summary>A fixed-point context (origin == target == point, scale 1).</summary>
    public static VfxContext At(Vector3 point) =>
        new VfxContext { origin = point, target = point, follow = null, scaleMult = 1f };
}
