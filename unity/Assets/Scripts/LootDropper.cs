using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One entry in a mob's item loot table: the item to drop and the probability (0–1) that it drops.
/// </summary>
[System.Serializable]
public class ItemDropEntry
{
    [Tooltip("The Item asset to drop.")]
    public Item item;
    [Range(0f, 1f)]
    [Tooltip("Drop probability per kill. 0.1 = 10 % chance.")]
    public float chance = 0.1f;
}

/// <summary>
/// WHAT A CREATURE IS CARRYING, and the rolls that decide how much of it survives its death. Hooks
/// <see cref="Health.onDeath"/> and hands everything that won its roll straight to whoever earned the kill —
/// Zeny into their purse, items into their bag. Configured per-archetype by <see cref="RigBuilder25D"/>.
///
/// <para><b>A KILL never leaves anything lying in the world.</b> Loot used to burst out of the body and land on
/// the ground as objects a player walked to and clicked, which on a server meant a race: two people clicking the
/// same coin, one of them quietly told no. It is now given directly, so there is nothing to reach, nothing to
/// compete for and nothing to leave behind. Who receives it is <see cref="Rewards.WhoEarned"/>'s answer and
/// nothing else's. A player putting something down on purpose is the other thing entirely, and that DOES lie
/// there for anybody to pick up — see <see cref="GroundDrop"/>.</para>
/// </summary>
[RequireComponent(typeof(Health))]
public class LootDropper : MonoBehaviour
{
    [Header("Gold drop")]
    [Tooltip("Minimum Zeny dropped (inclusive). 0 = always drops nothing if max is also 0.")]
    public int goldMin = 1;
    [Tooltip("Maximum Zeny dropped (inclusive).")]
    public int goldMax = 5;
    [Range(0f, 1f)]
    [Tooltip("Chance that any gold drops at all. 1 = always, 0 = never.")]
    public float goldDropChance = 0.85f;

    [Header("Item drops")]
    [Tooltip("Each entry is rolled independently. Multiple items can drop from one kill.")]
    public ItemDropEntry[] lootTable = System.Array.Empty<ItemDropEntry>();

    [Header("Worn gear")]
    [Tooltip("Chance (0–1) that each EQUIPPED piece drops when this unit dies. Its worn set IS its gear drop table — " +
             "there's no separate list to keep in sync, so a mob can never advertise gear it isn't actually carrying.")]
    [Range(0f, 1f)] public float wornDropChance = 0.15f;

    /// <summary>Chance a worn RING drops — deliberately its own number, and vanishingly small (0.001% = 1 in 100,000).
    /// A ring is the percentage passive layer, i.e. the single most valuable thing a unit can carry, so it's the
    /// MVP-card-tier prize rather than ordinary gear. Not a serialized field: this is a game-wide rule, not per-mob
    /// tuning, so there's one place it can ever be changed.</summary>
    public const float RingDropChance = 0.00001f;

    Health health;

    void Awake()
    {
        health = GetComponent<Health>();
        health.onDeath.AddListener(GiveLoot);
    }

    // WHAT THIS DEATH PAID, gathered before any of it is handed over so the whole kill is one payment: on a server
    // every payment sends the winner their entire bag and asks for it to be written down, so paying item by item
    // would send four bags and four writes for one dead creature. One static list because death is not a hot path
    // but it does happen per kill, and there is never a second one being filled at the same time.
    static readonly List<Item> won = new List<Item>(8);
    // WORN pieces given up are handed over as the very copies the creature wore — their rolled numbers, their kit —
    // never re-rolled at the bag. Gathered beside the table's items so one kill is still one payment.
    static readonly List<EquipmentInstance> wonWorn = new List<EquipmentInstance>(4);
    static readonly List<EquipmentInstance> wornScratch = new List<EquipmentInstance>(8);

    void GiveLoot()
    {
        // ASKED ONCE, BEFORE A SINGLE DIE IS ROLLED. Loot nobody can be paid is loot that should never have been
        // rolled, and asking again per item could pay two different people out of one death.
        var winner = Rewards.WhoEarned(health);
        if (PlayerUnits.Gone(winner) || winner is not Character) return;

        won.Clear();
        wonWorn.Clear();
        int gold = 0;

        if (Random.value <= goldDropChance)
        {
            int amount = Random.Range(goldMin, goldMax + 1);
            if (amount > 0) gold = amount;
        }

        // Items — each entry is an independent roll. The table carries MATERIALS and other non-worn drops.
        foreach (var entry in lootTable)
        {
            if (entry.item == null) continue;
            if (Random.value <= entry.chance) won.Add(entry.item);
        }

        RollWornGear();

        Rewards.PayLoot(winner, won, wonWorn, gold);
        won.Clear();
        wonWorn.Clear();
    }

    // A unit gives up what it WEARS: the equipment that made it dangerous is the equipment you take off its corpse —
    // the exact copy, roll and all. Whether each piece drops is rolled independently, like the loot table.
    void RollWornGear()
    {
        var gear = GetComponent<UnitEquipment>();
        if (gear == null || gear.Count == 0) return;

        gear.CopyWornInto(wornScratch);
        for (int i = 0; i < wornScratch.Count; i++)
        {
            var inst = wornScratch[i];
            if (inst?.def == null) continue;
            // A ring rolls on its own vanishingly-small chance; everything else uses this mob's normal gear rate.
            float chance = inst.def.slot == EquipSlotType.Ring ? RingDropChance : wornDropChance;
            if (chance > 0f && Random.value <= chance) wonWorn.Add(inst);
        }
        wornScratch.Clear();
    }
}
