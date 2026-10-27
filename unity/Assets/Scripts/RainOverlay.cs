using UnityEngine;

/// <summary>
/// Falling rain: the Rain Particles pack's rain (<c>Resources/Weather/Rain</c>) — streaks that splash and ring where
/// they hit the ground — held over whatever the camera follows by <see cref="WeatherParticles"/>, and emitting in
/// proportion to the Rain weather channel. Leans sideways with the base slant plus the Wind channel.
///
/// <para>Costs nothing while dry: the effect is not loaded until rain first starts, and is switched off once the
/// last drop has landed after the rain stops.</para>
/// </summary>
[DisallowMultipleComponent]
public class RainOverlay : MonoBehaviour
{
    [Range(0f, 1f)] [Tooltip("TARGET rain amount the rain eases toward — the per-map MAX (default 1). Flip this " +
             "on/off; the VISIBLE rain ramps to it over rampTime instead of popping. 0 = dry (nothing drawn).")]
    public float intensity = 1f;
    [Range(0f, 60f)] [Tooltip("Seconds for the rain to fade IN when it starts (and OUT when it stops) — the storm " +
             "builds 0→target gradually and tapers off when it clears. Mirrors SnowOverlay.rampTime.")]
    public float rampTime = 4f;
    [Range(-1f, 1f)] [Tooltip("Base wind slant — negative leans left, positive leans right. The Wind weather " +
                              "channel adds to this, so rain blows harder sideways in a storm.")]
    public float slant = 0.2f;
    [Range(0f, 1.5f)] [Tooltip("How far the Wind weather channel tilts the rain on top of the base slant (0 = " +
                               "ignore wind; rain falls at the fixed base slant).")]
    public float windSlant = 0.7f;

    /// <summary>Most-recently-awoken rain overlay, so the heavy wet-ground/splash effects can read the live
    /// rain amount without a hard reference.</summary>
    public static RainOverlay Instance { get; private set; }

    /// <summary>The eased, on-screen rain amount (0..1) — ramps toward <see cref="intensity"/> over
    /// <see cref="rampTime"/>. Consumers that should follow the VISIBLE rain (e.g. rain audio) read this, not the
    /// raw target. (The wet-ground soak/dry timers trigger off the raw <see cref="intensity"/> instead, so the
    /// ground starts soaking the instant rain is commanded on — "from 0 onwards".)</summary>
    public float Shown => shown;

    // The pack's demo scene stands the rain emitter on its side (-90 on X) so its sheet faces down, 5 above the
    // ground; 0.95 is that height in this world (× 0.19, the size the Rain prefab is scaled to).
    static readonly Vector3 RestEuler = new Vector3(-90f, 0f, 0f);
    const float HeightAboveGround = 0.95f;

    WeatherParticles rain;
    float shown;   // ramped rain amount (0..1) easing toward `intensity` — what the particles are driven by
    bool primed;   // first-frame: snap `shown` to the map's starting rain so we don't fade in on load

    /// <summary>Find the live rain overlay, creating one if none exists yet (so
    /// rain works even on a scene built before this component was added to the rig).</summary>
    public static RainOverlay Ensure()
    {
        var r = FindAnyObjectByType<RainOverlay>();
        if (r == null) r = new GameObject("RainOverlay") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<RainOverlay>();
        return r;
    }

    void Awake()
    {
        if (GameServer.RemovesPresentation(this)) return;
        Instance = this;
        rain = new WeatherParticles("Weather/Rain", HeightAboveGround, RestEuler);
    }

    void OnDisable() { if (rain != null) rain.Hide(); }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (rain != null) rain.Destroy();
    }

    void LateUpdate()
    {
        if (rain == null) return;

        // Follow the Rain weather channel when present — easing lives centrally in WeatherChannels, so we just read
        // the eased value (this is what lets a sequence ramp rain 0→target over an exact time, with clouds leading).
        var wc = WeatherChannels.Instance;
        if (wc != null)
        {
            shown = wc.Shown(WeatherChannel.Rain);
        }
        else
        {
            // Standalone fallback (no channels hub): ease locally toward `intensity`, snapping on the first frame so
            // we don't fade in on load into an already-rainy map.
            if (!primed) { shown = intensity; primed = true; }
            shown = Mathf.MoveTowards(shown, intensity, Time.deltaTime / Mathf.Max(0.1f, rampTime));
        }

        // Lean the rain with the wind: the Wind channel × the horizontal component of the wind direction, added on
        // top of the manual base slant. So a calm shower falls near-straight and a gale drives it hard sideways.
        // A slant of 1 is one unit sideways per unit fallen (45°).
        float windLean = WindGlobals.CurrentWind01 * windSlant * Mathf.Cos(WindGlobals.CurrentDirDeg * Mathf.Deg2Rad);
        float lean = Mathf.Atan(Mathf.Clamp(slant + windLean, -1f, 1f)) * Mathf.Rad2Deg;
        rain.Drive(shown, lean, Color.white);
    }
}
