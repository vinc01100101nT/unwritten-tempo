using UnityEngine;

/// <summary>
/// The single source of <b>wind</b> for foliage sway. Writes one shader global, <c>_GlobalWind</c>, each frame —
/// read by the <c>Unwritten/FoliageWind</c> shader on every swaying plant — so the whole map leans and flutters
/// the same way, in sync, at <b>zero per-object cost</b> (one <see cref="Shader.SetGlobalVector"/> per frame; the
/// animation itself runs in the GPU vertex stage).
///
/// <para>Direction + base speed come from the <see cref="CloudShadows"/> wind (<c>windDirDeg</c> / <c>windSpeed</c>),
/// the same wind that drifts the cloud shadows — so plants lean the way the clouds move, and changing that
/// wind steers both at once. Strength ramps up with the Rain weather channel, so
/// a storm whips the grass harder than a calm afternoon.</para>
///
/// <para>Lives on the persistent Systems Sun (bootstrapped by <see cref="DayNightLight"/>, and added explicitly by
/// SystemsSceneBuilder). In play mode the global is seeded to a gentle breeze before the first frame so foliage is
/// never frozen flat. While <b>authoring</b> (edit/Scene view, not playing) the wind is forced to zero strength so
/// the trees stand perfectly still — sway only happens in Play.</para>
/// </summary>
[ExecuteAlways]   // so we can actively FREEZE the wind in edit mode (LateUpdate runs in the editor too)
[DisallowMultipleComponent]
public class WindGlobals : MonoBehaviour
{
    public static WindGlobals Instance { get; private set; }

    [Tooltip("Master multiplier on ALL foliage sway (0 = perfectly still).")]
    [Range(0f, 3f)] public float swayScale = 1f;

    [Tooltip("Sway strength on a calm, clear day (before any storm boost).")]
    [Range(0f, 2f)] public float baseStrength = 0.55f;

    [Tooltip("How hard a full gale whips the foliage — MULTIPLIES the sway at peak Wind (the Wind weather channel). " +
             "0 = weather doesn't affect sway; 2 = a full-Wind storm bends plants ~3× a calm day.")]
    [Range(0f, 4f)] public float stormBoost = 2f;

    [Tooltip("Take wind DIRECTION + base speed from the drifting cloud shadows, so plants lean with the clouds.")]
    public bool followCloudWind = true;

    [Tooltip("Used for direction/speed only when no CloudShadows is present (fallback).")]
    public float windDirDeg = 71.33f;
    [Range(0f, 3f)] public float windSpeed = 1f;

    static readonly int WindID      = Shader.PropertyToID("_GlobalWind");
    static readonly int WindPhaseID = Shader.PropertyToID("_GlobalWindPhase");
    CloudShadows cloud;

    // Accumulated foliage animation clock. Integrating the (storm-boosted) speed here — instead of letting the
    // shader multiply the ever-growing _Time.y by a changing speed — keeps a speed ramp a SMOOTH rate change.
    // (The multiply popped a ~2s fast-forward burst at storm onset.)
    float windTime;

    /// <summary>The current wind strength (0..1, the Wind weather channel) — the single value other followers
    /// (e.g. <see cref="RainOverlay"/>'s slant) read so they blow with the same wind the foliage feels.</summary>
    public static float CurrentWind01 { get; private set; }
    /// <summary>The current wind compass direction in degrees (shared with the cloud drift).</summary>
    public static float CurrentDirDeg { get; private set; } = 71.33f;

    /// <summary>Find the live wind driver, creating one if none exists yet (so foliage sway works even on a
    /// Systems scene baked before this component joined the rig; normally it's on the Sun).</summary>
    public static WindGlobals Ensure()
    {
        var w = Instance != null ? Instance : FindAnyObjectByType<WindGlobals>();
        if (w == null) w = new GameObject("WindGlobals") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<WindGlobals>();
        return w;
    }

    // Seed a gentle breeze BEFORE the first frame (play mode) and on every editor domain reload, so the
    // foliage shader never reads an unset (zero) global and freezes flat — and so it sways in the Scene view.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Seed() => Shader.SetGlobalVector(WindID, SeedValue());
#if UNITY_EDITOR
    // While authoring, start the foliage FROZEN (zero strength) — not a breeze — so trees don't sway in edit mode.
    [UnityEditor.InitializeOnLoadMethod]
    static void SeedEditor() => Shader.SetGlobalVector(WindID, FlatValue());
#endif
    static Vector4 SeedValue()
    {
        float r = 71.33f * Mathf.Deg2Rad;
        return new Vector4(Mathf.Cos(r), Mathf.Sin(r), 0.55f, 0.3f);
    }

    // Direction only, strength + gust = 0 → the shader's bend resolves to zero, so foliage stands perfectly still.
    static Vector4 FlatValue()
    {
        float r = 71.33f * Mathf.Deg2Rad;
        return new Vector4(Mathf.Cos(r), Mathf.Sin(r), 0f, 0f);
    }

    void OnEnable()  { Instance = this; }
    void OnDisable() { if (Instance == this) Instance = null; }

    void LateUpdate()
    {
#if UNITY_EDITOR
        // Authoring (edit/Scene view): freeze the foliage. ExecuteAlways runs this every editor frame, so even after
        // exiting Play — which would otherwise leave a swaying value baked into the global — the trees go still.
        if (!Application.isPlaying)
        {
            Shader.SetGlobalVector(WindID, FlatValue());
            return;
        }
#endif
        float dirDeg = windDirDeg, speed = windSpeed;
        if (followCloudWind)
        {
            if (cloud == null) cloud = FindAnyObjectByType<CloudShadows>();   // resolves on frame 1 (same Sun)
            if (cloud != null) { dirDeg = cloud.windDirDeg; speed = cloud.windSpeed; }
        }

        // The Wind weather CHANNEL is the storminess (0..1) now — authored per weather (calm clear day → full gale
        // thunderstorm) and eased centrally like rain/cloud. It MULTIPLIES the sway, so a storm whips the grass while
        // a clear day barely stirs it; the manual cloud `windSpeed` adds a small ambient breeze on top.
        float wind01 = WeatherChannels.Instance != null ? WeatherChannels.Instance.Shown(WeatherChannel.Wind) : 0f;

        float scale    = Mathf.Max(0f, swayScale);
        float strength = (baseStrength + speed * 0.15f) * (1f + wind01 * stormBoost) * scale;   // Wind multiplies it
        float gust     = (0.25f + wind01 * 1.0f) * scale;                                       // flutter ramps with Wind

        // Advance the animation clock by a storm-boosted rate (faster in a gale + a touch with the ambient breeze).
        // Accumulating it makes the speed-up a smooth rate change — no onset fast-forward (see windTime above).
        float speedFactor = (1f + wind01 * 0.7f) * (1f + speed * 0.1f);
        windTime += Time.deltaTime * speedFactor;

        CurrentWind01 = wind01;       // publish for other wind followers (rain slant, …)
        CurrentDirDeg = dirDeg;

        float a = dirDeg * Mathf.Deg2Rad;
        Shader.SetGlobalVector(WindID, new Vector4(Mathf.Cos(a), Mathf.Sin(a), strength, gust));
        Shader.SetGlobalFloat(WindPhaseID, windTime);
    }
}
