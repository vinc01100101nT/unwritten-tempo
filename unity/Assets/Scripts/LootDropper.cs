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
/// <see cref="Health.onDeath"/> and hands everything that won its roll to whoever earned the kill — every item onto the
/// ground where the creature fell (<see cref="Rewards.PayLoot"/>). A death pays NO gold: gold comes only from saving a
/// character that never fights (see <see cref="SavedCharacters"/>). Configured per-archetype by <see cref="RigBuilder25D"/>.
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
    [Header("Item drops")]
    [Tooltip("Each entry is rolled independently. Multiple items can drop from one kill.")]
    public ItemDropEntry[] lootTable = System.Array.Empty<ItemDropEntry>();

    // NOTHING WORN IS EVER GIVEN UP. Gear is made, not taken off a corpse: a creature drops materials and
    // consumables, and its own gear dies with it. The worn roll that used to live here is gone for that reason.

    /// <summary>A chance written the way it is shown: 0.2 → "20%", 0.015 → "1.5%".</summary>
    public static string ChanceText(float chance) =>
        (chance * 100f).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    /// <summary>The chance a kill also gives up one random consumable — a potion or a food. Materials are never in this
    /// roll: they come from the creature's grade (<see cref="CreatureLoot"/>).</summary>
    public const float RandomItemChance = 0.30f;

    Health health;
    UnitStatBlock block;

    void Awake()
    {
        health = GetComponent<Health>();
        block = GetComponent<UnitStatBlock>();
        health.onDeath.AddListener(GiveLoot);
    }

    // WHAT THIS DEATH PAID, gathered before any of it is handed over so the whole kill is one payment. One static list
    // because death is not a hot path but it does happen per kill, and there is never a second one being filled at the
    // same time.
    static readonly List<Item> won = new List<Item>(40);
    // The drop chance (0–1) each entry of `won` won its roll at, index for index — what auto-loot's rate limit reads.
    static readonly List<float> wonAt = new List<float>(40);

    /// <summary>The item a material line gives up for a creature of <paramref name="level"/> — read by the roll below
    /// and by <c>/mobinfo</c>, so what a creature is said to drop is what it drops.</summary>
    public static Item MaterialFor(in CreatureLoot.Line line, int level)
    {
        int step = CreatureLoot.StepFor(line, level);
        switch (line.kind)
        {
            case MaterialKind.Ink:  return MaterialCatalog.Ink(step);
            case MaterialKind.Salt: return MaterialCatalog.Salt(step);
            default:                return MaterialCatalog.Band(line.kind, step);
        }
    }

    void GiveLoot()
    {
        // ASKED ONCE, BEFORE A SINGLE DIE IS ROLLED. Loot nobody can be paid is loot that should never have been
        // rolled, and asking again per item could pay two different people out of one death.
        var winner = Rewards.WhoEarned(health);
        if (PlayerUnits.Gone(winner) || winner is not Character) return;
        // A SUMMON CARRIES NOTHING — see Rewards.PayForKill for why.
        if (health.IsSummoned) return;
        // A SAVED CHARACTER carries something only if it is a rebel, and then it carries what one kind up carries —
        // the rare encounter a rebel is. One that helped or left carries nothing.
        var saved = health.savedAs;
        if (saved != null && saved.behaviour != SavedBehaviour.Rebel) return;
        var lootKind = saved != null ? SavedCharacterRules.LootKindOfRebel(health.kind) : health.kind;

        won.Clear();
        wonAt.Clear();

        // Items — each entry is an independent roll. The table carries MATERIALS and other non-worn drops.
        foreach (var entry in lootTable)
        {
            if (entry.item == null) continue;
            if (Random.value <= entry.chance) { won.Add(entry.item); wonAt.Add(entry.chance); }
        }

        // ITS GRADE'S MATERIALS, and the chance of an Ink or a Salt — what its kind and level say it carries.
        int level = block != null ? block.Level : 1;
        var lines = CreatureLoot.LinesFor(lootKind);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!CombatRng.Chance(line.chance)) continue;
            var item = MaterialFor(line, level);
            if (item == null) continue;
            int count = CombatRng.Range(line.min, line.max + 1);
            for (int n = 0; n < count; n++) { won.Add(item); wonAt.Add(line.chance); }
        }

        // AND ONE RANDOM CONSUMABLE, on its own chance.
        if (CombatRng.Chance(RandomItemChance))
        {
            var extra = ItemCatalog.RandomConsumable(health.element);
            if (extra != null) { won.Add(extra); wonAt.Add(RandomItemChance); }
        }

        // THE MATERIALS ONLY THIS CREATURE DROPS. A rebel rolls them AGAIN at the creature's own chances — its own kind,
        // never the one-kind-up its grade loot above is lifted to, or a normal creature's rarest material would fall
        // fifty times as often from its rebel.
        pileItems.Clear();
        pileCounts.Clear();
        var def = MonsterCatalog.ById(health.creatureId);
        if (def != null)
        {
            RollOwnMaterial(def.material, health.kind);
            RollOwnMaterial(def.rareMaterial, health.kind);
        }

        // NOTHING AN ELITE DROPS IS AUTO-LOOTED: no chances handed over means all of it falls, so an elite is hunted by
        // hand and never farmed by standing in its field with auto-combat on.
        Rewards.PayLoot(winner, won, health.kind == CreatureKind.Elite ? null : wonAt, 0, health, pileItems, pileCounts);
        won.Clear();
        wonAt.Clear();
        pileItems.Clear();
        pileCounts.Clear();
    }

    // Piles that go down APART, one entry each — an elite's ordinary material. Filled and emptied inside one death.
    static readonly List<Item> pileItems = new List<Item>(8);
    static readonly List<int> pileCounts = new List<int>(8);

    void RollOwnMaterial(Item item, CreatureKind kind)
    {
        if (!CreatureMaterials.Is(item)) return;
        var tier = CreatureMaterials.TierOfStep(item.materialStep);
        float chance = CreatureMaterials.Chance(kind, tier);
        if (chance <= 0f || !CombatRng.Chance(chance)) return;
        if (!CreatureMaterials.InPiles(kind, tier)) { won.Add(item); wonAt.Add(chance); return; }

        CreatureMaterials.Piles(kind, tier, out int pilesMin, out int pilesMax);
        CreatureMaterials.PileSize(kind, tier, out int sizeMin, out int sizeMax);
        int piles = CombatRng.Range(pilesMin, pilesMax + 1);
        for (int p = 0; p < piles; p++)
        {
            pileItems.Add(item);
            pileCounts.Add(CombatRng.Range(sizeMin, sizeMax + 1));
        }
    }
}
