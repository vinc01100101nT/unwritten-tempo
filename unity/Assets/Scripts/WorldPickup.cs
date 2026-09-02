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
[RequireComponent(typeof(Rigidbody))]
public class WorldPickup : MonoBehaviour
{
    [Header("Reward — set one or the other")]
    [Tooltip("Zeny reward. 0 = not a gold pickup.")]
    public int goldAmount = 0;
    [Tooltip("Item to add to the player's bag. Null = gold pickup.")]
    public Item item;

    [Header("Visuals (gold pickup)")]
    [Tooltip("Sprite shown for a gold coin pickup. Item pickups use item.icon automatically.")]
    public Sprite coinSprite;
    [Tooltip("Size of the pickup billboard in world units.")]
    public float displaySize = 0.2f;
    [Tooltip("Lay the icon flat on the ground (face-up) instead of standing upright toward the camera. Flat icons don't bob.")]
    public bool layFlat = true;

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

    /// <summary>Fires when the player collects ANY pickup (the player is the only collector). Lets the baked
    /// character play its PickUp gesture without this knowing about the visual. Static so listeners don't need a ref.</summary>
    public static event System.Action OnCollected;

    // -- internals --
    SpriteRenderer sr;
    float baseY;
    float phase;          // random start phase so coins don't all bob in sync

    // RO-style drop hop: the item arcs out of the dying mob and lands on the ground.
    bool dropping;
    float dropT, dropDur, dropArc;
    Vector3 dropStart, dropEnd;

    /// <summary>Fling this pickup out of the mob along a parabola from <paramref name="start"/> to
    /// <paramref name="end"/> (the ground landing spot), popping up <paramref name="arc"/> world-units
    /// at the apex — the Ragnarok-style "loot bursts out and lands" hop.</summary>
    public void BeginDrop(Vector3 start, Vector3 end, float dur, float arc)
    {
        dropStart = start; dropEnd = end;
        dropDur = Mathf.Max(0.01f, dur); dropArc = arc;
        dropT = 0f; dropping = true;
        transform.position = start;
    }

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        baseY = transform.position.y;
        phase = Random.Range(0f, Mathf.PI * 2f);

        // Kinematic Rigidbody: bobbing moves the collider every frame, and moving a *static* collider
        // (no body) forces PhysX to re-cook the scene's static AABB tree each time. A kinematic body is
        // cheap to move and still raises OnTriggerEnter against the player's CharacterController.
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
    }

    // Start runs the frame AFTER AddComponent, so coinSprite / item are already assigned.
    void Start()
    {
        Sprite activeSprite = (item != null && item.icon != null) ? item.icon : coinSprite;
        if (activeSprite != null) sr.sprite = activeSprite;

        // Scale so the sprite occupies displaySize world units regardless of PPU.
        float ppu     = activeSprite != null ? activeSprite.pixelsPerUnit : 16f;
        float texSize = activeSprite != null ? Mathf.Max(activeSprite.rect.width, activeSprite.rect.height) : 16f;
        float scale   = displaySize * ppu / texSize;
        transform.localScale = Vector3.one * scale;

        // Lay the icon face-up on the ground (sprite face is -Z; +90° about X points it at +Y).
        // The extra yaw turns the sprite's "up" away from the camera so it reads right on the tilted RO cam.
        if (layFlat) transform.rotation = Quaternion.Euler(90f, 180f, 0f);

        // Sphere trigger — sized to collectRadius (in LOCAL space we undo the scale).
        var col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = collectRadius / scale;   // world-space radius = col.radius * localScale
    }

    void Update()
    {
        if (dropping)
        {
            dropT += Time.deltaTime;
            float u = Mathf.Clamp01(dropT / dropDur);
            var p = transform.position;
            p.x = Mathf.Lerp(dropStart.x, dropEnd.x, u);
            p.z = Mathf.Lerp(dropStart.z, dropEnd.z, u);
            // Straight-line height between start/end + a parabola that peaks (4·u·(1-u) = 1) at the midpoint.
            p.y = Mathf.Lerp(dropStart.y, dropEnd.y, u) + dropArc * 4f * u * (1f - u);
            transform.position = p;
            if (u >= 1f) { dropping = false; transform.position = dropEnd; baseY = dropEnd.y; }
            return;
        }

        if (layFlat) return;   // flat icons sit still on the ground — no bob

        // Gentle bob in Y.
        float y = baseY + Mathf.Sin(Time.time * bobSpeed + phase) * bobAmplitude;
        var pos = transform.position;
        pos.y = y;
        transform.position = pos;
    }

    /// <summary>Collect this pickup — in a world this machine owns, give the reward and destroy it; in one it is
    /// only watching, ASK for it and wait. Called by <see cref="Player3DController"/> when the player CLICKS the
    /// item and reaches it (no auto-collect: the player must click to pick up, and is movement-locked briefly
    /// while the gesture plays).</summary>
    public void Collect()
    {
        // ASKED FOR, NOT TAKEN. On a machine watching somebody else's world a drop is not this machine's to give
        // away: the server decides whether this person may have it, hands it to ONE of them, and takes it away for
        // everybody watching. Nothing happens here until it says so — otherwise two people clicking the same coin
        // would each walk away believing they had it.
        if (GameNetwork.SendPickupToServer(this)) return;

        Award(item, goldAmount, collectSfx);
        Destroy(gameObject);
    }

    /// <summary>Give what a drop was worth. The ONE description of what collecting pays, for a game with NO
    /// world — where this machine's bag is the only bag there is. In a world the server pays it into the bag it
    /// holds and this is never reached; see <see cref="Celebrate"/>, which is all that is left for the winner's
    /// own machine to do.</summary>
    public static void Award(Item item, int goldAmount, SfxSet collectSfx)
    {
        if (item != null)
        {
            var bag = InventoryStore.Instance;
            if (bag != null) bag.Give(item, 1);
            else Debug.LogWarning($"[unwritten] Nowhere to put {item.displayName} — no inventory yet.");
            Sfx.Play(collectSfx);
        }
        else if (goldAmount > 0)
        {
            Character.Instance?.AddGold(goldAmount);
            Sfx.Play(collectSfx);
        }
        OnCollected?.Invoke();   // let the baked player visual play its PickUp gesture
    }

    /// <summary>The half of collecting that is still this machine's in a world: the sound, and the reaching
    /// gesture. What the person actually GOT arrived separately, with the bag the server sent.</summary>
    public static void Celebrate(SfxSet collectSfx)
    {
        Sfx.Play(collectSfx);
        OnCollected?.Invoke();
    }
}
