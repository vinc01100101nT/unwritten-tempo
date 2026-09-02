using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// The ONE door for changing what a character is carrying — give, take, equip, take off, use, reorder. Every
/// caller goes through here rather than editing the list itself, so a rule about what may enter the bag has one
/// place to live.
///
/// <para><b>It no longer OWNS the bag.</b> The list belongs to <see cref="Character"/>, alongside the worn gear
/// it always owned. This component used to hold it, and that was wrong in a way that cost real items: this lives
/// on the inventory WINDOW's canvas and is built the first time somebody opens the bag, so before that moment a
/// character's belongings had nowhere to be, and the code that saved them had to guess. Now this is the set of
/// operations, and the character is the thing being operated on.</para>
///
/// <para>The window still reads <see cref="Entries"/> and still listens to <see cref="OnChanged"/>; nothing about
/// how the bag looks or behaves changed.</para>
/// </summary>
public class InventoryStore : MonoBehaviour
{
    public static InventoryStore Instance { get; private set; }

    public event Action OnChanged;

    // Handed back when there is no character yet, so every reader can loop over it without a null check and
    // nobody is tempted to add to it. One instance, never rebuilt — this is read from the window's repaint.
    static readonly List<BagEntry> nothing = new List<BagEntry>();

    /// <summary>What the character is carrying. The character's own list, not a copy — editing it through the
    /// methods below changes the thing that gets saved.</summary>
    public List<BagEntry> Entries => Character.Instance != null ? Character.Instance.Bag : nothing;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // NOBODY IS GOING TO BRING A SAVE. Opening the game straight into a map to try something out means no
        // session, so nothing ever loads and nothing ever seeds — and the bag would simply be empty forever.
        //
        // Deliberately NOT gated on the save system being absent, which was the first attempt and was wrong:
        // it is built after the first scene's objects wake up, so that test reads TRUE on every ordinary boot
        // too. Asked of the session instead, which is the thing that actually decides whether a save is coming.
        // Should one arrive anyway, it replaces the whole list rather than adding to it, so the worst this can
        // do is be overwritten.
        if (!AccountSession.IsActive && Character.Instance != null && Character.Instance.Bag.Count == 0)
            Character.Instance.SeedBag();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public int TotalWeight
    {
        get
        {
            int w = 0;
            foreach (var e in Entries) if (e.def != null) w += e.def.weight * Mathf.Max(1, e.count);
            return w;
        }
    }

    /// <summary>The bag changed — repaint whatever is showing it, and (unless told otherwise) remember that it
    /// needs writing down. <paramref name="alsoSave"/> is false for the one case that is not a change at all:
    /// the bag having just been FILLED FROM a save, where marking it dirty would write back what was read.</summary>
    public void Notify(bool alsoSave = true)
    {
        OnChanged?.Invoke();
        // Bag-only changes (give/consume/reorder) don't touch Character's stat events, so flag the autosave
        // here too; equip/unequip already mark dirty via Character.OnStatsChanged.
        if (alsoSave) SaveManager.Instance?.MarkDirty();
    }

    /// <summary>Drop <paramref name="count"/> of an item into the bag. Equipment adds one entry per copy (each its
    /// own <see cref="EquipmentInstance"/>); stackables merge into an existing non-equip stack. The single
    /// add-item path — callers never poke <see cref="Entries"/> directly.</summary>
    public void Give(Item def, int count)
    {
        if (def == null || count <= 0 || Character.Instance == null) return;

        // NOT A DOOR A CLIENT MAY WALK THROUGH. In a world, things enter a bag because the server decided they
        // did — a drop claimed, a command carried out — and it puts them in its own copy and says so. A client
        // adding one here would be inventing an item, and it would vanish on the server's next word anyway.
        if (GameNetwork.MyPlayer != null && !NetworkServer.active) return;
        if (def.slot != EquipSlotType.None)
        {
            for (int i = 0; i < count; i++) GiveEquipment(def);   // each copy rolls its own identity (and notifies)
            return;
        }
        var entries = Entries;
        var e = entries.Find(x => x.def == def && !x.IsEquip);
        if (e != null) e.count += count;
        else entries.Add(new BagEntry { def = def, count = count, isNew = true });
        Notify();
    }

    /// <summary>Add ONE rolled copy of a piece of equipment and hand it back, so the caller can report what fate
    /// gave it (rarity + kit). <paramref name="rarity"/> forces the drop's rarity; null rolls it off the drop table.
    /// The single place a wearable copy is born — loot, GM grants and rewards all land here, so nothing can enter
    /// the bag having skipped its roll.</summary>
    public EquipmentInstance GiveEquipment(Item def, Rarity? rarity = null)
    {
        if (def == null || def.slot == EquipSlotType.None || Character.Instance == null) return null;
        var inst = EquipmentInstance.CreateRolled(def, rarity);
        Entries.Add(new BagEntry { def = def, count = 1, instance = inst, isNew = true });
        Notify();
        return inst;
    }

    /// <summary>Empty the whole bag and report how many stacks went. WORN gear is untouched — this is the bag, not
    /// the character. Returns the number of entries removed (0 = it was already empty).</summary>
    public int ClearAll()
    {
        int removed = Entries.Count;
        if (removed == 0) return 0;
        Entries.Clear();
        Notify();
        return removed;
    }

    // ---- ASKED, when there is a world; DONE, when there is not -------------------------------------------
    //
    // Every one of these changes what somebody owns, so in a world it is the server's to decide and this only
    // asks. What comes back is the whole bag, and the window redraws from that. With NO world — the game opened
    // straight into a map to try something out — there is nobody to ask and it happens right here, exactly as
    // it always did.
    //
    // The ask names a POSITION in the bag, because both copies are the same list in the same order and a
    // position cannot be mistaken for a different copy of an identical item.

    /// <summary>Equip an inventory entry; any item already in that slot returns to the bag.</summary>
    public void Equip(BagEntry e)
    {
        if (e == null || !e.IsEquip || e.instance == null || Character.Instance == null) return;

        var me = GameNetwork.MyPlayer;
        if (me != null) { me.AskToEquip(Entries.IndexOf(e), e.def != null ? e.def.id : ""); return; }

        var slot = e.def.slot;
        var prev = Character.Instance.GetEquippedInstance(slot);
        Character.Instance.SetEquipped(slot, e.instance);
        Entries.Remove(e);
        if (prev != null && prev.def != null)
            Entries.Add(new BagEntry { def = prev.def, count = 1, instance = prev });
        Notify();
    }

    public void Unequip(EquipSlotType slot)
    {
        if (Character.Instance == null) return;

        var me = GameNetwork.MyPlayer;
        if (me != null) { me.AskToUnequip(slot); return; }

        var inst = Character.Instance.GetEquippedInstance(slot);
        if (inst == null) return;
        Character.Instance.SetEquipped(slot, (EquipmentInstance)null);
        if (inst.def != null) Entries.Add(new BagEntry { def = inst.def, count = 1, instance = inst });
        Notify();
    }

    public void Consume(BagEntry e)
    {
        if (e == null || e.def == null) return;

        // The item's effect already goes to the server on its own road; what this decides is whether the stack
        // is ALSO reduced here. In a world it is not — the server takes it off and says so.
        if (!ItemUse.Use(e.def, Entries.IndexOf(e))) return;
        if (--e.count <= 0) Entries.Remove(e);
        Notify();
    }

    /// <summary>Drag-to-sort: pull <paramref name="moving"/> out and re-insert it just before
    /// <paramref name="before"/> (others shift up). A null <paramref name="before"/> appends to the
    /// end (dropped on an empty cell). No-op if nothing actually changes.</summary>
    public void Reorder(BagEntry moving, BagEntry before)
    {
        if (moving == null || moving == before) return;
        var entries = Entries;
        int from = entries.IndexOf(moving);
        if (from < 0) return;

        var me = GameNetwork.MyPlayer;
        if (me != null)
        {
            int to = before != null ? entries.IndexOf(before) : entries.Count - 1;
            me.AskToReorder(from, to < 0 ? entries.Count - 1 : to, moving.def != null ? moving.def.id : "");
            return;
        }

        entries.RemoveAt(from);
        int idx = before != null ? entries.IndexOf(before) : entries.Count;
        if (idx < 0) idx = entries.Count;
        entries.Insert(idx, moving);
        Notify();
    }
}
