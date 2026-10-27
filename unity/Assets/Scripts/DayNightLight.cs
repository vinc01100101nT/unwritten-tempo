using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// THE TIME OF DAY, as one tint over the whole picture (2026-10-28, the user's call: "keep their colors, and just use a
/// masking layer that tints everything. warm at sunset and then cold at night. and no tint at day"). The sun, the
/// whole-scene fill, the sky and the billboards keep their midday colours at every hour; a colour filter laid on the
/// finished picture does the rest — white at noon, warm while the sun is low, cold at night. The sun still moves
/// across the sky, so shadows still swing. Lives on the Systems Sun (added by <c>SystemsSceneBuilder</c>).
///
/// <para>Cost: the filter is one setting on the colour grading the camera already does every frame, so it adds no
/// pass. It is rewritten only when the clock moves.</para>
/// </summary>
[RequireComponent(typeof(Light))]
public class DayNightLight : MonoBehaviour
{
    [Header("Sun (the same at every hour)")]
    public Color dayColor = new Color(1f, 0.96f, 0.90f);
    public float dayIntensity = 1f;   // the nature pack demo's sun, beside its bright fill

    [Header("The tint over the picture")]
    // Sunset and sunrise use DIFFERENT colours: dusk a redder orange, dawn a softer lavender — picked by whether the sun
    // is rising or setting. Both peak while the sun is low (Daylight01 ≈ 0.5) and fade to nothing toward noon and
    // midnight.
    [Tooltip("The tint at SUNSET, at full strength.")]
    public Color horizonColor = new Color(0.99f, 0.64f, 0.10f);
    [Tooltip("The tint at SUNRISE, at full strength.")]
    public Color sunriseColor = new Color(0.77f, 0.62f, 1f);
    [Range(0f, 1f)] [Tooltip("How much of the sunset / sunrise colour the tint takes at its peak (0 = none).")]
    public float horizonStrength = 0.35f;
    // 0.15 (2026-10-28, the user: "make sure that the warm wont come back at day time"): the warmth shows only while
    // daylight is between 0.35 and 0.65 — the hour around sunrise and sunset — and is zero for the rest of the day. At
    // 0.5 it tinted every hour but exact noon.
    [Range(0.05f, 0.5f)] [Tooltip("Width of the sunset / sunrise band — bigger = the warmth lingers longer into day and night.")]
    public float horizonBand = 0.15f;
    [UnityEngine.Serialization.FormerlySerializedAs("nightSpriteTint")]
    [Tooltip("The tint at midnight — the whole picture is multiplied by it (1,1,1 = no change).")]
    public Color nightTint = new Color(0.34f, 0.42f, 0.60f);

    [Header("Ambient fill (whole-scene)")]
    public bool driveAmbient = true;
    // THE SKY'S OWN LIGHT BY DAY, from three directions (2026-10-28, at the user's ask to look like the nature pack's
    // demo): the demo lights its shadows from its blue sky, and a flat warm fill turned every pink red. These are the
    // sky's light at midday read off the game's own sky at 1.6 times its strength, then taken 60% of the way back
    // toward grey of the same brightness — at the sky's full blue every grey rock came out bright blue. Held as three
    // colours so nothing has to re-read the sky while the clock runs (re-reading it took about 4 ms a time on the main
    // thread). The pack's plant shader takes its brightness from this fill alone, never from the sun, so a dim fill
    // draws every flower dark.
    [Tooltip("Daytime fill from above (the sky overhead).")]
    public Color dayAmbient = new Color(1.274f, 1.374f, 1.671f);
    [Tooltip("Daytime fill from the side (the horizon).")]
    public Color dayAmbientEquator = new Color(1.105f, 1.265f, 1.544f);
    [Tooltip("Daytime fill from below (light thrown back off the ground).")]
    public Color dayAmbientGround = new Color(1.048f, 1.245f, 1.507f);
    [Header("Sky (camera background)")]
    // The main camera's solid clear colour — the "sky" at the world edges and through gaps — and the horizon colour
    // the procedural sky and the fog read. The same at every hour; the tint over the picture carries the time of day.
    [Tooltip("Paint the main camera's background with the sky colour.")]
    public bool driveSky = true;
    [Tooltip("The sky colour — bright daytime blue.")]
    public Color daySky = new Color(0.55f, 0.70f, 0.85f);

    // The billboards' shader multiplies in a global tint (_SpriteEnv). It stays white: the tint over the picture
    // already covers them, and a second tint on them alone would darken them twice at night.
    static readonly int SpriteEnvProp = Shader.PropertyToID("_SpriteEnv");

    // Seed the global white BEFORE the first frame renders (play mode) and on every editor domain reload,
    // so billboards are never multiplied by an unset (black) global.
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
    // 45 (2026-10-27, at the user's go): a sun straight overhead lit every upward-facing flower and leaf the same, so
    // the meadows read as flat colour; the nature pack's demo sun stands at about 45.
    [Tooltip("Sun pitch above the horizon at noon (high = overhead, short shadows under the objects).")] public float noonPitch = 45f;
    [Tooltip("Sun pitch at sunrise/sunset (low = long raking shadows).")] public float horizonPitch = 12f;
    [Tooltip("Compass direction of the sun at NOON — the centre the daily swing pivots around.")] public float yaw = -47.33f;
    [Range(0f, 180f)]
    [Tooltip("How far the sun's compass direction swings from sunrise to sunset. 180 = morning shadows fall " +
             "on the exact opposite side from evening shadows (sun crosses the sky). 0 = a fixed direction.")]
    public float yawSwing = 180f;

    Light sun;
    float lastTime = float.NaN;   // last applied time-of-day; NaN forces the next Update to apply

    /// <summary>The sky colour. <see cref="SkyboxSky"/> reads this as the horizon colour and <see cref="FogOverlay"/> as
    /// the haze colour.</summary>
    public Color SkyColorNow { get; private set; } = StartSkyColor;

    // The tint over the picture: a global volume of its own, made at run time, holding only a colour filter. Above the
    // map's own look in priority, so its filter is the one used; it touches nothing else in the grading.
    const float TintPriority = 100f;
    ColorAdjustments tint;

    /// <summary>The sky's colour before any clock has run — what anything reading the sky shows until one does.</summary>
    public static readonly Color StartSkyColor = new Color(0.55f, 0.70f, 0.85f);

    [Header("Cloud shadows")]
    [Tooltip("Project drifting cloud-shadow patches on the ground (adds/uses a CloudShadows component).")]
    public bool enableCloudShadows = true;

    void Awake()
    {
        if (GameServer.RemovesPresentation(this)) return;
        sun = GetComponent<Light>();
        if (driveAmbient) RenderSettings.ambientMode = AmbientMode.Trilight;   // so the three fill colours apply
        BuildTint();

        // Bootstrap the cloud-shadow projector onto the Sun (no scene wiring needed). Guarded so a builder or
        // a previous run that already added one doesn't get a duplicate.
        if (enableCloudShadows && GetComponent<CloudShadows>() == null)
            gameObject.AddComponent<CloudShadows>();

        // Bootstrap the foliage-wind driver too — it feeds _GlobalWind from the cloud wind so plants sway in
        // sync with the drifting clouds. Guarded against duplicates the same way.
        if (GetComponent<WindGlobals>() == null)
            gameObject.AddComponent<WindGlobals>();

        // Bootstrap the procedural sky (wraps the scene in a skybox; sets RenderSettings.skybox + camera clear).
        SkyboxSky.Ensure();
    }

    /// <summary>The whole-scene fill, the same at every hour. Also how the character preview lights its model.</summary>
    public void ApplyFill()
    {
        RenderSettings.ambientSkyColor = dayAmbient;
        RenderSettings.ambientEquatorColor = dayAmbientEquator;
        RenderSettings.ambientGroundColor = dayAmbientGround;
    }

    void BuildTint()
    {
        var holder = new GameObject("Time of day tint") { hideFlags = HideFlags.DontSave };
        holder.transform.SetParent(transform, false);
        var volume = holder.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = TintPriority;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.hideFlags = HideFlags.DontSave;
        tint = profile.Add<ColorAdjustments>();
        tint.colorFilter.overrideState = true;
        tint.colorFilter.value = Color.white;
        volume.sharedProfile = profile;
    }

    /// <summary>Force the next <see cref="Update"/> to re-apply colour/intensity/angle even when the clock
    /// hasn't moved, so a changed sun setting shows instantly instead of waiting for the next time-of-day
    /// change.</summary>
    public void Refresh() => lastTime = float.NaN;

    void Update()
    {
        var tod = TimeOfDay.Instance;
        float t   = tod != null ? tod.time01 : 0.25f;   // 0 sunrise · 0.25 noon · 0.5 sunset · 0.75 midnight
        float day = tod != null ? tod.Daylight01 : 1f;

        // Loading a map scene resets the global lighting to that scene's own (its saved ambient mode and colours), as
        // SkyboxSky notes for the skybox: put the three-colour fill back, and re-apply its colours this frame.
        if (driveAmbient && RenderSettings.ambientMode != AmbientMode.Trilight)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            lastTime = float.NaN;
        }

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

        sun.color = dayColor;
        sun.intensity = dayIntensity;
        if (driveAmbient) ApplyFill();
        if (tint != null) tint.colorFilter.value = TimeOfDayTint.At(day, nightTint, hc, glow);
        Shader.SetGlobalColor(SpriteEnvProp, Color.white);

        if (driveSky)
        {
            // Always store the sky colour (the SkyboxSky reads SkyColorNow as its horizon colour, even when there's no
            // camera yet or the camera clears with the skybox instead of this solid colour).
            SkyColorNow = daySky;

            var cam = CameraRig.Cam;   // cached; re-fetched after a scene swap
            if (cam != null) cam.backgroundColor = daySky;   // harmless when the camera clears with the skybox
            // No camera yet (Systems boots before the map's camera). Re-arm so we apply the moment it exists.
            else lastTime = float.NaN;
        }

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

/// <summary>The time-of-day tint's arithmetic, kept apart so it can be checked by calling it with numbers.</summary>
public static class TimeOfDayTint
{
    /// <summary>The colour the whole picture is multiplied by: white whenever the sun is up (daylight 0.5 and over —
    /// no tint by day, at the user's word), easing into <paramref name="nightTint"/> as daylight falls to 0 at midnight,
    /// and that taken <paramref name="glow"/> of the way toward <paramref name="glowColor"/> while the sun is low.</summary>
    public static Color At(float daylight, Color nightTint, Color glowColor, float glow)
    {
        float dayShare = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(daylight / 0.5f));
        var c = Color.Lerp(Color.Lerp(nightTint, Color.white, dayShare), glowColor, glow);
        c.a = 1f;
        return c;
    }
}
