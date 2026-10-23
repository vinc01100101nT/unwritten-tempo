using UnityEngine;

/// <summary>
/// The presentation SINK that <see cref="StanceController"/> drives — the seam the choke point poses. The live-3D
/// skinned-model sink <see cref="Character3DPose"/> implements it (players + townsfolk; mobs self-animate through
/// <see cref="BillboardAnimator3D"/> and don't go through here). StanceController resolves WHICH
/// <see cref="CharacterAction"/> + which clip + speed the character is in and pushes it here every frame; the
/// implementer just renders it. Kept an interface so the brain and choke point stay decoupled from the render tech.
///
/// <para>Each call carries BOTH the <see cref="CharacterAction"/> and the authored <c>clip</c> name: the live-3D sink
/// CrossFades to the Animator state named by <c>clip</c> (the Stance Composer owns it, no hardcoded action→clip map).
/// An empty/unknown clip means "no clip" — it just stands.</para>
/// </summary>
public interface IStancePose
{
    /// <summary>Play (hold) this action / clip. Called every frame the action is active; idempotent per frame.</summary>
    void Play(CharacterAction a, string clip);

    /// <summary>Play this action / clip STARTING part-way through it, at <paramref name="normalizedStart"/> (0→1 of
    /// the clip). For a body watched from another machine that joins an action already in progress: the server said
    /// when it began, so the body picks it up at the right frame instead of replaying it from the top. Called once,
    /// on the frame the action is joined; <see cref="Play"/> holds it from then on.</summary>
    void PlayFrom(CharacterAction a, string clip, float normalizedStart);

    /// <summary>Drive a jump/fall clip with a freeze point (in SECONDS into the clip). <paramref name="releaseToEnd"/>
    /// false = play forward but HOLD once the clip reaches <paramref name="freezeSeconds"/> (the airborne pose); true =
    /// resume from that frame and play to the end once (the landing tail). Used for the Jump + Fall stances.
    /// <paramref name="mult"/> is the stance's × mult — the crouch/landing play at that speed; the airborne HOLD stays 0.
    /// <paramref name="blendSeconds"/> above 0 = glide from the current pose straight into the freeze frame over that
    /// many real seconds, then hold (a bow drawn across the whole cast); 0 = play the clip up to the freeze frame.</summary>
    void PlayClipPhase(CharacterAction src, string clip, float freezeSeconds, bool releaseToEnd, float mult = 1f,
                        bool restart = false, float blendSeconds = 0f);

    /// <summary>Scale the current action's playback speed (1 = natural). Used for FitWindow / stat-synced stances.</summary>
    void SetActionSpeed(CharacterAction a, float multiplier);

    /// <summary>Does this visual have art/clip for the action/clip? (Lets the resolver fall back.)</summary>
    bool HasAction(CharacterAction a, string clip);

    /// <summary>The clip's natural play length in seconds (0 if absent) — so a FitWindow stance can fit it.</summary>
    float NaturalDuration(CharacterAction a, string clip);

    /// <summary>The clip's authored FRAME COUNT (0 if absent) — so a caller can turn an authored frame index into a
    /// normalized point in the clip (frame ÷ this), independent of any FitWindow time-rescale.</summary>
    int NaturalFrameCount(CharacterAction a, string clip);

    /// <summary>Hand back to the visual's own movement-sampled Idle/Walk/Run (self-driving units).</summary>
    void ResumeSelfDrive();

    /// <summary>Which stance is shown this frame. The held weapon's standing idle shows only on Idle and its carry
    /// pose only on the travel gears (Idle/Walk/Run/Sprint); every other stance plays its own whole-body clip. Told
    /// every frame by the stance choke point, before the stance is played.</summary>
    void SetShownStance(StanceId id);

    /// <summary>The character's current rendered world-space height (0 before it's renderable) — lets gameplay place
    /// head-anchored UI (e.g. an NPC speech bubble) without caring how the visual renders.</summary>
    float VisualWorldHeight { get; }

    /// <summary>Aim the visual at a world-space XZ direction (the 3D model yaws toward it).
    /// Lets gameplay turn a unit to face something — e.g. a talked-to townsperson turning to the player.
    /// <paramref name="snap"/> turns it there at once instead of at its turning speed.</summary>
    void SetFacing(Vector3 worldDir, bool snap = false);
}
