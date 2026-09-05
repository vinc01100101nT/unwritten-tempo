using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT A KILL IS WORTH, and who gets it — the one door every reward passes through.
///
/// <para><b>Why it exists.</b> A kill used to pay "the player", meaning the one person at this keyboard, from a
/// listener attached to each creature as it was born. That answer is wrong in both directions the moment a world
/// holds more than one person: on a server it pays nobody, and in a town it would pay whoever happened to be
/// standing there rather than whoever did the work. Paying the killer is a decision, and a decision needs one
/// place to live.</para>
///
/// <para><b>The rule:</b> the last unit to land a blow that cost the creature health takes the reward. That is the
/// simplest thing a player can predict from what they can see, and it is what the damage door already knows —
/// nothing new is tracked to answer it.</para>
///
/// <para>On a server the person being paid is the fighter it holds for that connection, and their machine is told
/// so their own character records it (see <see cref="GameNetwork"/>). On a single machine it is simply themselves.</para>
///
/// <para><b>Experience and loot are paid to the SAME person, always</b>, because both ask <see cref="WhoEarned"/>
/// and there is no second answer anywhere.</para>
/// </summary>
public static class Rewards
{
    /// <summary>Pay <paramref name="killer"/> for a kill worth <paramref name="xp"/>. Does nothing when nobody
    /// landed the blow — a creature that fell to a poison nobody owns, or died as its world was torn down.</summary>
    public static void PayForKill(ICombatUnit killer, int xp)
    {
        // A creature can outlive the person who mortally wounded it — its death fade takes a moment, and they can
        // disconnect inside it. That leaves a reference that passes a plain null check and throws when read.
        if (PlayerUnits.Gone(killer) || xp <= 0) return;

        // Only a CHARACTER has a progression to pay into; a creature killing a creature earns nothing today.
        var character = killer as Character;
        if (character == null) return;

        character.AddXP(xp);
        // On a server the character being paid is not the one on that player's screen, so their machine has to be
        // told or the kill quietly earns them nothing. Does nothing anywhere else.
        GameNetwork.TellOwnerTheyEarned(character, xp);
    }

    /// <summary>WHO EARNED WHAT THIS UNIT WAS CARRYING — its experience and its loot both. Today the answer is the
    /// last unit to land a blow that cost it health, which is the simplest thing a player can predict from what
    /// they can see and costs nothing to track: the damage door already writes it down.
    ///
    /// <para>THE ONE PLACE THIS IS DECIDED. When a share of the work is worked out properly — how much damage each
    /// person did, who healed, who held it still — it is worked out HERE, and every payment follows it without
    /// another line changing.</para></summary>
    public static ICombatUnit WhoEarned(Health victim) => victim != null ? victim.LastAttacker : null;

    /// <summary>Pay <paramref name="winner"/> everything one kill was carrying: the items in
    /// <paramref name="items"/> and <paramref name="gold"/> Zeny, straight into their bag. Nothing is left lying
    /// in the world — there is no drop to walk to and nothing to click.
    ///
    /// <para>ONE CALL PER KILL, not one per thing dropped: on a server every payment hands the owner their whole
    /// bag again and asks for it to be written down, so paying four times over would send four bags and four
    /// writes for one dead creature.</para></summary>
    public static void PayLoot(ICombatUnit winner, List<Item> items, int gold)
    {
        if (PlayerUnits.Gone(winner)) return;

        // Only a CHARACTER has a bag and a purse; a creature killing a creature carries nothing away.
        var character = winner as Character;
        if (character == null) return;

        int given = 0;
        if (items != null)
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && character.TakeIntoBag(items[i], 1) != null) given++;

        if (gold > 0) character.AddGold(gold);
        if (given == 0 && gold <= 0) return;

        // On a server the bag that just changed is not the one on that player's screen, so their machine is handed
        // the whole thing and the change is written down. Off a server there is only one bag and the window that
        // shows it is told directly.
        if (!GameNetwork.TellOwnerTheirBagChanged(character)) InventoryStore.Instance?.Notify();

        if (GameLog.On(LogCat.Loot)) GameLog.Info(LogCat.Loot, Describe(character, items, gold));
    }

    /// <summary>What a kill paid, as one line: who got it, how much Zeny, and every item by name. Builds a string,
    /// so it is only ever called behind <see cref="GameLog.On"/>.</summary>
    static string Describe(Character who, List<Item> items, int gold)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(GameNetwork.NameOf(who)).Append(" earned ");

        bool wroteSomething = false;
        if (gold > 0) { sb.Append(gold).Append(" Zeny"); wroteSomething = true; }

        if (items != null)
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                if (wroteSomething) sb.Append(", ");
                sb.Append(items[i].displayName);
                wroteSomething = true;
            }

        return sb.ToString();
    }
}
