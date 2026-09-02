using UnityEngine;

/// <summary>
/// The ONE source of combat-OUTCOME randomness — crit, damage spread, accuracy (HIT vs FLEE),
/// proc chance, off-hand block, and mob attack rolls all draw from HERE instead of calling
/// <see cref="UnityEngine.Random"/> directly. Centralized the same way <c>Hotkeys.cs</c> wraps raw
/// input: so the roll source can be made server-seeded / deterministic in a SINGLE place when
/// networking lands. A server-authoritative build swaps the backend in this file and every combat
/// die becomes reproducible and server-owned, with zero call-site churn.
///
/// <para><b>Scope:</b> rolls that DECIDE a result the server would have to own route here — every combat die,
/// plus the loot rolls that decide what a drop IS (<see cref="WeaponSkillRoll"/>'s rarity + kit). Movement/wander,
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

    /// <summary>A NUMBER TO ROLL A WHOLE CAST FROM. Picked once, on the machine that owns the world, and handed to
    /// every machine that has to draw the same cast — so a shower of meteors falls in the same places on every
    /// screen as it fell in on the server that decided who they hit.</summary>
    public static int NewSeed() => Random.Range(int.MinValue, int.MaxValue);

    /// <summary>ONE cast's own run of dice, rolled from a seed everybody was given rather than from the machine's
    /// global stream.
    ///
    /// <para><b>Why a separate stream at all.</b> Where a scattered shot LANDS is a gameplay fact — the tick fires
    /// at that point, so that is where the damage is measured from. Two machines drawing from their own global
    /// randomness therefore agreed about nothing: the picture on a watcher's screen showed the fall in one place
    /// and the server had taken the health off something standing somewhere else.</para>
    ///
    /// <para>A plain struct with its own state, so seeding a cast never disturbs the global stream that crits and
    /// accuracy come from, and no allocation happens per cast. Xorshift: small, fast, and identical on every
    /// machine — which is the whole requirement. It is not a source of combat OUTCOMES and must not be used as
    /// one; those stay on the methods above.</para></summary>
    public struct Stream
    {
        uint state;

        /// <summary>Start a run from <paramref name="seed"/>. The same seed always gives the same run.</summary>
        public Stream(int seed)
        {
            // Never zero — an xorshift seeded with zero stays there forever, which would put every scattered shot
            // in one spot and read as "scatter is broken" rather than as "the seed was bad".
            state = (uint)seed;
            if (state == 0u) state = 0x9E3779B9u;
        }

        uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>A roll in [0, 1).</summary>
        public float Value01() => (Next() >> 8) * (1f / 16777216f);

        /// <summary>A roll in [<paramref name="min"/>, <paramref name="max"/>] — the same shape
        /// <see cref="UnityEngine.Random.Range(float,float)"/> has, so a call site reads the same after the swap.</summary>
        public float Range(float min, float max) => min + (max - min) * Value01();
    }
}
