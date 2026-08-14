using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The runtime inventory model behind the new Inventory + Equipment windows: a flat list of
/// entries (equipment carries a unique <see cref="EquipmentInstance"/>; stackables carry a count).
/// Equipping/unequipping routes through <see cref="Character"/> so gear affects real combat stats.
/// Lives on the windows' canvas. On first-ever run it seeds from the starter <see cref="Item"/> assets
/// in Resources/Items; once a save carries a bag, it restores from that instead (v6+). SaveManager owns
/// the capture/restore stitching (this store is created lazily when the bag window first opens).
/// </summary>
public class InventoryStore : MonoBehaviour
{
    public static InventoryStore Instance { get; private set; }

    public class Entry
    {
        public Item def;
        public int count;
        public EquipmentInstance instance;   // present for equipment
        public bool isNew;
        public bool IsEquip => def != null && def.slot != EquipSlotType.None;
    }

    public readonly List<Entry> Entries = new List<Entry>();
    public event Action OnChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Restore the saved bag if one exists; otherwise seed the starter items (first-ever run). Gate on
        // SaveManager being initialized so a startup race can't seed over an existing save. In practice this
        // store is built only when the player opens the bag window, long after SaveManager has loaded.
        var loaded = (SaveManager.Instance != null && SaveManager.Instance.Initialized)
            ? SaveManager.Instance.Loaded : null;
        if (loaded != null && loaded.bagItemIds != null) LoadFrom(loaded);
        else Seed();
    }

    /// <summary>Snapshot the current bag into a <see cref="SaveData"/> (parallel arrays — see the schema).
    /// Equipment stacks carry refine + cards; stackables carry a count. Save-time only, not a hot path.</summary>
    public void CaptureInto(SaveData d)
    {
        var ids = new List<string>();
        var counts = new List<int>();
        var refines = new List<int>();
        var cards = new List<string>();
        var rarities = new List<int>();
        var kits = new List<string>();
        foreach (var e in Entries)
        {
            if (e == null || e.def == null) continue;
            // The Ring vessel is auto-worn on load (EnsureRingEquipped) and never a real bag item — don't
            // persist a bag copy, or a load would restore it here AND re-equip a fresh one (duplicate).
            if (e.def.slot == EquipSlotType.Ring) continue;
            ids.Add(e.def.id);
            counts.Add(Mathf.Max(1, e.count));
            if (e.IsEquip && e.instance != null)
            {
                refines.Add(e.instance.upgrade);
                cards.Add(ItemCatalog.EncodeCards(e.instance.cards));
                rarities.Add((int)e.instance.rarity);
                kits.Add(e.instance.EncodeSkills());
            }
            else { refines.Add(0); cards.Add(""); rarities.Add((int)e.def.rarity); kits.Add(""); }
        }
        d.bagItemIds = ids.ToArray();
        d.bagCounts  = counts.ToArray();
        d.bagRefines = refines.ToArray();
        d.bagCards   = cards.ToArray();
        d.bagRarities = rarities.ToArray();
        d.bagSkills   = kits.ToArray();
    }

    /// <summary>Rebuild the bag from a saved <see cref="SaveData"/>. Unknown item ids are dropped so a
    /// removed asset can't break a load.</summary>
    public void LoadFrom(SaveData d)
    {
        Entries.Clear();
        if (d.bagItemIds != null)
        {
            for (int i = 0; i < d.bagItemIds.Length; i++)
            {
                var def = ItemCatalog.Resolve(d.bagItemIds[i]);
                if (def == null) continue;
                int count = (d.bagCounts != null && i < d.bagCounts.Length) ? d.bagCounts[i] : 1;
                var entry = new Entry { def = def, count = Mathf.Max(1, count) };
                if (def.slot != EquipSlotType.None)
                {
                    int refine = (d.bagRefines != null && i < d.bagRefines.Length) ? d.bagRefines[i] : 0;
                    entry.count = 1;
                    entry.instance = new EquipmentInstance(def, refine);
                    if (d.bagCards != null && i < d.bagCards.Length)
                        ItemCatalog.DecodeCards(d.bagCards[i], entry.instance.cards);
                    // v7+: the copy's rolled rarity + kit ride the save; a pre-v7 entry keeps the def's rarity and
                    // an empty kit rather than silently re-rolling into something the player never earned.
                    if (d.bagRarities != null && i < d.bagRarities.Length) entry.instance.rarity = (Rarity)d.bagRarities[i];
                    if (d.bagSkills != null && i < d.bagSkills.Length) entry.instance.DecodeSkills(d.bagSkills[i]);
                }
                Entries.Add(entry);
            }
        }
        Notify();
    }

    void Seed()
    {
        Entries.Clear();
        foreach (var def in Resources.LoadAll<Item>("Items"))
        {
            if (def == null) continue;
            // The Ring of Progression vessel is auto-equipped by Character (one per player); don't also seed a
            // duplicate into the bag. It still returns to the bag normally if you unequip it.
            if (def.slot == EquipSlotType.Ring) continue;
            // WEAPONS are never seeded: a weapon's identity is its ROLLED kit, so it may only enter the world
            // through a roll (a drop, or /weapon). A hand-placed starter weapon would have no kit at all.
            if (def.slot == EquipSlotType.Weapon) continue;
            if (def.slot != EquipSlotType.None)
            {
                // Showcase the +N enhancement channel: rarer gear arrives pre-refined.
                int up = def.rarity >= Rarity.Legendary ? 9 : def.rarity >= Rarity.Rare ? 7 : 0;
                Entries.Add(new Entry { def = def, count = 1, instance = new EquipmentInstance(def, up) });
            }
            else
                Entries.Add(new Entry { def = def, count = def.healAmount > 0 ? 5 : 1 });
        }
    }

    /// <summary>Drop <paramref name="count"/> of an item into the bag (GM/testing + future loot). Equipment
    /// adds one entry per copy (each its own <see cref="EquipmentInstance"/>); stackables merge into an existing
    /// non-equip stack. The single add-item path — callers never poke <see cref="Entries"/> directly.</summary>
    public void Give(Item def, int count)
    {
        if (def == null || count <= 0) return;
        if (def.slot != EquipSlotType.None)
        {
            for (int i = 0; i < count; i++) GiveEquipment(def);   // each copy rolls its own identity (and notifies)
            return;
        }
        var e = Entries.Find(x => x.def == def && !x.IsEquip);
        if (e != null) e.count += count;
        else Entries.Add(new Entry { def = def, count = count, isNew = true });
        Notify();
    }

    /// <summary>Add ONE rolled copy of a piece of equipment and hand it back, so the caller can report what fate
    /// gave it (rarity + kit). <paramref name="rarity"/> forces the drop's rarity; null rolls it off the drop table.
    /// The single place a wearable copy is born — loot, GM grants and rewards all land here, so nothing can enter
    /// the bag having skipped its roll.</summary>
    public EquipmentInstance GiveEquipment(Item def, Rarity? rarity = null)
    {
        if (def == null || def.slot == EquipSlotType.None) return null;
        var inst = EquipmentInstance.CreateRolled(def, rarity);
        Entries.Add(new Entry { def = def, count = 1, instance = inst, isNew = true });
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

    public int TotalWeight
    {
        get
        {
            int w = 0;
            foreach (var e in Entries) if (e.def != null) w += e.def.weight * Mathf.Max(1, e.count);
            return w;
        }
    }

    public void Notify()
    {
        OnChanged?.Invoke();
        // Bag-only changes (give/consume/reorder) don't touch Character's stat events, so flag the autosave
        // here too; equip/unequip already mark dirty via Character.OnStatsChanged.
        SaveManager.Instance?.MarkDirty();
    }

    /// <summary>Equip an inventory entry; any item already in that slot returns to the bag.</summary>
    public void Equip(Entry e)
    {
        if (e == null || !e.IsEquip || e.instance == null || Character.Instance == null) return;
        var slot = e.def.slot;
        var prev = Character.Instance.GetEquippedInstance(slot);
        Character.Instance.SetEquipped(slot, e.instance);
        Entries.Remove(e);
        if (prev != null && prev.def != null)
            Entries.Add(new Entry { def = prev.def, count = 1, instance = prev });
        Notify();
    }

    public void Unequip(EquipSlotType slot)
    {
        if (Character.Instance == null) return;
        var inst = Character.Instance.GetEquippedInstance(slot);
        if (inst == null) return;
        Character.Instance.SetEquipped(slot, (EquipmentInstance)null);
        if (inst.def != null) Entries.Add(new Entry { def = inst.def, count = 1, instance = inst });
        Notify();
    }

    public void Consume(Entry e)
    {
        if (e == null || e.def == null || e.def.healAmount <= 0) return;
        Character.Instance?.Heal(e.def.healAmount);
        if (--e.count <= 0) Entries.Remove(e);
        Notify();
    }

    /// <summary>Drag-to-sort: pull <paramref name="moving"/> out and re-insert it just before
    /// <paramref name="before"/> (others shift up). A null <paramref name="before"/> appends to the
    /// end (dropped on an empty cell). No-op if nothing actually changes.</summary>
    public void Reorder(Entry moving, Entry before)
    {
        if (moving == null || moving == before) return;
        int from = Entries.IndexOf(moving);
        if (from < 0) return;
        Entries.RemoveAt(from);
        int idx = before != null ? Entries.IndexOf(before) : Entries.Count;
        if (idx < 0) idx = Entries.Count;
        Entries.Insert(idx, moving);
        Notify();
    }
}
