using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Top-right HUD minimap for the 2.5D world. The backdrop comes from one of two sources, picked
/// automatically per map; the moving <b>blips</b> layer on top is identical either way:
///
///  • <b>Baked image (preferred)</b> — if the loaded map has a <see cref="MinimapData"/> (a top-down
///    snapshot baked at author-time by the Bake Minimap tool), the HUD shows that PNG and pans/zooms a
///    UV window around the player. No second camera runs — basically free. This is the RO-style chart look.
///  • <b>Live camera (fallback)</b> — for maps with no bake yet, a runtime orthographic camera mounted
///    above the player renders the terrain into a <see cref="RenderTexture"/>. Created lazily and only
///    while it's actually needed.
///
/// In BOTH modes the billboards (player + mobs) are edge-on from straight above, so units are shown as
/// projected UI <b>blips</b> (green = player, red = mobs from <see cref="MonsterAI3D.Active"/>), centred
/// on the player and scaled by <see cref="viewRadius"/>. Fixed-north, like the classic RO minimap.
///
/// Built onto the persistent HUD by <see cref="UIShellBuilder"/>, so it survives map swaps with the rest
/// of the UI. Performance (HARD constraint — see PERFORMANCE.md): the live camera is disabled and driven
/// by a throttled manual <c>Render()</c>; blips reuse a pooled <see cref="Image"/> set + scratch — 0 B/frame.
/// </summary>
public class Minimap : MonoBehaviour
{
    [Header("Wired by UIShellBuilder")]
    public RawImage view;          // backdrop surface (baked image or live RT)
    public RectTransform blipRoot; // clipped container the dots live in

    [Header("Tuning")]
    [Tooltip("Half-extent (world units) the minimap shows around the player. Smaller = more zoomed-in.")]
    public float viewRadius = 20f;
    [Tooltip("How high above the player the live fallback camera sits.")]
    public float camHeight = 120f;
    [Tooltip("Live-camera re-render cap (Hz). 0 = follow the game's frame rate (so the terrain pans as smoothly " +
             "as the world moves, up to 120). Blips/baked-image pan always track every frame regardless.")]
    public float renderHz = 20f;
    [Tooltip("Upper bound on the live re-render rate when following the frame rate, so it never exceeds the world cap.")]
    public float maxRenderHz = 120f;
    public int textureSize = 256;
    public KeyCode toggleKey = KeyCode.M;

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

    static readonly Rect FullRect = new Rect(0, 0, 1, 1);

    Camera cam;                    // live fallback only (lazy)
    RenderTexture rt;              // live fallback only (lazy)
    Transform player;
    Player3DController playerCtrl; // cached for WorldFacing (orients the arrow)
    float nextRender;

    readonly List<Image> pool = new List<Image>();
    Sprite dotSprite;
    Sprite arrowSprite;            // player blip: a triangle that points where the player faces

    // Where you stand, written along the map's bottom edge — rewritten only when a whole number changes.
    TMPro.TextMeshProUGUI coordLabel;
    int lastX = int.MinValue, lastY = int.MinValue, lastZ = int.MinValue;

    void Start()
    {
        // A server has no screen and a dedicated server build ships no shaders, so asking the render pipeline
        // to draw the map throws — every frame, forever. Stand down the same way this already does when its
        // refs are missing: the map still exists on the server, nobody is looking at a picture of it. A client
        // started with no window (a player reading the game as text) has no screen either, and its map camera
        // fails the same way, about thirty times a second into its log.
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
    }

    // Built here while the game runs, never saved into the scene: a strip along the bottom of the map view.
    void BuildCoordLabel()
    {
        var strip = new GameObject("Coords", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { hideFlags = HideFlags.DontSaveInEditor };
        strip.transform.SetParent(Panel.transform, false);
        var bg = strip.GetComponent<Image>();
        bg.color = UiKit.PanelFill;
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
        Vector3 p = player.position;
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
        if (cam != null) Destroy(cam.gameObject);
        if (rt != null) { rt.Release(); Destroy(rt); }
    }

    GameObject Panel => view.transform.parent.gameObject;   // the themed HUD_Minimap frame

    void LateUpdate()
    {
        if (view == null) return;

        if (Hotkeys.Down(toggleKey)) UiAnim.Toggle(Panel);
        if (!Panel.activeInHierarchy) return;

        // Re-acquire the persistent player after a scene swap.
        if (player == null && PersistentPlayer3D.Instance != null)
        {
            player = PersistentPlayer3D.Instance.transform;
            playerCtrl = player.GetComponent<Player3DController>();
        }
        if (player == null) return;

        var data = MinimapData.Current;
        if (data != null && data.image != null) RenderStatic(data);
        else                                     RenderLive();

        PlaceBlips();
        if (coordLabel != null) RefreshCoords();
    }

    // ---- backdrop: baked image (pan/zoom a UV window around the player) --------
    void RenderStatic(MinimapData data)
    {
        if (view.texture != data.image) view.texture = data.image;

        float size = Mathf.Max(1f, data.worldSize);
        Vector3 p = player.position;
        float u = (p.x - data.worldMin.x) / size;     // player position as 0..1 across the map
        float v = (p.z - data.worldMin.y) / size;
        float hw = viewRadius / size;                 // half-window in UV
        view.uvRect = new Rect(u - hw, v - hw, 2f * hw, 2f * hw);
    }

    // ---- backdrop: live fallback camera ---------------------------------------
    void RenderLive()
    {
        EnsureCamera();
        if (view.texture != rt) { view.texture = rt; view.uvRect = FullRect; }

        Vector3 p = player.position;
        cam.transform.position = new Vector3(p.x, p.y + camHeight, p.z);
        cam.orthographicSize = viewRadius;

        if (Time.unscaledTime >= nextRender)
        {
            // renderHz <= 0 → match the world's frame rate (the game targets 60; -1 = display refresh), so the
            // terrain pans in lock-step with movement instead of stepping at a fixed low rate.
            float hz = renderHz > 0f
                ? renderHz
                : Mathf.Clamp(Application.targetFrameRate > 0 ? Application.targetFrameRate : maxRenderHz, 30f, maxRenderHz);
            nextRender = Time.unscaledTime + 1f / Mathf.Max(1f, hz);
            // URP/Unity 6: manual Camera.Render() is unsupported — use a render request (with a legacy fallback).
            var req = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, req)) { req.destination = rt; RenderPipeline.SubmitRenderRequest(cam, req); }
            else cam.Render();
        }
    }

    void EnsureCamera()
    {
        if (cam != null) return;

        rt = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.Default)
        { name = "MinimapRT", useMipMap = false, autoGenerateMips = false };
        rt.Create();

        var camGo = new GameObject("MinimapCamera") { hideFlags = HideFlags.DontSaveInEditor };
        cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = viewRadius;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = camHeight + 200f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.09f, 1f);
        // Exclude UI and the weather overlay quads (rain / wet-puddles / snow all live on TransparentFX)
        // so the minimap shows clean terrain, never the storm.
        cam.cullingMask = ~((1 << LayerMask.NameToLayer("UI")) | (1 << LayerMask.NameToLayer("TransparentFX")));
        cam.allowMSAA = false;
        cam.allowHDR = false;
        cam.targetTexture = rt;
        cam.enabled = false;          // we call Render() ourselves on a throttle
        camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // straight down, fixed-north
    }

    // ---- blips (shared by both modes) -----------------------------------------
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
