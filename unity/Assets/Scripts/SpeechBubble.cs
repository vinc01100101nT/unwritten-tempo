using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pooled WHITE speech bubble for ambient NPC chatter (<see cref="TownNPC"/> barks) and player chat. A rounded white
/// panel with dark text inside, riding the speaker's head and faded out over its lifetime. Drawn flat on the
/// screen through <see cref="OverheadScreenLayer"/>, so it stays the same size at every zoom.
///
/// Mirrors <see cref="DamagePopup"/>'s zero-allocation story (one shared <see cref="Canvas"/>, a
/// <see cref="Stack{T}"/> pool, a cached camera transform) so a town full of barks costs nothing per frame —
/// see PERFORMANCE.md. The only one-time costs are the shared Canvas and a procedurally-generated rounded-rect
/// sprite (built once, cached). Call <see cref="Show"/>.
/// </summary>
[DisallowMultipleComponent]
public class SpeechBubble : MonoBehaviour
{
    // World units per UI unit the bubble was drawn at in the world. It now only sets the bubble's fixed on-screen size
    // (see OverheadScreenLayer.ScaleFor) — the whole bubble, text included, grows and shrinks with it.
    const float BaseScale = 0.0027f;
    const int FontSize = 45;
    const float PadX = 14f, PadY = 7f;   // text inset inside the bubble (UI units)

    // ---- shared pool ----
    static Transform host;
    static Canvas hostCanvas;
    static readonly Stack<SpeechBubble> pool = new Stack<SpeechBubble>();
    // One bubble per character: the live bubble each owner currently has, so a new line REPLACES it instead of
    // stacking a second bubble over the same head. Keyed by the follow transform (a character's feet-seated visual).
    static readonly Dictionary<Transform, SpeechBubble> active = new Dictionary<Transform, SpeechBubble>();
    static Sprite tailSprite;
    const int BubbleRadius = 14;

    Image bg, tail;
    TMP_Text text;
    RectTransform rt;
    float age, lifetime;
    Transform follow;        // the unit this bubble belongs to — the bubble rides its head each frame
    Transform regKey;        // the `active` registry key (== owner) so Recycle can drop this owner's entry
    float followYOffset;     // world-up offset from the owner's origin to the bubble's anchor (its bottom)
    Vector3 lastFollowPos;   // fallback position if the owner is destroyed mid-bark

    // Gap between the model's crown and the bubble's bottom edge — shared by every unit so NPCs and the player anchor
    // identically. VisualWorldHeight is feet-to-crown, so crown + this small clearance clears the head.
    public const float HeadClearance = 0.05f;

    /// <summary>THE one anchor path for an overhead bubble on any live unit (NPC bark or player chat). Rides the
    /// <paramref name="pose"/>'s OWN transform — which is feet-seated for both the townsfolk root and the player's
    /// separate feet-riding visual — and lifts by the model's crown height, so both sit at exactly the same head
    /// clearance. Use this instead of hand-computing a per-caller offset.</summary>
    public static void ShowOverHead(IStancePose pose, string label, float life)
    {
        if (pose is Component c) Show(c.transform, pose.VisualWorldHeight + HeadClearance, label, life);
    }

    /// <summary>Show a bark line in a white bubble that RIDES <paramref name="owner"/> at
    /// <paramref name="yOffset"/> above its origin (pass the head height). Reuses a pooled bubble; zero
    /// allocation after warm-up. The bubble follows the owner as it walks and fades out over its life.</summary>
    public static void Show(Transform owner, float yOffset, string label, float life)
    {
        if (string.IsNullOrEmpty(label) || owner == null) return;
        EnsureHost();
        // Reuse this owner's existing bubble if it still has one live — one bubble per character, no overlap.
        if (active.TryGetValue(owner, out var existing) && existing != null)
        {
            existing.Begin(owner, yOffset, label, life);
            return;
        }
        var b = pool.Count > 0 ? pool.Pop() : Create();
        active[owner] = b;
        b.Begin(owner, yOffset, label, life);
    }

    // The shared canvas. Recreated lazily if a scene unload destroyed it (pool cleared with it).
    static void EnsureHost()
    {
        if (host != null) return;
        pool.Clear();
        active.Clear();   // a scene unload destroyed the old bubbles with the host; drop their stale registry entries
        // No raycaster: a bubble is never something the cursor can hit.
        hostCanvas = OverheadScreenLayer.CreateCanvas("SpeechBubbleSystem", clickable: false);
        host = hostCanvas.transform;
    }


    static SpeechBubble Create()
    {
        var go = new GameObject("SpeechBubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(SpeechBubble));
        go.transform.SetParent(host, false);
        go.layer = host.gameObject.layer;
        var b = go.GetComponent<SpeechBubble>();
        b.rt = (RectTransform)go.transform;

        // Anchor the bubble by its BOTTOM-CENTRE so it grows upward from the head and the tail hangs at the head.
        b.rt.pivot = new Vector2(0.5f, 0f);

        b.bg = go.GetComponent<Image>();
        UiKit.Round(b.bg, BubbleRadius);
        b.bg.color = UiKit.PanelFill;

        // Downward tail (classic speech-bubble pointer): hangs just below the bubble's bottom edge, pointing at
        // the owner's head. Anchored to bottom-centre, pivot at its top so it dangles below.
        var tg = new GameObject("Tail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        tg.transform.SetParent(go.transform, false);
        b.tail = tg.GetComponent<Image>();
        b.tail.sprite = TailSprite();
        b.tail.color = UiKit.PanelFill;
        b.tail.raycastTarget = false;
        var tlrt = (RectTransform)tg.transform;
        tlrt.anchorMin = tlrt.anchorMax = new Vector2(0.5f, 0f);   // bottom-centre of the bubble
        tlrt.pivot = new Vector2(0.5f, 1f);                        // hang downward from there
        tlrt.sizeDelta = new Vector2(16f, 11f);
        tlrt.anchoredPosition = new Vector2(0f, 1f);               // overlap the seam by 1px so no gap shows

        // Dark text, parented to the bubble and inset by the padding.
        b.text = UiKit.MakeText("Text", go.transform, "", FontSize, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
        b.text.color = UiKit.TextMain;
        b.text.raycastTarget = false;
        b.text.richText = false;   // player chat is arbitrary text — a stray '<' must stay literal, never markup
        var trt = b.text.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(PadX, PadY); trt.offsetMax = new Vector2(-PadX, -PadY);
        return b;
    }

    void Begin(Transform owner, float yOffset, string label, float life)
    {
        text.text = label;
        // Size the bubble to fit the text + padding.
        rt.sizeDelta = new Vector2(text.preferredWidth + PadX * 2f, text.preferredHeight + PadY * 2f);
        lifetime = Mathf.Max(0.2f, life);
        age = 0f;
        follow = owner;
        regKey = owner;
        followYOffset = yOffset;
        lastFollowPos = owner.position + Vector3.up * yOffset;
        SetAlpha(1f);
        Place();
        gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        // Ride the owner's head (so the bubble tracks the unit as it walks). If the owner was destroyed
        // mid-bark, hold the last known spot and just finish fading.
        if (follow != null) lastFollowPos = follow.position + Vector3.up * followYOffset;
        Place();

        age += Time.deltaTime;
        // Hold opaque most of its life, fade in the last third so it doesn't pop out.
        float t = lifetime > 0f ? age / lifetime : 1f;
        SetAlpha(Mathf.Clamp01((1f - t) / 0.34f));

        if (age >= lifetime) Recycle();
    }

    // Onto the screen over the head, at the fixed on-screen size. Hidden (scale 0) until a camera exists.
    void Place()
    {
        var cam = CameraRig.Cam;
        if (cam == null) { transform.localScale = Vector3.zero; return; }
        OverheadScreenLayer.Place(rt, lastFollowPos, cam, OverheadScreenLayer.ScaleFor(hostCanvas, BaseScale));
    }

    void SetAlpha(float a)
    {
        var c = bg.color; c.a = a; bg.color = c;
        var tc = text.color; tc.a = a; text.color = tc;
        var ta = tail.color; ta.a = a; tail.color = ta;
    }

    void Recycle()
    {
        if (regKey != null) active.Remove(regKey);
        regKey = null;
        follow = null;
        gameObject.SetActive(false);
        pool.Push(this);
    }

    // One-time white DOWNWARD triangle for the bubble's tail (wide at the top, apex at the bottom), soft 1px
    // edge. Cached statically. Not 9-sliced — it's drawn at a fixed small size.
    static Sprite TailSprite()
    {
        if (tailSprite != null) return tailSprite;
        const int W = 24, H = 16;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // y=0 bottom (apex), y=H-1 top (full width). Half-width grows linearly with height.
                float halfW = (y + 0.5f) / H * (W * 0.5f);
                float dist = Mathf.Abs(x + 0.5f - W * 0.5f);
                byte a = (byte)(Mathf.Clamp01(halfW - dist + 0.5f) * 255f);
                px[y * W + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px); tex.Apply();
        tailSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 1f), 100f);
        return tailSprite;
    }
}
