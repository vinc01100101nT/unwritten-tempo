using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A floating combat number that rises from a unit's head and fades out. Call <see cref="Spawn"/> /
/// <see cref="SpawnText"/> — there's nothing to wire up in the scene. Used by <see cref="Health"/>,
/// <see cref="Character"/>, and the mob AIs whenever damage / a MISS / a heal happens.
///
/// PERFORMANCE: popups are POOLED, not created-and-destroyed. The old version built a fresh
/// GameObject + world-space <see cref="Canvas"/> + <see cref="Text"/> + <see cref="Outline"/> per hit
/// and Destroyed it — worst case exactly when the game is busiest (auto-combat vs a field of mobs,
/// both directions dealing damage). That meant per-hit GC churn, a per-spawn builtin-font lookup, and
/// a separate Canvas (its own batch + rebuild) for every number on screen.
///
/// Now there is ONE shared world-space canvas; numbers are reused from a free-list, so a steady fight
/// allocates nothing after warm-up. The shared canvas sits at the origin (identity, unit scale) so each
/// pooled number's own world transform IS its local transform — no parent scale/rotation surprises.
/// </summary>
[DisallowMultipleComponent]
public class DamagePopup : MonoBehaviour
{
    [Tooltip("Seconds before it disappears.")]
    public float lifetime = 0.7f;
    [Tooltip("World units it floats upward per second.")]
    public float riseSpeed = 1.4f;

    // On-screen size = fontSize * BaseScale. We render the glyphs into a HIGH-res atlas (big fontSize) and
    // downscale with a small BaseScale — downscaling a large atlas stays crisp, where a small atlas upscaled
    // goes blurry. Tune size here via BaseScale; keep fontSize comfortably >= the displayed pixel height.
    const float BaseScale = 0.0026f;   // 64px atlas * 0.0026 ≈ 0.166 world units (a bit smaller than before)

    // ---- shared pool ----------------------------------------------------------
    static Transform host;                                   // the one canvas everything parents under
    static readonly Stack<DamagePopup> pool = new Stack<DamagePopup>();
    static Font font;                                        // builtin font, fetched once
    static Transform camT;                                   // cached camera transform for billboarding

    /// <summary>Spawn a number at a world position (a head offset is added for you).</summary>
    public static void Spawn(Vector3 worldPos, int amount, Color color) => Spawn(worldPos, amount, color, 1f);

    /// <summary>As <see cref="Spawn(Vector3,int,Color)"/> but at <paramref name="scaleMult"/>× size — e.g. a
    /// big yellow multi-hit TOTAL.</summary>
    public static void Spawn(Vector3 worldPos, int amount, Color color, float scaleMult)
    {
        if (amount <= 0) return;
        SpawnText(worldPos, amount.ToString(), color, scaleMult);
    }

    /// <summary>Spawn arbitrary floating text (e.g. "+10" for heals, "MISS").</summary>
    public static void SpawnText(Vector3 worldPos, string label, Color color) => SpawnText(worldPos, label, color, 1f);

    /// <summary>As <see cref="SpawnText(Vector3,string,Color)"/> but at <paramref name="scaleMult"/>× size.</summary>
    public static void SpawnText(Vector3 worldPos, string label, Color color, float scaleMult)
    {
        if (string.IsNullOrEmpty(label)) return;
        EnsureHost();
        var popup = pool.Count > 0 ? pool.Pop() : Create();
        popup.Begin(worldPos, label, color, scaleMult);
    }

    /// <summary>Spawn a HELD number that stays put (no rise/fade) and can be UPDATED — for a live combo total
    /// that accumulates across a multi-hit. Update it with <see cref="SetHeld"/>, then <see cref="Release"/> to
    /// let it rise + fade. Returns the handle.</summary>
    public static DamagePopup SpawnHeld(Vector3 worldPos, int amount, Color color, float scaleMult)
    {
        EnsureHost();
        var popup = pool.Count > 0 ? pool.Pop() : Create();
        popup.Begin(worldPos, amount.ToString(), color, scaleMult);
        popup.held = true;
        popup.holdAge = 0f;
        return popup;
    }

    /// <summary>Update a held popup's value + position (resets its idle failsafe). No-op once released.</summary>
    public void SetHeld(int amount, Vector3 worldPos)
    {
        if (!held) return;
        text.text = amount.ToString();
        transform.position = worldPos + new Vector3(0f, 0.5f, 0f);
        holdAge = 0f;
    }

    /// <summary>Stop holding — the popup now rises + fades like a normal number.</summary>
    public void Release()
    {
        if (!held) return;
        held = false;
        age = 0f;   // fresh fade starting now
    }

    // The shared canvas. Recreated lazily if a scene unload destroyed it (the pool is cleared then, since
    // its pooled children died with it). Kept at origin/identity/unit-scale — see the class note.
    static void EnsureHost()
    {
        if (host != null) return;        // alive (Unity-null check)
        pool.Clear();                    // stale refs from a previous scene's destroyed host — drop them

        var go = new GameObject("DamagePopupSystem");
        go.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera (no mirrored numbers)
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DepthSortRuntime.FxPopup;   // FX band: above all entities + overhead
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        host = go.transform;
    }

    static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    // Build one reusable popup (Text + dark outline) parented under the shared canvas.
    static DamagePopup Create()
    {
        var go = new GameObject("DamagePopup",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline), typeof(DamagePopup));
        go.transform.SetParent(host, false);
        go.layer = host.gameObject.layer;   // inherit TransparentFX so popups stay out of water reflections
        ((RectTransform)go.transform).sizeDelta = new Vector2(100, 40);

        var t = go.GetComponent<Text>();
        t.font = Font;
        t.fontSize = 64;   // high-res glyph atlas; BaseScale downscales it to the small on-screen size (stays crisp)
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;

        var outline = go.GetComponent<Outline>();             // dark edge so it reads on any background
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var popup = go.GetComponent<DamagePopup>();
        popup.text = t;
        return popup;
    }

    Text text;
    Color startColor;
    float age;
    float scaleMult = 1f;
    bool held;          // a live combo total: stays put + updatable until Release()
    float holdAge;      // failsafe so a never-released held popup can't get stuck on screen
    const float MaxHold = 8f;

    // (Re)activate this pooled popup at a fresh position with new text/colour/size.
    void Begin(Vector3 worldPos, string label, Color color, float mult)
    {
        text.text = label;
        text.color = color;
        startColor = color;
        scaleMult = Mathf.Max(0.1f, mult);
        age = 0f;
        held = false;       // SpawnHeld re-sets this true after Begin; a normal reuse starts un-held
        holdAge = 0f;
        // lift to the head, plus a little horizontal scatter so rapid hits don't stack exactly
        transform.position = worldPos + new Vector3(Random.Range(-0.15f, 0.15f), 0.5f, 0f);
        transform.localScale = Vector3.one * (BaseScale * scaleMult);
        gameObject.SetActive(true);
    }

    void Update()
    {
        // Held (live combo total): don't rise/fade/recycle — just sit and pulse while it accumulates.
        if (held)
        {
            holdAge += Time.deltaTime;
            float beat = 1f + 0.12f * Mathf.Abs(Mathf.Sin(holdAge * 9f));
            transform.localScale = Vector3.one * (BaseScale * scaleMult * beat);
            text.color = startColor;
            if (holdAge >= MaxHold) Release();   // failsafe: updates stopped → let it fade instead of sticking
            return;
        }

        age += Time.deltaTime;
        transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

        var c = startColor;
        c.a = Mathf.Clamp01(1f - age / lifetime);     // fade out over its lifetime
        text.color = c;

        // a quick "pop": briefly larger at spawn, then settle to the base size
        float pop = 1f + 0.4f * Mathf.Clamp01(1f - age * 6f);
        transform.localScale = Vector3.one * (BaseScale * scaleMult * pop);

        if (age >= lifetime) Recycle();
    }

    // Face the camera every frame so orbiting the 2.5D camera never skews/edge-ons the number. Runs in
    // LateUpdate so it's applied AFTER CameraFollow3D has moved the camera this frame.
    void LateUpdate()
    {
        if (camT == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            camT = cam.transform;
        }
        transform.rotation = camT.rotation;
    }

    // Back to the free-list instead of Destroy — the next hit reuses us, allocation-free.
    void Recycle()
    {
        gameObject.SetActive(false);
        pool.Push(this);
    }
}
