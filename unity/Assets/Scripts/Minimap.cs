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
    [Tooltip("Live-camera re-render rate (Hz). Blips track every frame; only the live terrain pass is throttled.")]
    public float renderHz = 12f;
    public int textureSize = 256;
    public KeyCode toggleKey = KeyCode.M;

    [Header("Blip colours")]
    public Color playerColor = new Color(0.35f, 1f, 0.45f, 1f);
    public Color mobColor    = new Color(1f, 0.30f, 0.30f, 1f);
    public Color portalColor = new Color(0.45f, 0.75f, 1f, 1f);
    public float blipSize    = 7f;
    [Tooltip("Portal blips are drawn this much bigger than unit blips (and clamped to the map edge when off-screen).")]
    public float portalBlipScale = 1.6f;

    static readonly Rect FullRect = new Rect(0, 0, 1, 1);

    Camera cam;                    // live fallback only (lazy)
    RenderTexture rt;              // live fallback only (lazy)
    Transform player;
    float nextRender;

    readonly List<Image> pool = new List<Image>();
    Sprite dotSprite;

    void Start()
    {
        if (view == null || blipRoot == null)
        {
            Debug.LogWarning("[unwritten] Minimap is missing its view/blipRoot refs — rebuild the UI shell " +
                             "(Tools ▸ unwritten ▸ Build UI Shell).");
            enabled = false;
            return;
        }
        var tex = Texture2D.whiteTexture;   // shared 1px sprite for all blips
        dotSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
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

        if (Input.GetKeyDown(toggleKey)) Panel.SetActive(!Panel.activeSelf);
        if (!Panel.activeInHierarchy) return;

        // Re-acquire the persistent player after a scene swap.
        if (player == null && PersistentPlayer3D.Instance != null)
            player = PersistentPlayer3D.Instance.transform;
        if (player == null) return;

        var data = MinimapData.Current;
        if (data != null && data.image != null) RenderStatic(data);
        else                                     RenderLive();

        PlaceBlips();
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
            nextRender = Time.unscaledTime + 1f / Mathf.Max(1f, renderHz);
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

        var camGo = new GameObject("MinimapCamera");
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

        // Player blip — always dead-centre (it's the projection origin).
        var pb = Blip(used++);
        pb.color = playerColor;
        pb.rectTransform.sizeDelta = new Vector2(blipSize, blipSize);
        pb.rectTransform.anchoredPosition = Vector2.zero;

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

            var b = Blip(used++);
            b.color = mobColor;
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
            var go = new GameObject("Blip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
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
