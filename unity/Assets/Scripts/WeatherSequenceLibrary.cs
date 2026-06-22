using UnityEngine;

/// <summary>
/// The single registry that maps each <see cref="WeatherDirector.Preset"/> to its authored
/// <see cref="WeatherSequenceAsset"/>. This is how the ScriptableObject authoring layer plugs into the unchanged
/// engine: <see cref="WeatherSequences.For"/> asks <see cref="Resolve"/> for an authored timeline first, and only
/// falls back to the hard-coded one when the preset has no asset. So the game is identical with no library at all
/// (today's state), and authoring assets later progressively moves presets out of code — one at a time, no engine change.
///
/// <para><b>Loading.</b> The live library is the one at <c>Resources/WeatherSequenceLibrary</c> (so it loads with zero
/// scene wiring — nothing to drag onto the Systems rig). The lookup is cached after the first access: if no library
/// exists the miss is remembered too, so a day-boundary weather change costs one null check, never a repeated
/// <see cref="Resources.Load"/>. The cache is reset on edit (so creating/editing the library in the Editor takes
/// effect immediately) and on domain reload (statics reset on Play/recompile).</para>
/// </summary>
[CreateAssetMenu(fileName = "WeatherSequenceLibrary", menuName = "unwritten/Weather/Sequence Library", order = 1)]
public class WeatherSequenceLibrary : ScriptableObject
{
    /// <summary>The Resources path the runtime loads the live library from (must sit at <c>Assets/Resources/&lt;this&gt;</c>).</summary>
    public const string ResourcePath = "WeatherSequenceLibrary";

    [Tooltip("Every authored timeline. Resolution is by each asset's own 'preset' field, so order here doesn't matter; " +
             "if two assets claim the same preset the first one wins.")]
    public WeatherSequenceAsset[] sequences = new WeatherSequenceAsset[0];

    // ── cached static resolution (not a hot path — only on a weather change/load — but kept allocation-free) ────────
    static WeatherSequenceLibrary cachedLib;
    static bool triedLoad;

    /// <summary>The authored timeline for <paramref name="p"/>, or <c>null</c> if no library/asset covers it (caller
    /// then uses the code fallback). Built via <see cref="WeatherSequenceAsset.ToSequence"/> (itself cached).</summary>
    public static WeatherSequence Resolve(WeatherDirector.Preset p)
    {
        var lib = Active();
        if (lib == null) return null;
        var asset = lib.Find(p);
        return asset != null ? asset.ToSequence() : null;
    }

    /// <summary>The live library from Resources, or <c>null</c> if there isn't one. Cached (including the miss).</summary>
    public static WeatherSequenceLibrary Active()
    {
        if (cachedLib != null) return cachedLib;
        if (triedLoad) return null;
        triedLoad = true;
        cachedLib = Resources.Load<WeatherSequenceLibrary>(ResourcePath);
        return cachedLib;
    }

    /// <summary>The asset that authors <paramref name="p"/> (matched on each asset's own <c>preset</c> field), or null.</summary>
    public WeatherSequenceAsset Find(WeatherDirector.Preset p)
    {
        if (sequences == null) return null;
        for (int i = 0; i < sequences.Length; i++)
            if (sequences[i] != null && sequences[i].preset == p) return sequences[i];
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Drop the cached library lookup so the next <see cref="Resolve"/> reloads — called when a library or
    /// any sequence asset is edited/created in the Editor, so changes take effect without entering Play twice.</summary>
    public static void InvalidateCache() { cachedLib = null; triedLoad = false; }

    void OnValidate() { InvalidateCache(); }
#endif
}
