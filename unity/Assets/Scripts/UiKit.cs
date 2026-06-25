using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Tiny runtime uGUI construction kit shared by the progression windows — the "unwritten" HUD
/// palette plus helpers to build images / text / buttons / a framed window / a rich ItemSlot.
/// Mirrors the editor builders' primitives, but usable at runtime so each window can build itself.
/// </summary>
public static class UiKit
{
    // HUD v2 typeface: Cinzel for titles/section heads, Inter for body/numbers (locked token sheet).
    // Drop the TTFs at Resources/Fonts/Cinzel.ttf and Resources/Fonts/Inter.ttf (import as Font). Until
    // they land, both fall back to the built-in legacy font so the UI still builds and runs.
    public static Font TitleFont => _title != null ? _title : (_title = Load("Fonts/Cinzel"));
    public static Font BodyFont  => _body  != null ? _body  : (_body  = Load("Fonts/Inter"));
    static Font _title, _body, _fallback;

    /// <summary>Back-compat alias: legacy callers using <c>UiKit.Font</c> get the body face.</summary>
    public static Font Font => BodyFont;

    static Font Fallback => _fallback != null ? _fallback : (_fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
    static Font Load(string path) { var f = Resources.Load<Font>(path); return f != null ? f : Fallback; }

    public static readonly Color PanelFill = new Color(0.082f, 0.086f, 0.102f, 0.98f); // #15161A
    public static readonly Color SlotFill  = new Color(0.07f, 0.075f, 0.09f, 1f);
    public static readonly Color Gold      = new Color(0.784f, 0.659f, 0.384f, 1f);    // #C8A862
    public static readonly Color Cyan      = new Color(0.35f, 0.95f, 1f, 1f);
    public static readonly Color CyanGlow  = new Color(0.30f, 0.85f, 1f, 0.45f);
    public static readonly Color TextMain  = new Color(0.906f, 0.890f, 0.835f);        // #E7E3D5
    public static readonly Color TextDim   = new Color(0.655f, 0.620f, 0.533f);        // #A79E88

    public static Image Img(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c;
        return img;
    }

    /// <summary>A drop-shadowed uGUI label. Pass <paramref name="title"/>=true for the Cinzel title
    /// face (window headers, section heads, item names); default is the Inter body face.</summary>
    public static Text Label(string name, Transform parent, string s, int size, FontStyle style, TextAnchor anchor, bool title = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = title ? TitleFont : BodyFont; t.fontSize = size; t.fontStyle = style; t.alignment = anchor;
        t.color = TextMain; t.text = s;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.7f);
        sh.effectDistance = new Vector2(1, -1);
        return t;
    }

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
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        return btn;
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

        var close = Img("Close", header.transform, new Color(0.6f, 0.16f, 0.16f, 0.9f));
        close.rectTransform.anchorMin = close.rectTransform.anchorMax = new Vector2(1, 0.5f);
        close.rectTransform.pivot = new Vector2(1, 0.5f);
        close.rectTransform.sizeDelta = new Vector2(24, 24);
        close.rectTransform.anchoredPosition = new Vector2(-5, 0);
        close.gameObject.AddComponent<WindowCloseButton>().window = border.gameObject;
        var x = Label("X", close.transform, "X", 15, FontStyle.Bold, TextAnchor.MiddleCenter);
        Fill(x.rectTransform, Vector2.zero, Vector2.zero); x.raycastTarget = false;

        var content = Img("Content", fill.transform, new Color(0, 0, 0, 0));
        content.raycastTarget = false;
        Fill(content.rectTransform, new Vector2(14, 14), new Vector2(-14, -40));

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
        Fill(glow.rectTransform, new Vector2(-6, -6), new Vector2(6, 6));
        glow.raycastTarget = false; glow.gameObject.SetActive(false);

        var ring = Img("SelectRing", border.transform, Cyan);
        Fill(ring.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
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
        return slot;
    }
}
