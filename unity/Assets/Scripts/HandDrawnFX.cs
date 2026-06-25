using UnityEngine;

/// <summary>
/// HAND-DRAWN look — a runtime "art filter" you can dial on top of the finished game, no scene re-bake. It has two
/// halves that together read as ink-on-paper while KEEPING the colour:
///
/// <list type="bullet">
/// <item><b>Ink linework + paper grain</b> — a camera-locked full-screen quad running <c>Unwritten/InkScreen</c>,
/// the exact same depth-overlay pattern as <see cref="FogOverlay"/>/<see cref="WetGround"/>. It reads the scene
/// depth texture and draws dark outlines on object silhouettes and surface creases, plus a faint paper fibre.</item>
/// <item><b>Lowered saturation / contrast</b> — done by the <see cref="HandDrawnGradeFeature"/> URP renderer feature,
/// which blits the whole camera image through a grade material. This component just <b>publishes</b> the live values
/// to the static <see cref="GradeActive"/>/<see cref="GradeSaturation"/>/<see cref="GradeContrast"/> the feature
/// reads each frame. We use a renderer feature (not a Color-Adjustments volume) because a volume only applies when
/// the camera has post-processing on AND its Volume-Mask matches — fragile per project; the feature always runs.
/// <see cref="saturation"/> is CLAMPED to <see cref="MinSaturation"/> so it can mute the palette but <b>never</b>
/// reach greyscale — colour always survives.</item>
/// </list>
///
/// Self-booting + decoupled like the weather renderers: <see cref="Ensure"/> creates it on demand. Zero-cost when
/// off: at <see cref="intensity"/> 0 the ink quad is deactivated (no draw call) and at full saturation/zero contrast
/// the grade feature skips its blit, so it adds nothing until you turn it on. Tune live from DevSettings (P).
///
/// NOTE for builds: add <c>Unwritten/InkScreen</c> AND <c>Unwritten/HandDrawnGrade</c> to Project Settings ▸ Graphics
/// ▸ Always Included Shaders (same requirement as the other Unwritten/* screen shaders). The grade feature must also
/// be registered on the URP renderer — run <b>Tools ▸ unwritten ▸ Setup Hand-Drawn Grade</b> once.
/// </summary>
[DisallowMultipleComponent]
public class HandDrawnFX : MonoBehaviour
{
    // Defaults below are the dialled-in look (DevSettings ▸ Hand-Drawn, 2026-06-25). Ink is ON by default and the
    // filter self-boots (see Boot) so the game launches with the hand-drawn look. Saturation/contrast are left
    // neutral so colour is untouched until you lower them.
    [Header("Master")]
    [Range(0f, 1f)] [Tooltip("Overall strength of the INK linework + paper. 0 = ink off (quad disabled). " +
             "Saturation below is independent of this.")]
    public float intensity = 1f;

    [Header("Ink linework")]
    [Range(0f, 1f)] [Tooltip("Opacity of the ink outlines.")]
    public float lineStrength = 0.97f;
    [Tooltip("How readily SILHOUETTE edges (object outlines) are inked. Higher = more outlines.")]
    public float silhouette = 2.37f;
    [Tooltip("How readily CREASE edges (surface folds/kinks) are inked. Higher = busier interior lines.")]
    public float crease = 2.78f;
    [Range(0.4f, 3f)] [Tooltip("Line thickness in pixels (neighbour sample distance).")]
    public float thickness = 2.14f;
    [Range(0f, 0.5f)] [Tooltip("Edge threshold — raise to ink only the strongest edges (less noise on detailed meshes).")]
    public float edgeThreshold = 0.50f;
    [Tooltip("Ink tint. Slightly warm off-black reads as ink rather than a hard black key-line.")]
    public Color inkColor = new Color(0.08f, 0.07f, 0.06f, 1f);

    [Header("Paper")]
    [Range(0f, 1f)] [Tooltip("Faint paper-fibre grain over the whole frame (subtle).")]
    public float paperGrain = 0.31f;

    [Header("Colour (HandDrawnGrade renderer feature)")]
    [Range(MinSaturation, 1f)] [Tooltip("1 = untouched colour. Lower = muted, illustrated palette. Clamped at " +
             "MinSaturation so it can never go full greyscale.")]
    public float saturation = 1f;
    [Range(-0.3f, 0.3f)] [Tooltip("Optional slight contrast lift to crisp up the inked look. 0 = none.")]
    public float contrast = 0f;

    /// <summary>Hard floor for <see cref="saturation"/> — the colour can be muted but never removed.</summary>
    public const float MinSaturation = 0.3f;

    // ---- live grade values read by HandDrawnGradeFeature (statics so the feature needs no scene lookup) ----
    /// <summary>True while the grade is doing something (saturation &lt; 1 or contrast ≠ 0) — the feature skips its
    /// blit entirely when false, so the look is genuinely zero-cost when off.</summary>
    public static bool  GradeActive;
    /// <summary>Current saturation (MinSaturation..1) the feature applies; 1 = untouched.</summary>
    public static float GradeSaturation = 1f;
    /// <summary>Current contrast (-0.3..0.3) the feature applies; 0 = untouched.</summary>
    public static float GradeContrast;

    /// <summary>Most-recently-awoken instance (mirrors <see cref="FogOverlay.Instance"/>) for DevSettings.</summary>
    public static HandDrawnFX Instance { get; private set; }

    Material mat;
    Transform quad;
    Mesh mesh;

    static readonly int InkColorID  = Shader.PropertyToID("_InkColor");
    static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    static readonly int LineID      = Shader.PropertyToID("_LineStrength");
    static readonly int DepthEdgeID = Shader.PropertyToID("_DepthEdge");
    static readonly int NormalEdgeID= Shader.PropertyToID("_NormalEdge");
    static readonly int ThicknessID = Shader.PropertyToID("_Thickness");
    static readonly int ThreshID    = Shader.PropertyToID("_EdgeThresh");
    static readonly int PaperID     = Shader.PropertyToID("_PaperStrength");

    /// <summary>When true, the filter self-boots on game start (with the tuned defaults above) so the world launches
    /// with the hand-drawn look. Flip to false here to make it opt-in via DevSettings only.</summary>
    public const bool BootEnabled = true;

    // Spawn the filter at startup and re-create it after a map swap destroys it, so the look persists everywhere
    // without DontDestroyOnLoad (matches the on-demand pattern the weather overlays use).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (BootEnabled)
        {
            Ensure();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => { if (Instance == null) Ensure(); };
        }
    }

    /// <summary>Find the live hand-drawn FX, creating one if none exists yet (used by the DevSettings knobs so it
    /// works even on a Systems scene baked before this component joined the rig).</summary>
    public static HandDrawnFX Ensure()
    {
        var f = Instance != null ? Instance : FindFirstObjectByType<HandDrawnFX>();
        if (f == null) f = new GameObject("HandDrawnFX").AddComponent<HandDrawnFX>();
        return f;
    }

    void Awake()
    {
        Instance = this;
        var sh = Shader.Find("Unwritten/InkScreen");
        if (sh == null)
        {
            Debug.LogWarning("[HandDrawnFX] Shader 'Unwritten/InkScreen' not found — ink linework disabled. " +
                             "(In a build, add it to Project Settings ▸ Graphics ▸ Always Included Shaders.) " +
                             "Saturation still works.");
        }
        else
        {
            mat = new Material(sh) { name = "InkScreen (runtime)" };
            BuildQuad();
        }
    }

    void OnDisable()
    {
        if (quad != null) quad.gameObject.SetActive(false);
        GradeActive = false;   // stop the feature blitting while we're off
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        GradeActive = false;
        if (quad != null) Destroy(quad.gameObject);
        if (mesh != null) Destroy(mesh);
        if (mat  != null) Destroy(mat);
    }

    // Same full-screen quad trick as the other overlays: verts span [-1,1], huge bounds so it never frustum-culls.
    void BuildQuad()
    {
        var go = new GameObject("InkQuad") { hideFlags = HideFlags.DontSave };
        go.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera
        quad = go.transform;

        mesh = new Mesh { name = "InkFullscreen" };
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
        go.SetActive(false);   // off until intensity is raised
    }

    void LateUpdate()
    {
        // ---- Ink linework + paper (the full-screen quad) ----
        bool inkOn = enabled && mat != null && (intensity > 0.001f);
        if (quad != null && quad.gameObject.activeSelf != inkOn) quad.gameObject.SetActive(inkOn);
        if (inkOn)
        {
            var cam = CameraRig.Cam;
            if (cam != null) quad.position = cam.transform.position;   // park on camera so it never culls

            mat.SetColor(InkColorID, inkColor);
            mat.SetFloat(IntensityID, intensity);
            mat.SetFloat(LineID, lineStrength);
            mat.SetFloat(DepthEdgeID, silhouette);
            mat.SetFloat(NormalEdgeID, crease);
            mat.SetFloat(ThicknessID, thickness);
            mat.SetFloat(ThreshID, edgeThreshold);
            mat.SetFloat(PaperID, paperGrain);
        }

        // ---- Colour grade: just publish the values; HandDrawnGradeFeature does the actual blit ----
        saturation = Mathf.Clamp(saturation, MinSaturation, 1f);   // never let it reach greyscale
        GradeSaturation = saturation;
        GradeContrast   = contrast;
        GradeActive     = enabled && (saturation < 0.999f || Mathf.Abs(contrast) > 0.001f);
    }
}
