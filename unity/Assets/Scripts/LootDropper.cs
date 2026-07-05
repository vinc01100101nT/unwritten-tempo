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
            if (amount > 0) SpawnCoin(amount, body);
        }

        // Items — each entry is an independent roll.
        foreach (var entry in lootTable)
        {
            if (entry.item == null) continue;
            if (Random.value <= entry.chance) SpawnItem(entry.item, body);
        }
    }

    void SpawnCoin(int amount, Vector3 body)
    {
        var go = new GameObject("GoldPickup");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";
        var mat = PickupMaterial.Get();
        if (mat != null) sr.sharedMaterial = mat;

        var pickup = go.AddComponent<WorldPickup>();
        pickup.goldAmount = amount;
        pickup.coinSprite = coinSprite;
        HopToGround(pickup, body);

        go.AddComponent<YDepthSorter>();
    }

    void SpawnItem(Item item, Vector3 body)
    {
        var go = new GameObject($"ItemPickup_{item.displayName}");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";
        var mat = PickupMaterial.Get();
        if (mat != null) sr.sharedMaterial = mat;

        var pickup = go.AddComponent<WorldPickup>();
        pickup.item = item;
        HopToGround(pickup, body);

        go.AddComponent<YDepthSorter>();
    }

    // Burst out of the mob's body and arc down to a scattered spot on the ground, resting at the mob's
    // visible foot height (see SurfaceY) so it always lands ON the grass the player sees, not under it.
    void HopToGround(WorldPickup pickup, Vector3 body)
    {
        float surfaceY = SurfaceY();
        Vector2 s = Random.insideUnitCircle * scatterRadius;

        Vector3 landing = new Vector3(body.x + s.x, surfaceY + GroundLift, body.z + s.y);
        Vector3 start   = new Vector3(body.x, surfaceY + BurstOutHeight, body.z);
        pickup.BeginDrop(start, landing, DropDur, DropArc);
    }
}
