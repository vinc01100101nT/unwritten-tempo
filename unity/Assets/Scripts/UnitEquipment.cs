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
    [Tooltip("Gear this unit SPAWNS wearing. Each item is put in its own slot (Item.slot) as a copy at the three " +
             "settings below. This is the serialized half — the live dictionary below is runtime-only, so what a mob " +
             "wears has to be authored here.")]
    public Item[] startingGear = System.Array.Empty<Item>();

    [Tooltip("THE GRADE every starting piece comes to life at. A MIRROR of the creature's own catalog entry, which " +
             "owns these three and writes them here as the creature is made (MobSpawner) — never edited on a body.")]
    public Quality gearQuality = Quality.Raw;
    [Tooltip("How many times every starting piece is folded. Clamped to the grade's cap. A mirror of the catalog entry.")]
    public int gearFolds;
    [Tooltip("Which Ink writes the stats on every starting piece, 1 Watery to 5 Sharp. The GRADE decides how many " +
             "stats are written; this decides what each is worth. A mirror of the catalog entry.")]
    public int gearInkTier = 1;

    readonly Dictionary<EquipSlotType, EquipmentInstance> equipped = new Dictionary<EquipSlotType, EquipmentInstance>();

    EquipBonuses cached;
    bool dirty = true;
    bool spawned;   // startingGear consumed? Lazy rather than Awake-driven so component order can't matter.

    // Equip the authored starting gear exactly once, on whichever access comes first. Doing this lazily (instead of in
    // Awake) means UnitStatBlock can read Bonuses during ITS Awake without depending on which component
    // Unity happened to wake first.
    void EnsureSpawnedGear()
    {
        if (spawned) return;
        spawned = true;
        SeatStartingGear();
    }

    // Every starting piece as a copy AT THE THREE SETTINGS ABOVE — the same door a forged piece and a grant come
    // through, so nothing here is in a state a station could not have produced.
    void SeatStartingGear()
    {
        equipped.Clear();
        if (startingGear != null)
            for (int i = 0; i < startingGear.Length; i++)
            {
                var item = startingGear[i];
                if (item == null || item.slot == EquipSlotType.None) continue;
                // A WATCHING MACHINE ROLLS NOTHING. Its copy of a creature wears the plain authored pieces, which is all
                // it needs to draw what is held; the rolled copies live on the server's creature.
                equipped[item.slot] = GameNetwork.TheWorldIsElsewhere || !Application.isPlaying
                    ? new EquipmentInstance(item)
                    : EquipmentInstance.CreateAt(item, gearQuality, gearFolds, gearInkTier);
            }
        dirty = true;
        version++;
        OnChanged?.Invoke();
    }

    /// <summary>Raised whenever the worn set changes — seated, equipped, taken off or wiped. What a body showing the
    /// weapon in its hand listens to, so it redraws on the change and never polls.</summary>
    public event System.Action OnChanged;

    int version;

    /// <summary>Goes up every time the worn set changes. Lets a watcher of this unit notice a change by comparing one
    /// number instead of walking the set.</summary>
    public int Version { get { EnsureSpawnedGear(); return version; } }

    /// <summary>A respawn is a fresh creature, so it wears fresh copies: everything is re-rolled from the authored
    /// starting gear. New objects, never the old ones edited — a copy the last life gave up as a drop may be sitting in
    /// somebody's bag. Called from <c>MobRespawn.Reactivate</c>, the one revive choke point.</summary>
    public void ReseatForNewLife()
    {
        spawned = true;
        SeatStartingGear();
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

    /// <summary>How many pieces are worn (what a drop roll iterates).</summary>
    public int Count { get { EnsureSpawnedGear(); return equipped.Count; } }

    /// <summary>What this unit is holding, or <see cref="WeaponType.None"/> when its hands are empty. The single
    /// reading of "which weapon is worn", because more than one thing asks: the pose a swing plays is chosen from
    /// it on the machine that owns the unit, and again on every machine watching.</summary>
    public WeaponType WeaponType
    {
        get
        {
            var worn = Get(EquipSlotType.Weapon);
            return worn != null && worn.def != null ? worn.def.weaponType : WeaponType.None;
        }
    }

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
        version++;
        OnChanged?.Invoke();
        return prev;
    }

    public EquipmentInstance Unequip(EquipSlotType slot) => Equip(slot, null);

    /// <summary>Strip every slot. Used when a save load rebuilds the whole worn set from scratch — the caller then
    /// re-equips piece by piece. Also consumes the starting-gear step, so a load can't be re-seeded behind it.</summary>
    public void Clear()
    {
        spawned = true;      // a deliberate wipe must not be undone by lazy starting-gear seeding afterwards
        equipped.Clear();
        dirty = true;
        version++;
        OnChanged?.Invoke();
    }

    /// <summary>Every worn (slot, piece) pair — what a save walks. Skips empty slots.</summary>
    public IEnumerable<KeyValuePair<EquipSlotType, EquipmentInstance>> Worn
    {
        get
        {
            EnsureSpawnedGear();
            foreach (var kv in equipped)
                if (kv.Value != null && kv.Value.def != null) yield return kv;
        }
    }

    /// <summary>Aggregate the worn set with ONE slot swapped for <paramref name="hypothetical"/> (null = preview
    /// taking that slot off) — the equip-hover "ATK 220 → 248" preview. Pure: nothing is mutated.</summary>
    public EquipBonuses BonusesWith(EquipSlotType slot, EquipmentInstance hypothetical)
    {
        EnsureSpawnedGear();
        var hypo = default(EquipBonuses);
        bool slotSeen = false;
        foreach (var kv in equipped)
        {
            var inst = kv.Key == slot ? hypothetical : kv.Value;
            if (kv.Key == slot) slotSeen = true;
            inst?.AccumulateInto(ref hypo);
        }
        if (!slotSeen) hypothetical?.AccumulateInto(ref hypo);   // equipping into an empty slot
        return hypo;
    }

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
