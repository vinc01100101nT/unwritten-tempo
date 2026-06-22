using UnityEngine;

/// <summary>
/// An <b>authored</b> weather timeline as a ScriptableObject — the Inspector-editable, live-tunable twin of one of the
/// hard-coded timelines in <see cref="WeatherSequences"/>. This is the "data source moves out of code" layer: the
/// channel + sequencer <i>engine</i> (<see cref="WeatherChannels"/>) is unchanged; an asset just feeds it the same
/// <see cref="WeatherSequence"/> it always consumed. Each asset says "I author the <see cref="preset"/> weather" and
/// lists its keyframes — <c>{ at, channel, target, ramp }</c> — exactly like a <c>K(...)</c> row in code, but tweakable
/// without a recompile.
///
/// <para><b>How it's used.</b> A <see cref="WeatherSequenceLibrary"/> collects these and resolves <c>preset → asset</c>;
/// <see cref="WeatherSequences.For"/> prefers an authored asset and falls back to the code timeline when none exists.
/// So assets are purely additive — the game runs identically with zero assets (the current state), and each asset you
/// create later overrides exactly one preset.</para>
///
/// <para><b>Live tuning.</b> <see cref="ToSequence"/> caches the built <see cref="WeatherSequence"/> (so a day-boundary
/// weather change allocates nothing), and <see cref="OnValidate"/> drops that cache whenever you edit the asset, so the
/// next weather change picks up your new numbers immediately — drag a slider, trigger the weather, see it. Keys may be
/// authored in any order; <see cref="ToSequence"/> sorts them by time (stably) the way the sequencer expects.</para>
/// </summary>
[CreateAssetMenu(fileName = "Weather_", menuName = "unwritten/Weather/Sequence", order = 0)]
public class WeatherSequenceAsset : ScriptableObject
{
    [Tooltip("Which weather preset this timeline authors. The library resolves preset → asset; the matching preset's " +
             "hard-coded timeline is overridden by this asset.")]
    public WeatherDirector.Preset preset = WeatherDirector.Preset.Clear;

    [Tooltip("HUD/readout name for this sequence. Leave blank to use the asset's file name.")]
    public string displayName = "";

    [Tooltip("The timeline: each key aims a channel at a 0..1 target, easing over 'ramp' seconds, starting at 'at' " +
             "seconds into the event. A channel HOLDS its last target between keys, so an after-rain puddle linger is " +
             "just a late Puddle→0 key. Order doesn't matter here — they're sorted by time when built.")]
    public WeatherKeyData[] keys = new WeatherKeyData[0];

    // Built once and reused so a weather change (which calls ToSequence) produces no GC. Dropped on edit (OnValidate).
    WeatherSequence cached;

    /// <summary>Convert this authored asset into the runtime <see cref="WeatherSequence"/> the sequencer consumes —
    /// cached after the first call (rebuilt only when the asset is edited). Keys are copied into a fresh array sorted
    /// by <see cref="WeatherKeyData.at"/> with a stable sort (equal times keep their authored order).</summary>
    public WeatherSequence ToSequence()
    {
        if (cached != null) return cached;

        int n = keys != null ? keys.Length : 0;
        var arr = new WeatherKey[n];
        for (int i = 0; i < n; i++)
            arr[i] = new WeatherKey(keys[i].at, keys[i].channel, Mathf.Clamp01(keys[i].target), Mathf.Max(0.01f, keys[i].ramp));

        // Stable insertion sort by time (arrays are tiny — at most a handful of keys — and the sequencer requires
        // ascending 'at'). Insertion sort preserves the authored order of equal-time keys, unlike Array.Sort.
        for (int i = 1; i < n; i++)
        {
            var key = arr[i];
            int j = i - 1;
            while (j >= 0 && arr[j].at > key.at) { arr[j + 1] = arr[j]; j--; }
            arr[j + 1] = key;
        }

        cached = new WeatherSequence(string.IsNullOrEmpty(displayName) ? name : displayName, arr);
        return cached;
    }

#if UNITY_EDITOR
    // Editing any field in the Inspector invalidates the cache so the next ToSequence rebuilds with the new values
    // (live re-tune: change a number, fire the weather, see it — no recompile, no re-bake).
    void OnValidate()
    {
        cached = null;
        WeatherSequenceLibrary.InvalidateCache();
    }
#endif
}

/// <summary>Inspector-serializable mirror of <see cref="WeatherKey"/> (the runtime struct isn't marked
/// <c>[System.Serializable]</c> and uses bare fields tuned for the sequencer). One row of an authored timeline.</summary>
[System.Serializable]
public struct WeatherKeyData
{
    [Tooltip("Seconds into the event when this key fires.")]
    public float at;

    [Tooltip("Which channel this key drives.")]
    public WeatherChannel channel;

    [Range(0f, 1f)] [Tooltip("Where to aim the channel (0 = off, 1 = full).")]
    public float target;

    [Tooltip("Seconds for a full 0→1 sweep — the ease speed toward the target (same units as the old overlay rampTime).")]
    public float ramp;

    public WeatherKeyData(float at, WeatherChannel channel, float target, float ramp)
    { this.at = at; this.channel = channel; this.target = target; this.ramp = ramp; }
}
