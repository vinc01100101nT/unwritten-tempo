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
/// and Destroyed it — worst case exactly when the game is busiest (auto-pilot AI vs a field of mobs,
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
    // A combat number RISES to a fixed height, HOLDS there so it can be read, then FADES in place.
    const float RiseSeconds = 0.3f, RiseHeight = 0.6f, HoldSeconds = 0.5f, FadeSeconds = 0.4f;
    const float DefaultLifetime = RiseSeconds + HoldSeconds + FadeSeconds, DefaultRise = RiseHeight / RiseSeconds;
    // A released multi-hit combo total lingers this long and fades IN PLACE (no rise) — see Release().
    const float HeldFadeSeconds = 2f;

    // A live TOTAL sits ABOVE every per-hit number it is counting, always — it is the headline, and a number that
    // ducks under the very hits feeding it reads as a mistake. Per-hit numbers climb for their whole life, so the top
    // of that climb IS the height, derived from the two constants that decide it rather than typed as a guess: a
    // hand-picked lift would silently go wrong the moment either of them is retuned.
    // (This is a plain world distance ON PURPOSE, unlike the anchor a number is spawned at — which must scale with the
    // body, and once didn't. What this clears is the CLIMB of the other numbers, and that climb is a world distance
    // too, so matching it is the only way to stay above them over a mob of any size.)
    const float HeldLift = RiseHeight;

    // How a pop decays: full size boost at the moment it fires, gone this many times faster than a second.
    const float PopDecay = 6f;
    const float SpawnPop = 0.4f;   // a fresh number arriving
    const float HeldPop = 0.24f;   // a live total taking another hit

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
        if (NobodyToShowItTo) return;
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

    /// <summary>Every floating number or word drawn over a unit — a damage number, a heal, a MISS — with the unit it
    /// was drawn over. For a player reading the game as text (AgentLink); nobody listens otherwise. Raised only by the
    /// overloads that take the unit: a number spawned at a bare position belongs to nobody it could name.</summary>
    public static event System.Action<Transform, string> Shown;

    /// <summary>Spawn a number over <paramref name="unit"/>'s head, and say whose head it was.</summary>
    public static void Spawn(Transform unit, int amount, Color color) => Spawn(unit, amount, color, 1f);

    /// <summary>As <see cref="Spawn(Transform,int,Color)"/> at <paramref name="scaleMult"/>× size.</summary>
    public static void Spawn(Transform unit, int amount, Color color, float scaleMult)
    {
        if (NobodyToShowItTo || amount <= 0) return;
        SpawnText(unit, Format(amount), color, scaleMult);
    }

    /// <summary>Spawn floating text over <paramref name="unit"/>'s head, and say whose head it was.</summary>
    public static void SpawnText(Transform unit, string label, Color color) => SpawnText(unit, label, color, 1f);

    /// <summary>As <see cref="SpawnText(Transform,string,Color)"/> at <paramref name="scaleMult"/>× size.</summary>
    public static void SpawnText(Transform unit, string label, Color color, float scaleMult)
    {
        if (unit == null || NobodyToShowItTo || string.IsNullOrEmpty(label)) return;
        SpawnText(UnitAnchor.Overhead(unit), label, color, scaleMult);
        Shown?.Invoke(unit, label);   // after the draw, so a listener's fault can never cost the screen its number
    }

    /// <summary>Spawn arbitrary floating text (e.g. "+10" for heals, "MISS").</summary>
    public static void SpawnText(Vector3 worldPos, string label, Color color) => SpawnText(worldPos, label, color, 1f);

    /// <summary>A speech "bark": floating text that lingers longer and barely rises — for ambient NPC chatter
    /// (<see cref="TownNPC"/>). Reuses the SAME pooled popup as combat numbers (zero-alloc); the per-instance
    /// timing is reset back to damage-number defaults on the next reuse, so a stretched bark can't leak its
    /// long lifetime into a later hit number.</summary>
    public static void SpawnBubble(Vector3 worldPos, string label, Color color, float life, float rise)
    {
        if (NobodyToShowItTo || string.IsNullOrEmpty(label)) return;
        EnsureHost();
        var popup = pool.Count > 0 ? pool.Pop() : Create();
        popup.Begin(worldPos, label, color, 1f);
        popup.lifetime = Mathf.Max(0.1f, life);   // override AFTER Begin (which reset it to the default)
        popup.riseSpeed = rise;
        popup.staged = false;                     // a bark keeps its own slow drift and long fade
    }

    /// <summary>As <see cref="SpawnText(Vector3,string,Color)"/> but at <paramref name="scaleMult"/>× size.</summary>
    public static void SpawnText(Vector3 worldPos, string label, Color color, float scaleMult)
    {
        if (NobodyToShowItTo || string.IsNullOrEmpty(label)) return;
        EnsureHost();
        var popup = pool.Count > 0 ? pool.Pop() : Create();
        popup.Begin(worldPos, label, color, scaleMult);
    }

    /// <summary>Spawn a HELD number that stays put (no rise/fade) and can be UPDATED — for a live combo total
    /// that accumulates across a multi-hit. Update it with <see cref="SetHeld"/>, then <see cref="Release"/> to
    /// let it rise + fade. Returns the handle.</summary>
    public static DamagePopup SpawnHeld(Vector3 worldPos, int amount, Color color, float scaleMult)
    {
        if (NobodyToShowItTo) return null;
        EnsureHost();
        var popup = pool.Count > 0 ? pool.Pop() : Create();
        popup.Begin(worldPos, amount.ToString(), color, scaleMult);
        popup.held = true;
        popup.holdAge = 0f;
        popup.transform.position += Vector3.up * HeldLift;   // clear of every per-hit number, from the first frame
        return popup;
    }

    /// <summary>Update a held popup's value + position, popping once for the change and resetting its idle failsafe.
    /// No-op once released.</summary>
    public void SetHeld(int amount, Vector3 worldPos)
    {
        if (!held) return;
        text.text = amount.ToString();
        // The SAME lift the spawn used — they used to differ, so a total jumped upward the first time it was updated.
        transform.position = worldPos + Vector3.up * HeldLift;
        holdAge = 0f;
        popAge = 0f;
    }

    /// <summary>Stop holding — the combo total now stays PUT and just fades out over <see cref="HeldFadeSeconds"/>
    /// (no upward drift), unlike a normal per-hit number which rises as it fades. Begin() resets riseSpeed/lifetime
    /// on the next pooled reuse, so this only affects THIS released total.</summary>
    public void Release()
    {
        if (!held) return;
        held = false;
        staged = false;               // a total fades where it stands, never rises
        age = 0f;                     // fresh fade starting now
        riseSpeed = 0f;               // hold position — fade in place, don't drift up
        lifetime = HeldFadeSeconds;   // linger ~2s before it's gone
        // A pop belongs to something HAPPENING — a number arriving, or a total taking another hit. Ending is neither,
        // so the fade starts past the pop window: the total simply fades from wherever the last hit left it. (This used
        // to reset the shared age, which the spawn pop reads, so every total flinched once just before disappearing.)
        popAge = 1f / PopDecay;
    }

    // The shared canvas. Recreated lazily if a scene unload destroyed it (the pool is cleared then, since
    // its pooled children died with it). Kept at origin/identity/unit-scale — see the class note.
    /// <summary>TRUE on the server, which draws nothing and is never meant to reach this door: every number is drawn by
    /// the screens from the blow the server announced, and reaching here writes an error to the server's log
    /// (<see cref="GameServer.CaughtDrawing"/>). Asked at the door rather than at the call sites, because the call
    /// sites are every place in the game that has ever shown a number and always will be.</summary>
    static bool NobodyToShowItTo => GameServer.CaughtDrawing("DamagePopup");

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
    bool staged;        // a combat number: rise, hold, fade (RiseSeconds / HoldSeconds / FadeSeconds)
    Vector3 basePos;    // where a staged number started, so its height is set rather than accumulated
    float holdAge;      // failsafe so a never-released held popup can't get stuck on screen
    float popAge;       // since the last POP — arriving, or a total taking another hit. Never reset by ending.
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
        popAge = 0f;        // arriving IS a pop
        held = false;       // SpawnHeld re-sets this true after Begin; a normal reuse starts un-held
        holdAge = 0f;
        // Horizontal scatter only, so rapid hits don't stack exactly. NO vertical lift: this used to add a fixed +0.5,
        // which is a world distance and so ignored unit size — it read as 3.7x body height over the smallest mob and
        // 1.1x over the largest. HEIGHT IS THE CALLER'S to choose, through UnitAnchor.Overhead (the single owner).
        transform.position = worldPos + new Vector3(Random.Range(-0.15f, 0.15f), 0f, 0f);
        basePos = transform.position;
        staged = true;
        transform.localScale = Vector3.one * (BaseScale * scaleMult);
        gameObject.SetActive(true);
    }

    void Update()
    {
        // Held (live combo total): don't rise/fade/recycle — sit still, and pop ONCE each time the number actually
        // changes. The pop decays from the moment of the change (holdAge is reset by every update), so a total that is
        // climbing bumps per hit while one that is simply waiting stays perfectly still. It used to ride an endless
        // sine instead, which was invisible on a fast flurry — the hits outran it — but read as a number shrinking and
        // growing by itself whenever the gap between hits was long enough to see, as a repeating run's is.
        if (held)
        {
            holdAge += Time.deltaTime;
            popAge += Time.deltaTime;
            transform.localScale = Vector3.one * (BaseScale * scaleMult * PopScale(HeldPop));
            text.color = startColor;
            if (holdAge >= MaxHold) Release();   // failsafe: updates stopped → let it fade instead of sticking
            return;
        }

        age += Time.deltaTime;
        popAge += Time.deltaTime;
        var c = startColor;
        // Faded SCALED by the colour's own alpha so a caller can pass a translucent colour (an element-resisted hit at
        // 70%, an immune hit at 30%) and have that cap honoured.
        if (staged)
        {
            transform.position = basePos + Vector3.up * StagedHeight(age);
            c.a = startColor.a * StagedAlpha(age);
        }
        else
        {
            transform.position += Vector3.up * (riseSpeed * Time.deltaTime);
            c.a = startColor.a * Mathf.Clamp01(1f - age / lifetime);
        }
        text.color = c;

        // a quick "pop": briefly larger at spawn, then settle to the base size
        transform.localScale = Vector3.one * (BaseScale * scaleMult * PopScale(SpawnPop));

        if (age >= lifetime) Recycle();
    }

    /// <summary>How high a combat number has risen <paramref name="age"/> seconds in: easing out to
    /// <see cref="RiseHeight"/> over <see cref="RiseSeconds"/>, then staying there.</summary>
    public static float StagedHeight(float age)
    {
        float t = Mathf.Clamp01(age / RiseSeconds);
        return RiseHeight * (1f - (1f - t) * (1f - t));
    }

    /// <summary>A combat number's opacity <paramref name="age"/> seconds in: full while it rises and holds, then fading
    /// to nothing over <see cref="FadeSeconds"/>.</summary>
    public static float StagedAlpha(float age) => Mathf.Clamp01(1f - (age - RiseSeconds - HoldSeconds) / FadeSeconds);

    /// <summary>The size boost from the most recent pop, decaying to nothing. ONE timer and ONE curve for both kinds of
    /// pop — a number arriving and a live total taking another hit — so they can only ever differ in strength, and a
    /// moment that is not a pop (a total ending) simply never resets the timer.</summary>
    float PopScale(float strength) => Pop(strength, popAge);

    /// <summary>THE POP every number on screen makes when it changes — a damage number arriving, a live total taking
    /// another hit, the kill combo counting another kill (<see cref="KillComboHud"/>): the size it is drawn at,
    /// <paramref name="secondsSince"/> after the change, for a pop of <paramref name="strength"/> (0.4 = 40% larger at
    /// the instant it fires). One curve, so every pop in the game feels the same.</summary>
    public static float Pop(float strength, float secondsSince) => 1f + strength * Mathf.Clamp01(1f - secondsSince * PopDecay);

    /// <summary>How long a pop lasts — past this, <see cref="Pop"/> is 1 and nothing needs redrawing.</summary>
    public const float PopSeconds = 1f / PopDecay;

    /// <summary>The strength of a fresh number's pop, for anything else that wants to arrive the same way.</summary>
    public const float ArrivalPop = SpawnPop;

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
