using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// THE MAP'S NAME, FLASHED ON ARRIVAL. Every time the map you stand in changes, its name (from the world layout, through
/// <see cref="WorldAtlas.NameOf"/>) is shown big across the upper middle of the screen in the colour of its kind
/// (<see cref="MapKinds"/> — the same colour its doors carry): it drifts down into place while its letters close up from
/// wide apart and a thin line grows out from under it to both sides, holds, then fades out as the letters spread again.
///
/// <para>Draws only; catches no clicks. One text and two lines, built once, and touched only while the name is
/// showing — the rest of the time a frame costs one number compared.</para>
/// </summary>
public class ZoneTitle : MonoBehaviour
{
    // The whole flash, in seconds: in, held, out.
    const float InFor = 0.7f, HoldFor = 2.2f, OutFor = 0.8f;
    // The letters start this far apart (in the text's spacing units) and close up to none.
    const float SpreadFrom = 38f, SpreadTo = 0f, SpreadOut = 22f;
    // How far above its resting place the name starts, on the screen layer (laid out as if every screen were 1280 wide).
    const float DropFrom = 26f;
    const float FontSize = 46f, LineWidth = 360f, LineThickness = 2f, LineGap = 34f, FromTop = 150f;

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

    // Installed with every Systems scene, not once per launch: leaving the world reloads that scene and takes this with
    // it (the same reason the map window installs itself this way).
    static void Install()
    {
        var systems = SceneManager.GetSceneByName(MapManager.SystemsScene);
        foreach (var existing in FindObjectsByType<ZoneTitle>(FindObjectsInactive.Include))
            if (existing != null && (existing.gameObject.scene == systems || !systems.IsValid())) return;
        var go = new GameObject("ZoneTitle");
        if (systems.IsValid() && systems.isLoaded) SceneManager.MoveGameObjectToScene(go, systems);
        else DontDestroyOnLoad(go);
        go.AddComponent<ZoneTitle>();
    }

    WorldAtlas atlas;
    RectTransform root;
    TextMeshProUGUI title;
    Image lineLeft, lineRight;
    // The map change last flashed; none yet, so the map you are already standing in when this starts is flashed too.
    int shownVersion = -1;
    float startedAt = -1f;

    void Start()
    {
        // A server draws nothing.
        if (GameServer.IsServer && !GameNetwork.TheWorldIsElsewhere) { enabled = false; return; }
        atlas = Resources.Load<WorldAtlas>("WorldAtlas");
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    void Update()
    {
        if (shownVersion != MapManager.MapVersion && !string.IsNullOrEmpty(MapManager.CurrentMap))
        {
            shownVersion = MapManager.MapVersion;
            Begin(MapManager.CurrentMap);
        }
        if (startedAt >= 0f) Step(Time.unscaledTime - startedAt);
    }

    void Begin(string map)
    {
        if (root == null && !Build()) return;
        var entry = atlas != null ? atlas.Find(map) : null;
        Color c = MapKinds.ColorOfType(entry != null ? entry.type : null);
        title.text = atlas != null ? atlas.NameOf(map) : map;
        title.color = c;
        lineLeft.color = c;
        lineRight.color = c;
        root.gameObject.SetActive(true);
        startedAt = Time.unscaledTime;
        Step(0f);
    }

    void Step(float t)
    {
        var f = ZoneTitleMath.At(t, InFor, HoldFor, OutFor);
        if (f.done)
        {
            startedAt = -1f;
            root.gameObject.SetActive(false);
            return;
        }
        title.alpha = f.alpha;
        title.characterSpacing = f.closing < 1f ? Mathf.Lerp(SpreadFrom, SpreadTo, f.closing)
                                                : Mathf.Lerp(SpreadTo, SpreadOut, f.opening);
        title.rectTransform.anchoredPosition = new Vector2(0f, DropFrom * (1f - f.closing));
        float half = LineWidth * 0.5f * f.line;
        SetLine(lineLeft, half, f.alpha);
        SetLine(lineRight, half, f.alpha);
    }

    static void SetLine(Image line, float length, float alpha)
    {
        var rt = line.rectTransform;
        rt.sizeDelta = new Vector2(length, LineThickness);
        var c = line.color; c.a = alpha; line.color = c;
    }

    bool Build()
    {
        var canvas = UiKit.HudCanvas;
        if (canvas == null) return false;
        root = new GameObject("ZoneTitle", typeof(RectTransform)).GetComponent<RectTransform>();
        root.gameObject.hideFlags = HideFlags.DontSaveInEditor;
        root.SetParent(canvas.transform, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -FromTop);
        root.sizeDelta = new Vector2(1000f, 80f);

        title = UiKit.MakeText("Name", root, "", FontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
        title.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.Font, 0.25f);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.raycastTarget = false;
        var trt = title.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(1000f, 70f);

        // Two lines meeting under the middle of the name, each growing outward from there.
        lineLeft = Line("LineLeft", new Vector2(1f, 0.5f));
        lineRight = Line("LineRight", new Vector2(0f, 0.5f));
        root.gameObject.SetActive(false);
        return true;
    }

    Image Line(string name, Vector2 pivot)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(root, false);
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot;
        rt.anchoredPosition = new Vector2(0f, -LineGap);
        return img;
    }
}

/// <summary>Where the arrival flash is at a moment of it. Numbers in, numbers out.</summary>
public static class ZoneTitleMath
{
    public struct Frame
    {
        /// <summary>How visible it is, 0 to 1.</summary>
        public float alpha;
        /// <summary>How far the letters have closed up and the name dropped into place, 0 to 1 (eased).</summary>
        public float closing;
        /// <summary>How far the letters have spread again on the way out, 0 to 1 (eased).</summary>
        public float opening;
        /// <summary>How far the lines under it have grown, 0 to 1 (eased; they finish a little after the name).</summary>
        public float line;
        /// <summary>The flash is over.</summary>
        public bool done;
    }

    /// <summary>The flash <paramref name="t"/> seconds after it began: <paramref name="inFor"/> seconds coming in,
    /// <paramref name="holdFor"/> held, <paramref name="outFor"/> going out.</summary>
    public static Frame At(float t, float inFor, float holdFor, float outFor)
    {
        var f = new Frame();
        if (t >= inFor + holdFor + outFor) { f.done = true; return f; }
        float into = Mathf.Clamp01(t / Mathf.Max(0.0001f, inFor));
        float outOf = Mathf.Clamp01((t - inFor - holdFor) / Mathf.Max(0.0001f, outFor));
        f.closing = EaseOutCubic(into);
        f.opening = EaseInCubic(outOf);
        f.line = EaseOutCubic(Mathf.Clamp01((t - inFor * 0.3f) / Mathf.Max(0.0001f, inFor)));
        f.alpha = Mathf.Min(EaseOutCubic(into), 1f - outOf);
        return f;
    }

    static float EaseOutCubic(float x) { float u = 1f - x; return 1f - u * u * u; }
    static float EaseInCubic(float x) => x * x * x;
}
