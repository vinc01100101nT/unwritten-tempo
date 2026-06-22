using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// HEAVY weather: drives the <c>Unwritten/WetGround</c> screen-space pass — darkens wet surfaces and adds glossy
/// rippling puddle reflections. Built on the exact full-screen-depth-quad pattern as <see cref="CloudShadows"/>
/// (camera-locked quad, requests the camera depth texture, world-XZ sampling), so it projects onto whatever the
/// depth buffer holds. Gated behind <see cref="WeatherFX.Heavy"/> (High tier only) so it never costs anything
/// on Low/Medium.
///
/// <para>The puddle COVERAGE is its own weather CHANNEL (<see cref="WeatherChannel.Puddle"/>), fully decoupled from
/// rain: this pass just renders <c>Puddle.shown × </c><see cref="puddleAmount"/> (the per-map coverage cap) as the wet
/// AREA — it is NOT an opacity fade. A weather <see cref="WeatherSequence"/> eases that channel (puddles can begin
/// after, and linger past, the rain), or the channels hub's auto-puddle driver follows rain with a soak/linger delay.
/// The rain-impact rings + open-ground sheen instead follow the LIVE Rain channel (<c>_RainNow</c>), so raindrops stop
/// landing the moment the rain does, while the standing-water puddles fade out on their own clock. (When there's no
/// <see cref="WeatherChannels"/> hub at all, this falls back to the old self-driven timing off the rain overlay, tuned
/// by <see cref="wetDelayIn"/>/<see cref="wetRampIn"/>/<see cref="wetDelayOut"/>/<see cref="wetRampOut"/>.)</para>
/// </summary>
[DisallowMultipleComponent]
public class WetGround : MonoBehaviour
{
    [Header("Wet look")]
    [Range(0f, 1f)] [Tooltip("Puddle strength master — darkening/shine OPACITY at full. Held CONSTANT; the puddle " +
             "transition is its area/coverage growing (puddleAmount), NOT this fading in/out. Leave at 1 normally.")]
    public float wetness = 1f;
    [Range(0f, 1f)] [Tooltip("How much wet surfaces darken.")]
    public float darken = 0.66f;
    [Range(0f, 1f)] [Tooltip("Per-map MAX puddle COVERAGE — how much of the ground pools into puddles once fully " +
             "soaked (default 0.75). After rain starts the coverage GROWS 0→this (the puddles spread, it's NOT an " +
             "opacity fade); after rain stops it shrinks back to 0. Decoupled from the live rain so puddles linger.")]
    public float puddleAmount = 0.75f;
    [Tooltip("Puddle size — smaller value = larger puddles (it's 1/size in world units).")]
    public float puddleScale = 0.40f;

    [Header("Wet timing — the after-rain feel")]
    [Range(0f, 30f)] [Tooltip("Seconds AFTER rain starts before puddles begin forming (the ground takes a moment to " +
             "soak). Timed from rain 0→onwards.")]
    public float wetDelayIn = 5f;
    [Range(0.1f, 60f)] [Tooltip("Seconds for puddle COVERAGE to spread from none to full (puddleAmount) once it starts.")]
    public float wetRampIn = 10f;
    [Range(0f, 60f)] [Tooltip("Seconds AFTER rain stops before puddles begin drying — they linger first (after-rain feel).")]
    public float wetDelayOut = 10f;
    [Range(0.1f, 60f)] [Tooltip("Seconds for puddle COVERAGE to shrink back to none once it starts receding.")]
    public float wetRampOut = 10f;

    [Header("Reflection + ripples")]
    [Tooltip("Tint of the faked puddle reflection (sky colour).")]
    public Color reflectColor = new Color(0.60f, 0.72f, 0.95f, 1f);
    [Range(0f, 4f)] [Tooltip("Brightness of the puddle reflection highlight.")]
    public float specStrength = 0f;
    [Tooltip("Ripple animation speed.")]
    public float rippleSpeed = 4f;
    [Range(0f, 1f)] [Tooltip("Ripple distortion strength.")]
    public float rippleStrength = 0.35f;

    [Header("Rain impact rings")]
    [Tooltip("Density of the rain-impact grid (higher = smaller, more frequent rings). Default = small, snappy " +
             "raindrop impacts.")]
    public float ripScale = 5.4f;
    [Tooltip("Impact-ring animation speed — higher = each ring blooms + fades faster (shorter life, faster rate).")]
    public float ripSpeed = 22f;
    // The SAME rain-impact ring pattern (rainRipple) is drawn on every flat wet pixel; these two dials are just its
    // brightness on the two surfaces it can land on. Independent — turning one down never affects the other.
    [Range(0f, 4f)] [Tooltip("Brightness of the rain-impact rings ON PUDDLES (standing water). The watery splashes.")]
    public float waterImpact = 0.44f;
    [Range(0f, 4f)] [Tooltip("Brightness of the SAME rain-impact rings on flat ground OFF puddles (0 = puddles only; " +
        "higher = rain visibly lands across the whole wet ground). Independent of Water impact.")]
    public float groundImpact = 0.15f;

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

    float puddleLevel; // current puddle COVERAGE (0..puddleAmount) fed to _PuddleAmount — self-driven, persists after rain
    float delayTimer;  // counts down the soak-in / dry-out delay before puddleLevel starts spreading/receding
    bool  wasRaining;  // last-frame rain-on state, to catch the start/stop edges
    bool  primed;      // first-frame: if we load into rain, start fully puddled instead of spreading from dry

    // Footstep ripple buffer, filled each frame from the shared FootstepTracker: (worldX, worldZ, age01, size);
    // dead slots have age >= 1. The positions/gait are the tracker's job — we only convert them to OUR lifetime.
    const int MaxSteps = FootstepTracker.MaxSteps;
    readonly Vector4[] steps = new Vector4[MaxSteps];
    FootstepTracker foot;

    static readonly int WetnessID    = Shader.PropertyToID("_Wetness");
    static readonly int RainNowID     = Shader.PropertyToID("_RainNow");
    static readonly int DarkenID      = Shader.PropertyToID("_Darken");
    static readonly int PuddleScaleID = Shader.PropertyToID("_PuddleScale");
    static readonly int PuddleAmtID   = Shader.PropertyToID("_PuddleAmount");
    static readonly int RippleSpeedID = Shader.PropertyToID("_RippleSpeed");
    static readonly int RippleStrID   = Shader.PropertyToID("_RippleStrength");
    static readonly int ReflectColID  = Shader.PropertyToID("_ReflectColor");
    static readonly int SpecStrID     = Shader.PropertyToID("_SpecStrength");
    static readonly int RipScaleID    = Shader.PropertyToID("_RipScale");
    static readonly int RipSpeedID    = Shader.PropertyToID("_RipSpeed");
    static readonly int WaterImpactID = Shader.PropertyToID("_WaterImpact");
    static readonly int GroundImpactID = Shader.PropertyToID("_GroundImpact");
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

    /// <summary>True while there's visible standing water — High tier, enabled, and the puddle coverage still
    /// spread enough to show. Tracks the self-driven <see cref="puddleLevel"/> (which lingers after the rain stops),
    /// so audio and the planar reflection follow the visible puddles, not the rain.</summary>
    public bool Active => enabled && WeatherFX.Heavy && puddleLevel > 0.001f;

    /// <summary>Is <paramref name="world"/> standing in a visible puddle right now? Re-evaluates the SAME world-XZ
    /// puddle mask the shader draws (<c>fbm(xz*_PuddleScale)</c> thresholded by <c>_PuddleAmount</c>), so the answer
    /// agrees with what the player sees — used to swap in the water/splash footstep SFX. Assumes flat ground (the
    /// shader's flatness term is ~1 where the player can stand). Cheap + zero-GC; call at most per footstep.</summary>
    public bool IsPuddleAt(Vector3 world)
    {
        if (!Active) return false;
        float pn = Fbm(new Vector2(world.x, world.z) * puddleScale);
        float edge = 1f - puddleLevel;   // match the LIVE coverage the shader is drawing, not the per-map max
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

    // Is rain currently COMMANDED on? Triggers the soak/dry timers off the rain TARGET (not the eased amount), so
    // the ground starts soaking the instant rain begins ("from 0 onwards") and starts its dry-out clock the instant
    // it's switched off — independent of how wet the ground currently looks.
    static bool Raining => RainOverlay.Instance != null && RainOverlay.Instance.intensity > 0.001f;

    void LateUpdate()
    {
        if (mat == null) return;

        // --- Puddle COVERAGE + live rain, read from the weather CHANNELS (the source of truth). ---
        // Puddles are their own channel now (decoupled from rain): a sequence — or the auto-puddle driver — eases
        // the normalized Puddle channel, and we scale it by this map's coverage CAP. The rain-impact rings + open
        // ground sheen follow the live Rain channel, so they stop when the rain does even while puddles linger.
        var wc = WeatherChannels.Instance;
        float rainNow;
        if (wc != null)
        {
            puddleLevel = wc.Shown(WeatherChannel.Puddle) * Mathf.Max(0f, puddleAmount);
            rainNow     = wc.Shown(WeatherChannel.Rain);
        }
        else
        {
            // Standalone fallback (no channels hub): the old self-driven puddle timing off the rain overlay.
            bool raining = Raining;
            if (!primed) { primed = true; wasRaining = raining; puddleLevel = raining ? puddleAmount : 0f; delayTimer = 0f; }
            if (raining != wasRaining) { wasRaining = raining; delayTimer = raining ? wetDelayIn : wetDelayOut; }

            float target;
            if (delayTimer > 0f) { delayTimer -= Time.deltaTime; target = puddleLevel; }
            else                 { target = raining ? puddleAmount : 0f; }

            float ramp  = raining ? wetRampIn : wetRampOut;
            float speed = Mathf.Max(0f, puddleAmount) / Mathf.Max(0.1f, ramp);
            puddleLevel = Mathf.MoveTowards(puddleLevel, target, speed * Time.deltaTime);
            rainNow     = RainOverlay.Instance != null ? RainOverlay.Instance.Shown : 0f;
        }

        bool show = enabled && WeatherFX.Heavy && (puddleLevel > 0.001f || rainNow > 0.001f);
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

        mat.SetFloat(WetnessID, wetness);            // constant puddle strength (opacity); coverage does the easing
        mat.SetFloat(RainNowID, rainNow);            // live rain → drives the impact rings + open-ground wetness
        mat.SetFloat(DarkenID, darken);
        mat.SetFloat(PuddleScaleID, puddleScale);
        mat.SetFloat(PuddleAmtID, puddleLevel);      // the LIVE coverage (0..puddleAmount), the visible transition
        mat.SetFloat(RippleSpeedID, rippleSpeed);
        mat.SetFloat(RippleStrID, rippleStrength);
        mat.SetColor(ReflectColID, reflectColor);
        mat.SetFloat(SpecStrID, specStrength);
        mat.SetFloat(RipScaleID, ripScale);
        mat.SetFloat(RipSpeedID, ripSpeed);
        mat.SetFloat(WaterImpactID, waterImpact);
        mat.SetFloat(GroundImpactID, groundImpact);
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
