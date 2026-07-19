using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drifting <b>cloud shadows</b> on the whole scene — the dappled dark patches a partly-cloudy sky throws as
/// clouds pass over. Decoupled from the sun on purpose: instead of a directional-light cookie (which rotates
/// with the sun, and our sun SWINGS across the day so the cookie whirled), this MULTIPLY-darkens by a
/// procedurally-generated cloud texture sampled in <b>world X/Z</b>, so the pattern is pinned to the world and
/// only drifts one way with the wind — never spinning — while the sun keeps its dawn/dusk shadow swing.
///
/// <para>How it reaches houses + props (not just the ground): a <b>screen-covering quad</b> reads the scene
/// <b>depth texture</b>, reconstructs each pixel's true world position, and darkens by the cloud value at that
/// world XZ — projecting the clouds straight down onto whatever surface is actually there (roof, wall, rock).
/// The old version was a flat ground plane, so houses stuck up out of it and stayed unshaded. The shader skips
/// sky pixels and draws before the billboard sprites, so characters stay untinted (unchanged from before).</para>
///
/// Cheap: one extra full-screen quad in the transparent queue (no per-object cost) plus the camera's depth
/// texture (a cheap depth-copy on our Forward renderer). The cloud texture is tiling fBm Perlin (no art asset);
/// <see cref="coverage"/>/<see cref="darkness"/>/<see cref="softness"/> bake into it (rebuilt on change,
/// debounced), while <see cref="patchSize"/>/<see cref="windSpeed"/>/<see cref="windDirDeg"/>/
/// <see cref="strength"/> are live. Lives on the Sun (DayNightLight bootstraps it), but it no longer touches
/// the light.
/// </summary>
[DisallowMultipleComponent]
public class CloudShadows : MonoBehaviour
{
    [Header("Cloud look (baked into the texture)")]
    [Range(0f, 1f)] [Tooltip("How much of the sky is clouded — higher = more/larger shadow patches.")]
    public float coverage = 0.39f;
    [Range(0f, 1f)] [Tooltip("How dark the cloud shadows get (0 = invisible, 1 = deep shade).")]
    public float darkness = 1f;
    [Range(0.01f, 0.5f)] [Tooltip("Edge softness of the patches (low = crisp clouds, high = hazy).")]
    public float softness = 0.5f;

    [Header("Drift (live)")]
    [Tooltip("Size of one cloud-shadow tile in world units — bigger = larger, broader patches.")]
    public float patchSize = 10f;
    [Tooltip("How fast the clouds drift on a calm day — the BASE speed the Wind weather channel scales up from.")]
    public float windSpeed = 1.06f;
    [Range(0f, 4f)] [Tooltip("How much the Wind weather channel speeds the drift (clouds scud faster in a gale). " +
                             "0 = ignore weather; the clouds always drift at the base Wind speed.")]
    public float windResponse = 2f;
    [Range(0f, 360f)] [Tooltip("Wind direction in degrees.")]
    public float windDirDeg = 70.67f;
    [Range(0f, 1f)] [Tooltip("Master fade over the whole effect (on top of the baked darkness).")]
    public float strength = 1f;

    [Header("Quality")]
    [Tooltip("Cloud texture resolution. 256 is plenty for soft shadows.")]
    public int resolution = 256;
    [Tooltip("Base cloud cells across the texture (fBm starting frequency).")]
    public int baseCells = 3;
    [Range(1, 5)] [Tooltip("fBm octaves — more = wispier detail.")]
    public int octaves = 3;

    Texture2D tex;
    Material mat;
    Transform quad;
    Mesh mesh;
    bool depthRequested;
    Vector2 scroll;
    float seedX, seedY;
    bool dirty;
    float regenAt;

    static readonly int CloudTexID = Shader.PropertyToID("_CloudTex");
    static readonly int ScaleID    = Shader.PropertyToID("_Scale");
    static readonly int StrengthID = Shader.PropertyToID("_Strength");
    static readonly int ScrollXID  = Shader.PropertyToID("_ScrollX");
    static readonly int ScrollYID  = Shader.PropertyToID("_ScrollY");

    /// <summary>The baked procedural cloud texture (R8: 1 = clear sky, lower = thicker cloud). <see cref="SkyboxSky"/>
    /// samples THIS same texture so the visible sky clouds and the ground cloud-shadows share one pattern.</summary>
    public Texture CloudTexture => tex;

    /// <summary>Find the live cloud-shadows effect, creating one if none exists yet (used by the weather director so
    /// clouds work even on a Systems scene baked before this component joined the rig; normally it's on the Sun).</summary>
    public static CloudShadows Ensure()
    {
        var c = FindFirstObjectByType<CloudShadows>();
        if (c == null) c = new GameObject("CloudShadows") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<CloudShadows>();
        return c;
    }

    void Awake()
    {
        seedX = Random.value * 1000f;
        seedY = Random.value * 1000f;

        var shader = Shader.Find("Unwritten/CloudShadow");
        if (shader == null)
        {
            Debug.LogWarning("[CloudShadows] Shader 'Unwritten/CloudShadow' not found — cloud shadows disabled. " +
                             "(In a build, add it to Project Settings ▸ Graphics ▸ Always Included Shaders.)");
            enabled = false;
            return;
        }
        mat = new Material(shader) { name = "CloudShadow (runtime)" };
        BuildQuad();
        Rebuild();
        PushMaterial();
    }

    void OnEnable()  { if (quad != null) quad.gameObject.SetActive(true); }
    void OnDisable() { if (quad != null) quad.gameObject.SetActive(false); }
    void OnDestroy()
    {
        if (quad != null) Destroy(quad.gameObject);
        if (mesh != null) Destroy(mesh);
        if (mat != null) Destroy(mat);
        if (tex != null) Destroy(tex);
    }

    /// <summary>Flag the baked params (coverage/darkness/softness) as changed; the texture rebuilds shortly
    /// after (debounced so dragging a slider doesn't rebuild every frame). Called by the DevSettings knobs.</summary>
    public void MarkDirty() { dirty = true; regenAt = Time.unscaledTime + 0.12f; }

    void LateUpdate()
    {
        if (mat == null) return;

        if (dirty && Time.unscaledTime >= regenAt) { Rebuild(); dirty = false; }

        // Follow the Cloud weather channel when present — the master fade IS the cloud channel's eased value, so a
        // weather sequence drives cloud cover here exactly like rain/snow/puddle. Falls back to the serialized
        // `strength` (manual/standalone) when there's no channels hub.
        var wc = WeatherChannels.Instance;
        if (wc != null) strength = wc.Shown(WeatherChannel.Cloud);

        // Make sure the camera produces the depth texture our shader samples, and keep the quad parked on the
        // camera so its (huge) bounds always pass culling. The world-XZ sampling is what pins the pattern —
        // the quad's transform only matters for staying un-culled.
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

        // Scroll one direction with the wind (UV space; /patchSize keeps speed ~constant in world terms). The Wind
        // weather channel speeds the drift, so clouds race during a storm and barely move on a still, foggy day.
        float wind01  = WeatherChannels.Instance != null ? WeatherChannels.Instance.Shown(WeatherChannel.Wind) : 0f;
        float effSpeed = windSpeed * (1f + wind01 * windResponse);
        float r = windDirDeg * Mathf.Deg2Rad;
        scroll += new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * (effSpeed * Time.deltaTime / Mathf.Max(1f, patchSize));

        mat.SetFloat(ScaleID, 1f / Mathf.Max(1f, patchSize));
        mat.SetFloat(StrengthID, strength);
        mat.SetFloat(ScrollXID, scroll.x);
        mat.SetFloat(ScrollYID, scroll.y);
    }

    void PushMaterial()
    {
        if (mat == null) return;
        mat.SetTexture(CloudTexID, tex);
        mat.SetFloat(ScaleID, 1f / Mathf.Max(1f, patchSize));
        mat.SetFloat(StrengthID, strength);
    }

    // ── the full-screen multiply quad ────────────────────────────────────────────────────────────────────
    // Verts span [-1,1] in XY; the shader emits them straight to clip space, so it fills the screen whatever
    // the transform is. Giant bounds keep it from ever being frustum-culled.
    void BuildQuad()
    {
        var go = new GameObject("CloudShadowQuad");
        go.hideFlags = HideFlags.DontSave;
        go.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera
        quad = go.transform;

        mesh = new Mesh { name = "CloudShadowFullscreen" };
        mesh.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3( 1f,  1f, 0f), new Vector3(-1f, 1f, 0f),
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);   // never frustum-cull

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var rend = go.AddComponent<MeshRenderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    // ── procedural seamless cloud texture ────────────────────────────────────────────────────────────────
    void Rebuild()
    {
        int res = Mathf.Clamp(resolution, 32, 1024);
        if (tex == null || tex.width != res)
        {
            if (tex != null) Destroy(tex);
            tex = new Texture2D(res, res, TextureFormat.R8, false, true)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "CloudTex" };
        }

        var px = new Color32[res * res];
        float lo = coverage - softness, hi = coverage + softness;
        for (int y = 0; y < res; y++)
        {
            float v = (float)y / res;
            for (int x = 0; x < res; x++)
            {
                float u = (float)x / res;
                float n = Fbm(u, v);
                float cloud = Mathf.SmoothStep(lo, hi, n);          // 0 = clear sky, 1 = thick cloud
                float lum = Mathf.Lerp(1f, 1f - darkness, cloud);   // white sun → darkened patch
                byte b = (byte)(Mathf.Clamp01(lum) * 255f);
                px[y * res + x] = new Color32(b, b, b, 255);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        if (mat != null) mat.SetTexture(CloudTexID, tex);
    }

    float Fbm(float u, float v)
    {
        float total = 0f, amp = 1f, norm = 0f;
        int period = Mathf.Max(1, baseCells);
        for (int o = 0; o < octaves; o++)
        {
            total += Tileable(u * period, v * period, period) * amp;
            norm += amp;
            amp *= 0.5f;
            period *= 2;
        }
        return total / norm;
    }

    // Seamless Perlin: cross-fade the noise with its period-shifted copies so opposite edges match (it tiles).
    float Tileable(float x, float y, int period)
    {
        float p = period, sx = seedX, sy = seedY;
        return (Mathf.PerlinNoise(x + sx,     y + sy)     * (p - x) * (p - y)
              + Mathf.PerlinNoise(x - p + sx, y + sy)     * (x)     * (p - y)
              + Mathf.PerlinNoise(x + sx,     y - p + sy) * (p - x) * (y)
              + Mathf.PerlinNoise(x - p + sx, y - p + sy) * (x)     * (y)) / (p * p);
    }
}
