/// <summary>Why a thing could not go into a bag. None = it fits.</summary>
public enum BagRefusal { None, SlotsFull, TooHeavy }

/// <summary>
/// HOW MUCH A BAG HOLDS — the two limits, and the one judgement of whether a thing fits. Pure numbers in, an answer
/// out, so it can be checked without the game (tools/check-game-math.sh).
///
/// <para><b>Slots</b>: each bag tab (<see cref="ItemCategory"/>) holds <see cref="SlotsPerCategory"/> entries. A stack is
/// one entry however many it holds; every piece of gear is its own entry. Worn gear counts in its tab, as Ragnarok counts
/// what you wear in your inventory, so putting a piece on or taking it off never changes the count.</para>
///
/// <para><b>Weight</b>: Ragnarok Online's carry limit, 2000 + 30 per point of STR. Its class bonus is left out — there
/// are no classes. Worn gear counts here too, for the same reason.</para>
///
/// <para>A thing that would go past either limit is REFUSED where it would arrive, with the reason said. It is never
/// put in and hidden.</para>
/// </summary>
public static class BagRoom
{
    public const int SlotsPerCategory = 50;
    public const int BaseCarryWeight = 2000;
    public const int CarryWeightPerStr = 30;

    public static int WeightCap(int str) => BaseCarryWeight + CarryWeightPerStr * (str > 0 ? str : 0);

    /// <summary>How many new entries <paramref name="count"/> of a thing takes: one per piece of gear, one for a stack
    /// not yet held, none for a stack already held.</summary>
    public static int SlotsNeeded(bool isGear, int count, bool alreadyHeld)
        => isGear ? (count > 0 ? count : 0) : (alreadyHeld || count <= 0 ? 0 : 1);

    /// <summary>Does it fit? <paramref name="slotsUsed"/> is what its tab holds now, <paramref name="weightNow"/> what is
    /// carried now. A thing that adds no entry is never refused for slots, and one that adds no weight never for weight —
    /// so a tab already over its limit (a save from before the limit) still takes more of a stack it holds.</summary>
    public static BagRefusal Judge(int slotsUsed, int slotsNeeded, int weightNow, int weightAdded, int weightCap)
    {
        if (slotsNeeded > 0 && slotsUsed + slotsNeeded > SlotsPerCategory) return BagRefusal.SlotsFull;
        if (weightAdded > 0 && weightNow + weightAdded > weightCap) return BagRefusal.TooHeavy;
        return BagRefusal.None;
    }
}
