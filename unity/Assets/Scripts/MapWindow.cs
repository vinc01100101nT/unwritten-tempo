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

    // The doors of the map the zone view shows: a mark in the colour of the map each leads to, its name beside it.
    // Laid when the map or the picture's size changed, resized only when the zoom did. A click on one opens that map.
    sealed class DoorMark { public Image mark; public TMP_Text label; public string leadsTo; }
    RectTransform doorRoot;
    readonly List<DoorMark> doorMarks = new List<DoorMark>();
    int doorsShown;
    string doorsDrawnMap;
    Vector2 doorsDrawnSize;
    float doorsDrawnZoom = -1f;

    // The spot picked to be led to (MapGuide), as a pin on the zone view of its map.
    Image guidePin;
    int guideDrawnVersion = -1;
    string guideDrawnMap;
    Vector2 guideDrawnSize;
    float guideDrawnZoom = -1f;
    Sprite pinSprite;

    // The creature counts the list was last laid out with.
    int rosterCountsVersion = -1;

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
    const float MarkerSize = 11f, RouteMarkSize = 14f, DoorMarkSize = 12f, DoorLabelSize = 11f, GuidePinSize = 22f;

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

        if (active == world) { UpdateWorldMarker(); UpdateHover(); } else { UpdateZone(); UpdateMobCard(); }
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
        HideHover();
        HideMobCard();
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
            doorsDrawnMap = null;
            guideDrawnVersion = -1;
            MapCreatureCounts.Ask(map);
            rosterCountsVersion = MapCreatureCounts.Version;
            zoneRoster.Show(null, map, panelRect.rect.height - ZoneRosterTop - ZoneRosterBottom);
            HideMobCard();
        }
        UpdateZone();
    }

    void Activate(View v)
    {
        active = v;
        gliding = false;
        world.zoomRoot.gameObject.SetActive(v == world);
        zone.zoomRoot.gameObject.SetActive(v == zone);
        zoneRoster.SetVisible(v == zone && zoneRoster.HasPeople);
        if (v != world) HideHover();
        if (v != zone) HideMobCard();
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
            if (map != null) hoverRoster.Show(map, map, panelRect.rect.height - 2f * HoverEdge);
            else hoverRoster.SetVisible(false);
        }
        if (hoverMap != null && hoverRoster.HasPeople) PlaceBesidePointer(hoverRoster.Root, pointer);
    }

    void HideHover()
    {
        hoverMap = null;
        hoverAsked = false;
        if (hoverRoster != null) hoverRoster.SetVisible(false);
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
            var under = pointerOverMap ? zoneRoster.MobUnder(pointer) : null;
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
        DrawDoors();
        DrawGuidePin();
        // The counts beside the names come from the server, asked again every few seconds while the map is shown; the
        // list is laid out again only when an answer arrives.
        MapCreatureCounts.Ask(zoneMap);
        // The screen changed size, or new counts arrived: the list is laid out again.
        float rosterRoom = panelRect.rect.height - ZoneRosterTop - ZoneRosterBottom;
        if (!Mathf.Approximately(rosterRoom, zoneRoster.LaidForHeight) || rosterCountsVersion != MapCreatureCounts.Version)
        {
            rosterCountsVersion = MapCreatureCounts.Version;
            zoneRoster.Show(null, zoneMap, rosterRoom);
            HideMobCard();
        }
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
        while (doorMarks.Count <= i)
        {
            var mark = NewImage("Door", doorRoot, Color.white);
            var mrt = mark.rectTransform;
            mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.sizeDelta = new Vector2(DoorMarkSize, DoorMarkSize);
            mark.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var label = NewText("DoorName", doorRoot, "", (int)DoorLabelSize, TextAnchor.UpperCenter);
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var lrt = label.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            // The name hangs just under its mark, centred on it: the pivot sits above the text by most of a mark, so
            // the gap shrinks with the zoom exactly as the mark does.
            const float labelHeight = 20f;
            lrt.pivot = new Vector2(0.5f, 1f + DoorMarkSize * 0.75f / labelHeight);
            lrt.sizeDelta = new Vector2(220f, labelHeight);
            doorMarks.Add(new DoorMark { mark = mark, label = label });
            doorsDrawnZoom = -1f;
        }
        var d = doorMarks[i];
        if (!d.mark.gameObject.activeSelf) d.mark.gameObject.SetActive(true);
        if (!d.label.gameObject.activeSelf) d.label.gameObject.SetActive(true);
        return d;
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

    /// <summary>The wheel, the drag and the click, caught by the black backdrop that covers the whole screen.</summary>
    sealed class MapInput : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IPointerClickHandler,
                            IPointerMoveHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public MapWindow owner;
        public void OnScroll(PointerEventData e) => owner.ZoomAt(e.position, e.scrollDelta.y);
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

    // ── UI construction, on the main screen layer ─────────────────────────────────────────────────────────────────
    void BuildUI()
    {
        panel = new GameObject("MapWindow", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        panel.transform.SetParent(canvas.transform, false);
        panelRect = panel.GetComponent<RectTransform>();
        Stretch(panelRect);

        // solid black, click-blocking backdrop — and the thing the wheel, the drag and the click land on
        var dim = NewImage("Dim", panel.transform, Color.black);
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;
        dim.gameObject.AddComponent<MapInput>().owner = this;

        atlasImage = BuildView(world, "World", data.atlas, (float)data.atlas.width / Mathf.Max(1, data.atlas.height));
        zoneImage = BuildView(zone, "Zone", null, 1f);
        BuildDoors();

        routeSprite = Resources.Load<Sprite>("Guide/GroundArrow");
        routeRoot = new GameObject("Route", typeof(RectTransform)).GetComponent<RectTransform>();
        routeRoot.SetParent(zone.picture, false);
        routeRoot.anchorMin = routeRoot.anchorMax = routeRoot.pivot = new Vector2(0.5f, 0.5f);

        doorRoot = new GameObject("Doors", typeof(RectTransform)).GetComponent<RectTransform>();
        doorRoot.SetParent(zone.picture, false);
        doorRoot.anchorMin = doorRoot.anchorMax = doorRoot.pivot = new Vector2(0.5f, 0.5f);

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

        zoneMissing = NewText("NoPicture", panel.transform, "This map has no picture yet.", 16, TextAnchor.MiddleCenter);
        Stretch(zoneMissing.rectTransform);

        title = NewText("Title", panel.transform, "", 22, TextAnchor.UpperCenter);
        Top(title.rectTransform, 24f, 420f);
        whereLabel = NewText("Where", panel.transform, "", 14, TextAnchor.LowerCenter);
        Bottom(whereLabel.rectTransform, 43f, 420f);
        var hint = NewText("Hint", panel.transform, $"[{GameKeys.Label(GameKeys.Map)}] / [Esc] to close", 11, TextAnchor.LowerCenter);
        UiKit.Paint(hint, UiKit.TextOnDim, 0.6f);
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
        pin.sprite = pinSprite;
        pin.rectTransform.sizeDelta = new Vector2(24f, 24f);
        locateButton = locate.gameObject;
        locateButton.SetActive(false);

        zoneRoster = new RosterCard(panel.transform);
        var zrt = zoneRoster.Root;
        zrt.anchorMin = zrt.anchorMax = zrt.pivot = new Vector2(1f, 1f);
        zrt.anchoredPosition = new Vector2(-24f, -ZoneRosterTop);
        hoverRoster = new RosterCard(panel.transform);
        hoverRoster.Root.anchorMin = hoverRoster.Root.anchorMax = new Vector2(0.5f, 0.5f);

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
    }

    // Where the zone view's list sits: below the title, above the location button.
    const float ZoneRosterTop = 64f, ZoneRosterBottom = 76f;
    // The hover list's distance from the pointer, and the least it keeps from the screen's edge.
    const float HoverGap = 16f, HoverEdge = 12f;

    /// <summary>
    /// WHO LIVES IN ONE MAP — its mobs, then its NPCs, each with its level (<see cref="UnitLabel.WithLevel"/>), read
    /// from the two lists of who lives where (<see cref="WorldSpawns.CreaturesIn"/>, <see cref="WorldTownsfolk.PeopleIn"/>).
    /// A list taller than the room it is given carries on in another column to the right. It catches no clicks, so
    /// the map under it still zooms, drags and picks. Hidden while the map has nobody.
    /// </summary>
    sealed class RosterCard
    {
        public RectTransform Root { get; }
        public bool HasPeople { get; private set; }
        public float LaidForHeight { get; private set; } = -1f;

        const float Pad = 10f, ColumnGap = 18f, EntrySize = 13f, HeadSize = 14f;

        readonly TMP_Text title;
        readonly List<TMP_Text> columns = new List<TMP_Text>();
        readonly List<MonsterDef> mobs = new List<MonsterDef>();
        readonly List<WorldTownsfolk.Row> people = new List<WorldTownsfolk.Row>();
        readonly List<string> lines = new List<string>();
        readonly List<bool> heads = new List<bool>();
        // The creature each line names (null for a heading, a blank or a person), and the line each shown column opens on.
        readonly List<MonsterDef> lineMobs = new List<MonsterDef>();
        readonly List<int> columnStarts = new List<int>();
        int shownColumns;
        readonly System.Text.StringBuilder sb = new System.Text.StringBuilder();
        readonly string headOpen;

        public RosterCard(Transform parent)
        {
            var card = UiKit.Card(UiKit.Img("Roster", parent, Color.white));
            foreach (var g in card.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            Root = card.rectTransform;
            title = UiKit.MakeText("Map", Root, "", HeadSize, FontStyle.Bold, TextAnchor.UpperLeft);
            UiKit.Paint(title, UiKit.TitleText);
            title.raycastTarget = false;
            Corner(title.rectTransform);
            headOpen = "<b><color=" + UiKit.Hex(UiKit.TitleText) + ">";
            Root.gameObject.SetActive(false);
        }

        public void SetVisible(bool on)
        {
            bool want = on && HasPeople;
            if (Root.gameObject.activeSelf != want) Root.gameObject.SetActive(want);
        }

        /// <summary>Lay out who lives in <paramref name="map"/> under <paramref name="heading"/> (none when null),
        /// at most <paramref name="maxHeight"/> tall, and show it when anybody lives there.</summary>
        public void Show(string heading, string map, float maxHeight)
        {
            LaidForHeight = maxHeight;
            WorldSpawns.CreaturesIn(map, mobs);
            WorldTownsfolk.PeopleIn(map, people);
            HasPeople = mobs.Count > 0 || people.Count > 0;
            if (!HasPeople) { shownColumns = 0; SetVisible(false); return; }

            lines.Clear();
            heads.Clear();
            lineMobs.Clear();
            columnStarts.Clear();
            if (mobs.Count > 0)
            {
                Add("Mobs", true);
                // How many are alive there right now over how many the map keeps, as the server last said
                // (MapCreatureCounts) — alive alone for a creature only a random mix puts there; no number until it has.
                foreach (var m in mobs)
                    Add(UnitLabel.WithLevel(m.displayName, m.level) + (m.kind == CreatureKind.Elite ? " (Elite)" : "")
                        + (!MapCreatureCounts.TryGet(map, m.id, out int alive, out int kept) ? ""
                           : kept > 0 ? "  " + alive + "/" + kept : "  " + alive), false, m);
            }
            if (people.Count > 0)
            {
                if (lines.Count > 0) Add("", false);
                Add("NPCs", true);
                foreach (var p in people) Add(UnitLabel.WithLevel(p.name, p.level), false);
            }

            bool titled = !string.IsNullOrEmpty(heading);
            title.gameObject.SetActive(titled);
            float top = Pad;
            if (titled)
            {
                title.text = heading;
                Vector2 t = title.GetPreferredValues(heading);
                title.rectTransform.anchoredPosition = new Vector2(Pad, -Pad);
                title.rectTransform.sizeDelta = t;
                top += t.y + 4f;
            }

            float lineHeight = Column(0).GetPreferredValues("Ag").y;
            int perColumn = Mathf.Max(2, Mathf.FloorToInt((maxHeight - top - Pad) / Mathf.Max(1f, lineHeight)));

            int used = 0, at = 0;
            float x = Pad, tallest = 0f, widest = titled ? title.rectTransform.sizeDelta.x : 0f;
            while (at < lines.Count)
            {
                // A blank line never opens a column, and a heading never closes one.
                if (lines[at].Length == 0) { at++; continue; }
                int end = Mathf.Min(lines.Count, at + perColumn);
                if (end < lines.Count && end - at > 1 && heads[end - 1]) end--;
                if (end < lines.Count && end - at > 2 && lines[end - 1].Length == 0) end--;

                sb.Clear();
                for (int i = at; i < end; i++)
                {
                    if (i > at) sb.Append('\n');
                    if (heads[i]) sb.Append(headOpen).Append(lines[i]).Append("</color></b>");
                    else sb.Append(lines[i]);
                }
                columnStarts.Add(at);
                var col = Column(used++);
                col.gameObject.SetActive(true);
                col.text = sb.ToString();
                Vector2 size = col.GetPreferredValues(col.text);
                col.rectTransform.anchoredPosition = new Vector2(x, -top);
                col.rectTransform.sizeDelta = size;
                x += size.x + ColumnGap;
                tallest = Mathf.Max(tallest, size.y);
                at = end;
            }
            for (int i = used; i < columns.Count; i++) columns[i].gameObject.SetActive(false);
            shownColumns = used;

            float width = Mathf.Max(x - ColumnGap + Pad, widest + 2f * Pad);
            Root.sizeDelta = new Vector2(width, top + tallest + Pad);
            SetVisible(true);
        }

        void Add(string line, bool head, MonsterDef mob = null) { lines.Add(line); heads.Add(head); lineMobs.Add(mob); }

        /// <summary>The creature whose line of the list is under <paramref name="screenPoint"/>; null over anything else.</summary>
        public MonsterDef MobUnder(Vector2 screenPoint)
        {
            if (!Root.gameObject.activeSelf) return null;
            for (int i = 0; i < shownColumns; i++)
            {
                var col = columns[i];
                if (!RectTransformUtility.RectangleContainsScreenPoint(col.rectTransform, screenPoint, null)) continue;
                int line = TMP_TextUtilities.FindIntersectingLine(col, screenPoint, null);
                int at = line >= 0 ? columnStarts[i] + line : -1;
                return at >= 0 && at < lineMobs.Count ? lineMobs[at] : null;
            }
            return null;
        }

        TMP_Text Column(int i)
        {
            while (columns.Count <= i)
            {
                var t = UiKit.MakeText("Column", Root, "", EntrySize, FontStyle.Normal, TextAnchor.UpperLeft);
                UiKit.Paint(t, UiKit.TextMain);
                t.raycastTarget = false;
                t.richText = true;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                Corner(t.rectTransform);
                columns.Add(t);
            }
            return columns[i];
        }

        static void Corner(RectTransform rt) => rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
    }

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

        var holder = new GameObject("Doors", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
        holder.SetParent(world.picture, false);
        Stretch(holder);
        AddDoorMeshes(holder, WorldMapDoors.Part.Lines, ends);
        AddDoorMeshes(holder, WorldMapDoors.Part.Squares, ends);
        world.marker.SetAsLastSibling();
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

        var mk = NewImage("YouAreHere", image.transform, new Color(1f, 0.92f, 0.25f, 1f));
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
