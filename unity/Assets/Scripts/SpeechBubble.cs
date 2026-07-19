using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pooled world-space WHITE speech bubble for ambient NPC chatter (<see cref="TownNPC"/> barks). A rounded white
/// panel with dark text inside, billboarded upright toward the camera and faded out over its lifetime.
///
/// Mirrors <see cref="DamagePopup"/>'s zero-allocation story (one shared world-space <see cref="Canvas"/>, a
/// <see cref="Stack{T}"/> pool, a cached camera transform) so a town full of barks costs nothing per frame —
/// see PERFORMANCE.md. The only one-time costs are the shared Canvas and a procedurally-generated rounded-rect
/// sprite (built once, cached). Call <see cref="Show"/>.
/// </summary>
[DisallowMultipleComponent]
public class SpeechBubble : MonoBehaviour
{
    // World units per UI unit. Smaller than DamagePopup's numbers — barks should be subtle, not shouty.
    const float BaseScale = 0.0018f;
    const int FontSize = 30;
    const float PadX = 14f, PadY = 7f;   // text inset inside the bubble (UI units)

    // ---- shared pool ----
    static Transform host;
    static readonly Stack<SpeechBubble> pool = new Stack<SpeechBubble>();
    static Transform camT;
    static Sprite bubbleSprite, tailSprite;

    Image bg, tail;
    TMP_Text text;
    RectTransform rt;
    float age, lifetime;
    Transform follow;        // the unit this bubble belongs to — the bubble rides its head each frame
    float followYOffset;     // world-up offset from the owner's origin to the bubble's anchor (its bottom)
    Vector3 lastFollowPos;   // fallback position if the owner is destroyed mid-bark

    /// <summary>Show a bark line in a white bubble that RIDES <paramref name="owner"/> at
    /// <paramref name="yOffset"/> above its origin (pass the head height). Reuses a pooled bubble; zero
    /// allocation after warm-up. The bubble follows the owner as it walks and fades out over its life.</summary>
    public static void Show(Transform owner, float yOffset, string label, float life)
    {
        if (string.IsNullOrEmpty(label) || owner == null) return;
        EnsureHost();
        var b = pool.Count > 0 ? pool.Pop() : Create();
        b.Begin(owner, yOffset, label, life);
    }

    // The shared canvas. Recreated lazily if a scene unload destroyed it (pool cleared with it).
    static void EnsureHost()
    {
        if (host != null) return;
        pool.Clear();
        var go = new GameObject("SpeechBubbleSystem") { hideFlags = HideFlags.DontSaveInEditor };
        go.layer = LayerMask.NameToLayer("TransparentFX");   // out of the planar-reflection camera
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DepthSortRuntime.FxPopup;       // FX band: above entities + overhead
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        host = go.transform;
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
        b.bg.sprite = BubbleSprite();
        b.bg.type = Image.Type.Sliced;          // 9-slice so corners stay round at any size
        b.bg.color = Color.white;

        // Downward tail (classic speech-bubble pointer): hangs just below the bubble's bottom edge, pointing at
        // the owner's head. Anchored to bottom-centre, pivot at its top so it dangles below.
        var tg = new GameObject("Tail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        tg.transform.SetParent(go.transform, false);
        b.tail = tg.GetComponent<Image>();
        b.tail.sprite = TailSprite();
        b.tail.color = Color.white;
        b.tail.raycastTarget = false;
        var tlrt = (RectTransform)tg.transform;
        tlrt.anchorMin = tlrt.anchorMax = new Vector2(0.5f, 0f);   // bottom-centre of the bubble
        tlrt.pivot = new Vector2(0.5f, 1f);                        // hang downward from there
        tlrt.sizeDelta = new Vector2(16f, 11f);
        tlrt.anchoredPosition = new Vector2(0f, 1f);               // overlap the seam by 1px so no gap shows

        // Dark text, parented to the bubble and inset by the padding.
        b.text = UiKit.MakeText("Text", go.transform, "", FontSize, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
        b.text.color = new Color(0.12f, 0.12f, 0.14f);
        b.text.raycastTarget = false;
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
        followYOffset = yOffset;
        lastFollowPos = owner.position + Vector3.up * yOffset;
        SetAlpha(1f);
        transform.position = lastFollowPos;
        transform.localScale = Vector3.one * BaseScale;
        gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        // Ride the owner's head (so the bubble tracks the unit as it walks). If the owner was destroyed
        // mid-bark, hold the last known spot and just finish fading.
        if (follow != null) lastFollowPos = follow.position + Vector3.up * followYOffset;
        transform.position = lastFollowPos;

        if (camT == null) { var c = CameraRig.Cam; if (c != null) camT = c.transform; }
        if (camT != null)
        {
            Vector3 dir = transform.position - camT.position; dir.y = 0f;   // upright billboard (RO-style)
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        age += Time.deltaTime;
        // Hold opaque most of its life, fade in the last third so it doesn't pop out.
        float t = lifetime > 0f ? age / lifetime : 1f;
        SetAlpha(Mathf.Clamp01((1f - t) / 0.34f));

        if (age >= lifetime) Recycle();
    }

    void SetAlpha(float a)
    {
        var c = bg.color; c.a = a; bg.color = c;
        var tc = text.color; tc.a = a; text.color = tc;
        var ta = tail.color; ta.a = a; tail.color = ta;
    }

    void Recycle()
    {
        follow = null;
        gameObject.SetActive(false);
        pool.Push(this);
    }

    // One-time procedural rounded-rect sprite (white, soft 1px edge), 9-sliced so it scales without distorting
    // the corners. Cached statically — built at most once per session.
    static Sprite BubbleSprite()
    {
        if (bubbleSprite != null) return bubbleSprite;
        const int S = 48, R = 14;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float cx = Mathf.Clamp(fx, R, S - R), cy = Mathf.Clamp(fy, R, S - R);
                float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                byte a = (byte)(Mathf.Clamp01(R - d + 0.5f) * 255f);   // inside=255, ~1px soft edge
                px[y * S + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px); tex.Apply();
        bubbleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
                                     SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        return bubbleSprite;
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
