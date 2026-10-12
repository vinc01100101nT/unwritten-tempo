using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE SHOP WINDOW — what opens when you press F on a shopkeeper. It lists what that shop sells, each at the price of
/// one, and — when the shop buys — what you are carrying that it will take, each at what one fetches.
///
/// <para><b>It works nothing out.</b> The price of one is the item's own number, read as it is; every press only asks,
/// and the server decides whether the deal goes through and what it came to, which arrives as a sentence. Gold and the
/// bag on screen are the server's, redrawn whenever it sends them.</para>
///
/// <para>Opening a shop is an interaction with a townsperson, exactly as a station is: <see cref="PlayerInteractor"/>
/// holds the player, frames the camera and closes this window when the interaction ends.</para>
/// </summary>
public class ShopPanel : MonoBehaviour
{
    public static ShopPanel Instance { get; private set; }

    GameObject root;
    RectTransform content;
    string shopId;
    string keeper;
    string lastResult;
    bool lastResultOk;
    bool listening;
    // When the minutes shown on a buy-back line next change (or one ends), on this machine's steady clock. The one
    // thing checked each frame while the window is open; infinity while nothing is kept.
    float redrawAt = float.PositiveInfinity;

    // The buy-back rows on screen: each one's label, the words before its minutes, and the moment it ends — so a minute
    // ticking over rewrites that label where it stands. Redrawing the whole list for it would destroy every button, and
    // a press that began before the redraw would land on nothing.
    struct KeptRow { public TMPro.TMP_Text label; public string before; public double endsAt; }
    readonly System.Collections.Generic.List<KeptRow> keptRows = new System.Collections.Generic.List<KeptRow>(ShopMath.BuybackMostEntries);

    static readonly Color Sub = UiKit.TextDim;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        StopListening();
    }

    /// <summary>Open the window for shop <paramref name="id"/>, kept by <paramref name="keeperTitle"/> — their name
    /// and what they are, as the plate over them reads.</summary>
    public static void OpenFor(string id, string keeperTitle)
    {
        if (Instance == null || WorldShops.Find(id) == null) return;
        Instance.Show(id, keeperTitle);
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

    /// <summary>What the server said a deal did, or why it refused. The chat carries the same sentence.</summary>
    public static void ShowResult(string line, bool ok)
    {
        if (Instance == null) return;
        Instance.lastResult = line;
        Instance.lastResultOk = ok;
        if (IsOpen) Instance.Rebuild();
    }

    void Show(string id, string keeperTitle)
    {
        shopId = id;
        keeper = keeperTitle;
        lastResult = null;
        Build();
        if (root == null) return;
        UiAnim.Open(root);
        UISounds.PlayOpen();
        StartListening();
        Rebuild();
    }

    // THE BAG AND THE PURSE ARRIVE FROM THE SERVER after a deal, a moment after the sentence saying what it did — so
    // the lists redraw when they do, not when the sentence lands.
    void StartListening()
    {
        if (listening) return;
        var store = InventoryStore.Instance;
        if (store == null) return;
        store.OnChanged += OnBagChanged;
        // What a first run gives free arrives with the quests, a moment after the bag.
        QuestStore.Changed += OnBagChanged;
        listening = true;
    }

    void StopListening()
    {
        if (!listening) return;
        var store = InventoryStore.Instance;
        if (store != null) store.OnChanged -= OnBagChanged;
        QuestStore.Changed -= OnBagChanged;
        listening = false;
    }

    void OnBagChanged()
    {
        if (IsOpen) Rebuild();
    }

    // A minute changed on some buy-back row: rewrite the minutes where they stand, and redraw the list only when a row
    // has ended and must go.
    void RetimeKept()
    {
        redrawAt = float.PositiveInfinity;
        for (int i = 0; i < keptRows.Count; i++)
        {
            var row = keptRows[i];
            double left = WorldClock.SecondsUntil(WorldClock.FromEpochSeconds(row.endsAt));
            if (left <= 0 || row.label == null) { Rebuild(); return; }
            row.label.text = row.before + ShopMath.MinutesShown(left) + " min left";
            redrawAt = Mathf.Min(redrawAt, Time.unscaledTime + (float)ShopMath.SecondsUntilMinuteChanges(left));
        }
    }

    void Update()
    {
        if (Time.unscaledTime >= redrawAt && IsOpen) RetimeKept();
    }

    void Build()
    {
        if (root != null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        content = UiKit.ListWindow(canvas.transform, "Shop", new Vector2(420, 460), out root);
    }

    void Rebuild()
    {
        if (content == null) return;
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

        var c = Character.Instance;
        var shop = WorldShops.Find(shopId);
        if (c == null || shop == null) { Line("Nobody here.", 13, Sub); return; }

        Line(keeper, 18, UiKit.TitleText);
        Line("Your gold: " + c.Gold, 13, UiKit.TextMain);

        // ---- what they sell ---------------------------------------------------------------------------
        Line("For sale", 15, UiKit.TitleText);
        int selling = 0;
        for (int i = 0; i < shop.stock.Count; i++)
        {
            var def = ItemCatalog.Resolve(shop.stock[i]);
            if (!WorldShops.Sells(shop, def)) continue;
            selling++;
            string id = def.id;
            // FREE ONES FIRST, as the server counted them — a tutorial's first run takes nothing it has you buy.
            int free = QuestStore.FreeBuys(id);
            string price = free > 0 ? free + " free, then " + def.buyPrice + "g each" : def.buyPrice + "g each";
            var buyOne = Deal(Rarities.Coloured(def, null, def.displayName) + "   " + price,
                              "Buy 1", () => Buy(id, 1),
                              "Buy 10", () => Buy(id, 10));
            GuideMarker.Put(buyOne.gameObject, GuideSpot.ShopBuyButton, id);
        }
        if (selling == 0) Line("Nothing for sale.", 12, Sub);

        // ---- what they buy ----------------------------------------------------------------------------
        if (shop.buysLoot)
        {
            Line("Sell", 15, UiKit.TitleText);
            int sellable = 0;
            for (int i = 0; i < c.Bag.Count; i++)
            {
                var e = c.Bag[i];
                if (!WorldShops.Buys(shop, e)) continue;
                sellable++;
                int at = i;
                string id = e.def.id;
                int all = e.count;
                // The piece's own id travels with the press, so the server sells exactly the piece on this row.
                string copy = e.instance != null ? e.instance.copyId ?? "" : "";
                // A piece of gear goes by its own name, as its bag row reads; a stack by its item name and count.
                string what = e.instance != null ? Rarities.Coloured(e.def, e.instance, ItemTooltip.CopyTitle(e.instance))
                                                 : Rarities.Coloured(e.def, null, e.def.displayName) + " ×" + e.count;
                Deal(what + "   " + e.def.zeny + "g each",
                     "Sell 1", () => Sell(at, id, copy, 1),
                     "Sell all", () => Sell(at, id, copy, all));
            }
            if (sellable == 0) Line("You are carrying nothing they will buy.", 12, Sub);
        }

        // ---- what they can buy back -------------------------------------------------------------------
        // Newest first. Prices and moments are the server's; this only picks which lines are still showing and how
        // many whole minutes each has left to say.
        redrawAt = float.PositiveInfinity;
        keptRows.Clear();
        var kept = c.Buyback.Entries;
        bool headed = false;
        for (int i = kept.Count - 1; i >= 0; i--)
        {
            var k = kept[i];
            double left = WorldClock.SecondsUntil(WorldClock.FromEpochSeconds(k.endsAt));
            if (left <= 0) continue;
            if (!headed) { Line("Buy back", 15, UiKit.TitleText); headed = true; }
            float changesIn = (float)ShopMath.SecondsUntilMinuteChanges(left);
            redrawAt = Mathf.Min(redrawAt, Time.unscaledTime + changesIn);
            string itemId = k.def.id;
            string copy = k.CopyId;
            string what = k.instance != null ? Rarities.Coloured(k.def, k.instance, ItemTooltip.CopyTitle(k.instance))
                                             : Rarities.Coloured(k.def, null, k.def.displayName) + " ×" + k.count;
            string before = what + "   " + k.price + "g   ";
            var press = Deal(before + ShopMath.MinutesShown(left) + " min left", "Buy back", () => BuyBack(itemId, copy));
            keptRows.Add(new KeptRow { label = press.transform.parent.GetComponentInChildren<TMPro.TMP_Text>(),
                                       before = before, endsAt = k.endsAt });
        }

        // WHAT THE LAST PRESS DID, last.
        if (!string.IsNullOrEmpty(lastResult)) Line(lastResult, 12, lastResultOk ? UiKit.TextMain : UiKit.Bad, 2);
    }

    void Buy(string itemId, int howMany)
    {
        var me = GameNetwork.MyPlayer;
        if (me != null) me.AskToBuy(shopId, itemId, howMany);
        UISounds.PlayClick();
    }

    void Sell(int bagIndex, string itemId, string copyId, int howMany)
    {
        var me = GameNetwork.MyPlayer;
        if (me != null) me.AskToSell(shopId, bagIndex, itemId, copyId, howMany);
        UISounds.PlayClick();
    }

    void BuyBack(string itemId, string copyId)
    {
        var me = GameNetwork.MyPlayer;
        if (me != null) me.AskToBuyBack(shopId, itemId, copyId);
        UISounds.PlayClick();
    }

    // A line of the list that is a deal, and a line of text — the list window's own, built into this window's column.
    Button Deal(string label, string firstWord, System.Action first, string secondWord = null, System.Action second = null)
        => UiKit.ListDeal(content, label, firstWord, first, secondWord, second);

    void Line(string s, int size, Color color, int rows = 1) => UiKit.ListLine(content, s, size, color, rows);
}
