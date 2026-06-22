using UnityEngine;

/// <summary>
/// Screen-space SNOW: the twin of <see cref="RainOverlay"/>. A single camera-locked full-screen quad runs the
/// <c>Unwritten/SnowScreen</c> shader, which paints DRIFTING FLAKES procedurally — so there are no flake objects
/// and the GPU cost is fixed (same in an empty field as mid-combat with a screen full of mobs). It's a pure 2D
/// overlay (no depth), drawn after everything; the world-surface snow build-up is a separate pass
/// (<see cref="SnowGround"/>) that follows this overlay's <see cref="intensity"/>.
///
/// <para>Zero-cost when clear: at <see cref="intensity"/> ≈ 0 the quad is deactivated, so it draws nothing.
/// Tune it live from the DevSettings panel (<b>P</b>) under "Snow". The fall scroll (<see cref="phase"/>) is
/// advanced in C# so it's framerate-independent and freezes if the game is paused. Runs on every quality tier
/// (it's cheap), unlike the heavier <see cref="SnowGround"/> footprints which are High-tier only.</para>
/// </summary>
[DisallowMultipleComponent]
public class SnowOverlay : MonoBehaviour
{
    [Range(0f, 1f)] [Tooltip("Master snow amount (the TARGET the flakes ease toward). 0 = clear (quad disabled). " +
             "Default 0 — snow is OFF until raised (DevSettings 'Snow' toggle, or a weather preset).")]
    public float intensity = 0f;
    [Range(0f, 60f)] [Tooltip("Seconds for the flakes to fade IN when snow starts (or out when it stops). Kept " +
             "separate from SnowGround's accumTime — flakes appear fast, the ground blanket builds up slower.")]
    public float rampTime = 3f;
    [Range(-1f, 1f)] [Tooltip("Camera parallax: how much the flakes drift WITH camera pan/rotate so they feel " +
             "world-anchored, not glued to the lens. 0 = locked to screen. Flip the sign to reverse the direction.")]
    public float parallax = 1f;
    [Tooltip("How fast the flakes fall (lower than rain — snow drifts down).")]
    public float fallSpeed = 5.55f;
    [Range(0f, 1f)] [Tooltip("Sideways flutter/wind sway of the flakes.")]
    public float drift = 1f;
    [Tooltip("Number of flake lanes across the screen — higher = denser, smaller flakes.")]
    public float density = 16.52f;
    [Range(0.05f, 0.9f)] [Tooltip("Flake size.")]
    public float flakeSize = 0.71f;
    [Tooltip("Flake tint + max opacity (alpha).")]
    public Color color = new Color(0.96f, 0.98f, 1f, 0.9f);
    [Tooltip("Colour the flakes fade toward at MIDNIGHT so they don't glow white over a dark world. White by day.")]
    public Color nightTint = new Color(0.30f, 0.34f, 0.48f, 1f);

    /// <summary>Most-recently-awoken snow overlay, so the heavy ground-accumulation pass can read the live
    /// snow amount without a hard reference (mirrors <see cref="RainOverlay.Instance"/>).</summary>
    public static SnowOverlay Instance { get; private set; }

    Material mat;
    Transform quad;
    Mesh mesh;
    float phase;
    float shown;   // ramped flake amount (0..1) easing toward `intensity` — the actual value fed to the shader

    Vector2 parOffset;        // accumulated parallax offset (screen UV) pushed to _Parallax
    Vector3 prevCamPos;
    Quaternion prevCamRot;
    bool hasPrevCam;

    static readonly int ColorID     = Shader.PropertyToID("_Color");
    static readonly int LightTintID = Shader.PropertyToID("_LightTint");
    static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    static readonly int PhaseID     = Shader.PropertyToID("_Phase");
    static readonly int DriftID     = Shader.PropertyToID("_Drift");
    static readonly int DensityID   = Shader.PropertyToID("_Density");
    static readonly int FlakeSizeID = Shader.PropertyToID("_FlakeSize");
    static readonly int ParallaxID  = Shader.PropertyToID("_Parallax");

    /// <summary>Find the live snow overlay, creating one if none exists yet (used by the DevSettings knob so
    /// snow works even on a Systems scene baked before this component joined the rig).</summary>
    public static SnowOverlay Ensure()
    {
        var s = FindFirstObjectByType<SnowOverlay>();
        if (s == null) s = new GameObject("SnowOverlay").AddComponent<SnowOverlay>();
        return s;
    }

    void Awake()
    {
        Instance = this;
        var sh = Shader.Find("Unwritten/SnowScreen");
        if (sh == null)
        {
            Debug.LogWarning("[SnowOverlay] Shader 'Unwritten/SnowScreen' not found — snow disabled. " +
                             "(In a build, add it to Project Settings ▸ Graphics ▸ Always Included Shaders.)");
            enabled = false;
            return;
        }
        mat = new Material(sh) { name = "SnowScreen (runtime)" };
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

    // Same full-screen quad trick as RainOverlay: verts span [-1,1], huge bounds so it never frustum-culls.
    void BuildQuad()
    {
        var go = new GameObject("SnowQuad") { hideFlags = HideFlags.DontSave };
        go.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera
        quad = go.transform;

        mesh = new Mesh { name = "SnowFullscreen" };
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
        go.SetActive(false);   // clear until intensity is raised
    }

    void LateUpdate()
    {
        if (mat == null) return;

        // Ease the flakes IN/OUT instead of popping: `shown` ramps toward the live intensity, so when snow starts
        // the flakes build 0→full (alongside the ground accumulation) and fade out when it stops.
        shown = Mathf.MoveTowards(shown, intensity, Time.deltaTime / Mathf.Max(0.1f, rampTime));

        // Zero-cost when clear: deactivate the quad so it issues no draw call at all.
        bool show = enabled && shown > 0.001f;
        if (quad != null && quad.gameObject.activeSelf != show) quad.gameObject.SetActive(show);
        if (!show) return;

        var cam = CameraRig.Cam;
        if (cam != null)
        {
            var ct = cam.transform;
            if (quad != null) quad.position = ct.position;   // park on camera so it never culls

            // Camera parallax: shift the flakes WITH the camera so they read as world-anchored, not lens-stuck.
            // Yaw → horizontal, pitch → vertical (deg/90 → screen units); translation along the camera's right/up.
            if (hasPrevCam && parallax != 0f)
            {
                Vector3 e = ct.eulerAngles, pe = prevCamRot.eulerAngles;
                float dyaw   = Mathf.DeltaAngle(pe.y, e.y);
                float dpitch = Mathf.DeltaAngle(pe.x, e.x);
                Vector3 dp = ct.position - prevCamPos;
                float dx = Vector3.Dot(dp, ct.right);
                float dy = Vector3.Dot(dp, ct.up);
                // Horizontal (pan left/right) is boosted ×4 for a stronger drift; no wrap — accumulate freely so it
                // never snaps/resets (Mathf.Repeat would jump when the offset crossed 0). Float precision is fine
                // for a session, and the shader frac()s the pattern anyway.
                parOffset.x += parallax * 4f * (dyaw / 90f + dx * 0.05f);
                parOffset.y += parallax * (-dpitch / 90f + dy * 0.05f);
            }
            prevCamPos = ct.position; prevCamRot = ct.rotation; hasPrevCam = true;
        }

        phase += Time.deltaTime * fallSpeed;
        mat.SetColor(ColorID, color);
        var tod = TimeOfDay.Instance;
        mat.SetColor(LightTintID, tod != null ? tod.WeatherTint(nightTint) : Color.white);
        mat.SetFloat(IntensityID, shown);
        mat.SetFloat(PhaseID, phase);
        mat.SetFloat(DriftID, drift);
        mat.SetFloat(DensityID, density);
        mat.SetFloat(FlakeSizeID, flakeSize);
        mat.SetVector(ParallaxID, parOffset);
    }
}
