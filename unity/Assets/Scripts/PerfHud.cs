using System.Text;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The on-screen PERFORMANCE PANEL, in every build: Settings ▸ Graphics ▸ Show performance (or F10) switches it on and
/// off, so a player can see what their own computer is managing while they tune the other graphics settings.
///
/// Shows frames a second and each frame's time; the main thread's and the graphics card's time per frame; the memory
/// used against the computer's; and the graphics memory used against the card's. Each line is coloured by how close it
/// is to what the computer can handle — green, yellow, orange, red (<see cref="Shade"/>). Editor and development
/// builds also show the developer lines: memory churn per second (the health metric — near 0 during combat), its worst
/// single frame since reset (F11), and the creature count.
///
/// Rendered with a <b>uGUI + TextMeshPro</b> label, NOT IMGUI: the text is rewritten only 4×/sec, and the canvas draws
/// every frame with ZERO per-frame C# allocation (the old IMGUI version allocated ~15 KB a frame; see memory
/// perfhud-imgui-self-pollution). Every reading is allocation-free (<see cref="ProfilerRecorder"/>,
/// <see cref="FrameTimingManager"/>), and nothing is read while the panel is hidden.
/// </summary>
public class PerfHud : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("PerfHud") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);   // a screen overlay that outlives every map — exempt from the no-DontDestroyOnLoad rule
        go.AddComponent<PerfHud>();
    }

    // THE BUDGETS each line is coloured against. A frame has 16.7 ms at the game's 60 frames a second.
    const float FpsGood = 55f, FpsFair = 45f, FpsPoor = 30f;
    const float MsGood = 12f, MsFair = 16.7f, MsPoor = 25f;
    const float ShareGood = 0.5f, ShareFair = 0.7f, SharePoor = 0.85f;

    ProfilerRecorder systemMemory, graphicsMemory;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    ProfilerRecorder gcRecorder;
    long allocThisFrame, peakFrame, secAccum, gcPerSec;
    float secTimer;
#endif
    readonly FrameTiming[] timing = new FrameTiming[1];
    float avgMs, cpuMs, gpuMs;
    float textTimer;

    // The four shades as rich-text colour tags, from the game's own palette, worked out once.
    string good, fair, poor, bad;

    readonly StringBuilder sb = new StringBuilder(256);
    GameObject canvasGo;
    RectTransform panel;
    TextMeshProUGUI label;

    static PerfHud sInstance;
    static bool sHidden;   // externally suppressed (e.g. hidden during an NPC dialogue) — ANDed with the setting

    /// <summary>Hide/show the panel from outside (e.g. while an NPC conversation hides the HUD). Restores to the
    /// player's setting when un-hidden.</summary>
    public static void SetHidden(bool h) { sHidden = h; if (sInstance != null) sInstance.Apply(); }

    bool Showing => GameSettings.ShowPerformance && !sHidden;

    void Awake()
    {
        if (GameServer.RemovesPresentation(this)) return;
    }

    void OnEnable()
    {
        GameSettings.ShowPerformanceChanged += Apply;
        if (canvasGo != null) Apply();
    }

    void OnDisable()
    {
        GameSettings.ShowPerformanceChanged -= Apply;
        StopReading();
    }

    void Start()
    {
        sInstance = this;
        good = Tag(UiKit.NoticeSuccess.Value);
        fair = Tag(UiKit.NoticeWarning.Value);
        poor = Tag(UiKit.RarityLegendary.Value);
        bad = Tag(UiKit.NoticeFailure.Value);
        BuildUi();
        Apply();
    }

    static string Tag(Color c) => "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">";

    // The panel shows, and its readings run, only while the player wants it.
    void Apply()
    {
        if (canvasGo == null) return;
        bool on = Showing;
        canvasGo.SetActive(on);
        if (on) StartReading(); else StopReading();
        if (on) { textTimer = 1f; }
    }

    void StartReading()
    {
        if (!systemMemory.Valid) systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        if (!graphicsMemory.Valid) graphicsMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!gcRecorder.Valid) gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
#endif
    }

    void StopReading()
    {
        systemMemory.Dispose();
        graphicsMemory.Dispose();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        gcRecorder.Dispose();
#endif
    }

    void Update()
    {
        if (Hotkeys.Down(GameKeys.PerformanceOverlay)) GameSettings.ShowPerformance = !GameSettings.ShowPerformance;
        if (!Showing) return;

        float ms = Time.unscaledDeltaTime * 1000f;
        avgMs = avgMs <= 0f ? ms : Mathf.Lerp(avgMs, ms, 0.1f);   // smooth the FPS readout

        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
        {
            cpuMs = Smooth(cpuMs, (float)timing[0].cpuMainThreadFrameTime);
            if (timing[0].gpuFrameTime > 0) gpuMs = Smooth(gpuMs, (float)timing[0].gpuFrameTime);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (gcRecorder.Valid)
        {
            allocThisFrame = gcRecorder.LastValue;
            if (allocThisFrame > peakFrame) peakFrame = allocThisFrame;
            secAccum += allocThisFrame;
        }
        secTimer += Time.unscaledDeltaTime;
        if (secTimer >= 1f) { gcPerSec = secAccum; secAccum = 0; secTimer = 0f; }   // roll up to bytes/sec
        if (Hotkeys.Down(GameKeys.PerformancePeakReset)) peakFrame = 0;
#endif

        // Rewrite the label only 4×/sec (a uGUI text assignment rebuilds its mesh), so it stays off the per-frame path.
        textTimer += Time.unscaledDeltaTime;
        if (textTimer >= 0.25f) { textTimer = 0f; Rebuild(); }
    }

    static float Smooth(float was, float now) => was <= 0f ? now : Mathf.Lerp(was, now, 0.1f);

    void Rebuild()
    {
        if (label == null) return;
        float fps = avgMs > 0.0001f ? 1000f / avgMs : 0f;
        sb.Clear();
        sb.Append("Performance  (F10)\n");

        sb.Append(fps >= FpsGood ? good : fps >= FpsFair ? fair : fps >= FpsPoor ? poor : bad);
        sb.Append("FPS   ").Append(Mathf.RoundToInt(fps)).Append("   ").Append(avgMs.ToString("0.0")).Append(" ms</color>\n");

        sb.Append(ForMs(cpuMs)).Append("CPU   ").Append(cpuMs.ToString("0.0")).Append(" ms</color>\n");
        if (gpuMs > 0f) sb.Append(ForMs(gpuMs)).Append("GPU   ").Append(gpuMs.ToString("0.0")).Append(" ms</color>\n");
        else sb.Append(good).Append("GPU   not reported</color>\n");

        Memory("RAM   ", systemMemory, SystemInfo.systemMemorySize);
        Memory("VRAM  ", graphicsMemory, SystemInfo.graphicsMemorySize);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int mobs = MonsterAI3D.Active != null ? MonsterAI3D.Active.Count : 0;
        sb.Append(gcPerSec > 4096 ? fair : good).Append("GC/s  ").Append(Bytes(gcPerSec)).Append("</color>\n");
        sb.Append(good).Append("peak  ").Append(Bytes(peakFrame)).Append(" /frame  (F11)</color>\n");
        sb.Append(good).Append("mobs  ").Append(mobs).Append("</color>");
#endif
        label.SetText(sb);   // SetText(StringBuilder) writes into TMP's char buffer without allocating a string
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, label.preferredHeight + 10f);
    }

    string ForMs(float ms) => ms < MsGood ? good : ms < MsFair ? fair : ms < MsPoor ? poor : bad;

    // One memory line: used of the computer's total, coloured by the share used. A reading the platform does not give
    // says so rather than showing 0.
    void Memory(string name, ProfilerRecorder used, int totalMb)
    {
        if (!used.Valid || used.LastValue <= 0 || totalMb <= 0)
        {
            sb.Append(good).Append(name).Append("not reported</color>\n");
            return;
        }
        float share = used.LastValue / (totalMb * 1048576f);
        sb.Append(share < ShareGood ? good : share < ShareFair ? fair : share < SharePoor ? poor : bad);
        sb.Append(name).Append((used.LastValue / 1073741824f).ToString("0.0")).Append(" / ")
          .Append((totalMb / 1024f).ToString("0.0")).Append(" GB</color>\n");
    }

    // Build the overlay canvas + panel + label once. Non-interactive (no GraphicRaycaster) so it can't steal clicks.
    void BuildUi()
    {
        canvasGo = new GameObject("PerfHudCanvas") { hideFlags = HideFlags.DontSaveInEditor };
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;   // above the game HUD

        var panelGo = new GameObject("Panel", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        panelGo.transform.SetParent(canvasGo.transform, false);
        panel = (RectTransform)panelGo.transform;
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);   // top-left corner
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(8f, -8f);
        panel.sizeDelta = new Vector2(230f, 140f);
        var bg = panelGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        bg.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        textGo.transform.SetParent(panel, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(8f, 5f); tr.offsetMax = new Vector2(-4f, -3f);
        label = textGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = 15f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.color = UiKit.TextMain.Value;
        label.raycastTarget = false;
        label.richText = true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static string Bytes(long b)
    {
        if (b <= 0) return "0 B";
        if (b < 1024) return b + " B";
        if (b < 1048576) return (b / 1024f).ToString("0.0") + " KB";
        return (b / 1048576f).ToString("0.0") + " MB";
    }
#endif
}
