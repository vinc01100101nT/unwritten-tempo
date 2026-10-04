using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// THE MAP WINDOW (M) — full screen, on the main screen layer, with two views and a button to switch between them.
///
/// <para><b>Zone view</b> (what M opens): one map's whole picture, the same picture the minimap slides under you
/// (<see cref="MinimapCatalog"/>). In the map you stand in it shows where you are and the followed quest's whole route
/// as the ground's own arrows (<see cref="QuestArrows.Marks"/>) — one route, drawn on the ground, the minimap and here.
/// Any other map shows its picture alone.</para>
///
/// <para><b>World view</b>: the baked <see cref="WorldAtlas"/> with a pulsing "you are here" over the map you are in.
/// Clicking a map that has a picture opens the zone view on it.</para>
///
/// <para>In both, the wheel zooms toward the cursor and a zoomed view drags with the mouse. The location button brings
/// the view back to you: in the world view it glides to your marker; in the zone view it opens your own map and
/// glides to you. Esc or M closes.</para>
///
/// <para>Self-installing: a runtime bootstrap spawns it into the persistent <c>Systems</c> scene the moment a
/// <see cref="WorldAtlas"/> exists in Resources, and it builds itself on <see cref="UiKit.HudCanvas"/>.</para>
/// </summary>
public class MapWindow : MonoBehaviour
{
    [Tooltip("How far in the wheel can zoom; 1 is the whole map filling the screen.")]
    public float maxZoom = 4f;
    [Tooltip("How much one wheel notch zooms.")]
    public float zoomPerNotch = 1.2f;
    [Tooltip("Seconds the location button takes to glide the view back to you. It starts fast and slows to a stop.")]
    public float locateSeconds = 0.4f;

    // INSTALLED EVERY TIME THE SYSTEMS SCENE COMES UP, not once per launch. Leaving the world ("Select character",
    // "Log out") reloads that scene, which takes this window and the screen layer it draws on down with it — and a
    // window installed only at launch never came back, so M did nothing until the game was restarted.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Install();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == MapManager.SystemsScene) Install();
    }

    static void Install()
    {
        var systems = SceneManager.GetSceneByName(MapManager.SystemsScene);
        // ALREADY HERE ONLY IF IT LIVES IN THIS SYSTEMS SCENE. The one from the scene just reloaded is still being
        // torn down when the new scene arrives, so "is there one at all" says yes and the window never comes back.
        foreach (var existing in FindObjectsByType<MapWindow>(FindObjectsInactive.Include))
            if (existing != null && (existing.gameObject.scene == systems || !systems.IsValid())) return;
        if (Resources.Load<WorldAtlas>("WorldAtlas") == null) return;   // no world baked yet

        var go = new GameObject("MapWindow");
        if (systems.IsValid() && systems.isLoaded) SceneManager.MoveGameObjectToScene(go, systems);
        else DontDestroyOnLoad(go);
        go.AddComponent<MapWindow>();
    }

    // ONE ZOOMABLE PICTURE. The picture sits inside one node that is scaled by the zoom and moved by the drag; a drag can
    // never pull an edge of the picture past the edge of the screen, and at the whole-map zoom there is nothing to drag.
    sealed class View
    {
        public RectTransform zoomRoot;
        public RectTransform picture;
        public RectTransform marker;
        public float zoom = 1f;
        public Vector2 pan;
    }

    WorldAtlas data;
    Canvas canvas;
    GameObject panel;
    RectTransform panelRect;
    readonly View world = new View(), zone = new View();
    View active;
    RawImage atlasImage, zoneImage;
    TMP_Text title, whereLabel, zoneMissing, switchLabel;
    GameObject locateButton;
    Transform player;

    // The map the zone view shows, and the picture it holds for it (taken from, and given back to, MinimapCatalog).
    string zoneMap, zonePictureMap;
    MinimapCatalog.Entry zoneEntry;

    // The route's arrows in the zone view, laid when the route, the map or the picture's size changed.
    RectTransform routeRoot;
    readonly List<Image> routeMarks = new List<Image>();
    Sprite routeSprite;
    int routeDrawnVersion = -1;
    string routeDrawnMap;
    Vector2 routeDrawnSize;
    float routeDrawnZoom = -1f;

    bool gliding;
    float glideT;
    Vector2 glideFrom, glideTo;

    // Sizes on the main screen layer, which is laid out as if every screen were 1280 wide.
    const float MarkerSize = 11f, RouteMarkSize = 14f;

    void Start()
    {
        data = Resources.Load<WorldAtlas>("WorldAtlas");
        canvas = UiKit.HudCanvas;
        // A server draws nothing.
        if (data == null || data.atlas == null || canvas == null || (GameServer.IsServer && !GameNetwork.TheWorldIsElsewhere))
        {
            enabled = false;
            return;
        }
        BuildUI();
        panel.SetActive(false);
    }

    void OnDestroy()
    {
        MinimapCatalog.ReleasePicture(zonePictureMap);
        if (panel != null) Destroy(panel);
    }

    void Update()
    {
        if (Hotkeys.Down(GameKeys.Map)) Toggle();
        if (panel == null || !panel.activeSelf) return;
        if (Hotkeys.CancelDown()) { Close(); Hotkeys.MarkCancelHandled(); return; }
        if (player == null && PersistentPlayer3D.Instance != null) player = PersistentPlayer3D.Instance.transform;

        if (active == world) UpdateWorldMarker(); else UpdateZone();
        StepGlide();
        locateButton.SetActive(active == zone ? LocateUsefulInZone() : Zoomed(world) && !CentredOnMarker(world));
    }

    void Toggle()
    {
        if (panel.activeSelf) { Close(); return; }
        panel.SetActive(true);
        UiLayers.Raise(panel.transform, UiLayers.Layer.Window);
        ResetView(world);
        ShowZone(MapManager.CurrentMap);
    }

    void Close()
    {
        panel.SetActive(false);
        gliding = false;
        // A closed window holds no picture.
        MinimapCatalog.ReleasePicture(zonePictureMap);
        zonePictureMap = null;
        zoneImage.texture = null;
    }

    // ── the two views ─────────────────────────────────────────────────────────────────────────────────────────────

    void ShowWorld()
    {
        Activate(world);
        title.text = "WORLD MAP";
        switchLabel.text = "Zone map";
        UpdateWorldMarker();
    }

    // The zone view, on `map`. The picture is taken from the one owner of map pictures; a map nobody has drawn says so.
    void ShowZone(string map)
    {
        Activate(zone);
        switchLabel.text = "World map";
        title.text = map ?? "";
        if (map != zoneMap || zonePictureMap == null)
        {
            ResetView(zone);
            MinimapCatalog.ReleasePicture(zonePictureMap);
            zonePictureMap = null;
            zoneMap = map;
            var catalog = MinimapCatalog.Instance;
            zoneEntry = catalog != null ? catalog.Find(map) : null;
            var picture = zoneEntry != null ? MinimapCatalog.AcquirePicture(map) : null;
            if (picture != null) zonePictureMap = map;
            zoneImage.texture = picture;
            zoneImage.enabled = picture != null;
            zoneMissing.gameObject.SetActive(picture == null);
            routeDrawnVersion = -1;
        }
        UpdateZone();
    }

    void Activate(View v)
    {
        active = v;
        gliding = false;
        world.zoomRoot.gameObject.SetActive(v == world);
        zone.zoomRoot.gameObject.SetActive(v == zone);
    }

    void SwitchView()
    {
        UISounds.PlayClick();
        if (active == world) ShowZone(zoneMap ?? MapManager.CurrentMap); else ShowWorld();
    }

    // ── world view: the marker over the map you are in ────────────────────────────────────────────────────────────
    void UpdateWorldMarker()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (whereLabel) whereLabel.text = scene;
        var e = data.Find(scene);
        if (e == null) { world.marker.gameObject.SetActive(false); return; }
        world.marker.gameObject.SetActive(true);

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

        Vector2 size = world.picture.rect.size;
        world.marker.anchoredPosition = new Vector2((u - 0.5f) * size.x, (0.5f - vFromTop) * size.y);
        Pulse(world);
    }

    // Kept the same size on screen however far in the view is zoomed.
    static void Pulse(View v)
    {
        float pulse = (1f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f)) / v.zoom;
        v.marker.localScale = new Vector3(pulse, pulse, 1f);
    }

    // A click on the world view opens the zone view on the map under the cursor — only a map that has a picture.
    void PickZone(Vector2 screenPoint)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(world.picture, screenPoint, null, out Vector2 local)) return;
        Vector2 size = world.picture.rect.size;
        float u = local.x / Mathf.Max(1f, size.x) + 0.5f, vFromTop = 0.5f - local.y / Mathf.Max(1f, size.y);
        float wx = data.minX + u * data.worldW, wy = data.minY + vFromTop * data.worldH;
        var catalog = MinimapCatalog.Instance;
        foreach (var e in data.maps)
        {
            if (e == null || wx < e.x || wx > e.x + e.w || wy < e.y || wy > e.y + e.h) continue;
            if (catalog == null || catalog.Find(e.scene) == null) return;
            UISounds.PlayClick();
            ShowZone(e.scene);
            return;
        }
    }

    // ── zone view: you, and the route, on the map you stand in ────────────────────────────────────────────────────
    bool ShowingOwnMap => zoneMap == MapManager.CurrentMap && zoneEntry != null;

    void UpdateZone()
    {
        if (whereLabel) whereLabel.text = MapManager.CurrentMap;
        bool own = ShowingOwnMap && player != null;
        zone.marker.gameObject.SetActive(own);
        if (own)
        {
            zone.marker.anchoredPosition = OnZonePicture(player.position);
            Pulse(zone);
        }
        DrawRoute();
    }

    // A spot on the ground of the map you stand in → a point on the zone picture, from its centre.
    Vector2 OnZonePicture(Vector3 worldPos)
    {
        Vector3 p = MapManager.InMap(worldPos, zoneMap);
        float size = Mathf.Max(1f, zoneEntry.worldSize);
        float u = (p.x - zoneEntry.worldMin.x) / size, v = (p.z - zoneEntry.worldMin.y) / size;
        Vector2 pictureSize = zone.picture.rect.size;
        return new Vector2((u - 0.5f) * pictureSize.x, (v - 0.5f) * pictureSize.y);
    }

    // The marks are laid only when the route, the map or the picture's size changed, and resized only when the zoom did.
    void DrawRoute()
    {
        bool own = ShowingOwnMap && zoneImage.enabled;
        Vector2 size = zone.picture.rect.size;
        if (routeDrawnVersion != QuestArrows.RouteVersion || routeDrawnMap != zoneMap || routeDrawnSize != size)
        {
            routeDrawnVersion = QuestArrows.RouteVersion;
            routeDrawnMap = zoneMap;
            routeDrawnSize = size;
            routeDrawnZoom = -1f;
            var marks = QuestArrows.Marks;
            int used = 0;
            if (own)
                for (int i = 0; i < marks.Count; i++)
                {
                    var m = marks[i];
                    var img = RouteMark(used++);
                    img.rectTransform.anchoredPosition = OnZonePicture(new Vector3(m.x, 0f, m.y));
                    img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, m.z);
                }
            for (int i = used; i < routeMarks.Count; i++)
                if (routeMarks[i].gameObject.activeSelf) routeMarks[i].gameObject.SetActive(false);
        }
        if (!Mathf.Approximately(routeDrawnZoom, zone.zoom))
        {
            routeDrawnZoom = zone.zoom;
            var s = new Vector3(1f / zone.zoom, 1f / zone.zoom, 1f);
            for (int i = 0; i < routeMarks.Count; i++) routeMarks[i].rectTransform.localScale = s;
        }
    }

    Image RouteMark(int i)
    {
        while (routeMarks.Count <= i)
        {
            var img = NewImage("RouteMark", routeRoot, Color.white);
            img.sprite = routeSprite;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(RouteMarkSize, RouteMarkSize);
            routeMarks.Add(img);
            routeDrawnZoom = -1f;
        }
        var mark = routeMarks[i];
        if (!mark.gameObject.activeSelf) mark.gameObject.SetActive(true);
        return mark;
    }

    // ── the location button ───────────────────────────────────────────────────────────────────────────────────────
    static bool Zoomed(View v) => v.zoom > 1.001f;

    bool CentredOnMarker(View v) => (ClampPan(v, -v.marker.anchoredPosition * v.zoom) - v.pan).sqrMagnitude <= 16f;

    // In the zone view it is useful while another map is shown (it brings you home), or while zoomed away from you.
    bool LocateUsefulInZone()
    {
        string here = MapManager.CurrentMap;
        if (zoneMap != here) { var c = MinimapCatalog.Instance; return c != null && c.Find(here) != null; }
        return zone.marker.gameObject.activeSelf && Zoomed(zone) && !CentredOnMarker(zone);
    }

    void Locate()
    {
        UISounds.PlayClick();
        if (active == zone && zoneMap != MapManager.CurrentMap) { ShowZone(MapManager.CurrentMap); return; }
        var v = active;
        glideFrom = v.pan;
        glideTo = ClampPan(v, -v.marker.anchoredPosition * v.zoom);
        glideT = 0f;
        gliding = true;
    }

    void StepGlide()
    {
        if (!gliding) return;
        glideT = Mathf.Min(1f, glideT + Time.unscaledDeltaTime / Mathf.Max(0.01f, locateSeconds));
        active.pan = ClampPan(active, Vector2.LerpUnclamped(glideFrom, glideTo, EaseOut(glideT)));
        ApplyView(active);
        if (glideT >= 1f) gliding = false;
    }

    /// <summary>Ease-out: quick at the start, slowing to rest at the end (a cubic curve, 0 to 1).</summary>
    static float EaseOut(float t) { float u = 1f - t; return 1f - u * u * u; }

    // ── zoom and drag, on whichever view is up ────────────────────────────────────────────────────────────────────
    void ZoomAt(Vector2 screenPoint, float notches)
    {
        var v = active;
        float next = Mathf.Clamp(v.zoom * Mathf.Pow(zoomPerNotch, notches), 1f, maxZoom);
        if (Mathf.Approximately(next, v.zoom)) return;
        gliding = false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(panelRect, screenPoint, null, out Vector2 cursor);
        Vector2 underCursor = (cursor - v.pan) / v.zoom;
        v.zoom = next;
        v.pan = ClampPan(v, cursor - underCursor * v.zoom);
        ApplyView(v);
    }

    void DragBy(Vector2 screenDelta)
    {
        var v = active;
        if (!Zoomed(v)) return;
        gliding = false;
        v.pan = ClampPan(v, v.pan + screenDelta / Mathf.Max(0.0001f, canvas.scaleFactor));
        ApplyView(v);
    }

    Vector2 ClampPan(View v, Vector2 want)
    {
        Vector2 picture = v.picture.rect.size * v.zoom;
        Vector2 screen = panelRect.rect.size;
        float mx = Mathf.Max(0f, (picture.x - screen.x) * 0.5f), my = Mathf.Max(0f, (picture.y - screen.y) * 0.5f);
        return new Vector2(Mathf.Clamp(want.x, -mx, mx), Mathf.Clamp(want.y, -my, my));
    }

    static void ApplyView(View v)
    {
        v.zoomRoot.localScale = new Vector3(v.zoom, v.zoom, 1f);
        v.zoomRoot.anchoredPosition = v.pan;
    }

    static void ResetView(View v)
    {
        v.zoom = 1f;
        v.pan = Vector2.zero;
        ApplyView(v);
    }

    /// <summary>The wheel, the drag and the click, caught by the dimmed backdrop that covers the whole screen.</summary>
    sealed class MapInput : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IPointerClickHandler
    {
        public MapWindow owner;
        public void OnScroll(PointerEventData e) => owner.ZoomAt(e.position, e.scrollDelta.y);
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) owner.DragBy(e.delta); }
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && owner.active == owner.world) owner.PickZone(e.position);
        }
    }

    // ── UI construction, on the main screen layer ─────────────────────────────────────────────────────────────────
    void BuildUI()
    {
        panel = new GameObject("MapWindow", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        panel.transform.SetParent(canvas.transform, false);
        panelRect = panel.GetComponent<RectTransform>();
        Stretch(panelRect);

        // dim, click-blocking backdrop — and the thing the wheel, the drag and the click land on
        var dim = NewImage("Dim", panel.transform, new Color(0f, 0f, 0f, 0.8f));
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;
        dim.gameObject.AddComponent<MapInput>().owner = this;

        atlasImage = BuildView(world, "World", data.atlas, (float)data.atlas.width / Mathf.Max(1, data.atlas.height));
        zoneImage = BuildView(zone, "Zone", null, 1f);

        routeSprite = Resources.Load<Sprite>("Guide/GroundArrow");
        routeRoot = new GameObject("Route", typeof(RectTransform)).GetComponent<RectTransform>();
        routeRoot.SetParent(zone.picture, false);
        routeRoot.anchorMin = routeRoot.anchorMax = routeRoot.pivot = new Vector2(0.5f, 0.5f);
        zone.marker.SetAsLastSibling();

        zoneMissing = NewText("NoPicture", panel.transform, "This map has no picture yet.", 16, TextAnchor.MiddleCenter);
        Stretch(zoneMissing.rectTransform);

        title = NewText("Title", panel.transform, "", 22, TextAnchor.UpperCenter);
        Top(title.rectTransform, 24f, 420f);
        whereLabel = NewText("Where", panel.transform, "", 14, TextAnchor.LowerCenter);
        Bottom(whereLabel.rectTransform, 43f, 420f);
        var hint = NewText("Hint", panel.transform, $"[{GameKeys.Label(GameKeys.Map)}] / [Esc] to close", 11, TextAnchor.LowerCenter);
        hint.color = new Color(UiKit.TextOnDim.r, UiKit.TextOnDim.g, UiKit.TextOnDim.b, 0.6f);
        Bottom(hint.rectTransform, 23f, 420f);

        // between the zone and the world map
        var switchButton = UiKit.Btn("", panel.transform, SwitchView);
        var srt = (RectTransform)switchButton.transform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f);
        srt.sizeDelta = new Vector2(110f, 28f);
        srt.anchoredPosition = new Vector2(24f, -24f);
        switchLabel = switchButton.GetComponentInChildren<TMP_Text>();

        // back to where you are (see Update for when it shows)
        var locate = UiKit.Btn("", panel.transform, Locate);
        var lrt = (RectTransform)locate.transform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 0f);
        lrt.sizeDelta = new Vector2(38f, 38f);
        lrt.anchoredPosition = new Vector2(-26f, 26f);
        var pin = NewImage("Pin", locate.transform, UiKit.Accent);
        pin.sprite = BuildPinSprite();
        pin.rectTransform.sizeDelta = new Vector2(24f, 24f);
        locateButton = locate.gameObject;
        locateButton.SetActive(false);
    }

    // A zoom node filling the screen, its picture fitted inside at the picture's own aspect, and a "you are here" on it.
    RawImage BuildView(View v, string name, Texture texture, float aspect)
    {
        v.zoomRoot = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        v.zoomRoot.SetParent(panel.transform, false);
        Stretch(v.zoomRoot);

        var image = new GameObject("Picture", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage))
            .GetComponent<RawImage>();
        image.transform.SetParent(v.zoomRoot, false);
        image.texture = texture;
        image.raycastTarget = false;
        Stretch(image.rectTransform);
        var fit = image.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = aspect;
        v.picture = image.rectTransform;

        var mk = NewImage("YouAreHere", image.transform, new Color(1f, 0.92f, 0.25f, 1f));
        v.marker = mk.rectTransform;
        v.marker.anchorMin = v.marker.anchorMax = v.marker.pivot = new Vector2(0.5f, 0.5f);
        v.marker.sizeDelta = new Vector2(MarkerSize, MarkerSize);
        return image;
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

    // ── tiny uGUI helpers ─────────────────────────────────────────────────────────────────────────────────────────
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
        rt.sizeDelta = new Vector2(width, 34f); rt.anchoredPosition = new Vector2(0f, -fromTop);
    }
    static void Bottom(RectTransform rt, float fromBottom, float width)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(width, 20f); rt.anchoredPosition = new Vector2(0f, fromBottom);
    }
}
