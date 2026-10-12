using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A character's belongings, in the shape they travel in.
///
/// <para><b>Why a snapshot of the whole bag rather than "one item was added".</b> A bag is small — tens of
/// stacks — and it changes when somebody picks something up, which is not often. Sending the whole thing means
/// the two copies cannot drift: there is no sequence of messages that could arrive out of order, no message
/// whose loss leaves the two disagreeing forever, and nothing to reconcile. The alternative buys a few bytes
/// and costs a class of bug that is invisible until somebody's items are wrong.</para>
///
/// <para><b>The arrays are the SAVE's arrays, deliberately.</b> Exactly the same parallel lists, meaning the
/// same things, because a second shape for one thing is a second thing to keep in step. What goes over the
/// wire is what gets written down.</para>
/// </summary>
public struct BagWire
{
    public string[] itemIds;
    public int[] counts;
    public int[] refines;
    public string[] cards;
    public string[] rolls;
    /// <summary>Each piece of gear's own id ("" for a stack) — what a hotbar slot names to mean exactly that piece.</summary>
    public string[] copyIds;

    /// <summary>How rare each one is, as the server decided it (<see cref="Rarities.Of"/>) — sent, because a weapon's
    /// rarity is read off its trademark skill, and a player's machine must not answer that from its own copy of the
    /// skills. Parallel to <see cref="itemIds"/>; <see cref="wornRarities"/> is the same for what is worn.</summary>
    public int[] rarities;
    public int[] wornRarities;

    /// <summary>Which of these were picked up since the person last looked. Rebuilt on arrival, so without
    /// carrying it the "new" marker could never appear on anything.</summary>
    public bool[] isNew;

    /// <summary>The purse rides along, because it changes at the same moments the bag does — one drop pays out
    /// coins or an item — and two messages for one event is two chances for a screen to show half of it.</summary>
    public int gold;

    /// <summary>How much the bag holds and how much is in it, worked out by the server (<see cref="BagRoom"/>): what is
    /// carried, the most that can be, and the entries in each tab (indexed by <see cref="ItemCategory"/>). Sent, so the
    /// bag window only copies them onto the screen.</summary>
    public int weight;
    public int weightCap;
    public int[] slotsUsed;

    // ---- and what they are WEARING --------------------------------------------------------------------
    //
    // It travels with the bag because it MOVES with the bag: equipping takes an item out of one and puts it in
    // the other, in a single decision, on the server. Sending only half of that left the item gone from the bag
    // and nowhere else — vanished, as far as the person could see — and taking something off showed it in the
    // bag while still drawn on the body: one item in two places.

    public int[] wornSlots;
    public string[] wornItemIds;
    public int[] wornRefines;
    public string[] wornCards;
    public string[] wornRolls;
    public string[] wornCopyIds;

    // ---- and the BAR, when the server changed it --------------------------------------------------------
    //
    // The server changes somebody's bar in three cases: it arrives with them on a map server, an item on it is used up,
    // or a piece of gear on it is no longer theirs (both are taken off the bar on the same push that tells them). Sending
    // WHAT THE SERVER DECIDED, rather than having the other machine work the same rule out a second time, is what keeps
    // one answer to "what is on my bar".
    //
    // IT IS NULL ON EVERY OTHER BAG CHANGE — picking something up, drinking a potion that leaves some behind, tidying
    // the bag. That is deliberate: a bar sent with every bag change would undo an arrangement the player made a moment
    // earlier, if the two crossed in flight. A null array costs one byte and arrives as null, so "the server did not
    // touch your bar" and "your bar is empty" cannot be mistaken for each other.

    public string[] hotbarBindings;

    // ---- and WHAT THEY CAN BUY BACK, when the server changed it -----------------------------------------
    //
    // Sent on arrival and whenever a sale or a buyback changes the list, each line with its finished price. NULL on
    // every other bag change for the hotbar's reason above: "the list did not change" and "the list is empty" must not
    // be mistaken for each other, and ten pieces of gear need not ride every potion picked up.

    public SoldLine[] buyback;

    /// <summary>Read a character's belongings off it, ready to send. Pass <paramref name="alsoTheBar"/> when the
    /// server has just rewritten this person's hotbar — see the note above; on every other change it is left out
    /// so an arrangement the player has just made cannot be undone by a message that crossed it.</summary>
    public static BagWire From(Character c, bool alsoTheBar = false, bool alsoTheBuyback = false)
    {
        var wire = new BagWire { gold = c != null ? c.Gold : 0 };
        if (c == null) { wire.itemIds = new string[0]; return wire; }

        wire.weight = c.CarriedWeight;
        wire.weightCap = c.WeightCap;
        wire.slotsUsed = new int[TabCount];
        for (int t = 0; t < TabCount; t++) wire.slotsUsed[t] = c.SlotsUsed((ItemCategory)t);

        c.CaptureWornInto(out wire.wornSlots, out wire.wornItemIds, out wire.wornRefines,
                          out wire.wornCards, out wire.wornRolls, out wire.wornCopyIds);
        wire.wornRarities = new int[wire.wornCopyIds != null ? wire.wornCopyIds.Length : 0];
        foreach (var worn in c.Gear.Worn)
        {
            int wornAt = System.Array.IndexOf(wire.wornCopyIds, worn.Value.copyId);
            if (wornAt >= 0) wire.wornRarities[wornAt] = (int)Rarities.Of(worn.Value.def, worn.Value);
        }

        if (alsoTheBar)
        {
            wire.hotbarBindings = c.Hotbar.Capture();
        }

        if (alsoTheBuyback) wire.buyback = c.Buyback.ToWire(WorldClock.EpochSecondsNow);

        var bag = c.Bag;
        int n = 0;
        for (int i = 0; i < bag.Count; i++)
            if (bag[i] != null && bag[i].def != null) n++;

        wire.itemIds = new string[n];
        wire.counts = new int[n];
        wire.refines = new int[n];
        wire.cards = new string[n];
        wire.rolls = new string[n];
        wire.copyIds = new string[n];
        wire.rarities = new int[n];
        wire.isNew = new bool[n];

        int at = 0;
        for (int i = 0; i < bag.Count; i++)
        {
            var e = bag[i];
            if (e == null || e.def == null) continue;
            wire.itemIds[at] = e.def.id;
            wire.counts[at] = Mathf.Max(1, e.count);
            wire.isNew[at] = e.isNew;
            wire.rarities[at] = (int)Rarities.Of(e.def, e.instance);
            if (e.IsEquip && e.instance != null)
            {
                e.instance.ToSaved(out wire.refines[at], out wire.cards[at], out wire.rolls[at], out wire.copyIds[at]);
            }
            else
            {
                wire.refines[at] = 0;
                wire.cards[at] = "";
                wire.rolls[at] = "";
                wire.copyIds[at] = "";
            }
            at++;
        }
        return wire;
    }

    /// <summary>Lay these belongings onto a character, replacing whatever it was holding.
    ///
    /// <para>An item this copy of the game has never heard of is skipped rather than refused, the same way a
    /// save does it — an asset removed from the game must not stop somebody playing.</para></summary>
    public void Onto(Character c)
    {
        if (c == null) return;

        c.Bag.Clear();
        if (itemIds != null)
        {
            for (int i = 0; i < itemIds.Length; i++)
            {
                var def = ItemCatalog.Resolve(itemIds[i]);
                if (def == null) continue;

                var entry = new BagEntry { def = def, count = Mathf.Max(1, At(counts, i, 1)),
                                           isNew = At(isNew, i, false) };
                if (def.slot != EquipSlotType.None)
                {
                    entry.count = 1;
                    entry.instance = EquipmentInstance.FromSaved(def, At(refines, i, 0), At(cards, i, ""),
                                                                 At(rolls, i, "") ?? "", At(copyIds, i, ""));
                    TakeRarity(entry.instance, rarities, i);
                }
                c.Bag.Add(entry);
            }
        }

        c.RestoreWornFrom(wornSlots, wornItemIds, wornRefines, wornCards, wornRolls, wornCopyIds);
        if (wornCopyIds != null)
            foreach (var worn in c.Gear.Worn)
                TakeRarity(worn.Value, wornRarities, System.Array.IndexOf(wornCopyIds, worn.Value.copyId));
        c.SetGold(gold);
        c.TakeToldBagRoom(weight, weightCap, slotsUsed);
        if (buyback != null) c.Buyback.TakeFromServer(buyback);

        // THE BAR LAST, AFTER THE GEAR.
        // What a character can cast is built from the bar AND the weapon they are holding, so rebuilding it
        // before the new weapon was on would build it against the old one.
        if (hotbarBindings != null)
        {
            c.Hotbar.Restore(hotbarBindings);
        }

        // AND ON EVERY BAG THAT ARRIVES, not only one that brought a bar with it.
        // What this character can use is built from the bag as well, so a potion picked up mid-session used to
        // land here with nothing behind it to run its effect: drinking it was refused on this machine, by name.
        // The server rebuilds on every change it makes to a bag; this is that same rule at the other end.
        // The bar also redraws from inside this, which is what the weapon-swap push above relies on.
        c.RefreshUsableSkills();
    }

    static readonly int TabCount = System.Enum.GetValues(typeof(ItemCategory)).Length;

    static T At<T>(IList<T> a, int i, T fallback) => a != null && i < a.Count ? a[i] : fallback;

    // The rarity the server sent for one piece, laid on it. A message from a build that sent none leaves it unsent, and
    // the piece falls back to reading its own data.
    static void TakeRarity(EquipmentInstance piece, int[] sent, int i)
    {
        if (piece == null || sent == null || i < 0 || i >= sent.Length) return;
        int r = sent[i];
        if (r < (int)Rarity.Common || r > (int)Rarity.Legendary) return;
        piece.rarity = (Rarity)r;
        piece.raritySent = true;
    }
}
