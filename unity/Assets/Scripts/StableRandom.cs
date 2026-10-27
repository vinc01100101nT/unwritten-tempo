/// <summary>
/// RANDOM NUMBERS THAT DEPEND ONLY ON WHAT THEY ARE FOR — a name, and a square of the map — never on what was rolled
/// before them. The same square of the same layer rolls the same numbers on every machine and every build, so adding,
/// removing or changing something in one place cannot move anything anywhere else (a shared <c>System.Random</c> shifts
/// every later roll when one earlier roll is added).
///
/// <para><b>Cost.</b> A handful of integer multiplies per number; nothing allocated.</para>
/// </summary>
public static class StableRandom
{
    /// <summary>A seed from a name (FNV-1a over its characters): the same name, the same seed, everywhere.</summary>
    public static uint Seed(string name)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in name) { h ^= c; h *= 16777619; }
            return h;
        }
    }

    /// <summary>The key of square (<paramref name="i"/>, <paramref name="j"/>) of the layer seeded <paramref name="seed"/>.</summary>
    public static uint Square(uint seed, int i, int j)
    {
        unchecked { return Mix(((ulong)(uint)i << 32 | (uint)j) * 0x9E3779B97F4A7C15UL + seed * 0xD1B54A32D192ED03UL); }
    }

    /// <summary>The <paramref name="n"/>-th number of a square, from 0 up to (not including) 1.</summary>
    public static float Value(uint square, int n)
    {
        unchecked { return (Mix(square * 0x9E3779B97F4A7C15UL + (ulong)(uint)n * 0xD1B54A32D192ED03UL) >> 8) * (1f / 16777216f); }
    }

    // SplitMix64's finaliser: every input bit reaches every output bit.
    static uint Mix(ulong x)
    {
        unchecked
        {
            x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27; x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (uint)(x >> 32);
        }
    }
}
