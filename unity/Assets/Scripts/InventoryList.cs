using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The bag rendered as TEXT ROWS rather than a grid of icons — a name, a tier and how many are held, in columns you
/// can sort and a field you can search.
/// It replaces the 40-square grid inside the Inventory window and owns nothing else: the bag, the tooltip and
/// every action still belong to <see cref="InventoryStore"/>, <see cref="ItemTooltip"/> and <see cref="ItemSlot"/>.
///
/// <para><b>Why a row carries an <see cref="ItemSlot"/>.</b> Hovering, the equip→stat-delta preview,
/// clicking, double-clicking and BOTH drag behaviours (reorder within the bag, drop onto the ground) are
/// implemented on that component and on <see cref="SlotDragHandler"/>, driven entirely by the bound
/// <see cref="BagEntry"/>. So each row gets one, and none of those behaviours is written twice. The drag's
/// floating ghost carries no icon.</para>
///
/// <para><b>Sorting is a LENS, never the order.</b> A column re-orders what you SEE; the bag's own order is the
/// player's, set by dragging, and is what you get back when no column is active. Clicking a column cycles
/// ascending → descending → off, and "off" is the stored order. Two owners of one order is a bug, so there is
/// only ever one: the store.</para>
/// </summary>
public class InventoryList
{
    // ---- the columns, and what each one sorts by ----
    public enum SortKey { Stored, Name, Held }

    struct Column
    {
        public SortKey key;
        public string label;
        public float width;      // 0 = the flexible name column
        public float textSize;   // the row cell's type size — the name reads larger than the figures beside it
        public TextAlignmentOptions align;
    }

    // Widths are fixed so the digits line up down the page; the name takes what is left. The project has no
    // monospaced font, so alignment comes from these and from TMP's own tabular figures, never from the glyphs.
    // Every number here is the one the approved mock of this window measures at, so the two cannot drift:
    // tools/check-ui-prototypes.py reads both and reports any that disagree.
    static readonly Column[] Columns =
    {
        new Column { key = SortKey.Name,   label = "Name",    width = 0f,   textSize = 12f, align = TextAlignmentOptions.Left },
        new Column { key = SortKey.Held,   label = "Held",    width = 34f,  textSize = 10f, align = TextAlignmentOptions.Right },
    };

    // The name is the whole point of a text inventory, so it gets a FLOOR it can never be squeezed below. Without
    // one, every fixed column takes its width first and the name is left with whatever remains — which was ~55px,
    // narrow enough that a name wrapped and then clipped to nothing inside a 22px row.
    const float NameMinWidth = 140f;

    const int RowPool = 40;          // the bag's own capacity — the same fixed pool the grid used, no per-refresh alloc
    const float HeaderTextSize = 10f;
    const float SearchHeight = 22f;
    const float HeaderHeight = 18f;
    const float RowHeight = 20f;
    const float ListHeight = 160f;   // fixed, so the window is one size on every tab and after every drop

    class Row
    {
        public GameObject go;
        public Image bg;
        public ItemSlot slot;
        public TMP_Text[] cells;
        public BagEntry entry;
        public GuideMarker guide;
    }

    readonly List<Row> rows = new List<Row>(RowPool);
    readonly List<BagEntry> view = new List<BagEntry>(RowPool);
    readonly List<TMP_Text> headerLabels = new List<TMP_Text>(Columns.Length);

    RectTransform content;           // the scroll view's content, holding the rows
    TMP_InputField search;
    TMP_Text hits;
    ItemActionMenu menu;

    SortKey sortKey = SortKey.Stored;
    bool sortDescending;
    string query = "";
    Action onChanged;                // the window's own refresh, raised after an action mutates the bag

    // The same blue mark the approved design uses, on a name that is otherwise drawn plain.
    static readonly string WornTag = "   <size=10><color=" + UiKit.Hex(UiKit.Accent) + ">worn</color></size>";

    static readonly Color HeaderIdle = UiKit.TextDim;

    /// <summary>Build the search field, the sortable header and the scrolling row list into
    /// <paramref name="parent"/>. <paramref name="refresh"/> is raised after any action that changes the bag,
    /// so the window that owns this can re-render its footer too.</summary>
    public void Build(Transform parent, Action refresh)
    {
        onChanged = refresh;

        var col = new GameObject("TextList", typeof(RectTransform)).GetComponent<RectTransform>();
        col.SetParent(parent, false);
        UiKit.Fill(col, new Vector2(0, 22), new Vector2(0, -24));
        var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 0f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        BuildSearch(col);
        BuildHeader(col);
        BuildScroller(col);

        menu = new ItemActionMenu();
        menu.Build(parent.GetComponentInParent<Canvas>());
    }

    void BuildSearch(Transform parent)
    {
        var bar = UiKit.Field(UiKit.Img("Search", parent, UiKit.SlotFill));
        var le = bar.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = SearchHeight;
        var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(6, 6, 2, 2);
        h.spacing = 6f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        var label = UiKit.MakeText("Label", bar.transform, "FIND", 10, FontStyle.Normal,
                                   TextAnchor.MiddleLeft, null, false);
        label.color = UiKit.TextDim;
        var ll = label.gameObject.AddComponent<LayoutElement>();
        ll.minWidth = ll.preferredWidth = 26f;

        // The field itself: TMP's input, the same kind chat and the sign-in screen use.
        var fieldGo = UiKit.Img("Field", bar.transform, new Color(0, 0, 0, 0));
        var fl = fieldGo.gameObject.AddComponent<LayoutElement>();
        fl.flexibleWidth = 1f;
        var text = UiKit.MakeText("Text", fieldGo.transform, "", 12, FontStyle.Normal,
                                  TextAnchor.MiddleLeft, null, false);
        UiKit.Fill(text.rectTransform, new Vector2(2, 0), new Vector2(-2, 0));
        // Short enough to sit on ONE line in the width it really gets — the long version wrapped and spilled
        // out of the 26px bar. And wrap:false so it can never wrap even if the window narrows.
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
            // Case-insensitive: both sides are lowered before they meet.
            query = (s ?? "").Trim().ToLowerInvariant();
            Refresh();
        });

        hits = UiKit.MakeText("Hits", bar.transform, "", 10, FontStyle.Normal,
                              TextAnchor.MiddleRight, null, false);
        hits.color = UiKit.TextDim;
        var hl2 = hits.gameObject.AddComponent<LayoutElement>();
        hl2.minWidth = hl2.preferredWidth = 40f;
    }

    void BuildHeader(Transform parent)
    {
        var head = UiKit.Img("Header", parent, UiKit.PanelRaise);
        var le = head.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = HeaderHeight;
        var h = head.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 0, 0);
        h.spacing = 8f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandHeight = true;
        // OFF, so a fixed column is exactly the width it declares. Left on, the leftover room is
        // shared out between every column and each one lands a couple of pixels over.
        h.childForceExpandWidth = false;

        for (int i = 0; i < Columns.Length; i++)
        {
            var c = Columns[i];

            // Same shape as a row cell: one transparent Image carrying one text and one LayoutElement. Identical
            // structure is the only thing that guarantees a header sits exactly over the column it names.
            var cellImg = UiKit.Img(c.label, head.transform, new Color(0, 0, 0, 0));
            var bl = cellImg.gameObject.AddComponent<LayoutElement>();
            if (c.width > 0f) { bl.minWidth = bl.preferredWidth = c.width; bl.flexibleWidth = 0f; }
            else { bl.minWidth = bl.preferredWidth = NameMinWidth; bl.flexibleWidth = 1f; }

            var t = UiKit.MakeText("Text", cellImg.transform, c.label, HeaderTextSize, FontStyle.Normal,
                                   TextAnchor.MiddleLeft, null, false);
            UiKit.Fill(t.rectTransform, Vector2.zero, Vector2.zero);
            t.alignment = c.align;
            t.color = HeaderIdle;
            t.raycastTarget = false;
            headerLabels.Add(t);

            var btn = cellImg.gameObject.AddComponent<Button>();
            btn.targetGraphic = cellImg;
            var colors = btn.colors;
            colors.normalColor = new Color(1, 1, 1, 0);
            colors.highlightedColor = UiKit.Hover;
            colors.pressedColor = UiKit.Rule;
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = colors.normalColor;
            btn.colors = colors;

            var key = c.key;
            btn.onClick.AddListener(() => CycleSort(key));
        }
    }

    void BuildScroller(Transform parent)
    {
        var viewport = UiKit.Img("Viewport", parent, new Color(0, 0, 0, 0));
        var le = viewport.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = ListHeight;
        viewport.gameObject.AddComponent<RectMask2D>();

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = RowHeight;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = viewport.rectTransform;

        content = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(viewport.transform, false);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f);
        // A fresh RectTransform carries sizeDelta (100, 100). Stretched left-to-right, that x of 100 makes every
        // row 100px WIDER than the viewport and — pivoted at the centre — hangs 50px of each row off the LEFT
        // edge, where the mask cuts it. The name is the first column, so the cut landed on the names: short ones
        // (Bow, Sword) vanished outright, long ones lost their opening word ("Grilled Fish" read as "Fish").
        // Same line every other scroller in the HUD carries; this one was missing it.
        content.offsetMin = content.offsetMax = Vector2.zero;
        var cv = content.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 0f;
        cv.childControlWidth = cv.childControlHeight = true;
        cv.childForceExpandWidth = true;
        cv.childForceExpandHeight = false;
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;

        for (int i = 0; i < RowPool; i++) rows.Add(BuildRow(content));
    }

    Row BuildRow(Transform parent)
    {
        var r = new Row();
        var bg = UiKit.Img("Row", parent, new Color(0, 0, 0, 0));
        r.go = bg.gameObject;
        r.bg = bg;

        var le = bg.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = RowHeight;

        var h = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 0, 0);
        h.spacing = 8f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandHeight = true;
        h.childForceExpandWidth = false;

        // The row carries the slot and drag components — that is what gives it hover, the stat preview,
        // clicking and both drags without reimplementing any of them.
        r.slot = bg.gameObject.AddComponent<ItemSlot>();
        r.slot.previewOnHover = true;
        bg.gameObject.AddComponent<SlotDragHandler>();

        r.cells = new TMP_Text[Columns.Length];
        for (int i = 0; i < Columns.Length; i++)
        {
            var c = Columns[i];
            // wrap:false is the REAL switch — MakeText sets textWrappingMode from it. Assigning the obsolete
            // enableWordWrapping afterwards changes nothing, and a wrapped name becomes two lines inside a 22px
            // row, which shows as a fragment.
            var t = UiKit.MakeText(c.label, bg.transform, "", c.textSize, FontStyle.Normal,
                                   TextAnchor.MiddleLeft, null, false);
            t.alignment = c.align;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            var tl = t.gameObject.AddComponent<LayoutElement>();
            if (c.width > 0f) { tl.minWidth = tl.preferredWidth = c.width; tl.flexibleWidth = 0f; }
            else { tl.minWidth = tl.preferredWidth = NameMinWidth; tl.flexibleWidth = 1f; }
            r.cells[i] = t;
        }
        return r;
    }

    // ---- sorting: a lens over the store's order, never a second owner of it ----

    void CycleSort(SortKey key)
    {
        if (sortKey != key) { sortKey = key; sortDescending = false; }
        else if (!sortDescending) sortDescending = true;
        else { sortKey = SortKey.Stored; sortDescending = false; }   // back to the player's own order
        Refresh();
    }

    // "B +12", "Raw", or nothing at all for something that was never forged. No column shows this any more — it
    // is what the search box matches on, so typing "raw" still finds every unquenched piece.
    static string GradeText(BagEntry e)
    {
        if (e == null || e.instance == null) return "";
        var inst = e.instance;
        return (inst.quality == Quality.Raw ? "Raw" : inst.quality.ToString())
               + (inst.upgrade > 0 ? " +" + inst.upgrade : "");
    }

    int Compare(BagEntry a, BagEntry b)
    {
        int sign = sortDescending ? -1 : 1;
        int c;
        switch (sortKey)
        {
            case SortKey.Name:   c = string.Compare(NameOf(a), NameOf(b), StringComparison.OrdinalIgnoreCase); break;
            case SortKey.Held:   c = a.count.CompareTo(b.count); break;
            default:             return 0;
        }
        // Name breaks every tie, so the order is the same on every refresh.
        if (c != 0) return c * sign;
        return string.Compare(NameOf(a), NameOf(b), StringComparison.OrdinalIgnoreCase);
    }

    // What a row is called: a copy by its full title ("+15 Thousand Petals [C]", "B +12 Sword"), anything else by its item
    // name. The one name the list shows, sorts by and searches.
    static string NameOf(BagEntry e)
    {
        if (e == null || e.def == null) return "";
        return e.instance != null ? ItemTooltip.CopyTitle(e.instance) : e.def.displayName;
    }

    // ---- searching: case-insensitive, and it reaches a piece's grade ----

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

    // ---- rendering ----

    /// <summary>Re-render from the store, honouring the tab filter the window already applied.</summary>
    public void Refresh(List<BagEntry> entries)
    {
        source = entries;
        Refresh();
    }

    List<BagEntry> source;

    void Refresh()
    {
        view.Clear();
        if (source != null)
            for (int i = 0; i < source.Count; i++)
                if (Matches(source[i])) view.Add(source[i]);

        if (sortKey != SortKey.Stored) view.Sort(Compare);

        if (hits != null)
            hits.text = query.Length > 0 && source != null ? view.Count + " / " + source.Count : "";

        for (int i = 0; i < Columns.Length; i++)
        {
            var t = headerLabels[i];
            if (t == null) continue;
            bool active = Columns[i].key == sortKey;
            t.color = active ? UiKit.TextMain : HeaderIdle;
            t.text = active ? Columns[i].label + (sortDescending ? " ▼" : " ▲") : Columns[i].label;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (i >= view.Count)
            {
                if (row.go.activeSelf) row.go.SetActive(false);
                row.entry = null;
                row.slot.boundEntry = null;
                row.slot.SetItem(null);
                continue;
            }

            var e = view[i];
            if (!row.go.activeSelf) row.go.SetActive(true);
            row.entry = e;

            // WHAT THE TUTORIAL GUIDE MAY POINT AT: a piece you could put on. A worn row is not one — a double-click on
            // it does nothing — so it carries no spot.
            if (row.guide == null) row.guide = row.go.AddComponent<GuideMarker>();
            row.guide.spot = e.worn || e.def == null ? GuideSpot.None : GuideSpot.BagRow;
            row.guide.itemId = e.def != null ? e.def.id : "";

            // The slot carries the DATA and the interactions; the cells carry the reading.
            if (e.instance != null) row.slot.SetInstance(e.instance);
            else row.slot.SetItem(e.def);
            row.slot.boundEntry = e;
            row.slot.previewSlot = e.def != null ? e.def.slot : EquipSlotType.None;

            var entry = e;
            row.slot.clickOverride = ev => OnRowClick(entry, ev);

            Fill(row, e);
        }
    }

    void Fill(Row row, BagEntry e)
    {
        string name = NameOf(e);
        // What you are wearing is listed with what you carry, so it has to say which it is.
        if (e.worn) name += WornTag;

        // The name in its rarity's colour (the "worn" tag keeps its own).
        Set(row, 0, name, Rarities.Color(Rarities.Of(e.def, e.instance)));
        Set(row, 1, e.count > 1 ? "×" + e.count : (e.count == 1 ? "1" : ""), UiKit.TextDim);
    }

    static void Set(Row row, int i, string text, Color c)
    {
        var t = row.cells[i];
        if (t == null) return;
        if (t.text != text) t.text = text;      // a TMP write rebuilds the mesh; only write on change
        t.color = c;
    }

    // ---- what a click does: the SAME two gestures the grid answered to, plus the menu ----

    void OnRowClick(BagEntry entry, PointerEventData ev)
    {
        if (entry == null || InventoryStore.Instance == null) return;

        if (ev.button == PointerEventData.InputButton.Right)
        {
            menu.Open(entry, ev.position, onChanged);
            return;
        }

        if (ev.clickCount >= 2)
        {
            if (entry.worn) return;              // already on; Unequip lives in the menu
            if (entry.IsEquip) InventoryStore.Instance.Equip(entry);
            else if (entry.def != null && entry.def.IsUsable) InventoryStore.Instance.Consume(entry);
        }
    }

    /// <summary>Once a frame from the window that owns this list, so the row menu can notice a click that
    /// landed somewhere else and take itself down.</summary>
    public void Tick()
    {
        if (menu != null) menu.TickDismiss();
    }

    /// <summary>Close anything floating — called when the window closes so a menu can't outlive it.</summary>
    public void Close()
    {
        if (menu != null) menu.Close();
    }
}
