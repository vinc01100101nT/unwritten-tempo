using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The runtime inventory model behind the new Inventory + Equipment windows: a flat list of
/// entries (equipment carries a unique <see cref="EquipmentInstance"/>; stackables carry a count).
/// Equipping/unequipping routes through <see cref="Character"/> so gear affects real combat stats.
/// Seeded from the starter <see cref="Item"/> assets in Resources/Items. Lives on the windows'
/// canvas; not saved yet (equipment persistence lands with the save milestone).
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
        Seed();
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
            for (int i = 0; i < count; i++)
                Entries.Add(new Entry { def = def, count = 1, instance = new EquipmentInstance(def, 0), isNew = true });
        else
        {
            var e = Entries.Find(x => x.def == def && !x.IsEquip);
            if (e != null) e.count += count;
            else Entries.Add(new Entry { def = def, count = count, isNew = true });
        }
        Notify();
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

    public void Notify() => OnChanged?.Invoke();

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
