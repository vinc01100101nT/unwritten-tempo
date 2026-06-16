using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps a small pack of monsters alive around itself. It spawns copies of
/// <see cref="monsterPrefab"/> at random points within <see cref="spawnRadius"/>,
/// up to <see cref="maxAlive"/>, and tops the pack back up as they die (their
/// <see cref="Health"/> destroys them, which this prunes). Optionally only runs
/// while the player is nearby, so off-screen fields aren't churning monsters.
///
/// Drop it in a scene, point it at a monster prefab, and place it where you want
/// the pack to roam. Built by Tools ▸ unwritten ▸ Create Monster Spawner.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    [Tooltip("The monster prefab to spawn (built by Create Monster Spawner).")]
    public GameObject monsterPrefab;

    [Header("Pack")]
    [Tooltip("How many monsters to keep alive at once.")]
    public int maxAlive = 5;
    [Tooltip("Monsters spawn at random points within this radius of the spawner.")]
    public float spawnRadius = 6f;

    [Header("Timing")]
    [Tooltip("Fill the pack to maxAlive immediately on Play.")]
    public bool spawnOnStart = true;
    [Tooltip("Seconds between top-up spawn attempts.")]
    public float spawnInterval = 3f;
    [Tooltip("Only spawn while the player is within this distance (0 = always active).")]
    public float activationRange = 0f;

    readonly List<GameObject> alive = new List<GameObject>();
    float nextSpawn;
    Transform player;

    void Start()
    {
        var pc = FindFirstObjectByType<PlayerController2D>();
        if (pc != null) player = pc.transform;

        if (spawnOnStart)
            for (int i = 0; i < maxAlive; i++) TrySpawn();

        nextSpawn = Time.time + spawnInterval;
    }

    void Update()
    {
        alive.RemoveAll(m => m == null);   // drop the ones Health has destroyed

        if (Time.time < nextSpawn) return;
        nextSpawn = Time.time + spawnInterval;

        if (PlayerInRange()) TrySpawn();
    }

    bool PlayerInRange()
    {
        if (activationRange <= 0f) return true;
        return player != null &&
               Vector2.Distance(player.position, transform.position) <= activationRange;
    }

    void TrySpawn()
    {
        if (monsterPrefab == null || alive.Count >= maxAlive) return;
        Vector2 pos = (Vector2)transform.position + Random.insideUnitCircle * spawnRadius;
        alive.Add(Instantiate(monsterPrefab, pos, Quaternion.identity));
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
        if (activationRange > 0f)
        {
            Gizmos.color = new Color(1f, 1f, 0.4f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, activationRange);
        }
    }
}
