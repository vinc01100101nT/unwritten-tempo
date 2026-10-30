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
/// Any other map shows its picture alone. Resting the pointer on a creature's name in the list beside it shows what
/// the server says about that creature (<see cref="MobInfo"/>) — the lines <c>/mobinfo</c> prints.</para>
///
/// <para><b>World view</b>: the baked <see cref="WorldAtlas"/> with every door between maps drawn on it
/// (<see cref="WorldMapDoors"/>) and a pulsing "you are here" over the map you are in.
/// Clicking a map that has a picture opens the zone view on it.</para>
///
/// <para>In both, the wheel zooms toward the cursor (over the zone view's list, when it is too long to show whole, it
/// scrolls the list instead) and a zoomed view drags with the mouse. The location button brings
/// the view back to you: in the world view it glides to your marker; in the zone view it opens your own map and
/// glides to you. Esc or M closes.</para>
///
/// <para>Self-installing: a runtime bootstrap spawns it into the persistent <c>Systems</c> scene the moment a
/// <see cref="WorldAtlas"/> exists in Resources, and it builds itself on <see cref="UiKit.HudCanvas"/>.</para>
/// </summary>
public class MapWindow : MonoBehaviour
{
    [Tooltip("How far in the wheel can zoom; 1 is the whole map filling the screen. A map drawn in square pictures " +
             "zooms further, until one pixel of a square picture is one pixel of the screen.")]
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
        // What is drawn this frame. The wheel and the zoom buttons move the goal; the drawn view eases to it.
        public float zoom = 1f;
        public Vector2 pan;
        public float goalZoom = 1f;
        public Vector2 goalPan;
    }

    WorldAtlas data;
    // The world view is the zone maps' picture of how they join up. A seamless world has no zone maps (the old ones were
    // deleted 2026-10-27), so there is no world view: M opens the map you are in, which for a seamless world is the
    // whole world.
    bool hasWorldView;
    GameObject switchButton;
    Canvas canvas;
    GameObject panel;
    static MapWindow shown;

    // THE ROOM THE MAP HAS: the screen left of the side panel, above the footer. Both views live in it and never run
    // under the panel; the zoom, the drag and the corner buttons are all measured in it.
    RectTransform viewport;
    // THE SIDE PANEL on the right: the map's name, where you stand, the two toggles and the mobs where you stand. Its
    // fold button narrows it to a strip and the map widens into the room it gave up.
    RectTransform sidePanel, mapArea;
    GameObject panelBody;
    TMP_Text foldLabel;
    bool folded, pointerOverPanel;
    float panelWidth = PanelWidth;
    const float ListWidth = PanelWidth - 1f;
    const float PanelWidth = 300f, FoldedWidth = 28f, FoldSeconds = 0.15f, FooterHeight = 28f;
    // How long the drawn view takes to ease to where the wheel sent it, in seconds.
    const float ZoomEaseSeconds = 0.07f;

    // THINGS THAT FADE instead of popping: the patches, the lit patch, the labels, the level ranges and the list beside
    // the pointer. Each eases its alpha to on or off, and is switched off once it has faded out.
    sealed class Fade
    {
        public CanvasGroup group;
        public float seconds, t;
        public bool on;
        public void Restart() { t = 0f; Apply(); }
        public void Step(float dt)
        {
            float to = on ? 1f : 0f;
            if (Mathf.Approximately(t, to) && group.gameObject.activeSelf == on) return;
            t = Mathf.MoveTowards(t, to, dt / seconds);
            Apply();
        }
        void Apply()
        {
            group.alpha = t * t * (3f - 2f * t);
            bool live = on || t > 0f;
            if (group.gameObject.activeSelf != live) group.gameObject.SetActive(live);
        }
    }
    readonly List<Fade> fades = new List<Fade>();
    Fade patchFade, patchHoverFade, labelFade, rangeFade, tipFade;
    const float FadeSeconds = 0.18f, TipFadeSeconds = 0.12f;

    Fade NewFade(GameObject on, float seconds)
    {
        var f = new Fade { group = on.AddComponent<CanvasGroup>(), seconds = seconds };
        f.group.blocksRaycasts = false;
        f.group.interactable = false;
        f.group.alpha = 0f;
        on.SetActive(false);
        fades.Add(f);
        return f;
    }

    // THE LEVEL RANGE OF EACH PATCH ("Lv 48–59"), over its middle while the patches are shown: the biggest patches first,
    // and a range that would sit on another is left out. A layer of its own under the labels, so showing or hiding the
    // labels never moves a range. Laid again whenever the view moves, from a list made once per map and picture size.
    RectTransform rangeRoot;
    readonly List<TextMeshProUGUI> rangeTexts = new List<TextMeshProUGUI>();
    readonly List<(Vector2 at, string text, float width)> rangeList = new List<(Vector2, string, float)>();
    readonly List<Rect> rangeTaken = new List<Rect>();
    string rangesListedMap;
    Vector2 rangesListedSize;
    float rangesDrawnZoom = -1f;
    Vector2 rangesDrawnPan, rangesDrawnRoom;
    // A range's words, and the room it keeps to itself: from RangeBelow under its middle to RangeAbove over it, and
    // RangeMargin either side of its words.
    const float RangeSize = 12f, RangeBelow = 7f, RangeAbove = 8f, RangeMargin = 2f;

    /// <summary>True while the map covers the screen — the tutorial's sentence steps aside for it.</summary>
    public static bool IsOpen => shown != null && shown.panel != null && shown.panel.activeSelf;
    RectTransform panelRect;
    readonly View world = new View(), zone = new View();
    View active;
    RawImage atlasImage, zoneImage;
    TMP_Text title, whereLabel, zoneMissing, switchLabel;
    Image switchIcon;   // a globe while the button leads to the world map, the folded map while it leads back
    Transform player;

    // The map the zone view shows, and the picture it holds for it (taken from, and given back to, MinimapCatalog).
    string zoneMap, zonePictureMap;
    MinimapCatalog.Entry zoneEntry;

    // THE SQUARE PICTURES OVER THE OVERVIEW. A tiled map's one overview picture is at most 2,048 pixels for the whole
    // world, so zoomed in a tree is a smudge of two or three pixels. Once a square picture would be drawn at half its own
    // pixels or more, the squares in view are laid over the overview, loaded TileLoadMilliseconds' worth a frame (the
    // overview shows until each one arrives), and given back as they leave the view or the zoom drops back under that
    // line. Measured 2026-10-30 in the editor: a square takes about 0.8 ms to load and holds 66 KB.
    MapTileLayer zoneTiles;
    readonly Vector3[] viewCorners = new Vector3[4];
    const float TileLoadMilliseconds = 2f;

    // The route's arrows in the zone view, laid when the route, the map or the picture's size changed.
    RectTransform routeRoot;
    readonly List<Image> routeMarks = new List<Image>();
    Sprite routeSprite;
    int routeDrawnVersion = -1;
    string routeDrawnMap;
    Vector2 routeDrawnSize;
    float routeDrawnZoom = -1f;

    // The doors of the map the zone view shows: a mark in the colour of the map each leads to, its name beside it.
    // Laid when the map or the picture's size changed, resized only when the zoom did. A click on one opens that map.
    // A mark on the zone picture with its name under it — a door out (leadsTo: where it goes) or a person (no leadsTo).
    sealed class DoorMark { public Image mark; public TMP_Text label; public string leadsTo; }
    RectTransform doorRoot;
    readonly List<DoorMark> doorMarks = new List<DoorMark>();
    int doorsShown;
    string doorsDrawnMap;
    Vector2 doorsDrawnSize;
    float doorsDrawnZoom = -1f;

    // THE RESPAWN PATCHES of the map the zone view shows (WorldSpawns' areas), laid over its picture while the toggle is
    // on: each patch filled in the colour of what lives there — green originals, purple Tainted, red Blighted — with its
    // edge darker. Painted into one small texture once per map, when first shown; nothing is redrawn while it is up.
    RawImage patchImage;
    GameObject patchButton, labelButton;
    TMP_Text patchLabel;
    bool showPatches;
    string patchesDrawnMap;
    Texture2D patchTexture;
    const float PatchUnitsPerPixel = 2f;
    const int PatchMaxPixels = 1024;
    // Which patch each of those pixels belongs to (index + 1, 0 = none), worked out once per map: what the paint, the
    // patch you stand on and the patch under the pointer are all read from.
    int[] patchOwner;
    int patchPixels;
    string patchOwnerMap;

    // THE PATCH UNDER THE POINTER in the zone view: lit in the accent colour, with what the world keeps on it beside the
    // pointer (never how many are alive). Repainted only when the pointer moves onto another patch.
    RawImage patchHoverImage;
    Texture2D patchHoverTexture;
    Color32[] patchHoverPixels;
    int hoverArea = -1;

    // The patch you stand on, whose mobs the zone view's list shows; NoPatchLaid until it is first laid out.
    int rosterArea = NoPatchLaid;
    const int NoPatchLaid = int.MinValue + 1;

    // THE WORLD'S PLACES on its picture — towns, landmarks, caves, dungeon doors (WorldPlaces), in the world layout's own
    // look, on and off with a button. Laid once per map and picture size, resized only when the zoom changes.
    RectTransform labelRoot;
    readonly List<RectTransform> labelParts = new List<RectTransform>();
    int labelsShown;
    bool showLabels = true;
    string labelsDrawnMap;
    Vector2 labelsDrawnSize;
    float labelsDrawnZoom = -1f;
    TMP_Text labelLabel;

    // The spot picked to be led to (MapGuide), as a pin on the zone view of its map.
    Image guidePin;
    int guideDrawnVersion = -1;
    string guideDrawnMap;
    Vector2 guideDrawnSize;
    float guideDrawnZoom = -1f;
    Sprite pinSprite;

    bool gliding;
    float glideT;
    Vector2 glideFrom, glideTo;

    // Who lives in a map: on the right of the zone view for the map it shows, and beside the pointer in the world view
    // for the map under it. Each is rebuilt only when the map it shows changes.
    RosterCard zoneRoster, hoverRoster;
    string hoverMap;
    bool pointerOverMap;
    Vector2 pointer, hoverAskedAt;
    float hoverAskedZoom;
    Vector2 hoverAskedPan;
    bool hoverAsked;

    // The creature card of the zone view: which creature's name the pointer rests on, since when, and what is drawn.
    RectTransform mobCard;
    TMP_Text mobCardText;
    Image mobCardFace;
    MonsterDef cardMob;
    float cardRestedSince;
    string cardShownText;
    Vector2 cardPlacedAt, cardLookedAt;
    bool cardLooked;
    // How long the pointer rests on a name before its card is asked for and shown, in seconds.
    const float MobCardRest = 0.15f;
    const float MobCardWidth = 380f, MobCardPad = 10f, MobCardFaceSize = 38f, MobCardFaceGap = 8f;

    // Sizes on the main screen layer, which is laid out as if every screen were 1280 wide.
    static readonly Color YouAreHereColour = new Color(1f, 0.92f, 0.25f, 1f);
    const float MarkerSize = 11f, RouteMarkSize = 14f, DoorMarkSize = 12f, DoorLabelSize = 11f, GuidePinSize = 22f;
    // A townsperson's dot, and the zoom from which their names show — at the whole-map view sixteen names would bury
    // the town under text, so the dots stand alone until the player zooms in to read them.
    const float MarkLabelHeight = 20f;
    const float PersonMarkSize = 8f, PersonIconSize = 16f, PersonLabelSize = 10f, PersonNamesFromZoom = 1.8f;

    RectTransform peopleRoot;
    readonly List<DoorMark> personMarks = new List<DoorMark>();
    readonly List<WorldTownsfolk.Row> peopleOnMap = new List<WorldTownsfolk.Row>();
    int peopleShown;
    string peopleDrawnMap;
    Vector2 peopleDrawnSize;
    float peopleDrawnZoom = -1f;

    void Start()
    {
        data = Resources.Load<WorldAtlas>("WorldAtlas");
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<WorldAtlas>();
            data.hideFlags = HideFlags.DontSave;
        }
        hasWorldView = data.atlas != null && data.maps != null && data.maps.Count > 0;
        shown = this;
        canvas = UiKit.HudCanvas;
        // A server draws nothing.
        if (canvas == null || (GameServer.IsServer && !GameNetwork.TheWorldIsElsewhere))
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
        zoneTiles?.Clear();
        if (patchTexture != null) Destroy(patchTexture);
        if (patchHoverTexture != null) Destroy(patchHoverTexture);
        if (panel != null) Destroy(panel);
    }

    void Update()
    {
        if (Hotkeys.Down(GameKeys.Map)) Toggle();
        if (panel == null || !panel.activeSelf) return;
        if (Hotkeys.CancelDown()) { Close(); Hotkeys.MarkCancelHandled(); return; }
        if (player == null && PersistentPlayer3D.Instance != null) player = PersistentPlayer3D.Instance.transform;

        StepFold();
        StepGlide();
        StepZoom(active);
        if (active == world) { UpdateWorldMarker(); UpdateHover(); UpdateTrail(); }
        else { UpdateZoneTiles(); UpdateZone(); UpdatePatchHover(); UpdateMobCard(); DrawRanges(); }
        zoneRoster.LightRow(pointer, pointerOverPanel);
        float dt = Time.unscaledDeltaTime;
        foreach (var f in fades) f.Step(dt);
        zoneRoster.StepRows(dt);
        hoverRoster.StepRows(dt);
    }

    // The panel narrows to its strip or widens back over FoldSeconds; the map's room follows it.
    void ToggleFold()
    {
        UISounds.PlayClick();
        folded = !folded;
        foldLabel.text = folded ? "‹" : "›";
        panelBody.SetActive(!folded);
    }

    void StepFold()
    {
        float want = folded ? FoldedWidth : PanelWidth;
        if (Mathf.Approximately(panelWidth, want)) return;
        panelWidth = Mathf.MoveTowards(panelWidth, want, (PanelWidth - FoldedWidth) * Time.unscaledDeltaTime / FoldSeconds);
        LayRoom();
    }

    void LayRoom()
    {
        sidePanel.sizeDelta = new Vector2(panelWidth, 0f);
        mapArea.offsetMax = new Vector2(-panelWidth, 0f);
        Reclamp(world);
        Reclamp(zone);
    }

    void Reclamp(View v)
    {
        v.goalPan = ClampPan(v, v.goalPan, v.goalZoom);
        v.pan = ClampPan(v, v.pan, v.zoom);
        ApplyView(v);
    }

    // THE DRAWN VIEW EASES TO ITS GOAL — the zoom on a log scale and the map point at the middle of the room in a straight
    // line, so nothing swims sideways while it zooms.
    void StepZoom(View v)
    {
        if (gliding) { v.goalZoom = v.zoom; v.goalPan = v.pan; return; }
        if (Mathf.Abs(v.zoom - v.goalZoom) < 1e-3f && (v.pan - v.goalPan).sqrMagnitude < 0.09f)
        {
            if (v.zoom == v.goalZoom && v.pan == v.goalPan) return;
            v.zoom = v.goalZoom;
            v.pan = v.goalPan;
            ApplyView(v);
            return;
        }
        float k = 1f - Mathf.Exp(-Mathf.Min(0.05f, Time.unscaledDeltaTime) / ZoomEaseSeconds);
        float z = Mathf.Exp(Mathf.Lerp(Mathf.Log(v.zoom), Mathf.Log(v.goalZoom), k));
        Vector2 middle = Vector2.Lerp(-v.pan / v.zoom, -v.goalPan / v.goalZoom, k);
        v.zoom = z;
        v.pan = -middle * z;
        ApplyView(v);
    }

    void Toggle()
    {
        if (panel.activeSelf) { Close(); return; }
        panel.SetActive(true);
        UiLayers.Raise(panel.transform, UiLayers.Layer.Window);
        LayRoom();
        ResetView(world);
        ShowZone(MapManager.CurrentMap);
    }

    void Close()
    {
        panel.SetActive(false);
        gliding = false;
        HideHover();
        HideMobCard();
        // A closed window holds no picture.
        MinimapCatalog.ReleasePicture(zonePictureMap);
        zonePictureMap = null;
        zoneImage.texture = null;
        zoneTiles.Clear();
    }

    // ── the two views ─────────────────────────────────────────────────────────────────────────────────────────────

    void ShowWorld()
    {
        Activate(world);
        title.text = "WORLD MAP";
        switchLabel.text = "Zone map";
        if (switchIcon != null) switchIcon.sprite = UiIcons.Get("Map");
        UpdateWorldMarker();
    }

    // The zone view, on `map`. The picture is taken from the one owner of map pictures; a map nobody has drawn says so.
    void ShowZone(string map)
    {
        Activate(zone);
        switchLabel.text = "World map";
        if (switchIcon != null) switchIcon.sprite = UiIcons.Get("WorldMap");
        title.text = map != null ? data.NameOf(map) : "";
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
            doorsDrawnMap = null;
            guideDrawnVersion = -1;
            rosterArea = NoPatchLaid;
            HideMobCard();
        }
        RefreshPatches();
        UpdateZone();
    }

    void Activate(View v)
    {
        active = v;
        gliding = false;
        world.zoomRoot.gameObject.SetActive(v == world);
        zone.zoomRoot.gameObject.SetActive(v == zone);
        zoneRoster.Root.gameObject.SetActive(v == zone);
        if (v != world) HideHover();
        HidePatchHover();
        if (v != zone) HideMobCard();
        if (patchButton != null) patchButton.SetActive(v == zone);
        if (labelButton != null) labelButton.SetActive(v == zone);
    }

    void TogglePatches()
    {
        UISounds.PlayClick();
        showPatches = !showPatches;
        RefreshPatches();
    }

    // Show or hide the patches over the zone picture, painting them the first time a map's are shown.
    void RefreshPatches()
    {
        patchLabel.text = showPatches ? "Hide respawn patches" : "Show respawn patches";
        bool on = showPatches && zoneEntry != null && zoneImage.enabled;
        if (on && patchesDrawnMap != zoneMap)
        {
            patchesDrawnMap = zoneMap;
            if (patchTexture != null) Destroy(patchTexture);
            patchTexture = EnsurePatchOwners() ? PaintPatches(zoneMap, patchOwner, patchPixels) : null;
            patchImage.texture = patchTexture;
        }
        patchFade.on = rangeFade.on = on && patchTexture != null;
        rangesDrawnZoom = -1f;
    }

    // The patch owners of the map the zone view shows, worked out the first time they are asked for; false when it has
    // no picture or no patches.
    bool EnsurePatchOwners()
    {
        if (zoneEntry == null) return false;
        if (patchOwnerMap != zoneMap)
        {
            patchOwnerMap = zoneMap;
            patchOwner = PatchOwners(zoneMap, zoneEntry.worldMin, zoneEntry.worldSize, out patchPixels);
        }
        return patchOwner != null;
    }

    /// <summary>The patch (its index in WorldSpawns' areas) holding <paramref name="inMap"/> — the map's own x east, y
    /// north — in the map the zone view shows; -1 on ground that is no patch.</summary>
    int PatchAt(Vector2 inMap)
    {
        if (!EnsurePatchOwners()) return -1;
        float perPixel = zoneEntry.worldSize / patchPixels;
        int x = Mathf.FloorToInt((inMap.x - zoneEntry.worldMin.x) / perPixel), z = Mathf.FloorToInt((inMap.y - zoneEntry.worldMin.y) / perPixel);
        if (x < 0 || z < 0 || x >= patchPixels || z >= patchPixels) return -1;
        return patchOwner[z * patchPixels + x] - 1;
    }

    // One pixel per PatchUnitsPerPixel of the picture's square of the map, holding the patch it belongs to (index + 1,
    // 0 = none); null when the map has no patches.
    static int[] PatchOwners(string map, Vector2 worldMin, float worldSize, out int px)
    {
        px = Mathf.Clamp(Mathf.CeilToInt(worldSize / PatchUnitsPerPixel), 1, PatchMaxPixels);
        var spawns = WorldSpawns.Instance;
        if (spawns == null || spawns.areas.Count == 0) return null;
        float perPixel = worldSize / px;
        var owner = new int[px * px];
        var laid = new bool[spawns.areas.Count];
        bool any = false;
        foreach (var row in spawns.rows)
        {
            if (row == null || row.map != map || row.area < 0 || row.area >= spawns.areas.Count || laid[row.area]) continue;
            laid[row.area] = true;
            int[] runs = spawns.areas[row.area].runs;
            for (int k = 0; k + 2 < runs.Length; k += 3)
                for (int n = 0; n < runs[k + 2]; n++)
                {
                    float x0 = runs[k] + n * WorldSpawns.AreaSquare - worldMin.x, z0 = runs[k + 1] - worldMin.y;
                    int ax = Mathf.Max(0, Mathf.FloorToInt(x0 / perPixel)), bx = Mathf.Min(px, Mathf.CeilToInt((x0 + WorldSpawns.AreaSquare) / perPixel));
                    int az = Mathf.Max(0, Mathf.FloorToInt(z0 / perPixel)), bz = Mathf.Min(px, Mathf.CeilToInt((z0 + WorldSpawns.AreaSquare) / perPixel));
                    for (int z = az; z < bz; z++) for (int x = ax; x < bx; x++) { owner[z * px + x] = row.area + 1; any = true; }
                }
        }
        return any ? owner : null;
    }

    // Each patch filled in the colour of its first row's creature — every resident of a patch is the same version —
    // with its edge darker.
    static Texture2D PaintPatches(string map, int[] owner, int px)
    {
        var spawns = WorldSpawns.Instance;
        var colour = new Color[spawns.areas.Count];
        var coloured = new bool[spawns.areas.Count];
        foreach (var row in spawns.rows)
        {
            if (row == null || row.map != map || row.area < 0 || row.area >= spawns.areas.Count || coloured[row.area]) continue;
            coloured[row.area] = true;
            colour[row.area] = VersionColour(MonsterCatalog.Get(row.creature));
        }
        var pixels = new Color32[px * px];
        bool any = false;
        for (int z = 0; z < px; z++)
            for (int x = 0; x < px; x++)
            {
                int o = owner[z * px + x];
                if (o == 0) continue;
                any = true;
                bool edge = (x > 0 && owner[z * px + x - 1] != o) || (x < px - 1 && owner[z * px + x + 1] != o)
                         || (z > 0 && owner[(z - 1) * px + x] != o) || (z < px - 1 && owner[(z + 1) * px + x] != o);
                var c = colour[o - 1];
                pixels[z * px + x] = edge ? new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, 0.85f) : new Color(c.r, c.g, c.b, 0.35f);
            }
        if (!any) return null;
        var tex = new Texture2D(px, px, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    // An original green, a Tainted version purple, a Blighted one red: of the two versions of one creature, the one
    // with the higher multiplier is the Blighted.
    static Color VersionColour(MonsterDef def)
    {
        if (def == null || def.versionOf == null) return UiKit.Good.Value;
        foreach (var other in MonsterCatalog.All)
            if (other != null && other != def && other.versionOf == def.versionOf && other.multiplier > def.multiplier)
                return UiKit.RarityEpic.Value;
        return UiKit.Bad.Value;
    }

    void SwitchView()
    {
        UISounds.PlayClick();
        if (active == world) ShowZone(zoneMap ?? MapManager.CurrentMap); else if (hasWorldView) ShowWorld();
    }

    // ── world view: the marker over the map you are in ────────────────────────────────────────────────────────────
    void UpdateWorldMarker()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (whereLabel && scene != whereShownFor) { whereShownFor = scene; whereLabel.text = YouAreIn(scene); }
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

    // The map under a point on the screen, in the world view; null over empty space.
    WorldAtlas.Entry MapUnder(Vector2 screenPoint)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(world.picture, screenPoint, null, out Vector2 local)) return null;
        Vector2 size = world.picture.rect.size;
        float u = local.x / Mathf.Max(1f, size.x) + 0.5f, vFromTop = 0.5f - local.y / Mathf.Max(1f, size.y);
        float wx = data.minX + u * data.worldW, wy = data.minY + vFromTop * data.worldH;
        foreach (var e in data.maps)
            if (e != null && wx >= e.x && wx <= e.x + e.w && wy >= e.y && wy <= e.y + e.h) return e;
        return null;
    }

    // A click on the world view opens the zone view on the map under the cursor — only a map that has a picture.
    void PickZone(Vector2 screenPoint)
    {
        var e = MapUnder(screenPoint);
        var catalog = MinimapCatalog.Instance;
        if (e == null || catalog == null || catalog.Find(e.scene) == null) return;
        UISounds.PlayClick();
        ShowZone(e.scene);
    }

    // ── world view: who lives in the map under the pointer ────────────────────────────────────────────────────────
    // The map under the pointer is looked up again only when the pointer, the zoom or the drag moved.
    void UpdateHover()
    {
        if (!pointerOverMap) { HideHover(); return; }
        if (hoverAsked && pointer == hoverAskedAt && Mathf.Approximately(world.zoom, hoverAskedZoom) && world.pan == hoverAskedPan)
            return;
        hoverAsked = true;
        hoverAskedAt = pointer;
        hoverAskedZoom = world.zoom;
        hoverAskedPan = world.pan;

        var e = MapUnder(pointer);
        string map = e != null ? e.scene : null;
        if (map != hoverMap)
        {
            hoverMap = map;
            if (map != null) hoverRoster.Show(map, map, WholeMap, panelRect.rect.height - 2f * HoverEdge, TipMinWidth, RosterMaxWidth);
            tipFade.on = map != null && hoverRoster.HasPeople;
        }
        if (tipFade.on) PlaceBesidePointer(hoverRoster.Root, pointer);
    }

    void HideHover()
    {
        hoverMap = null;
        hoverAsked = false;
        if (tipFade != null) tipFade.on = false;
        HidePatchHover();
    }

    // Below and to the right of the pointer, flipped to the other side when that would run off the screen.
    void PlaceBesidePointer(RectTransform card, Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(panelRect, screenPoint, null, out Vector2 at);
        Vector2 half = panelRect.rect.size * 0.5f, size = card.sizeDelta;
        bool left = at.x + HoverGap + size.x > half.x - HoverEdge;
        bool up = at.y - HoverGap - size.y < -half.y + HoverEdge;
        card.pivot = new Vector2(left ? 1f : 0f, up ? 0f : 1f);
        float x = at.x + (left ? -HoverGap : HoverGap), y = at.y + (up ? HoverGap : -HoverGap);
        x = left ? Mathf.Max(x, -half.x + HoverEdge + size.x) : Mathf.Min(x, half.x - HoverEdge - size.x);
        y = up ? Mathf.Min(y, half.y - HoverEdge - size.y) : Mathf.Max(y, -half.y + HoverEdge + size.y);
        card.anchoredPosition = new Vector2(x, y);
    }

    // ── zone view: what the server says about the creature whose name the pointer rests on ────────────────────────
    // The name under the pointer is looked up again only when the pointer moved. The text is the server's, shown as it
    // arrived; this machine works nothing out.
    void UpdateMobCard()
    {
        if (!cardLooked || pointer != cardLookedAt)
        {
            cardLooked = true;
            cardLookedAt = pointer;
            var under = pointerOverPanel ? zoneRoster.MobUnder(pointer) : null;
            if (under != cardMob)
            {
                cardMob = under;
                cardRestedSince = Time.unscaledTime;
                cardShownText = null;
                if (mobCard.gameObject.activeSelf) mobCard.gameObject.SetActive(false);
            }
        }
        if (cardMob == null || Time.unscaledTime - cardRestedSince < MobCardRest) return;

        if (!MobInfo.TryGetSent(cardMob.id, out string text)) { MobInfo.Ask(cardMob.id); text = "…"; }
        bool rewritten = !ReferenceEquals(text, cardShownText);
        if (rewritten)
        {
            cardShownText = text;
            mobCardText.text = text;
            // The creature's face sits left of the text; a creature with none gives the text the whole width.
            var face = CreaturePortraits.Of(cardMob);
            mobCardFace.sprite = face;
            mobCardFace.gameObject.SetActive(face != null);
            float beside = face != null ? MobCardFaceSize + MobCardFaceGap : 0f;
            float inner = MobCardWidth - 2f * MobCardPad - beside;
            float height = mobCardText.GetPreferredValues(text, inner, 0f).y;
            mobCardText.rectTransform.anchoredPosition = new Vector2(MobCardPad + beside, -MobCardPad);
            mobCardText.rectTransform.sizeDelta = new Vector2(inner, height);
            mobCard.sizeDelta = new Vector2(MobCardWidth, Mathf.Max(height, face != null ? MobCardFaceSize : 0f) + 2f * MobCardPad);
        }
        if (!mobCard.gameObject.activeSelf) mobCard.gameObject.SetActive(true);
        if (rewritten || pointer != cardPlacedAt)
        {
            cardPlacedAt = pointer;
            PlaceBesidePointer(mobCard, pointer);
        }
    }

    void HideMobCard()
    {
        cardMob = null;
        cardLooked = false;
        cardShownText = null;
        if (mobCard != null && mobCard.gameObject.activeSelf) mobCard.gameObject.SetActive(false);
    }

    // ── zone view: you, and the route, on the map you stand in ────────────────────────────────────────────────────
    string whereShownFor = "";
    int whereX, whereY, whereZ;
    // Passed as the patch you stand on while the map shown is another one.
    const int NoPatchHere = -2;
    string YouAreIn(string map) => "You are in " + data.NameOf(map);

    bool ShowingOwnMap => zoneMap == MapManager.CurrentMap && zoneEntry != null;

    void UpdateZone()
    {
        // Where YOU are: your position on your own map, the map you are in on another one.
        bool own = ShowingOwnMap && player != null;
        if (own)
        {
            Vector3 p = Coords.ToShown(player.position);
            int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y), z = Mathf.RoundToInt(p.z);
            if (whereShownFor != null || x != whereX || y != whereY || z != whereZ)
            {
                whereShownFor = null; whereX = x; whereY = y; whereZ = z;
                whereLabel.text = Coords.Shown(x, y, z);
            }
        }
        else if (MapManager.CurrentMap != whereShownFor)
        {
            whereShownFor = MapManager.CurrentMap;
            whereLabel.text = whereShownFor == null ? "" : YouAreIn(whereShownFor);
        }
        zone.marker.gameObject.SetActive(own);
        if (own)
        {
            zone.marker.anchoredPosition = OnZonePicture(player.position);
            Pulse(zone);
        }
        DrawRoute();
        DrawDoors();
        DrawPeople();
        DrawGuidePin();
        DrawPlaceLabels();
        // The panel's list is the mobs of the patch you stand on, laid out again when you step onto another or the
        // screen changed size; on ground that is no patch it says so, and on another map it is empty.
        int here = own ? PatchAt(InMapOnPicture(player.position)) : NoPatchHere;
        float rosterRoom = listRoom.rect.height;
        if (here != rosterArea || !Mathf.Approximately(rosterRoom, zoneRoster.LaidForHeight))
        {
            rosterArea = here;
            if (here >= 0) zoneRoster.Show("Mobs", zoneMap, here, rosterRoom, ListWidth, ListWidth);
            else if (here == -1) zoneRoster.ShowNone("Mobs", "No mobs here", rosterRoom, ListWidth);
            else zoneRoster.Clear(rosterRoom, ListWidth);
            HideMobCard();
        }
    }

    // A spot on the ground of the map the zone view shows, in that map's own x east, y north.
    Vector2 InMapOnPicture(Vector3 worldPos)
    {
        Vector3 p = MapManager.InMap(worldPos, zoneMap);
        return new Vector2(p.x, p.z);
    }

    // ── zone view: the patch under the pointer, and what the world keeps on it ───────────────────────────────────────
    void UpdatePatchHover()
    {
        int area = -1;
        if (pointerOverMap && zoneEntry != null && zoneImage.enabled && !zoneRoster.Contains(pointer)
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(zone.picture, pointer, null, out Vector2 local))
        {
            Vector2 size = zone.picture.rect.size;
            if (Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f)
                area = PatchAt(PictureToMap(local, zoneEntry.worldMin, zoneEntry.worldSize, size));
        }
        if (area != hoverArea)
        {
            hoverArea = area;
            PaintHoveredPatch();
            if (area >= 0) hoverRoster.Show(null, zoneMap, area, panelRect.rect.height - 2f * HoverEdge, TipMinWidth, RosterMaxWidth);
            tipFade.on = area >= 0 && hoverRoster.HasPeople;
        }
        if (tipFade.on) PlaceBesidePointer(hoverRoster.Root, pointer);
    }

    // The patch under the pointer as one point-filtered texture over the picture: filled in the accent colour, its edge
    // solid — the same pixels the patches are painted with.
    void PaintHoveredPatch()
    {
        bool on = hoverArea >= 0 && patchOwner != null && patchOwnerMap == zoneMap;
        patchHoverFade.on = on;
        if (!on) return;
        patchHoverFade.Restart();
        int px = patchPixels, mark = hoverArea + 1;
        if (patchHoverTexture == null || patchHoverTexture.width != px)
        {
            if (patchHoverTexture != null) Destroy(patchHoverTexture);
            patchHoverTexture = new Texture2D(px, px, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            patchHoverPixels = new Color32[px * px];
            patchHoverImage.texture = patchHoverTexture;
        }
        Color accent = UiKit.Accent.Value;
        Color32 fill = new Color(accent.r, accent.g, accent.b, 0.45f), edge = accent, none = default;
        for (int z = 0; z < px; z++)
            for (int x = 0; x < px; x++)
            {
                int i = z * px + x;
                if (patchOwner[i] != mark) { patchHoverPixels[i] = none; continue; }
                bool rim = (x > 0 && patchOwner[i - 1] != mark) || (x < px - 1 && patchOwner[i + 1] != mark)
                        || (z > 0 && patchOwner[i - px] != mark) || (z < px - 1 && patchOwner[i + px] != mark);
                patchHoverPixels[i] = rim ? edge : fill;
            }
        patchHoverTexture.SetPixels32(patchHoverPixels);
        patchHoverTexture.Apply(false, false);
    }

    void HidePatchHover()
    {
        hoverArea = -1;
        if (patchHoverFade != null) patchHoverFade.on = false;
        if (tipFade != null && hoverMap == null) tipFade.on = false;
    }

    // ── zone view: the world's places, in the world layout's own look ───────────────────────────────────────────────
    // Sizes as the world layout draws them, on the main screen layer.
    const float TownRingSize = 27f, TownDotSize = 21f, TownNameSize = 17f, TownNameRise = 18f;
    const float StarSize = 24f, StarDrop = 8f, LandmarkNameSize = 13f, LandmarkNameDrop = 25f;
    const float CaveWidth = 22f, CaveHeight = 19f, CaveEdge = 1f, CaveRise = -0.5f, CaveNameSize = 12f, CaveNameDrop = 24f, DoorGlyphSize = 20f, DoorDrop = 7f;
    // The world layout's label colours.
    static readonly Color PlaceNameColour = new Color(0.067f, 0.067f, 0.067f), PlaceEdgeColour = new Color(1f, 1f, 1f),
        TownRingColour = new Color(0.133f, 0.133f, 0.133f), LandmarkNameColour = new Color(0.114f, 0.114f, 0.114f),
        StarColour = new Color(1f, 0.827f, 0.302f), StarEdgeColour = new Color(0.353f, 0.239f, 0f),
        CaveColour = new Color(0.114f, 0.09f, 0.071f), CaveEdgeColour = new Color(0.91f, 0.878f, 0.816f),
        CaveNameEdgeColour = new Color(0f, 0f, 0f), DoorColour = new Color(0.165f, 0.102f, 0.039f);
    // How wide each word's outline is, in the text package's own measure.
    const float NameEdge = 0.2f, ThinEdge = 0.1f;
    const int RoundestCorner = 14;

    void ToggleLabels()
    {
        UISounds.PlayClick();
        showLabels = !showLabels;
        labelLabel.text = showLabels ? "Hide map labels" : "Show map labels";
        labelFade.on = showLabels;
    }

    void DrawPlaceLabels()
    {
        Vector2 size = zone.picture.rect.size;
        if (labelsDrawnMap != zoneMap || labelsDrawnSize != size)
        {
            labelsDrawnMap = zoneMap;
            labelsDrawnSize = size;
            labelsDrawnZoom = -1f;
            int used = 0;
            if (zoneEntry != null && zoneImage.enabled && zoneMap == WorldPlaces.Map)
                foreach (var place in WorldPlaces.All)
                {
                    Vector2 at = MapToPicture(new Vector2(place.mark.x, place.mark.z), zoneEntry.worldMin, zoneEntry.worldSize, size);
                    if (Mathf.Abs(at.x) > size.x * 0.5f || Mathf.Abs(at.y) > size.y * 0.5f) continue;
                    switch (place.kind)
                    {
                        case WorldPlaces.Kind.Town:
                            Shape(ref used, at, Vector2.zero, TownRingSize, TownRingSize, TownRingColour);
                            Shape(ref used, at, Vector2.zero, TownDotSize, TownDotSize, PlaceEdgeColour);
                            Word(ref used, at, new Vector2(0f, TownNameRise), place.name, TownNameSize, true, PlaceNameColour, PlaceEdgeColour, NameEdge);
                            break;
                        case WorldPlaces.Kind.Landmark:
                            Word(ref used, at, new Vector2(0f, -StarDrop), "★", StarSize, false, StarColour, StarEdgeColour, ThinEdge);
                            Word(ref used, at, new Vector2(0f, -LandmarkNameDrop), place.name, LandmarkNameSize, false, LandmarkNameColour, PlaceEdgeColour, NameEdge);
                            break;
                        case WorldPlaces.Kind.Cave:
                            Shape(ref used, at, new Vector2(0f, CaveRise), CaveWidth + CaveEdge * 2f, CaveHeight + CaveEdge * 2f, CaveEdgeColour);
                            Shape(ref used, at, new Vector2(0f, CaveRise), CaveWidth - CaveEdge * 2f, CaveHeight - CaveEdge * 2f, CaveColour);
                            Word(ref used, at, new Vector2(0f, -CaveNameDrop), "cave", CaveNameSize, false, PlaceEdgeColour, CaveNameEdgeColour, ThinEdge);
                            break;
                        case WorldPlaces.Kind.DungeonDoor:
                            Word(ref used, at, new Vector2(0f, -DoorDrop), "▼", DoorGlyphSize, false, DoorColour, PlaceEdgeColour, ThinEdge);
                            break;
                    }
                }
            for (int i = used; i < labelParts.Count; i++)
                if (labelParts[i].gameObject.activeSelf) labelParts[i].gameObject.SetActive(false);
            labelsShown = used;
        }
        if (!Mathf.Approximately(labelsDrawnZoom, zone.zoom))
        {
            labelsDrawnZoom = zone.zoom;
            var sc = new Vector3(1f / zone.zoom, 1f / zone.zoom, 1f);
            for (int i = 0; i < labelsShown; i++) labelParts[i].localScale = sc;
        }
    }

    // ── zone view: the level range of each patch ──────────────────────────────────────────────────────────────────
    // Laid only while the layer is up, again whenever the view moved: the biggest patches first, each on the room's
    // screen only, and none on another.
    void DrawRanges()
    {
        if (!rangeRoot.gameObject.activeSelf) return;
        Vector2 size = zone.picture.rect.size;
        if (rangesListedMap != zoneMap || rangesListedSize != size) ListRanges(size);
        Vector2 room = viewport.rect.size;
        if (Mathf.Approximately(rangesDrawnZoom, zone.zoom) && rangesDrawnPan == zone.pan && rangesDrawnRoom == room) return;
        rangesDrawnZoom = zone.zoom;
        rangesDrawnPan = zone.pan;
        rangesDrawnRoom = room;
        rangeTaken.Clear();
        var scale = new Vector3(1f / zone.zoom, 1f / zone.zoom, 1f);
        var edge = UiKit.OutlinedMaterial(UiKit.Font, NameEdge, UiKit.PanelFill.Value, outside: true);
        int used = 0;
        foreach (var (at, text, width) in rangeList)
        {
            Vector2 s = at * zone.zoom + zone.pan;
            if (Mathf.Abs(s.x) > room.x * 0.5f || Mathf.Abs(s.y) > room.y * 0.5f) continue;
            var box = new Rect(s.x - width * 0.5f - RangeMargin, s.y - RangeBelow, width + 2f * RangeMargin, RangeBelow + RangeAbove);
            bool free = true;
            foreach (var taken in rangeTaken) if (taken.Overlaps(box)) { free = false; break; }
            if (!free) continue;
            rangeTaken.Add(box);
            var t = RangeText(used++);
            if (t.fontSharedMaterial != edge) t.fontSharedMaterial = edge;
            t.text = text;
            t.rectTransform.anchoredPosition = at;
            t.rectTransform.sizeDelta = new Vector2(width + 8f, RangeSize * 1.6f);
            t.rectTransform.localScale = scale;
        }
        for (int i = used; i < rangeTexts.Count; i++)
            if (rangeTexts[i].gameObject.activeSelf) rangeTexts[i].gameObject.SetActive(false);
    }

    // Every patch of the map the zone view shows: where its middle lies on the picture, its range as words and their
    // width — biggest patch first. Made once per map and picture size.
    void ListRanges(Vector2 size)
    {
        rangesListedMap = zoneMap;
        rangesListedSize = size;
        rangesDrawnZoom = -1f;
        rangeList.Clear();
        var spawns = WorldSpawns.Instance;
        if (spawns == null || zoneEntry == null) return;
        var listed = new bool[spawns.areas.Count];
        var squares = new List<(int squares, int area)>();
        foreach (var row in spawns.rows)
        {
            if (row == null || row.map != zoneMap || row.area < 0 || row.area >= listed.Length || listed[row.area]) continue;
            listed[row.area] = true;
            int[] runs = spawns.areas[row.area].runs;
            int n = 0;
            for (int k = 2; k < runs.Length; k += 3) n += runs[k];
            squares.Add((n, row.area));
        }
        squares.Sort((a, b) => a.squares != b.squares ? b.squares.CompareTo(a.squares) : a.area.CompareTo(b.area));
        var probe = RangeText(0);
        foreach (var (_, area) in squares)
        {
            var a = spawns.areas[area];
            string text = a.lowLevel == a.highLevel ? "Lv " + a.lowLevel : "Lv " + a.lowLevel + "–" + a.highLevel;
            Vector2 at = MapToPicture(new Vector2(a.center.x, a.center.z), zoneEntry.worldMin, zoneEntry.worldSize, size);
            // a patch the picture does not reach gets no range, as it gets no paint
            if (Mathf.Abs(at.x) > size.x * 0.5f || Mathf.Abs(at.y) > size.y * 0.5f) continue;
            rangeList.Add((at, text, probe.GetPreferredValues(text).x));
        }
        probe.gameObject.SetActive(false);
    }

    TextMeshProUGUI RangeText(int i)
    {
        while (rangeTexts.Count <= i)
        {
            var t = (TextMeshProUGUI)UiKit.MakeText("Range", rangeRoot, "", RangeSize, FontStyle.Normal, TextAnchor.MiddleCenter);
            UiKit.Paint(t, UiKit.TextMain);
            t.raycastTarget = false;
            t.richText = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rangeTexts.Add(t);
        }
        var text = rangeTexts[i];
        if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
        return text;
    }

    // One round mark of a label: its middle `offset` (screen units, before the zoom) from the place.
    void Shape(ref int used, Vector2 at, Vector2 offset, float width, float height, Color colour)
    {
        var img = Part<Image>(ref used, "Mark");
        // The theme's largest corner, which rounds anything up to twice its size into a circle; a bigger mark has its
        // corners drawn larger to stay round.
        UiKit.Round(img, RoundestCorner);
        img.pixelsPerUnitMultiplier = Mathf.Min(1f, 2f * RoundestCorner / Mathf.Min(width, height));
        UiKit.Paint(img, colour);
        var rt = img.rectTransform;
        rt.pivot = new Vector2(0.5f - offset.x / width, 0.5f - offset.y / height);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = at;
    }

    // One word of a label in the labels' face, outlined, its baseline `offset` (screen units, before the zoom) from the
    // place — the way the world layout writes its words.
    void Word(ref int used, Vector2 at, Vector2 offset, string text, float size, bool bold, Color colour, Color edge, float edgeWidth)
    {
        var t = Part<TextMeshProUGUI>(ref used, "Word");
        t.font = UiKit.LabelFont;
        t.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.LabelFont, edgeWidth, edge, outside: true);
        t.fontSize = size;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.alignment = TextAlignmentOptions.Baseline;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.color = colour;
        t.text = text;
        var rt = t.rectTransform;
        rt.sizeDelta = new Vector2(t.GetPreferredValues(text).x + 8f, size * 1.6f);
        rt.pivot = new Vector2(0.5f, 0.5f - offset.y / rt.sizeDelta.y);
        rt.anchoredPosition = at;
    }

    // The next free piece of the labels, made the first time it is needed; anything else on the slot is a bug.
    T Part<T>(ref int used, string name) where T : Graphic
    {
        T part = used < labelParts.Count ? labelParts[used].GetComponent<T>() : null;
        if (part == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
            go.transform.SetParent(labelRoot, false);
            part = go.GetComponent<T>();
            part.raycastTarget = false;
            var rt = part.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            if (used < labelParts.Count) { Destroy(labelParts[used].gameObject); labelParts[used] = rt; }
            else labelParts.Add(rt);
        }
        if (!part.gameObject.activeSelf) part.gameObject.SetActive(true);
        part.transform.SetSiblingIndex(used);
        used++;
        return part;
    }

    // A spot on the ground of the map you stand in → a point on the zone picture, from its centre.
    Vector2 OnZonePicture(Vector3 worldPos)
    {
        Vector3 p = MapManager.InMap(worldPos, zoneMap);
        return MapToPicture(new Vector2(p.x, p.z), zoneEntry.worldMin, zoneEntry.worldSize, zone.picture.rect.size);
    }

    /// <summary>A spot in a map's own coordinates (x east, y north) → a point on its zone picture, from the picture's
    /// centre. <paramref name="worldMin"/> and <paramref name="worldSize"/> are the square of ground the picture covers.</summary>
    static Vector2 MapToPicture(Vector2 inMap, Vector2 worldMin, float worldSize, Vector2 pictureSize)
    {
        float size = Mathf.Max(1f, worldSize);
        float u = (inMap.x - worldMin.x) / size, v = (inMap.y - worldMin.y) / size;
        return new Vector2((u - 0.5f) * pictureSize.x, (v - 0.5f) * pictureSize.y);
    }

    /// <summary>The other way round: a point on a zone picture, from its centre → the spot in the map's own coordinates.</summary>
    static Vector2 PictureToMap(Vector2 onPicture, Vector2 worldMin, float worldSize, Vector2 pictureSize)
    {
        float size = Mathf.Max(1f, worldSize);
        float u = onPicture.x / Mathf.Max(1f, pictureSize.x) + 0.5f, v = onPicture.y / Mathf.Max(1f, pictureSize.y) + 0.5f;
        return new Vector2(worldMin.x + u * size, worldMin.y + v * size);
    }

    // ── zone view: the doors out of the map it shows ──────────────────────────────────────────────────────────────
    void DrawDoors()
    {
        bool showable = zoneEntry != null && zoneImage.enabled;
        Vector2 size = zone.picture.rect.size;
        if (doorsDrawnMap != zoneMap || doorsDrawnSize != size)
        {
            doorsDrawnMap = zoneMap;
            doorsDrawnSize = size;
            doorsDrawnZoom = -1f;
            int used = 0;
            if (showable && data.links != null)
                foreach (var link in data.links)
                {
                    if (link == null) continue;
                    string leadsTo;
                    Vector2 at;
                    if (link.a == zoneMap) { leadsTo = link.b; at = new Vector2(link.ax, link.az); }
                    else if (link.b == zoneMap) { leadsTo = link.a; at = new Vector2(link.bx, link.bz); }
                    else continue;
                    var there = data.Find(leadsTo);
                    var d = Door(used++);
                    d.leadsTo = leadsTo;
                    Color c = MapKinds.ColorOfType(there != null ? there.type : null);
                    d.mark.color = c;
                    d.label.color = c;
                    d.label.text = data.NameOf(leadsTo);
                    Vector2 p = MapToPicture(at, zoneEntry.worldMin, zoneEntry.worldSize, size);
                    d.mark.rectTransform.anchoredPosition = p;
                    d.label.rectTransform.anchoredPosition = p;
                }
            for (int i = used; i < doorMarks.Count; i++)
            {
                if (doorMarks[i].mark.gameObject.activeSelf) doorMarks[i].mark.gameObject.SetActive(false);
                if (doorMarks[i].label.gameObject.activeSelf) doorMarks[i].label.gameObject.SetActive(false);
            }
            doorsShown = used;
        }
        if (!Mathf.Approximately(doorsDrawnZoom, zone.zoom))
        {
            doorsDrawnZoom = zone.zoom;
            var s = new Vector3(1f / zone.zoom, 1f / zone.zoom, 1f);
            for (int i = 0; i < doorMarks.Count; i++)
            {
                doorMarks[i].mark.rectTransform.localScale = s;
                doorMarks[i].label.rectTransform.localScale = s;
            }
        }
    }

    DoorMark Door(int i)
    {
        while (doorMarks.Count <= i) { doorMarks.Add(NewMark(doorRoot, "Door", 45f, DoorMarkSize, DoorLabelSize, true)); doorsDrawnZoom = -1f; }
        var d = doorMarks[i];
        if (!d.mark.gameObject.activeSelf) d.mark.gameObject.SetActive(true);
        if (!d.label.gameObject.activeSelf) d.label.gameObject.SetActive(true);
        return d;
    }

    // One mark and the name hanging under it — a door's diamond or a person's dot, built the same way.
    DoorMark NewMark(RectTransform root, string kind, float turn, float markSize, float labelSize, bool square)
    {
        var mark = NewImage(kind, root, Color.white);
        if (!square) UiKit.Round(mark, UiKit.WindowRadius);
        var mrt = mark.rectTransform;
        mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0.5f);
        mrt.sizeDelta = new Vector2(markSize, markSize);
        mark.transform.localRotation = Quaternion.Euler(0f, 0f, turn);
        var label = NewText(kind + "Name", root, "", (int)labelSize, TextAnchor.UpperCenter);
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        var lrt = label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
        // The name hangs just under its mark, centred on it: the pivot sits above the text by most of a mark, so
        // the gap shrinks with the zoom exactly as the mark does.
        lrt.pivot = new Vector2(0.5f, 1f + markSize * 0.75f / MarkLabelHeight);
        lrt.sizeDelta = new Vector2(220f, MarkLabelHeight);
        return new DoorMark { mark = mark, label = label };
    }

    // ── zone view: the townsfolk of the map it shows ──────────────────────────────────────────────────────────────
    void DrawPeople()
    {
        Vector2 size = zone.picture.rect.size;
        if (peopleDrawnMap != zoneMap || peopleDrawnSize != size)
        {
            peopleDrawnMap = zoneMap;
            peopleDrawnSize = size;
            peopleDrawnZoom = -1f;
            int used = 0;
            if (zoneEntry != null && zoneImage.enabled)
            {
                WorldTownsfolk.PeopleIn(zoneMap, peopleOnMap);
                foreach (var person in peopleOnMap)
                {
                    while (personMarks.Count <= used) personMarks.Add(NewMark(peopleRoot, "Person", 0f, PersonMarkSize, PersonLabelSize, false));
                    var m = personMarks[used++];
                    // Somebody who offers something wears its picture; everybody else a plain dot.
                    var service = UiIcons.ServiceOf(person);
                    if (service != null)
                    {
                        m.mark.sprite = service;
                        m.mark.type = Image.Type.Simple;
                        m.mark.preserveAspect = true;
                        UiKit.Paint(m.mark, Color.white);
                        m.mark.rectTransform.sizeDelta = new Vector2(PersonIconSize, PersonIconSize);
                    }
                    else
                    {
                        UiKit.Round(m.mark, UiKit.WindowRadius);
                        UiKit.Paint(m.mark, UiKit.Accent);
                        m.mark.rectTransform.sizeDelta = new Vector2(PersonMarkSize, PersonMarkSize);
                    }
                    // The name hangs under whichever mark it is, as under a door's.
                    float markSize = m.mark.rectTransform.sizeDelta.y;
                    m.label.rectTransform.pivot = new Vector2(0.5f, 1f + markSize * 0.75f / MarkLabelHeight);
                    UiKit.Paint(m.label, UiKit.Accent);
                    m.label.text = string.IsNullOrEmpty(person.description) ? person.name : person.name + " · " + person.description;
                    // A person's home is in the map's own coordinates: x east, z north (y is height).
                    Vector2 p = MapToPicture(new Vector2(person.home.x, person.home.z), zoneEntry.worldMin, zoneEntry.worldSize, size);
                    m.mark.rectTransform.anchoredPosition = p;
                    m.label.rectTransform.anchoredPosition = p;
                    if (!m.mark.gameObject.activeSelf) m.mark.gameObject.SetActive(true);
                }
            }
            for (int i = used; i < personMarks.Count; i++)
            {
                if (personMarks[i].mark.gameObject.activeSelf) personMarks[i].mark.gameObject.SetActive(false);
                if (personMarks[i].label.gameObject.activeSelf) personMarks[i].label.gameObject.SetActive(false);
            }
            peopleShown = used;
        }
        if (!Mathf.Approximately(peopleDrawnZoom, zone.zoom))
        {
            peopleDrawnZoom = zone.zoom;
            var sc = new Vector3(1f / zone.zoom, 1f / zone.zoom, 1f);
            bool named = zone.zoom >= PersonNamesFromZoom;
            for (int i = 0; i < peopleShown; i++)
            {
                personMarks[i].mark.rectTransform.localScale = sc;
                personMarks[i].label.rectTransform.localScale = sc;
                if (personMarks[i].label.gameObject.activeSelf != named) personMarks[i].label.gameObject.SetActive(named);
            }
        }
    }

    // The door whose mark or name is under the pointer; null over anything else.
    string DoorUnder(Vector2 screenPoint)
    {
        for (int i = 0; i < doorsShown; i++)
        {
            var d = doorMarks[i];
            if (RectTransformUtility.RectangleContainsScreenPoint(d.mark.rectTransform, screenPoint, null)) return d.leadsTo;
            if (d.label.text.Length > 0 && TMP_TextUtilities.IsIntersectingRectTransform(d.label.rectTransform, screenPoint, null)
                && TMP_TextUtilities.FindIntersectingCharacter(d.label, screenPoint, null, true) >= 0) return d.leadsTo;
        }
        return null;
    }

    // ── zone view: the spot picked to be led to ───────────────────────────────────────────────────────────────────
    void DrawGuidePin()
    {
        bool here = MapGuide.Active && MapGuide.Map == zoneMap && zoneEntry != null && zoneImage.enabled;
        Vector2 size = zone.picture.rect.size;
        if (guideDrawnVersion != MapGuide.Version || guideDrawnMap != zoneMap || guideDrawnSize != size)
        {
            guideDrawnVersion = MapGuide.Version;
            guideDrawnMap = zoneMap;
            guideDrawnSize = size;
            if (guidePin.gameObject.activeSelf != here) guidePin.gameObject.SetActive(here);
            if (here) guidePin.rectTransform.anchoredPosition = MapToPicture(MapGuide.Spot, zoneEntry.worldMin, zoneEntry.worldSize, size);
        }
        if (!Mathf.Approximately(guideDrawnZoom, zone.zoom))
        {
            guideDrawnZoom = zone.zoom;
            guidePin.rectTransform.localScale = new Vector3(1f / zone.zoom, 1f / zone.zoom, 1f);
        }
    }

    // A click on the zone view: on a door, the view opens the map it leads to; anywhere else on the picture, that spot
    // becomes the one the ground arrows lead to (MapGuide).
    void ClickZone(Vector2 screenPoint)
    {
        if (zoneEntry == null || !zoneImage.enabled) return;
        string door = DoorUnder(screenPoint);
        if (door != null)
        {
            UISounds.PlayClick();
            ShowZone(door);
            return;
        }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(zone.picture, screenPoint, null, out Vector2 local)) return;
        Vector2 size = zone.picture.rect.size;
        if (Mathf.Abs(local.x) > size.x * 0.5f || Mathf.Abs(local.y) > size.y * 0.5f) return;
        UISounds.PlayClick();
        MapGuide.Set(zoneMap, PictureToMap(local, zoneEntry.worldMin, zoneEntry.worldSize, size));
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

    // ── the square pictures ───────────────────────────────────────────────────────────────────────────────────────
    bool TiledZone => zoneEntry != null && zoneEntry.tileSize > 0f && zoneEntry.tilePixels > 0;
    float SquarePixelsPerUnit => zoneEntry.tilePixels / zoneEntry.tileSize;
    float ScreenPixelsPerUnit(float zoom) => zone.picture.rect.width * zoom * canvas.scaleFactor / Mathf.Max(1f, zoneEntry.worldSize);

    float MaxZoom(View v) => v == zone && TiledZone ? Mathf.Max(maxZoom, SquarePixelsPerUnit / Mathf.Max(1e-4f, ScreenPixelsPerUnit(1f))) : maxZoom;

    void UpdateZoneTiles()
    {
        if (!TiledZone || ScreenPixelsPerUnit(zone.zoom) < 0.5f * SquarePixelsPerUnit) { zoneTiles.Clear(); return; }
        Vector2 size = zone.picture.rect.size;
        viewport.GetWorldCorners(viewCorners);
        Vector2 lo = PictureToMap(zone.picture.InverseTransformPoint(viewCorners[0]), zoneEntry.worldMin, zoneEntry.worldSize, size);
        Vector2 hi = PictureToMap(zone.picture.InverseTransformPoint(viewCorners[2]), zoneEntry.worldMin, zoneEntry.worldSize, size);
        float perUnit = 1f / Mathf.Max(1f, zoneEntry.worldSize);
        var scale = new Vector2(size.x * perUnit, size.y * perUnit);
        var origin = new Vector2(-0.5f * size.x - zoneEntry.worldMin.x * scale.x, -0.5f * size.y - zoneEntry.worldMin.y * scale.y);
        zoneTiles.Show(zoneMap, zoneEntry.tileSize, Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y), origin, scale, TileLoadMilliseconds);
    }

    // ── the location button ───────────────────────────────────────────────────────────────────────────────────────
    static bool Zoomed(View v) => v.goalZoom > 1.001f;

    // Glides the view back to you; on another map's view it opens your own.
    void Locate()
    {
        UISounds.PlayClick();
        if (active == zone && zoneMap != MapManager.CurrentMap) { ShowZone(MapManager.CurrentMap); return; }
        var v = active;
        v.zoom = v.goalZoom;
        glideFrom = v.pan;
        glideTo = ClampPan(v, -v.marker.anchoredPosition * v.zoom, v.zoom);
        glideT = 0f;
        gliding = true;
    }

    void StepGlide()
    {
        if (!gliding) return;
        glideT = Mathf.Min(1f, glideT + Time.unscaledDeltaTime / Mathf.Max(0.01f, locateSeconds));
        active.pan = ClampPan(active, Vector2.LerpUnclamped(glideFrom, glideTo, EaseOut(glideT)), active.zoom);
        ApplyView(active);
        if (glideT >= 1f) gliding = false;
    }

    /// <summary>Ease-out: quick at the start, slowing to rest at the end (a cubic curve, 0 to 1).</summary>
    static float EaseOut(float t) { float u = 1f - t; return 1f - u * u * u; }

    // ── zoom and drag, on whichever view is up ────────────────────────────────────────────────────────────────────
    // The goal moves; the drawn view eases after it (StepZoom). The map point under the pointer stays under it.
    void ZoomAt(Vector2 screenPoint, float notches)
    {
        var v = active;
        float next = Mathf.Clamp(v.goalZoom * Mathf.Pow(zoomPerNotch, notches), 1f, MaxZoom(v));
        if (Mathf.Approximately(next, v.goalZoom)) return;
        gliding = false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPoint, null, out Vector2 cursor);
        Vector2 underCursor = (cursor - v.goalPan) / v.goalZoom;
        v.goalZoom = next;
        v.goalPan = ClampPan(v, cursor - underCursor * next, next);
    }

    // The zoom buttons zoom toward the middle of the map's room.
    void ZoomStep(float notches)
    {
        UISounds.PlayClick();
        ZoomAt(RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(viewport.rect.center)), notches);
    }

    // A drag moves the drawn view at once, so the map stays under the hand.
    void DragBy(Vector2 screenDelta)
    {
        var v = active;
        if (!Zoomed(v)) return;
        gliding = false;
        v.zoom = v.goalZoom;
        v.pan = v.goalPan = ClampPan(v, v.pan + screenDelta / Mathf.Max(0.0001f, canvas.scaleFactor), v.zoom);
        ApplyView(v);
    }

    Vector2 ClampPan(View v, Vector2 want, float zoom)
    {
        Vector2 picture = v.picture.rect.size * zoom;
        Vector2 room = viewport.rect.size;
        float mx = Mathf.Max(0f, (picture.x - room.x) * 0.5f), my = Mathf.Max(0f, (picture.y - room.y) * 0.5f);
        return new Vector2(Mathf.Clamp(want.x, -mx, mx), Mathf.Clamp(want.y, -my, my));
    }

    static void ApplyView(View v)
    {
        v.zoomRoot.localScale = new Vector3(v.zoom, v.zoom, 1f);
        v.zoomRoot.anchoredPosition = v.pan;
    }

    static void ResetView(View v)
    {
        v.zoom = v.goalZoom = 1f;
        v.pan = v.goalPan = Vector2.zero;
        ApplyView(v);
    }

    /// <summary>The wheel, the drag and the click, caught over the map's room.</summary>
    sealed class MapInput : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IPointerClickHandler,
                            IPointerMoveHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public MapWindow owner;
        public void OnScroll(PointerEventData e)
        {
            owner.ZoomAt(e.position, e.scrollDelta.y);
        }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e)
        {
            owner.pointer = e.position;
            if (e.button == PointerEventData.InputButton.Left) owner.DragBy(e.delta);
        }
        public void OnPointerEnter(PointerEventData e) { owner.pointerOverMap = true; owner.pointer = e.position; }
        public void OnPointerMove(PointerEventData e) => owner.pointer = e.position;
        public void OnPointerExit(PointerEventData e) => owner.pointerOverMap = false;
        public void OnPointerClick(PointerEventData e)
        {
            // A DRAG IS NOT A CLICK. The backdrop both drags and clicks, so the event system still sends the click on
            // release after a drag; it is still marked dragging at that moment, which is how a drag is told apart.
            if (e.dragging) return;
            // A right click on the zone view drops the picked spot; the arrows go back to the quest.
            if (e.button == PointerEventData.InputButton.Right && owner.active == owner.zone && MapGuide.Active)
            {
                UISounds.PlayClick();
                MapGuide.Clear();
                return;
            }
            if (e.button != PointerEventData.InputButton.Left) return;
            if (owner.active == owner.world) owner.PickZone(e.position);
            else owner.ClickZone(e.position);
        }
    }

    /// <summary>The wheel and the pointer over the side panel: the wheel scrolls its list, and a name the pointer rests
    /// on opens its creature card. The panel catches clicks, so nothing behind it is picked.</summary>
    sealed class PanelInput : MonoBehaviour, IScrollHandler, IPointerMoveHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public MapWindow owner;
        public void OnScroll(PointerEventData e)
        {
            // The list moved under a still pointer, so the name under it is looked up again.
            if (owner.zoneRoster.ScrollAt(e.position, e.scrollDelta.y)) owner.cardLooked = false;
        }
        public void OnPointerEnter(PointerEventData e) { owner.pointerOverPanel = true; owner.pointer = e.position; }
        public void OnPointerMove(PointerEventData e) => owner.pointer = e.position;
        public void OnPointerExit(PointerEventData e) => owner.pointerOverPanel = false;
    }

    // ── UI construction, on the main screen layer ─────────────────────────────────────────────────────────────────
    // The side panel's sizes.
    const float PanelPadX = 14f, HeadPadTop = 10f, HeadPadBottom = 12f, WhereGap = 2f, HeadButtonsGap = 10f,
                HeadButtonHeight = 28f, HeadButtonPad = 10f, HeadButtonSpacing = 6f, ListPadTop = 4f, ListPadBottom = 10f;
    // The corner buttons: square, stacked up from the room's bottom-right corner.
    const float CornerButton = 38f, CornerGap = 6f, CornerInset = 16f;

    void BuildUI()
    {
        panel = new GameObject("MapWindow", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        panel.transform.SetParent(canvas.transform, false);
        panelRect = panel.GetComponent<RectTransform>();
        Stretch(panelRect);

        // THE MAP'S ROOM, left of the side panel: the window's fill, the map over it, the footer under it.
        mapArea = new GameObject("MapArea", typeof(RectTransform)).GetComponent<RectTransform>();
        mapArea.SetParent(panel.transform, false);
        Stretch(mapArea);
        Stretch(NewImage("Fill", mapArea, UiKit.PanelFill).rectTransform);
        viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        viewport.SetParent(mapArea, false);
        Stretch(viewport);
        viewport.offsetMin = new Vector2(0f, FooterHeight);
        // the thing the wheel, the drag and the click land on, and that keeps a click off the world behind
        var catcher = UiKit.Img("Catcher", viewport, new Color(0f, 0f, 0f, 0f));
        Stretch(catcher.rectTransform);
        catcher.raycastTarget = true;
        catcher.gameObject.AddComponent<MapInput>().owner = this;

        atlasImage = BuildView(world, "World", hasWorldView ? data.atlas : null,
                               hasWorldView ? (float)data.atlas.width / Mathf.Max(1, data.atlas.height) : 1f);
        zoneImage = BuildView(zone, "Zone", null, 1f);
        var tileRoot = new GameObject("Squares", typeof(RectTransform)).GetComponent<RectTransform>();
        tileRoot.SetParent(zone.picture, false);
        tileRoot.SetAsFirstSibling();
        Stretch(tileRoot);
        zoneTiles = new MapTileLayer(tileRoot);
        patchImage = new GameObject("RespawnPatches", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage))
            .GetComponent<RawImage>();
        patchImage.transform.SetParent(zone.picture, false);
        patchImage.raycastTarget = false;
        Stretch(patchImage.rectTransform);
        patchFade = NewFade(patchImage.gameObject, FadeSeconds);
        patchHoverImage = new GameObject("PatchUnderPointer", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage))
            .GetComponent<RawImage>();
        patchHoverImage.transform.SetParent(zone.picture, false);
        patchHoverImage.raycastTarget = false;
        Stretch(patchHoverImage.rectTransform);
        patchHoverFade = NewFade(patchHoverImage.gameObject, FadeSeconds);
        BuildDoors();
        BuildKindIcons();

        routeSprite = Resources.Load<Sprite>("Guide/GroundArrow");
        routeRoot = new GameObject("Route", typeof(RectTransform)).GetComponent<RectTransform>();
        routeRoot.SetParent(zone.picture, false);
        routeRoot.anchorMin = routeRoot.anchorMax = routeRoot.pivot = new Vector2(0.5f, 0.5f);

        doorRoot = new GameObject("Doors", typeof(RectTransform)).GetComponent<RectTransform>();
        doorRoot.SetParent(zone.picture, false);
        doorRoot.anchorMin = doorRoot.anchorMax = doorRoot.pivot = new Vector2(0.5f, 0.5f);

        peopleRoot = new GameObject("People", typeof(RectTransform)).GetComponent<RectTransform>();
        peopleRoot.SetParent(zone.picture, false);
        peopleRoot.anchorMin = peopleRoot.anchorMax = peopleRoot.pivot = new Vector2(0.5f, 0.5f);

        // the level ranges under the labels, each layer fading on its own
        rangeRoot = new GameObject("LevelRanges", typeof(RectTransform)).GetComponent<RectTransform>();
        rangeRoot.SetParent(zone.picture, false);
        rangeRoot.anchorMin = rangeRoot.anchorMax = rangeRoot.pivot = new Vector2(0.5f, 0.5f);
        rangeFade = NewFade(rangeRoot.gameObject, FadeSeconds);

        labelRoot = new GameObject("Places", typeof(RectTransform)).GetComponent<RectTransform>();
        labelRoot.SetParent(zone.picture, false);
        labelRoot.anchorMin = labelRoot.anchorMax = labelRoot.pivot = new Vector2(0.5f, 0.5f);
        labelFade = NewFade(labelRoot.gameObject, FadeSeconds);
        labelFade.on = showLabels;
        labelFade.t = showLabels ? 1f : 0f;

        pinSprite = GuidePin.Sprite;
        guidePin = NewImage("GuidePin", zone.picture, UiKit.Accent);
        guidePin.sprite = pinSprite;
        var gprt = guidePin.rectTransform;
        // The pin's point is its bottom (4 of its 64 pixels up), so that is the spot it stands on.
        gprt.anchorMin = gprt.anchorMax = new Vector2(0.5f, 0.5f);
        gprt.pivot = new Vector2(0.5f, GuidePin.TipFromBottom);
        gprt.sizeDelta = new Vector2(GuidePinSize, GuidePinSize);
        guidePin.gameObject.SetActive(false);
        zone.marker.SetAsLastSibling();

        zoneMissing = NewText("NoPicture", viewport, "This map has no picture yet.", 16, TextAnchor.MiddleCenter);
        UiKit.Paint(zoneMissing, UiKit.TextDim);
        Stretch(zoneMissing.rectTransform);

        // between the zone and the world map
        var switchBtn = UiKit.Btn("", viewport, SwitchView);
        switchButton = switchBtn.gameObject;
        switchButton.SetActive(hasWorldView);
        var srt = (RectTransform)switchBtn.transform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f);
        srt.sizeDelta = new Vector2(110f, 28f);
        srt.anchoredPosition = new Vector2(16f, -16f);
        switchLabel = switchBtn.GetComponentInChildren<TMP_Text>();
        switchIcon = UiKit.ButtonIcon(switchBtn, UiIcons.Get("WorldMap"), keepWords: true);

        // the corner: zoom in, zoom out, back to where you are
        CornerBtn("+", 2, () => ZoomStep(1f));
        CornerBtn("−", 1, () => ZoomStep(-1f));
        var locate = CornerBtn("", 0, Locate);
        var pin = NewImage("Pin", locate.transform, UiKit.Accent);
        pin.sprite = pinSprite;
        pin.rectTransform.sizeDelta = new Vector2(24f, 24f);

        // the footer under the map
        var footer = new GameObject("Footer", typeof(RectTransform)).GetComponent<RectTransform>();
        footer.SetParent(mapArea, false);
        footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1f, 0f); footer.pivot = new Vector2(0.5f, 0f);
        footer.sizeDelta = new Vector2(0f, FooterHeight);
        Line(footer, top: true);
        var hint = NewText("Hint", footer, $"[{GameKeys.Label(GameKeys.Map)}] / [Esc] to close", 11, TextAnchor.MiddleCenter);
        UiKit.Paint(hint, UiKit.TextDim);
        Stretch(hint.rectTransform);

        BuildSidePanel();

        // the list beside the pointer: what the world keeps on the patch under it
        hoverRoster = new RosterCard(panel.transform, card: true, padTop: 4f, padBottom: 4f);
        hoverRoster.Root.anchorMin = hoverRoster.Root.anchorMax = new Vector2(0.5f, 0.5f);
        tipFade = NewFade(hoverRoster.Root.gameObject, TipFadeSeconds);

        // the creature card — built last, so it draws over the list it belongs to; it catches no clicks
        var card = UiKit.Card(UiKit.Img("MobCard", panel.transform, Color.white));
        mobCard = card.rectTransform;
        mobCard.anchorMin = mobCard.anchorMax = new Vector2(0.5f, 0.5f);
        mobCardText = UiKit.MakeText("Text", mobCard, "", 12, FontStyle.Normal, TextAnchor.UpperLeft);
        UiKit.Paint(mobCardText, UiKit.TextMain);
        mobCardText.richText = true;
        mobCardText.textWrappingMode = TextWrappingModes.Normal;
        var crt = mobCardText.rectTransform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0f, 1f);
        crt.anchoredPosition = new Vector2(MobCardPad, -MobCardPad);
        mobCardFace = NewImage("Face", mobCard, Color.white);
        mobCardFace.preserveAspect = true;
        var frt = mobCardFace.rectTransform;
        frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0f, 1f);
        frt.anchoredPosition = new Vector2(MobCardPad, -MobCardPad);
        frt.sizeDelta = new Vector2(MobCardFaceSize, MobCardFaceSize);
        foreach (var g in card.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        mobCard.gameObject.SetActive(false);

        LayRoom();
    }

    // THE SIDE PANEL: the fold bar on top; under it the map's name, where you stand and the two toggles; under those the
    // mobs where you stand.
    void BuildSidePanel()
    {
        // clipped, so its words never spill past it while it widens
        sidePanel = new GameObject("SidePanel", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        sidePanel.SetParent(panel.transform, false);
        sidePanel.anchorMin = new Vector2(1f, 0f); sidePanel.anchorMax = Vector2.one; sidePanel.pivot = new Vector2(1f, 0.5f);
        var fill = UiKit.Img("Fill", sidePanel, UiKit.PanelFill);
        Stretch(fill.rectTransform);
        fill.raycastTarget = true;
        fill.gameObject.AddComponent<PanelInput>().owner = this;
        var edge = NewImage("Edge", sidePanel, UiKit.WindowEdge);
        var ert = edge.rectTransform;
        ert.anchorMin = Vector2.zero; ert.anchorMax = new Vector2(0f, 1f); ert.pivot = new Vector2(0f, 0.5f);
        ert.sizeDelta = new Vector2(1f, 0f);

        // the fold bar
        var bar = UiKit.Img("Fold", sidePanel, UiKit.PanelRaise);
        var brt = bar.rectTransform;
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = Vector2.one; brt.pivot = new Vector2(0.5f, 1f);
        brt.offsetMin = new Vector2(1f, -FooterHeight); brt.offsetMax = Vector2.zero;
        var fold = bar.gameObject.AddComponent<Button>();
        fold.targetGraphic = bar;
        bar.gameObject.AddComponent<UiButtonFx>();
        fold.onClick.AddListener(ToggleFold);
        Line(brt, top: false);
        foldLabel = NewText("Arrow", brt, "›", 14, TextAnchor.MiddleLeft);
        UiKit.Paint(foldLabel, UiKit.TextDim);
        Stretch(foldLabel.rectTransform);
        foldLabel.rectTransform.offsetMin = new Vector2(9f, 0f);

        var body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
        body.SetParent(sidePanel, false);
        Stretch(body);
        body.offsetMin = new Vector2(1f, 0f);
        body.offsetMax = new Vector2(0f, -FooterHeight);
        panelBody = body.gameObject;

        float y = HeadPadTop;
        title = NewText("Title", body, "", 22, TextAnchor.UpperLeft);
        UiKit.Paint(title, UiKit.TitleText);
        y = HeadLine(title.rectTransform, y, title.GetPreferredValues("Ag").y);
        whereLabel = NewText("Where", body, "", 14, TextAnchor.UpperLeft);
        UiKit.Paint(whereLabel, UiKit.TextDim);
        y = HeadLine(whereLabel.rectTransform, y + WhereGap, whereLabel.GetPreferredValues("Ag").y);
        y += HeadButtonsGap;

        // the respawn patches over the map, and the world's places on it, on and off; side by side while they fit
        float x = PanelPadX;
        HeadButton(body, "Show respawn patches", TogglePatches, ref x, ref y, out patchLabel, out patchButton);
        HeadButton(body, "Hide map labels", ToggleLabels, ref x, ref y, out labelLabel, out labelButton);
        y += HeadButtonHeight + HeadPadBottom;
        var rule = new GameObject("Rule", typeof(RectTransform)).GetComponent<RectTransform>();
        rule.SetParent(body, false);
        rule.anchorMin = new Vector2(0f, 1f); rule.anchorMax = Vector2.one; rule.pivot = new Vector2(0.5f, 1f);
        rule.sizeDelta = new Vector2(0f, 0f);
        rule.anchoredPosition = new Vector2(0f, -y);
        Line(rule, top: true);

        // the mobs where you stand
        listRoom = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
        listRoom.SetParent(body, false);
        Stretch(listRoom);
        listRoom.offsetMax = new Vector2(0f, -y);
        zoneRoster = new RosterCard(listRoom, card: false, padTop: ListPadTop, padBottom: ListPadBottom);
        var zrt = zoneRoster.Root;
        zrt.anchorMin = zrt.anchorMax = zrt.pivot = new Vector2(0f, 1f);
    }

    RectTransform listRoom;

    // One line of the panel's head: full width less the padding, `height` tall, `top` down from the body's top.
    static float HeadLine(RectTransform rt, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(PanelPadX, -top - height);
        rt.offsetMax = new Vector2(-PanelPadX, -top);
        return top + height;
    }

    // A head button as wide as its words, beside the last one, or on the next line when it would run past the panel.
    void HeadButton(Transform parent, string words, System.Action onClick, ref float x, ref float y, out TMP_Text label, out GameObject button)
    {
        var b = UiKit.Btn(words, parent, onClick);
        label = b.GetComponentInChildren<TMP_Text>();
        label.fontSize = 13f;
        // as wide as the longer of its two wordings, so it never changes size when it flips
        string other = words.StartsWith("Show") ? "Hide" + words.Substring(4) : "Show" + words.Substring(4);
        float width = Mathf.Max(label.GetPreferredValues(words).x, label.GetPreferredValues(other).x) + 2f * HeadButtonPad;
        if (x > PanelPadX && x + width > PanelWidth - PanelPadX) { x = PanelPadX; y += HeadButtonHeight + HeadButtonSpacing; }
        var rt = (RectTransform)b.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, HeadButtonHeight);
        rt.anchoredPosition = new Vector2(x, -y);
        x += width + HeadButtonSpacing;
        button = b.gameObject;
    }

    // A square button in the map's bottom-right corner, `fromBottom` places up the stack.
    Button CornerBtn(string words, int fromBottom, System.Action onClick)
    {
        var b = UiKit.Btn(words, viewport, onClick);
        b.GetComponentInChildren<TMP_Text>().fontSize = 20f;
        var rt = (RectTransform)b.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(CornerButton, CornerButton);
        rt.anchoredPosition = new Vector2(-CornerInset, CornerInset + fromBottom * (CornerButton + CornerGap));
        return b;
    }

    // A one-unit rule along the top or the bottom edge of `rt`.
    static void Line(RectTransform rt, bool top)
    {
        var line = NewImage("Rule", rt, UiKit.Rule).rectTransform;
        float y = top ? 1f : 0f;
        line.anchorMin = new Vector2(0f, y); line.anchorMax = new Vector2(1f, y); line.pivot = new Vector2(0.5f, y);
        line.sizeDelta = new Vector2(0f, 1f);
        line.anchoredPosition = Vector2.zero;
    }

    // The list beside the pointer is at least this wide.
    const float TipMinWidth = 230f;
    // The most of the screen's width a list may take, so the map beside it stays in view however many live there.
    const float RosterWidthShare = 0.4f;
    float RosterMaxWidth => panelRect.rect.width * RosterWidthShare;
    // The hover list's distance from the pointer, and the least it keeps from the screen's edge.
    const float HoverGap = 16f, HoverEdge = 12f;

    // Passed as the patch to a roster card: the whole map.
    const int WholeMap = int.MinValue;

    /// <summary>
    /// WHO LIVES IN ONE MAP OR ONE PATCH OF IT — a whole map's mobs, then its NPCs; a patch's mobs alone — one row each:
    /// the name, the level, and for a patch the number its row of the spawn list keeps there, never how many are alive.
    /// Read from the two lists of who lives where (<see cref="WorldSpawns.CreaturesIn"/>, <see cref="WorldSpawns.RowsOn"/>,
    /// <see cref="WorldTownsfolk.PeopleIn"/>). Each new list slides its rows in one after another. A list longer than its
    /// room scrolls inside it with the wheel (<see cref="ScrollAt"/>). It catches no clicks, so the map under it still
    /// drags and picks.
    /// </summary>
    sealed class RosterCard
    {
        public RectTransform Root { get; }
        public bool HasPeople { get; private set; }
        public float LaidForHeight { get; private set; } = -1f;

        // The map window's list sizes: a row is its name, a 52-wide level and a 36-wide count, 6 apart.
        const float RowSize = 13f, HeadSize = 14f, PadX = 14f, RowPadY = 3f, LevelColumn = 52f, CountColumn = 36f, ColumnGap = 6f;
        const float HeadPadTop = 10f, HeadPadBottom = 4f;
        // Each row fades in rising RowRise over RowInSeconds, RowStagger after the one above it.
        const float RowInSeconds = 0.2f, RowStagger = 0.025f, RowRise = 4f;

        sealed class Line
        {
            public RectTransform rt;
            public CanvasGroup fade;
            public TMP_Text name, level, count;
            public MonsterDef mob;
            public float top, height, delay, age = -1f;
        }

        readonly bool lightsRows;
        readonly float padTop, padBottom;
        // The clipped window onto the list, and the list itself, which slides up inside it as it scrolls.
        readonly RectTransform view, content;
        readonly Image lit;
        readonly List<Line> lines = new List<Line>();
        int used;
        readonly List<MonsterDef> mobs = new List<MonsterDef>();
        readonly List<WorldTownsfolk.Row> people = new List<WorldTownsfolk.Row>();
        readonly List<(MonsterDef def, int kept)> rows = new List<(MonsterDef def, int kept)>();
        // How far the list is scrolled down, the most it can be, and the map and patch it was laid out for — a new one
        // starts at the top and slides its rows in, the same one laid out again (a new screen size) stays as it was.
        float scrolled, scrollMax, rowHeight, headHeight;
        string laidMap;
        int laidArea = int.MaxValue;
        bool entering;

        /// <summary>A card of its own beside the pointer (<paramref name="card"/>), or a plain list inside a panel, whose
        /// row under the pointer is lit (<see cref="LightRow"/>).</summary>
        public RosterCard(Transform parent, bool card, float padTop, float padBottom)
        {
            Root = card ? UiKit.Card(UiKit.Img("Roster", parent, Color.white)).rectTransform
                        : new GameObject("Roster", typeof(RectTransform)).GetComponent<RectTransform>();
            if (!card) Root.SetParent(parent, false);
            foreach (var g in Root.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            lightsRows = !card;
            this.padTop = padTop;
            this.padBottom = padBottom;
            view = new GameObject("View", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            view.SetParent(Root, false);
            Corner(view);
            content = new GameObject("Lines", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(view, false);
            Corner(content);
            lit = UiKit.Img("Lit", content, UiKit.Hover);
            lit.raycastTarget = false;
            Corner(lit.rectTransform);
            lit.gameObject.SetActive(false);
        }

        /// <summary>True when <paramref name="screenPoint"/> is over the card while it is shown.</summary>
        public bool Contains(Vector2 screenPoint) =>
            Root.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(Root, screenPoint, null);

        /// <summary>Nothing to list.</summary>
        public void Clear(float maxHeight, float width)
        {
            Begin(null, int.MaxValue, maxHeight);
            Finish(width, width, maxHeight);
        }

        /// <summary>Only <paramref name="heading"/> and one line saying there is nobody (<paramref name="none"/>).</summary>
        public void ShowNone(string heading, string none, float maxHeight, float width)
        {
            Begin(null, -1, maxHeight);
            Head(heading);
            Row(none, "", "", null, UiKit.TextDim);
            Finish(width, width, maxHeight);
        }

        /// <summary>Lay out who lives in <paramref name="map"/> — the whole of it (<see cref="WholeMap"/>), or only its
        /// patch <paramref name="area"/> — under <paramref name="heading"/> (none when null), at most
        /// <paramref name="maxHeight"/> tall and between <paramref name="minWidth"/> and <paramref name="maxWidth"/>
        /// wide.</summary>
        public void Show(string heading, string map, int area, float maxHeight, float minWidth, float maxWidth)
        {
            Begin(map, area, maxHeight);
            bool wholeMap = area == WholeMap;
            mobs.Clear();
            rows.Clear();
            people.Clear();
            if (wholeMap) { WorldSpawns.CreaturesIn(map, mobs); WorldTownsfolk.PeopleIn(map, people); }
            else WorldSpawns.RowsOn(map, area, rows);
            HasPeople = mobs.Count > 0 || rows.Count > 0 || people.Count > 0;
            // A whole map names its mobs with no number: how many it keeps is a sum of its rows, which is the server's to
            // work out. A patch lists its rows, each with the number it keeps; none for a creature only a random mix puts
            // there, whose count belongs to the whole mix.
            if (wholeMap && mobs.Count > 0) heading = "Mobs";
            if (HasPeople && !string.IsNullOrEmpty(heading)) Head(heading);
            foreach (var m in mobs) Row(MobName(m), LevelOf(m.level), "", m, UiKit.TextMain);
            foreach (var (m, n) in rows) Row(MobName(m), LevelOf(m.level), n > 0 ? "×" + n : "", m, UiKit.TextMain);
            if (people.Count > 0)
            {
                Head("NPCs");
                foreach (var p in people)
                    Row(p.name + (string.IsNullOrEmpty(p.description) ? "" : "  · " + p.description), LevelOf(p.level), "", null, UiKit.TextMain);
            }
            Finish(minWidth, maxWidth, maxHeight);
        }

        static string MobName(MonsterDef m) => m.displayName + (m.kind == CreatureKind.Elite ? " (Elite)" : "");
        static string LevelOf(int level) => "Lv " + level;

        void Begin(string map, int area, float maxHeight)
        {
            LaidForHeight = maxHeight;
            entering = map != laidMap || area != laidArea;
            if (entering) scrolled = 0f;
            laidMap = map;
            laidArea = area;
            HasPeople = false;
            used = 0;
            lit.gameObject.SetActive(false);
            if (rowHeight <= 0f)
            {
                // From the face's own line height: a card still hidden measures its words as nothing.
                var face = Take().name.font.faceInfo;
                float line = face.lineHeight / Mathf.Max(1f, face.pointSize);
                rowHeight = line * RowSize + 2f * RowPadY;
                headHeight = line * HeadSize + HeadPadTop + HeadPadBottom;
                used = 0;
            }
        }

        void Head(string text)
        {
            var l = Take();
            l.name.fontSize = HeadSize;
            UiKit.Paint(l.name, UiKit.TitleText);
            l.name.text = text;
            l.level.text = l.count.text = "";
            l.mob = null;
            l.height = headHeight;
            l.name.rectTransform.offsetMin = new Vector2(PadX, HeadPadBottom);
            l.name.rectTransform.offsetMax = new Vector2(-PadX, -HeadPadTop);
            l.delay = -1f;
        }

        int rowIndex;
        void Row(string name, string level, string count, MonsterDef mob, ThemeColor colour)
        {
            var l = Take();
            l.name.fontSize = RowSize;
            UiKit.Paint(l.name, colour);
            l.name.text = name;
            l.level.text = level;
            l.count.text = count;
            l.mob = mob;
            l.height = rowHeight;
            l.name.rectTransform.offsetMin = new Vector2(PadX, RowPadY);
            l.name.rectTransform.offsetMax = new Vector2(-(PadX + LevelColumn + CountColumn + 2f * ColumnGap), -RowPadY);
            l.delay = rowIndex++ * RowStagger;
        }

        void Finish(float minWidth, float maxWidth, float maxHeight)
        {
            rowIndex = 0;
            float widest = 0f, y = padTop;
            for (int i = 0; i < used; i++)
            {
                var l = lines[i];
                l.top = y;
                y += l.height;
                widest = Mathf.Max(widest, l.name.GetPreferredValues(l.name.text).x
                                   + (l.delay >= 0f ? LevelColumn + CountColumn + 2f * ColumnGap : 0f));
            }
            for (int i = used; i < lines.Count; i++) lines[i].rt.gameObject.SetActive(false);
            float width = Mathf.Clamp(widest + 2f * PadX, minWidth, maxWidth);
            float listHeight = y + padBottom;
            float shown = Mathf.Min(listHeight, Mathf.Max(0f, maxHeight));
            for (int i = 0; i < used; i++)
            {
                var l = lines[i];
                l.rt.sizeDelta = new Vector2(width, l.height);
                // A new list slides in; the same one laid out again stands still.
                l.age = entering && l.delay >= 0f ? 0f : -1f;
                Place(l);
            }
            view.sizeDelta = new Vector2(width, shown);
            content.sizeDelta = new Vector2(width, listHeight);
            scrollMax = listHeight - shown;
            ScrollBy(0f);
            Root.sizeDelta = new Vector2(width, shown);
        }

        Line Take()
        {
            if (used == lines.Count)
            {
                var rt = new GameObject("Row", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
                rt.SetParent(content, false);
                Corner(rt);
                var l = new Line { rt = rt, fade = rt.GetComponent<CanvasGroup>() };
                l.fade.blocksRaycasts = false;
                l.name = Column(rt, "Name", TextAlignmentOptions.BaselineLeft, UiKit.TextMain);
                l.name.overflowMode = TextOverflowModes.Ellipsis;
                l.level = Column(rt, "Level", TextAlignmentOptions.BaselineRight, UiKit.TextDim);
                SetColumn(l.level.rectTransform, PadX + CountColumn + ColumnGap, LevelColumn);
                l.count = Column(rt, "Count", TextAlignmentOptions.BaselineRight, UiKit.TextDim);
                SetColumn(l.count.rectTransform, PadX, CountColumn);
                lines.Add(l);
            }
            var line = lines[used++];
            if (!line.rt.gameObject.activeSelf) line.rt.gameObject.SetActive(true);
            return line;
        }

        static TMP_Text Column(RectTransform row, string name, TextAlignmentOptions align, ThemeColor colour)
        {
            var t = UiKit.MakeText(name, row, "", RowSize, FontStyle.Normal, TextAnchor.MiddleLeft);
            UiKit.Paint(t, colour);
            t.raycastTarget = false;
            t.richText = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.alignment = align;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }

        // A column of `width` standing `fromRight` in from the row's right edge.
        static void SetColumn(RectTransform rt, float fromRight, float width)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(width, -2f * RowPadY);
            rt.anchoredPosition = new Vector2(-fromRight, 0f);
        }

        /// <summary>Move the rows still sliding in; nothing to do once they all stand.</summary>
        public void StepRows(float dt)
        {
            for (int i = 0; i < used; i++)
            {
                var l = lines[i];
                if (l.age < 0f) continue;
                l.age += dt;
                if (l.age >= l.delay + RowInSeconds) l.age = -1f;
                Place(l);
            }
        }

        void Place(Line l)
        {
            float e = 1f;
            if (l.age >= 0f)
            {
                float t = Mathf.Clamp01((l.age - l.delay) / RowInSeconds);
                e = t * t * (3f - 2f * t);
            }
            l.fade.alpha = e;
            l.rt.anchoredPosition = new Vector2(0f, -l.top - (1f - e) * RowRise);
        }

        /// <summary>Light the row under <paramref name="screenPoint"/> (none when <paramref name="over"/> is false) — a
        /// panel list's rows only.</summary>
        public void LightRow(Vector2 screenPoint, bool over)
        {
            if (!lightsRows) return;
            Line hit = over ? LineUnder(screenPoint) : null;
            bool on = hit != null && hit.level.text.Length > 0;
            if (lit.gameObject.activeSelf != on) lit.gameObject.SetActive(on);
            if (!on) return;
            lit.rectTransform.SetAsFirstSibling();
            lit.rectTransform.sizeDelta = hit.rt.sizeDelta;
            lit.rectTransform.anchoredPosition = new Vector2(0f, -hit.top);
        }

        /// <summary>Scroll the list by <paramref name="notches"/> of the wheel when <paramref name="screenPoint"/> is over
        /// the card and the list is longer than its room; false, with nothing moved, otherwise.</summary>
        public bool ScrollAt(Vector2 screenPoint, float notches)
        {
            if (!Contains(screenPoint) || scrollMax <= 0f) return false;
            ScrollBy(-notches * UiKit.ScrollStep);
            return true;
        }

        void ScrollBy(float distance)
        {
            scrolled = Mathf.Clamp(scrolled + distance, 0f, Mathf.Max(0f, scrollMax));
            content.anchoredPosition = new Vector2(0f, scrolled);
        }

        /// <summary>The creature whose row is under <paramref name="screenPoint"/>; null over anything else, a row
        /// scrolled out of view included.</summary>
        public MonsterDef MobUnder(Vector2 screenPoint) => LineUnder(screenPoint)?.mob;

        Line LineUnder(Vector2 screenPoint)
        {
            if (!Root.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint(view, screenPoint, null))
                return null;
            for (int i = 0; i < used; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(lines[i].rt, screenPoint, null)) return lines[i];
            return null;
        }

        static void Corner(RectTransform rt) => rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
    }

    // WHAT KIND OF MAP EACH SQUARE IS — a town's gate, a field's tree, a dungeon's cave — in its top-left corner. Laid by
    // the picture's own proportions, so it stays on its square at every size and zoom with nothing to redo. Built once.
    void BuildKindIcons()
    {
        if (data.maps == null) return;
        var root = new GameObject("MapKinds", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(world.picture, false);
        UiKit.Fill(root, Vector2.zero, Vector2.zero);
        float w = Mathf.Max(1f, data.worldW), h = Mathf.Max(1f, data.worldH);
        foreach (var e in data.maps)
        {
            if (e == null) continue;
            var picture = UiIcons.KindOf(e.type);
            if (picture == null) continue;
            float left = (e.x - data.minX) / w, top = 1f - (e.y - data.minY) / h;
            float sw = e.w / w, sh = e.h / h;
            var icon = NewImage("Kind_" + e.scene, root, Color.white);
            icon.sprite = picture;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var rt = icon.rectTransform;
            rt.anchorMin = new Vector2(left + KindInset * sw, top - (KindInset + KindShare) * sh);
            rt.anchorMax = new Vector2(left + (KindInset + KindShare) * sw, top - KindInset * sh);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    // How big a kind's picture is, and how far in from its square's corner, as shares of the square.
    const float KindShare = 0.32f, KindInset = 0.05f;

    // Every door of the world on the world picture, from the atlas's copy of the world layout's doors: the dashed
    // lines first and the squares over them, each as few meshes as hold them, on a canvas of their own so nothing
    // else on the screen layer is rebuilt with them. Built once; a door whose map the atlas does not hold is left out.
    void BuildDoors()
    {
        var ends = new List<Vector4>(data.links != null ? data.links.Count : 0);
        if (data.links != null)
            foreach (var link in data.links)
            {
                if (link == null) continue;
                var a = data.Find(link.a);
                var b = data.Find(link.b);
                if (a == null || b == null) continue;
                Vector2 pa = OnWorldPicture(a, link.ax, link.az), pb = OnWorldPicture(b, link.bx, link.bz);
                ends.Add(new Vector4(pa.x, pa.y, pb.x, pb.y));
            }
        if (ends.Count == 0) return;

        // The maps on the route walked, lit under every door line.
        trailMapsRoot = new GameObject("RouteMaps", typeof(RectTransform)).GetComponent<RectTransform>();
        trailMapsRoot.SetParent(world.picture, false);
        Stretch(trailMapsRoot);

        var holder = new GameObject("Doors", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
        holder.SetParent(world.picture, false);
        Stretch(holder);
        AddDoorMeshes(holder, WorldMapDoors.Part.Lines, ends);
        AddDoorMeshes(holder, WorldMapDoors.Part.Squares, ends);
        trailLines = AddTrailMesh(holder, "RouteLines");
        trailSquares = AddTrailMesh(holder, "RouteDoors");
        world.marker.SetAsLastSibling();
    }

    static WorldMapDoors AddTrailMesh(RectTransform holder, string name)
    {
        var mesh = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(WorldMapDoors))
            .GetComponent<WorldMapDoors>();
        mesh.transform.SetParent(holder, false);
        Stretch(mesh.rectTransform);
        return mesh;
    }

    // ── world view: the route walked, lit from where you stand to where it leads ──────────────────────────────────
    // The same route the ground's arrows follow (QuestArrows): the maps it passes through, a line from you to each door
    // on the way and on to the place, and each door between two of those maps. Laid only when that route or the map you
    // are in changed — a handful of lines and squares in two meshes, nothing per frame.
    RectTransform trailMapsRoot;
    WorldMapDoors trailLines, trailSquares;
    readonly List<Image> trailMaps = new List<Image>();
    readonly List<string> trailChain = new List<string>();
    readonly List<Vector4> trailSegments = new List<Vector4>(), trailDoors = new List<Vector4>();
    int trailDrawnVersion = -1;
    string trailDrawnMap;
    // Below 1 the rim is drawn thicker: 0.4 is two and a half times its one-unit edge.
    const float TrailRimThinness = 0.4f;

    void UpdateTrail()
    {
        if (trailLines == null) return;
        string here = MapManager.CurrentMap;
        if (trailDrawnVersion == QuestArrows.RouteVersion && trailDrawnMap == here) return;
        trailDrawnVersion = QuestArrows.RouteVersion;
        trailDrawnMap = here;

        trailChain.Clear();
        trailSegments.Clear();
        trailDoors.Clear();
        if (!LayTrail(here)) { trailChain.Clear(); trailSegments.Clear(); trailDoors.Clear(); }

        trailLines.Show(WorldMapDoors.Part.Lines, trailSegments, 0, trailSegments.Count, data.worldW, data.worldH, lit: true);
        trailSquares.Show(WorldMapDoors.Part.Squares, trailDoors, 0, trailDoors.Count, data.worldW, data.worldH, lit: true);

        int used = 0;
        for (int i = 0; i < trailChain.Count; i++)
        {
            var e = data.Find(trailChain[i]);
            if (e == null) continue;
            var img = TrailMap(used++);
            float w = Mathf.Max(1f, data.worldW), h = Mathf.Max(1f, data.worldH);
            float left = (e.x - data.minX) / w, top = 1f - (e.y - data.minY) / h;
            img.rectTransform.anchorMin = new Vector2(left, top - e.h / h);
            img.rectTransform.anchorMax = new Vector2(left + e.w / w, top);
            img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
        }
        for (int i = used; i < trailMaps.Count; i++)
            if (trailMaps[i].gameObject.activeSelf) trailMaps[i].gameObject.SetActive(false);
    }

    Image TrailMap(int i)
    {
        while (trailMaps.Count <= i)
        {
            // A rim, not a fill, so the map's own name and picture stay readable under it.
            var img = NewImage("RouteMap", trailMapsRoot, YouAreHereColour);
            img.sprite = UiKit.RimSprite(5);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = TrailRimThinness;
            trailMaps.Add(img);
        }
        return trailMaps[i];
    }

    // The maps from `here` to the place, and the lines between: from you to the door into the next map, out of the door
    // you arrive by to the next door, and on to the place. False when there is no route, or no way between two maps.
    bool LayTrail(string here)
    {
        string dest = QuestArrows.DestinationMap;
        if (QuestArrows.Route.Count == 0 || string.IsNullOrEmpty(dest) || data.Find(here) == null) return false;
        var start = MapManager.InMap(new Vector3(QuestArrows.RouteStart.x, 0f, QuestArrows.RouteStart.y), here);
        Vector2 at = new Vector2(start.x, start.z);
        string map = here;
        trailChain.Add(here);
        while (map != dest)
        {
            if (trailChain.Count > data.maps.Count) return false;
            string next = map == here ? QuestArrows.NextMapOnTheWay : WorldRoutes.NextMap(map, dest, out _);
            if (string.IsNullOrEmpty(next) || !NearestDoor(map, next, at, out Vector2 exit, out Vector2 arrive)) return false;
            var from = data.Find(map);
            var to = data.Find(next);
            if (from == null || to == null) return false;
            Vector2 pAt = OnWorldPicture(from, at.x, at.y), pExit = OnWorldPicture(from, exit.x, exit.y);
            Vector2 pArrive = OnWorldPicture(to, arrive.x, arrive.y);
            trailSegments.Add(new Vector4(pAt.x, pAt.y, pExit.x, pExit.y));
            var door = new Vector4(pExit.x, pExit.y, pArrive.x, pArrive.y);
            trailSegments.Add(door);
            trailDoors.Add(door);
            trailChain.Add(next);
            map = next;
            at = arrive;
        }
        var last = data.Find(dest);
        Vector2 a = OnWorldPicture(last, at.x, at.y), b = OnWorldPicture(last, QuestArrows.DestinationSpot.x, QuestArrows.DestinationSpot.y);
        trailSegments.Add(new Vector4(a.x, a.y, b.x, b.y));
        return true;
    }

    // Of the doors between `map` and `next`, the one standing nearest `at` in `map`: where it stands there, and where
    // its other end stands in `next` — each in metres east and north of that map's centre.
    bool NearestDoor(string map, string next, Vector2 at, out Vector2 exit, out Vector2 arrive)
    {
        exit = arrive = default;
        float best = float.MaxValue;
        bool any = false;
        if (data.links == null) return false;
        foreach (var l in data.links)
        {
            if (l == null) continue;
            Vector2 here, there;
            if (l.a == map && l.b == next) { here = new Vector2(l.ax, l.az); there = new Vector2(l.bx, l.bz); }
            else if (l.b == map && l.a == next) { here = new Vector2(l.bx, l.bz); there = new Vector2(l.ax, l.az); }
            else continue;
            float d = (here - at).sqrMagnitude;
            if (d >= best) continue;
            best = d; exit = here; arrive = there; any = true;
        }
        return any;
    }

    // A spot in a map, in metres east and north of the map's centre, as a share of the world picture from its left
    // and top edges.
    Vector2 OnWorldPicture(WorldAtlas.Entry map, float east, float north)
        => new Vector2((map.x + map.w * 0.5f + east - data.minX) / Mathf.Max(1f, data.worldW),
                       (map.y + map.h * 0.5f - north - data.minY) / Mathf.Max(1f, data.worldH));

    void AddDoorMeshes(RectTransform holder, WorldMapDoors.Part part, List<Vector4> ends)
    {
        int from = 0, corners = 0;
        for (int i = 0; i <= ends.Count; i++)
        {
            int next = i < ends.Count ? WorldMapDoors.CornersFor(part, ends[i], data.worldW, data.worldH) : 0;
            if (i < ends.Count && corners + next <= WorldMapDoors.MostCorners) { corners += next; continue; }
            if (i > from)
            {
                var mesh = new GameObject(part.ToString(), typeof(RectTransform), typeof(CanvasRenderer), typeof(WorldMapDoors))
                    .GetComponent<WorldMapDoors>();
                mesh.transform.SetParent(holder, false);
                Stretch(mesh.rectTransform);
                mesh.Show(part, ends, from, i, data.worldW, data.worldH);
            }
            from = i;
            corners = next;
        }
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

        var mk = NewImage("YouAreHere", image.transform, YouAreHereColour);
        v.marker = mk.rectTransform;
        v.marker.anchorMin = v.marker.anchorMax = v.marker.pivot = new Vector2(0.5f, 0.5f);
        v.marker.sizeDelta = new Vector2(MarkerSize, MarkerSize);
        return image;
    }

    // A map pin — a round head with a hole, narrowing to a point — baked once when the map is built.
    // ── tiny uGUI helpers ─────────────────────────────────────────────────────────────────────────────────────────
    static Image NewImage(string name, Transform parent, Color c) => NoClicks(UiKit.Img(name, parent, c));
    static Image NewImage(string name, Transform parent, ThemeColor c) => NoClicks(UiKit.Img(name, parent, c));

    static Image NoClicks(Image img)
    {
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text NewText(string name, Transform parent, string s, int size, TextAnchor anchor)
    {
        var t = UiKit.MakeText(name, parent, s, size, FontStyle.Normal, anchor);
        UiKit.Paint(t, UiKit.TextOnDim);
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
}
