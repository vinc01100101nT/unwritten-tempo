using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The bag drawn as a GRID of the hotbar's own boxes (<see cref="ItemGrid"/>) — each thing by its name, a stack with
/// its count — under a search field and a "Sort by :" row. It owns nothing else: the bag, the tooltip and every action
/// still belong to <see cref="InventoryStore"/>, <see cref="ItemTooltip"/> and <see cref="ItemSlot"/>.
///
/// <para><b>What the pointer does.</b> Double-click: use a consumable, put on a piece of gear, or merge ten of an Ink or
/// a Salt into one of the next tier. Shift-click: put the thing's name into the chat field as a link. Drag a box onto
/// another to swap them, onto an empty box to move it to the end, onto the hotbar to put it there, onto the body in the
/// equipment panel to wear it, or out over the world to put it down. There is no right-click menu.</para>
///
/// <para><b>Sorting is a LENS, never the order.</b> A sort re-orders what you SEE; the bag's own order is the player's —
/// things arrive at the end, and dragging rearranges it — and is what you get back when no sort is chosen. While a
/// sort is on, dragging inside the bag does nothing, because the order on screen is the sort's.</para>
///
/// <para><b>A tab shows its whole limit</b> (<see cref="BagRoom.SlotsPerCategory"/> boxes, the empty ones in the
/// hotbar's empty grey), so how full it is can be seen. All, and any search, show only what is held.</para>
/// </summary>
public class InventoryList : IItemBoxOwner
{
    public enum SortKey { Stored, Name, Held, Rarity }

    static readonly (SortKey key, string word)[] Sorts =
        { (SortKey.Name, "Name"), (SortKey.Held, "Held"), (SortKey.Rarity, "Rarity") };

    const float SearchHeight = 22f;
    const float SortHeight = 20f;
    const float SortTextSize = 10f;

    readonly ItemGrid grid = new ItemGrid();
    readonly List<BagEntry> view = new List<BagEntry>(64);
    readonly List<Button> sortButtons = new List<Button>(Sorts.Length);
    TMP_InputField search;
    TMP_Text hits;

    SortKey sortKey = SortKey.Stored;
    bool sortDescending;
    string query = "";
    List<BagEntry> source;
    bool showWholeTab;

    /// <summary>How tall it stands: the search field, the sort row and the grid.</summary>
    public const float Height = SearchHeight + 3f + SortHeight + 3f + ItemGrid.Height;

    /// <summary>Build the search field, the sort row and the grid into <paramref name="parent"/>, its top-left corner
    /// <paramref name="at"/> from the parent's top-left.</summary>
    public void Build(Transform parent, Vector2 at)
    {
        BuildSearch(parent, at);
        BuildSort(parent, at + new Vector2(0, -(SearchHeight + 3f)));
        grid.Build(parent, at + new Vector2(0, -(SearchHeight + 3f + SortHeight + 3f)), this);
    }

    static RectTransform TopLeft(RectTransform rt, Vector2 at, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = size;
        rt.anchoredPosition = at;
        return rt;
    }

    void BuildSearch(Transform parent, Vector2 at)
    {
        var bar = UiKit.Field(UiKit.Img("Search", parent, UiKit.SlotFill));
        TopLeft(bar.rectTransform, at, new Vector2(ItemGrid.Width, SearchHeight));
        var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(6, 6, 2, 2);
        h.spacing = 6f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        var label = UiKit.MakeText("Label", bar.transform, "FIND", 10, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        label.color = UiKit.TextDim;
        var ll = label.gameObject.AddComponent<LayoutElement>();
        ll.minWidth = ll.preferredWidth = 26f;

        var fieldGo = UiKit.Img("Field", bar.transform, new Color(0, 0, 0, 0));
        fieldGo.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var text = UiKit.MakeText("Text", fieldGo.transform, "", 12, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        UiKit.Fill(text.rectTransform, new Vector2(2, 0), new Vector2(-2, 0));
        var placeholder = UiKit.MakeText("Placeholder", fieldGo.transform, "name, slot, tier, weapon type, or a skill",
                                         11, FontStyle.Italic, TextAnchor.MiddleLeft, null, false);
        UiKit.Fill(placeholder.rectTransform, new Vector2(2, 0), new Vector2(-2, 0));
        placeholder.color = new Color(UiKit.TextDim.r, UiKit.TextDim.g, UiKit.TextDim.b, 0.55f);

        search = fieldGo.gameObject.AddComponent<TMP_InputField>();
        search.textViewport = fieldGo.rectTransform;
        search.textComponent = text;
        search.placeholder = placeholder;
        search.lineType = TMP_InputField.LineType.SingleLine;
        search.customCaretColor = true;
        search.caretColor = UiKit.Accent;
        search.selectionColor = UiKit.Hover;
        search.onValueChanged.AddListener(s =>
        {
            query = (s ?? "").Trim().ToLowerInvariant();
            Refresh();
            grid.ToTop();
        });

        hits = UiKit.MakeText("Hits", bar.transform, "", 10, FontStyle.Normal, TextAnchor.MiddleRight, null, false);
        hits.color = UiKit.TextDim;
        var hl = hits.gameObject.AddComponent<LayoutElement>();
        hl.minWidth = hl.preferredWidth = 40f;
    }

    // "Sort by :" in plain dim words, and the choices beside it as small buttons, the chosen one marked like a tab.
    void BuildSort(Transform parent, Vector2 at)
    {
        var row = UiKit.Img("Sort", parent, new Color(0, 0, 0, 0));
        row.raycastTarget = false;
        TopLeft(row.rectTransform, at, new Vector2(ItemGrid.Width, SortHeight));
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 3f;
        h.padding = new RectOffset(0, 0, 2, 2);
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;

        var label = UiKit.MakeText("Label", row.transform, "Sort by :", SortTextSize, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        label.color = UiKit.TextDim;
        var ll = label.gameObject.AddComponent<LayoutElement>();
        ll.minWidth = ll.preferredWidth = 46f;

        foreach (var (key, word) in Sorts)
        {
            var k = key;
            var btn = UiKit.Btn(word, row.transform, () => CycleSort(k));
            var t = btn.GetComponentInChildren<TMP_Text>();
            t.fontSize = SortTextSize;
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 52f;
            le.minHeight = le.preferredHeight = 16f;
            sortButtons.Add(btn);
        }
    }

    void CycleSort(SortKey key)
    {
        if (sortKey != key) { sortKey = key; sortDescending = false; }
        else if (!sortDescending) sortDescending = true;
        else { sortKey = SortKey.Stored; sortDescending = false; }   // back to the bag's own order
        Refresh();
        grid.ToTop();
    }

    int Compare(BagEntry a, BagEntry b)
    {
        int c;
        switch (sortKey)
        {
            case SortKey.Name:   c = string.Compare(NameOf(a), NameOf(b), StringComparison.OrdinalIgnoreCase); break;
            case SortKey.Held:   c = a.count.CompareTo(b.count); break;
            case SortKey.Rarity: c = ((int)Rarities.Of(a.def, a.instance)).CompareTo((int)Rarities.Of(b.def, b.instance)); break;
            default:             return 0;
        }
        if (sortDescending) c = -c;
        // Name breaks every tie, so the order is the same on every refresh.
        return c != 0 ? c : string.Compare(NameOf(a), NameOf(b), StringComparison.OrdinalIgnoreCase);
    }

    // What a box is called: a piece by its full title ("+15 Thousand Petals [C]"), anything else by its item name. The
    // one name the grid shows, sorts by and searches.
    static string NameOf(BagEntry e)
    {
        if (e == null || e.def == null) return "";
        return e.instance != null ? ItemTooltip.CopyTitle(e.instance) : e.def.displayName;
    }

    // "B +12", "Raw", or nothing for something that is not gear — what the search matches, so "raw" finds unquenched pieces.
    static string GradeText(BagEntry e)
    {
        if (e == null || e.instance == null) return "";
        var inst = e.instance;
        return (inst.quality == Quality.Raw ? "Raw" : inst.quality.ToString()) + (inst.upgrade > 0 ? " +" + inst.upgrade : "");
    }

    bool Matches(BagEntry e)
    {
        if (query.Length == 0) return true;
        if (e == null || e.def == null) return false;
        if (e.def.displayName != null && e.def.displayName.ToLowerInvariant().Contains(query)) return true;
        if (NameOf(e).ToLowerInvariant().Contains(query)) return true;
        if (EquipSlots.Label(e.def.slot).ToLowerInvariant().Contains(query)) return true;
        if (GradeText(e).ToLowerInvariant().Contains(query)) return true;
        if (e.def.weaponType != WeaponType.None && (e.def.weaponType.ToString().ToLowerInvariant().Contains(query)
            || WeaponTypes.Label(e.def.weaponType).ToLowerInvariant().Contains(query))) return true;
        return false;
    }

    /// <summary>Draw <paramref name="entries"/> — the bag, already cut to the tab the window has chosen.
    /// <paramref name="wholeTab"/> is true when that is one tab, whose every box (its limit) is drawn.</summary>
    public void Refresh(List<BagEntry> entries, bool wholeTab)
    {
        source = entries;
        showWholeTab = wholeTab;
        Refresh();
    }

    void Refresh()
    {
        view.Clear();
        if (source != null)
            for (int i = 0; i < source.Count; i++)
                if (Matches(source[i])) view.Add(source[i]);
        if (sortKey != SortKey.Stored) view.Sort(Compare);

        if (hits != null) hits.text = query.Length > 0 && source != null ? view.Count + " / " + source.Count : "";
        for (int i = 0; i < sortButtons.Count; i++)
        {
            bool on = Sorts[i].key == sortKey;
            UiKit.MarkTab(sortButtons[i], on);
            var t = sortButtons[i].GetComponentInChildren<TMP_Text>();
            string word = on ? Sorts[i].word + (sortDescending ? " ▼" : " ▲") : Sorts[i].word;
            if (t != null && t.text != word) t.text = word;
        }
        grid.Refresh();
    }

    // ---- the grid's questions -----------------------------------------------------------------------------------

    public int BoxCount
    {
        get
        {
            int boxes = view.Count;
            if (showWholeTab && query.Length == 0) return Mathf.Max(BagRoom.SlotsPerCategory, boxes);
            int filled = (boxes + ItemGrid.Columns - 1) / ItemGrid.Columns * ItemGrid.Columns;
            return Mathf.Max(ItemGrid.Rows * ItemGrid.Columns, filled);
        }
    }

    public void Bind(int index, ItemBox box)
    {
        if (index < view.Count) box.Show(view[index], ItemBoxSource.Bag);
        else box.ShowEmpty();
        box.SetCorner("", UiKit.TextDim);
    }

    public void Clicked(ItemBox box, PointerEventData e)
    {
        var entry = box.Entry;
        var store = InventoryStore.Instance;
        if (entry == null || entry.def == null || store == null || e.button != PointerEventData.InputButton.Left) return;

        // SHIFT: the thing goes into the chat field as a link.
        if (Hotkeys.ShiftHeld) { ChatController.InsertItemLink(entry); return; }
        if (e.clickCount < 2) return;

        if (entry.IsEquip) store.Equip(entry);
        else if (entry.def.IsUsable) store.Consume(entry);
        else if (CraftingMath.Merges(entry.def.materialKind) && entry.def.mergesInto != null
                 && entry.count >= CraftingMath.MergeCount(entry.def.materialKind, entry.def.materialStep)
                 && CraftingMath.MergeCount(entry.def.materialKind, entry.def.materialStep) > 0) store.Merge(entry);
    }

    public bool Dropped(ItemBox onto, ItemSlot from)
    {
        var store = InventoryStore.Instance;
        var fromBox = from != null ? from.GetComponent<ItemBox>() : null;
        var moving = from != null ? from.boundEntry : null;
        if (store == null || fromBox == null || moving == null) return false;

        // A WORN piece let go over the bag comes off.
        if (fromBox.source == ItemBoxSource.Worn) { store.Unequip(moving.wornSlot); return true; }
        if (fromBox.source != ItemBoxSource.Bag) return false;

        // While a sort is on, the order on screen is the sort's: nothing inside the bag moves.
        if (sortKey != SortKey.Stored) return true;
        var target = onto != null ? onto.Entry : null;
        if (target != null) store.Swap(moving, target);
        else store.Reorder(moving, null);   // an empty box: to the end
        return true;
    }

    /// <summary>Back to the first row — the window opening, or a tab chosen.</summary>
    public void ToTop() => grid.ToTop();
}
