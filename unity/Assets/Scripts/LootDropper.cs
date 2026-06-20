using UnityEngine;

/// <summary>
/// Hooks <see cref="Health.onDeath"/> and spawns a <see cref="WorldPickup"/> (gold coin)
/// at the mob's death position. Configured per-archetype by <see cref="Build25DSpike"/>.
///
/// Design: probabilistic gold drop only for MVP. Item drops are a future extension
/// (wire an Item[] lootTable with per-item chances once inventory is fuller).
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

    [Header("Scatter")]
    [Tooltip("Coins land within this radius of the death position (world units).")]
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
        if (Random.value > goldDropChance) return;

        int amount = Random.Range(goldMin, goldMax + 1);
        if (amount <= 0) return;

        // Death position: use the transform before it's destroyed by Health → DeathDissolve.
        // (onDeath fires before Destroy, so transform is still valid here.)
        Vector3 foot = transform.position;
        foot.y = 0f;   // snap to ground plane

        // Small scatter so multiple pickups from AoE don't stack exactly.
        Vector2 scatter = Random.insideUnitCircle * scatterRadius;
        Vector3 spawnPos = foot + new Vector3(scatter.x, 0.1f, scatter.y);

        var go = new GameObject("GoldPickup");
        go.transform.position = spawnPos;
        go.tag = "Pickup";

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";

        var pickup = go.AddComponent<WorldPickup>();
        pickup.goldAmount  = amount;
        pickup.coinSprite  = coinSprite;

        // Billboard so the coin faces the camera (same pattern as mobs).
        go.AddComponent<Billboard>();
        // Depth-sort so it sits correctly under mobs / player.
        go.AddComponent<YDepthSorter>();
    }
}
