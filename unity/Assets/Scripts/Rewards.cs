using System.Collections.Generic;
using UnityEngine;

/// <summary>One thing a kill gives up: the item, how many, the very copy when it is a piece of gear (made once, before
/// the kill is paid, so the ground and the bag are handed the same piece), and the chance it won its roll at — what
/// auto-loot's filter judges it by.</summary>
public struct LootEntry
{
    public Item item;
    public int count;
    public EquipmentInstance copy;
    public float chance;
}

/// <summary>
/// WHAT A KILL IS WORTH, and who gets it — the one door every reward passes through.
///
/// <para><b>Why it exists.</b> A kill used to pay "the player", meaning the one person at this keyboard, from a
/// listener attached to each creature as it was born. That answer is wrong in both directions the moment a world
/// holds more than one person: on a server it pays nobody, and in a town it would pay whoever happened to be
/// standing there rather than whoever did the work. Paying the killer is a decision, and a decision needs one
/// place to live.</para>
///
/// <para><b>The rule:</b> everyone who fought a creature collects points for it (<see cref="KillLedger"/>) — damage
/// dealt, blows taken, health restored to others, the first hit and the last. The most points owns its loot;
/// its experience is split among everyone by points.</para>
///
/// <para>On a server the person being paid is the fighter it holds for that connection, and their machine is told
/// so their own character records it (see <see cref="GameNetwork"/>). On a single machine it is simply themselves.</para>
///
/// <para><b>Loot and experience are decided off the SAME ledger, always</b>: <see cref="WhoEarned"/> names its top
/// scorer and <see cref="PayKill"/> splits by it, and there is no second count anywhere.</para>
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
        PayShare(character, xp, victim);
    }

    // Who one death pays and how much each, filled and emptied inside PayKill.
    static readonly List<Character> paidWho = new List<Character>(8);
    static readonly List<int> paidPart = new List<int>(8);

    /// <summary>PAY A KILL'S EXPERIENCE TO EVERYONE WHO FOUGHT FOR IT, each by their share of the points
    /// (<see cref="KillLedger"/>) — the parts add up to exactly <paramref name="xp"/>, before each person's own event
    /// rate and run of kills. With nobody on the ledger it falls back to <see cref="WhoEarned"/>'s one person.</summary>
    public static void PayKill(Health victim, int xp)
    {
        if (victim == null || xp <= 0 || victim.IsSummoned) return;
        var ledger = victim.Ledger;
        if (ledger != null && !ledger.IsEmpty)
            ledger.Split(xp, KillLedger.PersonBehind(victim.LastAttacker), victim.gameObject.scene.name, paidWho, paidPart);
        else
            paidWho.Clear();
        if (paidWho.Count == 0) { PayForKill(WhoEarned(victim), xp, victim); return; }
        for (int i = 0; i < paidWho.Count; i++)
            if (!PlayerUnits.Gone(paidWho[i])) PayShare(paidWho[i], paidPart[i], victim);
        paidWho.Clear();
        paidPart.Clear();
    }

    // One person's part of a kill that is worth something. A part that rounded down to nothing still steps their run
    // of kills and counts for their quests: they fought for it.
    static void PayShare(Character character, int xp, Health victim)
    {
        // UNDER WHATEVER WORLD EVENT IS RUNNING — an experience rate multiplies what the kill pays, here and nowhere
        // else, so the number paid, logged and shown is one number. A rate small enough to round a kill down to
        // nothing pays nothing, but the kill still counts for any quest below.
        int eventPaid = WorldEvents.KillExperience(xp);
        // AND UNDER THE RUN OF KILLS THEY ARE ON — a multiplier on top of the event's, so a run is worth the same share
        // of a creature at any level. Every kill that gets this far steps the run, even one that rounds to nothing.
        double now = KillCombo.Now;
        int combo = CountKill(character, victim, now);
        int comboPaid = KillComboMath.Paid(eventPaid, combo);
        // AND UNDER WHAT THEIR HEADGEAR ADDS (the Chef's Hat), on top of both.
        int paid = Headgear.ExperienceWithBonus(comboPaid, character.WornExperiencePercent);
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
                    ComboWords(combo, comboPaid - eventPaid), paid > comboPaid ? ", +" + (paid - comboPaid) + " from headgear" : "",
                }));
            // On a server the character being paid is not the one on that player's screen, so their machine has to
            // be told or the kill quietly earns them nothing. Does nothing anywhere else.
            GameNetwork.TellOwnerTheyEarned(character, paid, levelsGained, ringLevelsGained);
        }
        // Their combo, what this kill paid and their per-hour rates, to their own screen — every kill, paid or not.
        GameNetwork.TellOwnerTheirKillCombo(character, victim, true, paid, comboPaid - eventPaid, 0, 0);

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

    /// <summary>WHO OWNS WHAT THIS UNIT WAS CARRYING — the person with the most points for it (<see cref="KillLedger"/>:
    /// damage dealt, blows taken, health restored to others, the first hit and the last). Its loot is kept for them
    /// and a turned-back character is theirs; its experience is split among everyone with points
    /// (<see cref="PayKill"/>). When nobody is on the ledger, it is the last unit to land a blow that cost it health.
    ///
    /// <para>THE ONE PLACE THIS IS DECIDED, and the same answer every time it is asked during one death: the ledger
    /// does not change between the questions.</para></summary>
    public static ICombatUnit WhoEarned(Health victim)
    {
        var last = victim != null ? victim.LastAttacker : null;
        if (victim != null && victim.Ledger != null && !victim.Ledger.IsEmpty)
        {
            var top = victim.Ledger.Top(KillLedger.PersonBehind(last), victim.gameObject.scene.name);
            if (top != null) return top;
        }
        // A CREATURE FIGHTING FOR SOMEBODY earns for them: a saved character's kills are its saver's.
        if (last is Health fighter && fighter.owner != null && !PlayerUnits.Gone(fighter.owner)) return fighter.owner;
        return last;
    }

    /// <summary>Pay <paramref name="winner"/> everything one kill was carrying: <paramref name="gold"/> Zeny straight
    /// into their purse, and every entry in <paramref name="entries"/> onto the ground where <paramref name="victim"/>
    /// fell — kept for <paramref name="winner"/> for <see cref="GroundDrop.KillerOnlySeconds"/>, then anybody's, and gone
    /// after <see cref="GroundDrop.KillDropLifetimeSeconds"/>. Auto-combat's loot filter first takes what its owner asked
    /// for straight into their bag (<see cref="AutoLootTakes"/>), unless <paramref name="neverAutoLoot"/> — an elite's
    /// kill, hunted by hand.
    ///
    /// <para><paramref name="pileItems"/> and <paramref name="pileCounts"/>, index for index, are piles that go down
    /// APART and are never auto-looted — what a saved character carries beyond what its saver's bag can hold. Everything in <paramref name="entries"/> lands as one pile per item.</para>
    ///
    /// <para>ONE CALL PER KILL, not one per thing dropped: on a server the purse is handed to its owner and written down
    /// every time it changes, so paying piece by piece would send and write it several times for one dead creature.</para></summary>
    public static void PayLoot(ICombatUnit winner, List<LootEntry> entries, bool neverAutoLoot, int gold, Health victim,
                               List<Item> pileItems = null, List<int> pileCounts = null)
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
            // Who it all fell from, so every watcher throws the pictures out of that body.
            uint fromId = victim.TryGetComponent<Mirror.NetworkIdentity>(out var victimId) ? victimId.netId : 0;
            // AUTO-LOOT FIRST, ENTRY BY ENTRY: what its owner's filter takes leaves the list for their bag, so the
            // ground below only ever sees what was left.
            if (!neverAutoLoot) AutoLoot(character, entries, map);
            if (entries != null)
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e.item == null || e.count <= 0) continue;
                    // SEVERAL OF ONE THING GO DOWN AS ONE PILE — two rolls that both gave up Watery Ink are one drop. A
                    // piece of gear is always one of its own: the very copy the kill made.
                    int count = e.count;
                    if (e.copy == null)
                    {
                        if (FirstStackOf(entries, e.item) < i) continue;   // already put down with the first of its kind
                        count = StackCount(entries, e.item);
                    }
                    if (DropForKiller(at, map, e.item, count, e.copy, character, fromId)) dropped++;
                }
            if (pileItems != null && pileCounts != null)
                for (int i = 0; i < pileItems.Count && i < pileCounts.Count; i++)
                    if (pileItems[i] != null && pileCounts[i] > 0
                        && DropForKiller(at, map, pileItems[i], pileCounts[i], null, character, fromId)) dropped++;
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
        if (GameLog.On(LogCat.Loot)) GameLog.Info(LogCat.Loot, Describe(character, entries, gold, dropped, victim, pileItems, pileCounts));
    }

    // What auto-loot took, kept between the two halves of one payment; one list because a death is never paid twice at once.
    static readonly List<LootEntry> autoLooted = new List<LootEntry>(16);

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

    static bool TookStackOf(Item def)
    {
        for (int i = 0; i < autoLooted.Count; i++) if (autoLooted[i].item == def && autoLooted[i].copy == null) return true;
        return false;
    }

    // Take every entry auto-loot wants out of `entries` and into the killer's bag. The number of entries it took; 0 when
    // nothing was looted.
    static int AutoLoot(Character killer, List<LootEntry> entries, string map)
    {
        if (entries == null) return 0;
        var shell = GameNetwork.ShellFor(killer);
        if (shell == null || !shell.AutoCombatOn) return 0;

        autoLooted.Clear();
        // What the bag holds, read ONCE for the whole kill; each thing taken is counted on top.
        for (int t = 0; t < lootTabSlots.Length; t++) lootTabSlots[t] = killer.SlotsUsed((ItemCategory)t);
        int lootWeight = killer.CarriedWeight;
        int weightCap = killer.WeightCap;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            var def = e.item;
            if (def == null || e.count <= 0 || !AutoLootTakes(killer, shell, map, def, e.chance)) continue;
            // NO ROOM, AND IT STAYS IN THE DROP: it falls to the ground like anything auto-loot leaves, and they are
            // told why. Judged on everything the entry holds, on top of what this kill has already put in.
            bool gear = e.copy != null;
            bool held = !gear && (TookStackOf(def) || HoldsStackOf(killer, def));
            int needed = BagRoom.SlotsNeeded(gear, gear ? 1 : e.count, held);
            int weight = def.weight * (gear ? 1 : e.count);
            var noRoom = BagRoom.Judge(lootTabSlots[(int)def.category], needed, lootWeight, weight, weightCap);
            if (noRoom != BagRefusal.None) { shell.TellNoRoom(def, noRoom, "auto-loot", AutoLootQuietSeconds); continue; }
            lootTabSlots[(int)def.category] += needed;
            lootWeight += weight;
            autoLooted.Add(e);
            entries.RemoveAt(i);
        }
        if (autoLooted.Count == 0) return 0;

        // INTO THE BAG — a piece of gear as the very copy the kill made, the same one the ground would have been handed —
        // and the owner told each thing once with how many, by the bag's own door (Character.Arrived) and nowhere else.
        for (int i = 0; i < autoLooted.Count; i++)
        {
            var e = autoLooted[i];
            if (e.copy != null) { killer.TakeIntoBag(e.copy); continue; }
            if (FirstStackOf(autoLooted, e.item) < i) continue;
            int count = StackCount(autoLooted, e.item);
            killer.TakeIntoBag(e.item, count);
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
    static bool DropForKiller(Vector3 at, string map, Item def, int count, EquipmentInstance copy, Character killer,
                              uint fromId)
    {
        var drop = GroundDrop.Place(GroundDrop.FreeSpotNear(at, map), def, count, copy, Quality.Raw, 0, map, 0);
        if (drop == null) return false;
        drop.ThrownBy(fromId, at);
        drop.ReserveFor(killer, GroundDrop.KillerOnlySeconds, GroundDrop.KillDropLifetimeSeconds);
        GameNetwork.PublishDrop(drop);
        return true;
    }

    // The first entry that is a stack of `def` (not a piece of gear), or -1.
    static int FirstStackOf(List<LootEntry> entries, Item def)
    {
        for (int i = 0; i < entries.Count; i++) if (entries[i].item == def && entries[i].copy == null) return i;
        return -1;
    }

    // How many of `def` every stack entry holds together.
    static int StackCount(List<LootEntry> entries, Item def)
    {
        int n = 0;
        for (int i = 0; i < entries.Count; i++) if (entries[i].item == def && entries[i].copy == null) n += entries[i].count;
        return n;
    }

    /// <summary>What a kill paid, as one line: who got it, how much Zeny, every item put down for them by name, and which
    /// unit it came from — "field_15 Alpha #1 (id) earned 6 Zeny and dropped Jellopy for them from Snake #3", or "earned
    /// nothing from Snake #3". Builds a string, so it is only ever called behind <see cref="GameLog.On"/>.</summary>
    static string Describe(Character who, List<LootEntry> entries, int gold, int dropped, Health victim,
                           List<Item> pileItems, List<int> pileCounts)
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
            if (entries != null)
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e.item == null || e.count <= 0) continue;
                    int count = 1;
                    if (e.copy == null)
                    {
                        if (FirstStackOf(entries, e.item) < i) continue;
                        count = StackCount(entries, e.item);
                    }
                    if (!first) sb.Append(", ");
                    sb.Append(e.copy != null ? ItemTooltip.CopyTitle(e.copy) : e.item.displayName);
                    if (count > 1) sb.Append(" ×").Append(count);
                    first = false;
                }
            if (pileItems != null && pileCounts != null)
                for (int i = 0; i < pileItems.Count && i < pileCounts.Count; i++)
                {
                    if (pileItems[i] == null || pileCounts[i] <= 0) continue;
                    if (!first) sb.Append(", ");
                    sb.Append("a pile of ").Append(pileItems[i].displayName).Append(" ×").Append(pileCounts[i]);
                    first = false;
                }
            sb.Append(" for them");
        }

        if (victim != null) sb.Append(" from ").Append(SpawnIds.Label(victim));
        return sb.ToString();
    }
}
