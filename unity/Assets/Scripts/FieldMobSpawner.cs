using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RO-style field spawner: keeps a target POPULATION of mobs alive in an area, drawing random mob types from
/// the data catalog (<see cref="MonsterCatalog"/>) and respawning on a timer after each death — the same model
/// as a Ragnarok map's mob-spawn table ("keep N of this biome's mobs alive, respawn ~T seconds after a kill").
///
/// <para>Unlike the scene-BAKED mobs (fixed set stamped into the map, recycled in place by <see cref="MobRespawn"/>),
/// this spawns from data at runtime AND is backed by an OBJECT POOL: a dead mob is hidden and returned to a
/// per-type pool, and the next spawn of that type reuses it (<see cref="MobRespawn.Reactivate"/>) instead of
/// Destroy + re-Instantiate. So after the pool warms up (one object created per type it has spawned), steady-state
/// respawns allocate ~nothing — the Unity-cheap equivalent of RO's server-side "cheap mob struct" respawn.</para>
///
/// Perf: no <c>Update</c> — everything is event-driven (spawn on Start, respawn on death via a zero-alloc timer
/// coroutine). Prefab loads go through <see cref="MobSpawner"/> (Addressables, cached per type). See PERFORMANCE.md.
/// </summary>
public class FieldMobSpawner : MonoBehaviour
{
    [Header("Population")]
    [Tooltip("How many mobs this spawner keeps alive at once.")]
    [Min(0)] public int population = 12;
    [Tooltip("Spawn/respawn within this radius of the spawner (XZ), snapped to the Ground.")]
    public float radius = 12f;

    [Header("Respawn timing (RO-style)")]
    [Tooltip("Base seconds after a mob dies before its replacement appears.")]
    public float respawnDelay = 5f;
    [Tooltip("Extra 0..this random seconds added per respawn so they don't all pop back in sync.")]
    public float respawnJitter = 3f;

    [Header("Which mobs (drawn from the catalog like a map's biome table)")]
    [Tooltip("Empty = weighted mix of this biome's mobs in the tier band. A specific mob id/name (e.g. 'mob.slime') " +
             "= always that mob. 'Boss' = a random boss-flagged mob. Mirrors a recipe mob group's Archetype Name.")]
    public string fixedMobId = "";
    public Biome biome = Biome.Grassland;
    [Range(1, 4)] public int minTier = 1;
    [Range(1, 4)] public int maxTier = 4;
    [Tooltip("Also mix in biome-agnostic 'common' filler mobs (rats/bats/slimes). Only used when Fixed Mob Id is empty.")]
    public bool includeCommons = true;

    [Tooltip("Never respawn closer than this to the player (no spawning on their head).")]
    public float playerClearRadius = 6f;

    // Per-type pools of hidden, reusable mob objects (keyed by MonsterDef asset name = the Addressable key tail).
    readonly Dictionary<string, Stack<GameObject>> pool = new Dictionary<string, Stack<GameObject>>();
    int groundMask;
    Transform player;

    void Start()
    {
        groundMask = LayerMask.GetMask("Ground");
        var p = FindFirstObjectByType<Player3DController>();
        player = p != null ? p.transform : null;

        for (int i = 0; i < population; i++) SpawnOne();
    }

    // Fill one population slot: reuse a pooled mob of a freshly-picked type if we have one, else load+create it.
    void SpawnOne()
    {
        var def = PickDef();
        if (def == null) return;   // catalog empty / no eligible mobs — slot stays open

        Vector3 spot = PickSpot();
        string key = def.name;

        if (pool.TryGetValue(key, out var stack) && stack.Count > 0)
        {
            var reused = stack.Pop();
            if (reused != null)
            {
                reused.SetActive(true);
                MobRespawn.Reactivate(reused, spot);
                reused.GetComponent<MonsterAI3D>()?.SetHome(spot);   // wander around where it reappeared, not its first spawn
                return;
            }
        }

        // Nothing pooled for this type → create a fresh one from data (async; Addressables caches the prefab).
        MobSpawner.Spawn(def, spot, go =>
        {
            if (go == null) return;   // no baked/Addressable prefab for this def — slot stays open
            var pm = go.AddComponent<PooledMob>();
            pm.owner = this;
            pm.typeKey = key;
            go.GetComponent<MonsterAI3D>()?.SetHome(spot);
        });
    }

    // Which mob to spawn this slot: a fixed mob / boss if configured, else a weighted biome+tier pick (RO table).
    MonsterDef PickDef()
    {
        if (string.IsNullOrWhiteSpace(fixedMobId))
            return MonsterCatalog.WeightedPick(biome, minTier, maxTier, includeCommons);
        if (string.Equals(fixedMobId, "Boss", System.StringComparison.OrdinalIgnoreCase))
            return MonsterCatalog.RandomBoss();
        return MonsterCatalog.Get(fixedMobId);
    }

    /// <summary>A dead mob (its fade finished) hands itself back here: hide it, bank it in its type pool, and
    /// schedule a replacement so the population holds steady.</summary>
    public void ReturnToPool(PooledMob mob)
    {
        if (mob == null) return;
        var go = mob.gameObject;
        // Disable the CharacterController BEFORE deactivating: mobs shrink at runtime, so the authored 0.3 step
        // exceeds the scaled capsule bound. If it's still enabled, SetActive(true) on reuse re-validates it and
        // Unity throws "Step Offset must be ≤ …". Reactivate() re-enables it AFTER clamping stepOffset.
        var cc = go.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        go.SetActive(false);   // leaves MonsterAI3D.Active + stops its updates while banked

        if (!pool.TryGetValue(mob.typeKey, out var stack)) { stack = new Stack<GameObject>(); pool[mob.typeKey] = stack; }
        stack.Push(go);

        if (isActiveAndEnabled) StartCoroutine(RespawnAfterDelay());
    }

    IEnumerator RespawnAfterDelay()
    {
        float delay = respawnDelay + Random.value * Mathf.Max(0f, respawnJitter);
        // Zero-alloc wait (no per-death `new WaitForSeconds`): manual timer per PERFORMANCE.md.
        for (float el = 0f; el < delay; el += Time.deltaTime) yield return null;
        SpawnOne();
    }

    // A ground point within `radius` of this spawner, kept clear of the player; falls back to the spawner spot.
    Vector3 PickSpot()
    {
        Vector3 pPos = player != null ? player.position : transform.position;
        Vector3 c = transform.position;

        for (int i = 0; i < 16; i++)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 probe = new Vector3(c.x + r.x, c.y + 30f, c.z + r.y);
            if (groundMask != 0 &&
                Physics.Raycast(probe, Vector3.down, out var hit, 120f, groundMask, QueryTriggerInteraction.Ignore))
            {
                float dx = hit.point.x - pPos.x, dz = hit.point.z - pPos.z;
                if (dx * dx + dz * dz < playerClearRadius * playerClearRadius) continue;   // too close to the player
                return hit.point;
            }
        }
        return c;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
