using UnityEngine;

/// <summary>
/// PROCEDURAL SKY — wraps the whole scene in a seamless sphere of sky via Unity's skybox. Replaces the old flat
/// cloud deck: instead of a plane (which always had a hard horizon edge), the <c>Unwritten/SkyboxClouds</c> shader
/// computes the sky PER VIEW DIRECTION, so it's edge-free, infinitely far, and meets the ground at the horizon from
/// every angle. It drifts/rotates correctly with the camera (turn → clouds sweep past; it's pinned to world
/// directions, never to the screen) and shows in wet-ground reflections (the mirror cam also clears to Skybox).
///
/// <para>Drives the skybox material from three sources every frame: the <see cref="TimeOfDay"/> clock + the
/// <see cref="DayNightLight"/> palette (horizon/zenith colours, sun direction/intensity dimmed at night), and the
/// weather <see cref="WeatherChannels"/> (cloud cover + storm darkening, reusing <see cref="CloudShadows"/>'
/// procedural cloud texture so the sky clouds and the ground cloud-shadows share one pattern + wind).</para>
///
/// Cheap: the skybox is a single full-view pass at infinite depth (no geometry to manage). It also flips the main
/// camera + reflection cam to <c>clearFlags = Skybox</c> so they render it.
/// </summary>
[DisallowMultipleComponent]
public class SkyboxSky : MonoBehaviour
{
    [Header("Sky gradient")]
    [Tooltip("Sky colour straight overhead (zenith) at MIDDAY — deeper blue than the horizon.")]
    public Color dayZenith = new Color(0.20f, 0.42f, 0.78f);
    [Tooltip("Sky colour overhead at MIDNIGHT — near-black night blue.")]
    public Color nightZenith = new Color(0.02f, 0.03f, 0.08f);
    [Range(0.2f, 4f)] [Tooltip("Gradient bias: higher = the deep zenith colour stays high, horizon band is thinner.")]
    public float gradientPower = 0.58f;

    [Header("Sun")]
    [Tooltip("Colour of the sun disk + halo painted on the sky.")]
    public Color sunColor = new Color(1f, 0.95f, 0.82f);
    [Range(50f, 4000f)] [Tooltip("Sun disk tightness — higher = smaller, crisper disk.")]
    public float sunSharpness = 1352f;
    [Range(2f, 64f)] [Tooltip("Sun halo spread — lower = a wider, softer glow around the sun.")]
    public float sunHalo = 2f;

    [Header("Moon")]
    [Tooltip("Colour of the moon disk + halo — a cool white-blue. Rides up opposite the sun, so it's up at night.")]
    public Color moonColor = new Color(0.78f, 0.85f, 1f);
    [Range(50f, 6000f)] [Tooltip("Moon disk tightness — higher = smaller, crisper disk.")]
    public float moonSharpness = 6000f;
    [Range(2f, 64f)] [Tooltip("Moon halo spread — lower = a wider, softer glow around the moon.")]
    public float moonHalo = 64f;
    [Range(0f, 2f)] [Tooltip("Lifts the moon higher in the sky above its natural point opposite the sun " +
                             "(0 = exactly antisolar; higher = the moon rides further up the dome).")]
    public float moonLift = 0.4f;

    [Header("Clouds")]
    [Tooltip("Cloud colour in FAIR weather (bright white).")]
    public Color cloudColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Cloud colour at FULL STORM (ominous grey) — clouds darken toward this as it clouds over / rains.")]
    public Color stormColor = new Color(0.34f, 0.36f, 0.42f, 1f);
    [Range(0f, 1f)] [Tooltip("Cloud cover above which clouds start greying toward the storm colour.")]
    public float fairCloud = 0.66f;
    [Range(0f, 1f)] [Tooltip("How strongly clouds darken as the storm builds (0 = always white).")]
    public float stormDarken = 0f;
    [Tooltip("Cloud feature size in the sky — bigger = larger, broader clouds (fewer across the sky).")]
    public float cloudSize = 4f;
    [Range(0f, 4f)] [Tooltip("How opaque/full the clouds read.")]
    public float density = 0.96f;
    [Range(0.001f, 1f)] [Tooltip("Cloud edge softness — low = crisp puffs, high = hazy wisps.")]
    public float softEdge = 1f;
    [Range(0.001f, 1f)] [Tooltip("How high above the horizon clouds fade in — bigger = clear sky reaches higher up.")]
    public float horizonFade = 0.22f;
    [Tooltip("Colour clouds fade toward at MIDNIGHT (unlit, dimmed by hand on the clock like the snow blanket).")]
    public Color nightTint = new Color(0.30f, 0.34f, 0.48f, 1f);
    [Tooltip("Base drift speed of the sky clouds (scaled by the Wind weather channel, like the cloud shadows).")]
    public float driftScale = 0.012f;

    public static SkyboxSky Instance { get; private set; }

    Material mat;
    DayNightLight dnl;
    Vector2 scroll;

    static readonly int CloudTexID   = Shader.PropertyToID("_CloudTex");
    static readonly int HorizonColID = Shader.PropertyToID("_HorizonColor");
    static readonly int ZenithColID  = Shader.PropertyToID("_ZenithColor");
    static readonly int GradPowerID  = Shader.PropertyToID("_GradPower");
    static readonly int SunDirID     = Shader.PropertyToID("_SunDir");
    static readonly int SunColorID   = Shader.PropertyToID("_SunColor");
    static readonly int SunSharpID   = Shader.PropertyToID("_SunSharp");
    static readonly int SunGlowID    = Shader.PropertyToID("_SunGlow");
    static readonly int SunIntID     = Shader.PropertyToID("_SunIntensity");
    static readonly int MoonDirID    = Shader.PropertyToID("_MoonDir");
    static readonly int MoonColorID  = Shader.PropertyToID("_MoonColor");
    static readonly int MoonSharpID  = Shader.PropertyToID("_MoonSharp");
    static readonly int MoonGlowID   = Shader.PropertyToID("_MoonGlow");
    static readonly int MoonIntID    = Shader.PropertyToID("_MoonIntensity");
    static readonly int CloudColID   = Shader.PropertyToID("_CloudColor");
    static readonly int LightTintID  = Shader.PropertyToID("_LightTint");
    static readonly int CloudSclID   = Shader.PropertyToID("_CloudScale");
    static readonly int ScrollXID    = Shader.PropertyToID("_ScrollX");
    static readonly int ScrollYID    = Shader.PropertyToID("_ScrollY");
    static readonly int DensityID    = Shader.PropertyToID("_Density");
    static readonly int SoftEdgeID   = Shader.PropertyToID("_SoftEdge");
    static readonly int HorizonFadeID= Shader.PropertyToID("_HorizonFade");
    static readonly int StrengthID   = Shader.PropertyToID("_Strength");

    Camera taughtMain;

    // The cloud layer, held rather than looked up. Ensure() hunts the whole scene and builds one if it is
    // missing — a boot-time call, and it was being made every single frame from LateUpdate below.
    CloudShadows clouds;

    /// <summary>Find the live sky, creating one if none exists (used by the weather director / day-night light).</summary>
    public static SkyboxSky Ensure()
    {
        var s = FindAnyObjectByType<SkyboxSky>();
        if (s == null) s = new GameObject("SkyboxSky") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<SkyboxSky>();
        return s;
    }

    void Awake()
    {
        var shader = Shader.Find("Unwritten/SkyboxClouds");
        if (shader == null)
        {
            Debug.LogWarning("[SkyboxSky] Shader 'Unwritten/SkyboxClouds' not found — procedural sky disabled. " +
                             "(In a build, add it to Project Settings ▸ Graphics ▸ Always Included Shaders.)");
            enabled = false;
            return;
        }
        mat = new Material(shader) { name = "SkyboxClouds (runtime)" };
        RenderSettings.skybox = mat;
        // PERF: our skybox material changes every frame (drifting clouds + day/night). If the environment
        // reflection were left on "Skybox" mode, Unity would re-bake the reflection cubemap EVERY frame. Pin it to
        // Custom (flat env) so it never regenerates — our billboard sprites are unlit and don't sample it anyway.
        // (DayNightLight already sets ambientMode = Flat, so ambient light doesn't come from the skybox either.)
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (RenderSettings.skybox == mat) RenderSettings.skybox = null;
        if (mat != null) Destroy(mat);
    }

    void LateUpdate()
    {
        if (mat == null) return;

        // Loading a map scene additively can reset the global RenderSettings to that scene's lighting (clearing our
        // skybox + reflection mode). Re-assert them if they got knocked off — cheap, only writes when different.
        if (RenderSettings.skybox != mat) RenderSettings.skybox = mat;
        if (RenderSettings.defaultReflectionMode != UnityEngine.Rendering.DefaultReflectionMode.Custom)
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;

        // Make sure the cameras actually render the skybox (RigBuilder25D defaults them to a solid colour).
        var cam = CameraRig.Cam;
        TeachCameraSkybox(cam);

        // ── Day / night sky + sun (from the clock + the Sun) ──────────────────────────────────────────────────
        var tod = TimeOfDay.Instance;
        float day = tod != null ? tod.Daylight01 : 1f;
        if (dnl == null) dnl = FindAnyObjectByType<DayNightLight>();

        // Horizon colour = the same day/night sky colour DayNightLight already computes (carries the dawn/dusk
        // glow); zenith = our own deeper blue lerped on the same clock. Fall back to sensible day colours.
        Color horizon = dnl != null ? dnl.SkyColorNow : Color.Lerp(new Color(0.04f, 0.05f, 0.10f),
                                                                    new Color(0.55f, 0.70f, 0.85f), day);
        Color zenith = Color.Lerp(nightZenith, dayZenith, day);

        // Sun: direction TO the sun = opposite the directional light's forward (the way its rays travel).
        Vector3 sunDir = dnl != null ? -dnl.transform.forward : new Vector3(0f, 0.7f, 0.7f).normalized;

        mat.SetColor(HorizonColID, horizon);
        mat.SetColor(ZenithColID, zenith);
        mat.SetFloat(GradPowerID, gradientPower);
        mat.SetVector(SunDirID, sunDir);
        mat.SetColor(SunColorID, sunColor);
        mat.SetFloat(SunSharpID, sunSharpness);
        mat.SetFloat(SunGlowID, sunHalo);
        mat.SetFloat(SunIntID, day);          // sun disk fades out at night

        // Moon = opposite the sun (antisolar point), so it rises as the sun sets, then LIFTED further up the dome
        // by moonLift (our day-night arc is shallow, so the bare antisolar point hugs the horizon). Fades IN at night.
        Vector3 moonDir = (-sunDir + Vector3.up * moonLift).normalized;
        mat.SetVector(MoonDirID, moonDir);
        mat.SetColor(MoonColorID, moonColor);
        mat.SetFloat(MoonSharpID, moonSharpness);
        mat.SetFloat(MoonGlowID, moonHalo);
        mat.SetFloat(MoonIntID, 1f - day);    // moon fades in as the sun fades out

        // ── Clouds (weather channels + the shared CloudShadows texture/wind) ──────────────────────────────────
        var wc = WeatherChannels.Instance;
        float cover = wc != null ? wc.Shown(WeatherChannel.Cloud) : 1f;
        float rain  = wc != null ? wc.Shown(WeatherChannel.Rain) : 0f;
        float storm = Mathf.Max(Mathf.SmoothStep(fairCloud, 1f, cover), rain) * stormDarken;
        Color baseCloud = Color.Lerp(cloudColor, stormColor, storm);

        if (clouds == null) clouds = CloudShadows.Ensure();   // re-found only if a scene swap took it
        var cs = clouds;
        if (cs != null && cs.CloudTexture != null) mat.SetTexture(CloudTexID, cs.CloudTexture);

        // Drift the sky clouds with the SAME wind as the ground shadows (direction + the Wind channel speed-up).
        if (cs != null)
        {
            float wind01 = wc != null ? wc.Shown(WeatherChannel.Wind) : 0f;
            float speed  = cs.windSpeed * (1f + wind01 * cs.windResponse);
            float r = cs.windDirDeg * Mathf.Deg2Rad;
            scroll += new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * (speed * driftScale * Time.deltaTime);
        }

        mat.SetColor(CloudColID, baseCloud);
        mat.SetColor(LightTintID, tod != null ? tod.WeatherTint(nightTint) : Color.white);
        mat.SetFloat(CloudSclID, 1f / Mathf.Max(0.01f, cloudSize));
        mat.SetFloat(ScrollXID, scroll.x);
        mat.SetFloat(ScrollYID, scroll.y);
        mat.SetFloat(DensityID, density);
        mat.SetFloat(SoftEdgeID, softEdge);
        mat.SetFloat(HorizonFadeID, horizonFade);
        mat.SetFloat(StrengthID, cover);
    }

    // Flip the main camera to clear with the Skybox (idempotent; re-applied across scene swaps). The planar
    // reflection camera sets its OWN clearFlags to Skybox in PlanarReflection, so puddles get the sky too.
    void TeachCameraSkybox(Camera cam)
    {
        if (cam == null || cam == taughtMain) return;
        cam.clearFlags = CameraClearFlags.Skybox;
        taughtMain = cam;
    }
}
