using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a unit is WEARING — the equipment holder every combat unit carries, player and mob alike. It owns one
/// <see cref="EquipmentInstance"/> per <see cref="EquipSlotType"/> and folds them into a single
/// <see cref="EquipBonuses"/> total through the same <c>EquipBonuses.Aggregate</c> the player's sheet uses, so
/// there is no mob-only gear path and no second aggregation to keep in sync.
///
/// <para><b>Why a component:</b> gear has to travel with the spawned GameObject (a mob's worn set is also its drop
/// table — see <c>LootDropper</c>), and the derived-stat math needs to read it per unit, not per archetype.</para>
///
/// <para><b>Caching:</b> the aggregate is recomputed only when the set actually changes, so a per-hit stat read
/// never walks the dictionary. Nothing here runs per-frame.</para>
/// </summary>
public class UnitEquipment : MonoBehaviour
{
    [Tooltip("Gear this unit SPAWNS wearing. Each item is put in its own slot (Item.slot) as a fresh instance. This is " +
             "the serialized half — the live dictionary below is runtime-only, so what a mob wears has to be authored here.")]
    public Item[] startingGear = System.Array.Empty<Item>();

    readonly Dictionary<EquipSlotType, EquipmentInstance> equipped = new Dictionary<EquipSlotType, EquipmentInstance>();

    EquipBonuses cached;
    bool dirty = true;
    bool spawned;   // startingGear consumed? Lazy rather than Awake-driven so component order can't matter.

    // Equip the authored starting gear exactly once, on whichever access comes first. Doing this lazily (instead of in
    // Awake) means UnitStatBlock can read Bonuses/RingEquipped during ITS Awake without depending on which component
    // Unity happened to wake first.
    void EnsureSpawnedGear()
    {
        if (spawned) return;
        spawned = true;
        if (startingGear == null) return;
        for (int i = 0; i < startingGear.Length; i++)
        {
            var item = startingGear[i];
            if (item == null || item.slot == EquipSlotType.None) continue;
            equipped[item.slot] = new EquipmentInstance(item);
        }
        dirty = true;
    }

    /// <summary>The whole worn set folded into one bonus total. Cached; recomputed only after a change.</summary>
    public EquipBonuses Bonuses
    {
        get
        {
            EnsureSpawnedGear();
            if (dirty) { cached = EquipBonuses.Aggregate(equipped.Values); dirty = false; }
            return cached;
        }
    }

    /// <summary>True while a ring occupies the ring slot — the gate for the percentage passive layer.</summary>
    public bool RingEquipped => Get(EquipSlotType.Ring) != null;

    /// <summary>How many pieces are worn (what a drop roll iterates).</summary>
    public int Count { get { EnsureSpawnedGear(); return equipped.Count; } }

    public EquipmentInstance Get(EquipSlotType slot)
    {
        EnsureSpawnedGear();
        return equipped.TryGetValue(slot, out var inst) ? inst : null;
    }

    /// <summary>Put <paramref name="inst"/> in its slot (null clears it). Returns whatever was displaced.</summary>
    public EquipmentInstance Equip(EquipSlotType slot, EquipmentInstance inst)
    {
        EnsureSpawnedGear();
        var prev = Get(slot);
        if (inst == null) equipped.Remove(slot);
        else equipped[slot] = inst;
        dirty = true;
        return prev;
    }

    public EquipmentInstance Unequip(EquipSlotType slot) => Equip(slot, null);

    /// <summary>Every worn piece. Used by the derived-stat math and by death drops.</summary>
    public IEnumerable<EquipmentInstance> All { get { EnsureSpawnedGear(); return equipped.Values; } }

    /// <summary>Fill <paramref name="result"/> with the worn pieces (caller-owned list, no allocation here) —
    /// the shape <c>LootDropper</c> wants so a death roll doesn't allocate.</summary>
    public void CopyWornInto(List<EquipmentInstance> result)
    {
        if (result == null) return;
        EnsureSpawnedGear();
        result.Clear();
        foreach (var kv in equipped) if (kv.Value != null) result.Add(kv.Value);
    }
}
