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
    // THE interface typeface: DejaVu Sans Condensed, the one face every window, button, name and label is drawn in
    // (the combat numbers keep PixelFont). It is the free stand-in for Tahoma, whose licence forbids shipping it.
    // TextMeshPro (SDF) so text stays razor-sharp at ANY window size — the whole game routes text through
    // here. It is a SAVED font asset (Resources/Fonts, dynamic SDF atlas, glyphs populated on demand) so text baked
    // into the saved HUD carries it too: a text saved with no font is handed TMP's default the moment it switches on
    // in the editor, which marks the scene modified and stops every build (tools/check-scene-stays-clean.py).
    public static TMP_FontAsset Font => _font != null ? _font : (_font = LoadSavedFont());
    static TMP_FontAsset _font, _pixel, _fallback;

    static TMP_FontAsset LoadSavedFont()
    {
        var saved = Resources.Load<TMP_FontAsset>("Fonts/DejaVuSansCondensed SDF");
        return saved != null ? saved : Fallback;
    }

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

    /// <summary>THE one switch that makes a UI material draw OVER the world — over the ground, grass, trees and walls
    /// — instead of being hidden by whatever stands in front of it. Everything that floats over a unit is HUD: a name,
    /// a health or cast bar, a damage number, a battle notice. Every material one of those is drawn with passes through
    /// here (<see cref="OutlinedMaterial"/> for words, <see cref="MobNameplate.SharedOnTop"/> for bars and tiles), so
    /// they cannot disagree about it.
    ///
    /// <para>WORDS are moved onto their text shader's own always-on-top twin (the same shader with "Overlay" on its
    /// name, which differs only in testing against nothing). A shader is carried by every copy of a material, and the
    /// text package copies this material by itself for each letter it has to take from a fallback font — a copy that
    /// drops the switch below. The twin asks to be drawn later than the original, so the order the original was
    /// drawn in is put back: nothing changes place against the bars. It is found by name, so it is on Project
    /// Settings ▸ Graphics ▸ Always Included Shaders (ASSETS.md, rule 12).</para>
    ///
    /// <para>EVERYTHING ELSE — the built-in UI shader the bars and tiles use has no such twin — gets the test every UI
    /// shader names in its own source (<c>ZTest [unity_GUIZTestMode]</c>). No shader lists it as a property, so asking
    /// the material whether it "has" it answers no for all of them; it is set regardless. For the same reason
    /// <c>new Material(other)</c> does not copy it: a copy of one of these must be passed through here again.</para></summary>
    public static void DrawOverTheWorld(Material m)
    {
        if (m == null) return;
        if (m.HasProperty(ShaderUtilities.ID_OutlineWidth))
        {
            var onTop = m.shader.name.EndsWith(OnTopTextSuffix) ? m.shader : Shader.Find(m.shader.name + OnTopTextSuffix);   // shader-ok: TMP's "<text shader> Overlay" twin, on Always Included; if absent this logs an error and keeps the shader — it never throws
            if (onTop != null)
            {
                if (onTop == m.shader) return;
                int drawnInOrder = m.renderQueue;
                m.shader = onTop;
                m.renderQueue = drawnInOrder;
                return;
            }
            Debug.LogError($"[UiKit] no always-on-top twin of the text shader '{m.shader.name}' — a letter from a fallback font will be hidden behind scenery");
        }
        m.SetInt(GuiDepthTest, (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    const string OnTopTextSuffix = " Overlay";
    static readonly int GuiDepthTest = Shader.PropertyToID("unity_GUIZTestMode");

    // World-space labels (damage numbers, mob nameplates) float over bare terrain with no backing plate,
    // so they need a dark outline to stay legible. TMP outline is a material property — we cache ONE shared
    // outlined material per font asset and outline width (never a per-object instance) so thousands of pooled
    // damage numbers still batch into one draw call. Replaces the old per-object legacy Outline/Shadow components.
    static readonly System.Collections.Generic.Dictionary<(TMP_FontAsset, float), Material> _outlined = new();
    public static Material OutlinedMaterial(TMP_FontAsset font, float width = 0.2f)
    {
        if (font == null) return null;
        if (_outlined.TryGetValue((font, width), out var m) && m != null) return m;
        m = new Material(font.material);
        // Words floating in the world draw over it, never behind the grass or the ground they stand on. MOVED ONTO
        // THE ALWAYS-ON-TOP SHADER FIRST, and only then given the edge: a material changing shader keeps no promise
        // about the switches set on the old one, and the black edge went missing from every floating word the batch
        // this swap arrived in (2026-10-12).
        DrawOverTheWorld(m);
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        m.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0, 0, 0, 0.9f));
        _outlined[(font, width)] = m;
        return m;
    }

    // THE look of a name floating over the world — the name over whoever you point at, and an item's name on the
    // floor. The interface face, drawn bold, with a heavier black edge than other outlined text (0.3, the width the damage
    // numbers use) so it reads over any ground. The bold is heavier too: the font file holds only the regular
    // weight, so bold is drawn by thickening the letters, and the font's default amount (0.75) is too slight to see
    // at these sizes. 1.5 is double it, and still fits inside the margin the letters are stored with once the edge
    // is added. Set the text's style to bold as well, or this thickening is never used.
    const float WorldNameBoldWeight = 1.5f;
    static Material _worldName;
    public static Material WorldNameMaterial()
    {
        if (_worldName != null) return _worldName;
        var outlined = OutlinedMaterial(Font, 0.3f);
        if (outlined == null) return null;
        _worldName = new Material(outlined);
        // Said again for the copy: it keeps the always-on-top shader, but would not keep the fallback switch.
        DrawOverTheWorld(_worldName);
        _worldName.SetFloat(ShaderUtilities.ID_WeightBold, WorldNameBoldWeight);
        return _worldName;
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
        t.font = font != null ? font : Font;
        t.fontSize = size;
        t.fontStyle = TmpStyle(style);
        t.alignment = TmpAlign(anchor);
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.color = TextMain;
        t.text = s;
        return t;
    }

    // ---- THE interface theme: the one place every window, panel, button, label and bar reads its colours from ----
    // Light windows with a pale-blue fading title bar, a soft blue-grey edge, rounded corners and dark text. No script
    // types an interface colour of its own: a new colour need is a new row here, named for what it paints.
    // The combat numbers (DamagePopup) are the one exception and keep their own colours.
    // #F7F8FA — every window's body
    public static readonly Color PanelFill = new Color(0.969f, 0.973f, 0.980f);
    // #EDF1F7 — a sub-panel inside a window
    public static readonly Color PanelRaise = new Color(0.929f, 0.945f, 0.969f);
    // #FFFFFF — a text field, slot or track well
    public static readonly Color SlotFill = new Color(1.000f, 1.000f, 1.000f);
    // #7F97BD — the edge around every window
    public static readonly Color WindowEdge = new Color(0.498f, 0.592f, 0.741f);
    // #E6EEFA — the title bar fades from this…
    public static readonly Color TitleBarTop = new Color(0.902f, 0.933f, 0.980f);
    // #B9CCE9 — …down to this
    public static readonly Color TitleBarBottom = new Color(0.725f, 0.800f, 0.914f);
    // #22324D — a window's title, and headings
    public static readonly Color TitleText = new Color(0.133f, 0.196f, 0.302f);
    // #E9EBEF
    public static readonly Color ButtonFill = new Color(0.914f, 0.922f, 0.937f);
    // #B5BDC9 — also the edge of a text field or slot
    public static readonly Color ButtonEdge = new Color(0.710f, 0.741f, 0.788f);
    // #222222
    public static readonly Color ButtonText = new Color(0.133f, 0.133f, 0.133f);
    // #C9D3E2 — a divider line
    public static readonly Color Rule = new Color(0.788f, 0.827f, 0.886f);
    // #D6E5FB — behind whatever the pointer is over
    public static readonly Color Hover = new Color(0.839f, 0.898f, 0.984f);
    // #5B86C9 — hover edge, caret, tick, slider fill, a selected border
    public static readonly Color Accent = new Color(0.357f, 0.525f, 0.788f);
    // #D9DFE8 — the close button
    public static readonly Color CloseFill = new Color(0.851f, 0.875f, 0.910f);
    // #1B1B1B — all primary text
    public static readonly Color TextMain = new Color(0.106f, 0.106f, 0.106f);
    // #6C7380 — labels, units, secondary text
    public static readonly Color TextDim = new Color(0.424f, 0.451f, 0.502f);
    // #B83A3A — a stat that went down; a refusal
    public static readonly Color Bad = new Color(0.722f, 0.227f, 0.227f);
    // #FFFFFF — text laid straight over a darkened screen, not in a window
    public static readonly Color TextOnDim = new Color(1.000f, 1.000f, 1.000f);
    // #2E7D41 — a stat that went up
    public static readonly Color Good = new Color(0.180f, 0.490f, 0.255f);
    // #867200 — a private message in the chat: a dark gold yellow (4.47:1 against PanelFill)
    public static readonly Color PmText = new Color(0.525f, 0.447f, 0.000f);
    // An item's name by its rarity (Rarities.Color): Common is TextMain and Uncommon is Good; these three are the
    // rest, each dark enough to read at 4.5:1 or better against PanelFill.
    // #2A62B8 — a Rare item's name: blue (5.59:1)
    public static readonly Color RarityRare = new Color(0.165f, 0.384f, 0.722f);
    // #8A3FB8 — an Epic item's name: purple (5.68:1)
    public static readonly Color RarityEpic = new Color(0.541f, 0.247f, 0.722f);
    // #A85400 — a Legendary item's name: a deep orange gold (5.03:1)
    public static readonly Color RarityLegendary = new Color(0.659f, 0.329f, 0.000f);

    /// <summary>A theme colour as a rich-text tag value, "#RRGGBB" — for text that colours part of a line. Read it
    /// into a static field once; never call it per frame (it builds a string).</summary>
    public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    /// <summary>The contrast text needs against a window's body to read comfortably (the WCAG body-text figure).</summary>
    public const float ReadableContrast = 4.5f;

    /// <summary>A colour that MEANS something (a season, a weather, a skill type) made readable as TEXT on a window:
    /// returned unchanged when it already reads at <see cref="ReadableContrast"/> against <see cref="PanelFill"/>,
    /// otherwise darkened — same hue, same alpha — just far enough to. Call it when the text is built or changes,
    /// never per frame.</summary>
    public static Color Readable(Color c) => Readable(c, PanelFill);

    /// <summary>The same, against <paramref name="background"/> — for text whose plate changes colour, like a name on
    /// the floor turning to the hover fill under the cursor.</summary>
    public static Color Readable(Color c, Color background)
    {
        float bg = Luminance(background);
        if ((bg + 0.05f) / (Luminance(c) + 0.05f) >= ReadableContrast) return c;
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 20; i++)
        {
            float k = (lo + hi) * 0.5f;
            var d = new Color(c.r * k, c.g * k, c.b * k, c.a);
            if ((bg + 0.05f) / (Luminance(d) + 0.05f) >= ReadableContrast) lo = k; else hi = k;
        }
        return new Color(c.r * lo, c.g * lo, c.b * lo, c.a);
    }

    // Relative luminance of an sRGB colour, as the contrast formula defines it.
    static float Luminance(Color c)
    {
        static float Lin(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
    }

    /// <summary>THE blue title bar's height, the same on every window, menu and screen whatever it carries.</summary>
    public const float TitleBarHeight = 24f;

    // Corner rounding, in UI units, of a window and of a button/field — the same rounded shape, two radii.
    public const int WindowRadius = 5, ButtonRadius = 3;

    static readonly System.Collections.Generic.Dictionary<int, Sprite> _rounded = new();

    /// <summary>A white rounded rectangle, 9-sliced, of the given corner radius — tint it with an Image colour. One
    /// image per radius, stored in Resources/UI/Theme and written by tools/ui-theme-sprites.py; a radius with no
    /// image there is a bug, reported once.</summary>
    public static Sprite RoundedSprite(int radius)
    {
        if (_rounded.TryGetValue(radius, out var cached) && cached != null) return cached;
        var sp = Resources.Load<Sprite>("UI/Theme/Rounded" + radius);
        if (sp == null) Debug.LogError($"[UiKit] no theme image for corner radius {radius} — run tools/ui-theme-sprites.py");
        _rounded[radius] = sp;
        return sp;
    }

    /// <summary>Draw <paramref name="img"/> as a rounded rectangle of <paramref name="radius"/>, keeping its colour.</summary>
    public static Image Round(Image img, int radius)
    {
        img.sprite = RoundedSprite(radius);
        img.type = Image.Type.Sliced;
        return img;
    }

    static readonly System.Collections.Generic.Dictionary<int, Sprite> _rims = new();

    /// <summary>Make a framed shape's BODY see-through at <paramref name="alpha"/>: its <paramref name="edge"/> is
    /// redrawn as a rim only, so nothing opaque sits under the body, and the body's colour takes the alpha. The edge
    /// and body are the pair <see cref="DressWindow"/> (window radius) or <see cref="Framed"/> (button radius) made.</summary>
    public static void SeeThroughBody(Image edge, Image body, float alpha, int radius)
    {
        if (edge == null || body == null) return;
        if (!_rims.TryGetValue(radius, out var rim) || rim == null)
        {
            rim = Resources.Load<Sprite>("UI/Theme/RoundedRim" + radius);
            if (rim == null) Debug.LogError($"[UiKit] no rim image for corner radius {radius} — run tools/ui-theme-sprites.py");
            _rims[radius] = rim;
        }
        if (rim == null) return;
        edge.sprite = rim;
        var c = body.color; c.a = alpha; body.color = c;
    }

    /// <summary>Draw <paramref name="img"/> as a text field or slot well: a thin <see cref="ButtonEdge"/> rim around a
    /// <see cref="SlotFill"/> well, rounded.</summary>
    public static Image Field(Image img) => Framed(img, ButtonEdge, SlotFill, ButtonRadius);

    /// <summary>Draw <paramref name="img"/> as a card — a tooltip or hover card: the window edge around the window
    /// body, rounded.</summary>
    public static Image Card(Image img) => Framed(img, WindowEdge, PanelFill, WindowRadius);

    /// <summary>Turn <paramref name="img"/> into a one-unit <paramref name="edge"/> rim around a <paramref name="body"/>,
    /// both rounded. The body is a background child that layout groups ignore, so this is safe on an element that
    /// lays out its own children.</summary>
    public static Image Framed(Image img, Color edge, Color body, int radius)
    {
        Round(img, radius).color = edge;
        var well = Round(Img("Well", img.transform, body), Mathf.Max(1, radius - 1));
        well.raycastTarget = false;
        well.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Fill(well.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        well.transform.SetAsFirstSibling();
        return img;
    }

    /// <summary>A panel background filling <paramref name="parent"/>: the window edge around the window body, rounded,
    /// behind everything else in it. For HUD panels that are not draggable windows. Returns the body.</summary>
    public static Image Plate(Transform parent, string name = "BG")
    {
        var edge = Round(Img(name, parent, WindowEdge), WindowRadius);
        edge.raycastTarget = false;
        Fill(edge.rectTransform, Vector2.zero, Vector2.zero);
        edge.transform.SetAsFirstSibling();
        var body = Round(Img("Fill", edge.transform, PanelFill), WindowRadius - 1);
        body.raycastTarget = false;
        Fill(body.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        return body;
    }

    static Sprite _titleBody, _titleFade;

    /// <summary>THE title bar: a strip across the top of a window's body that fades from <see cref="TitleBarTop"/>
    /// down to <see cref="TitleBarBottom"/>, its top corners rounded to sit inside the body. Two tinted layers —
    /// the body in the bottom colour, the fade over it in the top colour — so the colours stay in this palette.
    /// Every header comes from here; the caller positions the returned image.</summary>
    public static Image TitleBar(Transform windowFill, string name = "Header")
    {
        if (_titleBody == null) _titleBody = Resources.Load<Sprite>("UI/Theme/TitleBarBody");
        if (_titleFade == null) _titleFade = Resources.Load<Sprite>("UI/Theme/TitleBarFade");
        var header = Img(name, windowFill, TitleBarBottom);
        header.sprite = _titleBody;
        header.type = Image.Type.Sliced;
        var fade = Img("Fade", header.transform, TitleBarTop);
        fade.sprite = _titleFade;
        fade.type = Image.Type.Sliced;
        fade.raycastTarget = false;
        fade.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Fill(fade.rectTransform, Vector2.zero, Vector2.zero);
        return header;
    }

    /// <summary>
    /// Single source of truth for HUD text crispness. Legacy uGUI <see cref="Text"/> rasterizes its glyph
    /// atlas at (fontSize × canvas.scaleFactor × dynamicPixelsPerUnit) texels; with the default DPU of 1,
    /// shrinking the window below the 1280×720 reference drops scaleFactor under 1 and glyphs get baked into
    /// too few texels → the chunky/blurry look. Baking at this higher density keeps text sharp at ANY window
    /// size. EVERY CanvasScaler in the game must route through <see cref="ApplyTextCrispness"/> so no canvas
    /// drifts back to blurry — raise it here once, not per canvas.
    /// </summary>
    public const float UiTextDensity = 3f;

    /// <summary>THE MAIN SCREEN LAYER — the one canvas the hotbar, chat and windows are drawn on, scaled with the
    /// screen's width from 1280×720. Placed in the Systems scene by the editor; null when it is not there. Looked up
    /// once and kept.</summary>
    public static Canvas HudCanvas
    {
        get
        {
            if (hudCanvas == null)
            {
                var go = GameObject.Find("GameUICanvas") ?? GameObject.Find("GameUICanvas(Clone)");
                if (go != null) hudCanvas = go.GetComponent<Canvas>();
            }
            return hudCanvas;
        }
    }
    static Canvas hudCanvas;

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

    /// <summary>A uGUI label in the interface face.</summary>
    public static TextMeshProUGUI Label(string name, Transform parent, string s, int size, FontStyle style, TextAnchor anchor)
        => MakeText(name, parent, s, size, style, anchor);

    /// <summary>A rounded button with a thin edge and a centered label. Returns the Button.</summary>
    public static Button Btn(string label, Transform parent, Action onClick)
    {
        var border = Round(Img("Btn_" + label, parent, ButtonEdge), ButtonRadius);
        var fill = Round(Img("Fill", border.transform, ButtonFill), ButtonRadius);
        Fill(fill.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        var t = Label("Text", fill.transform, label, 14, FontStyle.Normal, TextAnchor.MiddleCenter);
        Fill(t.rectTransform, Vector2.zero, Vector2.zero);
        t.color = ButtonText; t.raycastTarget = false;
        var btn = border.gameObject.AddComponent<Button>();
        btn.targetGraphic = border;
        // shared hover/press feel for every HUD button (see UiButtonFx) — added once at the factory
        border.gameObject.AddComponent<UiButtonFx>();
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        return btn;
    }

    /// <summary>THE ONE ROW OF TABS every tabbed window uses — the bag, Settings and Quests: a strip
    /// <paramref name="height"/> tall across the top of <paramref name="parent"/>, its buttons sharing the width.
    /// Add the tabs with <see cref="Btn"/>; show which is chosen with <see cref="MarkTab"/>.</summary>
    public static RectTransform TabRow(RectTransform parent, float height = 28f)
    {
        var row = Img("Tabs", parent, new Color(0, 0, 0, 0));
        var rt = row.rectTransform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, height);
        row.raycastTarget = false;
        var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 4;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = true;
        return rt;
    }

    /// <summary>Show whether a tab made by <see cref="Btn"/> is the chosen one: its edge in the accent colour when it
    /// is, the ordinary button edge when it is not.</summary>
    public static void MarkTab(Button tab, bool chosen)
    {
        if (tab == null) return;
        var edge = tab.GetComponent<Image>();
        if (edge != null) edge.color = chosen ? Accent : ButtonEdge;
    }

    /// <summary>Turn a window's fixed "Content" node into a clipped vertical scroll (wheel and drag) and return the
    /// inner node to build rows into. Idempotent: a second call returns the same inner node. THE one scroll
    /// column every list window uses — the Skills window and the station window alike — so a long list can never run
    /// off the bottom of one window and not another.</summary>
    public static RectTransform ScrollColumn(RectTransform viewport)
    {
        var existing = viewport.Find("ScrollContent") as RectTransform;
        if (existing != null) return existing;

        if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.GetComponent<Image>();
        if (vpImg != null) vpImg.raycastTarget = true;   // a graphic for the wheel to land on

        var scroll = viewport.GetComponent<ScrollRect>() ?? viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;

        var sc = new GameObject("ScrollContent", typeof(RectTransform));
        var scrt = (RectTransform)sc.transform;
        scrt.SetParent(viewport, false);
        scrt.anchorMin = new Vector2(0, 1); scrt.anchorMax = new Vector2(1, 1); scrt.pivot = new Vector2(0.5f, 1);
        scrt.offsetMin = scrt.offsetMax = Vector2.zero;
        sc.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = scrt;
        return scrt;
    }

    // ---- THE LIST WINDOW: one scrolling column of lines, buttons and deals ------------------------------------
    // The shop, the station and the Keeper are all this one window, and the System menu's entries are its buttons. They
    // used to be private copies in each; one copy here means a fix to one is a fix to all of them.

    /// <summary>A hidden <see cref="Window"/> of <paramref name="size"/> whose body is one scrolling column; returns the
    /// column, which every <see cref="ListLine"/>, <see cref="ListButton"/> and <see cref="ListDeal"/> is built into.</summary>
    public static RectTransform ListWindow(Transform canvas, string title, Vector2 size, out GameObject root)
    {
        var viewport = Window(canvas, title, size, Vector2.zero, out root);
        // A LONG LIST SCROLLS — thirty swords in the bag are a wheel away rather than off the bottom of the window.
        var content = ScrollColumn(viewport);
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;
        return content;
    }

    /// <summary>One line of text in a list, <paramref name="rows"/> lines tall — a sentence from the server needs room
    /// to wrap.</summary>
    public static TextMeshProUGUI ListLine(Transform list, string s, int size, Color color, int rows = 1)
    {
        var t = Label("Line", list, s, size, FontStyle.Normal, rows > 1 ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
        t.color = color;
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = (size + 4) * rows + 4;
        return t;
    }

    /// <summary>One full-width button in a list, <paramref name="height"/> tall. Under a vertical layout a control with
    /// no stated height is given none, so every button was drawn at zero height on top of the next — the 2026-09-21
    /// screenshot; this states it.</summary>
    public static Button ListButton(Transform list, string label, Action onClick, float height)
    {
        var btn = Btn(label, list, onClick);
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return btn;
    }

    /// <summary>One line of a list that is a deal: what it is on the left, one or two presses on the right. The first
    /// button is handed back so a guide can point at it; leave the second word null for a single press.</summary>
    public static Button ListDeal(Transform list, string label, string firstWord, Action first, string secondWord = null,
                                  Action second = null)
    {
        var row = new GameObject("Deal", typeof(RectTransform));
        row.transform.SetParent(list, false);
        row.AddComponent<LayoutElement>().preferredHeight = 30;
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        var t = Label("What", row.transform, label, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        t.color = TextMain;
        t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        var firstButton = Btn(firstWord, row.transform, first);
        firstButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 70;
        if (secondWord != null)
            Btn(secondWord, row.transform, second).gameObject.AddComponent<LayoutElement>().preferredWidth = 70;
        return firstButton;
    }

    /// <summary>A 20×20 tick box, anchored to the RIGHT edge of <paramref name="parent"/> — the shared
    /// on/off control for option rows. Returns the Toggle so the caller wires onValueChanged.</summary>
    public static Toggle Checkbox(Transform parent, bool on) => Tickable("Checkbox", parent, on, ButtonRadius);

    /// <summary>A round <see cref="Checkbox"/> — one of a set where at most one is on. The set's rule is the caller's;
    /// this only draws the button. Round because its corners are the theme's largest radius, larger than the button.</summary>
    public static Toggle RadioButton(Transform parent, bool on) => Tickable("Radio", parent, on, RoundRadius);

    // The speech bubble's corner — the largest the theme draws, which turns a 20-pixel square into a circle.
    const int RoundRadius = 14;

    static Toggle Tickable(string name, Transform parent, bool on, int radius)
    {
        var box = Round(Img(name, parent, ButtonEdge), radius);
        var brt = box.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
        brt.pivot = new Vector2(1f, 0.5f);
        brt.sizeDelta = new Vector2(20f, 20f);

        var well = Round(Img("Well", box.transform, SlotFill), radius);
        well.raycastTarget = false;
        Fill(well.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        var tick = Round(Img("Tick", box.transform, Accent), radius);
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
        var track = Round(Img("Slider", parent, ButtonEdge), ButtonRadius);
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
        var fill = Round(Img("Fill", fillArea.transform, Accent), ButtonRadius);
        fill.raycastTarget = false;
        Fill(fill.rectTransform, Vector2.zero, Vector2.zero);

        var handle = Round(Img("Handle", track.transform, WindowEdge), ButtonRadius);
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

    /// <summary>Keep what is typed into a single-line box INSIDE the box: its text and hint move into a clipped area
    /// <paramref name="inset"/> in from each side, and the box slides the text along as the caret passes the edge.
    /// Without it a long line is drawn straight on past the box's right edge.</summary>
    public static void ClipTyping(TMP_InputField box, float inset)
    {
        var area = (RectTransform)new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D)).transform;
        area.SetParent(box.transform, false);
        Fill(area, new Vector2(inset, 0), new Vector2(-inset, 0));
        foreach (var g in new Graphic[] { box.textComponent, box.placeholder })
        {
            if (g == null) continue;
            g.rectTransform.SetParent(area, false);
            Fill(g.rectTransform, Vector2.zero, Vector2.zero);
        }
        box.textViewport = area;
    }

    /// <summary>Build a framed, draggable window. Returns its Content area; the window root (for
    /// toggling) comes back via <paramref name="root"/>. Hidden (inactive) by default.</summary>
    public static RectTransform Window(Transform canvas, string title, Vector2 size, Vector2 pos, out GameObject root)
    {
        var border = Img("Window_" + title, canvas, WindowEdge);
        var rt = border.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var frame = border.gameObject.AddComponent<WindowFrame>();
        frame.persistKey = title;
        ClickShield(rt);   // catch clicks over the whole window (gaps included) so none leak to the world

        var content = DressWindow(border, title);
        border.gameObject.SetActive(false);
        root = border.gameObject;
        return content;
    }

    /// <summary>THE blue title bar across the top of a window's <paramref name="body"/>, with <paramref name="title"/>
    /// in it — every window's heading, the front screens' included. Optionally the whole bar drags
    /// <paramref name="window"/>, and a minimise (_) and a close (X) button sit at its right-hand end, close
    /// right-most. Returns the bar.</summary>
    public static Image Header(Image body, string title, Image window, bool closable, bool minimizable, bool draggable)
    {
        const float BarHeight = TitleBarHeight, ButtonSlot = 24f;
        var header = TitleBar(body.transform);
        var hrt = header.rectTransform;
        hrt.anchorMin = new Vector2(0, 1);
        hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, BarHeight);
        if (draggable) header.gameObject.AddComponent<WindowDragHandle>().window = window.rectTransform;

        int buttons = (closable ? 1 : 0) + (minimizable ? 1 : 0);
        var titleT = Label("Title", header.transform, title, 13, FontStyle.Bold, TextAnchor.MiddleLeft);
        Fill(titleT.rectTransform, new Vector2(10, 0), new Vector2(-(8 + buttons * ButtonSlot), 0));
        titleT.color = TitleText; titleT.raycastTarget = false;

        // Each button: a transparent hit area the full bar high carries the raycast; the small visible cell and its
        // glyph sit centred inside it and catch nothing, so the clickable region can never drift off the glyph.
        float right = 0f;
        if (closable)
        {
            var close = BarButton(header.transform, "Close", "X", right, ButtonSlot, BarHeight);
            close.gameObject.AddComponent<WindowCloseButton>().window = window.gameObject;
            right += ButtonSlot;
        }
        if (minimizable)
        {
            var min = window.gameObject.AddComponent<WindowMinimize>();
            min.edge = window; min.body = body; min.header = hrt;
            var btn = BarButton(header.transform, "Minimize", "_", right, ButtonSlot, BarHeight);
            btn.gameObject.AddComponent<WindowMinimizeButton>().target = min;
        }
        return header;
    }

    // One title-bar button slot, `fromRight` units in from the bar's right edge.
    static Image BarButton(Transform bar, string name, string glyph, float fromRight, float slot, float barHeight)
    {
        var hit = Img(name, bar, new Color(0f, 0f, 0f, 0f));
        hit.rectTransform.anchorMin = hit.rectTransform.anchorMax = new Vector2(1, 0.5f);
        hit.rectTransform.pivot = new Vector2(1, 0.5f);
        hit.rectTransform.sizeDelta = new Vector2(slot, barHeight);
        hit.rectTransform.anchoredPosition = new Vector2(-fromRight - 2f, 0f);

        var vis = Round(Img(name + "Vis", hit.transform, CloseFill), ButtonRadius);
        vis.raycastTarget = false;
        vis.rectTransform.anchorMin = vis.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        vis.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        vis.rectTransform.sizeDelta = new Vector2(18, 18);
        var g = Label("Glyph", vis.transform, glyph, 12, FontStyle.Bold, TextAnchor.MiddleCenter);
        Fill(g.rectTransform, Vector2.zero, Vector2.zero); g.raycastTarget = false;
        g.color = TitleText;
        return hit;
    }

    /// <summary>THE window look, put on an existing <paramref name="border"/> image however it is sized: the rounded
    /// edge, the body, the fading title bar with <paramref name="title"/> (see <see cref="Header"/> for its drag and
    /// its buttons), and an empty Content area below the bar, which is returned. Every window in the game — built at runtime or baked into the
    /// HUD — is dressed by this, so none can drift from the rest.</summary>
    public static RectTransform DressWindow(Image border, string title, bool closable = true, bool minimizable = true,
                                           bool draggable = true)
    {
        Round(border, WindowRadius).color = WindowEdge;

        var fill = Round(Img("Fill", border.transform, PanelFill), WindowRadius - 1);
        Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        fill.raycastTarget = true;

        Header(fill, title, border, closable, minimizable, draggable);

        var content = Img("Content", fill.transform, new Color(0, 0, 0, 0));
        content.raycastTarget = false;
        Fill(content.rectTransform, new Vector2(14, 14), new Vector2(-14, -(TitleBarHeight + 8f)));
        return content.rectTransform;
    }
}
