using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// SNOW ACCUMULATION on the world: the twin of <see cref="WetGround"/>. Drives the <c>Unwritten/SnowGround</c>
/// screen-space depth pass — whitens up-facing surfaces (roofs, ground, tree-tops) and stamps the player's
/// footprints into the snow. Built on the same camera-locked full-screen-depth-quad pattern, so it projects
/// onto whatever the depth buffer holds with a fixed GPU cost.
///
/// <para>The blanket <see cref="accum"/> RAMPS 0→target over <see cref="accumTime"/> seconds, so when snow
/// starts the roofs and ground visibly gather snow, and when it stops the snow melts back. The base pass runs
/// on every tier (Medium floor → WebGL gets the look), but the FOOTPRINTS are gated behind
/// <see cref="WeatherFX.Heavy"/> (High/PC only), mirroring how WetGround keeps its heavy work off min-spec.</para>
///
/// Footprints reuse WetGround's alternating-foot tracking, but a snow print is a PERSISTENT depression that
/// heals slowly (over <see cref="footHeal"/> seconds) instead of an expanding ripple that pops away.
/// </summary>
[DisallowMultipleComponent]
public class SnowGround : MonoBehaviour
{
    [Header("Accumulation")]
    [Range(0f, 60f)] [Tooltip("Seconds for the snow blanket to fully build up (or melt away) when snow starts/stops.")]
    public float accumTime = 14f;
    [Range(0f, 1f)] [Tooltip("How much of the up-facing ground ends up snow-covered at full accumulation.")]
    public float coverage = 1f;
    [Range(0f, 1f)] [Tooltip("Up-facing cutoff: lower = snow clings to steeper slopes (roofs). Higher = flat tops only.")]
    public float upCutoff = 0f;
    [Tooltip("Snow patch size — smaller value = larger patches (it's 1/size in world units).")]
    public float patchScale = 1f;

    [Header("Look")]
    [Tooltip("Bright snow colour on the lit tops.")]
    public Color snowColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Cool tint in crevices and footprint dents.")]
    public Color shadowTint = new Color(0.62f, 0.70f, 0.85f, 1f);
    [Range(0f, 1f)] [Tooltip("Fake VOLUME: domes the deep interior bright and shades a soft lip at the snow edge " +
             "so the blanket reads thick/rounded. (Screen-space can't bulge past silhouettes — that needs the " +
             "geometry snow-shell upgrade; this is the cheap illusion.)")]
    public float thickness = 0f;
    [Range(0f, 1f)] [Tooltip("Breaks the surface into cotton-like clumps instead of a smooth sheet.")]
    public float lumpiness = 0.4f;
    [Tooltip("Colour the snow fades toward at MIDNIGHT so the blanket respects the dark (it's a depth-overwrite " +
             "pass, so it can't be lit like the world — this dims it by hand). Stays bright white through the day.")]
    public Color nightTint = new Color(0.28f, 0.32f, 0.46f, 1f);

    [Header("Footprints (High tier only)")]
    [Tooltip("Seconds a footprint takes to heal/fill back in. Higher = a longer-lasting trail. (Print POSITIONS " +
             "and the gap between them come from the shared FootstepTracker, so wet ripples and snow prints align.)")]
    public float footHeal = 3.3f;
    [Tooltip("Footprint radius in world units.")]
    public float footRadius = 0.1f;
    [Range(0f, 1f)] [Tooltip("How deeply a print carves the snow away (reveals the trodden ground).")]
    public float footCarve = 0.27f;
    [Range(0f, 1f)] [Tooltip("How dark the shadow inside a print is.")]
    public float footDark = 0.4f;

    [Header("Cast shadows (High tier only)")]
    [Range(0f, 1f)] [Tooltip("How strongly the sun's CAST shadows (player/mobs via BillboardShadow) darken the snow. " +
             "0 = snow ignores shadows (saves a shadow-map sample per pixel; auto-forced to 0 below High tier).")]
    public float castShadowStrength = 0.5f;

    Material mat;
    Transform quad;
    Mesh mesh;
    bool depthRequested;
    float accum;   // current ramped blanket amount (0..1); lerps toward the live snow intensity
    MapBiome lastMap;   // the map the blanket was last ramped on, so a map change snaps to the new map's blanket

    // Footprint buffer, filled from the shared FootstepTracker each frame: (worldX, worldZ, age01, sizeScale);
    // dead slots have age >= 1. Positions/gait are the tracker's job — we just age them by OUR heal time.
    const int MaxSteps = FootstepTracker.MaxSteps;
    readonly Vector4[] steps = new Vector4[MaxSteps];
    FootstepTracker foot;

    static readonly int AccumID      = Shader.PropertyToID("_Accum");
    static readonly int SnowColorID  = Shader.PropertyToID("_SnowColor");
    static readonly int LightTintID  = Shader.PropertyToID("_LightTint");
    static readonly int ShadowTintID = Shader.PropertyToID("_ShadowTint");
    static readonly int UpCutoffID   = Shader.PropertyToID("_UpCutoff");
    static readonly int CoverageID   = Shader.PropertyToID("_Coverage");
    static readonly int PatchScaleID = Shader.PropertyToID("_PatchScale");
    static readonly int ThicknessID  = Shader.PropertyToID("_Thickness");
    static readonly int LumpinessID  = Shader.PropertyToID("_Lumpiness");
    static readonly int FootRadiusID = Shader.PropertyToID("_FootRadius");
    static readonly int FootCarveID  = Shader.PropertyToID("_FootCarve");
    static readonly int FootDarkID   = Shader.PropertyToID("_FootDark");
    static readonly int StepsID      = Shader.PropertyToID("_Steps");
    static readonly int ShadowStrengthID = Shader.PropertyToID("_ShadowStrength");

    /// <summary>Find the live snow-ground effect, creating one if none exists.</summary>
    public static SnowGround Ensure()
    {
        var s = FindAnyObjectByType<SnowGround>();
        if (s == null) s = new GameObject("SnowGround") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<SnowGround>();
        return s;
    }

    void Awake()
    {
        if (GameServer.RemovesPresentation(this)) return;
        var sh = Shader.Find("Unwritten/SnowGround");
        if (sh == null)
        {
            GameLog.Warn(LogCat.Unwritten, "SnowGround: Shader 'Unwritten/SnowGround' not found — snow accumulation disabled. " +
                             "(In a build, add it to Project Settings ▸ Graphics ▸ Always Included Shaders.)");
            enabled = false;
            return;
        }
        mat = new Material(sh) { name = "SnowGround (runtime)" };
        for (int i = 0; i < MaxSteps; i++) steps[i] = new Vector4(0f, 0f, 2f, 0f);   // all slots dead
        BuildQuad();
    }

    void OnDisable() { if (quad != null) quad.gameObject.SetActive(false); }
    void OnDestroy()
    {
        if (quad != null) Destroy(quad.gameObject);
        if (mesh != null) Destroy(mesh);
        if (mat  != null) Destroy(mat);
    }

    void BuildQuad()
    {
        var go = new GameObject("SnowGroundQuad") { hideFlags = HideFlags.DontSave };
        go.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera
        quad = go.transform;

        mesh = new Mesh { name = "SnowGroundFullscreen" };
        mesh.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3( 1f,  1f, 0f), new Vector3(-1f, 1f, 0f),
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var rend = go.AddComponent<MeshRenderer>();
        rend.sharedMaterial    = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows    = false;
        rend.lightProbeUsage   = UnityEngine.Rendering.LightProbeUsage.Off;
        go.SetActive(false);
    }

    // Live snow amount, used as the accumulation target. Reads the Snow weather channel (the source of truth);
    // falls back to the overlay's own intensity when there's no channels hub. The ground blanket still builds on its
    // OWN slower clock (accumTime) on top of this — roofs gather snow well after the flakes appear.
    static float SnowAmount
    {
        get
        {
            var wc = WeatherChannels.Instance;
            if (wc != null) return wc.Shown(WeatherChannel.Snow);
            return SnowOverlay.Instance != null ? SnowOverlay.Instance.intensity : 0f;
        }
    }

    void LateUpdate()
    {
        if (mat == null) return;

        // Ramp the blanket toward the live snowfall: builds up when it's snowing, melts back when it stops. A biome
        // that always holds snow keeps it there whatever the sky does.
        float target = enabled ? BiomeWeather.Hold(SnowAmount, BiomeWeather.AlwaysSnowCoverHere) : 0f;
        // Arriving on another map starts at that map's blanket, instead of the last map's snow melting or building.
        if (MapBiome.Current != lastMap) { lastMap = MapBiome.Current; accum = target; }
        accum = Mathf.MoveTowards(accum, target, Time.deltaTime / Mathf.Max(0.1f, accumTime));

        bool show = enabled && accum > 0.001f;
        if (quad != null && quad.gameObject.activeSelf != show) quad.gameObject.SetActive(show);
        if (!show) return;

        var cam = CameraRig.Cam;
        if (cam != null)
        {
            if (!depthRequested)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) { data.requiresDepthTexture = true; depthRequested = true; }
            }
            if (quad != null) quad.position = cam.transform.position;
        }

        mat.SetFloat(AccumID, accum);
        mat.SetColor(SnowColorID, snowColor);
        var tod = TimeOfDay.Instance;
        mat.SetColor(LightTintID, tod != null ? tod.WeatherTint(nightTint) : Color.white);
        mat.SetColor(ShadowTintID, shadowTint);
        mat.SetFloat(UpCutoffID, upCutoff);
        mat.SetFloat(CoverageID, coverage);
        mat.SetFloat(PatchScaleID, patchScale);
        mat.SetFloat(ThicknessID, thickness);
        mat.SetFloat(LumpinessID, lumpiness);
        mat.SetFloat(FootRadiusID, footRadius);
        mat.SetFloat(FootCarveID, footCarve);
        mat.SetFloat(FootDarkID, footDark);
        // Cast shadows: forced to 0 below High tier so min-spec skips the extra shadow-map sample per fullscreen pixel.
        mat.SetFloat(ShadowStrengthID, WeatherFX.Heavy ? castShadowStrength : 0f);

        UpdateFootprints();
        mat.SetVectorArray(StepsID, steps);
    }

    // Footprints are a HEAVY-tier extra: on Low/Medium keep every slot dead so the trail costs nothing. On High,
    // pull the shared footstep positions and age them by OUR long heal time so prints linger and slowly fill in.
    void UpdateFootprints()
    {
        if (!WeatherFX.Heavy)
        {
            for (int i = 0; i < MaxSteps; i++) steps[i].z = 2f;   // all dead → no prints sampled in the shader
            return;
        }
        if (foot == null) foot = FootstepTracker.Ensure();
        foot.SampleInto(steps, footHeal);
    }
}
