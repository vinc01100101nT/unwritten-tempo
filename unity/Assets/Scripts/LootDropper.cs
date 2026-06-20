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
/// items) at the mob's death position. Configured per-archetype by <see cref="Build25DSpike"/>.
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

    void Awake()
    {
        GetComponent<Health>().onDeath.AddListener(DropLoot);
    }

    void DropLoot()
    {
        // Use the mob's actual world position — don't force Y=0, terrain may not be flat.
        Vector3 foot = transform.position;

        // Gold.
        if (Random.value <= goldDropChance)
        {
            int amount = Random.Range(goldMin, goldMax + 1);
            if (amount > 0) SpawnCoin(amount, foot);
        }

        // Items — each entry is an independent roll.
        foreach (var entry in lootTable)
        {
            if (entry.item == null) continue;
            if (Random.value <= entry.chance) SpawnItem(entry.item, foot);
        }
    }

    void SpawnCoin(int amount, Vector3 foot)
    {
        var go = new GameObject("GoldPickup");
        go.transform.position = Scatter(foot);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";
        var mat = PickupMaterial.Get();
        if (mat != null) sr.sharedMaterial = mat;

        var pickup = go.AddComponent<WorldPickup>();
        pickup.goldAmount = amount;
        pickup.coinSprite  = coinSprite;

        go.AddComponent<Billboard>();
        go.AddComponent<YDepthSorter>();
    }

    void SpawnItem(Item item, Vector3 foot)
    {
        var go = new GameObject($"ItemPickup_{item.displayName}");
        go.transform.position = Scatter(foot);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";
        var mat = PickupMaterial.Get();
        if (mat != null) sr.sharedMaterial = mat;

        var pickup = go.AddComponent<WorldPickup>();
        pickup.item = item;

        go.AddComponent<Billboard>();
        go.AddComponent<YDepthSorter>();
    }

    Vector3 Scatter(Vector3 foot)
    {
        Vector2 s = Random.insideUnitCircle * scatterRadius;
        return foot + new Vector3(s.x, 0.1f, s.y);
    }
}
