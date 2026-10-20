/// <summary>
/// WHAT A CREATURE'S MULTIPLIER MULTIPLIES ITS PAY BY — plain arithmetic, so it can be checked by calling it. Everything
/// a kill is worth is multiplied by it: the kill's experience, the Temper Dust and Quench Oil it drops, the chance of
/// its Salts, Inks and own material, and the gold a saved character pays. Design: docs/3-world/what-a-kill-pays.md.
///
/// <para>A creature's level sets its body and the gear it wears; its MULTIPLIER (<see cref="MonsterDef.multiplier"/>) is
/// the one difficulty dial, typed per creature: it multiplies the creature's health, its attack by the cube root
/// (<see cref="CreatureKinds.AttackMultiplier"/>), and its pay, equal to the health it adds (decided 2026-10-20). ×1 pays
/// exactly what the level gives.</para>
/// </summary>
public static class CreaturePay
{
    /// <summary>The lowest multiplier a creature can have: ×1, an ordinary creature of its level.</summary>
    public const float LeastMultiplier = 1f;

    /// <summary>A typed multiplier held to <see cref="LeastMultiplier"/> or more.</summary>
    public static float Clamp(float multiplier) => multiplier >= LeastMultiplier ? multiplier : LeastMultiplier;

    /// <summary>An amount times the multiplier, rounded half up in whole numbers — the one rounding the price writer, the
    /// server and the offline check all use.</summary>
    public static long Scaled(long amount, float multiplier)
    {
        if (amount <= 0) return 0;
        return (long)System.Math.Floor(amount * (double)Clamp(multiplier) + 0.5);
    }

    /// <summary>How many of a thing a kill gives up on average: its ×1 chance times the multiplier. Past 1 it is more
    /// than one piece, never a chance held at certain — so a kill always pays its full multiplier.</summary>
    public static float ExpectedPieces(float chanceAtOne, float multiplier) =>
        chanceAtOne > 0f ? chanceAtOne * Clamp(multiplier) : 0f;

    /// <summary>The pieces one kill gives up for an average of <paramref name="expected"/>: the whole pieces, and one
    /// more when <paramref name="roll01"/> (0 to 1) falls under what is left over.</summary>
    public static int PiecesFor(float expected, float roll01)
    {
        if (expected <= 0f) return 0;
        int whole = (int)System.Math.Floor(expected);
        return whole + (roll01 < expected - whole ? 1 : 0);
    }

    /// <summary>The chance a kill gives up at least one, for an average of <paramref name="expected"/>.</summary>
    public static float ChanceOfAny(float expected) => expected <= 0f ? 0f : expected >= 1f ? 1f : expected;
}
