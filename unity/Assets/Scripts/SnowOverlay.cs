using UnityEngine;

/// <summary>
/// Falling snow: the twin of <see cref="RainOverlay"/>. The Rain Particles pack's snowfall
/// (<c>Resources/Weather/Snow</c>) is held over whatever the camera follows by <see cref="WeatherParticles"/> and
/// emits in proportion to the Snow weather channel; the world-surface snow build-up is a separate pass
/// (<see cref="SnowGround"/>) that follows this overlay's <see cref="intensity"/>.
///
/// <para>Costs nothing while clear: the effect is not loaded until snow first starts, and is switched off once the
/// last flake has landed after the snow stops.</para>
/// </summary>
[DisallowMultipleComponent]
public class SnowOverlay : MonoBehaviour
{
    [Range(0f, 1f)] [Tooltip("Master snow amount (the TARGET the flakes ease toward). 0 = clear (nothing drawn). " +
             "Default 0 — snow is OFF until raised (by a weather preset).")]
    public float intensity = 0f;
    [Range(0f, 60f)] [Tooltip("Seconds for the flakes to fade IN when snow starts (or out when it stops). Kept " +
             "separate from SnowGround's accumTime — flakes appear fast, the ground blanket builds up slower.")]
    public float rampTime = 3f;
    [Tooltip("Colour the flakes fade toward at MIDNIGHT so they don't glow white over a dark world. White by day.")]
    public Color nightTint = new Color(0.30f, 0.34f, 0.48f, 1f);

    /// <summary>Most-recently-awoken snow overlay, so the heavy ground-accumulation pass can read the live
    /// snow amount without a hard reference (mirrors <see cref="RainOverlay.Instance"/>).</summary>
    public static SnowOverlay Instance { get; private set; }

    // The pack's demo scene starts its snow 12 above the ground; 2.3 is that height in this world (× 0.19, the size
    // the Snow prefab is scaled to).
    const float HeightAboveGround = 2.3f;

    WeatherParticles snow;
    float shown;   // ramped flake amount (0..1) easing toward `intensity` — what the particles are driven by

    /// <summary>Find the live snow overlay, creating one if none exists yet (so
    /// snow works even on a Systems scene baked before this component joined the rig).</summary>
    public static SnowOverlay Ensure()
    {
        var s = FindAnyObjectByType<SnowOverlay>();
        if (s == null) s = new GameObject("SnowOverlay") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<SnowOverlay>();
        return s;
    }

    void Awake()
    {
        if (GameServer.RemovesPresentation(this)) return;
        Instance = this;
        snow = new WeatherParticles("Weather/Snow", HeightAboveGround, Vector3.zero);
    }

    void OnDisable() { if (snow != null) snow.Hide(); }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (snow != null) snow.Destroy();
    }

    void LateUpdate()
    {
        if (snow == null) return;

        // Follow the Snow weather channel when present (central easing in WeatherChannels); fall back to the local
        // `intensity` ease when there's no channels hub (standalone, on a bare scene).
        var wc = WeatherChannels.Instance;
        if (wc != null) shown = wc.Shown(WeatherChannel.Snow);
        else            shown = Mathf.MoveTowards(shown, intensity, Time.deltaTime / Mathf.Max(0.1f, rampTime));

        var tod = TimeOfDay.Instance;
        snow.Drive(shown, 0f, tod != null ? tod.WeatherTint(nightTint) : Color.white);
    }
}
