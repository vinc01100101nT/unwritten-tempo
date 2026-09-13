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
    public int[] rarities;
    public string[] skills;
    public string[] rolls;

    /// <summary>Which of these were picked up since the person last looked. Rebuilt on arrival, so without
    /// carrying it the "new" marker could never appear on anything.</summary>
    public bool[] isNew;

    /// <summary>The purse rides along, because it changes at the same moments the bag does — one drop pays out
    /// coins or an item — and two messages for one event is two chances for a screen to show half of it.</summary>
    public int gold;

    // ---- and what they are WEARING --------------------------------------------------------------------
    //
    // It travels with the bag because it MOVES with the bag: equipping takes an item out of one and puts it in
    // the other, in a single decision, on the server. Sending only half of that left the item gone from the bag
    // and nowhere else — vanished, as far as the person could see — and taking something off showed it in the
    // bag while still drawn on the body: one item in two places.
    //
    // The Ring vessel is left out, exactly as the save leaves it out: it is re-worn on its own and is never a
    // bag item, so carrying it would put a second one in somebody's hands.

    public int[] wornSlots;
    public string[] wornItemIds;
    public int[] wornRefines;
    public string[] wornCards;
    public int[] wornRarities;
    public string[] wornSkills;
    public string[] wornRolls;

    // ---- and the BAR the server just rewrote ----------------------------------------------------------
    //
    // Changing weapons replaces somebody's whole bar and clears the passives they had ticked, and the server is
    // where that is decided. It used to be decided there and nowhere else: the new weapon appeared on the body
    // while the keys kept the old weapon's skills, so pressing one asked for a skill the server no longer agreed
    // was theirs. Sending WHAT THE SERVER DECIDED, rather than having the other machine work the same rule out a
    // second time, is what keeps one answer to "what is on my bar".
    //
    // BOTH ARE NULL ON AN ORDINARY BAG CHANGE — picking something up, drinking a potion, tidying the bag — and
    // are filled in only by a push that follows a weapon going on or coming off. That is deliberate: a bar sent
    // with every bag change would undo an arrangement the player made a moment earlier, if the two crossed in
    // flight. A null array costs one byte and arrives as null, so "the server did not touch your bar" and "your
    // bar is empty" cannot be mistaken for each other.
    //
    // Same arrays as the save, like everything above.

    public string[] hotbarBindings;
    public string[] activePassives;

    /// <summary>Read a character's belongings off it, ready to send. Pass <paramref name="alsoTheBar"/> when the
    /// server has just rewritten this person's hotbar — see the note above; on every other change it is left out
    /// so an arrangement the player has just made cannot be undone by a message that crossed it.</summary>
    public static BagWire From(Character c, bool alsoTheBar = false)
    {
        var wire = new BagWire { gold = c != null ? c.Gold : 0 };
        if (c == null) { wire.itemIds = new string[0]; return wire; }

        c.CaptureWornInto(out wire.wornSlots, out wire.wornItemIds, out wire.wornRefines,
                          out wire.wornCards, out wire.wornRarities, out wire.wornSkills, out wire.wornRolls);

        if (alsoTheBar)
        {
            wire.hotbarBindings = c.Hotbar.Capture();
            wire.activePassives = c.PassiveSkills.Capture();
        }

        var bag = c.Bag;
        int n = 0;
        for (int i = 0; i < bag.Count; i++)
            if (bag[i] != null && bag[i].def != null && bag[i].def.slot != EquipSlotType.Ring) n++;

        wire.itemIds = new string[n];
        wire.counts = new int[n];
        wire.refines = new int[n];
        wire.cards = new string[n];
        wire.rarities = new int[n];
        wire.skills = new string[n];
        wire.rolls = new string[n];
        wire.isNew = new bool[n];

        int at = 0;
        for (int i = 0; i < bag.Count; i++)
        {
            var e = bag[i];
            if (e == null || e.def == null) continue;
            // The Ring vessel is worn automatically and is never a bag item. The save drops it for that reason
            // and so does this: carried across, it would be shown in the bag AND re-worn, which is one ring
            // becoming two.
            if (e.def.slot == EquipSlotType.Ring) continue;
            wire.itemIds[at] = e.def.id;
            wire.counts[at] = Mathf.Max(1, e.count);
            wire.isNew[at] = e.isNew;
            if (e.IsEquip && e.instance != null)
            {
                wire.refines[at] = e.instance.upgrade;
                wire.cards[at] = ItemCatalog.EncodeCards(e.instance.cards);
                wire.rarities[at] = (int)e.instance.rarity;
                wire.skills[at] = e.instance.EncodeSkills();
                wire.rolls[at] = e.instance.EncodeRoll();
            }
            else
            {
                wire.refines[at] = 0;
                wire.cards[at] = "";
                wire.rarities[at] = (int)e.def.rarity;
                wire.skills[at] = "";
                wire.rolls[at] = "";
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
                    entry.instance = new EquipmentInstance(def, At(refines, i, 0));
                    ItemCatalog.DecodeCards(At(cards, i, ""), entry.instance.cards);
                    entry.instance.rarity = (Rarity)At(rarities, i, (int)def.rarity);
                    entry.instance.DecodeSkills(At(skills, i, ""));
                    entry.instance.DecodeRoll(At(rolls, i, ""));
                }
                c.Bag.Add(entry);
            }
        }

        c.RestoreWornFrom(wornSlots, wornItemIds, wornRefines, wornCards, wornRarities, wornSkills, wornRolls);
        c.SetGold(gold);

        // THE BAR LAST, AFTER THE GEAR.
        // What a character can cast is built from the bar AND the weapon they are holding, so rebuilding it
        // before the new weapon was on would build it against the old one.
        if (hotbarBindings != null)
        {
            c.Hotbar.Restore(hotbarBindings);
            c.PassiveSkills.Restore(activePassives);
        }

        // AND ON EVERY BAG THAT ARRIVES, not only one that brought a bar with it.
        // What this character can use is built from the bag as well, so a potion picked up mid-session used to
        // land here with nothing behind it to run its effect: drinking it was refused on this machine, by name.
        // The server rebuilds on every change it makes to a bag; this is that same rule at the other end.
        // The bar also redraws from inside this, which is what the weapon-swap push above relies on.
        c.RefreshUsableSkills();
    }

    static T At<T>(IList<T> a, int i, T fallback) => a != null && i < a.Count ? a[i] : fallback;
}
