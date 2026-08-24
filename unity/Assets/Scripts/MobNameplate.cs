using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A small floating HP bar + name that sits at the BOTTOM of a billboard mob's sprite (RO/MMO style):
/// the bar hangs just under the feet and the mob's name reads below it. Billboards to the camera, drains
/// the bar from green→red as the mob is hurt, and hides while the mob is dead (restored on respawn).
///
/// PERFORMANCE (see PERFORMANCE.md): there is exactly ONE shared world-space <see cref="Canvas"/> for
/// EVERY mob nameplate — never a Canvas per mob (canvases are batch/rebuild boundaries; one-per-mob is
/// an explicit anti-pattern with dozens of mobs on screen). Each mob owns a lightweight widget (two
/// <see cref="Image"/>s + one <see cref="Text"/>) parented under that shared canvas; per frame it only
/// FOLLOWS the mob and faces the camera (cheap transform writes, no allocation — same as
/// <see cref="DamagePopup"/>), and it only rewrites the fill/colour when HP actually changes (the
/// <see cref="BossBanner"/> "update-on-change" discipline). The name is set
/// once. A shared ZTest-Always material keeps the bottom-anchored plate from being swallowed by the
/// terrain it sits on.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class MobNameplate : MonoBehaviour
{
    [Tooltip("Name shown under the HP bar. Set by the mob builder; falls back to the GameObject name. Still used " +
             "by the hover panel even when the on-plate label is hidden (see showName).")]
    public string displayName;

    [Tooltip("Draw the name label under the bar. OFF for normal mobs (cleaner); the boss turns it ON (BossBanner).")]
    public bool showName = false;
    [Tooltip("Hide the whole plate while the mob is at FULL health; show it only once it's been damaged (and " +
             "hide again if fully healed). The boss turns this OFF so its bar is always visible.")]
    public bool hideAtFullHealth = true;

    [Header("Layout (safe to tweak)")]
    [Tooltip("World size of the whole plate (shrinks the UI pixels into the 3D world).")]
    public float worldScale = 0.0075f;
    [Tooltip("Bar width / height in UI pixels (before worldScale).")]
    public float barWidth = 48f, barHeight = 9f;
    [Tooltip("Extra drop below the sprite's feet, in world units.")]
    public float gap = 0.02f;
    [Tooltip("Name font size in UI pixels (before worldScale).")]
    public int nameFontSize = 16;

    Health health;
    Transform camT;
    RectTransform root;
    Image fill;
    float feetOffset;            // world units below the mob's feet-pivot to hang the plate (a small gap)
    int lastShownHP = int.MinValue;
    bool shown = false;   // nothing is shown until the plate is first built (on first damage)

    // Debuff-icon row (above the bar), read from the mob's unified UnitStates store. Pooled Images reused across
    // rebuilds; rebuilt ONLY when the state SET changes (OnChanged) or a state silently expires (prune) — never per
    // frame (many-mobs perf rule). Icons only show while the plate is visible (i.e. once the mob's been damaged).
    UnitStates states;
    bool iconsDirty;
    readonly List<Image> iconSlots = new List<Image>(4);
    readonly List<TextMeshProUGUI> iconCounts = new List<TextMeshProUGUI>(4);   // "3" badge under each stacked icon
    const float IconSize = 11f, IconGap = 1.5f, IconRowGap = 2f;   // UI px (before worldScale)
    static readonly Color StackColor = new Color(1f, 0.87f, 0.45f);

    // ---- shared across ALL mobs: one canvas, one white sprite, the builtin font, one on-top material ----
    static Transform host;
    static Sprite whiteSprite;
    static Material onTopMat;     // UI/Default with ZTest Always, so a feet-level plate isn't hidden by terrain

    void Awake()
    {
        // Used by regular mobs AND the field boss (the boss just gets a bigger one — see BossBanner).
        health = GetComponent<Health>();
        if (string.IsNullOrEmpty(displayName))
            displayName = gameObject.name.StartsWith("Monster_") ? gameObject.name.Substring("Monster_".Length)
                                                                 : gameObject.name;

        // The mob is foot-pivoted (UnitAnchor invariant): transform.position IS the feet, so the plate just
        // drops the small gap below it — no centre→feet correction needed.
        feetOffset = -gap;
    }

    void OnDestroy()
    {
        if (states != null) states.OnChanged -= OnStatesChanged;
        // The widget lives under the shared canvas, not under the mob, so it must be cleaned up by hand
        // (a no-op if the canvas — and with it the child — was already torn down on scene unload).
        if (root != null) Destroy(root.gameObject);
    }

    void LateUpdate()
    {
        if (camT == null) { var c = Camera.main; if (c == null) return; camT = c.transform; }

        // Hidden when dead, and (for normal mobs) while at full HP — only appears once damaged, like RO.
        bool show = health.Current > 0 && (!hideAtFullHealth || health.Current < health.maxHealth);
        if (!show) { if (root != null && shown) { shown = false; root.gameObject.SetActive(false); } return; }

        if (root == null) Build();                 // lazy: build the widget only the first time it needs to show
        if (!shown) { shown = true; iconsDirty = true; root.gameObject.SetActive(true); }

        // Follow the mob (after it has moved this frame) and yaw to face the camera — same as DamagePopup.
        root.position = transform.position + new Vector3(0f, feetOffset, 0f);
        root.rotation = camT.rotation;

        if (health.Current != lastShownHP)         // only touch UI when HP actually changed (no per-frame rebuild)
        {
            lastShownHP = health.Current;
            float frac = Mathf.Clamp01((float)health.Current / Mathf.Max(1, health.maxHealth));
            fill.fillAmount = frac;
            fill.color = HpColor(frac);
        }

        if (states != null)
        {
            states.Prune();                        // clear silently-expired states (fires OnChanged → iconsDirty)
            if (iconsDirty) { iconsDirty = false; RebuildIcons(); }
        }
    }

    // Green when healthy → yellow → red when nearly dead, so HP reads at a glance.
    static Color HpColor(float f)
    {
        var green  = new Color(0.40f, 0.85f, 0.30f);
        var yellow = new Color(0.95f, 0.80f, 0.20f);
        var red    = new Color(0.88f, 0.20f, 0.20f);
        return f > 0.5f ? Color.Lerp(yellow, green, (f - 0.5f) * 2f) : Color.Lerp(red, yellow, f * 2f);
    }

    void Build()
    {
        EnsureHost();

        root = NewRect("MobNameplate", host);
        root.localScale = Vector3.one * worldScale;
        root.pivot = new Vector2(0.5f, 1f);                 // anchored at the top (the feet line); grows downward
        root.sizeDelta = new Vector2(barWidth, barHeight);

        // HP bar background (dark track), top of the plate.
        var bg = NewImage("Bar", root, new Color(0.06f, 0.05f, 0.07f, 0.85f));
        Place(bg.rectTransform, Vector2.zero, new Vector2(barWidth, barHeight));

        // HP bar fill, inset inside the track, drains right→left.
        fill = NewImage("Fill", bg.rectTransform, HpColor(1f));
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.pivot = new Vector2(0.5f, 0.5f);
        frt.offsetMin = new Vector2(1.5f, 1.5f); frt.offsetMax = new Vector2(-1.5f, -1.5f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;

        // Name, just BELOW the bar — opt-in (off for normal mobs, on for the boss). The hover panel still
        // shows the name regardless, so hiding this only declutters the world, it doesn't lose the info.
        if (showName)
        {
            var labelGO = new GameObject("Name", typeof(RectTransform));
            var lrt = (RectTransform)labelGO.transform;
            lrt.SetParent(root, false);
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f); lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, -barHeight - 2f);
            lrt.sizeDelta = new Vector2(barWidth * 3f, nameFontSize + 4);
            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.font = UiKit.BodyFont;
            // Shared outlined material = dark edge (reads on any terrain) + ZTest-Always (draws over the ground).
            label.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.BodyFont);
            label.fontSize = nameFontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Top;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.color = new Color(0.97f, 0.97f, 0.99f);
            label.text = displayName;
        }

        lastShownHP = int.MinValue;                         // force the first fill refresh in LateUpdate

        if (states == null) states = health.States;         // the mob's unified state store (shared with Health/StatusController)
        if (states != null) { states.OnChanged -= OnStatesChanged; states.OnChanged += OnStatesChanged; }
        iconsDirty = true;
    }

    void OnStatesChanged() => iconsDirty = true;

    // Rebuild the debuff-icon row from the live states. Reuses pooled Images (no alloc after warmup), skips states with
    // no icon (nothing to draw on a tiny plate), and centres the row above the bar. Runs only on a set change / expiry.
    void RebuildIcons()
    {
        if (root == null || states == null) return;
        var list = states.Active;
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s.icon == null) continue;
            var img = IconSlot(n);
            img.sprite = s.icon;
            img.preserveAspect = true;
            img.color = s.tint == default ? Color.white : s.tint;
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
            // Stack count sits on the icon; blank at a single stack. Rebuilds only on a state change, never per frame.
            var cnt = iconCounts[n];
            int stacks = s.StackCount;
            if (stacks > 1) { cnt.text = stacks.ToString(); if (!cnt.gameObject.activeSelf) cnt.gameObject.SetActive(true); }
            else if (cnt.gameObject.activeSelf) cnt.gameObject.SetActive(false);
            n++;
        }
        for (int i = n; i < iconSlots.Count; i++)
        {
            if (iconSlots[i].gameObject.activeSelf) iconSlots[i].gameObject.SetActive(false);
            if (iconCounts[i].gameObject.activeSelf) iconCounts[i].gameObject.SetActive(false);
        }

        if (n == 0) return;
        float total = n * IconSize + (n - 1) * IconGap;     // centre the row on the plate, sitting just above the bar
        float x = -total * 0.5f + IconSize * 0.5f;
        for (int i = 0; i < n; i++)
        {
            iconSlots[i].rectTransform.anchoredPosition = new Vector2(x, IconRowGap);
            x += IconSize + IconGap;
        }
    }

    // Pooled icon slot: grow the pool on demand (rare — only when a mob carries more debuffs than ever before), then
    // reuse. Each icon anchors to the plate's top-centre (the feet line) with a bottom pivot so it grows UP over the bar.
    Image IconSlot(int i)
    {
        while (iconSlots.Count <= i)
        {
            var img = NewImage("StateIcon", root, Color.white);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(IconSize, IconSize);
            img.gameObject.SetActive(false);
            iconSlots.Add(img);

            // Its stack-count badge, parented to the icon so it rides along when the row re-lays out.
            var go = new GameObject("Count", typeof(RectTransform));
            var crt = (RectTransform)go.transform;
            crt.SetParent(rt, false);
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.font = UiKit.BodyFont;
            txt.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.BodyFont);
            txt.fontSize = IconSize * 0.8f;
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.BottomRight;
            txt.textWrappingMode = TextWrappingModes.NoWrap;
            txt.raycastTarget = false;
            txt.color = StackColor;
            go.SetActive(false);
            iconCounts.Add(txt);
        }
        return iconSlots[i];
    }

    // ---- shared resources --------------------------------------------------------------------------
    static void EnsureHost()
    {
        if (host != null) return;                           // alive (Unity-null check); recreated after a scene unload
        var go = new GameObject("MobNameplateSystem") { hideFlags = HideFlags.DontSaveInEditor };   // runtime-only pool host — never serialize into a scene
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DepthSortRuntime.FxPopup;     // FX band: above all entities + overhead
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        host = go.transform;
    }

    // One shared UI material that ignores the depth buffer, so a plate sitting at the mob's feet draws on
    // top of the terrain it stands on instead of being clipped into it.
    static Material OnTop
    {
        get
        {
            if (onTopMat != null) return onTopMat;
            var sh = Shader.Find("UI/Default");
            if (sh == null) return null;
            onTopMat = new Material(sh) { name = "MobNameplate (on top)" };
            onTopMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            return onTopMat;
        }
    }

    static Sprite White => whiteSprite != null
        ? whiteSprite
        : (whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f)));

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = White;
        img.color = color;
        img.raycastTarget = false;
        if (OnTop != null) img.material = OnTop;
        return img;
    }

    // Anchor a child to the top-centre of the plate (the feet line) at the given size.
    static void Place(RectTransform rt, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }
}
