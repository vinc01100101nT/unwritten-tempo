/// <summary>
/// The presentation SINK that <see cref="StanceController"/> drives — the seam that lets ONE choke point
/// pose either a baked billboard (<see cref="BakedBillboard8"/>) or a live 3D skinned model
/// (<see cref="Character3DPose"/>). StanceController resolves WHICH <see cref="CharacterAction"/> + which clip +
/// speed the character is in and pushes it here every frame; the implementer just renders it. Swapping billboard ⇄ 3D
/// is then a matter of which IStancePose component sits on the visual — the brain and the choke point don't change.
///
/// <para>Each call carries BOTH the <see cref="CharacterAction"/> and the authored <c>clip</c> name because the two
/// sinks key differently: the 2D billboard selects a baked sprite set by <see cref="CharacterAction"/> (ignores
/// clip), while the live-3D sink CrossFades to the Animator state named by <c>clip</c> (the Stance Composer owns it,
/// no hardcoded action→clip map). An empty/unknown clip on the 3D sink means "no clip" — it just stands.</para>
/// </summary>
public interface IStancePose
{
    /// <summary>Play (hold) this action / clip. Called every frame the action is active; idempotent per frame.</summary>
    void Play(CharacterAction a, string clip);

    /// <summary>Drive a jump/fall clip with a freeze point (in SECONDS into the clip). <paramref name="releaseToEnd"/>
    /// false = play forward but HOLD once the clip reaches <paramref name="freezeSeconds"/> (the airborne pose); true =
    /// resume from that frame and play to the end once (the landing tail). Used for the Jump + Fall stances.
    /// <paramref name="mult"/> is the stance's × mult — the crouch/landing play at that speed; the airborne HOLD stays 0.</summary>
    void PlayClipPhase(CharacterAction src, string clip, float freezeSeconds, bool releaseToEnd, float mult = 1f);

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
}
