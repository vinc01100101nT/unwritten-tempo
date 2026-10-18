using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Where a box's thing is: in the bag, worn on the body, or in the Keeper's storage. A drop reads it to know
/// what moving the thing means.</summary>
public enum ItemBoxSource { Bag, Worn, Storage }

/// <summary>Something an item can be let go over: a box, a grid's empty space, the body in the equipment panel. Asked
/// by <see cref="SlotDragHandler"/> when a drag ends; true when it took the drop (anything it does is a request).</summary>
public interface IItemDropTarget
{
    bool TakeDrop(ItemSlot from, PointerEventData e);
}

/// <summary>What fills a grid's boxes and answers what is done to them — the bag, the Keeper's storage, the
/// equipment panel.</summary>
public interface IItemBoxOwner
{
    int BoxCount { get; }
    void Bind(int index, ItemBox box);
    void Clicked(ItemBox box, PointerEventData e);
    /// <summary><paramref name="onto"/> is null when the drop landed on the grid between or beyond its boxes.</summary>
    bool Dropped(ItemBox onto, ItemSlot from);
}

/// <summary>A drag of a thing out of a box, as anything that wants to react to it sees it — the equipment panel lights
/// the slot a piece of gear belongs in for as long as it is held.</summary>
public static class ItemDrag
{
    public static event Action<BagEntry> Started;
    public static event Action Ended;

    /// <summary>The thing being dragged, or null when nothing is.</summary>
    public static BagEntry Current { get; private set; }

    public static void Begin(BagEntry what)
    {
        Current = what;
        Started?.Invoke(what);
    }

    public static void End()
    {
        if (Current == null) return;
        Current = null;
        Ended?.Invoke();
    }
}

/// <summary>
/// ONE BOX: the hotbar's own square (48 by 48, rounded, white with a thin edge), drawing a thing by its name — the name
/// in its rarity's colour, as large as fits and cut with "…", a stack's count at the bottom right. An empty box is the
/// hotbar's empty grey. It carries an <see cref="ItemSlot"/> and a <see cref="SlotDragHandler"/>, so hovering shows the
/// item's tooltip and the box can be dragged — the same two components every bag row carried.
/// </summary>
/// <summary>THE ONE WAY a thing's picture is put on screen in a window — a bag box and a shop row alike: its icon on a
/// square filled in its rarity's colour (the window's plain slot white for Common).</summary>
public static class ItemIcon
{
    /// <summary>The square behind <paramref name="def"/>'s icon.</summary>
    public static ThemeColor Back(Item def, EquipmentInstance copy)
    {
        var r = Rarities.Of(def);
        return r == Rarity.Common ? UiKit.SlotFill : Rarities.Color(r);
    }

    /// <summary>An icon image filling <paramref name="parent"/> less <paramref name="inset"/> on every side.</summary>
    public static Image Make(Transform parent, float inset)
    {
        var img = UiKit.Img("Icon", parent, Color.white);
        img.raycastTarget = false;
        img.preserveAspect = true;
        UiKit.Fill(img.rectTransform, new Vector2(inset, inset), new Vector2(-inset, -inset));
        img.enabled = false;
        return img;
    }

    /// <summary>Show <paramref name="def"/>'s icon, or nothing. False when it has none, so the caller writes its name.</summary>
    public static bool Show(Image icon, Item def)
    {
        var s = def != null ? def.icon : null;
        if (icon.sprite != s) icon.sprite = s;
        bool on = s != null;
        if (icon.enabled != on) icon.enabled = on;
        return on;
    }
}

public class ItemBox : MonoBehaviour, IItemDropTarget
{
    public const float Size = 48f;
    // The hotbar's own tile text: as large as fits between these, then cut (HotbarPanel.NameOnTile).
    const float NameMinSize = 9f, NameMaxSize = 12f;

    public Image edge, fill, icon;
    public RawImage glow;
    public TMP_Text title, count, corner;
    // The fill a shown thing's rarity gave the box, put back when a drag stops lighting it.
    ThemeColor shownFill = UiKit.SlotFill;
    public ItemSlot slot;
    public GuideMarker guide;
    public IItemBoxOwner owner;
    public ItemBoxSource source;
    /// <summary>Which of its owner's things it shows; -1 when none.</summary>
    public int index = -1;

    public BagEntry Entry => slot != null ? slot.boundEntry : null;

    public static ItemBox Make(Transform parent, string goName, IItemBoxOwner owner)
    {
        var edgeImg = UiKit.Round(UiKit.Img(goName, parent, UiKit.ButtonEdge), UiKit.ButtonRadius);
        var rt = edgeImg.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(Size, Size);

        var fillImg = UiKit.Round(UiKit.Img("Fill", edgeImg.transform, UiKit.SlotFill), UiKit.ButtonRadius);
        UiKit.Fill(fillImg.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        fillImg.raycastTarget = false;

        var box = edgeImg.gameObject.AddComponent<ItemBox>();
        box.owner = owner;
        box.edge = edgeImg;
        box.fill = fillImg;
        box.icon = ItemIcon.Make(fillImg.transform, 3f);
        box.glow = GradeGlow.MakeForSquare(fillImg.transform, box.icon.transform);

        var t = UiKit.MakeText("Name", fillImg.transform, "", NameMaxSize, FontStyle.Bold, TextAnchor.MiddleCenter, null, true);
        t.raycastTarget = false;
        t.richText = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = NameMinSize;
        t.fontSizeMax = NameMaxSize;
        t.maxVisibleLines = 2;
        t.overflowMode = TextOverflowModes.Ellipsis;
        UiKit.Fill(t.rectTransform, new Vector2(2, 11), new Vector2(-2, -11));
        box.title = t;

        var n = UiKit.MakeText("Count", fillImg.transform, "", 11, FontStyle.Bold, TextAnchor.LowerRight, null, false);
        n.raycastTarget = false;
        UiKit.StyleItemCount(n);
        UiKit.Fill(n.rectTransform, new Vector2(3, 1), new Vector2(-3, -1));
        box.count = n;

        var k = UiKit.MakeText("Corner", fillImg.transform, "", 9, FontStyle.Normal, TextAnchor.UpperCenter, null, false);
        k.raycastTarget = false;
        UiKit.Fill(k.rectTransform, new Vector2(2, 1), new Vector2(-2, -2));
        box.corner = k;

        box.slot = edgeImg.gameObject.AddComponent<ItemSlot>();
        box.slot.previewOnHover = true;
        box.slot.clickOverride = e => { if (box.owner != null) box.owner.Clicked(box, e); };
        edgeImg.gameObject.AddComponent<SlotDragHandler>();
        box.guide = edgeImg.gameObject.AddComponent<GuideMarker>();
        box.ShowEmpty();
        return box;
    }

    /// <summary>Show <paramref name="e"/>, which is in <paramref name="from"/>. Null or nothing = an empty box.</summary>
    public void Show(BagEntry e, ItemBoxSource from)
    {
        source = from;
        if (e == null || e.def == null) { ShowEmpty(); return; }
        slot.boundEntry = e;
        if (e.instance != null) slot.SetInstance(e.instance);
        else slot.SetItem(e.def);
        // In the bag a piece's hover compares it with what is worn; worn or stored, it is just itself.
        slot.previewOnHover = from == ItemBoxSource.Bag;
        slot.previewSlot = e.def.slot;
        UiKit.Paint(edge, UiKit.ButtonEdge);
        shownFill = ItemIcon.Back(e.def, e.instance);
        UiKit.Paint(fill, shownFill);
        // The picture is the box; the name is in the card on hover. A thing with no picture still writes its name.
        bool pictured = ItemIcon.Show(icon, e.def);
        GradeGlow.Show(glow, e.instance);
        Write(title, pictured ? "" : e.instance != null ? ItemTooltip.CopyTitle(e.instance) : e.def.displayName);
        UiKit.Paint(title, Rarities.Color(Rarities.Of(e.def)));
        // A stack shows how many, always — as the hotbar does; a piece of gear is one of itself.
        Write(count, e.IsEquip ? "" : e.count.ToString());
        // WHAT THE TUTORIAL GUIDE MAY POINT AT: a thing in the bag. A worn or stored one, or an empty box, is not one.
        guide.spot = from == ItemBoxSource.Bag ? GuideSpot.BagRow : GuideSpot.None;
        guide.itemId = from == ItemBoxSource.Bag ? e.def.id : "";
    }

    public void ShowEmpty()
    {
        slot.boundEntry = null;
        slot.SetItem(null);
        edge.color = new Color(0, 0, 0, 0);   // still caught by the pointer, so a drop on an empty box lands
        UiKit.Paint(fill, UiKit.SlotEmpty);
        shownFill = UiKit.SlotFill;
        ItemIcon.Show(icon, null);
        GradeGlow.Show(glow, null);
        Write(title, "");
        Write(count, "");
        guide.spot = GuideSpot.None;
        guide.itemId = "";
    }

    /// <summary>The small word at the top of the box — an equipment slot's name.</summary>
    public void SetCorner(string word, ThemeColor colour)
    {
        Write(corner, word ?? "");
        UiKit.Paint(corner, colour);
    }

    /// <summary>Lit while a piece that belongs here is being dragged.</summary>
    public void Light(bool on)
    {
        if (on) { UiKit.Paint(edge, UiKit.Accent); UiKit.Paint(fill, UiKit.Hover); }
        else if (Entry != null) { UiKit.Paint(edge, UiKit.ButtonEdge); UiKit.Paint(fill, shownFill); }
        else { UiKit.Paint(edge, new Color(0, 0, 0, 0)); UiKit.Paint(fill, UiKit.SlotEmpty); }
    }

    public bool TakeDrop(ItemSlot from, PointerEventData e) => owner != null && owner.Dropped(this, from);

    // A text write rebuilds its mesh; written only when the words change.
    static void Write(TMP_Text t, string s)
    {
        if (t != null && t.text != s) t.text = s;
    }
}

/// <summary>The space of a grid that is not a box — a drop there means "this grid", with no box named.</summary>
public class ItemGridDropZone : MonoBehaviour, IItemDropTarget
{
    public IItemBoxOwner owner;
    public bool TakeDrop(ItemSlot from, PointerEventData e) => owner != null && owner.Dropped(null, from);
}

/// <summary>
/// THE GRID every list of things is drawn in — the bag and the Keeper's storage alike: <see cref="ItemBox"/>es six
/// across and four down with no gap between them, scrolling down when there are more.
///
/// <para><b>It builds only what is on screen.</b> Five rows of boxes are made once and moved as the list scrolls, each
/// told which thing it now shows — so 600 stored things cost thirty boxes.</para>
///
/// <para><b>The scroll bar has its own lane, outside the boxes.</b> The lane is always there, so nothing moves when the
/// bar appears; the bar shows only when the boxes do not fit, and never covers one. There is no sideways scroll.</para>
/// </summary>
public class ItemGrid
{
    public const int Columns = 6, Rows = 4;
    public const float Width = ItemBox.Size * Columns, Height = ItemBox.Size * Rows;
    public const float LaneGap = 4f, BarWidth = 6f;
    /// <summary>How wide the grid stands with its scroll lane: what a window must leave room for.</summary>
    public const float WidthWithLane = Width + LaneGap + BarWidth;

    IItemBoxOwner owner;
    RectTransform content;
    ScrollRect scroll;
    readonly List<ItemBox> pool = new List<ItemBox>((Rows + 1) * Columns);
    int shownFrom = -1, shownCount = -1;

    /// <summary>Build the grid into <paramref name="parent"/>, its top-left corner <paramref name="at"/> from the
    /// parent's top-left (y downwards is negative). Returns the boxes' area.</summary>
    public RectTransform Build(Transform parent, Vector2 at, IItemBoxOwner boxOwner)
    {
        owner = boxOwner;

        var viewport = UiKit.Img("Grid", parent, new Color(0, 0, 0, 0));
        var vrt = viewport.rectTransform;
        TopLeft(vrt, at, new Vector2(Width, Height));
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<ItemGridDropZone>().owner = boxOwner;

        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = ItemBox.Size;
        scroll.viewport = vrt;

        content = new GameObject("Boxes", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(vrt, false);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        scroll.content = content;

        // THE LANE, beside the boxes and never over them. AutoHide takes the bar away when everything fits, and only the
        // bar: the boxes keep their place either way.
        var lane = UiKit.Img("ScrollLane", parent, new Color(0, 0, 0, 0));
        TopLeft(lane.rectTransform, at + new Vector2(Width + LaneGap, 0), new Vector2(BarWidth, Height));
        var handle = UiKit.Round(UiKit.Img("Handle", lane.transform, UiKit.ButtonEdge), UiKit.ButtonRadius);
        UiKit.Fill(handle.rectTransform, Vector2.zero, Vector2.zero);
        var bar = lane.gameObject.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        scroll.onValueChanged.AddListener(_ => Lay(false));
        return vrt;
    }

    static void TopLeft(RectTransform rt, Vector2 at, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = size;
        rt.anchoredPosition = at;
    }

    /// <summary>The owner's things changed: every box in view is told again.</summary>
    public void Refresh() => Lay(true);

    /// <summary>Back to the first row.</summary>
    public void ToTop()
    {
        if (content != null) content.anchoredPosition = Vector2.zero;
        Lay(true);
    }

    void Lay(bool rebind)
    {
        if (content == null || owner == null) return;
        int count = Mathf.Max(0, owner.BoxCount);
        int rows = (count + Columns - 1) / Columns;
        float height = rows * ItemBox.Size;
        if (!Mathf.Approximately(content.sizeDelta.y, height)) content.sizeDelta = new Vector2(0f, height);

        int firstRow = Mathf.Clamp(Mathf.FloorToInt(content.anchoredPosition.y / ItemBox.Size), 0, Mathf.Max(0, rows - 1));
        int first = firstRow * Columns;
        if (!rebind && first == shownFrom && count == shownCount) return;
        shownFrom = first;
        shownCount = count;

        // perf-ok: grows to its fixed size once — five rows of boxes — and never again
        while (pool.Count < (Rows + 1) * Columns) pool.Add(ItemBox.Make(content, "Box", owner));
        for (int k = 0; k < pool.Count; k++)
        {
            var box = pool[k];
            int i = first + k;
            if (i >= count)
            {
                if (box.gameObject.activeSelf) box.gameObject.SetActive(false);
                box.index = -1;
                continue;
            }
            if (!box.gameObject.activeSelf) box.gameObject.SetActive(true);
            box.index = i;
            ((RectTransform)box.transform).anchoredPosition =
                new Vector2((i % Columns) * ItemBox.Size, -(i / Columns) * ItemBox.Size);
            owner.Bind(i, box);
        }
    }
}
