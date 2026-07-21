#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Dev-only on-screen performance probe — the cheap, glanceable read that makes the recurring perf
/// checkup (see <c>PERF_LOG.md</c> and the <c>/perf-checkup</c> command) sustainable. No more Profiler
/// expeditions for a routine "is anything allocating?" glance.
///
/// Shows: FPS / frame-ms, GC bytes allocated PER SECOND (the health metric — should sit near 0 during
/// combat), the worst single-frame GC since reset (allocations are bursty, so peak catches them), and
/// the live mob count.
///
/// Rendered with a <b>uGUI + TextMeshPro</b> label, NOT IMGUI: the text is rewritten only 4×/sec, and the
/// canvas draws every frame with ZERO per-frame C# allocation. (The old IMGUI <c>OnGUI</c> version allocated
/// ~15 KB/frame in its own draw — enough to swamp the very GC/s number it reports; see memory
/// perfhud-imgui-self-pollution.) The measurement itself is allocation-free (<see cref="ProfilerRecorder"/>),
/// so the HUD is now an honest "~0 or clearly not?" indicator.
///
/// Compiled ONLY into the editor / development builds (a release player carries zero overhead). Self-boots
/// with no scene wiring, its own overlay canvas on top of everything and non-interactive (no raycaster, so it
/// never eats game clicks). Toggle with F3; reset the peak with F4.
/// </summary>
public class PerfHud : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("PerfHud") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);   // dev tool only — intentionally exempt from the no-DontDestroyOnLoad rule
        go.AddComponent<PerfHud>();
    }

    static readonly Color CleanColor = new Color(0.6f, 1f, 0.6f);   // green: near-0 GC
    static readonly Color BusyColor  = new Color(1f, 0.8f, 0.3f);   // amber: GC/s climbed past ~4 KB

    ProfilerRecorder gcRecorder;
    bool show = true;

    float avgMs;
    long allocThisFrame, peakFrame, secAccum, gcPerSec;
    float secTimer, textTimer;
    bool lastBusy;

    readonly StringBuilder sb = new StringBuilder(160);
    GameObject canvasGo;
    TextMeshProUGUI label;

    void OnEnable() => gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
    void OnDisable() => gcRecorder.Dispose();

    static PerfHud sInstance;
    static bool sHidden;   // externally suppressed (e.g. hidden during an NPC dialogue) — ANDed with the F3 `show` state

    /// <summary>Hide/show the perf overlay from outside (e.g. while an NPC conversation hides the HUD). Restores to
    /// whatever the F3 toggle last left it when un-hidden.</summary>
    public static void SetHidden(bool h) { sHidden = h; if (sInstance != null) sInstance.Apply(); }

    void Apply() { if (canvasGo != null) canvasGo.SetActive(show && !sHidden); }

    void Start()
    {
        sInstance = this;
        BuildUi();
        Rebuild();
        Apply();
    }

    void Update()
    {
        float ms = Time.unscaledDeltaTime * 1000f;
        avgMs = avgMs <= 0f ? ms : Mathf.Lerp(avgMs, ms, 0.1f);   // smooth the FPS readout

        if (gcRecorder.Valid)
        {
            allocThisFrame = gcRecorder.LastValue;
            if (allocThisFrame > peakFrame) peakFrame = allocThisFrame;
            secAccum += allocThisFrame;
        }

        secTimer += Time.unscaledDeltaTime;
        if (secTimer >= 1f) { gcPerSec = secAccum; secAccum = 0; secTimer = 0f; }   // roll up to bytes/sec

        if (Pressed(KeyCode.F3)) { show = !show; Apply(); }
        if (Pressed(KeyCode.F4)) peakFrame = 0;

        // Rewrite the label only 4×/sec (a uGUI text assignment allocates + rebuilds its mesh, so we keep it
        // off the per-frame path); the canvas itself renders every frame allocation-free.
        if (!show) return;
        textTimer += Time.unscaledDeltaTime;
        if (textTimer >= 0.25f) { textTimer = 0f; Rebuild(); }
    }

    void Rebuild()
    {
        if (label == null) return;
        int mobs = MonsterAI3D.Active != null ? MonsterAI3D.Active.Count : 0;
        float fps = avgMs > 0.0001f ? 1000f / avgMs : 0f;
        sb.Clear();
        sb.Append("PERF  (F3 hide  F4 reset)\n");
        sb.Append("FPS  ").Append(Mathf.RoundToInt(fps)).Append("   ").Append(avgMs.ToString("0.0")).Append(" ms\n");
        sb.Append("GC/s ").Append(Bytes(gcPerSec)).Append('\n');
        sb.Append("peak ").Append(Bytes(peakFrame)).Append(" /frame\n");
        sb.Append("mobs ").Append(mobs);
        label.SetText(sb);   // SetText(StringBuilder) writes into TMP's char buffer without allocating a string

        bool busy = gcPerSec > 4096;
        if (busy != lastBusy) { label.color = busy ? BusyColor : CleanColor; lastBusy = busy; }
    }

    // Build the overlay canvas + panel + label once. Non-interactive (no GraphicRaycaster) so it can't steal clicks.
    void BuildUi()
    {
        canvasGo = new GameObject("PerfHudCanvas") { hideFlags = HideFlags.DontSaveInEditor };
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;   // above the game HUD

        var panel = new GameObject("Panel", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        panel.transform.SetParent(canvasGo.transform, false);
        var pr = (RectTransform)panel.transform;
        pr.anchorMin = pr.anchorMax = new Vector2(0f, 1f);   // top-left corner
        pr.pivot = new Vector2(0f, 1f);
        pr.anchoredPosition = new Vector2(8f, -8f);
        pr.sizeDelta = new Vector2(210f, 92f);
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        bg.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        textGo.transform.SetParent(panel.transform, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(8f, 5f); tr.offsetMax = new Vector2(-4f, -3f);
        label = textGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = 15f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.color = CleanColor;
        label.raycastTarget = false;
        label.richText = false;
    }

    static string Bytes(long b)
    {
        if (b <= 0) return "0 B";
        if (b < 1024) return b + " B";
        if (b < 1048576) return (b / 1024f).ToString("0.0") + " KB";
        return (b / 1048576f).ToString("0.0") + " MB";
    }

    static bool Pressed(KeyCode key) => Hotkeys.Down(key);
}
#endif
