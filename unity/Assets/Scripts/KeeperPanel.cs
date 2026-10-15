using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// THE KEEPER'S WINDOW — what opens when you press F on a town's Keeper. A menu of the Keeper's three services, each
/// opening its own page in the same window with a Back row, the way a station's window works:
/// <b>Save Point</b> (one press: this town becomes where you come back to), <b>Teleport</b> (every other town, at the
/// price the server worked out) and <b>Storage</b> (what your account keeps, and what you carry that can go in).
///
/// <para><b>It works nothing out.</b> Every price and the storage list arrive from the server, and every press only asks:
/// which service, which town, which bag position, which stored thing. A town whose price is more than the gold you hold
/// is drawn dim — a comparison of two numbers the server sent, which decides nothing: the press still goes, and the
/// server still answers.</para>
///
/// <para><b>It builds only what is on screen.</b> The page is a list of plain records (<see cref="Line"/>), and the
/// column (<see cref="UiListView"/>) makes text and rows only for the lines in view. Whatever arrives in one frame
/// redraws it once.</para>
///
/// <para><b>Storage is two grids, the bag's own</b> (<see cref="ItemGrid"/>): what is stored on the left, what you carry
/// on the right, each thing a box by its name with its tooltip on hover. Double-click a box to move all of it across,
/// Ctrl + double-click to move one; or drag it from one grid onto the other to move all of it. A grid makes boxes only
/// for the rows in view, so 600 stored things cost thirty boxes.</para>
///
/// <para>Opening it is an interaction with a townsperson, exactly as a shop is: <see cref="PlayerInteractor"/> holds the
/// player, frames the camera and closes this window when the interaction ends.</para>
/// </summary>
public class KeeperPanel : MonoBehaviour, UiListView.ISource
{
    public static KeeperPanel Instance { get; private set; }

    // The window's size on its menu and teleport pages, and wider on the storage page for its two grids side by side.
    static readonly Vector2 ListSize = new Vector2(420, 460);
    const float GridGap = 12f, StorageTop = 112f, CaptionHeight = 18f;
    static readonly Vector2 StorageSize = new Vector2(2 * ItemGrid.WidthWithLane + GridGap + 32f,
                                                      StorageTop + CaptionHeight + 4f + ItemGrid.Height + 8f + 50f);

    GameObject root;
    UiListView view;
    GameObject storageArea;
    TMP_Text storedCaption, carriedCaption;
    readonly ItemGrid storedGrid = new ItemGrid(), carriedGrid = new ItemGrid();
    StoredBoxes storedBoxes;
    CarriedBoxes carriedBoxes;
    string keeper;
    string lastResult;
    bool lastResultOk;
    bool listening;
    // Something the page shows changed since it was last laid out; laid out again at the end of this frame.
    bool linesOwed;

    enum Page { Menu, Teleport, Storage }
    Page page = Page.Menu;

    // What the server last sent: the towns and their prices, and what storage holds. The storage copy is kept across
    // openings of the window — every move since reached it as the one row that changed — and let go when the person at
    // this keyboard is a different character.
    KeeperTrip[] trips;
    readonly StorageList storage = new StorageList();
    int storedRows, storageLimit;
    Character storageOf;

    // ---- the page, as plain records ------------------------------------------------------------------------
    enum Kind { Title, Gold, Note, Button, Trip, Result }
    struct Line { public Kind kind; public int index; }
    readonly List<Line> lines = new List<Line>(64);

    // The fixed words of the page, by number, so a line holds a number and no text until it is on screen.
    static readonly string[] Notes =
    {
        "Nobody here.", "Asking the Keeper…", "There is nowhere to send you from here.", "Opening storage…",
    };
    const int NoteNobody = 0, NoteAsking = 1, NoteNowhere = 2, NoteOpening = 3;
    static readonly string[] Buttons = { "Save Point", "Storage", "Teleport", "← Back" };
    const int ButtonSavePoint = 0, ButtonStorage = 1, ButtonTeleport = 2, ButtonBack = 3;

    static readonly Color Sub = UiKit.TextDim;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        StopListening();
    }

    /// <summary>Open the window, kept by <paramref name="keeperTitle"/> — their name and what they are, as the plate
    /// over them reads.</summary>
    public static void OpenFor(string keeperTitle)
    {
        if (Instance == null) return;
        Instance.Show(keeperTitle);
    }

    /// <summary>Close it, whoever asked.</summary>
    public static void CloseIt()
    {
        if (Instance == null || Instance.root == null) return;
        UiAnim.Close(Instance.root);
        Instance.StopListening();
    }

    /// <summary>True while it is on screen.</summary>
    public static bool IsOpen => Instance != null && Instance.root != null && UiAnim.IsOpen(Instance.root);

    /// <summary>What the server said a service did, or why it refused. The chat carries the same sentence.</summary>
    public static void ShowResult(string line, bool ok)
    {
        if (Instance == null) return;
        Instance.lastResult = line;
        Instance.lastResultOk = ok;
        Instance.linesOwed = true;
    }

    /// <summary>The towns this Keeper can send you to, each with its finished price, as the server sent them.</summary>
    public static void TakeTrips(KeeperTrip[] sent)
    {
        if (Instance == null) return;
        Instance.trips = sent ?? new KeeperTrip[0];
        Instance.linesOwed = true;
    }

    /// <summary>One chunk of the storage list, starting at row <paramref name="startsAt"/>, and how many things storage
    /// holds out of how many it can.</summary>
    public static void TakeStorage(StoredLine[] sent, int startsAt, int rows, int limit)
    {
        if (Instance == null) return;
        Instance.KeepStorageFor(Character.Instance);
        Instance.storage.Take(sent, startsAt);
        Instance.storedRows = rows;
        Instance.storageLimit = limit;
        Instance.linesOwed = true;
    }

    /// <summary>One stored row that changed (a count of 0 is a row gone), and how many things storage now holds.</summary>
    public static void TakeStorageRow(StoredLine line, int rows)
    {
        if (Instance == null) return;
        Instance.KeepStorageFor(Character.Instance);
        Instance.storage.TakeRow(line);
        Instance.storedRows = rows;
        Instance.linesOwed = true;
    }

    // The storage copy belongs to one character's account; another character at this keyboard starts without it.
    void KeepStorageFor(Character who)
    {
        if (who == storageOf) return;
        storageOf = who;
        storage.Clear();
    }

    void Show(string keeperTitle)
    {
        keeper = keeperTitle;
        lastResult = null;
        page = Page.Menu;
        ShowStorageArea(false);
        trips = null;
        KeepStorageFor(Character.Instance);
        Build();
        if (root == null) return;
        UiAnim.Open(root);
        UISounds.PlayOpen();
        StartListening();
        linesOwed = true;
        view.ToTop();
    }

    // THE BAG AND THE PURSE ARRIVE FROM THE SERVER after a move or a trip is paid for, a moment after the sentence — so
    // the page is laid out again when they do.
    void StartListening()
    {
        if (listening) return;
        var store = InventoryStore.Instance;
        if (store != null) store.OnChanged += OnChanged;
        var c = Character.Instance;
        if (c != null) c.OnGoldChanged += OnGoldChanged;
        listening = true;
    }

    void StopListening()
    {
        if (!listening) return;
        var store = InventoryStore.Instance;
        if (store != null) store.OnChanged -= OnChanged;
        var c = Character.Instance;
        if (c != null) c.OnGoldChanged -= OnGoldChanged;
        listening = false;
    }

    void OnChanged() => linesOwed = true;
    void OnGoldChanged(long _) => linesOwed = true;

    void Build()
    {
        if (root != null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var body = UiKit.Window(canvas.transform, "Keeper", ListSize, Vector2.zero, out root);
        view = new UiListView(body, this);
        BuildStorage(body);
    }

    // The two grids and their captions, under the page's title and Back button; shown only on the storage page.
    void BuildStorage(RectTransform body)
    {
        var area = new GameObject("Storage", typeof(RectTransform));
        area.transform.SetParent(body, false);
        var art = (RectTransform)area.transform;
        art.anchorMin = art.anchorMax = new Vector2(0, 1);
        art.pivot = new Vector2(0, 1);
        art.anchoredPosition = new Vector2(0, -StorageTop);
        art.sizeDelta = new Vector2(2 * ItemGrid.WidthWithLane + GridGap, CaptionHeight + 4f + ItemGrid.Height);
        storageArea = area;

        storedCaption = Caption(art, 0f);
        carriedCaption = Caption(art, ItemGrid.WidthWithLane + GridGap);
        storedBoxes = new StoredBoxes(this);
        carriedBoxes = new CarriedBoxes(this);
        storedGrid.Build(art, new Vector2(0, -(CaptionHeight + 4f)), storedBoxes);
        carriedGrid.Build(art, new Vector2(ItemGrid.WidthWithLane + GridGap, -(CaptionHeight + 4f)), carriedBoxes);
        area.SetActive(false);
    }

    static TMP_Text Caption(RectTransform parent, float x)
    {
        var t = UiKit.MakeText("Caption", parent, "", 15, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        t.color = UiKit.TitleText;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, 0);
        rt.sizeDelta = new Vector2(ItemGrid.Width, CaptionHeight);
        return t;
    }

    // The storage page shows the grids and widens the window for them; every other page is the list alone.
    void ShowStorageArea(bool on)
    {
        if (root == null || storageArea == null) return;
        ((RectTransform)root.transform).sizeDelta = on ? StorageSize : ListSize;
        if (storageArea.activeSelf != on) storageArea.SetActive(on);
    }

    // AT MOST ONCE A FRAME, however many things arrived in it: the page is laid out again, and the column redraws the
    // lines in view. One test a frame when nothing happened.
    void LateUpdate()
    {
        if (view == null) return;
        if (linesOwed && IsOpen)
        {
            linesOwed = false;
            LayOut();
            view.Changed();
            DrawStorage();
        }
        view.RedrawIfNeeded();
    }

    void LayOut()
    {
        lines.Clear();
        var c = Character.Instance;
        if (c == null) { Add(Kind.Note, NoteNobody); return; }

        Add(Kind.Title, 0);
        if (page == Page.Menu)
        {
            Add(Kind.Button, ButtonSavePoint);
            Add(Kind.Button, ButtonStorage);
            Add(Kind.Button, ButtonTeleport);
        }
        else
        {
            Add(Kind.Button, ButtonBack);
            if (page == Page.Teleport)
            {
                Add(Kind.Gold, 0);
                if (trips == null) Add(Kind.Note, NoteAsking);
                else if (trips.Length == 0) Add(Kind.Note, NoteNowhere);
                else for (int i = 0; i < trips.Length; i++) Add(Kind.Trip, i);
            }
            else if (!storage.Known) Add(Kind.Note, NoteOpening);
            // The storage page's things are its two grids (DrawStorage), under these lines.
        }
        // WHAT THE LAST PRESS DID, last.
        if (!string.IsNullOrEmpty(lastResult)) Add(Kind.Result, 0);
    }

    void Add(Kind kind, int index) => lines.Add(new Line { kind = kind, index = index });

    // ---- what the column asks --------------------------------------------------------------------------------
    public int Count => lines.Count;

    public float HeightOf(int i)
    {
        switch (lines[i].kind)
        {
            case Kind.Title:  return UiListView.LineHeight(18);
            case Kind.Gold:   return UiListView.LineHeight(13);
            case Kind.Note:   return UiListView.LineHeight(12);
            case Kind.Result: return UiListView.LineHeight(12, 2);
            default:          return UiListView.RowHeight;
        }
    }

    public void Bind(int i, UiListView.Slot slot)
    {
        var line = lines[i];
        var c = Character.Instance;
        switch (line.kind)
        {
            case Kind.Title:
                slot.ShowLine(page == Page.Menu ? keeper : keeper + "  ·  " + (page == Page.Teleport ? "Teleport" : "Storage"),
                              18, UiKit.TitleText);
                break;
            case Kind.Gold:
                slot.ShowLine("You have " + Wallet.Amount(c != null ? c.Gold : 0), 13, UiKit.TextMain);
                break;
            case Kind.Note:
                slot.ShowLine(Notes[line.index], 12, Sub);
                break;
            case Kind.Button:
                slot.ShowButton(Buttons[line.index]);
                break;
            case Kind.Trip:
            {
                var t = trips[line.index];
                // TOO DEAR FOR THE PURSE THE SERVER SENT, drawn dim. The press still asks; the server still decides.
                slot.ShowButton(t.name + " — " + Wallet.Amount(t.price), c != null && t.price > c.Gold);
                break;
            }
            case Kind.Result:
                slot.ShowLine(lastResult ?? "", 12, lastResultOk ? UiKit.TextMain : UiKit.Bad, 2);
                break;
        }
    }

    public void Press(int i, int which)
    {
        var line = lines[i];
        var me = GameNetwork.MyPlayer;
        switch (line.kind)
        {
            case Kind.Button:
                if (line.index == ButtonSavePoint) { if (me != null) me.AskKeeperSavePoint(); UISounds.PlayClick(); }
                else Open(line.index == ButtonStorage ? Page.Storage : line.index == ButtonTeleport ? Page.Teleport : Page.Menu);
                break;
            case Kind.Trip:
                if (me != null && trips != null && line.index < trips.Length) me.AskKeeperTeleport(trips[line.index].map);
                UISounds.PlayClick();
                break;
        }
    }

    void Open(Page which)
    {
        page = which;
        ShowStorageArea(which == Page.Storage);
        if (which == Page.Storage) { storedGrid.ToTop(); carriedGrid.ToTop(); }
        lastResult = null;
        UISounds.PlayClick();
        var me = GameNetwork.MyPlayer;
        if (me != null)
        {
            if (which == Page.Teleport) { trips = null; me.AskKeeperTrips(); }
            else if (which == Page.Storage) me.AskKeeperStorage();
        }
        linesOwed = true;
        view.ToTop();
    }

    // ---- the storage page's two grids ----------------------------------------------------------------------------

    // What each grid shows, as the boxes hold it — the stored things as bag entries made once and reused, and the things
    // carried that can be stored (a quest item never can: the server refuses it, so it is not offered).
    readonly List<BagEntry> storedShown = new List<BagEntry>(64);
    readonly List<BagEntry> carriedShown = new List<BagEntry>(64);

    void DrawStorage()
    {
        if (page != Page.Storage || storageArea == null) return;
        var c = Character.Instance;

        var entries = storage.Entries;
        while (storedShown.Count < entries.Count) storedShown.Add(new BagEntry());   // perf-ok: grows to the most ever stored, once
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var row = storedShown[i];
            row.def = e.def;
            row.count = e.count;
            row.instance = e.instance;
        }
        storedBoxes.count = storage.Known ? entries.Count : 0;

        carriedShown.Clear();
        if (c != null)
            for (int i = 0; i < c.Bag.Count; i++)
            {
                var e = c.Bag[i];
                if (e != null && e.def != null && e.def.CanBeStored) carriedShown.Add(e);
            }

        storedCaption.text = storage.Known ? "In storage (" + storedRows + " of " + storageLimit + ")" : "In storage";
        carriedCaption.text = "In your bag";
        storedGrid.Refresh();
        carriedGrid.Refresh();
    }

    static int Padded(int count)
    {
        int filled = (count + ItemGrid.Columns - 1) / ItemGrid.Columns * ItemGrid.Columns;
        return Mathf.Max(ItemGrid.Rows * ItemGrid.Columns, filled);
    }

    // Double-click moves all of it; with Ctrl held, one.
    static bool JustOne() => Hotkeys.Held(KeyCode.LeftControl) || Hotkeys.Held(KeyCode.RightControl);

    void TakeOut(BagEntry stored, bool all)
    {
        var me = GameNetwork.MyPlayer;
        if (me == null || stored == null || stored.def == null) return;
        me.AskToTake(stored.def.id, stored.instance != null ? stored.instance.copyId ?? "" : "", all);
        UISounds.PlayClick();
    }

    void PutIn(BagEntry carried, bool all)
    {
        var me = GameNetwork.MyPlayer;
        var c = Character.Instance;
        if (me == null || c == null || carried == null || carried.def == null) return;
        int index = c.Bag.IndexOf(carried);
        if (index < 0) return;
        me.AskToStore(index, carried.def.id, carried.instance != null ? carried.instance.copyId ?? "" : "", all);
        UISounds.PlayClick();
    }

    class StoredBoxes : IItemBoxOwner
    {
        readonly KeeperPanel panel;
        public int count;
        public StoredBoxes(KeeperPanel owner) { panel = owner; }
        public int BoxCount => Padded(count);
        public void Bind(int index, ItemBox box)
        {
            if (index < count) box.Show(panel.storedShown[index], ItemBoxSource.Storage);
            else box.ShowEmpty();
        }
        public void Clicked(ItemBox box, PointerEventData e)
        {
            if (box.Entry != null && e.button == PointerEventData.InputButton.Left && e.clickCount >= 2)
                panel.TakeOut(box.Entry, !JustOne());
        }
        public bool Dropped(ItemBox onto, ItemSlot from)
        {
            var fromBox = from != null ? from.GetComponent<ItemBox>() : null;
            if (fromBox == null) return false;
            if (fromBox.source == ItemBoxSource.Bag) panel.PutIn(from.boundEntry, true);
            return true;
        }
    }

    class CarriedBoxes : IItemBoxOwner
    {
        readonly KeeperPanel panel;
        public CarriedBoxes(KeeperPanel owner) { panel = owner; }
        public int BoxCount => Padded(panel.carriedShown.Count);
        public void Bind(int index, ItemBox box)
        {
            if (index < panel.carriedShown.Count) box.Show(panel.carriedShown[index], ItemBoxSource.Bag);
            else box.ShowEmpty();
        }
        public void Clicked(ItemBox box, PointerEventData e)
        {
            if (box.Entry != null && e.button == PointerEventData.InputButton.Left && e.clickCount >= 2)
                panel.PutIn(box.Entry, !JustOne());
        }
        public bool Dropped(ItemBox onto, ItemSlot from)
        {
            var fromBox = from != null ? from.GetComponent<ItemBox>() : null;
            if (fromBox == null) return false;
            if (fromBox.source == ItemBoxSource.Storage) panel.TakeOut(from.boundEntry, true);
            return true;
        }
    }
}
