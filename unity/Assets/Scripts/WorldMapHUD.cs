using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen world-map overlay. Shows the baked <see cref="WorldAtlas"/> (a top-down snapshot of every map
/// composited by grid cell) with a pulsing "you are here" marker over the map you're currently in — placed by
/// the active scene's grid cell plus the player's sub-cell position. Toggle with <see cref="toggleKey"/>
/// (default <b>N</b>; <b>M</b> is the corner minimap). Esc closes.
///
/// Self-installing: a runtime bootstrap spawns it into the persistent <c>Systems</c> scene (so it survives map
/// swaps with the rest of the HUD, no DontDestroyOnLoad) the moment a <see cref="WorldAtlas"/> exists in
/// Resources. It builds its own overlay Canvas in code, so there's no UI-shell rebuild to run.
/// </summary>
public class WorldMapHUD : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.N;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<WorldMapHUD>() != null) return;       // already installed this session
        if (Resources.Load<WorldAtlas>("WorldAtlas") == null) return;   // no world baked yet

        var go = new GameObject("WorldMapHUD");
        var systems = SceneManager.GetSceneByName(MapManager.SystemsScene);
        if (systems.IsValid() && systems.isLoaded) SceneManager.MoveGameObjectToScene(go, systems);
        else DontDestroyOnLoad(go);
        go.AddComponent<WorldMapHUD>();
    }

    WorldAtlas data;
    GameObject panel;
    RawImage atlasImage;
    RectTransform marker;
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
        if (Hotkeys.Down(toggleKey)) Toggle();
        if (panel == null || !panel.activeSelf) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { panel.SetActive(false); return; }
        UpdateMarker();
    }

    void Toggle()
    {
        bool open = !panel.activeSelf;
        if (open) FitAtlas();
        panel.SetActive(open);
        if (open) UpdateMarker();
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

        float pulse = 1f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
        marker.localScale = new Vector3(pulse, pulse, 1f);
        if (whereLabel) whereLabel.text = scene;
    }

    // Fit the atlas into ~82% of the screen, preserving its aspect.
    void FitAtlas()
    {
        float aw = data.atlas.width, ah = data.atlas.height;
        float scale = Mathf.Min(Screen.width * 0.82f / aw, Screen.height * 0.82f / ah);
        atlasImage.rectTransform.sizeDelta = new Vector2(aw * scale, ah * scale);
    }

    // ── UI construction (own overlay canvas, in code) ─────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("WorldMapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;                                  // above the rest of the HUD
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        UiKit.ApplyTextCrispness(scaler);

        panel = new GameObject("WorldMapPanel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        Stretch(panel.GetComponent<RectTransform>());

        // dim, click-blocking backdrop
        var dim = NewImage("Dim", panel.transform, new Color(0f, 0f, 0f, 0.8f));
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;

        // the atlas image, centred
        atlasImage = new GameObject("Atlas", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage))
            .GetComponent<RawImage>();
        atlasImage.transform.SetParent(panel.transform, false);
        atlasImage.texture = data.atlas;
        var art = atlasImage.rectTransform;
        art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0.5f);
        FitAtlas();

        // thin frame behind the atlas (a slightly larger dark rect)
        var frame = NewImage("Frame", panel.transform, new Color(0.15f, 0.16f, 0.18f, 1f));
        frame.rectTransform.anchorMin = frame.rectTransform.anchorMax = frame.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        frame.rectTransform.sizeDelta = atlasImage.rectTransform.sizeDelta + new Vector2(8f, 8f);
        frame.transform.SetSiblingIndex(atlasImage.transform.GetSiblingIndex());   // behind the atlas

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
        var hint = NewText("Hint", panel.transform, $"[{toggleKey}] / [Esc] to close", 16, TextAnchor.LowerCenter);
        hint.color = new Color(1f, 1f, 1f, 0.6f);
        Bottom(hint.rectTransform, 34f, 420f);
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
        t.color = Color.white;
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
