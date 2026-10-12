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
        // A SUMMON PAYS NOTHING: it exists to get in the way, and a summoner that kept calling paid ones would be a
        // fountain of experience nobody had to fight the summoner for.
        if (victim != null && victim.IsSummoned) return;

        // Only a CHARACTER has a progression to pay into; a creature killing a creature earns nothing today.
        var character = killer as Character;
        if (character == null) return;

        // UNDER WHATEVER WORLD EVENT IS RUNNING — an experience rate multiplies what the kill pays, here and nowhere
        // else, so the number paid, logged and shown is one number. A rate small enough to round a kill down to
        // nothing pays nothing, but the kill still counts for any quest below.
        int eventPaid = WorldEvents.KillExperience(xp);
        // AND UNDER THE RUN OF KILLS THEY ARE ON — a multiplier on top of the event's, so a run is worth the same share
        // of a creature at any level. Every kill that gets this far steps the run, even one that rounds to nothing.
        double now = KillCombo.Now;
        int combo = CountKill(character, victim, now);
        int paid = KillComboMath.Paid(eventPaid, combo);
        PlaySession.Killed(character, paid);
        character.KillCombo.Earned(now, paid, 0);
        if (paid > 0)
        {
            int levelsGained = character.AddXP(paid, out int ringLevelsGained);
            // "field_15 Alpha #1 (id) earned 44 experience from Snake #3 — combo 12, +4 from it" — beside the loot line,
            // so what a kill paid is all in the log and nothing about it needs a recorder.
            if (GameLog.On(LogCat.Loot))
                GameLog.Info(LogCat.Loot, string.Concat(new[]
                {
                    SpawnIds.MapOf(character), " ", SpawnIds.Label(character), " (", GameNetwork.CharacterIdOf(character),
                    ") earned ", paid.ToString(), " experience from ", victim != null ? SpawnIds.Label(victim) : "somebody",
                    ComboWords(combo, paid - eventPaid),
                }));
            // On a server the character being paid is not the one on that player's screen, so their machine has to
            // be told or the kill quietly earns them nothing. Does nothing anywhere else.
            GameNetwork.TellOwnerTheyEarned(character, paid, 0, levelsGained, ringLevelsGained);
        }
        // Their combo, what this kill paid and their per-hour rates, to their own screen — every kill, paid or not.
        GameNetwork.TellOwnerTheirKillCombo(character, victim, true, paid, paid - eventPaid, 0, 0);

        // AND ANY QUEST COUNTING THIS CREATURE — the same kill, paid to the same person, through this same door.
        // A SAVED CHARACTER is not the creature whose body it wears — the same exclusion SavedCharacters makes.
        if (victim != null && !victim.IsSavedCharacter && character.Quests.CountKill(victim.creatureId))
        {
            character.Quests.Advance(character);
            GameNetwork.ShellOfFighter(character)?.QuestsMoved();
        }
    }

    /// <summary>THE RUN OF KILLS <paramref name="character"/> is on, counting the death of <paramref name="victim"/> at
    /// <paramref name="now"/>. Asked by every payment one death makes — its experience here, a turned-back character's
    /// rescue pay (<see cref="SavedCharacters"/>), whichever runs first — and the death steps the run once.</summary>
    public static int CountKill(Character character, Health victim, double now)
        => character.KillCombo.CountKill(victim != null ? victim.GetInstanceID() : 0, Time.frameCount, now);

    /// <summary>The end of a payment's log line saying the run it was paid under: " — combo 12, +4 from it", or
    /// " — combo 1" when the run added nothing.</summary>
    public static string ComboWords(int combo, int fromCombo)
        => fromCombo > 0 ? " — combo " + combo + ", +" + fromCombo + " from it" : " — combo " + combo;

    /// <summary>WHO EARNED WHAT THIS UNIT WAS CARRYING — its experience and its loot both. Today the answer is the
    /// last unit to land a blow that cost it health, which is the simplest thing a player can predict from what
    /// they can see and costs nothing to track: the damage door already writes it down.
    ///
    /// <para>THE ONE PLACE THIS IS DECIDED. When a share of the work is worked out properly — how much damage each
    /// person did, who healed, who held it still — it is worked out HERE, and every payment follows it without
    /// another line changing.</para></summary>
    public static ICombatUnit WhoEarned(Health victim)
    {
        var last = victim != null ? victim.LastAttacker : null;
        // A CREATURE FIGHTING FOR SOMEBODY earns for them: a saved character's kills are its saver's.
        if (last is Health fighter && fighter.owner != null && !PlayerUnits.Gone(fighter.owner)) return fighter.owner;
        return last;
    }

    /// <summary>Pay <paramref name="winner"/> everything one kill was carrying: <paramref name="gold"/> Zeny straight
    /// into their purse, and every item in <paramref name="items"/> onto the ground where
    /// <paramref name="victim"/> fell — kept for <paramref name="winner"/> for <see cref="GroundDrop.KillerOnlySeconds"/>,
    /// then anybody's, and gone after <see cref="GroundDrop.KillDropLifetimeSeconds"/>.
    ///
    /// <para>ONE CALL PER KILL, not one per thing dropped: on a server the purse is handed to its owner and written down
    /// every time it changes, so paying piece by piece would send and write it several times for one dead creature.</para></summary>
    /// <param name="items">Item DEFINITIONS the kill gives up — any equipment among them lands as a fresh Raw copy.</param>
    public static void PayLoot(ICombatUnit winner, List<Item> items, int gold, Health victim)
        => PayLoot(winner, items, null, gold, victim);

    /// <summary>The same, with the drop chance (0–1) each item won its roll at, index for index — so auto-combat's
    /// loot filter can take what its owner asked for straight into their bag (<see cref="AutoLootTakes"/>), the rest
    /// going down as usual. Null chances = nothing is auto-looted.</summary>
    public static void PayLoot(ICombatUnit winner, List<Item> items, List<float> chances, int gold, Health victim)
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
            // AUTO-LOOT FIRST, ENTRY BY ENTRY: what its owner's filter takes leaves the list for their bag, so the
            // ground below only ever sees what was left.
            AutoLoot(character, items, chances, map);
            if (items != null)
                for (int i = 0; i < items.Count; i++)
                {
                    var def = items[i];
                    if (def == null) continue;
                    // SEVERAL OF ONE THING GO DOWN AS ONE PILE — an elite's ten Iron is one drop of ten, not ten drops. A
                    // worn piece is always one of its own.
                    int count = 1;
                    if (def.slot == EquipSlotType.None)
                    {
                        if (IndexOf(items, def) < i) continue;   // already put down with the first of its kind
                        count = CountOf(items, def);
                    }
                    var copy = def.slot != EquipSlotType.None ? EquipmentInstance.CreateFresh(def) : null;
                    if (DropForKiller(at, map, def, count, copy, character)) dropped++;
                }
        }

        PlaySession.ThingsFell(character, dropped);
        if (gold > 0)
        {
            character.AddGold(gold);
            PlaySession.EarnedGold(character, gold);
            // On a server the purse that just changed is not the one on that player's screen, so their machine is handed
            // it and the change is written down. Off a server there is only one purse and its window is told directly.
            if (!GameNetwork.TellOwnerTheirBagChanged(character)) InventoryStore.Instance?.Notify();
        }

        // A KILL THAT PAID NOTHING SAYS SO too — without it "paid nothing" and "the payout never ran" are both silence,
        // and only the second is a bug.
        if (GameLog.On(LogCat.Loot)) GameLog.Info(LogCat.Loot, Describe(character, items, gold, dropped, victim));
    }

    // What auto-loot took, kept between the two halves of one payment; one list because a death is never paid twice at once.
    static readonly List<Item> autoLooted = new List<Item>(16);

    /// <summary>DOES AUTO-LOOT TAKE THIS ENTRY for <paramref name="killer"/> — they are here to take it (on the map the
    /// creature fell in, alive, with auto-combat on), and their filter wants a thing of its kind at this drop chance.</summary>
    static bool AutoLootTakes(Character killer, NetPlayer shell, string map, Item def, float chance01)
    {
        if (def == null || shell == null || !shell.AutoCombatOn || killer.IsDead || killer.Map != map) return false;
        var kind = AutoCombatMath.KindOf(def.slot, def.IsUsable, def.category, def.materialKind);
        return AutoCombatMath.Takes(killer.AutoCombat, kind, chance01);
    }

    // A thing auto-loot cannot take is said once in this long, not on every kill that drops it.
    const float AutoLootQuietSeconds = 45f;

    // Entries each bag tab holds during one kill's auto-loot, indexed by ItemCategory — reused.
    static readonly int[] lootTabSlots = new int[System.Enum.GetValues(typeof(ItemCategory)).Length];

    static bool HoldsStackOf(Character c, Item def)
    {
        for (int i = 0; i < c.Bag.Count; i++)
            if (c.Bag[i] != null && c.Bag[i].def == def && !c.Bag[i].IsEquip) return true;
        return false;
    }

    // Take every entry auto-loot wants out of `items` and into the killer's bag. The count it took; 0 when nothing was
    // looted, or there were no chances to judge by.
    static int AutoLoot(Character killer, List<Item> items, List<float> chances, string map)
    {
        if (items == null || chances == null || chances.Count != items.Count) return 0;
        var shell = GameNetwork.ShellFor(killer);
        if (shell == null || !shell.AutoCombatOn) return 0;

        autoLooted.Clear();
        // What the bag holds, read ONCE for the whole kill; each thing taken is counted on top.
        for (int t = 0; t < lootTabSlots.Length; t++) lootTabSlots[t] = killer.SlotsUsed((ItemCategory)t);
        int lootWeight = killer.CarriedWeight;
        int weightCap = killer.WeightCap;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var def = items[i];
            if (!AutoLootTakes(killer, shell, map, def, chances[i])) continue;
            // NO ROOM, AND IT STAYS IN THE DROP: it falls to the ground like anything auto-loot leaves, and they are
            // told why. Judged on top of what this kill has already put in.
            bool gear = def.slot != EquipSlotType.None;
            bool held = !gear && (autoLooted.Contains(def) || HoldsStackOf(killer, def));
            int needed = BagRoom.SlotsNeeded(gear, 1, held);
            var noRoom = BagRoom.Judge(lootTabSlots[(int)def.category], needed, lootWeight, def.weight, weightCap);
            if (noRoom != BagRefusal.None) { shell.TellNoRoom(def, noRoom, "auto-loot", AutoLootQuietSeconds); continue; }
            lootTabSlots[(int)def.category] += needed;
            lootWeight += def.weight;
            autoLooted.Add(def);
            items.RemoveAt(i);
            chances.RemoveAt(i);
        }
        if (autoLooted.Count == 0) return 0;

        // INTO THE BAG, a worn piece as its own fresh copy exactly as the ground would have made it, and the owner told
        // each thing once with how many.
        for (int i = 0; i < autoLooted.Count; i++)
        {
            var def = autoLooted[i];
            if (def.slot != EquipSlotType.None) { killer.TakeIntoBag(EquipmentInstance.CreateFresh(def)); continue; }
            if (autoLooted.IndexOf(def) < i) continue;
            killer.TakeIntoBag(def, CountOf(autoLooted, def));
        }
        for (int i = 0; i < autoLooted.Count; i++)
        {
            var def = autoLooted[i];
            if (autoLooted.IndexOf(def) < i) continue;
            shell.TellOwnerTheyGot(def.id, CountOf(autoLooted, def), 0);
        }
        PlaySession.PickedUp(killer);
        if (GameLog.On(LogCat.Loot))
            GameLog.Info(LogCat.Loot, string.Concat(SpawnIds.MapOf(killer), " ", SpawnIds.Label(killer), " (",
                                                    GameNetwork.CharacterIdOf(killer), ") auto-looted ",
                                                    autoLooted.Count.ToString(), " thing(s) straight into their bag"));
        shell.PushBagToOwner();
        shell.SaveSoon();
        int n = autoLooted.Count;
        autoLooted.Clear();
        return n;
    }

    // ONE THING DOWN ON THE GROUND for the killer, next to where the creature fell, and every player near enough told.
    static bool DropForKiller(Vector3 at, string map, Item def, int count, EquipmentInstance copy, Character killer)
    {
        var drop = GroundDrop.Place(GroundDrop.FreeSpotNear(at, map), def, count, copy, Quality.Raw, 0, map, 0);
        if (drop == null) return false;
        drop.ReserveFor(killer, GroundDrop.KillerOnlySeconds, GroundDrop.KillDropLifetimeSeconds);
        GameNetwork.PublishDrop(drop);
        return true;
    }

    static int IndexOf(List<Item> items, Item def)
    {
        for (int i = 0; i < items.Count; i++) if (items[i] == def) return i;
        return -1;
    }

    static int CountOf(List<Item> items, Item def)
    {
        int n = 0;
        for (int i = 0; i < items.Count; i++) if (items[i] == def) n++;
        return n;
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
                    var def = items[i];
                    if (def == null) continue;
                    int count = 1;
                    if (def.slot == EquipSlotType.None)
                    {
                        if (IndexOf(items, def) < i) continue;
                        count = CountOf(items, def);
                    }
                    if (!first) sb.Append(", ");
                    sb.Append(def.displayName);
                    if (count > 1) sb.Append(" ×").Append(count);
                    first = false;
                }
            sb.Append(" for them");
        }

        if (victim != null) sb.Append(" from ").Append(SpawnIds.Label(victim));
        return sb.ToString();
    }
}
