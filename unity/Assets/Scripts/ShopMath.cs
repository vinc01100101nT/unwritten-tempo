/// <summary>
/// THE ARITHMETIC OF A SHOP — what a sale pays, what a purchase costs, and what getting a sold thing back costs.
/// Plain numbers in, plain numbers out, so every rule here is checked by calling it (tools/check-game-math.sh).
///
/// <para><b>One price per item, and Tax.</b> An item has ONE value (<see cref="Item.value"/>). A shop sells one for
/// its value plus <see cref="TaxEach"/>, and buys one for its value less it — a third of the value, rounded up, taken
/// on EACH unit, on each side of a trade separately. Per unit, so a hundred sold pay exactly a hundred times one;
/// per side, so a cart that buys and sells the same amount still pays its Tax on both.</para>
///
/// <para><b>Buying back costs more than the sale paid, always.</b> A shop keeps what you sold for
/// <see cref="BuybackSeconds"/>, and the price to have it back is what it paid you plus Tax on that — the same Tax as
/// every other trade, a third of each unit's amount, rounded up — and never less than one more than it paid,
/// rounded UP and never less than one gold more — so a sold thing is never a free place to keep it.</para>
/// </summary>
public static class ShopMath
{
    /// <summary>How long a sold thing can still be bought back, in seconds.</summary>
    public const double BuybackSeconds = 3600;

    /// <summary>How many different sold things are kept at once. A stack is one, however many it holds.</summary>
    public const int BuybackMostEntries = 10;

    /// <summary>THE TAX on one unit of a thing worth <paramref name="value"/>: a third of it, rounded up.</summary>
    public static int TaxEach(int value) => value <= 0 ? 0 : (int)(((long)value + 2) / 3);

    /// <summary>The Tax on <paramref name="count"/> of a thing worth <paramref name="value"/>, bought or sold.</summary>
    public static long TaxOn(int value, int count) => (long)TaxEach(value) * System.Math.Max(0, count);

    /// <summary>What a shop pays for <paramref name="count"/> of a thing worth <paramref name="value"/>: its value less
    /// the Tax, per unit.</summary>
    public static long SalePay(int value, int count) =>
        (long)System.Math.Max(0, value - TaxEach(value)) * System.Math.Max(0, count);

    /// <summary>What <paramref name="paidCount"/> of a thing worth <paramref name="value"/> costs: its value and the
    /// Tax, per unit.</summary>
    public static long BuyCost(int value, int paidCount) =>
        ((long)System.Math.Max(0, value) + TaxEach(value)) * System.Math.Max(0, paidCount);

    /// <summary>The value that makes a sale of one pay exactly <paramref name="sellPrice"/> — how a price written as
    /// "what a shop pays" becomes an item's value: the price and half of it again, rounded up.</summary>
    public static int ValueForSellPrice(int sellPrice) => sellPrice <= 0 ? 0 : sellPrice + (sellPrice + 1) / 2;

    /// <summary>What it costs to buy back <paramref name="count"/> of a thing the shop paid <paramref name="paid"/> for
    /// in all: what it paid, plus Tax on each unit's share of it (<see cref="TaxEach"/>) — and at least one more than it
    /// paid. One kind of thing sells for the same each, so the shares are even; any odd remainder is taxed once.</summary>
    public static long BuybackPrice(long paid, int count)
    {
        if (paid < 0) paid = 0;
        if (count < 1) count = 1;
        long each = paid / count, left = paid % count;
        long tax = count * TaxOn(each) + TaxOn(left);
        long price = paid > long.MaxValue - tax ? long.MaxValue : paid + tax;
        return System.Math.Max(price, paid + 1);
    }

    // The Tax on one amount of any size — a third, rounded up.
    static long TaxOn(long amount) => amount <= 0 ? 0 : amount / 3 + (amount % 3 == 0 ? 0 : 1);

    /// <summary>The moment a thing sold at <paramref name="soldAtEpoch"/> stops being kept, in world seconds.</summary>
    public static double BuybackEndsAt(double soldAtEpoch) => soldAtEpoch + BuybackSeconds;

    /// <summary>Whole minutes shown for <paramref name="secondsLeft"/>: any part of a minute counts as one, so the
    /// last line reads "1 min" until it is gone. 0 once nothing is left.</summary>
    public static int MinutesShown(double secondsLeft) =>
        secondsLeft <= 0 ? 0 : (int)System.Math.Ceiling(secondsLeft / 60.0);

    /// <summary>Seconds until the minutes shown for <paramref name="secondsLeft"/> next change — the moment a window
    /// showing it must be redrawn. 0 once nothing is left.</summary>
    public static double SecondsUntilMinuteChanges(double secondsLeft)
    {
        if (secondsLeft <= 0) return 0;
        double toNext = secondsLeft - (MinutesShown(secondsLeft) - 1) * 60.0;
        return toNext > 0 ? toNext : 0;
    }
}
