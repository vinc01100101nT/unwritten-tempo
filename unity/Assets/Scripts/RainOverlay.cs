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
    [Range(0f, 1f)] [Tooltip("Master rain amount. 0 = dry (quad disabled, zero cost).")]
    public float intensity = 1f;
    [Tooltip("How fast the streaks fall.")]
    public float fallSpeed = 20f;
    [Range(-1f, 1f)] [Tooltip("Wind slant — negative leans left, positive leans right.")]
    public float slant = 0.2f;
    [Tooltip("Number of vertical rain lanes across the screen — higher = denser, thinner rain.")]
    public float density = 160f;
    [Tooltip("Rain tint + max opacity (alpha).")]
    public Color color = new Color(0.85f, 0.90f, 1f, 0.85f);

    /// <summary>Most-recently-awoken rain overlay, so the heavy wet-ground/splash effects can read the live
    /// rain amount without a hard reference.</summary>
    public static RainOverlay Instance { get; private set; }

    Material mat;
    Transform quad;
    Mesh mesh;
    float phase;

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
        if (r == null) r = new GameObject("RainOverlay").AddComponent<RainOverlay>();
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

        // Zero-cost when dry: deactivate the quad so it issues no draw call at all.
        bool show = enabled && intensity > 0.001f;
        if (quad != null && quad.gameObject.activeSelf != show) quad.gameObject.SetActive(show);
        if (!show) return;

        var cam = CameraRig.Cam;
        if (cam != null && quad != null) quad.position = cam.transform.position;   // park on camera so it never culls

        phase += Time.deltaTime * fallSpeed;
        mat.SetColor(ColorID, color);
        mat.SetFloat(IntensityID, intensity);
        mat.SetFloat(PhaseID, phase);
        mat.SetFloat(SlantID, slant);
        mat.SetFloat(DensityID, density);
    }
}
