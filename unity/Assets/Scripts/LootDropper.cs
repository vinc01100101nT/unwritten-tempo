using System.Collections.Generic;
using System.Globalization;
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
/// <see cref="Health.onDeath"/> and hands everything that won its roll to whoever earned the kill — Zeny straight into
/// their purse, and every item onto the ground where the creature fell (<see cref="Rewards.PayLoot"/>). Configured
/// per-archetype by <see cref="RigBuilder25D"/>.
///
/// <para><b>What falls is kept for the killer first.</b> Loot on the ground is a race — two people reaching for the same
/// thing, one of them quietly told no — so a kill's drops are held for whoever earned the kill for
/// <see cref="GroundDrop.KillerOnlySeconds"/>, then anybody may take them, and whatever nobody takes goes away after
/// <see cref="GroundDrop.KillDropLifetimeSeconds"/>. Who earned it is <see cref="Rewards.WhoEarned"/>'s answer and
/// nothing else's.</para>
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

    /// <summary>Chance a worn RING drops — deliberately its own number, and vanishingly small (0.001% = 1 in 100,000).
    /// A ring is the percentage passive layer, i.e. the single most valuable thing a unit can carry, so it's the
    /// MVP-card-tier prize rather than ordinary gear. Not a serialized field: this is a game-wide rule, not per-mob
    /// tuning, so there's one place it can ever be changed.</summary>
    public const float RingDropChance = 0.00001f;

    // The chance a worn piece other than a ring is given up, by the copy's rarity: Common, Uncommon, Rare, Epic,
    // Legendary. A game-wide rule for the same reason the ring's is — a creature's worn set IS its gear drop table, so
    // the rarer the piece it wears, the rarer the piece it gives up.
    static readonly float[] WornDropChances = { 0.20f, 0.12f, 0.06f, 0.03f, 0.01f };

    /// <summary>The chance <paramref name="worn"/> is given up when its wearer dies. THE one answer — the roll below and
    /// the line on a creature's health bar both read it, so what the bar promises is what the roll does.</summary>
    public static float WornDropChance(EquipmentInstance worn)
    {
        if (worn == null || worn.def == null) return 0f;
        return worn.def.slot == EquipSlotType.Ring ? RingDropChance : WornDropChances[(int)worn.Rarity];
    }

    /// <summary>A chance written the way it is shown: 0.2 → "20%", 0.015 → "1.5%", 0.00001 → "0.001%".</summary>
    public static string ChanceText(float chance) =>
        (chance * 100f).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    /// <summary>The chance a kill also gives up one random item that is not equipment — a stand-in for authored drop
    /// lists until they exist.</summary>
    public const float RandomItemChance = 0.30f;

    Health health;

    void Awake()
    {
        health = GetComponent<Health>();
        health.onDeath.AddListener(GiveLoot);
    }

    // WHAT THIS DEATH PAID, gathered before any of it is handed over so the whole kill is one payment. One static list
    // because death is not a hot path but it does happen per kill, and there is never a second one being filled at the
    // same time.
    static readonly List<Item> won = new List<Item>(8);
    // WORN pieces given up are handed over as the very copies the creature wore — their rolled numbers, their kit —
    // never re-rolled on the way to the ground. Gathered beside the table's items so one kill is still one payment.
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

        // AND ONE RANDOM THING THAT IS NOT EQUIPMENT, on its own chance.
        if (CombatRng.Chance(RandomItemChance))
        {
            var extra = ItemCatalog.RandomUnworn();
            if (extra != null) won.Add(extra);
        }

        RollWornGear();

        Rewards.PayLoot(winner, won, wonWorn, gold, health);
        won.Clear();
        wonWorn.Clear();
    }

    // A unit gives up what it WEARS: the equipment that made it dangerous is the equipment you take off its corpse —
    // the exact copy, roll and all. Whether each piece drops is rolled independently, on its rarity's chance.
    void RollWornGear()
    {
        var gear = GetComponent<UnitEquipment>();
        if (gear == null || gear.Count == 0) return;

        gear.CopyWornInto(wornScratch);
        for (int i = 0; i < wornScratch.Count; i++)
        {
            var inst = wornScratch[i];
            if (inst?.def == null) continue;
            float chance = WornDropChance(inst);
            if (chance > 0f && Random.value <= chance) wonWorn.Add(inst);
        }
        wornScratch.Clear();
    }
}
