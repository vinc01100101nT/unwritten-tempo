using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stat Family Icon Set v1 — the six primary-stat glyphs (STR/AGI/VIT/INT/DEX/LUK) drawn in the
/// procedural line-art in the theme's accent colour (thin bars + small rotated-square "diamonds"). They sit in the left gutter of each <see cref="StatPanel"/> row so the
/// six rows gain visual rhythm and the eye can find a stat by its mark, not by reading the label.
///
/// Direction (GPT⇄Claude relay, 2026-06-25): the Character panel was the flattest panel; per the ROI
/// order ("make information more recognizable, not add more decoration"). Guardrails:
///   • one colour only, the theme's accent,
///   • ~1.5–2.5px stroke weight (line-art, must survive 720p / small windows / Steam Deck),
///   • recognition over literalism — each glyph is a distinct EMBLEM, not an illustration,
///   • drop-in sprite hook (<see cref="art"/>) so a hand-drawn icon family can replace any glyph later
///     with zero layout change.
///
/// Distinct silhouettes (the whole point — they must read apart at ~18px):
///   STR ∧ wedge/anvil · AGI » speed chevrons · VIT ✚ cross · INT ◇ gem ring · DEX ↗ aim arrow · LUK ✦ sparkle
/// Everything is decorative: raycastTarget off, anchored to its holder's centre (immune to layout stretch).
/// </summary>
public static class StatGlyph
{
    /// <summary>Optional drop-in art, indexed by <see cref="PrimaryStat"/> (0..5). Non-null entries
    /// render as a tinted sprite instead of the procedural glyph — no layout change. Wire from
    /// UIShellBuilder once a hand-drawn icon family exists.</summary>
    public static readonly Sprite[] art = new Sprite[6];

    static Color Ink     => UiKit.Accent;
    static Color InkSoft => new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.55f);

    /// <summary>Build the glyph for <paramref name="stat"/> into a fixed <paramref name="box"/>-sized,
    /// centre-anchored holder under <paramref name="parent"/>. The holder is transparent and carries a
    /// <see cref="LayoutElement"/> so it behaves as a fixed-width gutter inside a HorizontalLayoutGroup.</summary>
    public static RectTransform Build(Transform parent, PrimaryStat stat, float box = 18f)
    {
        var holder = UiKit.Img("Glyph_" + stat, parent, new Color(0, 0, 0, 0));
        holder.raycastTarget = false;
        var rt = holder.rectTransform;
        rt.sizeDelta = new Vector2(box, box);
        var le = holder.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = box;

        int i = (int)stat;
        if (i >= 0 && i < art.Length && art[i] != null) { Sprite(rt, art[i], box); return rt; }

        switch (stat)
        {
            case PrimaryStat.STR: Str(rt); break;
            case PrimaryStat.AGI: Agi(rt); break;
            case PrimaryStat.VIT: Vit(rt); break;
            case PrimaryStat.INT: Int(rt); break;
            case PrimaryStat.DEX: Dex(rt); break;
            case PrimaryStat.LUK: Luk(rt); break;
        }
        return rt;
    }

    // ---- the six emblems (coords are box-local, origin at centre, ~ -9..+9 for an 18px box) ----

    // STR — an anvil wedge: a strong ∧ peak over a short base bar. Reads as "power / heavy".
    static void Str(RectTransform p)
    {
        Bar(p, -3f, 1.5f, 2.4f, 11f, -45f);   // left arm  "/"
        Bar(p,  3f, 1.5f, 2.4f, 11f,  45f);   // right arm "\"
        Bar(p,  0f, -6f, 12f, 2.4f);          // base
    }

    // AGI — two forward speed-chevrons » (motion lines). Asymmetric offset = momentum, not a static arrow.
    static void Agi(RectTransform p)
    {
        Chevron(p, -2.5f, 1f);
        Chevron(p,  3.5f, 1f, InkSoft);       // trailing chevron faded = motion trail
    }

    // a ">" chevron (peak at right) centred at (cx,cy).
    static void Chevron(RectTransform p, float cx, float cy, Color? c = null)
    {
        Bar(p, cx - 1.5f, cy + 3f, 2.2f, 8f,  45f, c);   // top arm "\"
        Bar(p, cx - 1.5f, cy - 3f, 2.2f, 8f, -45f, c);   // bottom arm "/"
    }

    // VIT — a compact, THICK cross ✚ (health). Short + heavy so it never reads as LUK's thin sparkle.
    static void Vit(RectTransform p)
    {
        Bar(p, 0f, 0f, 2.8f, 12f);   // vertical
        Bar(p, 0f, 0f, 12f, 2.8f);   // horizontal
    }

    // INT — a diamond gem ring ◇ with a centre stud (arcane / mana). Four thin edges + a small diamond.
    static void Int(RectTransform p)
    {
        Bar(p,  3.4f,  3.4f, 1.7f, 10f,  45f);   // top-right edge "\"
        Bar(p,  3.4f, -3.4f, 1.7f, 10f, -45f);   // bottom-right edge "/"
        Bar(p, -3.4f, -3.4f, 1.7f, 10f,  45f);   // bottom-left edge "\"
        Bar(p, -3.4f,  3.4f, 1.7f, 10f, -45f);   // top-left edge "/"
        Diamond(p, 0f, 0f, 3.4f, Ink);          // centre stud
    }

    // DEX — an aim arrow ↗ (precision). A "/" shaft + an L-shaped head at the upper-right tip.
    static void Dex(RectTransform p)
    {
        Bar(p, 0f, 0f, 2.3f, 16f, -45f);    // shaft  "/"
        Bar(p, 3f, 6f, 6.5f, 2.3f);         // head: bar pointing left from the tip
        Bar(p, 6f, 3f, 2.3f, 6.5f);         // head: bar pointing down from the tip
    }

    // LUK — an 8-point sparkle ✦ (fortune / shine). The diagonal rays are full-gold and nearly as long
    // as the orthogonals so the burst reads clearly apart from VIT's bold, compact cross at small sizes.
    static void Luk(RectTransform p)
    {
        Bar(p, 0f, 0f, 1.6f, 15f);            // vertical ray
        Bar(p, 0f, 0f, 15f, 1.6f);            // horizontal ray
        Bar(p, 0f, 0f, 1.5f, 12f,  45f);      // diagonal ray "\"
        Bar(p, 0f, 0f, 1.5f, 12f, -45f);      // diagonal ray "/"
        Diamond(p, 0f, 0f, 5f, Ink);          // bright centre
    }

    // ---- primitives (bars and diamonds, centre-anchored for a self-contained glyph) ----

    static Image Bar(RectTransform p, float x, float y, float w, float h, float angle = 0f, Color? c = null)
    {
        var img = UiKit.Img("g", p, c ?? Ink);
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
        if (angle != 0f) rt.localRotation = Quaternion.Euler(0, 0, angle);
        return img;
    }

    static Image Diamond(RectTransform p, float x, float y, float size, Color c)
        => Bar(p, x, y, size, size, 45f, c);

    static void Sprite(RectTransform holder, Sprite s, float box)
    {
        var img = UiKit.Img("Art", holder, Ink);
        img.sprite = s; img.preserveAspect = true; img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(box, box);
    }
}
