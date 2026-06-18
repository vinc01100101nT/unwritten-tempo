#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using Unity.Profiling;
using UnityEngine;
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
/// The measurement itself is allocation-free (Unity's <see cref="ProfilerRecorder"/>). The only thing
/// the HUD allocates is rebuilding its little text 4×/sec, which adds a small steady floor to the GC/s
/// reading — so treat the HUD as a "roughly 0, or clearly not?" indicator. For the precise per-system
/// number, use Profiler ▸ Hierarchy: the HUD is its own marker and never pollutes the
/// <c>MonsterAI3D.Update</c> row you actually trust.
///
/// Compiled ONLY into the editor / development builds (a release player carries zero overhead). Self-
/// boots with no scene wiring. Toggle with F3; reset the peak with F4.
/// </summary>
public class PerfHud : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("PerfHud");
        DontDestroyOnLoad(go);   // dev tool only — intentionally exempt from the no-DontDestroyOnLoad rule
        go.AddComponent<PerfHud>();
    }

    ProfilerRecorder gcRecorder;
    bool show = true;

    float avgMs;
    long allocThisFrame, peakFrame, secAccum, gcPerSec;
    float secTimer, textTimer;

    readonly StringBuilder sb = new StringBuilder(160);
    string cached = "";
    GUIStyle style;

    void OnEnable() => gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
    void OnDisable() => gcRecorder.Dispose();

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

        if (Pressed(KeyCode.F3)) show = !show;
        if (Pressed(KeyCode.F4)) peakFrame = 0;

        // Rebuild the display text only 4×/sec, so the HUD's own allocation stays a small steady trickle.
        textTimer += Time.unscaledDeltaTime;
        if (textTimer >= 0.25f) { textTimer = 0f; Rebuild(); }
    }

    void Rebuild()
    {
        int mobs = MonsterAI3D.Active != null ? MonsterAI3D.Active.Count : 0;
        float fps = avgMs > 0.0001f ? 1000f / avgMs : 0f;
        sb.Clear();
        sb.Append("PERF  (F3 hide  F4 reset)\n");
        sb.Append("FPS  ").Append(Mathf.RoundToInt(fps)).Append("   ").Append(avgMs.ToString("0.0")).Append(" ms\n");
        sb.Append("GC/s ").Append(Bytes(gcPerSec)).Append('\n');
        sb.Append("peak ").Append(Bytes(peakFrame)).Append(" /frame\n");
        sb.Append("mobs ").Append(mobs);
        cached = sb.ToString();
    }

    void OnGUI()
    {
        if (!show || cached.Length == 0) return;
        if (style == null)
            style = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };

        // Green = clean; amber once GC/s climbs past ~4 KB during play (worth opening the Profiler).
        style.normal.textColor = gcPerSec > 4096 ? new Color(1f, 0.8f, 0.3f) : new Color(0.6f, 1f, 0.6f);

        var rect = new Rect(8, 8, 210, 92);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 8, rect.y + 5, rect.width - 12, rect.height - 8), cached, style);
    }

    static string Bytes(long b)
    {
        if (b <= 0) return "0 B";
        if (b < 1024) return b + " B";
        if (b < 1048576) return (b / 1024f).ToString("0.0") + " KB";
        return (b / 1048576f).ToString("0.0") + " MB";
    }

    static bool Pressed(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
            return key == KeyCode.F3 ? Keyboard.current.f3Key.wasPressedThisFrame
                 : key == KeyCode.F4 && Keyboard.current.f4Key.wasPressedThisFrame;
#endif
        return Input.GetKeyDown(key);
    }
}
#endif
