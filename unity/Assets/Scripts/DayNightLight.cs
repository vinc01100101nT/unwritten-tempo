using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drives the directional Sun's color, intensity, sky angle, and the scene's ambient fill from the shared
/// <see cref="TimeOfDay"/> clock — so the world visibly goes from warm bright noon to dim blue midnight and
/// back. Lives on the Systems Sun (added by <c>SystemsSceneBuilder</c>). All values lerp by
/// <see cref="TimeOfDay.Daylight01"/>, so it crossfades smoothly with the day/night ambience.
/// </summary>
[RequireComponent(typeof(Light))]
public class DayNightLight : MonoBehaviour
{
    [Header("Sun color + brightness")]
    public Color dayColor = new Color(1f, 0.96f, 0.90f);     // warm midday
    public Color nightColor = new Color(0.50f, 0.60f, 0.95f); // cool moonlight
    public float dayIntensity = 2.28f;
    [Tooltip("0 = fully dark night. Bump up if night is too black to play in.")]
    public float nightIntensity = 0f;

    [Header("Sunset / sunrise glow")]
    // The plain night→day lerp gives a washed-out mid-tone at the horizon. This blends a warm colour into the
    // sun (and optionally the ambient) ONLY while the sun is low — peaking at sunrise/sunset (Daylight01 ≈ 0.5)
    // and fading to nothing toward noon and midnight. Sunset and sunrise use DIFFERENT colours: dusk is a
    // redder, dustier orange; dawn is a cooler, softer pink — picked by whether the sun is rising or setting.
    [Tooltip("Warm colour mixed into the sun at SUNSET — fiery orange (a day of haze scatters out the blue).")]
    public Color horizonColor = new Color(0.99f, 0.64f, 0.10f);   // sunset gold-orange
    [Tooltip("Cooler colour mixed into the sun at SUNRISE — soft lavender dawn (clean morning air).")]
    public Color sunriseColor = new Color(0.77f, 0.62f, 1f);      // dawn lavender
    [Range(0f, 1f)] [Tooltip("How strongly the horizon colour takes over at dawn/dusk (0 = off).")]
    public float horizonStrength = 1f;
    [Range(0.05f, 0.5f)] [Tooltip("Width of the glow band — bigger = colour lingers longer into day/night.")]
    public float horizonBand = 0.5f;
    [Tooltip("Also warm the whole-scene ambient fill at sunset/sunrise (half as strongly as the sun).")]
    public bool tintAmbient = true;

    [Header("Ambient fill (whole-scene)")]
    public bool driveAmbient = true;
    public Color dayAmbient = new Color(0.55f, 0.56f, 0.60f);
    public Color nightAmbient = new Color(0.10f, 0.12f, 0.22f);

    [Header("Sky (camera background)")]
    // Drives the main camera's solid clear colour — the "sky" you see at the world edges and through gaps.
    // Lerps day→night by Daylight01 (same weight as the sun/ambient) and warms toward the sunrise/sunset
    // colour while the sun is low, so the backdrop tracks the same dawn/dusk glow as the world. The reflection
    // camera copies this each frame, so the water's sky follows for free.
    [Tooltip("Tint the main camera's background (sky) by time of day.")]
    public bool driveSky = true;
    [Tooltip("Sky colour at midday — bright daytime blue.")]
    public Color daySky = new Color(0.55f, 0.70f, 0.85f);
    [Tooltip("Sky colour at midnight — deep night blue.")]
    public Color nightSky = new Color(0.04f, 0.05f, 0.10f);
    [Range(0f, 1f)] [Tooltip("How strongly the sunrise/sunset glow bleeds into the sky (0 = off).")]
    public float skyHorizonTint = 0.6f;

    [Header("Sprite lighting (player / mobs / loot)")]
    // Billboard sprites use the unlit Unwritten/BillboardLit shader, so they ignore the sun and stay
    // full-bright at night unless we tell them otherwise. We push a single global tint (_SpriteEnv) that
    // the shader multiplies in: white at noon (true art) easing to nightSpriteTint (dim, cool moonlight)
    // at midnight, warmed slightly toward the sunrise/sunset glow. One global write per clock change —
    // zero per-sprite cost, no extra shadow/dye conflict.
    [Tooltip("Dim & cool the billboard sprites by time of day (player, mobs, loot).")]
    public bool driveSpriteLight = true;
    [Tooltip("Sprite multiply tint at midnight — dim, cool moonlight (1,1,1 = no change).")]
    public Color nightSpriteTint = new Color(0.34f, 0.42f, 0.60f);
    [Range(0f, 1f)] [Tooltip("How much the sunrise/sunset glow warms the sprites (0 = off).")]
    public float spriteHorizonWarmth = 0.25f;

    static readonly int SpriteEnvProp = Shader.PropertyToID("_SpriteEnv");

    // Seed the global white BEFORE the first frame renders (play mode) and on every editor domain reload,
    // so billboards are never multiplied by an unset (black) global when no DayNightLight is driving it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void SeedSpriteEnv() => Shader.SetGlobalColor(SpriteEnvProp, Color.white);
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    static void SeedSpriteEnvEditor() => Shader.SetGlobalColor(SpriteEnvProp, Color.white);
#endif

    [Header("Sun angle")]
    public bool rotateSun = true;
    // Pitch is lerped horizonPitch→noonPitch by Daylight01, so it tracks the real sun: HIGH/overhead at noon
    // (short shadows tucked under the objects) and LOW at sunrise/sunset (long shadows raking across the
    // ground). A LOW pitch streaks the billboards' directional shadows out far; a HIGH one keeps them short.
    [Tooltip("Sun pitch above the horizon at noon (high = overhead, short shadows under the objects).")] public float noonPitch = 85f;
    [Tooltip("Sun pitch at sunrise/sunset (low = long raking shadows).")] public float horizonPitch = 12f;
    [Tooltip("Compass direction of the sun at NOON — the centre the daily swing pivots around.")] public float yaw = -47.33f;
    [Range(0f, 180f)]
    [Tooltip("How far the sun's compass direction swings from sunrise to sunset. 180 = morning shadows fall " +
             "on the exact opposite side from evening shadows (sun crosses the sky). 0 = a fixed direction.")]
    public float yawSwing = 180f;

    Light sun;
    float lastTime = float.NaN;   // last applied time-of-day; NaN forces the next Update to apply

    [Header("Cloud shadows")]
    [Tooltip("Project drifting cloud-shadow patches on the ground (adds/uses a CloudShadows component).")]
    public bool enableCloudShadows = true;

    void Awake()
    {
        sun = GetComponent<Light>();
        if (driveAmbient) RenderSettings.ambientMode = AmbientMode.Flat;   // so ambientLight actually applies

        // Bootstrap the cloud-shadow projector onto the Sun (no scene wiring needed). Guarded so a builder or
        // a previous run that already added one doesn't get a duplicate.
        if (enableCloudShadows && GetComponent<CloudShadows>() == null)
            gameObject.AddComponent<CloudShadows>();

        // Bootstrap the foliage-wind driver too — it feeds _GlobalWind from the cloud wind so plants sway in
        // sync with the drifting clouds. Guarded against duplicates the same way.
        if (GetComponent<WindGlobals>() == null)
            gameObject.AddComponent<WindGlobals>();
    }

    /// <summary>Force the next <see cref="Update"/> to re-apply colour/intensity/angle even when the clock
    /// hasn't moved. The DevSettings sun sliders call this so tuning shows instantly instead of waiting for
    /// the next time-of-day change.</summary>
    public void Refresh() => lastTime = float.NaN;

    void Update()
    {
        var tod = TimeOfDay.Instance;
        float t   = tod != null ? tod.time01 : 0.25f;   // 0 sunrise · 0.25 noon · 0.5 sunset · 0.75 midnight
        float day = tod != null ? tod.Daylight01 : 1f;

        // Only re-apply when the clock actually moved. Guarding on time01 (not Daylight01) matters now that the
        // sun also SWINGS its compass direction: Daylight01 plateaus around noon/midnight while time keeps
        // moving, so a day-based guard would freeze the azimuth there. While paused, time01 is constant → skip.
        if (!float.IsNaN(lastTime) && t == lastTime) return;
        lastTime = t;

        // Horizon glow weight: peaks (1) when the sun is low — Daylight01 ≈ 0.5 = sunrise/sunset — and
        // falls to 0 toward noon (1) and midnight (0). Smoothstep'd for a soft fade. (day only changes when
        // the clock moves, and this Update already early-outs when it doesn't, so this stays ~free.)
        float horizon = Mathf.Clamp01(1f - Mathf.Abs(day - 0.5f) / Mathf.Max(0.01f, horizonBand));
        horizon = horizon * horizon * (3f - 2f * horizon);
        float glow = horizon * horizonStrength;

        // Dawn vs dusk: the sun is RISING from midnight (0.75) through to noon (0.25), and SETTING the rest of
        // the day. The glow weight is 0 at both noon and midnight, so this hand-off never pops mid-fade.
        bool rising = t < 0.25f || t > 0.75f;
        Color hc = rising ? sunriseColor : horizonColor;

        sun.color = Color.Lerp(Color.Lerp(nightColor, dayColor, day), hc, glow);
        sun.intensity = Mathf.Lerp(nightIntensity, dayIntensity, day);

        if (driveAmbient)
        {
            Color amb = Color.Lerp(nightAmbient, dayAmbient, day);
            if (tintAmbient) amb = Color.Lerp(amb, hc, glow * 0.5f);
            RenderSettings.ambientLight = amb;
        }

        if (driveSky)
        {
            var cam = CameraRig.Cam;   // cached; re-fetched after a scene swap
            if (cam != null)
            {
                Color sky = Color.Lerp(nightSky, daySky, day);
                sky = Color.Lerp(sky, hc, glow * skyHorizonTint);
                cam.backgroundColor = sky;
            }
            // No camera yet (Systems boots before the map's camera). Re-arm so we apply the moment it exists,
            // instead of being stuck at Bootstrap's colour until the clock next moves (e.g. while paused).
            else lastTime = float.NaN;
        }

        if (driveSpriteLight)
        {
            Color env = Color.Lerp(nightSpriteTint, Color.white, day);
            env = Color.Lerp(env, hc, glow * spriteHorizonWarmth);
            Shader.SetGlobalColor(SpriteEnvProp, env);
        }
        else Shader.SetGlobalColor(SpriteEnvProp, Color.white);   // off → sprites render true art

        if (rotateSun)
        {
            // Azimuth swings across the day: sin is -1 at sunrise (t=0) → 0 at noon (t=0.25) → +1 at sunset
            // (t=0.5), so the compass direction sweeps yaw-swing/2 → yaw → yaw+swing/2 and the shadows rake
            // across to the OTHER side by dusk. It continues smoothly back through the night so the new
            // sunrise lands on the opposite side from the last sunset.
            float az = yaw + Mathf.Sin((t - 0.25f) * Mathf.PI * 2f) * (yawSwing * 0.5f);
            sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(horizonPitch, noonPitch, day), az, 0f);
        }
    }
}
