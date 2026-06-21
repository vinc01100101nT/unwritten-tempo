using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Rain ambience: crossfades the <b>Forest Day Storm</b> and <b>Forest Night Storm</b> loops by time-of-day and
/// scales the whole thing by the live rain amount (<see cref="RainOverlay.Instance"/>). Two looping 2D
/// <see cref="AudioSource"/>s whose volumes are blended — no hard cuts, no per-frame allocation — mirroring
/// <see cref="DayNightAmbience"/>. Tied to rain intensity (NOT the heavy quality tier), so audio plays whenever
/// it's raining on any quality setting; silent (volume 0) when dry.
/// </summary>
[DisallowMultipleComponent]
public class RainAudio : MonoBehaviour
{
    [Tooltip("Daytime storm loop (Forest Day Storm).")]
    public AudioClip dayStorm;
    [Tooltip("Nighttime storm loop (Forest Night Storm).")]
    public AudioClip nightStorm;
    [Range(0f, 1f)] [Tooltip("Loudest the storm gets, at full rain.")]
    public float maxVolume = 1f;

    AudioSource daySrc, nightSrc;

    public static RainAudio Instance { get; private set; }

    /// <summary>Find the live rain audio, creating one if none exists (used by the DevSettings knob).</summary>
    public static RainAudio Ensure()
    {
        var a = FindFirstObjectByType<RainAudio>();
        if (a == null) a = new GameObject("RainAudio").AddComponent<RainAudio>();
        return a;
    }

    void Awake()
    {
        Instance = this;
#if UNITY_EDITOR
        // Editor convenience: if the builder didn't assign the clips, load them by path so this works the moment
        // it's added in-editor. In a player build the #if is stripped — the refs come from the saved scene.
        const string bgs = "Assets/Audio/TomMusic/WAV Files/BGS Loops";
        if (dayStorm   == null) dayStorm   = AssetDatabase.LoadAssetAtPath<AudioClip>(bgs + "/Forest Day/Forest Day Storm.wav");
        if (nightStorm == null) nightStorm = AssetDatabase.LoadAssetAtPath<AudioClip>(bgs + "/Forest Night/Forest Night Storm.wav");
#endif
        daySrc   = MakeSource(dayStorm);
        nightSrc = MakeSource(nightStorm);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    AudioSource MakeSource(AudioClip clip)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;   // 2D, like the rest of the ambience/SFX
        s.volume = 0f;
        if (clip != null) s.Play();
        return s;
    }

    void Update()
    {
        float rain   = RainOverlay.Instance != null ? RainOverlay.Instance.intensity : 0f;
        float master = Mathf.Clamp01(rain) * maxVolume;
        float night  = TimeOfDay.Instance != null ? TimeOfDay.Instance.Night01 : 0f;   // 0 day .. 1 night

        if (daySrc   != null) daySrc.volume   = master * (1f - night);
        if (nightSrc != null) nightSrc.volume = master * night;
    }
}
