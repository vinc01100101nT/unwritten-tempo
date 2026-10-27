using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// A live 3D look at a character body, rendered into a UI panel — what character creation shows so a player
/// picks a body by SEEING it rather than by reading its name.
///
/// <para>The body is a real model from the shared roster (<see cref="CharacterModels"/>), so the preview can
/// never show something the game would not draw. It is built far above the world and filmed by its own camera,
/// which is why nothing from the map leaks into the shot and nothing in the shot touches the map.</para>
///
/// <para>Everything it makes is marked never-save and destroyed with it: the staging rig, the camera, its light
/// and the picture it renders into. Leaving any of them behind is how a runtime-only object ends up baked into
/// a saved scene.</para>
/// </summary>
public class CharacterPreview : MonoBehaviour
{
    // Far enough above the map that no world geometry, weather or light can wander into frame.
    static readonly Vector3 Stage = new Vector3(0f, 5000f, 0f);

    // Used only when the picture's on-screen size cannot be read (no canvas yet).
    const int FallbackSize = 512;

    Camera cam;
    Camera world;
    RenderTexture picture;
    RawImage shownIn;

    // The portrait's own view angle, used when there is no world camera to match.
    const float PortraitFov = 30f;
    // How much further back than the portrait distance the camera stands, so a narrower view keeps the same framing.
    float spread = 1f;
    Transform stageRoot;
    Transform bodyPivot;
    GameObject body;
    CharacterDye3D dye;
    string shownModel;

    DayNightLight dayNight;
    Light worldSun;
    bool sunWasOn, ambientSet;
    Color ambientWas, equatorWas, groundWas;
    AmbientMode ambientModeWas;

    // How the body is being held right now. The camera and the lights never move sideways — it is the BODY that
    // turns, which keeps the lighting steady while it does.
    float turn;
    float zoom = 1f;

    const float ZoomIn = 0.45f;
    const float ZoomOut = 1.9f;

    // Framing the head instead of the whole body: 0 = whole body, 1 = head, eased between over FocusSeconds. The head
    // is measured once per body (its centre in the stage's space, and its height), never per frame.
    float focus, focusTarget;
    Vector3 headCentre;
    float headHeight;
    const float FocusSeconds = 0.25f;
    // How much of the picture's height the head fills when framed: the picture shows this many head-heights.
    const float HeadsInView = 2.4f;

    /// <summary>Raised once a new body is standing and its recolourable parts are known, so a colour picker can
    /// rebuild for the body actually on show rather than the one before it.</summary>
    public event System.Action OnBodyChanged;

    /// <summary>The model of the body standing in the picture right now — not the one still loading.</summary>
    public string BodyOnStage { get; private set; }

    /// <summary>The parts of the body on show that can be recoloured, or an empty list before one is up.</summary>
    public System.Collections.Generic.IReadOnlyList<Color> Parts =>
        dye != null ? dye.DetectableParts() : System.Array.Empty<Color>();

    /// <summary>Recolour one part of the body on show. Nothing is saved: the colours are held by the persistent
    /// character this screen sits on, stamped for this body, and only written down when the character is created.</summary>
    public void SetPartColor(Color source, Color target)
    {
        if (dye != null) dye.SetPartColor(source, target);
    }

    /// <summary>What a part is currently showing as.</summary>
    public Color TargetFor(Color source) => dye != null ? dye.TargetFor(source) : source;

    /// <summary>Build a preview and put its picture into <paramref name="into"/>. It lives under
    /// <paramref name="owner"/>, so closing the screen that opened it takes the camera, the light and the
    /// picture with it — a preview that outlived its panel would keep rendering, unseen, forever.</summary>
    public static CharacterPreview Create(RawImage into, Transform owner)
    {
        var go = new GameObject("CharacterPreview") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.SetParent(owner, false);
        var preview = go.AddComponent<CharacterPreview>();
        QuitTeardown.Register(go);
        preview.Build(into);
        return preview;
    }

    void Build(RawImage into)
    {
        stageRoot = new GameObject("Stage") { hideFlags = HideFlags.DontSaveInEditor }.transform;
        stageRoot.SetParent(transform, false);
        stageRoot.position = Stage;

        // The body hangs from a pivot at half its height, so it turns about its own middle.
        bodyPivot = new GameObject("BodyPivot") { hideFlags = HideFlags.DontSaveInEditor }.transform;
        bodyPivot.SetParent(stageRoot, false);
        bodyPivot.localPosition = new Vector3(0f, Character3DBody.BodyHeight * 0.5f, 0f);

        // The ink itself: the game's own filter, with its own live settings — not a copy. It draws for every camera
        // that renders its layer, this one included; this only makes sure it exists on a screen before the world.
        HandDrawnFX.Ensure();

        var camGo = new GameObject("Camera") { hideFlags = HideFlags.DontSaveInEditor };
        camGo.transform.SetParent(stageRoot, false);
        cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.white;   // a plain white studio, so a body reads as itself and not as a silhouette
        cam.orthographic = false;
        cam.fieldOfView = PortraitFov;
        cam.nearClipPlane = 0.01f;
        // The preview draws with its own, lighter ink.
        HandDrawnFX.PreviewCamera = cam;
        world = FindWorldCamera();
        shownIn = into;
        FitPicture();

        // Slightly above eye level looking down a little, which reads as a portrait rather than a floor plan.
        camGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
        ApplyView();

        // ALWAYS NOON. The world's sun and ambient follow the clock, and the preview used to take them along, so
        // at night the body was filmed in the night's dark. The studio's key is the world's MIDDAY sun, read from
        // the one place that owns it, and while this camera films, the world sun is off and the ambient is midday's.
        dayNight = FindFirstObjectByType<DayNightLight>();
        worldSun = dayNight != null ? dayNight.GetComponent<Light>() : RenderSettings.sun;
        var lightGo = new GameObject("Light") { hideFlags = HideFlags.DontSaveInEditor };
        lightGo.transform.SetParent(stageRoot, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = dayNight != null ? dayNight.dayIntensity : 1.2f;
        light.color = dayNight != null ? dayNight.dayColor : new Color(1f, 0.97f, 0.9f);
        // From the front, high and to one side: the body faces the camera, so the key travels away from it.
        lightGo.transform.localRotation = Quaternion.Euler(35f, 30f, 0f);
        light.cullingMask = ~0;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        RenderPipelineManager.endCameraRendering += AfterCamera;

        // A second, softer light from the other side. One light leaves half of a body in darkness, which reads
        // as "this model is broken" rather than "this side is unlit".
        var fillGo = new GameObject("Fill") { hideFlags = HideFlags.DontSaveInEditor };
        fillGo.transform.SetParent(stageRoot, false);
        var fillLight = fillGo.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.6f;
        fillLight.color = new Color(0.85f, 0.9f, 1f);
        fillGo.transform.localRotation = Quaternion.Euler(20f, -40f, 0f);

        if (into != null)
        {
            into.color = Color.white;
            into.raycastTarget = true;   // the picture catches the wheel and the double-click
            var hand = into.gameObject.AddComponent<PreviewHandle>();
            hand.preview = this;
        }
    }

    // ---- holding the body ------------------------------------------------------------------------------

    /// <summary>Turn the body left/right, in degrees — what the screen's two turn buttons call.</summary>
    public void Turn(float degrees)
    {
        turn += degrees;
        ApplyView();
    }

    /// <summary>Move the camera closer or further away. Positive comes closer.</summary>
    public void Zoom(float amount)
    {
        zoom = Mathf.Clamp(zoom - amount, ZoomIn, ZoomOut);
        ApplyView();
    }

    /// <summary>Frame the head (<paramref name="on"/>) or the whole body, easing there over a quarter second.</summary>
    public void FocusHead(bool on)
    {
        focusTarget = on ? 1f : 0f;
        MeasureHead();
    }

    // The head of the body on show: from its head bone up to the top of the bare body.
    void MeasureHead()
    {
        var socket = body != null ? body.GetComponent<HeadSocket>() : null;
        if (socket == null || stageRoot == null || !socket.TryHead(out var headBase, out var top)) return;
        headHeight = Mathf.Max(0.05f, top - headBase.y);
        headCentre = stageRoot.InverseTransformPoint(new Vector3(headBase.x, (headBase.y + top) * 0.5f, headBase.z));
    }

    /// <summary>Back to the straight-on portrait the preview opens with.</summary>
    public void ResetView()
    {
        turn = 0f;
        zoom = 1f;
        ApplyView();
    }

    void ApplyView()
    {
        if (bodyPivot != null) bodyPivot.localRotation = Quaternion.Euler(0f, turn, 0f);
        if (cam == null) return;
        float height = Character3DBody.BodyHeight;
        var portrait = new Vector3(0f, height * 0.62f * zoom, -height * 2.6f * zoom);
        // Backed off along its own line of sight, about the point where that line crosses the body, so what sits
        // in the middle of the picture stays there however narrow the view is.
        var fwd = cam.transform.localRotation * Vector3.forward;
        float along = -portrait.z / fwd.z;
        var whole = portrait + fwd * (along * (1f - spread));
        if (focus <= 0f || headHeight <= 0f) { cam.transform.localPosition = whole; }
        else
        {
            // Backed off from the head's centre along the same line of sight, far enough that the view holds
            // HeadsInView head-heights — the wheel's zoom still moves in and out from there.
            float halfTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float distance = headHeight * HeadsInView * 0.5f / halfTan * zoom;
            var head = headCentre - fwd * distance;
            cam.transform.localPosition = Vector3.Lerp(whole, head, Mathf.SmoothStep(0f, 1f, focus));
        }
        // Only just past the furthest the camera can be pulled back to, so nothing but the staged body can be
        // in shot. Measured against the zoom limit rather than guessed, or a body vanishes when zoomed out.
        cam.farClipPlane = height * (2.6f * ZoomOut * spread + 2f);
    }

    // THE SAME VIEW PER PIXEL AS THE WORLD. The ink finds creases by comparing depth a fixed number of PIXELS apart,
    // so how many lines grow on a body depends on how much of the view one pixel covers. The world camera's pixel
    // covers about a third of the portrait's (35° over 972 pixels against 30° over 265, measured), which drew
    // three times the lines here. Given the world's share per pixel, the same body grows the same lines.
    void MatchWorldView()
    {
        if (cam == null || picture == null) return;
        if (world == null || world.pixelHeight <= 0) { cam.fieldOfView = PortraitFov; spread = 1f; ApplyView(); return; }
        float perPixel = Mathf.Tan(world.fieldOfView * 0.5f * Mathf.Deg2Rad) / world.pixelHeight;
        float halfView = perPixel * picture.height;
        cam.fieldOfView = 2f * Mathf.Atan(halfView) * Mathf.Rad2Deg;
        spread = Mathf.Tan(PortraitFov * 0.5f * Mathf.Deg2Rad) / halfView;
        ApplyView();
    }

    // The world's camera, found once. On the front screen it is switched off, which hides it from Camera.main.
    static Camera FindWorldCamera()
    {
        foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.CompareTag("MainCamera")) return c;
        return null;
    }

    /// <summary>The picture's own hands: the wheel zooms, and a double-click puts it back the way it opened. It is
    /// deliberately NOT draggable — the screen's turn buttons turn it.</summary>
    class PreviewHandle : MonoBehaviour, IScrollHandler, IPointerClickHandler
    {
        public CharacterPreview preview;

        const float ZoomPerNotch = 0.09f;

        public void OnScroll(PointerEventData e)
        {
            if (preview != null) preview.Zoom(e.scrollDelta.y * ZoomPerNotch);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (preview != null && e.clickCount >= 2) preview.ResetView();
        }
    }

    /// <summary>Show <paramref name="modelName"/>. Asking for the body already on stage does nothing, so paging
    /// back and forth through the roster does not rebuild the same body over and over.</summary>
    public void Show(string modelName)
    {
        if (string.IsNullOrEmpty(modelName) || modelName == shownModel) return;
        shownModel = modelName;

        CharacterModels.Load(modelName, model =>
        {
            if (this == null || model == null) return null;
            if (modelName != shownModel) return null;   // paged on while this one was loading — it is no longer wanted

            if (body != null) Destroy(body);
            dye = null;

            body = Instantiate(model, bodyPivot);
            BodyOnStage = modelName;
            body.hideFlags = HideFlags.DontSaveInEditor;
            // Hung from the pivot's middle, so its feet still land on the stage floor.
            body.transform.localPosition = new Vector3(0f, -Character3DBody.BodyHeight * 0.5f, 0f);
            body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // face the camera

            // Fit to the same height every character in the game stands at, so paging bodies doesn't make them
            // jump in size — measured at scale one, exactly as the shared body builder does it.
            body.transform.localScale = Vector3.one;
            float native = Character3DBody.MeasureHeight(body);
            if (native > 1e-3f) body.transform.localScale = Vector3.one * (Character3DBody.BodyHeight / native);

            // The shared Humanoid controller, so the body stands in its authored idle instead of a T-pose.
            var animator = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
            animator.runtimeAnimatorController = CharacterSpec.Active.animatorController;
            animator.applyRootMotion = false;

            // The SAME recolouring the character wears in game. It binds to the persistent character that already
            // exists under this screen, so the world body is bound to the same colours — which is why a body only
            // SHOWS colours stamped for it and never erases them. Nothing is saved until the character is created.
            dye = body.AddComponent<CharacterDye3D>();
            // A body the player has not coloured yet starts with light clothes, saved like a pick of their own.
            if (!dye.WearsOwnDye) dye.StartLight();
            // …and the hair being chosen, held by that same character.
            body.AddComponent<HeadSocket>();
            // A body just paged to is shown as itself, not still held at the last one's angle — framed the way the
            // screen asks, on its own head.
            ResetView();
            MeasureHead();
            ApplyView();
            OnBodyChanged?.Invoke();
            return body;
        });
    }

    void LateUpdate()
    {
        FitPicture();
        if (focus == focusTarget) return;
        focus = Mathf.MoveTowards(focus, focusTarget, Time.unscaledDeltaTime / FocusSeconds);
        ApplyView();
    }

    // THE SAME PIXELS IT IS SHOWN AT. The game's ink outline (HandDrawnFX) is drawn a set number of PIXELS thick
    // into whatever picture a camera renders, so a picture smaller than its box is stretched up and its lines grow
    // thick and blocky with it. The box's size on screen is only settled once its canvas has scaled — it was
    // measured at 120x171 while the box showed 186x265 — and it changes again with the window, so it is followed
    // rather than read once. The picture is remade only when that size actually moves.
    void FitPicture()
    {
        var size = PixelSize(shownIn);
        if (picture != null && picture.width == size.x && picture.height == size.y) return;
        if (cam != null) cam.targetTexture = null;
        if (picture != null) { picture.Release(); Destroy(picture); }
        picture = new RenderTexture(size.x, size.y, 16) { name = "CharacterPreview" };
        if (cam != null) cam.targetTexture = picture;
        if (shownIn != null) shownIn.texture = picture;
        MatchWorldView();
    }

    // The picture's size on screen in pixels, from its box and its canvas's scale.
    static Vector2Int PixelSize(RawImage into)
    {
        if (into == null || into.canvas == null) return new Vector2Int(FallbackSize, FallbackSize);
        var r = into.rectTransform.rect.size * into.canvas.scaleFactor;
        return new Vector2Int(Mathf.Max(64, Mathf.RoundToInt(r.x)), Mathf.Max(64, Mathf.RoundToInt(r.y)));
    }

    // Only around this camera's own render: the world's camera, before and after, sees the clock's lighting.
    void BeforeCamera(ScriptableRenderContext _, Camera c)
    {
        if (c != cam || !Application.isPlaying) return;
        sunWasOn = worldSun != null && worldSun.enabled;
        if (sunWasOn) worldSun.enabled = false;
        if (dayNight == null) return;
        ambientWas = RenderSettings.ambientSkyColor;
        equatorWas = RenderSettings.ambientEquatorColor;
        groundWas = RenderSettings.ambientGroundColor;
        ambientModeWas = RenderSettings.ambientMode;
        // The world's fill: the preview shows the model as it looks in the world.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        dayNight.ApplyFill();
        ambientSet = true;
    }

    void AfterCamera(ScriptableRenderContext _, Camera c)
    {
        if (c != cam) return;
        if (sunWasOn && worldSun != null) worldSun.enabled = true;
        sunWasOn = false;
        if (!ambientSet) return;
        RenderSettings.ambientMode = ambientModeWas;
        RenderSettings.ambientSkyColor = ambientWas;
        RenderSettings.ambientEquatorColor = equatorWas;
        RenderSettings.ambientGroundColor = groundWas;
        ambientSet = false;
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        RenderPipelineManager.endCameraRendering -= AfterCamera;
        if (sunWasOn && worldSun != null) worldSun.enabled = true;
        if (HandDrawnFX.PreviewCamera == cam) HandDrawnFX.PreviewCamera = null;
        if (cam != null) cam.targetTexture = null;
        if (picture != null) { picture.Release(); Destroy(picture); }
    }
}
