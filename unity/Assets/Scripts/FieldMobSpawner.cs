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
    [Tooltip("Empty = weighted mix of this biome's mobs in the level band. A specific mob id/name (e.g. 'mob.slime') " +
             "= always that mob. 'Boss' = a random boss-flagged mob. Mirrors a recipe mob group's Archetype Name.")]
    public string fixedMobId = "";
    public Biome biome = Biome.Grassland;
    public int minLevel = 1;
    public int maxLevel = 99;
    [Tooltip("Also mix in biome-agnostic 'common' filler mobs (rats/bats/slimes). Only used when Fixed Mob Id is empty.")]
    public bool includeCommons = true;

    [Tooltip("Never respawn closer than this to the player (no spawning on their head). Authored PER SPAWNER — the " +
             "map recipe writes it (MapRecipe.clearFromSpawn), so a tight courtyard and an open field can differ.")]
    public float playerClearRadius = 6f;

    [Header("Shadows of the Past (live-3D mobs)")]
    [Range(0f, 1f)]
    [Tooltip("Fraction of this spawner's mobs that appear as live-3D characters instead of billboard sprites. " +
             "0 = every mob is a sprite (unchanged). Purely a VISUAL swap — a shadow keeps the same brain, stats, " +
             "loot and death as any other mob, and copies its body from the live 3D player (no setup needed).")]
    public float shadowFraction = 0f;

    // Per-type pools of hidden, reusable mob objects (keyed by MonsterDef asset name = the Addressable key tail).
    readonly Dictionary<string, Stack<GameObject>> pool = new Dictionary<string, Stack<GameObject>>();
    int groundMask;

    void Start()
    {
        // ONE world, so ONE set of creatures. Only the machine that owns the world populates it; every other
        // machine receives those mobs over the wire (see NetMob). A client that spawned its own would give
        // each player a private set of monsters at private positions with private health — which is what
        // this game did before it had a server, and is the thing that makes shared combat impossible.
        //
        // ONLY THE SERVER POPULATES A WORLD. A player is never the authority for anything now, so there is no
        // longer a case where this machine should make its own creatures — no offline game to fall back to, and
        // no window during a handshake to slip through. A player receives what the world already has (see NetMob).
        if (!GameServer.IsServer)
        {
            enabled = false;
            return;
        }

        groundMask = LayerMask.GetMask("Ground");

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
                // SWITCHING IT ON IS REACTIVATE'S JOB. Doing it here first validates the body's controller
                // against the step it was banked with, before anything has fitted that step to its size — the
                // burst of "Step Offset must be ≤ …" the watching side used to throw for exactly this reason.
                MobRespawn.Reactivate(reused, spot);
                reused.GetComponent<MonsterAI3D>()?.SetHome(spot);   // wander around where it reappeared, not its first spawn
                // A pooled mob coming back is a NEW creature as far as every watching player is concerned:
                // its old shell died with it, so it needs a fresh one. Missing this is how a respawned mob
                // becomes invisible to everyone but the server.
                GameNetwork.PublishMob(reused, def.id);
                return;
            }
        }

        // Nothing pooled for this type → create a fresh one from data (async; Addressables caches the prefab).
        bool asShadow = shadowFraction > 0f && Random.value < shadowFraction;
        MobSpawner.Spawn(def, spot, gameObject.scene, go =>
        {
            if (go == null) return;   // no baked/Addressable prefab for this def — slot stays open
            var pm = go.AddComponent<PooledMob>();   // perf-ok: once per mob CREATED; every later life comes from the pool
            pm.owner = this;
            pm.typeKey = key;
            go.GetComponent<MonsterAI3D>()?.SetHome(spot);   // perf-ok: once per mob created or pulled from the pool
            // A shadow is decided ONCE, at creation: the component builds a rigged body in its Start and the mob
            // then keeps it for life, including through pool reuse. Re-rolling per respawn would Instantiate a rig
            // per death — exactly the churn the pool exists to avoid — so a pooled mob stays whatever it was born as
            // and the live mix drifts from this fraction over a long session.
            //
            // DECIDED BEFORE IT IS ANNOUNCED. What crosses the wire is read off the creature at the moment it is
            // published, so adding this afterwards told every watching machine it was an ordinary sprite.
            if (asShadow) go.AddComponent<MobShadow3DVisual>();   // perf-ok: once per mob created; a pooled mob keeps what it was born as
            // Born on the server → every connected player gets to see it. No-op offline.
            GameNetwork.PublishMob(go, def.id);
        });
    }

    // Which mob to spawn this slot: a fixed mob / boss if configured, else a weighted biome+level pick (RO table).
    MonsterDef PickDef()
    {
        if (string.IsNullOrWhiteSpace(fixedMobId))
            return MonsterCatalog.WeightedPick(biome, minLevel, maxLevel, includeCommons);
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

    // WHEN EACH BANKED CREATURE IS DUE BACK, on the world's clock, one moment per return owed. The wait below
    // is a coroutine, and a coroutine stops dead when its object is switched off and never restarts itself — so
    // a map put to sleep with returns outstanding would wake up permanently short of those creatures, silently,
    // and lose more of its population every time it slept. A moment does not care that nothing was running.
    readonly List<System.DateTime> returnsOwed = new List<System.DateTime>();

    void OnEnable()
    {
        // BACK FROM BEING SWITCHED OFF with returns still owed. Anything whose moment passed while this was off
        // comes back at once; the rest pick up their waits with only the time genuinely left on them.
        for (int i = returnsOwed.Count - 1; i >= 0; i--)
        {
            System.DateTime due = returnsOwed[i];
            returnsOwed.RemoveAt(i);
            if (WorldClock.SecondsUntil(due) <= 0f) SpawnOne();
            else StartCoroutine(RespawnAtMoment(due));
        }
    }

    IEnumerator RespawnAfterDelay()
    {
        float delay = respawnDelay + Random.value * Mathf.Max(0f, respawnJitter);
        return RespawnAtMoment(WorldClock.UtcNow.AddSeconds(delay));
    }

    IEnumerator RespawnAtMoment(System.DateTime due)
    {
        returnsOwed.Add(due);
        // Zero-alloc wait (no per-death `new WaitForSeconds`): manual timer per PERFORMANCE.md. It counts the
        // frames it is actually run for; the moment above is what makes the wait survive not being run at all.
        for (float el = 0f, left = WorldClock.SecondsUntil(due); el < left; el += Time.deltaTime) yield return null;
        returnsOwed.Remove(due);
        SpawnOne();
    }

    // A ground point within `radius` of this spawner, kept clear of EVERYBODY; falls back to the spawner spot.
    //
    // It used to look the world over for "the" player once, at birth, and measure against whatever it found. On a
    // server that is nothing at all — so the clear-radius quietly measured the distance from the spawner to itself,
    // which is zero, and creatures could appear on top of anyone. Asked of the roll of who is here instead, which
    // has an answer on every machine and has as many answers as there are people.
    Vector3 PickSpot()
    {
        Vector3 c = transform.position;

        for (int i = 0; i < 16; i++)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 probe = new Vector3(c.x + r.x, c.y + 30f, c.z + r.y);
            if (groundMask != 0 &&
                Physics.Raycast(probe, Vector3.down, out var hit, 120f, groundMask, QueryTriggerInteraction.Ignore))
            {
                // Clear of everybody IN THIS MAP. Judged across all of them, a spot is refused because somebody is
                // standing on the same coordinates in a different place entirely.
                if (PlayerUnits.AnyoneWithin(hit.point, playerClearRadius, gameObject.scene.name)) continue;
                return hit.point;
            }
        }
        // NOT ONE OF SIXTEEN TRIES FOUND A FLOOR. Returning the spawner's own spot regardless is how a creature
        // ends up somewhere nobody checked; saying nothing about it is how that goes unnoticed for months. Once
        // per spawner, so a field that cannot see its ground does not drown the log.
        if (!warnedNoFloor)
        {
            warnedNoFloor = true;
            GameLog.Warn(LogCat.Spawn, "'" + gameObject.scene.name + "' found no ground under " + name
                             + " in sixteen tries — its creatures are being placed unchecked.");
        }
        return c;
    }

    bool warnedNoFloor;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
