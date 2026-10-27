using UnityEngine;

/// <summary>
/// DEPTH FOG, always on at full on every map and in every weather (fog is not a weather). A single camera-locked
/// full-screen quad runs the <c>Unwritten/FogScreen</c>
/// shader, which reads the scene DEPTH texture and fogs each pixel by its TRUE distance from the camera — so far
/// trees/houses/ground genuinely dissolve into haze (<see cref="startDistance"/>→<see cref="fullDistance"/> fade band)
/// while what's near the player stays clear. Same depth-quad pattern as <see cref="WetGround"/>/<see cref="CloudShadows"/>
/// (needs the URP depth texture on, which they already require). The veil is textured with world-pinned noise so the
/// banks are anchored to the world and you move THROUGH them. GPU cost is fixed regardless of mob count (one pass).
///
/// <para>Draws LAST (Overlay queue, after the billboard characters) so it tints over the world AND the sprite
/// mobs/player; sprites aren't in the depth texture, so they fog by the distance of the ground directly behind them
/// (≈ their own distance). It reads no weather channel: nothing turns it down.</para>
/// </summary>
[DisallowMultipleComponent]
public class FogOverlay : MonoBehaviour
{
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

    /// <summary>Most-recently-awoken fog overlay (mirrors <see cref="RainOverlay.Instance"/>).</summary>
    public static FogOverlay Instance { get; private set; }

    Material mat;
    DayNightLight dayNight;
    Transform quad;
    Mesh mesh;
    float phase;

    static readonly int ColorID     = Shader.PropertyToID("_Color");
    static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    static readonly int PhaseID     = Shader.PropertyToID("_Phase");
    static readonly int ScaleID     = Shader.PropertyToID("_Scale");
    static readonly int CoverageID  = Shader.PropertyToID("_Coverage");
    static readonly int FloorID     = Shader.PropertyToID("_Floor");
    static readonly int FogStartID  = Shader.PropertyToID("_FogStart");
    static readonly int FogEndID    = Shader.PropertyToID("_FogEnd");

    /// <summary>Find the live fog overlay, creating one if none exists yet (used by the
    /// director so fog works even on a Systems scene baked before this component joined the rig).</summary>
    public static FogOverlay Ensure()
    {
        var f = Instance != null ? Instance : FindAnyObjectByType<FogOverlay>();
        if (f == null) f = new GameObject("FogOverlay") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<FogOverlay>();
        return f;
    }

    void Awake()
    {
        if (GameServer.RemovesPresentation(this)) return;
        Instance = this;
        var sh = Shader.Find("Unwritten/FogScreen");
        if (sh == null)
        {
            GameLog.Warn(LogCat.Unwritten, "FogOverlay: Shader 'Unwritten/FogScreen' not found — fog disabled. " +
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
    }

    void LateUpdate()
    {
        if (mat == null) return;

        // FOG IS ALWAYS ON, at full, on every map and in every weather — it is no longer a weather of its own.
        bool show = enabled;
        if (quad != null && quad.gameObject.activeSelf != show) quad.gameObject.SetActive(show);
        if (!show) return;

        var cam = CameraRig.Cam;
        if (cam != null && quad != null) quad.position = cam.transform.position;   // park on camera so it never culls

        phase += Time.deltaTime * driftSpeed;
        // The haze is the sky's own colour at the horizon — the one the sky itself is drawn with — so far ground fades
        // into the sky at every hour, dark at night and warm at dusk, with no colour of its own to drift from it.
        if (dayNight == null) dayNight = FindAnyObjectByType<DayNightLight>();
        var sky = dayNight != null ? dayNight.SkyColorNow : DayNightLight.StartSkyColor;
        sky.a = 1f;
        mat.SetColor(ColorID, sky);
        mat.SetFloat(IntensityID, 1f);
        mat.SetFloat(PhaseID, phase);
        mat.SetFloat(ScaleID, 1f / Mathf.Max(0.01f, patchSize));
        // The player's fog thickness (Settings ▸ Graphics) thins the whole veil; it never goes below the share that still
        // hides the edge of the loaded ground.
        float thickness = GameSettings.FogThickness;
        mat.SetFloat(CoverageID, coverage * thickness);
        mat.SetFloat(FloorID, floor * thickness);
        mat.SetFloat(FogStartID, startDistance);
        mat.SetFloat(FogEndID, Mathf.Max(startDistance + 0.1f, fullDistance));
    }
}
