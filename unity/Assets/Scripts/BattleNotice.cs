using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ONE place a "you can't do that" BATTLE WARNING is shown — a dedicated notification strip that hovers above
/// the player's head and STACKS the most recent messages (Silenced, No MP, Can't target mobs, Out of range, …).
/// Gameplay code calls <see cref="Warn"/>; it never spawns its own floating text for a rejected action. That keeps
/// every combat warning in one styled, one-anchored place instead of scattered <see cref="DamagePopup"/> calls at
/// different world positions — so re-styling or re-siting the warning feed is a one-file edit, and several warnings
/// in quick succession read as a legible stack rather than overlapping numbers.
///
/// <para>This is NOT floating combat text: <see cref="DamagePopup"/> stays the home for per-unit numbers/MISS that
/// belong ON the struck unit. A battle notice is always about the PLAYER's own action being blocked, so it lives on
/// the player.</para>
///
/// PERFORMANCE: self-booting singleton with a shared world-space canvas + a small pooled line set (mirrors
/// <see cref="DamagePopup"/>). Warnings fire on a rejected input (a keypress), never per-frame; the only steady work
/// is repositioning ≤ <see cref="MaxLines"/> billboarded lines in LateUpdate — no allocation, no per-frame Find.
/// </summary>
[DisallowMultipleComponent]
public class BattleNotice : MonoBehaviour
{
    /// <summary>Show a battle warning above the player (default warning colour). Duplicate of a still-showing
    /// message just refreshes its timer instead of stacking a copy (so holding a key doesn't spam the strip).</summary>
    public static void Warn(string message) => Warn(message, WarnColor);

    /// <summary>As <see cref="Warn(string)"/> with an explicit colour (e.g. No MP = blue, Silenced = purple).</summary>
    public static void Warn(string message, Color color)
    {
        if (string.IsNullOrEmpty(message)) return;
        Ensure();
        inst.Push(message, color);
    }

    // ---- tuning ---------------------------------------------------------------
    const int MaxLines = 5;          // how many warnings can stack at once (oldest recycled beyond this)
    const float Life = 2.4f;         // seconds a line stays before it fades out
    const float FadeFor = 0.4f;      // seconds of fade at the end of Life
    const float FadeInFor = 0.2f;    // seconds of fade-IN at the start (new row eases in)
    const float BaseY = 0.5f;        // world height above the player pivot where the strip's bottom (newest) line sits
    const float LineStep = 0.13f;    // world-unit vertical gap between stacked lines (chat rows)
    const float SlideIn = 0.16f;     // how far BELOW its slot a new row starts, so its rise is clearly visible
    const float EaseRate = 7f;       // lower = slower/softer glide toward the target slot (the visible push)
    const float BaseScale = 0.00093f;// fontSize(64) * this ≈ glyph world height — 3× smaller than the first pass
    static readonly Color WarnColor = new Color(1f, 0.55f, 0.3f);

    // ---- singleton + shared canvas -------------------------------------------
    static BattleNotice inst;
    static Font font;
    Transform camT, player;
    Transform host;                  // the shared world-space canvas

    static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("BattleNotice");
        inst = go.AddComponent<BattleNotice>();
        inst.Build();
    }

    void Build()
    {
        var canvasGo = new GameObject("BattleNoticeCanvas");
        canvasGo.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DepthSortRuntime.FxPopup;            // above entities/overhead, like DamagePopup
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        host = canvasGo.transform;
    }

    static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    // ---- line pool ------------------------------------------------------------
    class Line { public GameObject go; public Text text; public float age; public string msg; public float slotY; }
    readonly List<Line> active = new List<Line>(MaxLines);
    readonly Stack<Line> pool = new Stack<Line>();

    void Push(string message, Color color)
    {
        // Chat-style: every warning appends a fresh row at the bottom (repeats included). Oldest scrolls off once
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
        var go = new GameObject("BattleNoticeLine",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        go.transform.SetParent(host, false);
        go.layer = host.gameObject.layer;
        ((RectTransform)go.transform).sizeDelta = new Vector2(220, 40);

        var t = go.GetComponent<Text>();
        t.font = Font;
        t.fontSize = 64;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;

        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

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

        if (camT == null) { var c = Camera.main; if (c != null) camT = c.transform; }
        if (player == null) { var p = FindFirstObjectByType<Player3DController>(); if (p != null) player = p.transform; }
        if (player == null) return;   // no anchor this frame — leave lines as-is (don't churn)

        Vector3 baseP = player.position + Vector3.up * BaseY;

        // Newest line sits at the bottom of the strip (nearest the head); older ones pushed upward. Each line EASES
        // toward its slot instead of snapping, so a new entry visibly slides the older rows up (FIFO chat feel).
        float dt = Time.deltaTime;
        float ease = 1f - Mathf.Exp(-EaseRate * dt);   // framerate-independent smoothing toward the target slot
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var l = active[i];
            l.age += dt;
            if (l.age >= Life) { Recycle(l); active.RemoveAt(i); continue; }

            // Glide toward this row's slot (bottom = newest). A new push bumps every older row's slot up by one, so
            // they all visibly slide together — the FIFO chat push.
            int fromNewest = active.Count - 1 - i;
            l.slotY = Mathf.Lerp(l.slotY, fromNewest * LineStep, ease);
            l.go.transform.position = baseP + Vector3.up * l.slotY;

            // Grow-in pop on the freshest frames so a new row reads as "arriving".
            float pop = Mathf.SmoothStep(0.4f, 1f, Mathf.Clamp01(l.age / FadeInFor));
            l.go.transform.localScale = Vector3.one * (BaseScale * pop);
            if (camT != null) l.go.transform.rotation = camT.rotation;

            // Fade IN at the start, OUT at the end.
            float fadeIn = Mathf.Clamp01(l.age / FadeInFor);
            float fadeOut = 1f - Mathf.Clamp01((l.age - (Life - FadeFor)) / FadeFor);
            var c = l.text.color;
            c.a = Mathf.Min(fadeIn, fadeOut);
            l.text.color = c;
        }
    }
}
