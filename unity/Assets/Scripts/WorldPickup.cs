using UnityEngine;

/// <summary>
/// A shiny thing sitting in the 3D world (Zeny coin, item shard) that the player
/// collects by walking near it. Spawned by <see cref="LootDropper"/> when a mob dies.
///
/// Design: auto-collect on proximity (sphere trigger). A gentle bob animation makes it
/// visible from a distance. No click required — just walk through.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(SphereCollider))]
public class WorldPickup : MonoBehaviour
{
    [Header("Reward")]
    [Tooltip("How many Zeny this pickup is worth. 0 = it's an item pickup (future).")]
    public int goldAmount = 1;

    [Header("Visuals")]
    [Tooltip("Sprite shown in the world. Assign GoldCoin or SilverCoin from NinjaAdventure/Items/Treasure.")]
    public Sprite coinSprite;
    [Tooltip("Size of the pickup billboard in world units.")]
    public float displaySize = 0.35f;

    [Header("Bob")]
    [Tooltip("How far up/down the pickup bobs (world units).")]
    public float bobAmplitude = 0.08f;
    [Tooltip("Bob cycles per second.")]
    public float bobSpeed = 2.2f;

    [Header("Collect")]
    [Tooltip("Auto-collect when the player centre is this close (world units).")]
    public float collectRadius = 1.1f;
    [Tooltip("Sound played on pickup. Assign a coin jingle clip via LootDropper or leave empty.")]
    public SfxSet collectSfx;

    // -- internals --
    SpriteRenderer sr;
    float baseY;
    float phase;          // random start phase so coins don't all bob in sync

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (coinSprite != null) sr.sprite = coinSprite;

        // Scale so the sprite occupies displaySize world units regardless of PPU.
        float ppu = coinSprite != null ? coinSprite.pixelsPerUnit : 16f;
        float texSize = coinSprite != null ? Mathf.Max(coinSprite.rect.width, coinSprite.rect.height) : 16f;
        float scale = displaySize * ppu / texSize;
        transform.localScale = Vector3.one * scale;

        // Sphere trigger — sized to collectRadius (in LOCAL space we undo the scale).
        var col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = collectRadius / scale;   // world-space radius = col.radius * localScale

        baseY = transform.position.y;
        phase = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        // Gentle bob in Y.
        float y = baseY + Mathf.Sin(Time.time * bobSpeed + phase) * bobAmplitude;
        var pos = transform.position;
        pos.y = y;
        transform.position = pos;
    }

    void OnTriggerEnter(Collider other)
    {
        // Only the player collects pickups — check for the controller rather than relying on a tag.
        if (other.GetComponentInParent<Player3DController>() == null) return;
        Collect();
    }

    void Collect()
    {
        if (goldAmount > 0)
        {
            Wallet.Add(goldAmount);
            Sfx.Play(collectSfx);
        }
        Destroy(gameObject);
    }
}
