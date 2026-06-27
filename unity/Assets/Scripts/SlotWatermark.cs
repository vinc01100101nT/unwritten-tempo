using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Final Watermark Set v1 — the empty equip-slot silhouettes, drawn in the SAME procedural ink+gold
/// line-art vocabulary as <see cref="Ornament"/> and <see cref="StatGlyph"/> (thin gold bars + rotated
/// "diamond" studs). When a paperdoll slot is empty it shows a faint gold emblem of the gear that goes
/// there, so the Equipment window reads as labelled slots — not black holes — and in ONE consistent
/// hand-authored language instead of borrowed sprite art from a different pack.
///
/// Direction (GPT⇄Claude ROI order, after Stat Family Icons): "Final Watermark Set" before any
/// hand-drawn ornament. Guardrails inherited from the kit:
///   • monochrome GOLD, kept FAINT (~0.22a) — a watermark, never reads as a real equipped item,
///   • ~2px line-art (survives 720p / small windows / Steam Deck),
///   • emblem over illustration — each silhouette just needs to differ and hint its slot,
///   • sprite-replaceable: if <see cref="ItemSlot.emptyPlaceholder"/> carries a real sprite it WINS and
///     this procedural mark hides (same drop-in pattern as StatGlyph.art / Ornament.cornerArt).
///
/// Distinct silhouettes: Head ⛑ helm · Armor cuirass · Garment cloak (narrow-top/wide-hem) ·
/// Weapon sword · Shield heater · Accessory ring+gem. Decorative: raycast off, hidden when filled.
/// </summary>
public static class SlotWatermark
{
    static Color Gold => UiKit.Gold;
    // Watermark opacity (GPT review: land ~12–15%, weapon a touch lower — must drop from conscious
    // perception the instant an item is equipped). Set per-build in Build() before the shape is drawn.
    static float curAlpha = 0.14f;
    static Color Ink => new Color(Gold.r, Gold.g, Gold.b, curAlpha);   // faint gold — a watermark, not an icon

    /// <summary>Build the silhouette for <paramref name="type"/> centred in <paramref name="slot"/>'s
    /// icon area, assign it to <see cref="ItemSlot.emptyWatermark"/>, and return the holder. The slot's
    /// Refresh toggles it (shown only while empty AND no override sprite is set).</summary>
    public static GameObject Build(ItemSlot slot, EquipSlotType type, float box = 36f)
    {
        var parent = slot.iconImage != null ? slot.iconImage.transform.parent : slot.transform;
        var holder = UiKit.Img("Watermark_" + type, parent, new Color(0, 0, 0, 0));
        holder.raycastTarget = false;
        var rt = holder.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(box, box);

        // The sword's long, mostly-straight strokes read heavier than the other emblems — dim it a touch more.
        curAlpha = type == EquipSlotType.Weapon ? 0.11f : 0.14f;

        switch (type)
        {
            case EquipSlotType.Head:        Head(rt);   break;
            case EquipSlotType.Armor:       Armor(rt);  break;
            case EquipSlotType.Garment:     Cloak(rt);  break;
            case EquipSlotType.Weapon:      Sword(rt);  break;
            case EquipSlotType.Shield:      Shield(rt); break;
            case EquipSlotType.Accessory1:
            case EquipSlotType.Accessory2:  Ring(rt);   break;
            default:                        Ring(rt);   break;
        }

        slot.emptyWatermark = holder.gameObject;
        return holder.gameObject;
    }

    // ---- silhouettes (box-local, origin centre, ~ -16..+16 for a 36px box) ----

    // a closed great-helm: a fuller dome, a wide brow, eye-slits, cheeks and a chin bar (not an open arch).
    static void Head(RectTransform p)
    {
        Bar(p,  0f, 13f, 11f, 2.2f);           // wide cap
        Bar(p, -7f, 8f, 2.2f, 11f, -38f);      // dome left  "/" (gentle, wide stance)
        Bar(p,  7f, 8f, 2.2f, 11f,  38f);      // dome right "\"
        Bar(p,  0f, 2f, 17f, 2.2f);            // brow — the widest line
        Bar(p, -8.5f, -5f, 2.2f, 12f);         // left cheek
        Bar(p,  8.5f, -5f, 2.2f, 12f);         // right cheek
        Bar(p, -4.5f, -3f, 5f, 2.0f);          // left eye-slit
        Bar(p,  4.5f, -3f, 5f, 2.0f);          // right eye-slit
        Bar(p,  0f, -11f, 12f, 2.2f);          // chin (closes the helm)
    }

    // a breastplate: pauldron flares, a collar notch, straight sides, a centre seam, V-tapered to a point.
    static void Armor(RectTransform p)
    {
        Bar(p, -9f, 11f, 2.2f, 7f,  35f);      // left pauldron flare  "/"
        Bar(p,  9f, 11f, 2.2f, 7f, -35f);      // right pauldron flare "\"
        Bar(p, -5f, 12f, 7f, 2.2f);            // collar left  (centre notch between)
        Bar(p,  5f, 12f, 7f, 2.2f);            // collar right
        Bar(p, -8.5f, 3f, 2.2f, 15f);          // left side
        Bar(p,  8.5f, 3f, 2.2f, 15f);          // right side
        Bar(p,  0f, 3f, 1.8f, 13f);            // centre seam
        Bar(p, -4.5f, -9f, 2.2f, 11f, -38f);   // taper left  "\"
        Bar(p,  4.5f, -9f, 2.2f, 11f,  38f);   // taper right "/" (meet at a point)
    }

    // a hanging cloak: narrow collar, A-line sides, wide hem (the inverse taper of Armor).
    static void Cloak(RectTransform p)
    {
        Bar(p,  0f, 13f, 9f, 2.2f);            // collar
        Bar(p, -7.5f, 0f, 2.2f, 22f, -16f);    // left fall  "\"
        Bar(p,  7.5f, 0f, 2.2f, 22f,  16f);    // right fall "/"
        Bar(p,  0f, -12f, 20f, 2.2f);          // hem
        Bar(p,  0f, 1f, 1.6f, 18f);            // centre fold
    }

    // a sword, point up: long blade + crossguard + pommel diamond.
    static void Sword(RectTransform p)
    {
        Bar(p, 0f, 3f, 2.2f, 22f);             // blade
        Bar(p, 0f, -7f, 15f, 2.2f);            // crossguard
        Diamond(p, 0f, -12.5f, 4.5f);          // pommel
    }

    // a heater shield: flat top, straight sides, converging point.
    static void Shield(RectTransform p)
    {
        Bar(p,  0f, 11f, 18f, 2.2f);           // top
        Bar(p, -9f, 3f, 2.2f, 15f);            // left
        Bar(p,  9f, 3f, 2.2f, 15f);            // right
        Bar(p, -5f, -7f, 2.2f, 12f, -32f);     // lower-left  "\"
        Bar(p,  5f, -7f, 2.2f, 12f,  32f);     // lower-right "/"
    }

    // a ring with a gemstone: a diamond band + a stone on top.
    static void Ring(RectTransform p)
    {
        Bar(p,  4.5f,  1.5f, 1.9f, 13f,  45f);  // band top-right
        Bar(p,  4.5f, -7.5f, 1.9f, 13f, -45f);  // band bottom-right
        Bar(p, -4.5f, -7.5f, 1.9f, 13f,  45f);  // band bottom-left
        Bar(p, -4.5f,  1.5f, 1.9f, 13f, -45f);  // band top-left
        Diamond(p, 0f, 10f, 7f);                // gemstone
    }

    // ---- primitives (mirror StatGlyph, faint-gold ink) ----

    static Image Bar(RectTransform p, float x, float y, float w, float h, float angle = 0f)
    {
        var img = UiKit.Img("w", p, Ink);
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
        if (angle != 0f) rt.localRotation = Quaternion.Euler(0, 0, angle);
        return img;
    }

    static Image Diamond(RectTransform p, float x, float y, float size) => Bar(p, x, y, size, size, 45f);
}
