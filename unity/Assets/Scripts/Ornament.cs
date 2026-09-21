using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ornament Kit v1.1 — the "Unwritten" branding layer over the UiKit windows.
/// Art direction (GPT⇄Claude relay, 2026-06-25): <b>ink + gold, explorer's journal / grimoire margin</b>
/// — NOT royal filigree. Guardrails, enforced here:
///   • monochrome GOLD only (never competes with the art it frames),
///   • 1–2 px visual weight max (must never out-weigh item borders / loot stays the focal point),
///   • asymmetric, "hand-authored" feel (corners pinwheel; they are not a mirrored picture frame),
///   • restraint — if you strip the item icons, the bare ornament should read as an unfinished journal page.
///
/// v1.1 ships PROCEDURAL line-art (thin gold bars + small rotated-square "diamonds" + ink ticks). Each
/// motif still has a drop-in <see cref="Sprite"/> hook (<see cref="cornerArt"/>, <see cref="dividerArt"/>)
/// so hand-drawn ink art can replace it later with no layout change. Everything is decorative:
/// raycastTarget off, never in a layout group.
/// </summary>
public static class Ornament
{
    // Drop-in art hooks (null => procedural). Wire from UIShellBuilder when hand-drawn kit art exists.
    public static Sprite cornerArt;    // single corner flourish; mirrored into the other corners
    public static Sprite dividerArt;   // a horizontal divider strip (9-sliced)

    static Color Gold     => UiKit.Gold;
    static Color GoldSoft => new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.6f);

    const float Thin = 2f, Inset = 5f, ArmLong = 18f, ArmShort = 11f;

    /// <summary>Decorate a UiKit window's fill: pinwheel corner frame + a chapter divider under the header.</summary>
    public static void DecorateWindow(RectTransform fill, float headerH)
    {
        Corners(fill);
        Divider(fill, headerH);
    }

    // ---- corners: a "broken frame" — the long arm + its diamond pinwheel around the four corners so the
    //      result never reads as a perfect mirrored picture frame (GPT: "charted territory", not a frame).
    static void Corners(RectTransform fill)
    {
        Corner(fill, new Vector2(0, 1),  1, -1, true);    // top-left   : long horizontal arm
        Corner(fill, new Vector2(1, 1), -1, -1, false);   // top-right  : long vertical arm
        Corner(fill, new Vector2(0, 0),  1,  1, false);   // bottom-left: long vertical arm
        Corner(fill, new Vector2(1, 0), -1,  1, true);    // bottom-right: long horizontal arm
    }

    static void Corner(RectTransform fill, Vector2 c, float dx, float dy, bool longHorizontal)
    {
        Vector2 pos = new Vector2(Inset * dx, Inset * dy);
        if (cornerArt != null)
        {
            var img = UiKit.Img("Corner", fill, Color.white);
            img.sprite = cornerArt; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = c; rt.pivot = c;
            rt.sizeDelta = new Vector2(ArmLong, ArmLong);
            rt.anchoredPosition = pos;
            rt.localScale = new Vector3(dx, -dy, 1);   // mirror the single art into each corner
            return;
        }
        float hLen = longHorizontal ? ArmLong : ArmShort;
        float vLen = longHorizontal ? ArmShort : ArmLong;
        Bar(fill, c, new Vector2(hLen, Thin), pos);   // horizontal arm
        Bar(fill, c, new Vector2(Thin, vLen), pos);   // vertical arm
        // a small diamond at the inner tip of the LONG arm (└─◇), the asymmetric flourish.
        Vector2 dpos = longHorizontal ? new Vector2((Inset + hLen) * dx, Inset * dy)
                                      : new Vector2(Inset * dx, (Inset + vLen) * dy);
        Diamond(fill, c, dpos, 5f, Gold);
    }

    static void Bar(RectTransform parent, Vector2 corner, Vector2 size, Vector2 pos)
    {
        var img = UiKit.Img("Accent", parent, Gold);
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = corner; rt.pivot = corner;
        rt.sizeDelta = size; rt.anchoredPosition = pos;
    }

    static Image Diamond(RectTransform parent, Vector2 anchor, Vector2 pos, float size, Color color)
    {
        var d = UiKit.Img("Diamond", parent, color);
        d.raycastTarget = false;
        var rt = d.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size); rt.anchoredPosition = pos;
        rt.localRotation = Quaternion.Euler(0, 0, 45);
        return d;
    }

    // small vertical "ink tick" at a fractional position along the top edge (journal notation hint).
    static void Tick(RectTransform fill, float frac, float y)
    {
        var t = UiKit.Img("Tick", fill, GoldSoft);
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(frac, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(1.5f, 4f); rt.anchoredPosition = new Vector2(0, y + 1.5f);
    }

    // ---- divider: ────◇──── under the header — a chapter rule with a center diamond + a few ink ticks.
    static void Divider(RectTransform fill, float headerH)
    {
        float y = -(headerH + 2f);
        if (dividerArt != null)
        {
            var strip = UiKit.Img("Divider", fill, Color.white);
            strip.sprite = dividerArt; strip.type = Image.Type.Sliced; strip.raycastTarget = false;
            var r = strip.rectTransform;
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            r.sizeDelta = new Vector2(-20, 6); r.anchoredPosition = new Vector2(0, y);
            return;
        }
        const float gapHalf = 9f, h = 1.5f;
        // two hairline segments leaving a center gap for the diamond.
        var left = UiKit.Img("DivL", fill, GoldSoft); left.raycastTarget = false;
        var lr = left.rectTransform;
        lr.anchorMin = new Vector2(0, 1); lr.anchorMax = new Vector2(0.5f, 1); lr.pivot = new Vector2(0, 1);
        lr.offsetMin = new Vector2(Inset + 9, y - h); lr.offsetMax = new Vector2(-gapHalf, y);

        var right = UiKit.Img("DivR", fill, GoldSoft); right.raycastTarget = false;
        var rr = right.rectTransform;
        rr.anchorMin = new Vector2(0.5f, 1); rr.anchorMax = new Vector2(1, 1); rr.pivot = new Vector2(1, 1);
        rr.offsetMin = new Vector2(gapHalf, y - h); rr.offsetMax = new Vector2(-(Inset + 9), y);

        Diamond(fill, new Vector2(0.5f, 1), new Vector2(0, y - h * 0.5f), 7f, Gold);

        // a few irregularly-spaced ink ticks (not symmetric — hand-authored notation feel).
        Tick(fill, 0.20f, y); Tick(fill, 0.34f, y);
        Tick(fill, 0.67f, y); Tick(fill, 0.83f, y);
    }

    /// <summary>A portrait / name backplate: dark charcoal + a thin gold border + one top &amp; one bottom
    /// diamond ornament (restrained, per direction). Decorative; the caller positions it. A future
    /// billboard-portrait sprite drops into the returned plate's inner child.</summary>
    public static Image Backplate(Transform parent, Vector2 size)
    {
        var edge = UiKit.Img("Backplate", parent, GoldSoft);   // thin gold border (1px via the inner inset)
        edge.raycastTarget = false;
        edge.rectTransform.sizeDelta = size;

        var inner = UiKit.Img("Fill", edge.transform, UiKit.PanelRaise);   // charcoal plate, set off from the window
        UiKit.Fill(inner.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        inner.raycastTarget = false;

        var et = (RectTransform)edge.transform;
        Diamond(et, new Vector2(0.5f, 1), Vector2.zero, 6f, Gold);   // top stud
        Diamond(et, new Vector2(0.5f, 0), Vector2.zero, 6f, Gold);   // bottom stud
        return edge;
    }
}
