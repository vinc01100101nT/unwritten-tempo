using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// HEAVY weather: drives the <c>Unwritten/WetGround</c> screen-space pass — darkens wet surfaces and adds glossy
/// rippling puddle reflections. Built on the exact full-screen-depth-quad pattern as <see cref="CloudShadows"/>
/// (camera-locked quad, requests the camera depth texture, world-XZ sampling), so it projects onto whatever the
/// depth buffer holds. Gated behind <see cref="WeatherFX.Heavy"/> (High tier only) so it never costs anything
/// on Low/Medium, and its master amount follows the live rain (<see cref="RainOverlay.Instance"/>).
/// </summary>
[DisallowMultipleComponent]
public class WetGround : MonoBehaviour
{
    [Header("Wet look")]
    [Range(0f, 1f)] [Tooltip("Extra wetness multiplier on top of the live rain amount.")]
    public float wetness = 1f;
    [Range(0f, 1f)] [Tooltip("How much wet surfaces darken.")]
    public float darken = 0.66f;
    [Range(0f, 1f)] [Tooltip("How much of the ground pools into puddles.")]
    public float puddleAmount = 0.59f;
    [Tooltip("Puddle size — smaller value = larger puddles (it's 1/size in world units).")]
    public float puddleScale = 0.40f;

    [Header("Reflection + ripples")]
    [Tooltip("Tint of the faked puddle reflection (sky colour).")]
    public Color reflectColor = new Color(0.60f, 0.72f, 0.95f, 1f);
    [Range(0f, 4f)] [Tooltip("Brightness of the puddle reflection highlight.")]
    public float specStrength = 0f;
    [Tooltip("Ripple animation speed.")]
    public float rippleSpeed = 4f;
    [Range(0f, 1f)] [Tooltip("Ripple distortion strength.")]
    public float rippleStrength = 0.35f;

    [Header("Ripple rings")]
    [Tooltip("Density of the ambient rain-ripple grid (higher = smaller, more frequent rings).")]
    public float ripScale = 0.5f;
    [Tooltip("Ambient ripple animation speed.")]
    public float ripSpeed = 1f;
    [Range(0f, 4f)] [Tooltip("Strength of the ambient rain ripples on puddles.")]
    public float ripAmbient = 0.44f;

    [Header("Footstep ripples")]
    [Tooltip("Seconds a footstep ripple takes to expand and fade. (The step POSITIONS come from the shared " +
             "FootstepTracker — gap/spacing/jump-splash are tuned there so puddles and snow stay in sync.)")]
    public float stepLife = 0.7f;
    [Tooltip("How far a footstep ripple expands (world units). Small = a tight little splash.")]
    public float stepRadius = 0.35f;
    [Range(0f, 8f)] [Tooltip("Brightness of footstep ripples (only show on puddles).")]
    public float stepBright = 1.76f;

    [Header("Planar reflection")]
    [Range(0f, 2f)] [Tooltip("How strongly the mirrored scene shows in puddles. Needs a PlanarReflection in the " +
        "rig (the builder adds one) + High tier; PlanarReflection binds _ReflectionTex to black when off, so this " +
        "stays safe even at >0.")]
    public float reflStrength = 0.90f;
    [Range(0f, 0.3f)] [Tooltip("Ripple distortion of the reflection.")]
    public float reflDistort = 0.04f;
    [Tooltip("Flip the reflection vertically (fix if it renders upside-down on your platform).")]
    public bool reflFlipY = false;

    Material mat;
    Transform quad;
    Mesh mesh;
    bool depthRequested;

    // Footstep ripple buffer, filled each frame from the shared FootstepTracker: (worldX, worldZ, age01, size);
    // dead slots have age >= 1. The positions/gait are the tracker's job — we only convert them to OUR lifetime.
    const int MaxSteps = FootstepTracker.MaxSteps;
    readonly Vector4[] steps = new Vector4[MaxSteps];
    FootstepTracker foot;

    static readonly int WetnessID    = Shader.PropertyToID("_Wetness");
    static readonly int DarkenID      = Shader.PropertyToID("_Darken");
    static readonly int PuddleScaleID = Shader.PropertyToID("_PuddleScale");
    static readonly int PuddleAmtID   = Shader.PropertyToID("_PuddleAmount");
    static readonly int RippleSpeedID = Shader.PropertyToID("_RippleSpeed");
    static readonly int RippleStrID   = Shader.PropertyToID("_RippleStrength");
    static readonly int ReflectColID  = Shader.PropertyToID("_ReflectColor");
    static readonly int SpecStrID     = Shader.PropertyToID("_SpecStrength");
    static readonly int RipScaleID    = Shader.PropertyToID("_RipScale");
    static readonly int RipSpeedID    = Shader.PropertyToID("_RipSpeed");
    static readonly int RipAmbientID  = Shader.PropertyToID("_RipAmbient");
    static readonly int StepRadiusID  = Shader.PropertyToID("_StepRadius");
    static readonly int StepBrightID  = Shader.PropertyToID("_StepBright");
    static readonly int StepsID       = Shader.PropertyToID("_Steps");
    static readonly int ReflStrengthID = Shader.PropertyToID("_ReflStrength");
    static readonly int ReflDistortID  = Shader.PropertyToID("_ReflDistort");
    static readonly int ReflFlipYID    = Shader.PropertyToID("_ReflFlipY");

    /// <summary>Most-recent live effect, so consumers (e.g. footstep SFX) can query puddles without a Find.</summary>
    public static WetGround Instance { get; private set; }

    /// <summary>Find the live wet-ground effect, creating one if none exists (used by the DevSettings knob).</summary>
    public static WetGround Ensure()
    {
        var w = Instance != null ? Instance : FindFirstObjectByType<WetGround>();
        if (w == null) w = new GameObject("WetGround").AddComponent<WetGround>();
        return w;
    }

    /// <summary>True while wet ground is actually rendering — High tier, enabled, and live rain making it wet.
    /// Matches the same gate <see cref="LateUpdate"/> uses to show the quad, so audio can follow the visuals.</summary>
    public bool Active => enabled && WeatherFX.Heavy && wetness * RainAmount > 0.001f;

    /// <summary>Is <paramref name="world"/> standing in a visible puddle right now? Re-evaluates the SAME world-XZ
    /// puddle mask the shader draws (<c>fbm(xz*_PuddleScale)</c> thresholded by <c>_PuddleAmount</c>), so the answer
    /// agrees with what the player sees — used to swap in the water/splash footstep SFX. Assumes flat ground (the
    /// shader's flatness term is ~1 where the player can stand). Cheap + zero-GC; call at most per footstep.</summary>
    public bool IsPuddleAt(Vector3 world)
    {
        if (!Active) return false;
        float pn = Fbm(new Vector2(world.x, world.z) * puddleScale);
        float edge = 1f - puddleAmount;
        return Smoothstep(edge, edge + 0.18f, pn) >= 0.5f;   // past the midpoint of the shader's puddle smoothstep
    }

    // --- C# replica of the shader's value-noise puddle field (WetGround.shader: hash21 / vnoise / fbm). Must
    //     stay byte-for-byte with the HLSL so the audible puddle matches the visible one. frac() = x - floor(x).
    static float Frac(float x) => x - Mathf.Floor(x);

    static float Hash21(Vector2 p)
    {
        p = new Vector2(Frac(p.x * 123.34f), Frac(p.y * 456.21f));
        float d = p.x * (p.x + 45.32f) + p.y * (p.y + 45.32f);   // dot(p, p + 45.32)
        p.x += d; p.y += d;
        return Frac(p.x * p.y);
    }

    static float VNoise(Vector2 p)
    {
        Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
        Vector2 f = new Vector2(Frac(p.x), Frac(p.y));
        f = new Vector2(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y));
        float a = Hash21(i);
        float b = Hash21(i + new Vector2(1f, 0f));
        float c = Hash21(i + new Vector2(0f, 1f));
        float d = Hash21(i + new Vector2(1f, 1f));
        return Mathf.Lerp(Mathf.Lerp(a, b, f.x), Mathf.Lerp(c, d, f.x), f.y);
    }

    static float Fbm(Vector2 p)
    {
        float v = 0f, amp = 0.5f;
        for (int o = 0; o < 4; o++) { v += VNoise(p) * amp; p *= 2f; amp *= 0.5f; }
        return v;
    }

    static float Smoothstep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-6f, b - a));
        return t * t * (3f - 2f * t);
    }

    void Awake()
    {
        Instance = this;
        var sh = Shader.Find("Unwritten/WetGround");
        if (sh == null)
        {
            Debug.LogWarning("[WetGround] Shader 'Unwritten/WetGround' not found — wet ground disabled. " +
                             "(In a build, add it to Project Settings ▸ Graphics ▸ Always Included Shaders.)");
            enabled = false;
            return;
        }
        mat = new Material(sh) { name = "WetGround (runtime)" };
        for (int i = 0; i < MaxSteps; i++) steps[i] = new Vector4(0f, 0f, 2f, 0f);   // all slots dead
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

    void BuildQuad()
    {
        var go = new GameObject("WetGroundQuad") { hideFlags = HideFlags.DontSave };
        go.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera
        quad = go.transform;

        mesh = new Mesh { name = "WetGroundFullscreen" };
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

    // Live rain amount (0 if there's no rain overlay), used as the master wetness driver.
    static float RainAmount => RainOverlay.Instance != null ? RainOverlay.Instance.intensity : 0f;

    void LateUpdate()
    {
        if (mat == null) return;

        float amount = wetness * RainAmount;
        bool show = enabled && WeatherFX.Heavy && amount > 0.001f;
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

        mat.SetFloat(WetnessID, amount);
        mat.SetFloat(DarkenID, darken);
        mat.SetFloat(PuddleScaleID, puddleScale);
        mat.SetFloat(PuddleAmtID, puddleAmount);
        mat.SetFloat(RippleSpeedID, rippleSpeed);
        mat.SetFloat(RippleStrID, rippleStrength);
        mat.SetColor(ReflectColID, reflectColor);
        mat.SetFloat(SpecStrID, specStrength);
        mat.SetFloat(RipScaleID, ripScale);
        mat.SetFloat(RipSpeedID, ripSpeed);
        mat.SetFloat(RipAmbientID, ripAmbient);
        mat.SetFloat(StepRadiusID, stepRadius);
        mat.SetFloat(StepBrightID, stepBright);
        mat.SetFloat(ReflStrengthID, reflStrength);
        mat.SetFloat(ReflDistortID, reflDistort);
        mat.SetFloat(ReflFlipYID, reflFlipY ? 1f : 0f);

        // Footstep ripples come from the shared tracker, aged by OUR short ripple lifetime (so a print can have
        // rippled away here while it's still healing in the snow). The shader masks them to puddles anyway.
        if (foot == null) foot = FootstepTracker.Ensure();
        foot.SampleInto(steps, stepLife);
        mat.SetVectorArray(StepsID, steps);
    }
}
