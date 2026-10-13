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
            Character.Instance.GiveStarterKit();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>The bag changed — repaint whatever is showing it.
    ///
    /// <para>It used to also flag the bag for writing down from this machine. It no longer can: the server owns
    /// a character while somebody is playing it and writes the bag itself, and a player's own machine has no way
    /// to save one any more.</para></summary>
    public void Notify()
    {
        OnChanged?.Invoke();
    }

    /// <summary>Put <paramref name="count"/> of an item into THIS machine's own bag and repaint the window. What
    /// lands where is decided by <see cref="Character.TakeIntoBag"/>, which is the one place a thing enters any
    /// bag; this adds the two things only a screen needs — the refusal below, and the repaint. Callers never poke
    /// <see cref="Entries"/> directly.</summary>
    public void Give(Item def, int count)
    {
        if (def == null || count <= 0 || Character.Instance == null) return;

        // NOT A DOOR A CLIENT MAY WALK THROUGH. In a world, things enter a bag because the server decided they
        // did — a drop claimed, a command carried out — and it puts them in its own copy and says so. A client
        // adding one here would be inventing an item, and it would vanish on the server's next word anyway.
        if (GameNetwork.MyPlayer != null && !NetworkServer.active) return;

        Character.Instance.TakeIntoBag(def, count);
        Notify();
    }

    /// <summary>Add ONE fresh copy of a piece of equipment to this machine's own bag and hand it back — Raw,
    /// unfolded, nothing written on it, which is what a piece nobody has worked on is.</summary>
    public EquipmentInstance GiveEquipment(Item def)
    {
        if (def == null || def.slot == EquipSlotType.None || Character.Instance == null) return null;
        var entry = Character.Instance.TakeIntoBag(def, 1);
        Notify();
        return entry != null ? entry.instance : null;
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

    /// <summary>Merge ten of a tiered material into one of the next tier up. The server does the whole of it —
    /// this only names which pile.</summary>
    public void Merge(BagEntry e)
    {
        if (e == null || e.def == null) return;
        var me = GameNetwork.MyPlayer;
        if (me != null) me.AskToMerge(Entries.IndexOf(e), e.def.id);
    }

    public void Unequip(EquipSlotType slot)
    {
        if (Character.Instance == null) return;

        var me = GameNetwork.MyPlayer;
        if (me != null) { me.AskToUnequip(slot); return; }

        var inst = Character.Instance.GetEquippedInstance(slot);
        if (inst == null) return;
        // The same free box the server asks for (BagRoom), here because with no world this machine decides.
        var noRoom = Character.Instance.RoomFor(inst.def, 1);
        if (noRoom != BagRefusal.None) { ChatController.GameSaid(Character.Instance.RefusalLine(inst.def, noRoom, "take off")); return; }
        Character.Instance.SetEquipped(slot, (EquipmentInstance)null);
        if (inst.def != null) Entries.Add(new BagEntry { def = inst.def, count = 1, instance = inst });
        Notify();
    }

    /// <summary>Put <paramref name="count"/> of an entry DOWN, on the ground at the character's feet, where it
    /// stays until somebody picks it up. Nothing is destroyed: what leaves the bag lands on the floor as a sparkle
    /// as a sparkle, named to anybody holding Alt (see <see cref="GroundDrop"/>).
    ///
    /// <para>A wearable always goes down one at a time, because each copy is its own stack. A worn piece cannot
    /// be dropped at all — take it off first — and neither can a quest item.</para></summary>
    public void Drop(BagEntry e, int count)
    {
        if (e == null || e.def == null || count <= 0 || Character.Instance == null) return;
        if (e.worn) return;
        if (e.def.category == ItemCategory.Quest) return;

        var me = GameNetwork.MyPlayer;
        if (me != null) { me.AskToDrop(Entries.IndexOf(e), e.def.id, count); return; }

        int take = e.IsEquip ? 1 : Mathf.Clamp(count, 1, e.count);
        var body = Character.Instance.Body;
        // The same "nearest clear spot" the server picks, so a game with no world scatters exactly as a world does.
        var where = GroundDrop.FreeSpotNear(body != null ? body.position : Vector3.zero, MapManager.CurrentMap);
        var drop = GroundDrop.Place(where, e.def, take, e.instance, Quality.Raw, 0, MapManager.CurrentMap, 0);
        if (drop == null) return;

        // OUT OF THE BAG ONLY ONCE IT IS ON THE FLOOR, so a placement that fails cannot leave an item in neither
        // place. The same order the server does it in.
        if (e.IsEquip || take >= e.count) Entries.Remove(e);
        else e.count -= take;
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

    /// <summary>A box dropped on another box: the two trade places. Neither may be a worn piece (what is worn is not in
    /// the bag).</summary>
    public void Swap(BagEntry a, BagEntry b)
    {
        if (a == null || b == null || a == b || a.worn || b.worn) return;
        var entries = Entries;
        int ia = entries.IndexOf(a), ib = entries.IndexOf(b);
        if (ia < 0 || ib < 0) return;

        var me = GameNetwork.MyPlayer;
        if (me != null) { me.AskToSwap(ia, ib, a.def != null ? a.def.id : "", b.def != null ? b.def.id : ""); return; }

        (entries[ia], entries[ib]) = (entries[ib], entries[ia]);
        Notify();
    }

    /// <summary>Drag-to-sort: pull <paramref name="moving"/> out and re-insert it just before
    /// <paramref name="before"/> (others shift up). A null <paramref name="before"/> appends to the
    /// end (dropped on an empty cell). No-op if nothing actually changes.</summary>
    public void Reorder(BagEntry moving, BagEntry before)
    {
        // A WORN piece is not in the bag, so it has no place in the bag's order — dragging one can only mean
        // "put it somewhere", and there is nowhere to put it. Taking it off is the menu's job.
        if (moving != null && moving.worn) return;
        if (before != null && before.worn) return;

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
