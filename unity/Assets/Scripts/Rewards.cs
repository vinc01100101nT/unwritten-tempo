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
    public static void PayForKill(ICombatUnit killer, int xp, Health victim)
    {
        // A creature can outlive the person who mortally wounded it — its death fade takes a moment, and they can
        // disconnect inside it. That leaves a reference that passes a plain null check and throws when read.
        if (PlayerUnits.Gone(killer) || xp <= 0) return;

        // Only a CHARACTER has a progression to pay into; a creature killing a creature earns nothing today.
        var character = killer as Character;
        if (character == null) return;

        int levelsGained = character.AddXP(xp, out int ringLevelsGained);
        // "field_15 Alpha #1 (id) earned 40 experience from Snake #3" — beside the loot line, so what a kill paid is
        // all in the log and nothing about it needs a recorder.
        if (GameLog.On(LogCat.Loot))
            GameLog.Info(LogCat.Loot, string.Concat(new[]
            {
                SpawnIds.MapOf(character), " ", SpawnIds.Label(character), " (", GameNetwork.CharacterIdOf(character),
                ") earned ", xp.ToString(), " experience from ", victim != null ? SpawnIds.Label(victim) : "somebody",
            }));
        // On a server the character being paid is not the one on that player's screen, so their machine has to be
        // told or the kill quietly earns them nothing. Does nothing anywhere else.
        GameNetwork.TellOwnerTheyEarned(character, xp, 0, levelsGained, ringLevelsGained);
    }

    /// <summary>Pay PRACTICE for a kill — the other half of what a death is worth, and the only way a weapon
    /// mastery grows. Paid to everyone who took health off the creature, by the SHARE each of them took with each
    /// weapon they held, so a party splits one creature's worth between them and nobody is paid twice for the
    /// same blow.
    ///
    /// <para>The creature's LEVEL never makes this bigger: it decides how challenging the fight was, which is a
    /// multiplier held between nothing and one and a half. A creature ten levels below the fighter teaches
    /// nothing, and one that cannot die is never here at all.</para></summary>
    public static void PayPractice(Health victim)
    {
        if (victim == null) return;
        int pool = Mathf.Max(1, victim.maxHealth);

        for (int i = 0; i < Health.PracticePlaces; i++)
        {
            if (!victim.PracticeAt(i, out var who, out var weapon, out int dealt, out var grade)) continue;
            if (PlayerUnits.Gone(who) || weapon == WeaponType.None) continue;
            var character = who as Character;
            if (character == null) continue;

            // PRICED AND CAPPED BY THE BLADE THAT DID THE WORK, not by whatever is in hand at the moment it
            // fell — swapping a fine sword in for the killing blow buys none of the fight that came before it.
            float points = MasteryMath.PracticeFromKill(dealt, pool, victim.Level, character.Level, grade);
            if (points <= 0f) continue;

            GameNetwork.SaveSoonFor(character);
            int before = character.Mastery.EarnedLevel(weapon);
            character.Mastery.AddPractice(weapon, points, grade);
            int after = character.Mastery.EarnedLevel(weapon);
            if (after > before) character.Mastery.LearnWhatIsOpen();

            if (GameLog.On(LogCat.Loot))
                GameLog.Info(LogCat.Loot, string.Concat(new[]
                {
                    SpawnIds.MapOf(character), " ", SpawnIds.Label(character), " practised ", weapon.ToString(),
                    " ", points.ToString("0.##"), " (level ", after.ToString(), ") from ",
                    SpawnIds.Label(victim),
                }));
        }
    }

    /// <summary>WHO EARNED WHAT THIS UNIT WAS CARRYING — its experience and its loot both. Today the answer is the
    /// last unit to land a blow that cost it health, which is the simplest thing a player can predict from what
    /// they can see and costs nothing to track: the damage door already writes it down.
    ///
    /// <para>THE ONE PLACE THIS IS DECIDED. When a share of the work is worked out properly — how much damage each
    /// person did, who healed, who held it still — it is worked out HERE, and every payment follows it without
    /// another line changing.</para></summary>
    public static ICombatUnit WhoEarned(Health victim) => victim != null ? victim.LastAttacker : null;

    /// <summary>Pay <paramref name="winner"/> everything one kill was carrying: <paramref name="gold"/> Zeny straight
    /// into their purse, and every item in <paramref name="items"/> onto the ground where
    /// <paramref name="victim"/> fell — kept for <paramref name="winner"/> for <see cref="GroundDrop.KillerOnlySeconds"/>,
    /// then anybody's, and gone after <see cref="GroundDrop.KillDropLifetimeSeconds"/>.
    ///
    /// <para>ONE CALL PER KILL, not one per thing dropped: on a server the purse is handed to its owner and written down
    /// every time it changes, so paying piece by piece would send and write it several times for one dead creature.</para></summary>
    /// <param name="items">Item DEFINITIONS the kill gives up — any equipment among them lands as a fresh Raw copy.</param>
    public static void PayLoot(ICombatUnit winner, List<Item> items, int gold, Health victim)
    {
        if (PlayerUnits.Gone(winner)) return;

        // Only a CHARACTER has a purse; a creature killing a creature carries nothing away.
        var character = winner as Character;
        if (character == null) return;

        int dropped = 0;
        if (victim != null)
        {
            var at = victim.transform.position;
            string map = victim.gameObject.scene.name;
            if (items != null)
                for (int i = 0; i < items.Count; i++)
                {
                    var def = items[i];
                    if (def == null) continue;
                    var copy = def.slot != EquipSlotType.None ? EquipmentInstance.CreateFresh(def) : null;
                    if (DropForKiller(at, map, def, copy, character)) dropped++;
                }
        }

        if (gold > 0)
        {
            character.AddGold(gold);
            // On a server the purse that just changed is not the one on that player's screen, so their machine is handed
            // it and the change is written down. Off a server there is only one purse and its window is told directly.
            if (!GameNetwork.TellOwnerTheirBagChanged(character)) InventoryStore.Instance?.Notify();
        }

        // A KILL THAT PAID NOTHING SAYS SO too — without it "paid nothing" and "the payout never ran" are both silence,
        // and only the second is a bug.
        if (GameLog.On(LogCat.Loot)) GameLog.Info(LogCat.Loot, Describe(character, items, gold, dropped, victim));
    }

    // ONE THING DOWN ON THE GROUND for the killer, next to where the creature fell, and every player near enough told.
    static bool DropForKiller(Vector3 at, string map, Item def, EquipmentInstance copy, Character killer)
    {
        var drop = GroundDrop.Place(GroundDrop.FreeSpotNear(at, map), def, 1, copy, Quality.Raw, 0, map, 0);
        if (drop == null) return false;
        drop.ReserveFor(killer, GroundDrop.KillerOnlySeconds, GroundDrop.KillDropLifetimeSeconds);
        GameNetwork.PublishDrop(drop);
        return true;
    }

    /// <summary>What a kill paid, as one line: who got it, how much Zeny, every item put down for them by name, and which
    /// unit it came from — "field_15 Alpha #1 (id) earned 6 Zeny and dropped Jellopy for them from Snake #3", or "earned
    /// nothing from Snake #3". Builds a string, so it is only ever called behind <see cref="GameLog.On"/>.</summary>
    static string Describe(Character who, List<Item> items, int gold, int dropped, Health victim)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(SpawnIds.MapOf(who)).Append(' ').Append(SpawnIds.Label(who))
          .Append(" (").Append(GameNetwork.CharacterIdOf(who)).Append(") earned ");

        if (gold <= 0 && dropped <= 0) sb.Append("nothing");
        if (gold > 0) sb.Append(gold).Append(" Zeny");
        if (dropped > 0)
        {
            sb.Append(gold > 0 ? " and dropped " : "dropped ");
            bool first = true;
            if (items != null)
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] == null) continue;
                    if (!first) sb.Append(", ");
                    sb.Append(items[i].displayName);
                    first = false;
                }
            sb.Append(" for them");
        }

        if (victim != null) sb.Append(" from ").Append(SpawnIds.Label(victim));
        return sb.ToString();
    }
}
