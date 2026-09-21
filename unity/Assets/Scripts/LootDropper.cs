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

    // NOTHING WORN IS EVER GIVEN UP. Gear is made, not taken off a corpse: a creature drops materials, consumables
    // and gold, and its own gear dies with it. The worn roll that used to live here is gone for that reason.

    /// <summary>A chance written the way it is shown: 0.2 → "20%", 0.015 → "1.5%".</summary>
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

    void GiveLoot()
    {
        // ASKED ONCE, BEFORE A SINGLE DIE IS ROLLED. Loot nobody can be paid is loot that should never have been
        // rolled, and asking again per item could pay two different people out of one death.
        var winner = Rewards.WhoEarned(health);
        if (PlayerUnits.Gone(winner) || winner is not Character) return;

        won.Clear();
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

        Rewards.PayLoot(winner, won, gold, health);
        won.Clear();
    }
}
