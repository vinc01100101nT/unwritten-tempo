using UnityEngine;

/// <summary>
/// THE NUMBERS OF EVERYTHING THAT FLIES — an arrow, a thrown shuriken, a skill strike that bounces — and the plain
/// arithmetic over them. Numbers in, numbers out, so the game-maths checks pin every one without standing the game up.
/// The flight store (<see cref="Flights"/>) and nothing else asks these on the machine that owns the world; a watching
/// machine only draws the times it is sent.
/// </summary>
public static class FlightMath
{
    /// <summary>How fast anything flies, in world units a second — an arrow, a blade, its return. One number for every
    /// flight in the game, so the same gap takes the same time whoever throws across it.</summary>
    public const float Speed = 7f;

    /// <summary>The most jumps one chain can make, whatever adds to it — the size of the list of units it has already
    /// hit. An authored count past it is refused by the skill-data check, and a buff that would push past it is capped
    /// here.</summary>
    public const int MaxJumps = 12;

    // A THROWN SHURIKEN (decided 2026-10-06): three jumps, each to the nearest enemy it has not hit yet within 2.5 of
    // the last one, each worth 75% of the one before, then it flies back to the hand.
    public const int ThrowJumps = 3;
    public const float ThrowJumpRange = 2.5f;
    public const float ThrowDamageKept = 0.75f;

    /// <summary>How long one leg from <paramref name="from"/> to <paramref name="to"/> is in the air.</summary>
    public static float LegSeconds(Vector3 from, Vector3 to) => Vector3.Distance(from, to) / Speed;

    /// <summary>What hit number <paramref name="hop"/> of a chain is worth, as a multiple of the first: the first hit
    /// (hop 0) is worth 1, and each jump keeps <paramref name="kept"/> of the one before.</summary>
    public static float HopScale(float kept, int hop) => Mathf.Pow(Mathf.Clamp01(kept), Mathf.Max(0, hop));

    /// <summary>How many jumps a chain makes: its own count plus the whole jumps a buff adds, capped at
    /// <see cref="MaxJumps"/>. A chain whose own count is 0 does not bounce at all, whatever is added — a buff adds
    /// jumps to bouncing, it does not make an arrow bounce.</summary>
    public static int TotalJumps(int own, float extra)
    {
        if (own <= 0) return 0;
        return Mathf.Clamp(own + Mathf.FloorToInt(Mathf.Max(0f, extra) + 0.001f), 0, MaxJumps);
    }
}
