using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>What kind of place a named area of a seamless world is. Decides the colour of its title.</summary>
public enum SeamlessAreaKind { Field, Town }

/// <summary>
/// ONE CONTINUOUS WORLD IN ONE SCENE, its ground and props kept in chunks that come and go around whoever is near.
///
/// <para><b>What a world scene holds.</b> This component, an arrival point and the pathfinder settings — nothing that
/// stands on the ground. Every square of ground with what grows on it is a prefab of its own (<see cref="chunks"/>),
/// fetched through <see cref="AddressablePrefabs"/> and built into this scene while somebody is close enough to need it.
/// Everything else in the game keeps treating the world as one map, because it is one scene: its creatures, its people,
/// the server's parking and the network's sight all work by scene exactly as they do for a zone map.</para>
///
/// <para><b>Who keeps what loaded.</b> A player's machine keeps the chunks around its own body, out past the fog: the
/// fog is fully thick at <see cref="FogOverlay.fullDistance"/>, so nothing further is ever seen, and the ring adds how
/// far the fastest body travels while one chunk loads, plus the farthest any prop reaches past its own chunk. A server
/// keeps the chunks around every player in the world and under every awake spawn area, because creatures walk on the
/// ground there whether or not anybody draws it.</para>
///
/// <para><b>Spawn areas sleep.</b> On a server, an area nobody is near is switched off with its creatures banked
/// (<see cref="FieldMobSpawner.Sleep"/>), and switched back on — keeping every respawn wait it was owed — once somebody
/// comes near and its ground is in. An area stays awake while any of its creatures is near a player or hunting one.</para>
///
/// <para><b>Named areas.</b> Each field, town or cave is a rectangle with a name. A player's machine works out from its
/// own position which one it stands in and raises <see cref="AreaVersion"/> on a change, which the area title reads.
/// That is display only: no game number comes from it.</para>
/// </summary>
[DisallowMultipleComponent]
public class SeamlessWorld : MonoBehaviour
{
    [System.Serializable]
    public class Chunk
    {
        public string address;
        public Vector2 min, max;
    }

    [System.Serializable]
    public class Area
    {
        public string name;
        public SeamlessAreaKind kind;
        public Vector2 min, max;
    }

    [Tooltip("The world's south-west corner, x and z, in this scene's own coordinates.")]
    public Vector2 worldMin;
    [Tooltip("The world's north-east corner, x and z, in this scene's own coordinates.")]
    public Vector2 worldMax;
    [Tooltip("Every square of ground: where it lies and the address its prefab loads from.")]
    public List<Chunk> chunks = new List<Chunk>();
    [Tooltip("Every named place, a rectangle each. The first one holding a point is where that point is.")]
    public List<Area> areas = new List<Area>();
    [Tooltip("The farthest any prop reaches past the chunk it stands in, in metres — written by the world builder from " +
             "the props it actually placed.")]
    public float propReach = 4f;
    [Tooltip("How long one chunk took to load when it was measured, in seconds. The ring grows by itself when this " +
             "machine sees a slower load than this.")]
    public float measuredLoadSeconds = 0.25f;

    /// <summary>Raised every time this machine's player crosses into a different named area of a seamless world.</summary>
    public static int AreaVersion { get; private set; }

    /// <summary>The named area this machine's player stands in, or null outside any (or outside a seamless world).</summary>
    public static Area CurrentArea { get; private set; }

    /// <summary>TRUE while this machine is holding its player back until the ground at their destination has loaded.</summary>
    public static bool PlacementPending { get; private set; }

    // How far past the load ring a chunk may drift before it is let go, so walking back and forth across one line does
    // not load and unload the same chunk over and over.
    const float UnloadMargin = 8f;
    // How often a player's machine and a server look at what they need loaded — a caretaker, not a hot path.
    const float ClientTickSeconds = 0.2f;
    const float ServerTickSeconds = 0.5f;
    // How far a spawn area's creatures can be found from its middle: its own radius, plus a wander and a chase step.
    const float CreatureReach = 12f;
    // An area wakes when somebody is within sight of it plus this much, so a creature is never seen appearing.
    const float WakeMargin = 10f;

    static readonly List<SeamlessWorld> worlds = new List<SeamlessWorld>();

    sealed class Loaded
    {
        public GameObject holder;
        public bool ready;
        public float askedAt;
    }

    readonly Dictionary<int, Loaded> loaded = new Dictionary<int, Loaded>();
    readonly List<int> toUnload = new List<int>();
    readonly List<Vector3> focusPoints = new List<Vector3>();
    readonly List<Rect> focusRects = new List<Rect>();
    readonly List<ICombatUnit> peopleScratch = new List<ICombatUnit>();
    object[] areaLabels;
    float observedSlowestLoad;
    float nextTick;
    bool labelsDirty;
    Vector3 pendingFocus;
    bool hasPendingFocus;

    void OnEnable()
    {
        if (!worlds.Contains(this)) worlds.Add(this);
        areaLabels = new object[areas.Count];
        BuildIndex();
    }

    // ---- which chunks lie near a point, without walking them all ------------------------------------------------------
    // A whole world holds thousands of chunks, and a server asks about every player twice a second: the chunks are filed
    // once, by IndexCell squares, so a question only looks at the chunks in the squares it touches.
    const float IndexCell = 32f;
    readonly Dictionary<(int, int), List<int>> index = new Dictionary<(int, int), List<int>>();
    readonly List<int> nearScratch = new List<int>();
    readonly List<int> candidates = new List<int>();
    readonly HashSet<int> candidateSet = new HashSet<int>();
    int[] seenStamp;
    int stamp;

    void BuildIndex()
    {
        index.Clear();
        seenStamp = new int[chunks.Count];
        for (int i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            for (int gy = Mathf.FloorToInt(c.min.y / IndexCell); gy <= Mathf.FloorToInt((c.max.y - 0.001f) / IndexCell); gy++)
                for (int gx = Mathf.FloorToInt(c.min.x / IndexCell); gx <= Mathf.FloorToInt((c.max.x - 0.001f) / IndexCell); gx++)
                {
                    if (!index.TryGetValue((gx, gy), out var list)) index[(gx, gy)] = list = new List<int>();
                    list.Add(i);
                }
        }
    }

    // The chunks that may touch the box (x0, z0)..(x1, z1), each once, into `into` (cleared first).
    void ChunksIn(float x0, float z0, float x1, float z1, List<int> into)
    {
        into.Clear();
        if (seenStamp == null || seenStamp.Length != chunks.Count) BuildIndex();
        if (++stamp == int.MaxValue) { stamp = 1; System.Array.Clear(seenStamp, 0, seenStamp.Length); }
        for (int gy = Mathf.FloorToInt(z0 / IndexCell); gy <= Mathf.FloorToInt(z1 / IndexCell); gy++)
            for (int gx = Mathf.FloorToInt(x0 / IndexCell); gx <= Mathf.FloorToInt(x1 / IndexCell); gx++)
            {
                if (!index.TryGetValue((gx, gy), out var list)) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    int i = list[k];
                    if (seenStamp[i] == stamp) continue;
                    seenStamp[i] = stamp; into.Add(i);
                }
            }
    }

    void OnDisable()
    {
        worlds.Remove(this);
        if (areaLabels != null)
            for (int i = 0; i < areaLabels.Length; i++) { Pathfinder3D.ForgetAreas(areaLabels[i]); areaLabels[i] = null; }
        foreach (var l in loaded.Values) if (l.holder != null) Destroy(l.holder);
        loaded.Clear();
        if (MapName == MapManager.CurrentMap) { CurrentArea = null; PlacementPending = false; }
    }

    string MapName => gameObject.scene.name;

    /// <summary>The seamless world that is the map <paramref name="map"/>, or null when that map is a zone map.</summary>
    public static SeamlessWorld ForMap(string map)
    {
        if (string.IsNullOrEmpty(map)) return null;
        for (int i = 0; i < worlds.Count; i++)
            if (worlds[i] != null && worlds[i].MapName == map) return worlds[i];
        return null;
    }

    /// <summary>The seamless world in <paramref name="scene"/>, or null when it is a zone map.</summary>
    public static SeamlessWorld InScene(Scene scene)
    {
        for (int i = 0; i < worlds.Count; i++)
            if (worlds[i] != null && worlds[i].gameObject.scene == scene) return worlds[i];
        return null;
    }

    /// <summary>The world's box, in the coordinates this machine has set the world down at.</summary>
    public Bounds WorldBounds(float lowY, float highY)
    {
        Vector3 a = MapManager.InWorld(new Vector3(worldMin.x, lowY, worldMin.y), MapName);
        Vector3 b = MapManager.InWorld(new Vector3(worldMax.x, highY, worldMax.y), MapName);
        var box = new Bounds();
        box.SetMinMax(a, b);
        return box;
    }

    /// <summary>The named area holding <paramref name="inMap"/> (this world's own coordinates), or -1.</summary>
    public int AreaIndexAt(Vector3 inMap)
    {
        for (int i = 0; i < areas.Count; i++)
        {
            var a = areas[i];
            if (inMap.x >= a.min.x && inMap.x <= a.max.x && inMap.z >= a.min.y && inMap.z <= a.max.y) return i;
        }
        return -1;
    }

    /// <summary>How far around a player this machine keeps the ground loaded: past the fog by the distance the fastest
    /// body covers while one chunk loads, plus the farthest a prop reaches. Every part is read from its owner.</summary>
    public float LoadRing
    {
        get
        {
            float fog = FogOverlay.Instance != null ? FogOverlay.Instance.fullDistance : 29f;
            float fastest = CharacterSpec.Active != null ? CharacterSpec.Active.FullSpeed * UnitStatMath.MaxMoveMult : 5f;
            return fog + fastest * Mathf.Max(measuredLoadSeconds, observedSlowestLoad) + propReach;
        }
    }

    /// <summary>TRUE when every chunk within <paramref name="radius"/> of <paramref name="inMap"/> is loaded and built.</summary>
    public bool ReadyAround(Vector3 inMap, float radius)
    {
        float r2 = radius * radius;
        ChunksIn(inMap.x - radius, inMap.z - radius, inMap.x + radius, inMap.z + radius, nearScratch);
        foreach (int i in nearScratch)
        {
            if (DistSqToChunk(i, inMap.x, inMap.z) > r2) continue;
            if (!loaded.TryGetValue(i, out var l) || !l.ready) return false;
        }
        return true;
    }

    /// <summary>TRUE when every chunk under the rectangle <paramref name="inMap"/> is loaded and built.</summary>
    public bool ReadyUnder(Rect inMap)
    {
        ChunksIn(inMap.xMin, inMap.yMin, inMap.xMax, inMap.yMax, nearScratch);
        foreach (int i in nearScratch)
        {
            if (!Overlaps(i, inMap)) continue;
            if (!loaded.TryGetValue(i, out var l) || !l.ready) return false;
        }
        return true;
    }

    float DistSqToChunk(int i, float x, float z)
    {
        var c = chunks[i];
        float dx = x < c.min.x ? c.min.x - x : x > c.max.x ? x - c.max.x : 0f;
        float dz = z < c.min.y ? c.min.y - z : z > c.max.y ? z - c.max.y : 0f;
        return dx * dx + dz * dz;
    }

    bool Overlaps(int i, Rect r)
    {
        var c = chunks[i];
        return c.max.x > r.xMin && c.min.x < r.xMax && c.max.y > r.yMin && c.min.y < r.yMax;
    }

    // ---- placing this machine's player on ground that has loaded ---------------------------------------------------

    /// <summary>Hold this machine's player back until the ground around <paramref name="inMap"/> has loaded, then call
    /// <paramref name="place"/>. The loading screen stands in front until then. Called by the one placement path every
    /// arrival goes through (<see cref="PersistentPlayer3D"/>), so a respawn, a wing, a Keeper's trip, a sign-in and a
    /// way out of a dungeon all wait the same way. Places at once when the ground is already there.</summary>
    public void PlaceWhenReady(Vector3 inMap, System.Action place)
    {
        if (ReadyAround(inMap, Mathf.Min(LoadRing, FogOverlay.Instance != null ? FogOverlay.Instance.fullDistance : 29f)))
        {
            place();
            return;
        }
        pendingFocus = inMap;
        hasPendingFocus = true;
        PlacementPending = true;
        LoadingScreen.Show();
        StopAllCoroutines();
        StartCoroutine(WaitThenPlace(inMap, place));
        nextTick = 0f;
    }

    System.Collections.IEnumerator WaitThenPlace(Vector3 inMap, System.Action place)
    {
        float since = Time.unscaledTime;
        float near = FogOverlay.Instance != null ? FogOverlay.Instance.fullDistance : 29f;
        while (!ReadyAround(inMap, Mathf.Min(LoadRing, near))) yield return null;
        hasPendingFocus = false;
        PlacementPending = false;
        place();
        LoadingScreen.Hide();
        GameLog.Info(LogCat.Map, "ground around the arrival in '" + MapName + "' loaded in "
                                 + Mathf.RoundToInt((Time.unscaledTime - since) * 1000f) + " ms");
    }

    // ---- the caretaker -------------------------------------------------------------------------------------------

    void Update()
    {
        if (Time.unscaledTime < nextTick) return;
        bool server = GameServer.IsServer;
        nextTick = Time.unscaledTime + (server ? ServerTickSeconds : ClientTickSeconds);

        focusPoints.Clear();
        focusRects.Clear();
        if (server) GatherServerFocus();
        else GatherClientFocus();

        Stream();
        if (labelsDirty) RefreshAreaLabels();
        if (server) RunWhatWaitedForGround();
        if (server) TendSpawnAreas();
        else TrackArea();
    }

    void GatherClientFocus()
    {
        if (MapName != MapManager.CurrentMap) return;
        if (hasPendingFocus) focusPoints.Add(pendingFocus);
        var me = PersistentPlayer3D.Instance;
        if (me != null && !PlacementPending) focusPoints.Add(MapManager.InMap(me.transform.position, MapName));
    }

    void GatherServerFocus()
    {
        peopleScratch.Clear();
        PlayerUnits.CollectIn(MapName, peopleScratch);
        for (int i = 0; i < peopleScratch.Count; i++)
        {
            var u = peopleScratch[i];
            if (PlayerUnits.Gone(u) || u.Body == null || u.Map != MapName) continue;
            focusPoints.Add(MapManager.InMap(u.Body.position, MapName));
        }
        var spawners = WorldSpawner.AreasIn(gameObject.scene);
        for (int i = 0; i < spawners.Count; i++)
        {
            var s = spawners[i];
            if (s == null || !(s.gameObject.activeSelf || WantsAwake(s))) continue;
            focusRects.Add(SpawnRect(s));
        }
        focusRects.AddRange(heldForGood);
    }

    // ---- ground a server keeps for good, and work that waits for it ---------------------------------------------------
    // A body standing on a square that is not loaded has nothing under it. Townsfolk are built when the world loads,
    // before any square is in, and they stand in one place for good — so the ground under them is held from the start,
    // and each is built once it is in (2026-10-27: Lastline's 16 townsfolk were built at load, the first square came in
    // nearly two minutes later, and no player ever saw one).
    readonly List<Rect> heldForGood = new List<Rect>();
    readonly List<(Rect where, System.Action then)> waitingForGround = new List<(Rect, System.Action)>();

    /// <summary>SERVER: keep the ground under <paramref name="inMap"/> (this world's own coordinates) loaded from now on,
    /// whoever is near, and call <paramref name="then"/> once it is in.</summary>
    public void HoldGround(Rect inMap, System.Action then)
    {
        heldForGood.Add(inMap);
        if (then != null) waitingForGround.Add((inMap, then));
        nextTick = 0f;
    }

    void RunWhatWaitedForGround()
    {
        for (int i = waitingForGround.Count - 1; i >= 0; i--)
        {
            if (!ReadyUnder(waitingForGround[i].where)) continue;
            var then = waitingForGround[i].then;
            waitingForGround.RemoveAt(i);
            // The physics engine learns where the new ground is before anything is put on it.
            Physics.SyncTransforms();
            then();
        }
    }

    Rect SpawnRect(FieldMobSpawner s)
    {
        Vector3 c = MapManager.InMap(s.transform.position, MapName);
        float r = s.radius + CreatureReach;
        return new Rect(c.x - r, c.z - r, r * 2f, r * 2f);
    }

    void Stream()
    {
        float ring = LoadRing, ring2 = ring * ring;
        float keep = ring + UnloadMargin, keep2 = keep * keep;

        // Only the chunks within the keep distance of somebody, or under a held rectangle, can be wanted or held; every
        // loaded chunk outside them is let go.
        candidates.Clear(); candidateSet.Clear();
        void Take(float x0, float z0, float x1, float z1)
        {
            ChunksIn(x0, z0, x1, z1, nearScratch);
            foreach (int i in nearScratch) if (candidateSet.Add(i)) candidates.Add(i);
        }
        for (int p = 0; p < focusPoints.Count; p++) Take(focusPoints[p].x - keep, focusPoints[p].z - keep, focusPoints[p].x + keep, focusPoints[p].z + keep);
        for (int r = 0; r < focusRects.Count; r++) Take(focusRects[r].xMin, focusRects[r].yMin, focusRects[r].xMax, focusRects[r].yMax);
        foreach (var held in loaded.Keys) if (!candidateSet.Contains(held)) toUnload.Add(held);

        foreach (int i in candidates)
        {
            bool want = false, hold = false;
            for (int p = 0; p < focusPoints.Count && !want; p++)
            {
                float d2 = DistSqToChunk(i, focusPoints[p].x, focusPoints[p].z);
                if (d2 <= ring2) want = true;
                else if (d2 <= keep2) hold = true;
            }
            for (int r = 0; r < focusRects.Count && !want; r++)
                if (Overlaps(i, focusRects[r])) want = true;

            bool have = loaded.ContainsKey(i);
            if (want && !have) Load(i);
            else if (!want && !hold && have) toUnload.Add(i);
        }
        for (int i = 0; i < toUnload.Count; i++) Unload(toUnload[i]);
        toUnload.Clear();
    }

    void Load(int index)
    {
        var entry = new Loaded { askedAt = Time.unscaledTime };
        loaded[index] = entry;
        string address = chunks[index].address;
        AddressablePrefabs.Load(address, prefab =>
        {
            // Let go while it was loading: nothing to build, and nothing uses the prefab.
            if (this == null || !loaded.TryGetValue(index, out var still) || still != entry) return null;
            if (prefab == null)
            {
                GameLog.Warn(LogCat.Map, "chunk '" + address + "' of '" + MapName + "' has no prefab behind it — that ground stays empty.");
                entry.ready = true;
                return null;
            }
            var holder = new GameObject("Chunk " + index)
            {
                // Made during play only, so an editor save can never bake it into the scene.
                hideFlags = HideFlags.DontSaveInEditor,
            };
            MapManager.PutInMap(holder, gameObject.scene);
            holder.transform.position = MapManager.InWorld(Vector3.zero, MapName);
            entry.holder = holder;
            // Built over the next frames rather than all in this one, so a chunk arriving never stalls the frame.
            // THE POSITION IS GIVEN, not left to the parent: a background build keeps the prefab's own world position
            // and ignores where its parent stands (MEASURED 2026-10-26 — under a parent at x 2000 it landed at 0), so on
            // a server, which sets this world 2,000 m from its other maps, every chunk stood at the origin and every
            // creature was put down over nothing.
            var op = InstantiateAsync(prefab, holder.transform, holder.transform.position, Quaternion.identity);
            op.completed += _ => OnBuilt(index, entry);
            return holder;
        });
    }

    void OnBuilt(int index, Loaded entry)
    {
        if (this == null || !loaded.TryGetValue(index, out var still) || still != entry || entry.holder == null) return;
        // The physics engine is told where the new ground is before anything asks it for the floor (this project keeps
        // the automatic sync off — see MapManager's parking for what happens otherwise).
        Physics.SyncTransforms();
        entry.ready = true;
        labelsDirty = true;
        float took = Time.unscaledTime - entry.askedAt;
        if (took > observedSlowestLoad) observedSlowestLoad = took;
        if (GameLog.On(LogCat.Map))
            GameLog.Info(LogCat.Map, "chunk " + index + " of '" + MapName + "' in after "
                                     + Mathf.RoundToInt(took * 1000f) + " ms; " + loaded.Count + " held");
    }

    void Unload(int index)
    {
        if (!loaded.TryGetValue(index, out var l)) return;
        loaded.Remove(index);
        labelsDirty = true;
        if (l.holder != null) MapManager.DeleteWhenNoMapIsMoving(l.holder);
        AddressablePrefabs.LetGoIfUnused(chunks[index].address);
    }

    // ---- route areas, one set per named area while its whole ground is in ------------------------------------------

    void RefreshAreaLabels()
    {
        labelsDirty = false;
        if (areaLabels == null || areaLabels.Length != areas.Count) areaLabels = new object[areas.Count];
        for (int i = 0; i < areas.Count; i++)
        {
            var a = areas[i];
            var rect = new Rect(a.min, a.max - a.min);
            bool whole = ReadyUnder(rect);
            if (whole && areaLabels[i] == null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var box = new Bounds();
                box.SetMinMax(MapManager.InWorld(new Vector3(a.min.x, -50f, a.min.y), MapName),
                              MapManager.InWorld(new Vector3(a.max.x, 50f, a.max.y), MapName));
                areaLabels[i] = Pathfinder3D.LabelAreas(box);
                GameLog.Info(LogCat.Map, "'" + a.name + "' route areas labelled in " + sw.ElapsedMilliseconds + " ms");
            }
            else if (!whole && areaLabels[i] != null)
            {
                Pathfinder3D.ForgetAreas(areaLabels[i]);
                areaLabels[i] = null;
            }
        }
    }

    // ---- spawn areas sleeping and waking (server) ------------------------------------------------------------------

    float WakeDistance(FieldMobSpawner s) => GameNetwork.VisibleRange + s.radius + WakeMargin;

    bool WantsAwake(FieldMobSpawner s)
    {
        if (PlayerUnits.AnyoneWithin(s.transform.position, WakeDistance(s), MapName)) return true;
        return s.gameObject.activeSelf && s.AnyCreatureEngaged(MapName, GameNetwork.VisibleRange + WakeMargin);
    }

    void TendSpawnAreas()
    {
        var spawners = WorldSpawner.AreasIn(gameObject.scene);
        for (int i = 0; i < spawners.Count; i++)
        {
            var s = spawners[i];
            if (s == null) continue;
            bool want = WantsAwake(s);
            if (want && !s.gameObject.activeSelf)
            {
                // Its ground first: a spawn area that wakes over ground not in yet drops every creature on one spot.
                if (ReadyUnder(SpawnRect(s))) s.gameObject.SetActive(true);
            }
            else if (!want && s.gameObject.activeSelf) s.Sleep();
        }
    }

    // ---- which named area this machine's player stands in (display only) ------------------------------------------

    void TrackArea()
    {
        if (MapName != MapManager.CurrentMap || PlacementPending) return;
        var me = PersistentPlayer3D.Instance;
        if (me == null) return;
        int at = AreaIndexAt(MapManager.InMap(me.transform.position, MapName));
        var area = at >= 0 ? areas[at] : null;
        if (area != CurrentArea)
        {
            CurrentArea = area;
            AreaVersion++;
        }
    }
}
