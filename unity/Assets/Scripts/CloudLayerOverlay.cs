using UnityEngine;

/// <summary>
/// CLOUD LAYER: patchy soft white clouds floating over the map at treetop height, drifting slowly across it. A single
/// camera-locked full-screen quad runs the <c>Unwritten/CloudLayer</c> shader, which reads the scene depth and, for
/// every pixel, measures how much of the view ray passes through the cloud slab between <see cref="bottomHeight"/> and
/// <see cref="topHeight"/>, so tree tops rise out of the clouds and the ground shows through the gaps. Around the point
/// on the ground the camera looks at, the clouds fade out, so the player is never hidden while the far map dissolves
/// into cloud. Same depth-quad pattern as <see cref="FogOverlay"/> (and it draws just before the fog, so a biome's fog
/// still hazes the clouds by distance).
///
/// <para>It is a BIOME look, not weather: the amount comes only from <see cref="BiomeWeather.AlwaysCloudsHere"/>
/// for the map loaded right now, eased in and out when the map changes. Zero-cost elsewhere: at 0 the quad is
/// deactivated, so it draws nothing. Look only — it decides nothing about the game.</para>
/// </summary>
[DisallowMultipleComponent]
public class CloudLayerOverlay : MonoBehaviour
{
    [Range(0f, 60f)] [Tooltip("Seconds for the clouds to fade in when you arrive on a cloud map (or out when you leave).")]
    public float rampTime = 2f;
    [Tooltip("World height of the underside of the cloud layer (the map's ground sits at about 0).")]
    public float bottomHeight = 2f;
    [Tooltip("World height of the top of the cloud layer (the map's ground sits at about 0).")]
    public float topHeight = 4.5f;
    [Range(2f, 120f)] [Tooltip("World size of one cloud patch (metres). Bigger = broad banks with wide gaps.")]
    public float patchSize = 16f;
    [Range(0f, 1f)] [Tooltip("Roughly the share of the map left clear between cloud patches: 0 = one unbroken layer, " +
                             "1 = no clouds.")]
    public float clearShare = 0.5f;
    [Range(0.05f, 5f)] [Tooltip("How much each metre of cloud the eye looks through hides what is behind it.")]
    public float opacityPerMetre = 0.7f;
    [Tooltip("Where nothing was drawn (the sky), how far along the view the clouds are still counted (metres).")]
    public float maxDistance = 80f;
    [Tooltip("Radius around the point the camera looks at that stays fully clear of cloud (metres).")]
    public float clearRadius = 6f;
    [Tooltip("Radius at which the clouds are back to full (metres); between the two they fade in.")]
    public float clearFadeEnd = 10f;
    [Tooltip("How fast the clouds drift (scrolls the world-pinned noise).")]
    public float driftSpeed = 1.5f;
    [Tooltip("Colour of the sunlit tops of the clouds.")]
    public Color litColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Colour of the clouds' shaded undersides.")]
    public Color shadeColor = new Color(0.76f, 0.80f, 0.88f, 1f);
    [Tooltip("Colour the clouds fade toward at MIDNIGHT so they don't glow over a dark world. White (no change) by day.")]
    public Color nightTint = new Color(0.34f, 0.37f, 0.46f, 1f);

    public static CloudLayerOverlay Instance { get; private set; }

    Material mat;
    Transform quad;
    Mesh mesh;
    float phase;
    float shown;

    static readonly int ColorID           = Shader.PropertyToID("_Color");
    static readonly int ShadeColorID      = Shader.PropertyToID("_ShadeColor");
    static readonly int LightTintID       = Shader.PropertyToID("_LightTint");
    static readonly int IntensityID       = Shader.PropertyToID("_Intensity");
    static readonly int PhaseID           = Shader.PropertyToID("_Phase");
    static readonly int BottomID          = Shader.PropertyToID("_Bottom");
    static readonly int TopID             = Shader.PropertyToID("_Top");
    static readonly int ScaleID           = Shader.PropertyToID("_Scale");
    static readonly int ClearShareID      = Shader.PropertyToID("_ClearShare");
    static readonly int OpacityPerMetreID = Shader.PropertyToID("_OpacityPerMetre");
    static readonly int MaxDistanceID     = Shader.PropertyToID("_MaxDistance");
    static readonly int FocusID           = Shader.PropertyToID("_Focus");
    static readonly int ClearRadiusID     = Shader.PropertyToID("_ClearRadius");
    static readonly int ClearFadeEndID    = Shader.PropertyToID("_ClearFadeEnd");

    /// <summary>Find the live cloud-layer overlay, creating one if none exists yet. A boot-time call (the weather
    /// director makes it alongside the fog), never a per-frame one.</summary>
    public static CloudLayerOverlay Ensure()
    {
        var c = Instance != null ? Instance : FindAnyObjectByType<CloudLayerOverlay>();
        if (c == null) c = new GameObject("CloudLayerOverlay") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<CloudLayerOverlay>();
        return c;
    }

    void Awake()
    {
        Instance = this;
        var sh = Shader.Find("Unwritten/CloudLayer");
        if (sh == null)
        {
            GameLog.Warn(LogCat.Unwritten, "CloudLayerOverlay: Shader 'Unwritten/CloudLayer' not found — cloud layer disabled. " +
                             "(In a build, add it to Project Settings ▸ Graphics ▸ Always Included Shaders.)");
            enabled = false;
            return;
        }
        mat = new Material(sh) { name = "CloudLayer (runtime)" };
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

    // Same full-screen quad trick as FogOverlay: verts span [-1,1], huge bounds so it never frustum-culls.
    void BuildQuad()
    {
        var go = new GameObject("CloudLayerQuad") { hideFlags = HideFlags.DontSave };
        go.layer = LayerMask.NameToLayer("TransparentFX");
        // TransparentFX is excluded from the planar-reflection camera.
        quad = go.transform;

        mesh = new Mesh { name = "CloudLayerFullscreen" };
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
        go.SetActive(false);
    }

    /// <summary>The point on the ground (world Y 0) the camera is looking at: where its forward ray meets the ground,
    /// or straight below the camera when that ray never comes down to it.</summary>
    public static Vector3 GroundFocus(Vector3 camPos, Vector3 camForward)
    {
        if (camForward.y < -1e-4f && camPos.y > 0f)
            return camPos + camForward * (-camPos.y / camForward.y);
        return new Vector3(camPos.x, 0f, camPos.z);
    }

    void LateUpdate()
    {
        if (mat == null) return;

        shown = Mathf.MoveTowards(shown, BiomeWeather.AlwaysCloudsHere, Time.deltaTime / Mathf.Max(0.1f, rampTime));

        bool show = enabled && shown > 0.001f;
        if (quad != null && quad.gameObject.activeSelf != show) quad.gameObject.SetActive(show);
        if (!show) return;

        var cam = CameraRig.Cam;
        if (cam != null)
        {
            var ct = cam.transform;
            if (quad != null) quad.position = ct.position;
            mat.SetVector(FocusID, GroundFocus(ct.position, ct.forward));
        }

        phase += Time.deltaTime * driftSpeed;
        var tod = TimeOfDay.Instance;
        mat.SetColor(ColorID, litColor);
        mat.SetColor(ShadeColorID, shadeColor);
        mat.SetColor(LightTintID, tod != null ? tod.WeatherTint(nightTint) : Color.white);
        mat.SetFloat(IntensityID, shown);
        mat.SetFloat(PhaseID, phase);
        mat.SetFloat(BottomID, bottomHeight);
        mat.SetFloat(TopID, Mathf.Max(bottomHeight + 0.01f, topHeight));
        mat.SetFloat(ScaleID, 1f / Mathf.Max(0.01f, patchSize));
        mat.SetFloat(ClearShareID, clearShare);
        mat.SetFloat(OpacityPerMetreID, opacityPerMetre);
        mat.SetFloat(MaxDistanceID, maxDistance);
        mat.SetFloat(ClearRadiusID, clearRadius);
        mat.SetFloat(ClearFadeEndID, Mathf.Max(clearRadius + 0.01f, clearFadeEnd));
    }
}
