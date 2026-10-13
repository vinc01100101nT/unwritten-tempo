/// <summary>
/// THE ARITHMETIC OF A SHOP — what a sale pays, what a purchase costs, and what getting a sold thing back costs.
/// Plain numbers in, plain numbers out, so every rule here is checked by calling it (tools/check-game-math.sh).
///
/// <para><b>Buying back costs more than the sale paid, always.</b> A shop keeps what you sold for
/// <see cref="BuybackSeconds"/>, and the price to have it back is <see cref="BuybackPercent"/>% of what it paid you,
/// rounded UP and never less than one gold more — so a sold thing is never a free place to keep it.</para>
/// </summary>
public static class ShopMath
{
    /// <summary>What getting a sold thing back costs, as a percentage of what the shop paid for it.</summary>
    public const int BuybackPercent = 125;

    /// <summary>How long a sold thing can still be bought back, in seconds.</summary>
    public const double BuybackSeconds = 3600;

    /// <summary>How many different sold things are kept at once. A stack is one, however many it holds.</summary>
    public const int BuybackMostEntries = 10;

    /// <summary>What a shop pays for <paramref name="count"/> of a thing worth <paramref name="sellValueEach"/>.</summary>
    public static long SalePay(int sellValueEach, int count) =>
        (long)System.Math.Max(0, sellValueEach) * System.Math.Max(0, count);

    /// <summary>What <paramref name="paidCount"/> of a thing at <paramref name="priceEach"/> costs.</summary>
    public static long BuyCost(int priceEach, int paidCount) =>
        (long)System.Math.Max(0, priceEach) * System.Math.Max(0, paidCount);

    /// <summary>What it costs to buy back a sold thing the shop paid <paramref name="paid"/> for: rounded up, and at
    /// least one gold more than it paid.</summary>
    public static long BuybackPrice(long paid)
    {
        if (paid < 0) paid = 0;
        long price = (paid * BuybackPercent + 99) / 100;
        return System.Math.Max(price, paid + 1);
    }

    /// <summary>Two amounts paid for one kept stack, added — stopped at the most gold a purse can hold.</summary>
    public static long PaidTogether(long a, long b)
    {
        if (a < 0) a = 0;
        if (b < 0) b = 0;
        return a > long.MaxValue - b ? long.MaxValue : a + b;
    }

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
