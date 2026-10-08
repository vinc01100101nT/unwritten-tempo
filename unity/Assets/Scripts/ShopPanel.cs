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

    void Build()
    {
        if (root != null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var viewport = UiKit.Window(canvas.transform, "Shop", new Vector2(420, 460), Vector2.zero, out root);
        content = UiKit.ScrollColumn(viewport);
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;
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
            var buyOne = Deal(def.displayName + "   " + price,
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
                // A piece of gear goes by its own name, as its bag row reads; a stack by its item name and count.
                string what = e.instance != null ? ItemTooltip.CopyTitle(e.instance) : e.def.displayName + " ×" + e.count;
                Deal(what + "   " + e.def.zeny + "g each",
                     "Sell 1", () => Sell(at, id, 1),
                     "Sell all", () => Sell(at, id, all));
            }
            if (sellable == 0) Line("You are carrying nothing they will buy.", 12, Sub);
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

    void Sell(int bagIndex, string itemId, int howMany)
    {
        var me = GameNetwork.MyPlayer;
        if (me != null) me.AskToSell(shopId, bagIndex, itemId, howMany);
        UISounds.PlayClick();
    }

    // One line of the list: what it is and its price on the left, two presses on the right.
    // A row with its two buttons; the first is handed back so a guide can point at it.
    Button Deal(string label, string firstWord, System.Action first, string secondWord, System.Action second)
    {
        var row = new GameObject("Deal", typeof(RectTransform));
        row.transform.SetParent(content, false);
        row.AddComponent<LayoutElement>().preferredHeight = 30;
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        var t = UiKit.Label("What", row.transform, label, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        t.color = UiKit.TextMain;
        t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        var firstButton = UiKit.Btn(firstWord, row.transform, first);
        firstButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 70;
        UiKit.Btn(secondWord, row.transform, second).gameObject.AddComponent<LayoutElement>().preferredWidth = 70;
        return firstButton;
    }

    // One line of text, `rows` lines tall — a sentence from the server needs room to wrap.
    void Line(string s, int size, Color color, int rows = 1)
    {
        var t = UiKit.Label("Line", content, s, size, FontStyle.Normal, rows > 1 ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
        t.color = color;
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = (size + 4) * rows + 4;
    }
}
