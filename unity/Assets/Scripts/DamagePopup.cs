using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    public float lifetime = DefaultLifetime;
    [Tooltip("World units it floats upward per second.")]
    public float riseSpeed = DefaultRise;

    // Damage-number timing. Begin() resets to these every reuse so a SpawnBubble bark (which stretches
    // lifetime/riseSpeed) can't leak its long-lived, slow-rising timing into the next combat number.
    const float DefaultLifetime = 0.7f, DefaultRise = 1.4f;
    // A released multi-hit combo total lingers this long and fades IN PLACE (no rise) — see Release().
    const float HeldFadeSeconds = 2f;

    // On-screen size = fontSize * BaseScale. We render the glyphs into a HIGH-res atlas (big fontSize) and
    // downscale with a small BaseScale — downscaling a large atlas stays crisp, where a small atlas upscaled
    // goes blurry. Tune size here via BaseScale; keep fontSize comfortably >= the displayed pixel height.
    const float BaseScale = 0.0026f;   // 64px atlas * 0.0026 ≈ 0.166 world units (a bit smaller than before)

    // ---- shared pool ----------------------------------------------------------
    static Transform host;                                   // the one canvas everything parents under
    static readonly Stack<DamagePopup> pool = new Stack<DamagePopup>();
    static Transform camT;                                   // cached camera transform for billboarding

    /// <summary>Spawn a number AT <paramref name="worldPos"/> — pass <see cref="UnitAnchor.Overhead"/> for text over a
    /// unit. No offset is added for you: height belongs to the anchor, which knows how big the unit is.</summary>
    public static void Spawn(Vector3 worldPos, int amount, Color color) => Spawn(worldPos, amount, color, 1f);

    /// <summary>As <see cref="Spawn(Vector3,int,Color)"/> but at <paramref name="scaleMult"/>× size — e.g. a
    /// big yellow multi-hit TOTAL.</summary>
    public static void Spawn(Vector3 worldPos, int amount, Color color, float scaleMult)
    {
        if (amount <= 0) return;
        SpawnText(worldPos, Format(amount), color, scaleMult);
    }

    // RuneScape-style number: full digits up to 10k, then abbreviated with a GREEN suffix (10.3K / 4.2M / 1.1B).
    // Rich-text tints only the suffix; the number inherits Text.color (the caller's damage/element/heal colour).
    const string SuffixColor = "#86E01E";   // OSRS-ish lime for the K/M/B tail
    static string Format(int amount)
    {
        if (amount < 10000) return amount.ToString();
        string s; char suf;
        if (amount < 1_000_000)          { s = (amount / 1000f).ToString("0.#");         suf = 'K'; }
        else if (amount < 1_000_000_000) { s = (amount / 1_000_000f).ToString("0.#");    suf = 'M'; }
        else                             { s = (amount / 1_000_000_000f).ToString("0.#"); suf = 'B'; }
        return s + "<color=" + SuffixColor + ">" + suf + "</color>";
    }

    /// <summary>Spawn arbitrary floating text (e.g. "+10" for heals, "MISS").</summary>
    public static void SpawnText(Vector3 worldPos, string label, Color color) => SpawnText(worldPos, label, color, 1f);

    /// <summary>A speech "bark": floating text that lingers longer and barely rises — for ambient NPC chatter
    /// (<see cref="TownNPC"/>). Reuses the SAME pooled popup as combat numbers (zero-alloc); the per-instance
    /// timing is reset back to damage-number defaults on the next reuse, so a stretched bark can't leak its
    /// long lifetime into a later hit number.</summary>
    public static void SpawnBubble(Vector3 worldPos, string label, Color color, float life, float rise)
    {
        if (string.IsNullOrEmpty(label)) return;
        EnsureHost();
        var popup = pool.Count > 0 ? pool.Pop() : Create();
        popup.Begin(worldPos, label, color, 1f);
        popup.lifetime = Mathf.Max(0.1f, life);   // override AFTER Begin (which reset it to the default)
        popup.riseSpeed = rise;
    }

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

    /// <summary>Stop holding — the combo total now stays PUT and just fades out over <see cref="HeldFadeSeconds"/>
    /// (no upward drift), unlike a normal per-hit number which rises as it fades. Begin() resets riseSpeed/lifetime
    /// on the next pooled reuse, so this only affects THIS released total.</summary>
    public void Release()
    {
        if (!held) return;
        held = false;
        age = 0f;                     // fresh fade starting now
        riseSpeed = 0f;               // hold position — fade in place, don't drift up
        lifetime = HeldFadeSeconds;   // linger ~2s before it's gone
    }

    // The shared canvas. Recreated lazily if a scene unload destroyed it (the pool is cleared then, since
    // its pooled children died with it). Kept at origin/identity/unit-scale — see the class note.
    static void EnsureHost()
    {
        if (host != null) return;        // alive (Unity-null check)
        pool.Clear();                    // stale refs from a previous scene's destroyed host — drop them

        var go = new GameObject("DamagePopupSystem") { hideFlags = HideFlags.DontSaveInEditor };   // runtime-only pool host — never serialize into a scene
        go.layer = LayerMask.NameToLayer("TransparentFX");   // excluded from the planar-reflection camera (no mirrored numbers)
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DepthSortRuntime.FxPopup;   // FX band: above all entities + overhead
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        host = go.transform;
    }

    // Build one reusable popup (TMP text + shared dark-outline material) parented under the shared canvas.
    static DamagePopup Create()
    {
        var go = new GameObject("DamagePopup",
            typeof(RectTransform), typeof(DamagePopup)) { hideFlags = HideFlags.DontSaveInEditor };   // pooled clone — never serialize into a scene (matches the host tag above)
        go.transform.SetParent(host, false);
        go.layer = host.gameObject.layer;   // inherit TransparentFX so popups stay out of water reflections
        ((RectTransform)go.transform).sizeDelta = new Vector2(100, 40);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = UiKit.PixelFont;
        // Thick black keyline for the chunky RuneScape/OSRS hitsplat look. ONE shared outlined material
        // across every pooled number, so thousands of them still batch into a single draw call.
        t.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.PixelFont, 0.3f);
        t.fontSize = 64;   // BaseScale downscales it on screen; SDF keeps it crisp at any size
        t.fontStyle = FontStyles.Normal;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.richText = true;                                    // lets the K/M/B suffix render in its own colour

        var popup = go.GetComponent<DamagePopup>();
        popup.text = t;
        return popup;
    }

    TMP_Text text;
    Color startColor;
    float age;
    float scaleMult = 1f;
    bool held;          // a live combo total: stays put + updatable until Release()
    float holdAge;      // failsafe so a never-released held popup can't get stuck on screen
    const float MaxHold = 8f;

    // (Re)activate this pooled popup at a fresh position with new text/colour/size.
    void Begin(Vector3 worldPos, string label, Color color, float mult)
    {
        lifetime = DefaultLifetime;   // reset timing (a previous SpawnBubble may have stretched these)
        riseSpeed = DefaultRise;
        text.text = label;
        text.color = color;
        startColor = color;
        scaleMult = Mathf.Max(0.1f, mult);
        age = 0f;
        held = false;       // SpawnHeld re-sets this true after Begin; a normal reuse starts un-held
        holdAge = 0f;
        // Horizontal scatter only, so rapid hits don't stack exactly. NO vertical lift: this used to add a fixed +0.5,
        // which is a world distance and so ignored unit size — it read as 3.7x body height over the smallest mob and
        // 1.1x over the largest. HEIGHT IS THE CALLER'S to choose, through UnitAnchor.Overhead (the single owner).
        transform.position = worldPos + new Vector3(Random.Range(-0.15f, 0.15f), 0f, 0f);
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
        // Fade out over its lifetime, SCALED by the colour's own alpha so a caller can pass a translucent
        // colour (e.g. an element-resisted hit at 70% / an immune hit at 30%) and have that cap honoured.
        c.a = startColor.a * Mathf.Clamp01(1f - age / lifetime);
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
