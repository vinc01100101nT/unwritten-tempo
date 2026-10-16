using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE HORIZONTAL ITEM CARD: the list button's look, and inside it the thing's picture on its rarity square at the left,
/// its name (cut with "…" when room runs short — never the price), its count, a small tag on either side, and a price at
/// the right. The shop's rows and the card that pops up when something arrives in the bag (<see cref="LootCards"/>) are
/// both this one card, so the two can never look different.
/// </summary>
public sealed class ItemRow
{
    public RectTransform root;
    /// <summary>The box behaviour — name on hover, card on right-click, drag. Null on a card that only shows.</summary>
    public ItemSlot slot;

    TextMeshProUGUI name, count, price, tagBefore, tagAfter;
    Image back, icon;
    Image glow;
    GameObject tagBeforeBox, tagAfterBox;
    // False: the picture stands on nothing, and only the name carries the rarity's colour.
    bool square;

    /// <summary>Build one filling <paramref name="parent"/>, <paramref name="height"/> tall. An
    /// <paramref name="interactive"/> card is an item box as well (<see cref="slot"/>); one that is not only shows. With
    /// <paramref name="raritySquare"/> the picture sits on a square of its rarity's colour, as in a bag box; without,
    /// it stands on nothing and only the name is coloured.</summary>
    public static ItemRow Make(RectTransform parent, float height, bool interactive, bool raritySquare = true)
    {
        var r = new ItemRow { square = raritySquare };
        var edge = UiKit.Round(UiKit.Img("Item", parent, UiKit.ButtonEdge), UiKit.ButtonRadius);
        r.root = edge.rectTransform;
        UiKit.Fill(r.root, Vector2.zero, Vector2.zero);
        var fill = UiKit.Round(UiKit.Img("Fill", r.root, UiKit.ButtonFill), UiKit.ButtonRadius);
        UiKit.Fill(fill.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        fill.raycastTarget = false;

        var h = fill.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 0, 0);
        h.spacing = 6f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        r.back = UiKit.Round(UiKit.Img("IconBack", fill.transform, UiKit.SlotFill), 3);
        r.back.raycastTarget = false;
        r.back.gameObject.AddComponent<DragFace>();   // dragging this row carries the item's picture, not the row
        var square = r.back.gameObject.AddComponent<LayoutElement>();
        square.minWidth = square.preferredWidth = square.minHeight = square.preferredHeight = height - 6f;
        r.icon = ItemIcon.Make(r.back.transform, 1f);
        r.glow = GradeGlow.MakeForSquare(r.back.transform);
        r.back.gameObject.SetActive(false);
        r.tagBeforeBox = MakeTag(fill.transform, "Before", out r.tagBefore);
        r.name = Part(fill.transform, "Name", 13, FontStyles.Bold);
        r.name.overflowMode = TextOverflowModes.Ellipsis;
        var grow = r.name.gameObject.AddComponent<LayoutElement>();
        grow.flexibleWidth = 1f;
        grow.minWidth = 0f;
        // THE NAME TAKES ONLY WHAT IS LEFT. Asking for its full width made a long name squeeze the count and the price
        // below their own widths, so their words ran into each other; it is cut with "…" instead.
        grow.preferredWidth = 0f;
        r.count = Part(fill.transform, "Count", 12, FontStyles.Normal);
        r.count.color = UiKit.TextDim;
        r.tagAfterBox = MakeTag(fill.transform, "After", out r.tagAfter);
        r.price = Part(fill.transform, "Price", 13, FontStyles.Bold);
        r.price.color = UiKit.TitleText;

        if (interactive)
        {
            edge.gameObject.AddComponent<UiButtonFx>();
            r.slot = edge.gameObject.AddComponent<ItemSlot>();
            r.slot.nameOnHover = false;   // its name is already written beside the picture
            edge.gameObject.AddComponent<SlotDragHandler>();
        }
        else edge.raycastTarget = false;
        return r;
    }

    /// <summary>Show <paramref name="def"/> — <paramref name="copy"/> for a piece of gear — called
    /// <paramref name="title"/>, in <paramref name="rarity"/>'s colour, with <paramref name="countText"/> beside it, a tag
    /// before and after (null for none) and <paramref name="priceText"/> at the right.</summary>
    public void Show(Item def, EquipmentInstance copy, Rarity rarity, string title, string countText,
                     string before, Color beforeColour, string after, string priceText)
    {
        Write(name, title ?? "");
        name.color = Rarities.Color(rarity);
        bool pictured = ItemIcon.Show(icon, def);
        GradeGlow.Show(glow, copy);
        if (back.gameObject.activeSelf != pictured) back.gameObject.SetActive(pictured);
        if (pictured) back.color = !square ? Color.clear : rarity == Rarity.Common ? UiKit.SlotFill : Rarities.Color(rarity);
        Write(count, countText ?? "");
        Write(price, priceText ?? "");
        Tag(tagBeforeBox, tagBefore, before, beforeColour);
        Tag(tagAfterBox, tagAfter, after, UiKit.TextDim);
    }

    static void Tag(GameObject box, TextMeshProUGUI text, string word, Color colour)
    {
        bool on = !string.IsNullOrEmpty(word);
        if (box.activeSelf != on) box.SetActive(on);
        if (!on) return;
        Write(text, word);
        text.color = colour;
    }

    // A text write rebuilds its mesh; written only when the words change.
    static void Write(TextMeshProUGUI t, string s)
    {
        if (t.text != s) t.text = s;
    }

    static TextMeshProUGUI Part(Transform parent, string name, float size, FontStyles style)
    {
        var t = UiKit.MakeText(name, parent, "", size, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        t.fontStyle = style;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    // A small word on a white chip — "Buy back", "2 free", "Buy", "Sell".
    static GameObject MakeTag(Transform parent, string name, out TextMeshProUGUI word)
    {
        var chip = UiKit.Round(UiKit.Img(name, parent, UiKit.SlotFill), 2);
        chip.raycastTarget = false;
        var h = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(4, 4, 1, 1);
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        word = Part(chip.transform, "Word", 10, FontStyles.Bold);
        chip.gameObject.SetActive(false);
        return chip.gameObject;
    }
}
