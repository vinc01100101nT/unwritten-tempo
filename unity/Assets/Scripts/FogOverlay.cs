using UnityEngine;

/// <summary>
/// DEPTH FOG: the dedicated renderer that makes the <see cref="WeatherDirector.Preset.Fog"/> preset actually look
/// foggy instead of just "heavy overcast". A single camera-locked full-screen quad runs the <c>Unwritten/FogScreen</c>
/// shader, which reads the scene DEPTH texture and fogs each pixel by its TRUE distance from the camera — so far
/// trees/houses/ground genuinely dissolve into haze (<see cref="startDistance"/>→<see cref="fullDistance"/> fade band)
/// while what's near the player stays clear. Same depth-quad pattern as <see cref="WetGround"/>/<see cref="CloudShadows"/>
/// (needs the URP depth texture on, which they already require). The veil is textured with world-pinned noise so the
/// banks are anchored to the world and you move THROUGH them. GPU cost is fixed regardless of mob count (one pass).
///
/// <para>Draws LAST (Overlay queue, after the billboard characters) so it tints over the world AND the sprite
/// mobs/player; sprites aren't in the depth texture, so they fog by the distance of the ground directly behind them
/// (≈ their own distance). Decoupled like every other renderer: it follows its own <see cref="WeatherChannel.Fog"/>
/// channel (central easing lives in <see cref="WeatherChannels"/>), so a sequence can roll fog in gradually,
/// independent of clouds and rain. Zero-cost when clear: at fog ≈ 0 the quad is deactivated, so it draws nothing.
/// Tune it live from the DevSettings panel (<b>P</b>) under "Fog".</para>
/// </summary>
[DisallowMultipleComponent]
public class FogOverlay : MonoBehaviour
{
    [Range(0f, 1f)] [Tooltip("Master fog amount (the TARGET the haze eases toward). 0 = clear (quad disabled). " +
             "Default 0 — fog is OFF until raised (DevSettings 'Fog', or the Fog weather preset).")]
    public float intensity = 0f;
    [Range(0f, 60f)] [Tooltip("Seconds for the fog to roll IN when it starts (or out when it clears) — used by the " +
             "standalone fallback; weather sequences set their own ramp.")]
    public float rampTime = 6f;
    [Tooltip("World distance from the camera where fog BEGINS — anything nearer than this stays clear.")]
    public float startDistance = 10f;
    [Tooltip("World distance from the camera where fog is FULLY thick — anything beyond is lost in the haze. " +
             "The gap between start and full is the fade band.")]
    public float fullDistance = 29f;
    [Tooltip("How fast the haze rolls/billows (scrolls the world-pinned noise).")]
    public float driftSpeed = 4f;
    [Range(2f, 120f)] [Tooltip("World size of a fog patch (metres). Bigger = broad slow banks; smaller = busier wisps.")]
    public float patchSize = 10f;
    [Range(0f, 1f)] [Tooltip("Max opacity at full intensity — how much the fog hides the distance.")]
    public float coverage = 1f;
    [Range(0f, 1f)] [Tooltip("Minimum veil in fogged areas even where the noise is thin, so distance reads as solid mist.")]
    public float floor = 0.8f;
    [Tooltip("Fog tint + max opacity (alpha).")]
    public Color color = new Color(0.80f, 0.82f, 0.85f, 1f);
    [Tooltip("Colour the fog fades toward at MIDNIGHT so it doesn't glow over a dark world. White (no change) by day.")]
    public Color nightTint = new Color(0.34f, 0.37f, 0.46f, 1f);

    /// <summary>Most-recently-awoken fog overlay (mirrors <see cref="RainOverlay.Instance"/>) for DevSettings.</summary>
    public static FogOverlay Instance { get; private set; }

    Material mat;
    Transform quad;
    Mesh mesh;
    float phase;
    float shown;   // ramped fog amount (0..1) easing toward `intensity` — the actual value fed to the shader

    static readonly int ColorID     = Shader.PropertyToID("_Color");
    static readonly int LightTintID = Shader.PropertyToID("_LightTint");
    static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    static readonly int PhaseID     = Shader.PropertyToID("_Phase");
    static readonly int ScaleID     = Shader.PropertyToID("_Scale");
    static readonly int CoverageID  = Shader.PropertyToID("_Coverage");
    static readonly int FloorID     = Shader.PropertyToID("_Floor");
    static readonly int FogStartID  = Shader.PropertyToID("_FogStart");
    static readonly int FogEndID    = Shader.PropertyToID("_FogEnd");

    /// <summary>Find the live fog overlay, creating one if none exists yet (used by the DevSettings knob + the
    /// director so fog works even on a Systems scene baked before this component joined the rig).</summary>
    public static FogOverlay Ensure()
    {
        var f = Instance != null ? Instance : FindFirstObjectByType<FogOverlay>();
        if (f == null) f = new GameObject("FogOverlay").AddComponent<FogOverlay>();
        return f;
    }

    void Awake()
    {
        Instance = this;
        var sh = Shader.Find("Unwritten/FogScreen");
        if (sh == null)
        {
            Debug.LogWarning("[FogOverlay] Shader 'Unwritten/FogScreen' not found — fog disabled. " +
                             "(In a build, add it to Project Settings ▸ Graphics ▸ Always Included Shaders.)");
            enabled = false;
            return;
        }
        mat = new Material(sh) { name = "FogScreen (runtime)" };
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

    // Same full-screen quad trick as the other overlays: verts span [-1,1], huge bounds so it never frustum-culls.
    void BuildQuad()
    {
        var go = new GameObject("FogQuad") { hideFlags = HideFlags.DontSave };
        go.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera
        quad = go.transform;

        mesh = new Mesh { name = "FogFullscreen" };
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

        // Follow the Fog weather channel when present (central easing in WeatherChannels); fall back to the local
        // `intensity` ease when there's no channels hub (standalone / DevSettings on a bare scene).
        var wc = WeatherChannels.Instance;
        if (wc != null) shown = wc.Shown(WeatherChannel.Fog);
        else            shown = Mathf.MoveTowards(shown, intensity, Time.deltaTime / Mathf.Max(0.1f, rampTime));

        // Zero-cost when clear: deactivate the quad so it issues no draw call at all.
        bool show = enabled && shown > 0.001f;
        if (quad != null && quad.gameObject.activeSelf != show) quad.gameObject.SetActive(show);
        if (!show) return;

        var cam = CameraRig.Cam;
        if (cam != null && quad != null) quad.position = cam.transform.position;   // park on camera so it never culls

        phase += Time.deltaTime * driftSpeed;
        mat.SetColor(ColorID, color);
        var tod = TimeOfDay.Instance;
        mat.SetColor(LightTintID, tod != null ? tod.WeatherTint(nightTint) : Color.white);
        mat.SetFloat(IntensityID, shown);
        mat.SetFloat(PhaseID, phase);
        mat.SetFloat(ScaleID, 1f / Mathf.Max(0.01f, patchSize));
        mat.SetFloat(CoverageID, coverage);
        mat.SetFloat(FloorID, floor);
        mat.SetFloat(FogStartID, startDistance);
        mat.SetFloat(FogEndID, Mathf.Max(startDistance + 0.1f, fullDistance));
    }
}
