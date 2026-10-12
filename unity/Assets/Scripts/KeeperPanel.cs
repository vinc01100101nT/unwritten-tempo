using System.Collections.Generic;
using UnityEngine;

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
/// column (<see cref="UiListView"/>) makes text and rows only for the lines in view — 600 stored things cost the rows on
/// screen, not 600 rows made and destroyed on every change. Whatever arrives in one frame redraws it once.</para>
///
/// <para>Opening it is an interaction with a townsperson, exactly as a shop is: <see cref="PlayerInteractor"/> holds the
/// player, frames the camera and closes this window when the interaction ends.</para>
/// </summary>
public class KeeperPanel : MonoBehaviour, UiListView.ISource
{
    public static KeeperPanel Instance { get; private set; }

    GameObject root;
    UiListView view;
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
    enum Kind { Title, Gold, Note, Header, Button, Trip, Stored, Carried, Result }
    struct Line { public Kind kind; public int index; }
    readonly List<Line> lines = new List<Line>(64);

    // The fixed words of the page, by number, so a line holds a number and no text until it is on screen.
    static readonly string[] Notes =
    {
        "Nobody here.", "Asking the Keeper…", "There is nowhere to send you from here.", "Opening storage…",
        "Nothing is stored.", "You are carrying nothing that can be stored.",
    };
    const int NoteNobody = 0, NoteAsking = 1, NoteNowhere = 2, NoteOpening = 3, NoteNothingStored = 4, NoteNothingCarried = 5;
    static readonly string[] Buttons = { "Save Point", "Storage", "Teleport", "← Back" };
    const int ButtonSavePoint = 0, ButtonStorage = 1, ButtonTeleport = 2, ButtonBack = 3;
    const int HeaderStored = 0, HeaderCarried = 1;

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
        Instance.storage.Take(sent, startsAt, true);
        Instance.storedRows = rows;
        Instance.storageLimit = limit;
        Instance.linesOwed = true;
    }

    /// <summary>One stored row that changed (a count of 0 is a row gone), and how many things storage now holds.</summary>
    public static void TakeStorageRow(StoredLine line, int rows)
    {
        if (Instance == null) return;
        Instance.KeepStorageFor(Character.Instance);
        Instance.storage.TakeRow(line, true);
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
    void OnGoldChanged(int _) => linesOwed = true;

    void Build()
    {
        if (root != null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var body = UiKit.Window(canvas.transform, "Keeper", new Vector2(420, 460), Vector2.zero, out root);
        view = new UiListView(body, this);
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
            else
            {
                Add(Kind.Header, HeaderStored);
                for (int i = 0; i < storage.Entries.Count; i++) Add(Kind.Stored, i);
                if (storage.Entries.Count == 0) Add(Kind.Note, NoteNothingStored);
                Add(Kind.Header, HeaderCarried);
                int storable = 0;
                for (int i = 0; i < c.Bag.Count; i++)
                {
                    var e = c.Bag[i];
                    // A QUEST ITEM IS NEVER STORED — the server refuses it, so it is not offered.
                    if (e == null || e.def == null || e.def.category == ItemCategory.Quest) continue;
                    storable++;
                    Add(Kind.Carried, i);
                }
                if (storable == 0) Add(Kind.Note, NoteNothingCarried);
            }
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
            case Kind.Header: return UiListView.LineHeight(15);
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
                slot.ShowLine("Your gold: " + (c != null ? c.Gold : 0), 13, UiKit.TextMain);
                break;
            case Kind.Note:
                slot.ShowLine(Notes[line.index], 12, Sub);
                break;
            case Kind.Header:
                slot.ShowLine(line.index == HeaderStored ? "In storage (" + storedRows + " of " + storageLimit + ")"
                                                         : "In your bag", 15, UiKit.TitleText);
                break;
            case Kind.Button:
                slot.ShowButton(Buttons[line.index]);
                break;
            case Kind.Trip:
            {
                var t = trips[line.index];
                // TOO DEAR FOR THE PURSE THE SERVER SENT, drawn dim. The press still asks; the server still decides.
                slot.ShowButton(t.name + " — " + t.price + "g", c != null && t.price > c.Gold);
                break;
            }
            case Kind.Stored:
            {
                var e = storage.Entries[line.index];
                if (e.instance != null)
                    slot.ShowDeal(Rarities.Coloured(e.def, e.instance, ItemTooltip.CopyTitle(e.instance)), "Take");
                else
                    slot.ShowDeal(Rarities.Coloured(e.def, null, e.def.displayName) + " ×" + e.count, "Take 1", "Take all");
                break;
            }
            case Kind.Carried:
            {
                var e = c != null && line.index < c.Bag.Count ? c.Bag[line.index] : null;
                if (e == null || e.def == null) { slot.ShowLine("", 12, Sub); break; }
                if (e.instance != null)
                    slot.ShowDeal(Rarities.Coloured(e.def, e.instance, ItemTooltip.CopyTitle(e.instance)), "Store");
                else
                    slot.ShowDeal(Rarities.Coloured(e.def, null, e.def.displayName) + " ×" + e.count, "Store 1", "Store all");
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
            case Kind.Stored:
            {
                if (line.index >= storage.Entries.Count) break;
                var e = storage.Entries[line.index];
                if (me != null) me.AskToTake(e.def.id, e.CopyId, which == 1);
                UISounds.PlayClick();
                break;
            }
            case Kind.Carried:
            {
                var c = Character.Instance;
                if (c == null || line.index >= c.Bag.Count) break;
                var e = c.Bag[line.index];
                if (e == null || e.def == null) break;
                if (me != null) me.AskToStore(line.index, e.def.id, e.instance != null ? e.instance.copyId ?? "" : "", which == 1);
                UISounds.PlayClick();
                break;
            }
        }
    }

    void Open(Page which)
    {
        page = which;
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
}
