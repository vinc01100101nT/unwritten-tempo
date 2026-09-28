using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// HEAVY weather: true <b>planar reflection</b> of the scene in standing water. Still water is flat, and a flat water
/// surface reflects exactly like a mirror laid at its height, so every water height of the map
/// (<see cref="WaterSurfaces"/>) gets a mirror of its own: a camera placed as the reflection of the main camera across
/// that height renders the world, at the main camera's full pixel size, into its own texture, exposed as the global
/// <c>_ReflectionTex0..7</c> with its height in <c>_MirrorHeights</c>. <c>Unwritten/WetGround</c> samples, at each
/// water pixel, the texture of the mirror at that pixel's own height (screen-projected, ripple-distorted) — so a tree
/// on a cliff top meets its reflection in the cliff-top puddle, and the low ground's puddles show the cliff.
///
/// <para>Only water near the middle of the view reflects the world: the water heights with ground on screen within
/// <see cref="SkyOnlyFrom"/> of it get a mirror each, nearest first — one per distinct height, shared by every body of
/// water standing at it — at most <see cref="MaxMirrors"/>. Past <see cref="FullReflectionTo"/> the reflection fades to
/// the sky alone, drawn by one extra mirror that renders only the sky (the same at every height, so it can never show a
/// wrong reflection); a water height with no mirror of its own reflects that sky. No water is ever drawn dry.</para>
///
/// <para>So everything above the water — houses, trees, props, terrain, AND the billboard player/mobs (Crowd
/// layer) — shows up inverted in the puddles. The sprites work without any per-camera facing: they're upright
/// billboards that share the mirror camera's yaw (the mirror is the main camera flipped below the plane, same
/// XZ), so they present their front face. Uses the canonical reflection-matrix + an oblique near-clip laid exactly on
/// the water surface: nothing beneath the water is ever reflected, not even a sliver of it.</para>
///
/// <para>Cost: one extra scene render per mirrored water height near the middle of the view, plus the sky mirror (the
/// sky alone, at half the screen's pixels). Gated behind <see cref="WeatherFX.Heavy"/> AND
/// visible puddles, with a tight cull mask — so it's free on Low/Medium or when dry. Each mirror camera
/// auto-renders to its target texture (no manual Render call); its view/projection are rewritten each frame in
/// <see cref="OnBeginCamera"/>, and culling is inverted around its pass (the mirror flips winding).</para>
/// </summary>
// After every ordinary LateUpdate: the camera has moved for this frame (CameraFollow3D) and the water has been refilled
// (WetGround), so the mirrors are handed out for the view and the water heights this frame actually draws. Handed out
// earlier, a refill in the same frame left every mirror at the old heights, and the water blinked dry for that frame.
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class PlanarReflection : MonoBehaviour
{
    [Tooltip("Layers the reflection renders. Default = the 3D world; sprites (Crowd), UI, FX quads, Water are excluded.")]
    public LayerMask reflectMask = -1;
    [Tooltip("Render shadows into the reflection (costlier). Off reads fine in small puddles.")]
    public bool reflectionShadows = false;

    /// <summary>The most mirrors drawn in one frame — one per water height near the middle of the view.</summary>
    public const int MaxMirrors = 8;
    /// <summary>World units, across the ground, from the middle of the view within which water reflects the world fully.</summary>
    public const float FullReflectionTo = 6f;
    /// <summary>World units from the middle of the view past which water reflects only the sky, and gets no mirror.</summary>
    public const float SkyOnlyFrom = 9f;

    public static PlanarReflection Instance { get; private set; }

    sealed class Mirror
    {
        public Camera cam;
        public UniversalAdditionalCameraData data;
        public RenderTexture rt;
        public float height;
    }

    readonly Mirror[] mirrors = new Mirror[MaxMirrors];
    // Renders only the sky, reflected: what water far from the middle of the view, or with no mirror left, reflects.
    Mirror sky;
    readonly float[] nearSqr = new float[WaterSurfaces.MaxHeights];
    Vector4 shownNear = new Vector4(float.NaN, 0f, 0f, 0f);
    int groundLayer = -2;
    Texture shownSky;
    bool puddlesShow;
    readonly float[] mirrorHeights = new float[MaxMirrors];
    readonly float[] shownHeights = new float[MaxMirrors];
    readonly Texture[] shownTex = new Texture[MaxMirrors];
    readonly Plane[] frustum = new Plane[6];
    readonly List<int> onScreen = new List<int>(WaterSurfaces.MaxHeights);
    int mirrorCount;
    int shownCount = -1;
    bool running;
    bool shownRunning;
    int shownUnmirrored;

    static readonly int[] ReflectionTexIDs =
    {
        Shader.PropertyToID("_ReflectionTex0"), Shader.PropertyToID("_ReflectionTex1"),
        Shader.PropertyToID("_ReflectionTex2"), Shader.PropertyToID("_ReflectionTex3"),
        Shader.PropertyToID("_ReflectionTex4"), Shader.PropertyToID("_ReflectionTex5"),
        Shader.PropertyToID("_ReflectionTex6"), Shader.PropertyToID("_ReflectionTex7"),
    };
    static readonly int MirrorHeightsID = Shader.PropertyToID("_MirrorHeights");
    static readonly int MirrorCountID   = Shader.PropertyToID("_MirrorCount");
    static readonly int MirrorsOnID     = Shader.PropertyToID("_MirrorsOn");
    static readonly int SkyTexID        = Shader.PropertyToID("_ReflectionSky");
    static readonly int ReflectNearID   = Shader.PropertyToID("_ReflectNear");

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
        var p = FindAnyObjectByType<PlanarReflection>();
        if (p == null) p = new GameObject("PlanarReflection") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<PlanarReflection>();
        return p;
    }

    void Awake()
    {
        // Nothing is drawn on a server, so there is no camera to hook and no reflection to render. Standing
        // down in Awake means OnEnable never subscribes, rather than subscribing and then failing per frame.
        // The same for a client started with no window: it has no screen, and a reflection of nothing fails
        // every frame into its log.
        if (GameServer.IsServer || Application.isBatchMode) { enabled = false; return; }

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
        for (int i = 0; i < MaxMirrors; i++)
        {
            var m = mirrors[i];
            if (m == null) continue;
            if (m.cam != null) { m.cam.targetTexture = null; Destroy(m.cam.gameObject); }
            if (m.rt != null) { m.rt.Release(); Destroy(m.rt); }
            mirrors[i] = null;
        }
        if (sky != null)
        {
            if (sky.cam != null) { sky.cam.targetTexture = null; Destroy(sky.cam.gameObject); }
            if (sky.rt != null) { sky.rt.Release(); Destroy(sky.rt); }
            sky = null;
        }
        mirrorCount = 0;
        running = false;
        Publish();
    }

    bool ShouldRun()
        => enabled && WeatherFX.Heavy
        // Follow the VISIBLE puddles (which linger after the rain), not the rain itself, so reflections fade out
        // with the puddles instead of snapping off the moment the rain stops.
        && WetGround.Instance != null && WetGround.Instance.Active;

    /// <summary>Is a mirror drawing the water height <paramref name="height"/> this frame? A water height without one
    /// reflects only the sky (see the class summary).</summary>
    public bool Mirrors(float height)
    {
        for (int i = 0; i < mirrorCount; i++)
            if (mirrorHeights[i] == height) return true;
        return false;
    }

    /// <summary>True while mirrors are being drawn this frame.</summary>
    public bool Running => running;

    void LateUpdate()
    {
        // Decide per-frame which mirrors run (this runs BEFORE rendering, so the toggle is same-frame).
        var main = CameraRig.Cam;
        running = main != null && ShouldRun();
        mirrorCount = 0;

        if (running)
        {
            WaterSurfaces.Refresh();
            GeometryUtility.CalculateFrustumPlanes(main, frustum);
            WaterSurfaces.OnScreen(frustum, onScreen);
            Vector2 centre = ViewCentre(main);
            SortNearestFirst(centre);
            int w = Mathf.Max(2, main.pixelWidth), h = Mathf.Max(2, main.pixelHeight);
            EnsureSky(main, w, h);
            if (!sky.cam.gameObject.activeSelf) sky.cam.gameObject.SetActive(true);
            PublishNear(new Vector4(centre.x, centre.y, FullReflectionTo, SkyOnlyFrom));
            puddlesShow = WetGround.Instance != null && WetGround.Instance.PuddlesShow;
            for (int k = 0; k < onScreen.Count && mirrorCount < MaxMirrors; k++)
            {
                // A sheet holds water only where its puddles show; with none showing it needs no mirror.
                if (!Holds(onScreen[k])) continue;
                // Nearest first, so once one lies past SkyOnlyFrom every one after it does too.
                if (nearSqr[onScreen[k]] >= SkyOnlyFrom * SkyOnlyFrom) break;
                // Water at the same height is one mirror, however many separate bodies stand at it (every lake on the
                // same low level rises to the same height): a second mirror would draw the same picture again.
                float height = WaterSurfaces.Heights[onScreen[k]];
                if (Mirrors(height)) continue;
                var m = EnsureMirror(mirrorCount, main, w, h);
                m.height = height;
                mirrorHeights[mirrorCount] = m.height;
                if (!m.cam.gameObject.activeSelf) m.cam.gameObject.SetActive(true);
                mirrorCount++;
            }
        }

        for (int i = mirrorCount; i < MaxMirrors; i++)
            if (mirrors[i] != null && mirrors[i].cam.gameObject.activeSelf) mirrors[i].cam.gameObject.SetActive(false);
        if (!running && sky != null && sky.cam.gameObject.activeSelf) sky.cam.gameObject.SetActive(false);

        // Said out loud when it changes: water near the middle of the view that got no mirror reflects only the sky.
        int unmirrored = 0;
        if (running)
            for (int k = 0; k < onScreen.Count; k++)
                if (Holds(onScreen[k]) && nearSqr[onScreen[k]] < SkyOnlyFrom * SkyOnlyFrom && !Mirrors(WaterSurfaces.Heights[onScreen[k]])) unmirrored++;
        if (unmirrored != shownUnmirrored)
        {
            shownUnmirrored = unmirrored;
            if (unmirrored > 0)
                GameLog.Info(LogCat.Unwritten, "PlanarReflection: " + unmirrored + " body(ies) of water near the middle of the view got no mirror (" +
                             mirrorCount + " mirrors in use) — they reflect only the sky.");
        }

        Publish();
    }

    // Hand the shader which mirrors run and at what height — written only when that changed.
    void Publish()
    {
        Texture skyTex = running && sky != null ? sky.rt : Texture2D.blackTexture;
        bool changed = shownRunning != running || shownCount != mirrorCount || shownSky != skyTex;
        for (int i = 0; i < mirrorCount && !changed; i++)
            changed = shownHeights[i] != mirrorHeights[i] || shownTex[i] != mirrors[i].rt;
        if (!changed) return;

        for (int i = 0; i < MaxMirrors; i++)
        {
            Texture tex = i < mirrorCount ? mirrors[i].rt : Texture2D.blackTexture;
            shownHeights[i] = i < mirrorCount ? mirrorHeights[i] : 0f;
            shownTex[i] = tex;
            Shader.SetGlobalTexture(ReflectionTexIDs[i], tex);
        }
        Shader.SetGlobalFloatArray(MirrorHeightsID, shownHeights);
        Shader.SetGlobalTexture(SkyTexID, skyTex);
        shownSky = skyTex;
        Shader.SetGlobalFloat(MirrorCountID, mirrorCount);
        Shader.SetGlobalFloat(MirrorsOnID, running ? 1f : 0f);
        shownCount = mirrorCount;
        shownRunning = running;
    }

    bool Holds(int body) => puddlesShow || body >= WaterSurfaces.FilmCount;

    // The middle of the view: where the camera's centre line meets the ground, or right under the camera if it meets none.
    Vector2 ViewCentre(Camera main)
    {
        if (groundLayer == -2) groundLayer = LayerMask.NameToLayer("Ground");
        var t = main.transform;
        if (groundLayer >= 0 && Physics.Raycast(t.position, t.forward, out var hit, main.farClipPlane, 1 << groundLayer,
                                                QueryTriggerInteraction.Ignore))
            return new Vector2(hit.point.x, hit.point.z);
        return new Vector2(t.position.x, t.position.z);
    }

    // Order the water on screen nearest the middle of the view first (a handful of bodies: an insertion sort, no garbage).
    void SortNearestFirst(Vector2 centre)
    {
        for (int k = 0; k < onScreen.Count; k++)
            nearSqr[onScreen[k]] = WaterSurfaces.NearestSqrXZ(onScreen[k], centre.x, centre.y);
        for (int k = 1; k < onScreen.Count; k++)
        {
            int body = onScreen[k];
            int j = k - 1;
            while (j >= 0 && nearSqr[onScreen[j]] > nearSqr[body]) { onScreen[j + 1] = onScreen[j]; j--; }
            onScreen[j + 1] = body;
        }
    }

    // Hand the shader the middle of the view — written only when it moved.
    void PublishNear(Vector4 near)
    {
        if (near == shownNear) return;
        shownNear = near;
        Shader.SetGlobalVector(ReflectNearID, near);
    }

    // The sky mirror renders nothing but the sky, which is soft, so half the screen's pixels are plenty.
    void EnsureSky(Camera main, int w, int h)
    {
        if (sky == null) sky = NewMirror("PlanarReflectionSky", main);
        int sw = Mathf.Max(2, w / 2), sh = Mathf.Max(2, h / 2);
        if (sky.rt == null || sky.rt.width != sw || sky.rt.height != sh) SizeTexture(sky, sw, sh, "PlanarReflectionSkyRT");
    }

    Mirror NewMirror(string name, Camera main)
    {
        var m = new Mirror();
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        m.cam = go.AddComponent<Camera>();
        // auto-renders to its target texture each frame
        m.cam.enabled = true;
        // render before the main camera
        m.cam.depth = main.depth - 10;
        m.cam.allowMSAA = false;
        m.cam.allowHDR = main.allowHDR;

        m.data = m.cam.GetUniversalAdditionalCameraData();
        m.data.renderType = CameraRenderType.Base;
        m.data.renderPostProcessing = false;
        m.data.requiresDepthTexture = false;
        m.data.requiresColorTexture = false;
        m.data.renderShadows = reflectionShadows;
        return m;
    }

    void SizeTexture(Mirror m, int w, int h, string name)
    {
        // Unhook the old texture from the camera first: releasing one a camera still targets is an error.
        m.cam.targetTexture = null;
        if (m.rt != null) { m.rt.Release(); Destroy(m.rt); }
        // A 24-bit depth buffer: a 16-bit one cannot tell apart surfaces millimetres apart at this camera distance
        // (a cliff top's grass lies 4 mm over the ground), and would flicker between them in the reflection.
        m.rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default) { name = name };
        m.rt.Create();
        m.cam.targetTexture = m.rt;
    }

    Mirror EnsureMirror(int slot, Camera main, int w, int h)
    {
        var m = mirrors[slot];
        if (m == null)
        {
            m = NewMirror("PlanarReflectionCam" + slot, main);
            mirrors[slot] = m;
        }

        // The main camera's own pixel size, so each reflected pixel lands on exactly one screen pixel.
        if (m.rt == null || m.rt.width != w || m.rt.height != h) SizeTexture(m, w, h, "PlanarReflectionRT" + slot);
        return m;
    }

    // ── per-camera hooks ─────────────────────────────────────────────────────────────────────────────────────
    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (!running) return;
        Mirror m = null;
        for (int i = 0; i < mirrorCount; i++)
            if (mirrors[i].cam == cam) { m = mirrors[i]; break; }
        bool skyOnly = sky != null && sky.cam == cam;
        if (skyOnly) m = sky;
        if (m == null) return;

        var main = CameraRig.Cam;
        if (main == null) return;

        MirrorTo(main, m);
        // The sky is infinitely far, so its reflection is the same at every water height: render it and nothing else.
        if (skyOnly) m.cam.cullingMask = 0;
        m.data.renderShadows = reflectionShadows;
        // the mirror flips triangle winding; flip back so faces aren't culled
        GL.invertCulling = true;
    }

    void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        for (int i = 0; i < MaxMirrors; i++)
            if (mirrors[i] != null && mirrors[i].cam == cam) { GL.invertCulling = false; return; }
        if (sky != null && sky.cam == cam) GL.invertCulling = false;
    }

    // ── reflection math (canonical planar reflection) ────────────────────────────────────────────────────────
    void MirrorTo(Camera main, Mirror m)
    {
        var reflCam = m.cam;
        reflCam.fieldOfView   = main.fieldOfView;
        reflCam.nearClipPlane = main.nearClipPlane;
        reflCam.farClipPlane  = main.farClipPlane;
        reflCam.aspect        = main.aspect;
        reflCam.cullingMask   = reflectMask;
        // Clear with the SKYBOX so the wrapped sky (gradient + sun + drifting clouds) shows up in the puddles —
        // the mirror cam looks UP from below the water, so it samples the sky in the reflected directions.
        reflCam.clearFlags    = CameraClearFlags.Skybox;
        // fallback only (used if no skybox material is set)
        reflCam.backgroundColor = main.backgroundColor;

        Vector3 planePos = new Vector3(0f, m.height, 0f);
        Vector3 normal   = Vector3.up;

        // Reflection matrix about the plane y = this mirror's water height.
        Vector4 plane = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, planePos));
        Matrix4x4 reflection = CalculateReflectionMatrix(plane);

        // Mirror the transform too (for sensible frustum culling); the matrices below do the exact render.
        Vector3 mPos = reflection.MultiplyPoint(main.transform.position);
        reflCam.transform.position = mPos;
        Vector3 e = main.transform.eulerAngles;
        reflCam.transform.eulerAngles = new Vector3(-e.x, e.y, e.z);

        reflCam.worldToCameraMatrix = main.worldToCameraMatrix * reflection;

        // Oblique near-clip exactly on the water surface: whatever is beneath it is not reflected.
        Vector4 clip = CameraSpacePlane(reflCam.worldToCameraMatrix, planePos, normal);
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

    static Vector4 CameraSpacePlane(Matrix4x4 worldToCam, Vector3 pos, Vector3 normal)
    {
        Vector3 cPos = worldToCam.MultiplyPoint(pos);
        Vector3 cNormal = worldToCam.MultiplyVector(normal).normalized;
        return new Vector4(cNormal.x, cNormal.y, cNormal.z, -Vector3.Dot(cPos, cNormal));
    }
}
