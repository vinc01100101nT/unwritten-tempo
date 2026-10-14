using System.Collections.Generic;

/// <summary>What one line of a shop cart asks for. Buy and BuyBack take; Sell gives.</summary>
public enum CartKind : byte { Buy = 0, Sell = 1, BuyBack = 2 }

/// <summary>
/// ONE LINE OF A SHOP CART, as the player's machine sends it — an INTENT, never an amount of money. Which line kind,
/// which item, and for a sale the bag position and (for gear) the very piece. Whatever it carries is checked again on
/// the server; nothing in it is trusted.
/// </summary>
[System.Serializable]
public struct CartLine
{
    public byte kind;
    public string itemId;
    /// <summary>The piece's own id — a sold piece of gear. Empty otherwise.</summary>
    public string copyId;
    /// <summary>Which sale a BuyBack line buys back (<see cref="SoldEntry.id"/>). Ignored on Buy and Sell lines.</summary>
    public int saleId;
    /// <summary>The bag position of a sold stack or piece. Ignored on Buy and BuyBack lines.</summary>
    public int bagIndex;
    /// <summary>How many — to buy, or to sell from that position. Ignored on a BuyBack line: the whole entry comes back.</summary>
    public int count;
}

/// <summary>THE SERVER'S TOTALS for a cart, finished — what the shop window draws under the cart and nothing else.
/// <see cref="refusal"/> is empty when the cart could be carried out as it stands, and otherwise says why not.</summary>
public struct CartQuote
{
    public int version;
    /// <summary>The values of everything bought (free tutorial ones left out) plus what the buy-backs cost.</summary>
    public long buying;
    /// <summary>The values of everything sold.</summary>
    public long selling;
    /// <summary>The Tax on both sides, per unit (<see cref="ShopMath.TaxOn"/>). Never on a buy-back.</summary>
    public long tax;
    /// <summary>What changes hands: positive, the player pays it; negative, the player is paid it.</summary>
    public long net;
    public string refusal;
}

/// <summary>
/// THE ONE CHECK OF A SHOP CART — server-side, used by the quote and by the trade alike, so what a player is quoted and
/// what the trade carries out can never be judged two ways. It reads the character and the shop and writes a
/// <see cref="Plan"/>; it changes nothing.
///
/// <para>Every way a cart could duplicate or lose something is refused here: a bag position or piece in two lines, a count
/// above the stack it names, the same buy-back entry twice, an item bought on two lines (its tutorial free buys would count twice), and a bag
/// that would not hold what comes in once what goes out has left it.</para>
/// </summary>
public static class ShopCart
{
    /// <summary>The most lines one cart may hold — a guard on what a player's machine sends.</summary>
    public const int MostLines = 50;
    /// <summary>The most of one thing one line may buy — the same guard the "how many?" box holds to.</summary>
    public const int MostInOneLine = DropAmountPrompt.MostEverTyped;

    public struct BuyLine { public Item def; public int count, free; }
    public struct SellLine { public int bagIndex; public BagEntry entry; public int count; public long pay; }

    /// <summary>A checked cart, ready to carry out. Reused: the server holds one per check, and a check never runs
    /// inside another.</summary>
    public sealed class Plan
    {
        public readonly List<BuyLine> buys = new List<BuyLine>(8);
        public readonly List<SellLine> sells = new List<SellLine>(8);
        public readonly List<SoldEntry> backs = new List<SoldEntry>(4);
        public long buying, selling, tax, net, buyTax, sellTax, backsCost;

        public void Clear()
        {
            buys.Clear(); sells.Clear(); backs.Clear();
            buying = selling = tax = net = buyTax = sellTax = backsCost = 0;
        }
    }

    static readonly Plan scratch = new Plan();
    static readonly HashSet<int> positions = new HashSet<int>();
    static readonly HashSet<string> pieces = new HashSet<string>();
    static readonly Dictionary<ItemCategory, int> slots = new Dictionary<ItemCategory, int>();
    static readonly HashSet<Item> heldAfter = new HashSet<Item>();

    /// <summary>Check <paramref name="lines"/> for <paramref name="c"/> at <paramref name="shop"/>. Returns null and
    /// fills <paramref name="plan"/> when the whole cart can be carried out; otherwise the sentence saying why not.
    /// <paramref name="nowEpoch"/> ends buy-back entries that have run out first.</summary>
    public static string Check(Character c, WorldShops.Row shop, CartLine[] lines, double nowEpoch, out Plan plan)
    {
        plan = scratch;
        plan.Clear();
        positions.Clear(); pieces.Clear();
        if (c == null || shop == null) return "You are not at that shop.";
        if (lines == null || lines.Length == 0) return "The cart is empty.";
        if (lines.Length > MostLines) return "That is more than one cart holds (" + MostLines + " lines).";

        c.Buyback.DropEnded(nowEpoch);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            string why;
            switch (line.kind)
            {
                case (byte)CartKind.Buy:     why = CheckBuy(c, shop, line, plan); break;
                case (byte)CartKind.Sell:    why = CheckSell(c, shop, line, plan); break;
                case (byte)CartKind.BuyBack: why = CheckBuyBack(c, line, plan); break;
                default:                     why = "That cart has a line no shop knows."; break;
            }
            if (why != null) return why;
        }
        Totals(plan);
        if (plan.net > c.Gold) return "Needs " + Wallet.Amount(plan.net) + " — you have " + Wallet.Amount(c.Gold) + ".";
        return Room(c, plan);
    }

    static string CheckBuy(Character c, WorldShops.Row shop, CartLine line, Plan plan)
    {
        var def = ItemCatalog.Resolve(line.itemId);
        if (def == null || !WorldShops.Sells(shop, def)) return (def != null ? def.displayName : "That") + " is not for sale here.";
        if (line.count < 1 || line.count > MostInOneLine) return "Buy between 1 and " + MostInOneLine + " " + def.displayName + ".";
        for (int i = 0; i < plan.buys.Count; i++)
            if (plan.buys[i].def == def) return def.displayName + " is in the cart twice.";
        // A TUTORIAL'S FIRST RUN TAKES NOTHING it has you buy: the free ones are counted first, once for the item.
        int free = System.Math.Min(line.count, c.Quests.FreeBuysOf(def.id));
        plan.buys.Add(new BuyLine { def = def, count = line.count, free = free });
        return null;
    }

    static string CheckSell(Character c, WorldShops.Row shop, CartLine line, Plan plan)
    {
        int at = line.bagIndex;
        if (at < 0 || at >= c.Bag.Count) return "That is no longer in your bag.";
        var e = c.Bag[at];
        if (e == null || e.def == null || e.def.id != line.itemId) return "That is no longer in your bag.";
        // THE VERY PIECE THEY PUT IN THE CART, not another of its kind that slid into that position since.
        if (e.instance != null && (e.instance.copyId ?? "") != (line.copyId ?? "")) return "That is no longer in your bag.";
        if (!positions.Add(at)) return e.def.displayName + " is in the cart twice.";
        if (e.instance != null && !string.IsNullOrEmpty(e.instance.copyId) && !pieces.Add(e.instance.copyId))
            return e.def.displayName + " is in the cart twice.";
        int most = e.instance != null ? 1 : e.count;
        if (line.count < 1 || line.count > most)
            return "You have " + most + " " + e.def.displayName + " — sell between 1 and " + most + ".";
        if (!WorldShops.Buys(shop, e)) return "They will not buy " + e.def.displayName + ".";
        var owing = c.Quests.FirstRunOwing(e.def.id);
        if (owing != null) return "Your " + e.def.displayName + " is still needed for " + owing + " — finish it first.";
        plan.sells.Add(new SellLine { bagIndex = at, entry = e, count = line.count,
                                      pay = ShopMath.SalePay(e.def.value, line.count) });
        return null;
    }

    static string CheckBuyBack(Character c, CartLine line, Plan plan)
    {
        var kept = c.Buyback.Find(line.saleId);
        if (kept == null) return "That is no longer here to buy back.";
        if (plan.backs.Contains(kept)) return kept.def.displayName + " is in the cart twice.";
        plan.backs.Add(kept);
        return null;
    }

    /// <summary>Buying, Selling, Tax and what changes hands — per unit, per side, never on the difference.</summary>
    static void Totals(Plan plan)
    {
        for (int i = 0; i < plan.buys.Count; i++)
        {
            var b = plan.buys[i];
            int paid = b.count - b.free;
            plan.buying += (long)b.def.value * paid;
            plan.buyTax += ShopMath.TaxOn(b.def.value, paid);
        }
        for (int i = 0; i < plan.backs.Count; i++) plan.backsCost += ShopMath.BuybackPrice(plan.backs[i].paid, plan.backs[i].count);
        plan.buying += plan.backsCost;
        for (int i = 0; i < plan.sells.Count; i++)
        {
            var s = plan.sells[i];
            plan.selling += (long)s.entry.def.value * s.count;
            plan.sellTax += ShopMath.TaxOn(s.entry.def.value, s.count);
        }
        plan.tax = plan.buyTax + plan.sellTax;
        plan.net = plan.buying - plan.selling + plan.tax;
    }

    // WILL IT ALL FIT, once what is sold has left the bag: slots per tab and weight, and which stacks are still held —
    // a stack sold to its last one no longer holds a place for what is bought of it.
    static string Room(Character c, Plan plan)
    {
        slots.Clear();
        heldAfter.Clear();
        int weight = c.CarriedWeight, cap = c.WeightCap;
        for (int i = 0; i < plan.sells.Count; i++)
        {
            var s = plan.sells[i];
            var def = s.entry.def;
            weight -= def.weight * s.count;
            if (s.entry.instance != null || s.count >= s.entry.count) slots[def.category] = SlotsIn(c, def.category) - 1;
        }
        for (int i = 0; i < c.Bag.Count; i++)
        {
            var e = c.Bag[i];
            if (e == null || e.def == null || e.IsEquip) continue;
            if (FullySold(plan, i)) continue;
            heldAfter.Add(e.def);
        }

        for (int i = 0; i < plan.buys.Count; i++)
        {
            var why = Fits(c, plan.buys[i].def, plan.buys[i].count, ref weight, cap, "buy");
            if (why != null) return why;
        }
        for (int i = 0; i < plan.backs.Count; i++)
        {
            var k = plan.backs[i];
            var why = Fits(c, k.def, k.instance != null ? 1 : k.count, ref weight, cap, "buy back");
            if (why != null) return why;
        }
        return null;
    }

    static bool FullySold(Plan plan, int bagIndex)
    {
        for (int i = 0; i < plan.sells.Count; i++)
            if (plan.sells[i].bagIndex == bagIndex) return plan.sells[i].count >= plan.sells[i].entry.count;
        return false;
    }

    static int SlotsIn(Character c, ItemCategory tab) => slots.TryGetValue(tab, out int n) ? n : c.SlotsUsed(tab);

    static string Fits(Character c, Item def, int count, ref int weight, int cap, string verb)
    {
        bool gear = def.slot != EquipSlotType.None;
        int used = SlotsIn(c, def.category);
        int needed = BagRoom.SlotsNeeded(gear, count, !gear && heldAfter.Contains(def));
        var why = BagRoom.Judge(used, needed, weight, def.weight * count, cap);
        if (why == BagRefusal.SlotsFull)
            return "Can't " + verb + " " + def.displayName + " — that tab of your bag would be full (" + BagRoom.SlotsPerCategory + ").";
        if (why == BagRefusal.TooHeavy)
            return "Can't " + verb + " " + def.displayName + " — it would be too heavy to carry (" + cap + ").";
        slots[def.category] = used + needed;
        weight += def.weight * count;
        if (!gear) heldAfter.Add(def);
        return null;
    }
}
