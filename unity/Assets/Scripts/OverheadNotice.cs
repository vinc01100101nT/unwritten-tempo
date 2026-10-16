using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// THE OVERHEAD NOTICE — the game's default flashing notification: a strip on the screen that STACKS the most recent
/// short messages about YOUR OWN doings. Two kinds go here and nowhere else:
/// <list type="bullet">
/// <item>a "you can't do that" warning (Silenced, No MP, Can't target mobs, Out of range, …) — <see cref="Warn"/>;</item>
/// <item>what a transaction came to — a buy, a sale, a buy-back, a save point, a trip, a storage move, a fold, a
/// quench, an Ink, a recycle, a merge, a quest's reward — the one sentence the server sent, which the chat keeps as
/// well — <see cref="Result"/>.</item>
/// </list>
/// <para><b>EVERY LINE IS SHORT — at most <see cref="MostCharacters"/> characters.</b> That is why it stacks: several
/// short lines read at a glance, a long one does not. Whoever sends a line writes its short form; the full sentence goes
/// to the chat (and to the window it is about), never here. A longer line is still shown, whole, and the editor's
/// console says which one broke the rule.</para>
///
/// Nothing spawns its own floating text for either. That keeps every such message in one styled, one-anchored place,
/// so re-styling or re-siting it is a one-file edit, and several in quick succession read as a legible stack.
///
/// <para>This is NOT floating combat text: <see cref="DamagePopup"/> stays the home for per-unit numbers/MISS that
/// belong ON the struck unit. An overhead notice is always about the PLAYER, and is read off the screen, not off a body.</para>
///
/// <para><b>Fixed on the screen, read whatever is open.</b> It sits centred, a little above the middle of the screen,
/// at one size, over every window (see <see cref="OverheadScreenLayer.AboveWindows"/>) — never on the character, so it
/// does not move, shrink or hide when the camera does. Three colours, nothing else: green for something done
/// (<see cref="Success"/>), red for something refused or broken (<see cref="Fail"/>), yellow for "you can't do that
/// right now" (<see cref="Warn"/>).</para>
///
/// PERFORMANCE: self-booting singleton with one screen canvas + a small pooled line set. Messages fire on an input or
/// a server's answer, never per-frame; the only steady work is easing ≤ <see cref="MaxLines"/> lines in LateUpdate while
/// any is showing — no allocation, no Find.
/// </summary>
[DisallowMultipleComponent]
public class OverheadNotice : MonoBehaviour
{
    /// <summary>Every message shown, as its text — for a player reading the game as text (AgentLink).</summary>
    public static event System.Action<string> Warned;

    /// <summary>"You can't do that right now" — Silenced, No MP, out of range — in yellow.</summary>
    public static void Warn(string message) => Show(message, UiKit.NoticeWarning);

    /// <summary>Something done — a trade, a fold, a step of a quest — in green.</summary>
    public static void Success(string message) => Show(message, UiKit.NoticeSuccess);

    /// <summary>Something refused or broken — a trade the server turned down, a move that failed — in red.</summary>
    public static void Fail(string message) => Show(message, UiKit.NoticeFailure);

    /// <summary>What a transaction came to, as the server worded it: done in green, refused in red.</summary>
    public static void Result(string line, bool ok)
    {
        if (ok) Success(line);
        else Fail(line);
    }

    /// <summary>The longest a line may be. Write the short form at the sender; the chat carries the whole sentence.</summary>
    public const int MostCharacters = 40;

    static void Show(string message, Color color)
    {
        if (string.IsNullOrEmpty(message)) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (message.Length > MostCharacters)
            Debug.LogWarning("[notice] longer than " + MostCharacters + " characters — give it a short form at the sender: " + message);
#endif
        // NOBODY TO TELL on a headless server — and the server never means to: a notice is a player's own screen's.
        // The twin of Sfx.Deaf and Vfx.Blind, written the same way.
        if (GameServer.CaughtDrawing("OverheadNotice")) return;
        Warned?.Invoke(message);
        Ensure();
        inst.Push(message, color);
    }

    // ---- tuning (in the HUD's own units: the screen is 1280 wide) ------------
    const int MaxLines = 5;          // how many can stack at once (oldest recycled beyond this)
    const float Life = 2.4f;         // seconds a line stays before it fades out
    const float FadeFor = 0.4f;      // seconds of fade at the end of Life
    const float FadeInFor = 0.2f;    // seconds of fade-IN at the start (new row eases in)
    // Where the newest line's bottom sits: centred, this far above the middle of the screen.
    const float AboveMiddle = 230f;
    const float FontSize = 18f;
    const float LineStep = 24f;      // the gap between stacked lines
    const float SlideIn = 18f;       // how far BELOW its slot a new row starts, so its rise is clearly visible
    const float EaseRate = 7f;       // lower = slower/softer glide toward the target slot (the visible push)

    // ---- singleton + shared canvas -------------------------------------------
    static OverheadNotice inst;
    Canvas canvas;
    // The strip every line hangs under, fixed on the screen.
    RectTransform strip;

    static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("OverheadNotice") { hideFlags = HideFlags.DontSaveInEditor };
        inst = go.AddComponent<OverheadNotice>();
        inst.Build();
    }

    void Build()
    {
        canvas = OverheadScreenLayer.CreateCanvas("OverheadNoticeCanvas", clickable: false, OverheadScreenLayer.AboveWindows);
        canvas.transform.SetParent(transform, false);
        strip = new GameObject("Strip", typeof(RectTransform)).GetComponent<RectTransform>();
        strip.SetParent(canvas.transform, false);
        strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
        strip.pivot = new Vector2(0.5f, 0f);
        strip.sizeDelta = Vector2.zero;
        strip.anchoredPosition = new Vector2(0f, AboveMiddle);
    }

    // ---- line pool ------------------------------------------------------------
    class Line { public GameObject go; public TMP_Text text; public float age; public string msg; public float slotY; }
    readonly List<Line> active = new List<Line>(MaxLines);
    readonly Stack<Line> pool = new Stack<Line>();

    void Push(string message, Color color)
    {
        // Chat-style: every message appends a fresh row at the bottom (repeats included). Oldest scrolls off once
        // the strip is full.
        if (active.Count >= MaxLines) { Recycle(active[0]); active.RemoveAt(0); }

        var l = pool.Count > 0 ? pool.Pop() : Create();
        l.msg = message;
        l.age = 0f;
        l.slotY = -SlideIn;    // start BELOW the bottom slot → clearly rises into place (new row appears at the bottom)
        l.text.text = message;
        l.text.color = color;
        l.go.SetActive(true);
        active.Add(l);
    }

    Line Create()
    {
        var go = new GameObject("OverheadNoticeLine", typeof(RectTransform));
        go.transform.SetParent(strip, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1000f, LineStep);
        rt.pivot = new Vector2(0.5f, 0f);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = UiKit.Font;
        t.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.Font);   // dark edge, reads on any backdrop
        t.fontSize = FontSize;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Bottom;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;

        return new Line { go = go, text = t };
    }

    void Recycle(Line l)
    {
        l.go.SetActive(false);
        pool.Push(l);
    }

    void LateUpdate()
    {
        if (active.Count == 0) return;

        // Newest line sits at the bottom of the strip; older ones pushed upward. Each line EASES toward its slot instead
        // of snapping, so a new entry visibly slides the older rows up (FIFO chat feel).
        float dt = Time.deltaTime;
        float ease = 1f - Mathf.Exp(-EaseRate * dt);   // framerate-independent smoothing toward the target slot
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var l = active[i];
            l.age += dt;
            if (l.age >= Life) { Recycle(l); active.RemoveAt(i); continue; }

            int fromNewest = active.Count - 1 - i;
            l.slotY = Mathf.Lerp(l.slotY, fromNewest * LineStep, ease);
            ((RectTransform)l.go.transform).anchoredPosition = new Vector2(0f, l.slotY);

            // Grow-in pop on the freshest frames so a new row reads as "arriving".
            float pop = Mathf.SmoothStep(0.4f, 1f, Mathf.Clamp01(l.age / FadeInFor));
            l.go.transform.localScale = new Vector3(pop, pop, 1f);

            // Fade IN at the start, OUT at the end.
            float fadeIn = Mathf.Clamp01(l.age / FadeInFor);
            float fadeOut = 1f - Mathf.Clamp01((l.age - (Life - FadeFor)) / FadeFor);
            var c = l.text.color;
            c.a = Mathf.Min(fadeIn, fadeOut);
            l.text.color = c;
        }
    }
}
