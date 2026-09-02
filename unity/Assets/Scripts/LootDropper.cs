using System.Collections.Generic;
using UnityEngine;

// Cached at runtime so every pickup reuses one material instance (no per-spawn alloc).
// The shader is always in memory because the scene's mob sprites already reference it.
static class PickupMaterial
{
    static Material _mat;
    public static Material Get()
    {
        if (_mat != null) return _mat;
        var shader = Shader.Find("Unwritten/BillboardLit");
        if (shader != null) _mat = new Material(shader) { name = "BillboardLit (pickup)" };
        return _mat;
    }
}

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
/// Hooks <see cref="Health.onDeath"/> and spawns <see cref="WorldPickup"/> objects (Zeny coins and/or
/// items) at the mob's death position. Configured per-archetype by <see cref="RigBuilder25D"/>.
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

    [Header("Scatter")]
    [Tooltip("Coins / items land within this radius of the death position (world units).")]
    public float scatterRadius = 0.5f;

    [Header("Visuals")]
    [Tooltip("Sprite used for the gold coin world pickup. Assign GoldCoin.png at build time.")]
    public Sprite coinSprite;

    CharacterController cc;   // the mob's capsule (fallback ground reference)
    SpriteRenderer mobSprite; // the mob's billboard — its BOTTOM is where the mob VISUALLY meets the ground

    void Awake()
    {
        GetComponent<Health>().onDeath.AddListener(DropLoot);
        cc = GetComponent<CharacterController>();
        mobSprite = GetComponentInChildren<SpriteRenderer>();
    }

    // The height loot should rest at = where the mob VISUALLY stands. The mob's sprite bottom is the only
    // reliable source: on field maps the visible ground mesh (bumpy overlapping grass tiles) sits well ABOVE
    // the flat walk collider, so a ground raycast OR the capsule bottom both read too low and loot sinks out of sight.
    // The sprite's foot line follows the visible surface, so anchor to it (fall back to the capsule, then root).
    float SurfaceY()
    {
        if (mobSprite != null) return mobSprite.bounds.min.y;
        if (cc != null && cc.enabled) return cc.bounds.min.y;
        return transform.position.y;
    }

    const float GroundLift  = 0.06f;   // rest height above the surface — enough to avoid z-fight, small enough to read as "on the ground"
    const float DropArc     = 0.6f;    // apex of the pop-up hop
    const float DropDur     = 0.45f;   // seconds for the burst-out-and-land animation
    const float BurstOutHeight = 0.7f; // starting height (mob body) the item bursts from

    void DropLoot()
    {
        Vector3 body = transform.position;      // mob root — around its body/head, NOT the floor

        // Gold.
        if (Random.value <= goldDropChance)
        {
            int amount = Random.Range(goldMin, goldMax + 1);
            if (amount > 0) Drop(null, amount, body);
        }

        // Items — each entry is an independent roll. The table carries MATERIALS and other non-worn drops.
        foreach (var entry in lootTable)
        {
            if (entry.item == null) continue;
            if (Random.value <= entry.chance) Drop(entry.item, 0, body);
        }

        DropWornGear(body);
    }

    [Header("Worn gear")]
    [Tooltip("Chance (0–1) that each EQUIPPED piece drops when this unit dies. Its worn set IS its gear drop table — " +
             "there's no separate list to keep in sync, so a mob can never advertise gear it isn't actually carrying.")]
    [Range(0f, 1f)] public float wornDropChance = 0.15f;

    /// <summary>Chance a worn RING drops — deliberately its own number, and vanishingly small (0.001% = 1 in 100,000).
    /// A ring is the percentage passive layer, i.e. the single most valuable thing a unit can carry, so it's the
    /// MVP-card-tier prize rather than ordinary gear. Not a serialized field: this is a game-wide rule, not per-mob
    /// tuning, so there's one place it can ever be changed.</summary>
    public const float RingDropChance = 0.00001f;

    static readonly List<EquipmentInstance> wornScratch = new List<EquipmentInstance>(8);

    // A unit drops what it WEARS: the equipment that made it dangerous is the equipment you take off its corpse.
    // Rolled per piece, independently, like the loot table. Reuses one static scratch list — death is not a hot path,
    // but this runs per kill and there's no reason to allocate.
    void DropWornGear(Vector3 body)
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
            if (chance > 0f && Random.value <= chance) Drop(inst.def, 0, body);
        }
        wornScratch.Clear();
    }

    // A drop lands the same way whatever it is: burst out of the mob's body and arc down to a scattered spot,
    // resting at the mob's visible foot height (see SurfaceY) so it lands ON the grass the player sees.
    void Drop(Item item, int gold, Vector3 body)
    {
        float surfaceY = SurfaceY();
        Vector2 s = Random.insideUnitCircle * scatterRadius;

        Vector3 landing = new Vector3(body.x + s.x, surfaceY + GroundLift, body.z + s.y);
        Vector3 start   = new Vector3(body.x, surfaceY + BurstOutHeight, body.z);

        // ON A SERVER NOTHING IS BUILT HERE. A server has no screen, so a body it made would be lying where
        // nobody can see it; what every player needs is the shell that crosses the wire, and each of them
        // builds its own body from that — the same division a creature already lives under.
        if (GameNetwork.PublishLoot(item, gold, start, landing, gameObject.scene.name)) return;

        var pickup = BuildBody(item, gold, coinSprite, start, landing);
        // Dropped in THIS map, so it lies in this map and no other.
        MapManager.PutInMap(pickup.gameObject, gameObject.scene);
    }

    /// <summary>Build a drop's body and start its hop. The ONE description of what a drop lying on the ground
    /// IS, because it is built in two places now: here, by the world that dropped it, and on every machine
    /// watching a world it does not own (see <see cref="NetLoot"/>).</summary>
    public static WorldPickup BuildBody(Item item, int gold, Sprite coin, Vector3 start, Vector3 landing)
    {
        var go = new GameObject(item != null ? "ItemPickup_" + item.displayName : "GoldPickup")
                 { hideFlags = HideFlags.DontSaveInEditor };
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";
        var mat = PickupMaterial.Get();
        if (mat != null) sr.sharedMaterial = mat;

        var pickup = go.AddComponent<WorldPickup>();
        pickup.item = item;
        pickup.goldAmount = gold;
        pickup.coinSprite = coin;
        pickup.BeginDrop(start, landing, DropDur, DropArc);

        go.AddComponent<YDepthSorter>();
        return pickup;
    }

    /// <summary>Where a WATCHING machine finds the Zeny coin's picture. A creature carries its own (see
    /// <see cref="coinSprite"/>), but a watcher has no creature to read it off — a drop reaches it from a server
    /// as an amount and a place — so it loads its own copy by name, the way the net layer loads every other
    /// thing both halves must be able to build.</summary>
    const string CoinSpriteName = "GoldCoin";

    static Sprite watchedCoin;
    static bool lookedForWatchedCoin;

    public static Sprite WatchedCoinSprite()
    {
        if (lookedForWatchedCoin) return watchedCoin;
        lookedForWatchedCoin = true;

        // GoldCoin.png is a sheet of several sprites and the baker that fills in a mob's coin takes the FIRST
        // Sprite it finds there (see RigBuilder25D), so this takes the first one too.
        var sprites = Resources.LoadAll<Sprite>(CoinSpriteName);
        watchedCoin = sprites != null && sprites.Length > 0 ? sprites[0] : null;

        // SAID OUT LOUD, ONCE. Without it a coin dropped in a shared world lies there showing nothing at all,
        // which looks exactly like loot that was never sent.
        if (watchedCoin == null)
            Debug.LogError("[net] no sprite called '" + CoinSpriteName + "' in Resources — Zeny dropped in a "
                           + "shared world will lie on the ground with nothing to show for it.");
        return watchedCoin;
    }
}
