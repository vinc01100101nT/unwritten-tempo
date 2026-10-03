using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen world-map overlay: the map fills the screen at its own aspect. Shows the baked <see cref="WorldAtlas"/> — the world-map picture exported from the
/// world creator, as it is (or, without one, a top-down snapshot of every map) — with a pulsing "you are here" marker
/// over the map you're currently in, placed by that map's rectangle plus the player's position in it. Toggle with <see cref="GameKeys.WorldMap"/>
/// (default <b>N</b>; <b>M</b> is the corner minimap). Esc closes. The wheel zooms toward the cursor, a zoomed map drags
/// with the mouse, and a location button — shown only while zoomed in and not already centred on you — brings the view
/// back to your character. Every open starts at the whole map.
///
/// Self-installing: a runtime bootstrap spawns it into the persistent <c>Systems</c> scene (so it survives map
/// swaps with the rest of the HUD, no DontDestroyOnLoad) the moment a <see cref="WorldAtlas"/> exists in
/// Resources. It builds its own overlay Canvas in code, so there's no UI-shell rebuild to run.
/// </summary>
public class WorldMapHUD : MonoBehaviour
{
    [Tooltip("How far in the wheel can zoom; 1 is the whole map filling the screen.")]
    public float maxZoom = 4f;
    [Tooltip("How much one wheel notch zooms.")]
    public float zoomPerNotch = 1.2f;
    [Tooltip("Seconds the location button takes to glide the view back to you. It starts fast and slows to a stop.")]
    public float locateSeconds = 0.4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<WorldMapHUD>() != null) return;       // already installed this session
        if (Resources.Load<WorldAtlas>("WorldAtlas") == null) return;   // no world baked yet

        var go = new GameObject("WorldMapHUD");
        var systems = SceneManager.GetSceneByName(MapManager.SystemsScene);
        if (systems.IsValid() && systems.isLoaded) SceneManager.MoveGameObjectToScene(go, systems);
        else DontDestroyOnLoad(go);
        go.AddComponent<WorldMapHUD>();
    }

    WorldAtlas data;
    GameObject panel;
    RectTransform panelRect;
    RectTransform zoomRoot;
    Canvas canvas;
    RawImage atlasImage;
    RectTransform marker;
    GameObject locateButton;
    float zoom = 1f;
    Vector2 pan;
    bool gliding;
    float glideT;
    Vector2 glideFrom, glideTo;
    TMP_Text whereLabel;
    Transform player;

    void Start()
    {
        data = Resources.Load<WorldAtlas>("WorldAtlas");
        if (data == null || data.atlas == null) { enabled = false; return; }
        BuildUI();
        panel.SetActive(false);
    }

    void Update()
    {
        if (Hotkeys.Down(GameKeys.WorldMap)) Toggle();
        if (panel == null || !panel.activeSelf) return;
        if (Hotkeys.CancelDown()) { panel.SetActive(false); Hotkeys.MarkCancelHandled(); return; }
        UpdateMarker();
        StepGlide();
        locateButton.SetActive(zoom > 1.001f && (ClampPan(-marker.anchoredPosition * zoom) - pan).sqrMagnitude > 16f);
    }

    void Toggle()
    {
        bool open = !panel.activeSelf;
        panel.SetActive(open);
        if (open) { zoom = 1f; pan = Vector2.zero; gliding = false; ApplyView(); UpdateMarker(); }
    }

    // ── marker = current map's world rect + the player's position within it ───────────────────────────────
    void UpdateMarker()
    {
        string scene = SceneManager.GetActiveScene().name;
        var e = data.Find(scene);
        if (e == null) { marker.gameObject.SetActive(false); if (whereLabel) whereLabel.text = scene; return; }
        marker.gameObject.SetActive(true);

        if (player == null && PersistentPlayer3D.Instance != null) player = PersistentPlayer3D.Instance.transform;

        float cx = e.x + e.w * 0.5f, cy = e.y + e.h * 0.5f;     // map centre in map-space (y is south-down)
        float px = 0f, pz = 0f;
        if (player != null)
        {
            px = Mathf.Clamp(player.position.x, -e.w * 0.5f, e.w * 0.5f);   // east offset within the map
            pz = Mathf.Clamp(player.position.z, -e.h * 0.5f, e.h * 0.5f);   // north offset
        }
        float worldX = cx + px;
        float worldY = cy - pz;                                  // +z (north) → smaller y (toward the top)

        float u = (worldX - data.minX) / Mathf.Max(1f, data.worldW);
        float vFromTop = (worldY - data.minY) / Mathf.Max(1f, data.worldH);

        Vector2 size = atlasImage.rectTransform.rect.size;
        marker.anchoredPosition = new Vector2((u - 0.5f) * size.x, (0.5f - vFromTop) * size.y);

        // Kept the same size on screen however far in the map is zoomed.
        float pulse = (1f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f)) / zoom;
        marker.localScale = new Vector3(pulse, pulse, 1f);
        if (whereLabel) whereLabel.text = scene;
    }

    // ── zoom and drag ─────────────────────────────────────────────────────────────────────────────────────
    // The picture sits inside one node that is scaled by the zoom and moved by the drag. A drag can never pull an edge
    // of the picture past the edge of the screen, and at the whole-map zoom there is nothing to drag.

    void ZoomAt(Vector2 screenPoint, float notches)
    {
        float next = Mathf.Clamp(zoom * Mathf.Pow(zoomPerNotch, notches), 1f, maxZoom);
        if (Mathf.Approximately(next, zoom)) return;
        gliding = false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(panelRect, screenPoint, null, out Vector2 cursor);
        Vector2 underCursor = (cursor - pan) / zoom;
        zoom = next;
        pan = ClampPan(cursor - underCursor * zoom);
        ApplyView();
    }

    void DragBy(Vector2 screenDelta)
    {
        if (zoom <= 1.001f) return;
        gliding = false;
        pan = ClampPan(pan + screenDelta / Mathf.Max(0.0001f, canvas.scaleFactor));
        ApplyView();
    }

    // The wheel or a drag takes the view back from a glide the moment either is used.
    void Locate()
    {
        glideFrom = pan;
        glideTo = ClampPan(-marker.anchoredPosition * zoom);
        glideT = 0f;
        gliding = true;
    }

    void StepGlide()
    {
        if (!gliding) return;
        glideT = Mathf.Min(1f, glideT + Time.unscaledDeltaTime / Mathf.Max(0.01f, locateSeconds));
        pan = ClampPan(Vector2.LerpUnclamped(glideFrom, glideTo, EaseOut(glideT)));
        ApplyView();
        if (glideT >= 1f) gliding = false;
    }

    /// <summary>Ease-out: quick at the start, slowing to rest at the end (a cubic curve, 0 to 1).</summary>
    static float EaseOut(float t) { float u = 1f - t; return 1f - u * u * u; }

    Vector2 ClampPan(Vector2 want)
    {
        Vector2 picture = atlasImage.rectTransform.rect.size * zoom;
        Vector2 screen = panelRect.rect.size;
        float mx = Mathf.Max(0f, (picture.x - screen.x) * 0.5f), my = Mathf.Max(0f, (picture.y - screen.y) * 0.5f);
        return new Vector2(Mathf.Clamp(want.x, -mx, mx), Mathf.Clamp(want.y, -my, my));
    }

    void ApplyView()
    {
        zoomRoot.localScale = new Vector3(zoom, zoom, 1f);
        zoomRoot.anchoredPosition = pan;
    }

    /// <summary>The wheel and the drag, caught by the dimmed backdrop that covers the whole screen.</summary>
    sealed class MapInput : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler
    {
        public WorldMapHUD owner;
        public void OnScroll(PointerEventData e) => owner.ZoomAt(e.position, e.scrollDelta.y);
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) owner.DragBy(e.delta); }
    }

    // ── UI construction (own overlay canvas, in code) ─────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("WorldMapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;                                  // above the rest of the HUD
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        UiKit.ApplyTextCrispness(scaler);

        panel = new GameObject("WorldMapPanel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        panelRect = panel.GetComponent<RectTransform>();
        Stretch(panelRect);

        // dim, click-blocking backdrop — and the thing the wheel and the drag land on, since nothing above it catches them
        var dim = NewImage("Dim", panel.transform, new Color(0f, 0f, 0f, 0.8f));
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;
        dim.gameObject.AddComponent<MapInput>().owner = this;

        zoomRoot = new GameObject("Zoom", typeof(RectTransform)).GetComponent<RectTransform>();
        zoomRoot.SetParent(panel.transform, false);
        Stretch(zoomRoot);

        // the atlas image, filling the whole screen at its own aspect (the fitter keeps it fitted on any resize)
        atlasImage = new GameObject("Atlas", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage))
            .GetComponent<RawImage>();
        atlasImage.transform.SetParent(zoomRoot, false);
        atlasImage.texture = data.atlas;
        atlasImage.raycastTarget = false;
        Stretch(atlasImage.rectTransform);
        var fit = atlasImage.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = (float)data.atlas.width / Mathf.Max(1, data.atlas.height);

        // "you are here" marker (child of the atlas so it shares its rect space)
        var mk = NewImage("YouAreHere", atlasImage.transform, new Color(1f, 0.92f, 0.25f, 1f));
        marker = mk.rectTransform;
        marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(16f, 16f);

        // title + hint + current-map label
        var title = NewText("Title", panel.transform, "WORLD MAP", 34, TextAnchor.UpperCenter);
        Top(title.rectTransform, 36f, 420f);
        whereLabel = NewText("Where", panel.transform, "", 20, TextAnchor.LowerCenter);
        Bottom(whereLabel.rectTransform, 64f, 420f);
        var hint = NewText("Hint", panel.transform, $"[{GameKeys.Label(GameKeys.WorldMap)}] / [Esc] to close", 16, TextAnchor.LowerCenter);
        hint.color = new Color(UiKit.TextOnDim.r, UiKit.TextOnDim.g, UiKit.TextOnDim.b, 0.6f);
        Bottom(hint.rectTransform, 34f, 420f);

        // back to where you are — only shown while zoomed in and looking somewhere else (see Update)
        var locate = UiKit.Btn("", panel.transform, Locate);
        var lrt = (RectTransform)locate.transform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 0f);
        lrt.sizeDelta = new Vector2(56f, 56f);
        lrt.anchoredPosition = new Vector2(-40f, 40f);
        var pin = NewImage("Pin", locate.transform, UiKit.Accent);
        pin.sprite = BuildPinSprite();
        pin.rectTransform.sizeDelta = new Vector2(34f, 34f);
        locateButton = locate.gameObject;
        locateButton.SetActive(false);
    }

    // A map pin — a round head with a hole, narrowing to a point — baked once when the map is built.
    static Sprite BuildPinSprite()
    {
        const int size = 64, samples = 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        var head = new Vector2(32f, 42f);
        const float radius = 18f, hole = 7f, tipY = 4f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int inside = 0;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                var p = new Vector2(x + (sx + 0.5f) / samples, y + (sy + 0.5f) / samples);
                float d = Vector2.Distance(p, head);
                bool body = d <= radius
                            || (p.y >= tipY && p.y <= head.y && Mathf.Abs(p.x - head.x) <= (p.y - tipY) / (head.y - tipY) * radius * 0.82f);
                if (body && d > hole) inside++;
            }
            px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (samples * samples)));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // ── tiny uGUI helpers ─────────────────────────────────────────────────────────────────────────────────
    static Image NewImage(string name, Transform parent, Color c)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text NewText(string name, Transform parent, string s, int size, TextAnchor anchor)
    {
        var t = UiKit.MakeText(name, parent, s, size, FontStyle.Normal, anchor);
        t.color = UiKit.TextOnDim;
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
    static void Top(RectTransform rt, float fromTop, float width)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(width, 50f); rt.anchoredPosition = new Vector2(0f, -fromTop);
    }
    static void Bottom(RectTransform rt, float fromBottom, float width)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(width, 30f); rt.anchoredPosition = new Vector2(0f, fromBottom);
    }
}
