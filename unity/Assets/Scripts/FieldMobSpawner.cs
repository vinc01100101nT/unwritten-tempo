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

    // NO RESPAWN FIELDS HERE. How long a creature stays gone is its KIND's (CreatureKinds.RespawnSeconds) — five
    // seconds for a normal, hours for an elite or a shadow — so one area can never give an elite a normal's wait.

    [Header("Which mobs (drawn from the catalog like a map's biome table)")]
    [Tooltip("Empty = weighted mix of this biome's normals in the level band. A creature's id or name (e.g. " +
             "'mob.slime') = always that creature — the way an elite or a shadow is placed. Filled in from the world " +
             "spawn list (WorldSpawns); nothing is saved in a map.")]
    public string fixedMobId = "";
    public Biome biome = Biome.Grassland;
    public int minLevel = 1;
    public int maxLevel = 99;
    [Tooltip("Also mix in biome-agnostic 'common' filler mobs (rats/bats/slimes). Only used when Fixed Mob Id is empty.")]
    public bool includeCommons = true;

    [Tooltip("Never respawn closer than this to the player (no spawning on their head). Authored PER SPAWNER — the " +
             "map recipe writes it (MapRecipe.clearFromSpawn), so a tight courtyard and an open field can differ.")]
    public float playerClearRadius = 6f;

    // Per-type pools of hidden, reusable mob objects (keyed by MonsterDef asset name = the Addressable key tail).
    readonly Dictionary<string, Stack<GameObject>> pool = new Dictionary<string, Stack<GameObject>>();
    // The creatures this area has out in the world right now — what it banks when it is put to sleep.
    readonly List<(GameObject go, MonsterAI3D ai)> live = new List<(GameObject go, MonsterAI3D ai)>();
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
                live.Add((reused, reused.GetComponent<MonsterAI3D>()));   // perf-ok: once per creature brought back
                // A pooled mob coming back is a NEW creature as far as every watching player is concerned:
                // its old shell died with it, so it needs a fresh one. Missing this is how a respawned mob
                // becomes invisible to everyone but the server.
                GameNetwork.PublishMob(reused, def.id);
                return;
            }
        }

        // Nothing pooled for this type → create a fresh one from data (async; Addressables caches the prefab).
        // A shadow's 3D body is put on by MobSpawner itself, from the creature's kind, before this ever sees it.
        MobSpawner.Spawn(def, spot, gameObject.scene, go =>
        {
            if (go == null) return;   // no baked/Addressable prefab for this def — slot stays open
            var pm = go.AddComponent<PooledMob>();   // perf-ok: once per mob CREATED; every later life comes from the pool
            pm.owner = this;
            pm.typeKey = key;
            pm.kind = def.kind;
            go.GetComponent<MonsterAI3D>()?.SetHome(spot);   // perf-ok: once per mob created or pulled from the pool
            live.Add((go, go.GetComponent<MonsterAI3D>()));   // perf-ok: once per mob created
            // Born on the server → every connected player gets to see it. No-op offline.
            GameNetwork.PublishMob(go, def.id);
        });
    }

    // Which creature fills this slot: the named one if this area names one, else a weighted biome+level pick of normals.
    MonsterDef PickDef()
    {
        if (string.IsNullOrWhiteSpace(fixedMobId))
            return MonsterCatalog.WeightedPick(biome, minLevel, maxLevel, includeCommons);
        var named = MonsterCatalog.Get(fixedMobId);
        if (named == null && !warnedUnknown)
        {
            warnedUnknown = true;
            GameLog.Warn(LogCat.Spawn, "'" + gameObject.scene.name + "' names a creature '" + fixedMobId + "' that is not on the roster — that area stays empty.");
        }
        // A SUMMON-ONLY BOSS never spawns on its own: the area stays empty and says why, once.
        if (named != null && SummonRecipes.IsSummonOnly(named.id))
        {
            if (!warnedSummonOnly)
            {
                warnedSummonOnly = true;
                GameLog.Warn(LogCat.Spawn, "'" + gameObject.scene.name + "' names '" + named.id + "', which only appears by summoning — that area stays empty.");
            }
            return null;
        }
        return named;
    }

    bool warnedUnknown, warnedSummonOnly;

    /// <summary>A dead mob (its fade finished) hands itself back here: hide it, bank it in its type pool, and
    /// schedule a replacement so the population holds steady.</summary>
    public void ReturnToPool(PooledMob mob)
    {
        if (mob == null) return;
        Bank(mob);
        if (isActiveAndEnabled) StartCoroutine(RespawnAfterDelay(mob.kind));
    }

    // Hide a creature and put it in its type's pool, ready to be used again.
    void Bank(PooledMob mob)
    {
        var go = mob.gameObject;
        for (int i = live.Count - 1; i >= 0; i--) if (live[i].go == go) { live.RemoveAt(i); break; }
        // Disable the CharacterController BEFORE deactivating: mobs shrink at runtime, so the authored 0.3 step
        // exceeds the scaled capsule bound. If it's still enabled, SetActive(true) on reuse re-validates it and
        // Unity throws "Step Offset must be ≤ …". Reactivate() re-enables it AFTER clamping stepOffset.
        var cc = go.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        go.SetActive(false);   // leaves MonsterAI3D.Active + stops its updates while banked

        if (!pool.TryGetValue(mob.typeKey, out var stack)) { stack = new Stack<GameObject>(); pool[mob.typeKey] = stack; }
        stack.Push(go);
    }

    /// <summary>PUT TO SLEEP, nobody being near (SeamlessWorld). Every creature still out is banked and owed back the
    /// moment the area wakes; every creature that died keeps the wait it already had, because those waits are moments on
    /// the world's clock (below) and nothing here touches them. So an elite killed an hour ago stays gone until its own
    /// time is up, however often the area sleeps and wakes. Then the area itself is switched off.</summary>
    public void Sleep()
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var go = i < live.Count ? live[i].go : null;
            if (go == null) { live.RemoveAt(i); continue; }
            var mob = go.GetComponent<PooledMob>();   // perf-ok: once per creature, only as its area falls asleep
            if (mob == null) { live.RemoveAt(i); continue; }
            Bank(mob);
            returnsOwed.Add(WorldClock.UtcNow);
        }
        live.Clear();
        gameObject.SetActive(false);
    }

    /// <summary>Is any creature of this area hunting somebody, or within <paramref name="reach"/> of a player in
    /// <paramref name="map"/>? While one is, the area must stay awake: a creature that gave up a chase stays where it
    /// stopped, so it can be far from the area's middle and still in front of somebody.</summary>
    public bool AnyCreatureEngaged(string map, float reach)
    {
        for (int i = 0; i < live.Count; i++)
        {
            var (go, ai) = live[i];
            if (go == null || !go.activeSelf) continue;
            if (ai != null && ai.Prey != null) return true;
            if (PlayerUnits.AnyoneWithin(go.transform.position, reach, map)) return true;
        }
        return false;
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

    IEnumerator RespawnAfterDelay(CreatureKind kind)
    {
        float delay = CreatureKinds.RespawnSeconds(kind, Random.value);
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
            Vector3 probe;
            if (shapeSquares > 0) probe = SpotInShape(c.y);
            else
            {
                Vector2 r = Random.insideUnitCircle * radius;
                probe = new Vector3(c.x + r.x, c.y + 30f, c.z + r.y);
            }
            if (groundMask != 0 &&
                Physics.Raycast(probe, Vector3.down, out var hit, shapeSquares > 0 ? ProbeDrop : 120f, groundMask, QueryTriggerInteraction.Ignore))
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

    // THE AREA'S OWN SHAPE (WorldSpawns.Area): runs of squares, in the map's own coordinates, and where the map's
    // middle stands in the world. Empty = the circle of `radius`.
    int[] shapeRuns;
    int shapeSquares;
    Vector3 mapOrigin;

    // A shaped area spans more height than a circle round one spot, so its probe starts higher and falls further.
    const float ProbeRise = 60f, ProbeDrop = 160f;

    /// <summary>Spawn on <paramref name="runs"/> (see <see cref="WorldSpawns.Area.runs"/>) instead of the circle;
    /// <paramref name="mapOriginInWorld"/> is where the map's own (0, 0, 0) stands. Set before the area starts.
    /// <paramref name="areaMates"/>: every spawner on the same area, this one included, whose creatures this one's
    /// spread away from (null = only its own).</summary>
    public void SetShape(int[] runs, Vector3 mapOriginInWorld, List<FieldMobSpawner> areaMates = null)
    {
        shapeRuns = runs; mapOrigin = mapOriginInWorld; shapeSquares = 0; mates = areaMates;
        for (int k = 2; k < runs.Length; k += 3) shapeSquares += runs[k];
    }

    List<FieldMobSpawner> mates;

    // SPREAD EVENLY, never in clumps: of SpreadTries random points of the shape, the one farthest from every creature
    // the area already has out. Random points alone leave crowds beside empty stretches on a big patch.
    const int SpreadTries = 8;

    Vector3 SpotInShape(float aroundY)
    {
        Vector3 best = RandomInShape(aroundY);
        float bestGap = NearestCreatureSqr(best);
        for (int t = 1; t < SpreadTries; t++)
        {
            Vector3 p = RandomInShape(aroundY);
            float gap = NearestCreatureSqr(p);
            if (gap > bestGap) { best = p; bestGap = gap; }
        }
        return best;
    }

    // Flat distance, squared, from p to the nearest creature this area has out (every spawner on it).
    float NearestCreatureSqr(Vector3 p)
    {
        float best = float.MaxValue;
        if (mates == null) return NearestIn(this, p, best);
        for (int m = 0; m < mates.Count; m++) if (mates[m] != null) best = NearestIn(mates[m], p, best);
        return best;
    }

    static float NearestIn(FieldMobSpawner s, Vector3 p, float best)
    {
        for (int i = 0; i < s.live.Count; i++)
        {
            var go = s.live[i].go;
            if (go == null) continue;
            Vector3 q = go.transform.position;
            float dx = q.x - p.x, dz = q.z - p.z, d = dx * dx + dz * dz;
            if (d < best) best = d;
        }
        return best;
    }

    // A point straight above a random square of the shape, every square as likely as any other.
    Vector3 RandomInShape(float aroundY)
    {
        int pick = Random.Range(0, shapeSquares);
        for (int k = 0; k + 2 < shapeRuns.Length; k += 3)
        {
            if (pick >= shapeRuns[k + 2]) { pick -= shapeRuns[k + 2]; continue; }
            float x = shapeRuns[k] + (pick + Random.value) * WorldSpawns.AreaSquare;
            float z = shapeRuns[k + 1] + Random.value * WorldSpawns.AreaSquare;
            return new Vector3(mapOrigin.x + x, aroundY + ProbeRise, mapOrigin.z + z);
        }
        return new Vector3(transform.position.x, aroundY + ProbeRise, transform.position.z);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
