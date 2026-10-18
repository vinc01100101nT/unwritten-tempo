using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-right HUD minimap for the 2.5D world. The backdrop is the current map's own picture, drawn once in the editor
/// straight down from above (<see cref="MinimapCatalog"/>): it is loaded when you enter a map, let go when you leave,
/// and slid under you as you walk. No camera renders the world for it.
///
/// On top of it, the billboards (player + mobs) are edge-on from straight above, so units are shown as projected UI
/// <b>blips</b> (green = player, red = mobs from <see cref="MonsterAI3D.Active"/>), centred on the player and scaled by
/// <see cref="viewRadius"/>. Fixed-north, like the classic RO minimap. Always shown — there is no key to hide it.
///
/// Under the dots, the followed quest's whole route is drawn as the same arrows the ground shows
/// (<see cref="QuestArrows.Marks"/>), placed each frame like the dots so they slide with the map.
///
/// Built onto the persistent HUD by <see cref="UIShellBuilder"/>, so it survives map swaps with the rest of the UI.
/// Performance (HARD constraint — see PERFORMANCE.md): a map change loads one picture; every other frame only moves a
/// UV window, and blips reuse a pooled <see cref="Image"/> set + scratch — 0 B/frame.
/// </summary>
public class Minimap : MonoBehaviour
{
    [Header("Wired by UIShellBuilder")]
    public RawImage view;          // backdrop surface: the current map's picture
    public RectTransform blipRoot; // clipped container the dots live in

    [Header("Tuning")]
    [Tooltip("Half-extent (world units) the minimap shows around the player. Smaller = more zoomed-in.")]
    public float viewRadius = 20f;
    [Tooltip("The closest the + button zooms in: the fewest world units shown from the player to the edge.")]
    public float minViewRadius = 8f;
    [Tooltip("The farthest the - button zooms out: the most world units shown from the player to the edge.")]
    public float maxViewRadius = 60f;
    [Tooltip("How much one press of + or - zooms.")]
    public float zoomStep = 1.5f;

    [Header("Blip colours")]
    public Color playerColor = new Color(0.35f, 1f, 0.45f, 1f);
    public Color mobColor    = new Color(1f, 0.30f, 0.30f, 1f);
    public Color eliteColor  = new Color(0.70f, 0.35f, 1f, 1f);
    public Color shadowColor = new Color(1f, 0.60f, 0.15f, 1f);
    [Tooltip("Every other person in your map.")]
    public Color otherPlayerColor = Color.white;
    public Color portalColor = new Color(0.45f, 0.75f, 1f, 1f);
    public float blipSize    = 7f;
    [Tooltip("Portal blips are drawn this much bigger than unit blips (and clamped to the map edge when off-screen).")]
    public float portalBlipScale = 1.6f;

    Transform player;
    Player3DController playerCtrl; // cached for WorldFacing (orients the arrow)

    // The map whose picture is showing, by MapManager.MapVersion — a map change is noticed by one integer compare.
    int shownVersion = -1;
    MinimapCatalog.Entry shown;
    Texture2D picture;
    string pictureMap;

    // The route's arrows, in a layer under every dot, each drawn with the ground arrow's own picture.
    RectTransform routeRoot;
    readonly List<Image> routePool = new List<Image>();
    Sprite routeSprite;
    const float RouteMarkSize = 12f;

    readonly List<Image> pool = new List<Image>();
    Sprite dotSprite;
    Sprite arrowSprite;            // player blip: a triangle that points where the player faces

    // Where you stand, written along the map's bottom edge — rewritten only when a whole number changes.
    TMPro.TextMeshProUGUI coordLabel;
    int lastX = int.MinValue, lastY = int.MinValue, lastZ = int.MinValue;

    void Start()
    {
        // A server has no screen, and neither has a client started with no window (a player reading the game as
        // text): nobody is looking at a minimap there, so it stands down rather than load pictures nobody sees.
        if (GameServer.IsServer || Application.isBatchMode) { enabled = false; return; }

        if (view == null || blipRoot == null)
        {
            GameLog.Warn(LogCat.Unwritten, "Minimap is missing its view/blipRoot refs — rebuild the UI shell " +
                             "(Tools ▸ unwritten ▸ Build UI Shell).");
            enabled = false;
            return;
        }
        var tex = Texture2D.whiteTexture;   // shared 1px sprite for all blips
        dotSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        arrowSprite = BuildArrowSprite();   // the player marker — rotated to its facing each frame
        BuildCoordLabel();
        BuildZoomButtons();

        routeSprite = Resources.Load<Sprite>("Guide/GroundArrow");
        routeRoot = new GameObject("Route", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor }
            .GetComponent<RectTransform>();
        routeRoot.SetParent(blipRoot, false);
        routeRoot.SetAsFirstSibling();
        routeRoot.anchorMin = Vector2.zero; routeRoot.anchorMax = Vector2.one;
        routeRoot.offsetMin = routeRoot.offsetMax = Vector2.zero;
    }

    // + and - in the map's top-right corner. Built while the game runs, never saved into the scene. The map stays
    // centred on the player, so zooming always closes in on exactly where they stand.
    void BuildZoomButtons()
    {
        var vrt = view.rectTransform;
        AddZoomButton("+", 1f / zoomStep, vrt, 0f);
        AddZoomButton("\u2212", zoomStep, vrt, -20f);
    }

    void AddZoomButton(string label, float factor, RectTransform vrt, float down)
    {
        var btn = UiKit.Btn(label, Panel.transform, () =>
            viewRadius = Mathf.Clamp(viewRadius * factor, minViewRadius, maxViewRadius));
        btn.gameObject.hideFlags = HideFlags.DontSaveInEditor;
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(vrt.anchorMax.x, vrt.anchorMax.y);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(18f, 18f);
        rt.anchoredPosition = new Vector2(vrt.offsetMax.x - 3f, vrt.offsetMax.y - 3f + down);
    }

    // Built here while the game runs, never saved into the scene: a strip along the bottom of the map view.
    void BuildCoordLabel()
    {
        var strip = new GameObject("Coords", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { hideFlags = HideFlags.DontSaveInEditor };
        strip.transform.SetParent(Panel.transform, false);
        var bg = strip.GetComponent<Image>();
        UiKit.Paint(bg, UiKit.PanelFill);
        bg.raycastTarget = false;
        var srt = bg.rectTransform;
        var vrt = view.rectTransform;
        srt.anchorMin = vrt.anchorMin;
        srt.anchorMax = new Vector2(vrt.anchorMax.x, vrt.anchorMin.y);
        srt.pivot = new Vector2(0.5f, 0f);
        srt.offsetMin = vrt.offsetMin;
        srt.offsetMax = new Vector2(vrt.offsetMax.x, vrt.offsetMin.y + 14f);

        coordLabel = UiKit.Label("Text", strip.transform, "", 10, FontStyle.Normal, TextAnchor.MiddleCenter);
        coordLabel.raycastTarget = false;
        var trt = coordLabel.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
    }

    void RefreshCoords()
    {
        // YOUR OWN position, which your machine moves (the one thing it may) — shown, never sent anywhere.
        Vector3 p = Coords.ToShown(player.position);
        int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y), z = Mathf.RoundToInt(p.z);
        if (x == lastX && y == lastY && z == lastZ) return;
        lastX = x; lastY = y; lastZ = z;
        coordLabel.text = "X " + x + "  Y " + y + "  Z " + z;
    }

    // A small upward-pointing triangle (apex at top) baked once. The player blip uses this and we spin its
    // RectTransform to the player's heading, so it reads as a directional arrow rather than a dot.
    static Sprite BuildArrowSprite()
    {
        const int N = 32;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "MinimapArrow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        Color32 clear = new Color32(255, 255, 255, 0), solid = new Color32(255, 255, 255, 255);
        for (int y = 0; y < N; y++)
        {
            // Widen from the apex (top, y=N-1) down to the base (bottom). A slight notch on the trailing
            // edge would need a concave test; a clean triangle reads fine at blip size.
            float t = 1f - (y / (float)(N - 1));          // 0 at apex → 1 at base
            float halfW = t * (N * 0.42f);
            for (int x = 0; x < N; x++)
            {
                float dx = Mathf.Abs(x - (N - 1) * 0.5f);
                px[y * N + x] = dx <= halfW ? solid : clear;
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
    }

    void OnDestroy()
    {
        MinimapCatalog.ReleasePicture(pictureMap);
    }

    GameObject Panel => view.transform.parent.gameObject;   // the themed HUD_Minimap frame

    void LateUpdate()
    {
        if (view == null) return;

        if (!Panel.activeInHierarchy) return;

        // Re-acquire the persistent player after a scene swap.
        if (player == null && PersistentPlayer3D.Instance != null)
        {
            player = PersistentPlayer3D.Instance.transform;
            playerCtrl = player.GetComponent<Player3DController>();
        }
        if (player == null) return;

        ShowBackdrop();

        PlaceRoute();
        PlaceBlips();
        if (coordLabel != null) RefreshCoords();
    }

    // ---- backdrop: the map's picture (pan/zoom a UV window around the player) ----
    void ShowBackdrop()
    {
        if (MapManager.MapVersion != shownVersion)
        {
            shownVersion = MapManager.MapVersion;
            SwapPicture(MapManager.CurrentMap);
        }
        if (shown == null) return;

        float size = Mathf.Max(1f, shown.worldSize);
        // Where you stand INSIDE the map, the same coordinates the picture was drawn in.
        Vector3 p = MapManager.InMap(player.position, MapManager.CurrentMap);
        float u = (p.x - shown.worldMin.x) / size;     // player position as 0..1 across the map
        float v = (p.z - shown.worldMin.y) / size;
        float hw = viewRadius / size;                  // half-window in UV
        view.uvRect = new Rect(u - hw, v - hw, 2f * hw, 2f * hw);
    }

    // Let the last map's picture go and load this one's. A map nobody has drawn shows no backdrop — only the blips —
    // and says so once, so the missing picture is found rather than guessed at.
    void SwapPicture(string map)
    {
        MinimapCatalog.ReleasePicture(pictureMap);
        picture = null;
        pictureMap = null;
        var catalog = MinimapCatalog.Instance;
        shown = catalog != null ? catalog.Find(map) : null;
        if (shown != null) { picture = MinimapCatalog.AcquirePicture(map); if (picture != null) pictureMap = map; }
        if (picture == null)
        {
            shown = null;
            if (!string.IsNullOrEmpty(map))
                GameLog.Warn(LogCat.Unwritten, "No minimap picture for map '" + map + "' — its scene has not been drawn "
                                               + "yet (Tools ▸ unwritten ▸ World ▸ Bake Minimaps).");
        }
        view.texture = picture;
        view.enabled = picture != null;
    }

    // ---- the route ---------------------------------------------------------------
    // Every mark inside the shown square, re-placed each frame from the player's spot — the picture slides under the
    // dots by its texture window, so anything drawn over it must move each frame too. No allocation: a pool, grown once.
    void PlaceRoute()
    {
        if (routeRoot == null) return;
        var marks = QuestArrows.Marks;
        Vector2 half = blipRoot.rect.size * 0.5f;
        Vector3 c = player.position;
        float r2 = viewRadius * viewRadius;
        int used = 0;
        for (int i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            float dx = m.x - c.x, dz = m.y - c.z;
            if (dx * dx + dz * dz > r2 || !Project(dx, dz, half, out Vector2 sp)) continue;
            var img = RouteMark(used++);
            img.rectTransform.anchoredPosition = sp;
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, m.z);
        }
        for (int i = used; i < routePool.Count; i++)
            if (routePool[i].enabled) routePool[i].enabled = false;
    }

    Image RouteMark(int i)
    {
        while (routePool.Count <= i)
        {
            var go = new GameObject("RouteMark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { hideFlags = HideFlags.DontSaveInEditor };
            go.transform.SetParent(routeRoot, false);
            var img = go.GetComponent<Image>();
            img.sprite = routeSprite;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(RouteMarkSize, RouteMarkSize);
            routePool.Add(img);
        }
        var mark = routePool[i];
        if (!mark.enabled) mark.enabled = true;
        return mark;
    }

    // ---- blips -----------------------------------------------------------------
    void PlaceBlips()
    {
        Vector2 half = blipRoot.rect.size * 0.5f;
        int used = 0;

        Vector3 c = player.position;

        // Player marker — an arrow at dead-centre (the projection origin), spun to the player's heading.
        // Fixed-north top-down: world +X → map right, world +Z → map up, so the heading is read straight off
        // worldFacing's XZ. The arrow sprite points up (+Y), i.e. 90° from +X, so subtract that.
        var pb = Blip(used++);
        pb.sprite = arrowSprite;
        pb.color = playerColor;
        float arrowSize = blipSize * 1.7f;     // a touch bigger than a dot so the heading reads clearly
        pb.rectTransform.sizeDelta = new Vector2(arrowSize, arrowSize);
        pb.rectTransform.anchoredPosition = Vector2.zero;
        Vector3 f = playerCtrl != null ? playerCtrl.WorldFacing : player.forward;
        float angle = (f.x * f.x + f.z * f.z) > 1e-6f ? Mathf.Atan2(f.z, f.x) * Mathf.Rad2Deg - 90f : 0f;
        pb.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);

        // Mobs from the shared registry; distance-cull to the view box (cheap, no alloc).
        float r2 = viewRadius * viewRadius;
        var active = MonsterAI3D.Active;
        for (int i = 0; i < active.Count; i++)
        {
            var m = active[i];
            if (m == null) continue;
            if (m.Health != null && m.Health.IsDead) continue;
            Vector3 mp = m.transform.position;
            float dx = mp.x - c.x, dz = mp.z - c.z;
            if (dx * dx + dz * dz > r2) continue;            // outside the shown area
            if (!Project(dx, dz, half, out Vector2 sp)) continue;

            // Coloured by its kind as the server sent it; a body with no shell yet reads as an ordinary creature.
            var shell = NetMob.ShellForView(m.gameObject);
            var kind = shell != null ? shell.Kind : CreatureKind.Normal;
            var b = Blip(used++);
            b.color = kind == CreatureKind.Shadow ? shadowColor : kind == CreatureKind.Elite ? eliteColor : mobColor;
            b.rectTransform.sizeDelta = new Vector2(blipSize, blipSize);
            b.rectTransform.anchoredPosition = sp;
        }

        // Every other person this machine is drawing — only ever the ones in your map.
        var folk = NetPlayer.WatchedBodies;
        for (int i = 0; i < folk.Count; i++)
        {
            var h = folk[i];
            if (h == null || h.IsDead) continue;
            Vector3 op = h.transform.position;
            float dx = op.x - c.x, dz = op.z - c.z;
            if (dx * dx + dz * dz > r2) continue;
            if (!Project(dx, dz, half, out Vector2 sp)) continue;

            var b = Blip(used++);
            b.color = otherPlayerColor;
            b.rectTransform.sizeDelta = new Vector2(blipSize, blipSize);
            b.rectTransform.anchoredPosition = sp;
        }

        // Portals from their registry. Drawn last (on top) and bigger; clamped to the edge when off-screen
        // so the exit's direction is always visible — it's a navigation marker, not a threat.
        float pSize = blipSize * portalBlipScale;
        var portals = Portal3D.Active;
        for (int i = 0; i < portals.Count; i++)
        {
            var pt = portals[i];
            if (pt == null) continue;
            Vector3 pp = pt.transform.position;
            ProjectClamped(pp.x - c.x, pp.z - c.z, half, out Vector2 sp);

            var b = Blip(used++);
            b.color = portalColor;
            b.rectTransform.sizeDelta = new Vector2(pSize, pSize);
            b.rectTransform.anchoredPosition = sp;
        }

        for (int i = used; i < pool.Count; i++)              // hide leftovers from a busier frame
            if (pool[i].enabled) pool[i].enabled = false;
    }

    // World offset from the player → blipRoot local point. [-viewRadius, viewRadius] maps to the rect edges.
    bool Project(float dx, float dz, Vector2 half, out Vector2 local)
    {
        float sx = dx / viewRadius, sy = dz / viewRadius;    // -1..1 at the edges
        local = new Vector2(sx * half.x, sy * half.y);
        return Mathf.Abs(sx) <= 1f && Mathf.Abs(sy) <= 1f;
    }

    // Like Project, but always returns a point — when the target is outside the view box it's clamped onto
    // the edge (keeping its direction), so portal markers never disappear off the map.
    void ProjectClamped(float dx, float dz, Vector2 half, out Vector2 local)
    {
        float sx = dx / viewRadius, sy = dz / viewRadius;
        float m = Mathf.Max(Mathf.Abs(sx), Mathf.Abs(sy));
        if (m > 1f) { sx /= m; sy /= m; }                    // push onto the ring at the same heading
        local = new Vector2(sx * half.x, sy * half.y);
    }

    Image Blip(int i)
    {
        while (pool.Count <= i)
        {
            var go = new GameObject("Blip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { hideFlags = HideFlags.DontSaveInEditor };
            go.transform.SetParent(blipRoot, false);
            var img = go.GetComponent<Image>();
            img.sprite = dotSprite;
            img.raycastTarget = false;
            var brt = img.rectTransform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);   // measured from map centre
            brt.sizeDelta = new Vector2(blipSize, blipSize);
            pool.Add(img);
        }
        var b = pool[i];
        if (!b.enabled) b.enabled = true;
        return b;
    }
}
