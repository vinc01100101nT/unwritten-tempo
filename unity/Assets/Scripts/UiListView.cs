using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A LIST WINDOW'S COLUMN THAT BUILDS ONLY THE ROWS IN VIEW. It draws the same three kinds of line the list window's
/// pieces draw (<see cref="UiKit.ListLine"/>, <see cref="UiKit.ListButton"/>, <see cref="UiKit.ListDeal"/> — the same
/// sizes, colours and 70-wide presses), but keeps only enough of them to fill the view and re-points them as the list
/// scrolls. A list of 600 stored things costs the twenty-odd rows on screen, not 600 rows made and destroyed on every
/// change.
///
/// <para>The owner says what each line is (<see cref="ISource"/>): how many there are, how tall each is, what one shows
/// when it comes into view, and what a press on it does. Text is made only for the lines on screen.</para>
///
/// <para>It redraws at most once per frame, when its owner calls <see cref="RedrawIfNeeded"/> — after a scroll, or after
/// the owner said the lines changed (<see cref="Changed"/>). Nothing runs on a frame where neither happened.</para>
/// </summary>
public sealed class UiListView
{
    /// <summary>What the lines of a list are. Asked only for the lines on screen, except <see cref="HeightOf"/>, which
    /// is asked for every line when the lines change.</summary>
    public interface ISource
    {
        int Count { get; }
        float HeightOf(int index);
        void Bind(int index, Slot slot);
        void Press(int index, int which);
    }

    /// <summary>What a list of THINGS answers besides — the clicks on one of its item rows (<see cref="Slot.ShowItem"/>):
    /// a double-click, a shift-click. A list that shows no item rows need not be one.</summary>
    public interface IItemSource
    {
        void ItemClicked(int index, UnityEngine.EventSystems.PointerEventData e);
    }

    // The list window's measures (UiKit.ListWindow / ListDeal), so this column and a built one look the same.
    public const float Pad = 6f, Spacing = 5f, RowHeight = 30f, PressWidth = 70f, PressGap = 4f;

    /// <summary>How tall a line of text of <paramref name="size"/> is over <paramref name="rows"/> rows — the same
    /// figure <see cref="UiKit.ListLine"/> gives one.</summary>
    public static float LineHeight(int size, int rows = 1) => (size + 4) * rows + 4;

    /// <summary>One reusable row on screen. The owner fills it in <see cref="ISource.Bind"/> with exactly one of
    /// <see cref="ShowLine"/>, <see cref="ShowButton"/> or <see cref="ShowDeal"/>.</summary>
    public sealed class Slot
    {
        internal RectTransform rect;
        internal TextMeshProUGUI label;
        internal Button wide, first, second;
        internal TextMeshProUGUI wideText, firstText, secondText;
        internal int index = -1;
        internal int boundVersion = -1;
        internal UiListView owner;

        // THE ITEM ROW, made the first time this row shows a thing: the shared horizontal item card (ItemRow). It names
        // the item, shows its picture, carries the item card on right-click and can be dragged, as a box in the bag can.
        ItemRow item;

        /// <summary>A thing: <paramref name="entry"/>'s name in its rarity's colour, <paramref name="count"/> beside it
        /// (empty for none), a tag before the name and one after it (null for none), and <paramref name="price"/> at the
        /// right. Right-clicking shows its card; dragging it hands <paramref name="entry"/> to whatever it is let go over.</summary>
        public void ShowItem(BagEntry entry, string count, string before, Color beforeColour, string after, string price)
        {
            label.gameObject.SetActive(false);
            wide.gameObject.SetActive(false);
            first.gameObject.SetActive(false);
            second.gameObject.SetActive(false);
            if (item == null)
            {
                item = ItemRow.Make(rect, RowHeight, true);
                item.slot.clickOverride = e => { if (index >= 0 && owner != null) owner.ItemClicked(index, e); };
            }
            item.root.gameObject.SetActive(true);

            var slot = item.slot;
            slot.boundEntry = entry;
            if (entry != null && entry.instance != null) slot.SetInstance(entry.instance);
            else slot.SetItem(entry != null ? entry.def : null);
            var def = entry != null ? entry.def : null;
            var copy = entry != null ? entry.instance : null;
            string name = def == null ? "" : copy != null ? ItemTooltip.CopyTitle(copy) : def.displayName;
            item.Show(def, copy, def != null ? Rarities.Of(def) : Rarity.Common, name, count, before, beforeColour,
                      after, price);
        }

        // A text write rebuilds its mesh; written only when the words change.
        static void Write(TextMeshProUGUI t, string s)
        {
            if (t.text != s) t.text = s;
        }

        /// <summary>What the tutorial guide may light on this row's item, set every time the row is filled — a reused row
        /// never keeps the mark of the thing it showed before. None clears it.</summary>
        public void MarkForGuide(GuideSpot spot, string itemId)
        {
            if (item == null) return;
            if (spot != GuideSpot.None) { GuideMarker.Put(item.root.gameObject, spot, itemId); return; }
            var marker = item.root.GetComponent<GuideMarker>();
            if (marker != null) marker.spot = GuideSpot.None;
        }

        void HideItem()
        {
            if (item != null && item.root.gameObject.activeSelf) item.root.gameObject.SetActive(false);
        }

        /// <summary>A line of text, <paramref name="rows"/> lines tall.</summary>
        public void ShowLine(string text, int size, Color color, int rows = 1)
        {
            HideItem();
            wide.gameObject.SetActive(false);
            first.gameObject.SetActive(false);
            second.gameObject.SetActive(false);
            label.gameObject.SetActive(true);
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = UiKit.TmpAlign(rows > 1 ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            label.rectTransform.offsetMax = Vector2.zero;
        }

        /// <summary>A full-width press; its words drawn <paramref name="dim"/> when the owner says so.</summary>
        public void ShowButton(string text, bool dim = false)
        {
            HideItem();
            label.gameObject.SetActive(false);
            first.gameObject.SetActive(false);
            second.gameObject.SetActive(false);
            wide.gameObject.SetActive(true);
            wideText.text = text;
            wideText.color = dim ? UiKit.TextDim : UiKit.ButtonText;
        }

        /// <summary>A deal: what it is on the left, one press (or two, when <paramref name="secondWord"/> is given) on
        /// the right.</summary>
        public void ShowDeal(string text, string firstWord, string secondWord = null)
        {
            HideItem();
            wide.gameObject.SetActive(false);
            label.gameObject.SetActive(true);
            label.text = text;
            label.fontSize = 13;
            label.color = UiKit.TextMain;
            label.alignment = UiKit.TmpAlign(TextAnchor.MiddleLeft);
            first.gameObject.SetActive(true);
            firstText.text = firstWord;
            bool two = secondWord != null;
            second.gameObject.SetActive(two);
            if (two) secondText.text = secondWord;
            // The first press sits left of the second, so with only one it takes the second's place at the edge.
            var fr = (RectTransform)first.transform;
            fr.anchoredPosition = new Vector2(two ? -(PressWidth + PressGap) : 0f, 0f);
            label.rectTransform.offsetMax = new Vector2(-(two ? 2 : 1) * (PressWidth + PressGap), 0f);
        }
    }

    readonly ISource source;
    readonly RectTransform viewport;
    readonly RectTransform content;
    readonly List<Slot> slots = new List<Slot>();
    float[] tops = new float[64];
    int counted;
    float total;
    int version;
    bool linesChanged = true;
    bool dirty = true;

    /// <summary>Turn <paramref name="viewport"/> — a window's body (<see cref="UiKit.Window"/>) — into a scrolling column
    /// whose lines are <paramref name="source"/>'s.</summary>
    public UiListView(RectTransform viewport, ISource source)
    {
        this.viewport = viewport;
        this.source = source;
        // THE SAME SCROLL COLUMN every list window scrolls with — minus the fitter that sizes it from laid-out children,
        // because here the height is worked out from the lines and set directly.
        content = UiKit.ScrollColumn(viewport);
        var fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null) { fitter.enabled = false; UnityEngine.Object.Destroy(fitter); }
        var scroll = viewport.GetComponent<ScrollRect>();
        if (scroll != null) scroll.onValueChanged.AddListener(_ => dirty = true);
    }

    /// <summary>The lines changed — how many, how tall, or what they say. Redrawn at the next <see cref="RedrawIfNeeded"/>.</summary>
    public void Changed()
    {
        linesChanged = true;
        dirty = true;
        version++;
    }

    /// <summary>Back to the top of the list — a new page.</summary>
    public void ToTop()
    {
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
        dirty = true;
    }

    /// <summary>Redraw what is in view, if a scroll or a change happened since the last redraw. Called once a frame by the
    /// owner; costs one test when nothing happened.</summary>
    public void RedrawIfNeeded()
    {
        if (!dirty) return;
        dirty = false;

        if (linesChanged)
        {
            linesChanged = false;
            counted = Mathf.Max(0, source.Count);
            if (tops.Length < counted) tops = new float[Mathf.NextPowerOfTwo(counted)];
            float y = Pad;
            for (int i = 0; i < counted; i++)
            {
                tops[i] = y;
                y += source.HeightOf(i) + Spacing;
            }
            total = counted > 0 ? y - Spacing + Pad : 0f;
            content.sizeDelta = new Vector2(content.sizeDelta.x, total);
        }

        float viewTop = Mathf.Max(0f, content.anchoredPosition.y);
        float viewBottom = viewTop + viewport.rect.height;
        int first = FirstAtOrBelow(viewTop);
        int used = 0;
        for (int i = first; i < counted && tops[i] < viewBottom; i++)
        {
            if (used == slots.Count) slots.Add(MakeSlot());
            var slot = slots[used++];
            if (slot.index != i || slot.boundVersion != version)
            {
                slot.index = i;
                slot.boundVersion = version;
                float h = source.HeightOf(i);
                slot.rect.anchoredPosition = new Vector2(0f, -tops[i]);
                slot.rect.sizeDelta = new Vector2(-2f * Pad, h);
                source.Bind(i, slot);
            }
            if (!slot.rect.gameObject.activeSelf) slot.rect.gameObject.SetActive(true);
        }
        for (int s = used; s < slots.Count; s++)
        {
            if (slots[s].rect.gameObject.activeSelf) slots[s].rect.gameObject.SetActive(false);
            slots[s].index = -1;
        }
    }

    // The first line whose bottom is at or below `y` — a binary search over where each line starts.
    int FirstAtOrBelow(float y)
    {
        int lo = 0, hi = counted - 1, found = counted;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (tops[mid] + source.HeightOf(mid) >= y) { found = mid; hi = mid - 1; }
            else lo = mid + 1;
        }
        return found;
    }

    Slot MakeSlot()
    {
        var s = new Slot { owner = this };
        var go = new GameObject("Line", typeof(RectTransform));
        s.rect = (RectTransform)go.transform;
        s.rect.SetParent(content, false);
        s.rect.anchorMin = new Vector2(0f, 1f);
        s.rect.anchorMax = new Vector2(1f, 1f);
        s.rect.pivot = new Vector2(0.5f, 1f);

        s.label = UiKit.Label("What", s.rect, "", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        UiKit.Fill(s.label.rectTransform, Vector2.zero, Vector2.zero);

        s.wide = UiKit.Btn("", s.rect, () => Pressed(s, 0));
        UiKit.Fill((RectTransform)s.wide.transform, Vector2.zero, Vector2.zero);
        s.wideText = s.wide.GetComponentInChildren<TextMeshProUGUI>(true);

        s.second = Press(s.rect, () => Pressed(s, 1), 0f, out s.secondText);
        s.first = Press(s.rect, () => Pressed(s, 0), -(PressWidth + PressGap), out s.firstText);
        return s;
    }

    // One 70-wide press at the row's right edge, `fromRight` in from it.
    static Button Press(RectTransform row, Action onClick, float fromRight, out TextMeshProUGUI text)
    {
        var b = UiKit.Btn("", row, onClick);
        var r = (RectTransform)b.transform;
        r.anchorMin = new Vector2(1f, 0f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(1f, 0.5f);
        r.sizeDelta = new Vector2(PressWidth, 0f);
        r.anchoredPosition = new Vector2(fromRight, 0f);
        text = b.GetComponentInChildren<TextMeshProUGUI>(true);
        return b;
    }

    void ItemClicked(int index, UnityEngine.EventSystems.PointerEventData e)
    {
        if (index < counted && source is IItemSource items) items.ItemClicked(index, e);
    }

    void Pressed(Slot s, int which)
    {
        if (s.index >= 0 && s.index < counted) source.Press(s.index, which);
    }
}
