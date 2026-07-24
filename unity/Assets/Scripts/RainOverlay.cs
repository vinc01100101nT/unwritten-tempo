using UnityEngine;

/// <summary>
/// SPIKE: screen-space rain. A single camera-locked full-screen quad runs the <c>Unwritten/RainScreen</c>
/// shader, which paints falling streaks procedurally — so there are no raindrop objects and the GPU cost is
/// fixed (same in an empty field as mid-combat with a screen full of mobs). Modelled on <see cref="CloudShadows"/>,
/// minus the depth sampling and the baked texture (rain is a pure 2D overlay drawn after everything).
///
/// <para>Zero-cost when dry: at <see cref="intensity"/> ≈ 0 the quad is deactivated, so it draws nothing.
/// Tune it live from the DevSettings panel (<b>P</b>) under "Rain". The clock-style scroll (<see cref="phase"/>)
/// is advanced in C# so it's framerate-independent and freezes if the game is paused.</para>
/// </summary>
[DisallowMultipleComponent]
public class RainOverlay : MonoBehaviour
{
    [Range(0f, 1f)] [Tooltip("TARGET rain amount the streaks ease toward — the per-map MAX (default 1). Flip this " +
             "on/off; the VISIBLE rain ramps to it over rampTime instead of popping. 0 = dry (quad disabled).")]
    public float intensity = 1f;
    [Range(0f, 60f)] [Tooltip("Seconds for the rain to fade IN when it starts (and OUT when it stops) — the storm " +
             "builds 0→target gradually and tapers off when it clears. Mirrors SnowOverlay.rampTime.")]
    public float rampTime = 4f;
    [Tooltip("How fast the streaks fall.")]
    public float fallSpeed = 20f;
    [Range(-1f, 1f)] [Tooltip("Base wind slant — negative leans left, positive leans right. The Wind weather " +
                              "channel adds to this, so rain blows harder sideways in a storm.")]
    public float slant = 0.2f;
    [Range(0f, 1.5f)] [Tooltip("How far the Wind weather channel tilts the rain on top of the base slant (0 = " +
                               "ignore wind; rain falls at the fixed base slant).")]
    public float windSlant = 0.7f;
    [Tooltip("Number of vertical rain lanes across the screen — higher = denser, thinner rain.")]
    public float density = 160f;
    [Tooltip("Rain tint + max opacity (alpha).")]
    public Color color = new Color(0.85f, 0.90f, 1f, 0.85f);

    /// <summary>Most-recently-awoken rain overlay, so the heavy wet-ground/splash effects can read the live
    /// rain amount without a hard reference.</summary>
    public static RainOverlay Instance { get; private set; }

    /// <summary>The eased, on-screen rain amount (0..1) — ramps toward <see cref="intensity"/> over
    /// <see cref="rampTime"/>. Consumers that should follow the VISIBLE rain (e.g. rain audio) read this, not the
    /// raw target. (The wet-ground soak/dry timers trigger off the raw <see cref="intensity"/> instead, so the
    /// ground starts soaking the instant rain is commanded on — "from 0 onwards".)</summary>
    public float Shown => shown;

    Material mat;
    Transform quad;
    Mesh mesh;
    float phase;
    float shown;   // ramped rain amount (0..1) easing toward `intensity` — the actual value fed to the shader
    bool primed;   // first-frame: snap `shown` to the map's starting rain so we don't fade in on load

    static readonly int ColorID     = Shader.PropertyToID("_Color");
    static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    static readonly int PhaseID     = Shader.PropertyToID("_Phase");
    static readonly int SlantID     = Shader.PropertyToID("_Slant");
    static readonly int DensityID   = Shader.PropertyToID("_Density");

    /// <summary>Find the live rain overlay, creating one if none exists yet (used by the DevSettings knob so
    /// rain works even on a scene built before this component was added to the rig).</summary>
    public static RainOverlay Ensure()
    {
        var r = FindFirstObjectByType<RainOverlay>();
        if (r == null) r = new GameObject("RainOverlay") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<RainOverlay>();
        return r;
    }

    void Awake()
    {
        Instance = this;
        var sh = Shader.Find("Unwritten/RainScreen");
        if (sh == null)
        {
            Debug.LogWarning("[RainOverlay] Shader 'Unwritten/RainScreen' not found — rain disabled. " +
                             "(In a build, add it to Project Settings ▸ Graphics ▸ Always Included Shaders.)");
            enabled = false;
            return;
        }
        mat = new Material(sh) { name = "RainScreen (runtime)" };
        BuildQuad();
    }

    void OnDisable() { if (quad != null) quad.gameObject.SetActive(false); }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (quad != null) Destroy(quad.gameObject);
        if (mesh != null) Destroy(mesh);
        if (mat  != null) Destroy(mat);
    }

    // Same full-screen quad trick as CloudShadows: verts span [-1,1], huge bounds so it never frustum-culls.
    void BuildQuad()
    {
        var go = new GameObject("RainQuad") { hideFlags = HideFlags.DontSave };
        go.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera
        quad = go.transform;

        mesh = new Mesh { name = "RainFullscreen" };
        mesh.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3( 1f,  1f, 0f), new Vector3(-1f, 1f, 0f),
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var rend = go.AddComponent<MeshRenderer>();
        rend.sharedMaterial      = mat;
        rend.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows      = false;
        rend.lightProbeUsage     = UnityEngine.Rendering.LightProbeUsage.Off;
        go.SetActive(false);   // dry until intensity is raised
    }

    void LateUpdate()
    {
        if (mat == null) return;

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

        // Zero-cost when dry: deactivate the quad so it issues no draw call at all.
        bool show = enabled && shown > 0.001f;
        if (quad != null && quad.gameObject.activeSelf != show) quad.gameObject.SetActive(show);
        if (!show) return;

        var cam = CameraRig.Cam;
        if (cam != null && quad != null) quad.position = cam.transform.position;   // park on camera so it never culls

        // Keep the scroll clock small so it never loses float precision on a marathon session. 10240 is an exact
        // multiple of the shader's per-sheet repeat length (1024) for every rain layer's speed, so the wrap is
        // bit-seamless — no visible jump. Without this the counter would grow unbounded and, after ~1000h of
        // continuous on-screen rain, hit float's 2^23 ceiling and freeze mid-fall (see RainScreen.shader PERIOD).
        phase = Mathf.Repeat(phase + Time.deltaTime * fallSpeed, 10240f);
        mat.SetColor(ColorID, color);
        mat.SetFloat(IntensityID, shown);
        mat.SetFloat(PhaseID, phase);
        // Lean the rain with the wind: the Wind channel × the horizontal component of the wind direction, added on
        // top of the manual base slant. So a calm shower falls near-straight and a gale drives it hard sideways.
        float windLean = WindGlobals.CurrentWind01 * windSlant * Mathf.Cos(WindGlobals.CurrentDirDeg * Mathf.Deg2Rad);
        mat.SetFloat(SlantID, Mathf.Clamp(slant + windLean, -1f, 1f));
        mat.SetFloat(DensityID, density);
    }
}
