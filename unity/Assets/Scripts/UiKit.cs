using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tiny runtime uGUI construction kit shared by the progression windows — the "unwritten" HUD
/// palette plus helpers to build images / text / buttons / a framed window / a rich ItemSlot.
/// Mirrors the editor builders' primitives, but usable at runtime so each window can build itself.
/// </summary>
public static class UiKit
{
    // HUD v2 typeface: Cinzel for titles/section heads, Inter for body/numbers (locked token sheet).
    // TextMeshPro (SDF) so text stays razor-sharp at ANY window size — the whole game routes text through
    // here. The TMP font assets are created ONCE at runtime from the TTFs already in Resources/Fonts
    // (dynamic SDF atlas, glyphs populated on demand) — no Font Asset Creator step, no saved .asset to bake.
    public static TMP_FontAsset TitleFont => _title != null ? _title : (_title = MakeFont("Fonts/Cinzel"));
    public static TMP_FontAsset BodyFont  => _body  != null ? _body  : (_body  = MakeFont("Fonts/Inter"));
    static TMP_FontAsset _title, _body, _pixel, _fallback;

    /// <summary>Back-compat alias: legacy callers using <c>UiKit.Font</c> get the body face.</summary>
    public static TMP_FontAsset Font => BodyFont;

    /// <summary>The crunchy pixel face for RO/OSRS-style combat numbers.</summary>
    public static TMP_FontAsset PixelFont => _pixel != null ? _pixel : (_pixel = MakeFont("Fonts/PixelFont"));

    static TMP_FontAsset Fallback => _fallback != null ? _fallback
        : (_fallback = TMP_Settings.defaultFontAsset != null
            ? TMP_Settings.defaultFontAsset
            : TMP_FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));

    static TMP_FontAsset MakeFont(string path)
    {
        var ttf = Resources.Load<Font>(path);
        return ttf != null ? TMP_FontAsset.CreateFontAsset(ttf) : Fallback;
    }

    // World-space labels (damage numbers, mob nameplates) float over bare terrain with no backing plate,
    // so they need a dark outline to stay legible. TMP outline is a material property — we cache ONE shared
    // outlined material per font asset (never a per-object instance) so thousands of pooled damage numbers
    // still batch into one draw call. Replaces the old per-object legacy Outline/Shadow components.
    static readonly System.Collections.Generic.Dictionary<TMP_FontAsset, Material> _outlined = new();
    public static Material OutlinedMaterial(TMP_FontAsset font, float width = 0.2f)
    {
        if (font == null) return null;
        if (_outlined.TryGetValue(font, out var m) && m != null) return m;
        m = new Material(font.material);
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        m.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0, 0, 0, 0.9f));
        // World-space labels sit at a unit's feet; ZTest Always makes them draw ON TOP of the terrain they
        // stand on instead of being clipped into it (replaces the old per-plate "UI/Default on-top" material).
        if (m.HasProperty("_ZTestMode"))
            m.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
        _outlined[font] = m;
        return m;
    }

    // Legacy uGUI enums → TMP equivalents, so every call site can keep passing FontStyle/TextAnchor
    // (one translation point instead of touching a dozen private text factories).
    public static FontStyles TmpStyle(FontStyle s) =>
        s == FontStyle.BoldAndItalic ? (FontStyles.Bold | FontStyles.Italic)
        : s == FontStyle.Bold ? FontStyles.Bold
        : s == FontStyle.Italic ? FontStyles.Italic
        : FontStyles.Normal;

    public static TextAlignmentOptions TmpAlign(TextAnchor a)
    {
        switch (a)
        {
            case TextAnchor.UpperLeft:   return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight:  return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft:  return TextAlignmentOptions.Left;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft:   return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight:  return TextAlignmentOptions.BottomRight;
            default:                     return TextAlignmentOptions.Center;
        }
    }

    /// <summary>THE central TMP text factory — every text element in the game should come from here (or a
    /// helper that delegates to it). Keeps the legacy FontStyle/TextAnchor call convention.</summary>
    public static TextMeshProUGUI MakeText(string name, Transform parent, string s, float size,
        FontStyle style, TextAnchor anchor, TMP_FontAsset font = null, bool wrap = true)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = font != null ? font : BodyFont;
        t.fontSize = size;
        t.fontStyle = TmpStyle(style);
        t.alignment = TmpAlign(anchor);
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.color = TextMain;
        t.text = s;
        return t;
    }

    public static readonly Color PanelFill = new Color(0.082f, 0.086f, 0.102f, 0.98f); // #15161A
    public static readonly Color PanelRaise = new Color(0.130f, 0.135f, 0.155f, 0.98f);// detail/sub-panels: same charcoal family, ~6% lighter than PanelFill
    public static readonly Color SlotFill  = new Color(0.07f, 0.075f, 0.09f, 1f);
    public static readonly Color Gold      = new Color(0.784f, 0.659f, 0.384f, 1f);    // #C8A862
    public static readonly Color Cyan      = new Color(0.35f, 0.95f, 1f, 1f);
    // Hover bloom kept deliberately soft so the hierarchy stays rarity-border > select-ring > hover.
    public static readonly Color CyanGlow  = new Color(0.30f, 0.85f, 1f, 0.22f);
    public static readonly Color TextMain  = new Color(0.906f, 0.890f, 0.835f);        // #E7E3D5
    public static readonly Color TextDim   = new Color(0.655f, 0.620f, 0.533f);        // #A79E88

    /// <summary>
    /// Single source of truth for HUD text crispness. Legacy uGUI <see cref="Text"/> rasterizes its glyph
    /// atlas at (fontSize × canvas.scaleFactor × dynamicPixelsPerUnit) texels; with the default DPU of 1,
    /// shrinking the window below the 1280×720 reference drops scaleFactor under 1 and glyphs get baked into
    /// too few texels → the chunky/blurry look. Baking at this higher density keeps text sharp at ANY window
    /// size. EVERY CanvasScaler in the game must route through <see cref="ApplyTextCrispness"/> so no canvas
    /// drifts back to blurry — raise it here once, not per canvas.
    /// </summary>
    public const float UiTextDensity = 3f;

    /// <summary>Apply the shared HUD text density to a canvas's scaler. Call on every UI CanvasScaler.</summary>
    public static void ApplyTextCrispness(CanvasScaler scaler)
    {
        if (scaler != null) scaler.dynamicPixelsPerUnit = UiTextDensity;
    }

    public static Image Img(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c;
        return img;
    }

    /// <summary>
    /// THE single click-catcher for any window/panel. Inserts a full-rect, fully transparent, raycast-blocking
    /// Image as the BACKMOST child of <paramref name="root"/>, so the WHOLE window area counts as UI. Without
    /// it, a click that lands in a gap (transparent margin, space between elements) falls through to the 3D
    /// world and hits a mob behind the window — this closes every such hole in one place. Idempotent: safe to
    /// call more than once per root. Call it once per window/panel root; every world-input script already
    /// skips clicks when <c>EventSystem.IsPointerOverGameObject()</c> is true, which this guarantees.
    /// </summary>
    public static void ClickShield(RectTransform root)
    {
        if (root == null || root.Find("ClickShield") != null) return;
        var img = Img("ClickShield", root, new Color(0f, 0f, 0f, 0f));   // invisible
        Fill(img.rectTransform, Vector2.zero, Vector2.zero);            // span the entire root rect
        img.raycastTarget = true;                                       // catch clicks anywhere over it
        img.transform.SetAsFirstSibling();                             // sits BEHIND all real content
    }

    /// <summary>A drop-shadowed uGUI label. Pass <paramref name="title"/>=true for the Cinzel title
    /// face (window headers, section heads, item names); default is the Inter body face.</summary>
    public static TextMeshProUGUI Label(string name, Transform parent, string s, int size, FontStyle style, TextAnchor anchor, bool title = false)
        => MakeText(name, parent, s, size, style, anchor, title ? TitleFont : BodyFont);

    /// <summary>A gold-bordered button with a centered label. Returns the Button.</summary>
    public static Button Btn(string label, Transform parent, Action onClick)
    {
        var border = Img("Btn_" + label, parent, Gold);
        var fill = Img("Fill", border.transform, new Color(0.12f, 0.12f, 0.14f, 1f));
        Fill(fill.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        var t = Label("Text", fill.transform, label, 14, FontStyle.Bold, TextAnchor.MiddleCenter);
        Fill(t.rectTransform, Vector2.zero, Vector2.zero);
        t.color = Gold; t.raycastTarget = false;
        var btn = border.gameObject.AddComponent<Button>();
        btn.targetGraphic = border;
        // shared hover/press feel for every HUD button (see UiButtonFx) — added once at the factory
        border.gameObject.AddComponent<UiButtonFx>();
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        return btn;
    }

    /// <summary>A 20×20 tick box, anchored to the RIGHT edge of <paramref name="parent"/> — the shared
    /// on/off control for option rows. Returns the Toggle so the caller wires onValueChanged.</summary>
    public static Toggle Checkbox(Transform parent, bool on)
    {
        var box = Img("Checkbox", parent, new Color(0.09f, 0.10f, 0.13f, 1f));
        var brt = box.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
        brt.pivot = new Vector2(1f, 0.5f);
        brt.sizeDelta = new Vector2(20f, 20f);

        var tick = Img("Tick", box.transform, Cyan);
        tick.raycastTarget = false;
        Fill(tick.rectTransform, new Vector2(4, 4), new Vector2(-4, -4));

        var toggle = box.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = tick;
        toggle.isOn = on;
        return toggle;
    }

    /// <summary>A horizontal 0–1 slider filling the RIGHT half of <paramref name="parent"/> — the shared
    /// control for option rows that hold an amount. Returns the Slider so the caller wires onValueChanged.</summary>
    public static Slider MakeSlider(Transform parent, float value)
    {
        var track = Img("Slider", parent, SlotFill);
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(0.45f, 0.5f);
        trt.anchorMax = new Vector2(1f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.offsetMin = new Vector2(0f, -5f);
        trt.offsetMax = new Vector2(-2f, 5f);

        // Fill grows from the left edge; the Slider drives its width via fillRect.
        var fillArea = Img("FillArea", track.transform, new Color(0, 0, 0, 0));
        fillArea.raycastTarget = false;
        Fill(fillArea.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        var fill = Img("Fill", fillArea.transform, Gold);
        fill.raycastTarget = false;
        Fill(fill.rectTransform, Vector2.zero, Vector2.zero);

        var handle = Img("Handle", track.transform, TextMain);
        handle.rectTransform.sizeDelta = new Vector2(10f, 18f);

        var slider = track.gameObject.AddComponent<Slider>();
        slider.targetGraphic = handle;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = value;
        return slider;
    }

    public static void Fill(RectTransform rt, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
    }

    /// <summary>Build a framed, draggable window. Returns its Content area; the window root (for
    /// toggling) comes back via <paramref name="root"/>. Hidden (inactive) by default.</summary>
    public static RectTransform Window(Transform canvas, string title, Vector2 size, Vector2 pos, out GameObject root)
    {
        var border = Img("Window_" + title, canvas, Gold);
        var rt = border.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var frame = border.gameObject.AddComponent<WindowFrame>();
        frame.persistKey = title;
        ClickShield(rt);   // catch clicks over the whole window (gaps included) so none leak to the world

        var fill = Img("Fill", border.transform, PanelFill);
        Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        fill.raycastTarget = true;

        // header (drag) + close
        var header = Img("Header", fill.transform, new Color(Gold.r, Gold.g, Gold.b, 0.16f));
        header.rectTransform.anchorMin = new Vector2(0, 1);
        header.rectTransform.anchorMax = new Vector2(1, 1);
        header.rectTransform.pivot = new Vector2(0.5f, 1);
        header.rectTransform.sizeDelta = new Vector2(0, 32);
        header.gameObject.AddComponent<WindowDragHandle>().window = rt;

        var titleT = Label("Title", header.transform, title, 17, FontStyle.Bold, TextAnchor.MiddleLeft, title: true);
        Fill(titleT.rectTransform, new Vector2(14, 0), new Vector2(-36, 0));
        titleT.color = Gold; titleT.raycastTarget = false;

        // Close: a generous transparent hit area (full header height) carries the raycast +
        // WindowCloseButton; the visible red cell + X are non-raycast children centered inside it.
        // Decoupling the hit box from the small 24px visual keeps the clickable region centered on
        // the glyph with margin on every side, so it can't drift off the icon ("had to click to the
        // right of the X" bug).
        var close = Img("Close", header.transform, new Color(0f, 0f, 0f, 0f));
        close.rectTransform.anchorMin = close.rectTransform.anchorMax = new Vector2(1, 0.5f);
        close.rectTransform.pivot = new Vector2(1, 0.5f);
        close.rectTransform.sizeDelta = new Vector2(38, 32);
        close.rectTransform.anchoredPosition = Vector2.zero;
        close.gameObject.AddComponent<WindowCloseButton>().window = border.gameObject;

        var closeVis = Img("CloseVis", close.transform, new Color(0.6f, 0.16f, 0.16f, 0.9f));
        closeVis.raycastTarget = false;
        closeVis.rectTransform.anchorMin = closeVis.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        closeVis.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        closeVis.rectTransform.sizeDelta = new Vector2(24, 24);
        closeVis.rectTransform.anchoredPosition = new Vector2(-2, 0);
        var x = Label("X", closeVis.transform, "X", 15, FontStyle.Bold, TextAnchor.MiddleCenter);
        Fill(x.rectTransform, Vector2.zero, Vector2.zero); x.raycastTarget = false;

        var content = Img("Content", fill.transform, new Color(0, 0, 0, 0));
        content.raycastTarget = false;
        Fill(content.rectTransform, new Vector2(14, 14), new Vector2(-14, -40));

        // Ornament Kit v1: corner brackets + a header divider (procedural placeholders until kit art is wired).
        Ornament.DecorateWindow(fill.rectTransform, 32f);

        border.gameObject.SetActive(false);
        root = border.gameObject;
        return content.rectTransform;
    }

    /// <summary>A rich ItemSlot wired with all channels. Sibling order keeps the cyan select ring
    /// inside the rarity border and the icon on top.</summary>
    public static ItemSlot Slot(Transform parent, int size, EquipSlotType accepts = EquipSlotType.None)
    {
        var border = Img("Slot", parent, Color.white);
        border.rectTransform.sizeDelta = new Vector2(size, size);
        var le = border.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = size;
        border.raycastTarget = true;

        var glow = Img("HoverGlow", border.transform, CyanGlow);
        Fill(glow.rectTransform, new Vector2(-8, -8), new Vector2(8, 8));   // soft outer bloom (~8px spread)
        glow.raycastTarget = false; glow.gameObject.SetActive(false);

        // Green "valid drop target" halo shown only while dragging (SlotDragHandler). A touch wider and
        // more opaque than the cyan hover bloom so targets read clearly; kept behind the icon/badges.
        var drop = Img("DropHighlight", border.transform, new Color(0.35f, 1f, 0.45f, 0.30f));
        Fill(drop.rectTransform, new Vector2(-9, -9), new Vector2(9, 9));
        drop.raycastTarget = false; drop.gameObject.SetActive(false);

        // Selection = a clear-but-not-shouty cyan ring INSIDE the rarity border. Inset 1 with the
        // fill at inset 3 leaves a ~2px ring; alpha 0.78 reads as intentional without out-shouting rarity.
        var ring = Img("SelectRing", border.transform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.78f));
        Fill(ring.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        ring.raycastTarget = false; ring.gameObject.SetActive(false);

        var fill = Img("Fill", border.transform, SlotFill);
        Fill(fill.rectTransform, new Vector2(3, 3), new Vector2(-3, -3));
        fill.raycastTarget = false;

        var icon = Img("Icon", fill.transform, Color.white);
        Fill(icon.rectTransform, new Vector2(7, 7), new Vector2(-7, -7));
        icon.raycastTarget = false; icon.preserveAspect = true;

        var badge = Label("Badge", border.transform, "", 14, FontStyle.Bold, TextAnchor.UpperLeft);
        badge.color = Gold;
        badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0, 1);
        badge.rectTransform.pivot = new Vector2(0, 1);
        badge.rectTransform.sizeDelta = new Vector2(40, 18);
        badge.rectTransform.anchoredPosition = new Vector2(3, -2);
        badge.raycastTarget = false;

        var pips = Label("Pips", border.transform, "", 14, FontStyle.Bold, TextAnchor.LowerCenter);
        pips.rectTransform.anchorMin = new Vector2(0, 0);
        pips.rectTransform.anchorMax = new Vector2(1, 0);
        pips.rectTransform.pivot = new Vector2(0.5f, 0);
        pips.rectTransform.sizeDelta = new Vector2(0, 15);
        pips.rectTransform.anchoredPosition = new Vector2(0, 1);
        pips.raycastTarget = false;

        var slot = border.gameObject.AddComponent<ItemSlot>();
        slot.accepts = accepts;
        slot.iconImage = icon;
        slot.rarityBorder = border;
        slot.upgradeBadge = badge;
        slot.cardPips = pips;
        slot.selectRing = ring.gameObject;
        slot.hoverGlow = glow.gameObject;
        slot.dropHighlight = drop.gameObject;
        return slot;
    }
}
