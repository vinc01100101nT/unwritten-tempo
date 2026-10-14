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

    // WHAT IT WEARS IS NEVER TAKEN OFF IT. On CreatureLoot.GearDropChance of kills it drops a NEW copy of one random
    // piece of it — Raw, unfolded, its own trademark or word, and a rarity its kind rolls — and its own piece dies with it.

    /// <summary>A chance written the way it is shown: 0.2 → "20%", 0.015 → "1.5%".</summary>
    public static string ChanceText(float chance) =>
        (chance * 100f).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    Health health;
    UnitStatBlock block;
    UnitEquipment equipment;

    void Awake()
    {
        health = GetComponent<Health>();
        block = GetComponent<UnitStatBlock>();
        equipment = GetComponent<UnitEquipment>();
        health.onDeath.AddListener(GiveLoot);
    }

    // WHAT THIS DEATH PAID, gathered before any of it is handed over so the whole kill is one payment. One static list
    // because death is not a hot path but it does happen per kill, and there is never a second one being filled at the
    // same time.
    static readonly List<LootEntry> won = new List<LootEntry>(16);
    // The pieces the creature wears that a kill may hand out, read once per kill that rolls a piece.
    static readonly List<EquipmentInstance> wornScratch = new List<EquipmentInstance>(8);

    /// <summary>The item a material line gives up for a creature of <paramref name="level"/> — read by the roll below
    /// and by <c>/mobinfo</c>, so what a creature is said to drop is what it drops.</summary>
    public static Item MaterialFor(in CreatureLoot.Line line, int level)
    {
        int step = CreatureLoot.StepFor(line, level);
        switch (line.kind)
        {
            case MaterialKind.Ink:  return MaterialCatalog.Ink(step);
            case MaterialKind.Salt: return MaterialCatalog.Salt(step);
            default:                return null;
        }
    }

    /// <summary>True when <paramref name="piece"/> is one a kill may hand out: a piece of gear made of something. A piece
    /// made of nothing is worn for its look alone and is never handed out.</summary>
    public static bool Droppable(Item piece) => piece != null && piece.slot != EquipSlotType.None && piece.madeOf != MaterialKind.None;

    // One more thing this kill gave up.
    static void Won(Item item, int count, EquipmentInstance copy, float chance)
        => won.Add(new LootEntry { item = item, count = count, copy = copy, chance = chance });

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

        // Items — each entry is an independent roll. The table carries MATERIALS and other non-worn drops.
        foreach (var entry in lootTable)
        {
            if (entry.item == null) continue;
            if (Random.value <= entry.chance) Won(entry.item, 1, null, entry.chance);
        }

        // THE CHANCE OF AN INK OR A SALT — what its kind says it carries.
        int level = block != null ? block.Level : 1;
        var lines = CreatureLoot.LinesFor(lootKind);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!CreatureLoot.DropsAt(line, level) || !CombatRng.Chance(line.chance)) continue;
            var item = MaterialFor(line, level);
            if (item == null) continue;
            Won(item, CombatRng.Range(line.min, line.max + 1), null, line.chance);
        }

        // TEMPER DUST AND QUENCH OIL, on every kill — by its FILE's level, never a level it grew by hitting people (the
        // same guard its experience has), its loot kind and its file's gear. A creature with no file pays as ungeared.
        var def = MonsterCatalog.ById(health.creatureId);
        int fileLevel = def != null ? def.level : level;
        var gearGrade = def != null ? def.gearQuality : Quality.Raw;
        int gearFolds = def != null ? def.gearFolds : 0;
        WonUnits(MaterialCatalog.TemperDust, CreatureLoot.UnitsPerKill(CreatureLoot.TemperDustPerGrade, fileLevel, lootKind,
                                                                        gearGrade, gearFolds, CombatRng.Range(0, 100)));
        WonUnits(MaterialCatalog.QuenchOil, CreatureLoot.UnitsPerKill(CreatureLoot.QuenchOilPerGrade, fileLevel, lootKind,
                                                                       gearGrade, gearFolds, CombatRng.Range(0, 100)));

        // ONE PIECE OF WHAT IT WEARS, on one kill in ten: a new copy, its rarity rolled by the creature's OWN kind — a
        // rebel's piece is its creature's, never lifted a kind.
        if (equipment != null && CombatRng.Chance(CreatureLoot.GearDropChance))
        {
            equipment.CopyWornInto(wornScratch);
            for (int i = wornScratch.Count - 1; i >= 0; i--)
                if (wornScratch[i] == null || !Droppable(wornScratch[i].def)) wornScratch.RemoveAt(i);
            if (wornScratch.Count > 0)
            {
                var piece = wornScratch[CombatRng.Range(0, wornScratch.Count)].def;
                var rarity = CreatureLoot.DropRarity(health.kind, CombatRng.Range(0, CreatureLoot.DropRarityRollSize));
                Won(piece, 1, EquipmentInstance.CreateDropped(piece, rarity), CreatureLoot.GearDropChance / wornScratch.Count);
            }
            wornScratch.Clear();
        }

        // AND ITS FIND — the food or the converter its element carries — on its kind's chance. A certain find is not
        // rolled: the roll's top value would lose it.
        float findChance = CreatureFinds.Chance(lootKind);
        if (findChance >= 1f || CombatRng.Chance(findChance))
        {
            var find = CreatureFinds.Roll(health.element);
            if (find != null) Won(find, 1, null, findChance);
        }

        // THE MATERIALS ONLY THIS CREATURE DROPS. A rebel rolls them AGAIN at the creature's own chances — its own kind,
        // never the one-kind-up its grade loot above is lifted to, or a normal creature's rarest material would fall
        // fifty times as often from its rebel.
        pileItems.Clear();
        pileCounts.Clear();
        if (def != null)
        {
            RollOwnMaterial(def.material, health.kind);
            RollOwnMaterial(def.rareMaterial, health.kind);
        }

        // NOTHING AN ELITE DROPS IS AUTO-LOOTED: all of it falls, so an elite is hunted by hand and never farmed by
        // standing in its field with auto-combat on.
        Rewards.PayLoot(winner, won, health.kind == CreatureKind.Elite, 0, health, pileItems, pileCounts);
        won.Clear();
        pileItems.Clear();
        pileCounts.Clear();
    }

    // Some units of a material every kill drops — certain, so its chance is 1. Nothing when it rounded to none.
    static void WonUnits(Item material, int units)
    {
        if (material != null && units > 0) Won(material, units, null, 1f);
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
        if (!CreatureMaterials.InPiles(kind, tier)) { Won(item, 1, null, chance); return; }

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
