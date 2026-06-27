using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// HEAVY weather: true <b>planar reflection</b> of the scene onto wet ground. A second "mirror" camera is placed
/// as the reflection of the main camera across the water plane (<see cref="waterHeight"/>), renders the world into
/// a half-res <see cref="RenderTexture"/>, and exposes it as the global <c>_ReflectionTex</c> that
/// <c>Unwritten/WetGround</c> samples (screen-projected, ripple-distorted) inside the puddle mask.
///
/// <para>So everything above the water — houses, trees, props, terrain, AND the billboard player/mobs (Crowd
/// layer) — shows up inverted in the puddles. The sprites work without any per-camera facing: they're upright
/// billboards that share the mirror camera's yaw (the mirror is the main camera flipped below the plane, same
/// XZ), so they present their front face. Uses the canonical reflection-matrix + oblique-near-clip so nothing
/// below the water leaks.</para>
///
/// <para>Cost: one extra scene render. Gated behind <see cref="WeatherFX.Heavy"/> AND live rain, rendered at
/// <see cref="resScale"/> (half-res by default) with a tight cull mask — so it's free on Low/Medium or when dry.
/// The mirror camera auto-renders to its target texture (no manual Render call); its view/projection are rewritten
/// each frame in <see cref="OnBeginCamera"/>, and culling is inverted around its pass (the mirror flips winding).</para>
/// </summary>
[DisallowMultipleComponent]
public class PlanarReflection : MonoBehaviour
{
    [Tooltip("World Y of the water surface the scene is mirrored about (puddles sit ~ground level). When " +
        "'followGround' is on this is overwritten each frame with the real ground height under the player.")]
    public float waterHeight = 0f;
    [Tooltip("Auto-snap waterHeight to the real ground under the player each frame (raycasts the Ground layer), " +
        "so puddle reflections line up at the true floor on every map without hand-tuning. Off = set waterHeight manually.")]
    public bool followGround = true;
    [Tooltip("Fine offset added to the auto-detected ground height (use to nudge the reflection seam up/down).")]
    public float groundOffset = 0f;
    [Tooltip("Layers the reflection renders. Default = the 3D world; sprites (Crowd), UI, FX quads, Water are excluded.")]
    public LayerMask reflectMask = -1;
    [Range(0.25f, 1f)] [Tooltip("Reflection texture resolution scale (0.5 = half-res, cheaper).")]
    public float resScale = 0.5f;
    [Tooltip("Pushes the clip plane slightly below the water so the shoreline doesn't shimmer.")]
    public float clipPlaneOffset = 0.03f;
    [Tooltip("Render shadows into the reflection (costlier). Off reads fine in small puddles.")]
    public bool reflectionShadows = false;

    public static PlanarReflection Instance { get; private set; }

    Camera reflCam;
    UniversalAdditionalCameraData reflData;
    RenderTexture rt;
    int rtW, rtH;
    bool running;
    Transform focus;        // what we raycast under for the ground height (player, else camera)
    int groundMask = -1;    // cached Ground layer mask (-1 = not resolved yet)

    static readonly int ReflectionTexID = Shader.PropertyToID("_ReflectionTex");

    /// <summary>Default cull mask: the solid 3D world PLUS the Crowd sprites (player + mobs — Step 2). They're
    /// upright billboards that share the mirror camera's yaw, so they show their front face in the reflection.
    /// Excludes UI, the camera-locked FX quads (TransparentFX), the Water surface itself, and Ignore Raycast.</summary>
    static int DefaultReflectMask()
    {
        int exclude = 0;
        exclude |= 1 << LayerMask.NameToLayer("UI");
        exclude |= 1 << LayerMask.NameToLayer("TransparentFX");
        exclude |= 1 << LayerMask.NameToLayer("Water");
        exclude |= 1 << LayerMask.NameToLayer("Ignore Raycast");
        return ~exclude;
    }

    public static PlanarReflection Ensure()
    {
        var p = FindFirstObjectByType<PlanarReflection>();
        if (p == null) p = new GameObject("PlanarReflection").AddComponent<PlanarReflection>();
        return p;
    }

    void Awake()
    {
        if (reflectMask == -1) reflectMask = DefaultReflectMask();
    }

    void OnEnable()
    {
        Instance = this;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering   += OnEndCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering   -= OnEndCamera;
        if (Instance == this) Instance = null;
        Shader.SetGlobalTexture(ReflectionTexID, Texture2D.blackTexture);
        if (reflCam != null) { Destroy(reflCam.gameObject); reflCam = null; }
        if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
        running = false;
    }

    bool ShouldRun()
        => enabled && WeatherFX.Heavy
        // Follow the VISIBLE puddles (which linger after the rain), not the rain itself, so reflections fade out
        // with the puddles instead of snapping off the moment the rain stops.
        && WetGround.Instance != null && WetGround.Instance.Active;

    void Update()
    {
        // Decide per-frame whether the mirror pass runs (this runs BEFORE rendering, so the toggle is same-frame).
        var main = CameraRig.Cam;
        running = main != null && ShouldRun();

        if (!running)
        {
            if (reflCam != null) reflCam.gameObject.SetActive(false);
            Shader.SetGlobalTexture(ReflectionTexID, Texture2D.blackTexture);   // puddles get no reflection
            return;
        }

        if (followGround) TrackGround();   // snap the mirror plane to the real ground before MirrorTo uses it

        EnsureResources(main);
        reflCam.gameObject.SetActive(true);
        Shader.SetGlobalTexture(ReflectionTexID, rt);
    }

    // Raycast straight down onto the Ground layer under the player (or camera) and park the mirror plane on it,
    // so the puddle reflection always meets objects at the true floor — even if a map's ground isn't exactly Y0.
    void TrackGround()
    {
        if (groundMask == -1)
        {
            int l = LayerMask.NameToLayer("Ground");
            groundMask = l >= 0 ? (1 << l) : 0;
        }
        if (groundMask == 0) return;   // no Ground layer in this project — leave waterHeight as authored

        if (focus == null)
        {
            var p = FindFirstObjectByType<Player3DController>();
            focus = p != null ? p.transform : (CameraRig.Cam != null ? CameraRig.Cam.transform : null);
        }
        if (focus == null) return;

        Vector3 origin = focus.position + Vector3.up * 100f;
        if (Physics.Raycast(origin, Vector3.down, out var hit, 500f, groundMask, QueryTriggerInteraction.Ignore))
            waterHeight = hit.point.y + groundOffset;
    }

    void EnsureResources(Camera main)
    {
        int w = Mathf.Max(2, Mathf.RoundToInt(Screen.width  * resScale));
        int h = Mathf.Max(2, Mathf.RoundToInt(Screen.height * resScale));
        if (rt == null || w != rtW || h != rtH)
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 16, RenderTextureFormat.Default) { name = "PlanarReflectionRT" };
            rt.Create();
            rtW = w; rtH = h;
            if (reflCam != null) reflCam.targetTexture = rt;
        }

        if (reflCam == null)
        {
            var go = new GameObject("PlanarReflectionCam") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            reflCam = go.AddComponent<Camera>();
            reflCam.enabled = true;                 // auto-renders to its target texture each frame
            reflCam.targetTexture = rt;
            reflCam.depth = main.depth - 10;        // render before the main camera
            reflCam.allowMSAA = false;
            reflCam.allowHDR = main.allowHDR;

            reflData = reflCam.GetUniversalAdditionalCameraData();
            reflData.renderType = CameraRenderType.Base;
            reflData.renderPostProcessing = false;
            reflData.requiresDepthTexture = false;
            reflData.requiresColorTexture = false;
            reflData.renderShadows = reflectionShadows;
        }
    }

    // ── per-camera hooks ─────────────────────────────────────────────────────────────────────────────────────
    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (!running || reflCam == null || cam != reflCam) return;

        var main = CameraRig.Cam;
        if (main == null) return;

        MirrorTo(main);
        reflData.renderShadows = reflectionShadows;
        GL.invertCulling = true;        // the mirror flips triangle winding; flip back so faces aren't culled
    }

    void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (reflCam != null && cam == reflCam) GL.invertCulling = false;
    }

    // ── reflection math (canonical planar reflection) ────────────────────────────────────────────────────────
    void MirrorTo(Camera main)
    {
        reflCam.fieldOfView   = main.fieldOfView;
        reflCam.nearClipPlane = main.nearClipPlane;
        reflCam.farClipPlane  = main.farClipPlane;
        reflCam.aspect        = main.aspect;
        reflCam.cullingMask   = reflectMask;
        // Clear with the SKYBOX so the wrapped sky (gradient + sun + drifting clouds) shows up in the puddles —
        // the mirror cam looks UP from below the water, so it samples the sky in the reflected directions.
        reflCam.clearFlags    = CameraClearFlags.Skybox;
        reflCam.backgroundColor = main.backgroundColor;   // fallback only (used if no skybox material is set)

        Vector3 planePos = new Vector3(0f, waterHeight, 0f);
        Vector3 normal   = Vector3.up;

        // Reflection matrix about the plane y = waterHeight.
        Vector4 plane = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, planePos));
        Matrix4x4 reflection = CalculateReflectionMatrix(plane);

        // Mirror the transform too (for sensible frustum culling); the matrices below do the exact render.
        Vector3 mPos = reflection.MultiplyPoint(main.transform.position);
        reflCam.transform.position = mPos;
        Vector3 e = main.transform.eulerAngles;
        reflCam.transform.eulerAngles = new Vector3(-e.x, e.y, e.z);

        reflCam.worldToCameraMatrix = main.worldToCameraMatrix * reflection;

        // Oblique near-clip at the water plane so nothing below the surface is reflected.
        Vector4 clip = CameraSpacePlane(reflCam.worldToCameraMatrix, planePos, normal, 1f);
        reflCam.projectionMatrix = main.CalculateObliqueMatrix(clip);
    }

    static Matrix4x4 CalculateReflectionMatrix(Vector4 p)
    {
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y;     m.m02 = -2f * p.x * p.z;     m.m03 = -2f * p.w * p.x;
        m.m10 = -2f * p.y * p.x;     m.m11 = 1f - 2f * p.y * p.y; m.m12 = -2f * p.y * p.z;     m.m13 = -2f * p.w * p.y;
        m.m20 = -2f * p.z * p.x;     m.m21 = -2f * p.z * p.y;     m.m22 = 1f - 2f * p.z * p.z; m.m23 = -2f * p.w * p.z;
        m.m30 = 0f;                  m.m31 = 0f;                  m.m32 = 0f;                  m.m33 = 1f;
        return m;
    }

    Vector4 CameraSpacePlane(Matrix4x4 worldToCam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos - normal * clipPlaneOffset;
        Vector3 cPos = worldToCam.MultiplyPoint(offsetPos);
        Vector3 cNormal = worldToCam.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cNormal.x, cNormal.y, cNormal.z, -Vector3.Dot(cPos, cNormal));
    }
}
