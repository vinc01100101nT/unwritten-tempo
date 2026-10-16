using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// THE SHOP WINDOW — what opens when you press F on a shopkeeper. One window, three lists side by side: <b>For sale</b>
/// (her stock, then what you sold her and can still buy back), the <b>Cart</b>, and your <b>Inventory</b>. Every row shows
/// the price of one, whole, at its right.
///
/// <para><b>Everything goes through the cart.</b> Dragging a row from For sale to your Inventory (or into the Cart), or
/// double-clicking it, puts it in to buy; dragging from your Inventory to For sale (or into the Cart), or double-clicking it,
/// puts it in to sell; double-clicking a cart row, or dragging it out, takes it back out. A stack asks how many in the one
/// "how many?" box. Nothing is bought or sold until Confirm, which sends the whole cart at once (<see cref="CartLine"/>).</para>
///
/// <para><b>It works nothing out.</b> A row's price is the item's own value; the totals under the cart — Buying, Selling,
/// Tax, You pay — are the server's (<see cref="CartQuote"/>), asked for every time the cart changes and drawn as they come.
/// Confirm waits for the quote of the cart as it stands, and stays pressed-down until the server answers, so a second
/// press can never send the same cart twice.</para>
///
/// <para>Opening a shop is an interaction with a townsperson, exactly as a station is: <see cref="PlayerInteractor"/>
/// holds the player, frames the camera and closes this window when the interaction ends.</para>
/// </summary>
public class ShopPanel : MonoBehaviour
{
    public static ShopPanel Instance { get; private set; }

    // The window's measures: two item columns either side of a narrower cart, the purse and the two buttons under them.
    // How wide the totals' longest word ("Tax (33%)") is drawn; the amounts start right of it.
    const float SumWordWidth = 64f;
    const float PurseWidth = 200f;
    const float CartWidth = 200f, ColumnGap = 8f, HeaderHeight = 20f, SumHeight = 72f, ActionHeight = 30f, ActionWidth = 120f;
    static readonly Vector2 Size = new Vector2(724f, 430f);
    // How long the cart must sit still before it is priced — a flurry of drags is one quote, of the cart as it ends up.
    const float QuoteAfterSeconds = 0.2f;

    GameObject root;
    RectTransform body;
    string shopId;
    bool listening;

    // ---- the cart: intents only ----------------------------------------------------------------------------------
    sealed class CartItem
    {
        public CartKind kind;
        public BagEntry shown;       // the thing itself: the bag's own entry for a sale, the shelf's for a buy
        // WHAT THE CART'S ROW HOLDS: a copy, never the bag's own entry — so dragging a cart row out over the world can
        // never put the real stack down on the ground.
        public BagEntry row;
        public string itemId, copyId;
        public int count;
        public int price;            // the price of one, as its row shows it: the item's value, or a buy-back's price
        public int saleId;           // which sale a buy-back line buys back
    }
    readonly List<CartItem> cart = new List<CartItem>(16);
    int version;
    bool quoted;                     // the quote for the cart as it stands has arrived
    CartQuote quote;
    bool trading;                    // Confirm was pressed and the server has not answered yet
    float quoteAt = float.PositiveInfinity;

    // ---- what the three lists show, rebuilt when the bag, the buy-backs or the free tutorial buys change ----------
    readonly List<BagEntry> stock = new List<BagEntry>(16);
    readonly List<int> stockPrice = new List<int>(16);
    readonly List<SoldEntry> backs = new List<SoldEntry>(10);
    // The bag as the Inventory list shows it: every entry not wholly in the cart to be sold.
    readonly List<BagEntry> bagShown = new List<BagEntry>(64);
    readonly List<BagEntry> backShown = new List<BagEntry>(10);

    UiListView shopView, cartView, bagView;
    ShopList shopList; CartList cartList; BagList bagList;
    // The cart's totals: four words on the left, the server's four numbers on the right.
    readonly TextMeshProUGUI[] sumWords = new TextMeshProUGUI[4], sumNumbers = new TextMeshProUGUI[4];
    TextMeshProUGUI refusal, purse;
    Button confirm, cancel;
    float redrawAt = float.PositiveInfinity;

    void Awake()
    {
        Instance = this;
        shopList = new ShopList(this);
        cartList = new CartList(this);
        bagList = new BagList(this);
    }

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
        DropAmountPrompt.CloseShared();
    }

    /// <summary>True while it is on screen.</summary>
    public static bool IsOpen => Instance != null && Instance.root != null && UiAnim.IsOpen(Instance.root);

    /// <summary>THE SERVER'S TOTALS for a cart. Drawn only when they are for the cart as it stands now.</summary>
    public static void TakeQuote(CartQuote q)
    {
        if (Instance == null || q.version != Instance.version) return;
        Instance.quote = q;
        Instance.quoted = true;
        Instance.DrawSums();
    }

    /// <summary>The server carried out (<paramref name="done"/>) or refused the cart sent as <paramref name="sentAs"/>.
    /// Done empties the cart; refused keeps it, fixed against the bag as it now stands, and asks again.</summary>
    public static void TradeAnswered(int sentAs, bool done)
    {
        if (Instance == null) return;
        Instance.trading = false;
        if (done) Instance.cart.Clear();
        Instance.CartChanged();
    }

    void Show(string id, string keeperTitle)
    {
        shopId = id;
        cart.Clear();
        trading = false;
        Build(keeperTitle);
        if (root == null) return;
        UiAnim.Open(root);
        UISounds.PlayOpen();
        StartListening();
        Refill();
        shopView.ToTop(); cartView.ToTop(); bagView.ToTop();
        CartChanged();
    }

    // THE BAG, THE PURSE AND THE BUY-BACKS ARRIVE FROM THE SERVER after a trade, a moment after the sentence saying what it
    // did — so the lists redraw when they do, and the cart is checked against what is now there.
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
        if (!IsOpen) return;
        Refill();
        if (FixCart()) CartChanged();
        else DrawPurse();
    }

    void Update()
    {
        if (!IsOpen) return;
        if (Time.unscaledTime >= quoteAt) AskForQuote();
        if (Time.unscaledTime >= redrawAt) { Refill(); if (FixCart()) CartChanged(); }
        shopView.RedrawIfNeeded();
        cartView.RedrawIfNeeded();
        bagView.RedrawIfNeeded();
    }

    // ---- building, once --------------------------------------------------------------------------------------------

    void Build(string title)
    {
        if (root != null) { SetTitle(title); return; }
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        body = UiKit.Window(canvas.transform, "Shop", Size, Vector2.zero, out root);
        SetTitle(title);

        float bodyWidth = Size.x - 32f;
        float itemWidth = (bodyWidth - CartWidth - 2 * ColumnGap) / 2f;
        float columnsHeight = Size.y - UiKit.TitleBarHeight - 8f - 18f - ActionHeight - 8f;

        var shopCol = Column("For sale", 0f, itemWidth, columnsHeight, ColumnDrop.Into.Shop, out var shopPort);
        var cartCol = Column("Cart", itemWidth + ColumnGap, CartWidth, columnsHeight, ColumnDrop.Into.Cart, out var cartPort);
        Column("Inventory", itemWidth + ColumnGap + CartWidth + ColumnGap, itemWidth, columnsHeight, ColumnDrop.Into.Bag, out var bagPort);
        shopView = new UiListView(shopPort, shopList);
        cartView = new UiListView(cartPort, cartList);
        bagView = new UiListView(bagPort, bagList);

        // THE CART'S TOTALS, under its rows — the server's four numbers, the last of them larger.
        var cartPortRect = cartPort;
        cartPortRect.offsetMin = new Vector2(cartPortRect.offsetMin.x, SumHeight);
        var sums = new GameObject("Totals", typeof(RectTransform)).GetComponent<RectTransform>();
        sums.SetParent(cartCol, false);
        sums.anchorMin = new Vector2(0f, 0f); sums.anchorMax = new Vector2(1f, 0f); sums.pivot = new Vector2(0.5f, 0f);
        sums.offsetMin = new Vector2(8f, 6f); sums.offsetMax = new Vector2(-8f, SumHeight);
        sumNumbers[0] = SumRow(sums, "Buying", 0, 12, out sumWords[0]);
        sumNumbers[1] = SumRow(sums, "Selling", 1, 12, out sumWords[1]);
        sumNumbers[2] = SumRow(sums, "Tax (33%)", 2, 12, out sumWords[2]);
        sumNumbers[3] = SumRow(sums, "You pay", 3, 14, out sumWords[3]);
        refusal = UiKit.MakeText("Refusal", sums, "", 11, FontStyle.Normal, TextAnchor.UpperLeft, null, true);
        refusal.color = UiKit.Bad;
        refusal.raycastTarget = false;
        UiKit.Fill(refusal.rectTransform, Vector2.zero, Vector2.zero);

        // THE PURSE AND THE TWO BUTTONS, fixed to the bottom.
        purse = UiKit.MakeText("Purse", body, "", 12, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        purse.raycastTarget = false;
        var prt = purse.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0f, 0f); prt.pivot = new Vector2(0f, 0f);
        prt.sizeDelta = new Vector2(PurseWidth, ActionHeight);
        prt.anchoredPosition = Vector2.zero;

        // "DON'T ASK QUANTITY", in the footer's free space: ticked, a stack goes straight into the cart — the whole stack
        // to sell, one to buy — with no "how many?" box. The game's own checkbox; remembered between sessions.
        var ask = new GameObject("DontAskQuantity", typeof(RectTransform)).GetComponent<RectTransform>();
        ask.SetParent(body, false);
        ask.anchorMin = ask.anchorMax = new Vector2(0f, 0f); ask.pivot = new Vector2(0f, 0f);
        ask.sizeDelta = new Vector2(180f, ActionHeight);
        ask.anchoredPosition = new Vector2(PurseWidth + 10f, 0f);
        var tick = UiKit.Checkbox(ask, GameSettings.DontAskQuantity);
        var trt = (RectTransform)tick.transform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0f, 0.5f);
        trt.anchoredPosition = Vector2.zero;
        tick.onValueChanged.AddListener(on => { GameSettings.DontAskQuantity = on; UISounds.PlayClick(); });
        var words = UiKit.MakeText("Words", ask, "Don't ask quantity", 12, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        words.raycastTarget = false;
        UiKit.Fill(words.rectTransform, new Vector2(26f, 0f), Vector2.zero);

        confirm = BottomButton("Confirm", 0f, Confirm);
        UiKit.MarkPrimary(confirm);
        cancel = BottomButton("Cancel", -(ActionWidth + 6f), () => { cart.Clear(); UISounds.PlayClick(); CartChanged(); });
    }

    void SetTitle(string title)
    {
        var header = root.transform.GetComponentInChildren<TextMeshProUGUI>(true);
        if (header != null && !string.IsNullOrEmpty(title)) header.text = title;
    }

    RectTransform Column(string heading, float x, float width, float height, ColumnDrop.Into into, out RectTransform viewport)
    {
        var frame = UiKit.Framed(UiKit.Img(heading, body, UiKit.Rule), UiKit.Rule, UiKit.PanelRaise, UiKit.ButtonRadius);
        var rt = frame.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(x, 0f);
        var drop = frame.gameObject.AddComponent<ColumnDrop>();
        drop.panel = this;
        drop.into = into;
        drop.edge = frame;

        var h = UiKit.MakeText("Heading", rt, heading, 13, FontStyle.Bold, TextAnchor.MiddleLeft, null, false);
        h.color = UiKit.TitleText;
        h.raycastTarget = false;
        var hrt = h.rectTransform;
        hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = new Vector2(1f, 1f); hrt.pivot = new Vector2(0.5f, 1f);
        hrt.offsetMin = new Vector2(8f, -(HeaderHeight + 4f)); hrt.offsetMax = new Vector2(-8f, -4f);

        var port = UiKit.Img("List", rt, new Color(0, 0, 0, 0));
        viewport = port.rectTransform;
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(0f, 2f); viewport.offsetMax = new Vector2(0f, -(HeaderHeight + 6f));
        return rt;
    }

    static TextMeshProUGUI SumRow(RectTransform parent, string word, int row, float size, out TextMeshProUGUI label)
    {
        float lineH = 16f, top = row * lineH;
        label = UiKit.MakeText(word, parent, word, size, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        label.color = row < 3 ? UiKit.TextDim : UiKit.TextMain;
        label.raycastTarget = false;
        Place(label.rectTransform, top, lineH + (row == 3 ? 4f : 0f));
        var n = UiKit.MakeText(word + "Amount", parent, "", size, FontStyle.Bold, TextAnchor.MiddleRight, null, false);
        n.color = UiKit.TitleText;
        n.raycastTarget = false;
        Place(n.rectTransform, top, lineH + (row == 3 ? 4f : 0f));
        // A LONG AMOUNT NEVER RUNS OVER ITS WORD: the number has the room right of the longest word, and shrinks to fit
        // it — down to 8 — rather than spill.
        n.rectTransform.offsetMin = new Vector2(SumWordWidth, n.rectTransform.offsetMin.y);
        n.enableAutoSizing = true;
        n.fontSizeMax = size;
        n.fontSizeMin = 8f;
        return n;
    }

    static void Place(RectTransform rt, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(0f, -(top + height)); rt.offsetMax = new Vector2(0f, -top);
    }

    Button BottomButton(string word, float fromRight, System.Action onClick)
    {
        var b = UiKit.Btn(word, body, onClick);
        var r = (RectTransform)b.transform;
        r.anchorMin = r.anchorMax = new Vector2(1f, 0f);
        r.pivot = new Vector2(1f, 0f);
        r.sizeDelta = new Vector2(ActionWidth, ActionHeight);
        r.anchoredPosition = new Vector2(fromRight, 0f);
        var t = b.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.fontStyle = FontStyles.Bold;
        return b;
    }

    // ---- what the lists show ---------------------------------------------------------------------------------------

    void Refill()
    {
        stock.Clear(); stockPrice.Clear(); backs.Clear(); backShown.Clear();
        redrawAt = float.PositiveInfinity;
        var shop = WorldShops.Find(shopId);
        var c = Character.Instance;
        if (shop != null)
            for (int i = 0; i < shop.stock.Count; i++)
            {
                var def = ItemCatalog.Resolve(shop.stock[i]);
                if (!WorldShops.Sells(shop, def)) continue;
                // perf-ok: one entry per stocked thing, made when the window redraws — never per frame
                stock.Add(new BagEntry { def = def, count = 1 });
                stockPrice.Add(WorldShops.PriceOf(shop, def));
            }
        // What can still be bought back, newest first — prices and moments are the server's.
        if (c != null)
        {
            var kept = c.Buyback.Entries;
            for (int i = kept.Count - 1; i >= 0; i--)
            {
                var k = kept[i];
                double left = WorldClock.SecondsUntil(WorldClock.FromEpochSeconds(k.endsAt));
                if (left <= 0 || k.def == null) continue;
                backs.Add(k);
                // perf-ok: one entry per kept sale, at most ten, made when the window redraws
                backShown.Add(new BagEntry { def = k.def, count = k.count, instance = k.instance });
                redrawAt = Mathf.Min(redrawAt, Time.unscaledTime + (float)ShopMath.SecondsUntilMinuteChanges(left));
            }
        }
        shopView.Changed();
        RefillBag();
        DrawPurse();
    }

    // A STACK OR PIECE PUT WHOLLY IN THE CART LEAVES THE INVENTORY LIST; part of a stack keeps its row.
    void RefillBag()
    {
        bagShown.Clear();
        var c = Character.Instance;
        if (c != null)
            for (int i = 0; i < c.Bag.Count; i++)
            {
                var e = c.Bag[i];
                if (e == null || e.def == null) continue;
                var sale = FindSale(e);
                if (sale != null && sale.count >= (e.instance != null ? 1 : e.count)) continue;
                bagShown.Add(e);
            }
        if (bagView != null) bagView.Changed();
    }

    int ShopRows => stock.Count + backs.Count;

    // THE CART AGAINST WHAT IS THERE NOW: a sold stack gone or shrunk, a buy-back ended — the line goes, or its count is
    // capped. True when anything changed.
    bool FixCart()
    {
        var c = Character.Instance;
        if (c == null) return false;
        bool changed = false;
        for (int i = cart.Count - 1; i >= 0; i--)
        {
            var line = cart[i];
            if (line.kind == CartKind.Sell)
            {
                if (!c.Bag.Contains(line.shown)) { cart.RemoveAt(i); changed = true; continue; }
                int most = line.shown.instance != null ? 1 : line.shown.count;
                if (line.count > most) { line.count = most; changed = true; }
            }
            else if (line.kind == CartKind.BuyBack)
            {
                int at = BackIndex(line.saleId);
                if (at < 0) { cart.RemoveAt(i); changed = true; }
                else { line.shown = backShown[at]; line.row.instance = backShown[at].instance; }
            }
        }
        return changed;
    }

    int BackIndex(int saleId)
    {
        for (int i = 0; i < backs.Count; i++)
            if (backs[i].id == saleId) return i;
        return -1;
    }

    // ---- the cart --------------------------------------------------------------------------------------------------

    /// <summary>Put the shop's row <paramref name="row"/> in the cart to buy (or buy back). A stack asks how many.</summary>
    void AddFromShop(int row, Vector2 at)
    {
        if (trading || row < 0 || row >= ShopRows) return;
        if (row >= stock.Count)
        {
            var k = backs[row - stock.Count];
            // EACH SALE IS ITS OWN ENTRY: the same one twice is refused, a different sale of the same thing is its own line.
            for (int i = 0; i < cart.Count; i++) if (cart[i].kind == CartKind.BuyBack && cart[i].saleId == k.id) return;
            var back = Line(CartKind.BuyBack, backShown[row - stock.Count], k.def.id, k.CopyId, k.count,
                            (int)System.Math.Min(int.MaxValue, k.price));
            back.saleId = k.id;
            cart.Add(back);
            UISounds.PlayClick();
            CartChanged();
            return;
        }
        var def = stock[row].def;
        AskHowMany("Buy how many?", 1, 0, at, n =>
        {
            var line = FindLine(CartKind.Buy, def.id, "");
            if (line != null) line.count = Mathf.Min(ShopCart.MostInOneLine, line.count + n);
            else cart.Add(Line(CartKind.Buy, stock[row], def.id, "", n, stockPrice[row]));
            CartChanged();
        });
    }

    /// <summary>Put the bag's <paramref name="entry"/> in the cart to sell. A stack asks how many.</summary>
    void AddFromBag(BagEntry entry, Vector2 at)
    {
        if (trading || entry == null || entry.def == null) return;
        if (!entry.def.CanBeSold) { ChatController.GameSaid(Item.RefusedLine(Item.CannotBeSoldText), false); return; }
        var shop = WorldShops.Find(shopId);
        if (!WorldShops.Buys(shop, entry)) return;
        var existing = FindSale(entry);
        if (entry.instance != null)
        {
            if (existing != null) return;
            cart.Add(Line(CartKind.Sell, entry, entry.def.id, entry.instance.copyId ?? "", 1, entry.def.value));
            UISounds.PlayClick();
            CartChanged();
            return;
        }
        int left = entry.count - (existing != null ? existing.count : 0);
        if (left <= 0) return;
        AskHowMany("Sell how many? (1–" + left + ")", left, left, at, n =>
        {
            if (existing != null && cart.Contains(existing)) existing.count = Mathf.Min(entry.count, existing.count + n);
            else cart.Add(Line(CartKind.Sell, entry, entry.def.id, "", Mathf.Min(entry.count, n), entry.def.value));
            CartChanged();
        });
    }

    static CartItem Line(CartKind kind, BagEntry shown, string itemId, string copyId, int count, int price) =>
        new CartItem
        {
            kind = kind, shown = shown, itemId = itemId, copyId = copyId ?? "", count = count, price = price,
            row = new BagEntry { def = shown.def, count = count, instance = shown.instance },
        };

    void TakeOut(int line)
    {
        if (trading || line < 0 || line >= cart.Count) return;
        cart.RemoveAt(line);
        UISounds.PlayClick();
        CartChanged();
    }

    CartItem FindLine(CartKind kind, string itemId, string copyId)
    {
        for (int i = 0; i < cart.Count; i++)
            if (cart[i].kind == kind && cart[i].itemId == itemId && cart[i].copyId == (copyId ?? "")) return cart[i];
        return null;
    }

    CartItem FindSale(BagEntry entry)
    {
        for (int i = 0; i < cart.Count; i++)
            if (cart[i].kind == CartKind.Sell && cart[i].shown == entry) return cart[i];
        return null;
    }

    void AskHowMany(string heading, int start, int most, Vector2 at, System.Action<int> confirmed)
    {
        // TICKED "DON'T ASK QUANTITY": straight in, at what the box would have opened holding — the whole stack to sell,
        // one to buy.
        if (GameSettings.DontAskQuantity) { UISounds.PlayClick(); confirmed(start); return; }
        var box = DropAmountPrompt.Shared(GetComponentInParent<Canvas>());
        if (box != null) box.Ask(heading, start, most, at, confirmed, GuideSpot.ShopAmountBox);
    }

    // The cart changed: a new count, a new quote asked for once the cart sits still, and Confirm waits for it.
    void CartChanged()
    {
        version++;
        quoted = false;
        quoteAt = cart.Count > 0 ? Time.unscaledTime + QuoteAfterSeconds : float.PositiveInfinity;
        cartView.Changed();
        RefillBag();
        DrawSums();
    }

    void AskForQuote()
    {
        quoteAt = float.PositiveInfinity;
        var me = GameNetwork.MyPlayer;
        if (me == null || cart.Count == 0) return;
        me.AskForCartQuote(shopId, Lines(), version);
    }

    void Confirm()
    {
        if (trading || !quoted || cart.Count == 0 || !string.IsNullOrEmpty(quote.refusal)) return;
        var me = GameNetwork.MyPlayer;
        if (me == null) return;
        trading = true;
        UISounds.PlayClick();
        me.AskToTrade(shopId, Lines(), version);
        DrawSums();
    }

    // The cart as the server reads it. A sale names where its stack sits now.
    CartLine[] Lines()
    {
        var c = Character.Instance;
        // perf-ok: one small array per quote or trade, sent at most five times a second while the cart is being changed
        var lines = new CartLine[cart.Count];
        for (int i = 0; i < cart.Count; i++)
        {
            var it = cart[i];
            lines[i] = new CartLine
            {
                kind = (byte)it.kind,
                itemId = it.itemId,
                copyId = it.copyId ?? "",
                saleId = it.kind == CartKind.BuyBack ? it.saleId : 0,
                bagIndex = it.kind == CartKind.Sell && c != null ? c.Bag.IndexOf(it.shown) : -1,
                count = it.count,
            };
        }
        return lines;
    }

    // ---- drawing the totals, the purse and the buttons -------------------------------------------------------------

    void DrawSums()
    {
        if (root == null) return;
        bool show = quoted && cart.Count > 0;
        bool refused = show && !string.IsNullOrEmpty(quote.refusal);
        // A REFUSED CART SHOWS WHY in place of its totals; an empty or unpriced one shows the words and no numbers.
        Write(sumNumbers[0], show && !refused ? Wallet.Amount(quote.buying) : "");
        Write(sumNumbers[1], show && !refused ? Wallet.Amount(quote.selling) : "");
        Write(sumNumbers[2], show && !refused ? Wallet.Amount(quote.tax) : "");
        Write(sumNumbers[3], show && !refused ? Wallet.Amount(quote.net < 0 ? -quote.net : quote.net) : "");
        Write(sumWords[3], show && !refused && quote.net < 0 ? "You get" : "You pay");
        for (int i = 0; i < 4; i++)
            if (sumWords[i].gameObject.activeSelf == refused) sumWords[i].gameObject.SetActive(!refused);
        Write(refusal, refused ? quote.refusal : "");
        confirm.interactable = show && !refused && !trading;
        cancel.interactable = cart.Count > 0 && !trading;

        // THE GUIDE MAY LIGHT CONFIRM only while the cart holds something to buy — the item named is the first one.
        var marker = confirm.GetComponent<GuideMarker>();
        string buying = null;
        for (int i = 0; i < cart.Count && buying == null; i++) if (cart[i].kind == CartKind.Buy) buying = cart[i].itemId;
        if (buying != null) GuideMarker.Put(confirm.gameObject, GuideSpot.ShopConfirm, buying);
        else if (marker != null) marker.spot = GuideSpot.None;
        DrawPurse();
    }

    void DrawPurse()
    {
        if (purse == null) return;
        var c = Character.Instance;
        Write(purse, "<color=" + UiKit.Hex(UiKit.TextDim) + ">You have</color> " + Wallet.Amount(c != null ? c.Gold : 0));
    }

    static void Write(TMP_Text t, string s)
    {
        if (t != null && t.text != s) t.text = s;
    }

    // ---- the three lists -------------------------------------------------------------------------------------------

    // A row's price, whole: "1,250,000u".
    static string Price(long n) => Wallet.Amount(n);

    sealed class ShopList : UiListView.ISource, UiListView.IItemSource
    {
        readonly ShopPanel p;
        public ShopList(ShopPanel owner) { p = owner; }
        public int Count => p.ShopRows;
        public float HeightOf(int index) => UiListView.RowHeight;
        public void Bind(int index, UiListView.Slot slot)
        {
            if (index < p.stock.Count)
            {
                var e = p.stock[index];
                int free = QuestStore.FreeBuys(e.def.id);
                slot.ShowItem(e, "", null, UiKit.TextDim, free > 0 ? free + " free" : null, Price(p.stockPrice[index]));
                p.MarkRow(slot, GuideSpot.ShopItemRow, e.def.id);
                return;
            }
            int b = index - p.stock.Count;
            var k = p.backs[b];
            double left = WorldClock.SecondsUntil(WorldClock.FromEpochSeconds(k.endsAt));
            slot.ShowItem(p.backShown[b], k.instance != null ? "" : "×" + k.count, null, UiKit.TextDim,
                          "Buy back · " + ShopMath.MinutesShown(left) + "m", Price(k.price));
            p.MarkRow(slot, GuideSpot.None, "");
        }
        public void Press(int index, int which) { }
        public void ItemClicked(int index, PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (Hotkeys.ShiftHeld) { ChatController.InsertItemLink(index < p.stock.Count ? p.stock[index] : p.backShown[index - p.stock.Count]); return; }
            if (e.clickCount >= 2) p.AddFromShop(index, e.position);
        }
    }

    sealed class CartList : UiListView.ISource, UiListView.IItemSource
    {
        readonly ShopPanel p;
        public CartList(ShopPanel owner) { p = owner; }
        public int Count => p.cart.Count;
        public float HeightOf(int index) => UiListView.RowHeight;
        public void Bind(int index, UiListView.Slot slot)
        {
            var it = p.cart[index];
            bool selling = it.kind == CartKind.Sell;
            it.row.count = it.count;
            slot.ShowItem(it.row, it.row.instance != null ? "" : "×" + it.count,
                          selling ? "Sell" : it.kind == CartKind.BuyBack ? "Buy back" : "Buy",
                          selling ? UiKit.Bad : UiKit.Good, null, Price(it.price));
            p.MarkRow(slot, GuideSpot.None, "");
        }
        public void Press(int index, int which) { }
        public void ItemClicked(int index, PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && e.clickCount >= 2) p.TakeOut(index);
        }
    }

    sealed class BagList : UiListView.ISource, UiListView.IItemSource
    {
        readonly ShopPanel p;
        public BagList(ShopPanel owner) { p = owner; }
        List<BagEntry> Bag => p.bagShown;
        public int Count => p.bagShown.Count;
        public float HeightOf(int index) => UiListView.RowHeight;
        public void Bind(int index, UiListView.Slot slot)
        {
            var e = Bag[index];
            slot.ShowItem(e, e.instance != null ? "" : "×" + e.count, null, UiKit.TextDim, null,
                          e.def != null && e.def.CanBeSold ? Price(e.def.value) : "");
            p.MarkRow(slot, GuideSpot.None, "");
        }
        public void Press(int index, int which) { }
        public void ItemClicked(int index, PointerEventData e)
        {
            var bag = Bag;
            if (bag == null || index >= bag.Count || e.button != PointerEventData.InputButton.Left) return;
            if (Hotkeys.ShiftHeld) { ChatController.InsertItemLink(bag[index]); return; }
            if (e.clickCount >= 2) p.AddFromBag(bag[index], e.position);
        }
    }

    // A reused row is told every time what the guide may light on it — or that it is nothing, now.
    void MarkRow(UiListView.Slot slot, GuideSpot spot, string itemId) => slot.MarkForGuide(spot, itemId);

    // ---- drops: a column takes what is let go over it --------------------------------------------------------------

    /// <summary>What a drag let go over one of the three columns means: from the shop into the cart or the bag = buy;
    /// from the bag into the cart or the shop = sell; from the cart into either side = take it back out.</summary>
    internal void Dropped(ColumnDrop.Into into, ItemSlot from, Vector2 at)
    {
        var entry = from != null ? from.boundEntry : null;
        if (entry == null) return;
        int fromShop = stock.IndexOf(entry);
        if (fromShop < 0) { int b = backShown.IndexOf(entry); if (b >= 0) fromShop = stock.Count + b; }
        int fromCart = -1;
        for (int i = 0; i < cart.Count && fromCart < 0; i++) if (cart[i].row == entry) fromCart = i;

        if (fromCart >= 0) { if (into != ColumnDrop.Into.Cart) TakeOut(fromCart); return; }
        if (fromShop >= 0) { if (into != ColumnDrop.Into.Shop) AddFromShop(fromShop, at); return; }
        var c = Character.Instance;
        if (c != null && c.Bag.Contains(entry) && into != ColumnDrop.Into.Bag) AddFromBag(entry, at);
    }

    /// <summary>One column of the shop window as somewhere to let a dragged row go.</summary>
    public sealed class ColumnDrop : MonoBehaviour, IItemDropTarget
    {
        public enum Into { Shop, Cart, Bag }
        internal ShopPanel panel;
        internal Into into;
        internal Image edge;

        public bool TakeDrop(ItemSlot from, PointerEventData e)
        {
            if (panel == null) return false;
            panel.Dropped(into, from, e.position);
            return true;
        }
    }
}
