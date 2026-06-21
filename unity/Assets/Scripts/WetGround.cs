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
    public float darken = 0.49f;
    [Range(0f, 1f)] [Tooltip("How much of the ground pools into puddles.")]
    public float puddleAmount = 0.59f;
    [Tooltip("Puddle size — smaller value = larger puddles (it's 1/size in world units).")]
    public float puddleScale = 0.40f;

    [Header("Reflection + ripples")]
    [Tooltip("Tint of the faked puddle reflection (sky colour).")]
    public Color reflectColor = new Color(0.60f, 0.72f, 0.95f, 1f);
    [Range(0f, 4f)] [Tooltip("Brightness of the puddle reflection highlight.")]
    public float specStrength = 1.19f;
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
    public float ripAmbient = 1f;

    [Header("Footstep ripples")]
    [Tooltip("How far the player walks (world units) between footstep ripples (each = one foot).")]
    public float strideDist = 0.45f;
    [Tooltip("Seconds a footstep ripple takes to expand and fade.")]
    public float stepLife = 0.7f;
    [Tooltip("How far a footstep ripple expands (world units). Small = a tight little splash.")]
    public float stepRadius = 0.35f;
    [Range(0f, 8f)] [Tooltip("Brightness of footstep ripples (only show on puddles).")]
    public float stepBright = 1.76f;
    [Tooltip("Sideways offset of each step from the walk line — left/right feet alternate by this.")]
    public float footSpacing = 0.2f;

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

    // Footstep ripple ring-buffer. Each step: (worldX, worldZ, age01, 1); dead slots have age >= 1.
    const int MaxSteps = 16;
    readonly Vector4[] steps = new Vector4[MaxSteps];
    int stepHead;
    Transform player;
    Vector3 lastStepPos;
    bool hasLast;
    bool leftFoot;
    float nextFind;

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

    /// <summary>Find the live wet-ground effect, creating one if none exists (used by the DevSettings knob).</summary>
    public static WetGround Ensure()
    {
        var w = FindFirstObjectByType<WetGround>();
        if (w == null) w = new GameObject("WetGround").AddComponent<WetGround>();
        return w;
    }

    void Awake()
    {
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

        UpdateFootsteps();
        mat.SetVectorArray(StepsID, steps);
    }

    // Age existing footstep ripples and record a new one each time the player walks a stride. Ripples are masked
    // by the puddle wetness in the shader, so a step only shows when the player is actually on a puddle.
    void UpdateFootsteps()
    {
        float dt = Time.deltaTime / Mathf.Max(0.05f, stepLife);
        for (int i = 0; i < MaxSteps; i++)
            if (steps[i].z < 1f) steps[i].z += dt;

        if (player == null && Time.time >= nextFind)
        {
            var pc = FindFirstObjectByType<Player3DController>();
            player = pc != null ? pc.transform : null;
            nextFind = Time.time + 1f;
        }
        if (player == null) return;

        Vector3 p = player.position;
        if (!hasLast) { lastStepPos = p; hasLast = true; return; }

        Vector3 delta = p - lastStepPos;
        if (delta.sqrMagnitude >= strideDist * strideDist)
        {
            // Drop the ripple under the LEFT or RIGHT foot, alternating, offset sideways from the walk line —
            // so the prints zig-zag like real footsteps instead of a single centred trail.
            Vector3 dir = delta; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 perp = new Vector3(dir.z, 0f, -dir.x);                       // 90° in the ground plane
            Vector3 foot = p + perp * (footSpacing * (leftFoot ? -1f : 1f));
            leftFoot = !leftFoot;

            lastStepPos = p;
            steps[stepHead] = new Vector4(foot.x, foot.z, 0f, 1f);              // fresh ripple at the foot, age 0
            stepHead = (stepHead + 1) % MaxSteps;
        }
    }
}
