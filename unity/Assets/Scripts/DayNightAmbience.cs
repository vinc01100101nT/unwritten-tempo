using UnityEngine;

/// <summary>
/// Per-map looping ambience that <b>crossfades between a day and a night loop</b> (e.g. TomMusic
/// "Forest Day" ↔ "Forest Night"). It owns two looping 2D <see cref="AudioSource"/>s and only changes
/// their volumes, so the swap is a smooth blend (no hard cut) and there's no per-frame allocation.
///
/// It reads the shared <see cref="TimeOfDay"/> clock (on the Systems layer) so the audio stays locked to
/// the light's day/night state — and to the live test tuner. If no <see cref="TimeOfDay"/> exists (a map
/// played standalone), it falls back to its own internal <see cref="fallbackCycleSeconds"/> timer.
///
/// Lives on the map's <c>Ambient</c> object (added by <c>NatureStageBuilder</c>). Survives the additive
/// Systems load — MapManager strips AudioListeners/cameras/lights, not the map's own AudioSources.
/// </summary>
public class DayNightAmbience : MonoBehaviour
{
    [Header("Loops")]
    public AudioClip dayClip;
    public AudioClip nightClip;

    [Range(0f, 1f)] [Tooltip("Loudest each loop ever gets.")]
    public float maxVolume = 0.22f;

    [Header("Fallback (only if no TimeOfDay in the scene)")]
    [Tooltip("Seconds for one full day → night → day cycle when running without the shared clock.")]
    public float fallbackCycleSeconds = 240f;

    AudioSource daySrc, nightSrc;
    float fallbackTod;   // normalized [0,1), only used when TimeOfDay.Instance is null

    void Awake()
    {
        daySrc   = MakeSource(dayClip);
        nightSrc = MakeSource(nightClip);
        Apply(CurrentNight());   // set starting volumes immediately (no swell-in on the first frame)
    }

    AudioSource MakeSource(AudioClip clip)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;   // 2D, like the SFX
        src.volume = 0f;
        if (clip != null) src.Play();
        return src;
    }

    void Update() => Apply(CurrentNight());

    // Night weight [0..1] — prefer the shared clock, else advance our own.
    float CurrentNight()
    {
        var tod = TimeOfDay.Instance;
        if (tod != null) return tod.Night01;
        if (fallbackCycleSeconds > 0.01f)
            fallbackTod = Mathf.Repeat(fallbackTod + Time.deltaTime / fallbackCycleSeconds, 1f);
        return (1f - Mathf.Cos(fallbackTod * Mathf.PI * 2f)) * 0.5f;
    }

    void Apply(float night)
    {
        if (daySrc   != null) daySrc.volume   = (1f - night) * maxVolume;
        if (nightSrc != null) nightSrc.volume = night * maxVolume;
    }
}
