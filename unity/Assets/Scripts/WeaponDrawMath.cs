/// <summary>
/// The numbers of drawing and putting away a weapon, in one place every unit reads — the shared spec for combat and
/// peace mode, never a value typed onto a unit. Plain numbers in, plain numbers out, so each one is pinned by the
/// game-maths checks without the game running.
/// </summary>
public static class WeaponDrawMath
{
    /// <summary>The fastest a draw or a sheathe ever is — empty hands included, so a phase always lasts several ticks
    /// and a request flood cannot flip a unit's mode every frame.</summary>
    public const float MinSeconds = 0.2f;

    /// <summary>What each point of a weapon's weight adds to drawing or putting it away.</summary>
    public const float SecondsPerWeight = 0.006f;

    /// <summary>The least time between two draws that grant the weapon's draw buff. Longer than any draw buff lasts,
    /// so drawing over and over can never keep one up.</summary>
    public const float DrawBuffSpacingSeconds = 15f;

    /// <summary>How long a sheathed unit must go without dealing or taking a blow before the peace bonuses pay.</summary>
    public const float QuietSpellSeconds = 5f;

    /// <summary>What peace multiplies a unit's NATURAL health and mana recovery by — never a healing skill's.</summary>
    public const float PeaceRegenMultiplier = 2f;

    /// <summary>What peace multiplies the distance a creature notices this unit from by.</summary>
    public const float PeaceNoticeMultiplier = 0.5f;

    /// <summary>Seconds to draw, or to put away, a weapon weighing <paramref name="weight"/> (0 = empty hands).</summary>
    public static float DrawSeconds(int weight) => MinSeconds + SecondsPerWeight * (weight > 0 ? weight : 0);

    /// <summary>Are the peace bonuses paying: sheathed, and <see cref="QuietSpellSeconds"/> since the last blow
    /// dealt or taken.</summary>
    public static bool PeaceBonusesOn(bool sheathed, float now, float lastBlowTime)
        => sheathed && now - lastBlowTime >= QuietSpellSeconds;

    /// <summary>The natural recovery rate per second, peace included.</summary>
    public static float NaturalRegen(float ratePerSecond, bool peace) => peace ? ratePerSecond * PeaceRegenMultiplier : ratePerSecond;

    /// <summary>The whole recovery rate per second: the natural rate (peace included) plus a timed change's bonus,
    /// which peace never multiplies.</summary>
    public static float RegenPerSecond(float naturalPerSecond, bool peace, float timedBonus)
        => NaturalRegen(naturalPerSecond, peace) + timedBonus;

    /// <summary>The distance a creature notices a unit from, peace included.</summary>
    public static float NoticeRadius(float radius, bool peace) => peace ? radius * PeaceNoticeMultiplier : radius;

    /// <summary>A squared distance as a creature weighs it when choosing whom to notice: a unit at peace is noticed
    /// from <see cref="PeaceNoticeMultiplier"/> of the distance, so it counts as that much further away.</summary>
    public static float NoticedSqr(float sqr, bool peace)
        => peace ? sqr / (PeaceNoticeMultiplier * PeaceNoticeMultiplier) : sqr;

    /// <summary>How much faster than a run the server lets a drawn weapon travel before it counts as a sprint — a
    /// measured speed is noisy, so a margin, never the run speed itself.</summary>
    public const float SprintMargin = 1.3f;

    /// <summary>Was a measured speed a sprint rather than a run, given the run speed and the unit's live
    /// move-speed multiplier (a Haste lifts the run, a Slow never lowers the line).</summary>
    public static bool FasterThanRunning(float measured, float runSpeed, float moveMult)
        => measured > runSpeed * (moveMult > 1f ? moveMult : 1f) * SprintMargin;

    /// <summary>The slack added to a move the server ordered before a fast step counts as a sprint again — the owner's
    /// reports of the move trail it by a few of their sends.</summary>
    public const float OrderedMoveSlackSeconds = 0.5f;

    /// <summary>How long a move the server ordered stays exempt from the sprint check: its travel (distance over speed)
    /// plus its ease time, capped at <paramref name="longest"/>, plus the slack.</summary>
    public static float OrderedMoveSeconds(float distance, float speed, float easeSeconds, float longest)
    {
        float travel = speed > 0.1f ? distance / speed : distance / 0.1f;
        float lasts = travel + (easeSeconds > 0f ? easeSeconds : 0f);
        return (lasts < longest ? lasts : longest) + OrderedMoveSlackSeconds;
    }

    /// <summary>How fast a move the server ordered travels: the run gear's speed × the glide's own speed multiplier.
    /// 0 for a move that is not a glide, which then leaves the movement check's ceiling as it was.</summary>
    public static float OrderedMoveSpeed(float runSpeed, float glideSpeedMult) =>
        glideSpeedMult > 0f ? runSpeed * glideSpeedMult : 0f;

    /// <summary>The share of a spin's window spent lifting the weapon off the fist at its start, and the same share
    /// settling it back at its end.</summary>
    public const float SpinLiftEaseShare = 0.2f;

    /// <summary>How far through a spin's window a put-away or take-out is (0..1) at <paramref name="progress"/> of the
    /// move; 0 before the window, after it, and for a window with no length.</summary>
    public static float SpinWindowShare(float progress, float from, float to)
        => to <= from || progress <= from || progress >= to ? 0f : (progress - from) / (to - from);

    /// <summary>How much of its turning a spun weapon has done (0..1) at <paramref name="windowShare"/> of the window:
    /// it starts slowly, is fastest in the middle and slows to a stop.</summary>
    public static float SpinTurnShare(float windowShare) => windowShare * windowShare * (3f - 2f * windowShare);

    /// <summary>How much of its lift off the fist a spun weapon has (0..1) at <paramref name="windowShare"/> of the
    /// window: it rises over the first <see cref="SpinLiftEaseShare"/>, stays up, and settles over the last.</summary>
    public static float SpinLiftShare(float windowShare)
    {
        float e = windowShare < SpinLiftEaseShare ? windowShare / SpinLiftEaseShare
                : windowShare > 1f - SpinLiftEaseShare ? (1f - windowShare) / SpinLiftEaseShare : 1f;
        return e * e * (3f - 2f * e);
    }

    /// <summary>May a draw grant its buff now: never before, or at least the spacing since the last one.</summary>
    public static bool DrawBuffDue(float now, float lastBuffTime, bool never) => never || now - lastBuffTime >= DrawBuffSpacingSeconds;
}
