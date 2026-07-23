using UnityEngine;

/// <summary>
/// The ONE source of combat-OUTCOME randomness — crit, damage spread, accuracy (HIT vs FLEE),
/// proc chance, off-hand block, and mob attack rolls all draw from HERE instead of calling
/// <see cref="UnityEngine.Random"/> directly. Centralized the same way <c>Hotkeys.cs</c> wraps raw
/// input: so the roll source can be made server-seeded / deterministic in a SINGLE place when
/// networking lands. A server-authoritative build swaps the backend in this file and every combat
/// die becomes reproducible and server-owned, with zero call-site churn.
///
/// <para><b>Scope:</b> only rolls that DECIDE a combat result route here. Movement/wander,
/// <c>insideUnitCircle</c>, and cosmetic/VFX randomness stay on <see cref="UnityEngine.Random"/> —
/// they don't need to agree across the wire.</para>
/// </summary>
public static class CombatRng
{
    /// <summary>A roll in [0, 1). Crit, block, proc, and accuracy compare against this.</summary>
    public static float Value01() => Random.value;

    /// <summary>Integer roll in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>) —
    /// same semantics as <see cref="UnityEngine.Random.Range(int,int)"/> (e.g. a damage spread rolls
    /// <c>Range(min, max + 1)</c> to include max).</summary>
    public static int Range(int minInclusive, int maxExclusive) => Random.Range(minInclusive, maxExclusive);

    /// <summary>Float roll in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>] —
    /// same semantics as <see cref="UnityEngine.Random.Range(float,float)"/> (mob damage spread).</summary>
    public static float Range(float minInclusive, float maxExclusive) => Random.Range(minInclusive, maxExclusive);

    /// <summary>True with probability <paramref name="p"/> — the shared shape of every combat chance
    /// roll (crit / block / proc / accuracy). Equivalent to <c>Value01() &lt; p</c>.</summary>
    public static bool Chance(float p) => Random.value < p;
}
