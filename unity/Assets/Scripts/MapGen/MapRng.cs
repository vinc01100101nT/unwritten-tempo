using System.Collections.Generic;

/// <summary>
/// Deterministic RNG for map generation — same seed = same town, every run.
/// Wraps System.Random (independent of UnityEngine.Random global state).
/// </summary>
public class MapRng
{
    readonly System.Random r;

    public MapRng(int seed) { r = new System.Random(seed); }

    /// <summary>Integer in [minInclusive, maxExclusive).</summary>
    public int Range(int minInclusive, int maxExclusive) => r.Next(minInclusive, maxExclusive);

    /// <summary>Float in [0,1).</summary>
    public float Value => (float)r.NextDouble();

    /// <summary>Float in [min, max).</summary>
    public float Range(float min, float max) => min + (float)r.NextDouble() * (max - min);

    /// <summary>True with probability p (0..1).</summary>
    public bool Chance(float p) => r.NextDouble() < p;

    /// <summary>Random element, or default(T)/null if the list is empty.</summary>
    public T Pick<T>(IReadOnlyList<T> list)
        => (list == null || list.Count == 0) ? default : list[r.Next(list.Count)];
}
