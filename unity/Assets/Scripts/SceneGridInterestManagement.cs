using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// WHO IS TOLD ABOUT WHAT: everything within <see cref="visRange"/> of a person, in the same map, measured as a true
/// distance — and nothing else.
///
/// <para><b>Why not Mirror's own.</b> Mirror's scene-and-distance checker re-checks EVERY object in a map whenever any
/// one person in it moves, which is objects times people each second; harmless while a map holds a dozen people, and
/// the whole server once a seamless world puts everybody in one map. Mirror's grid checker avoids that but only
/// guarantees half its range (its squares are half the range wide and it looks one square out), so creatures would
/// pop in at the fog line. This one files people and objects in squares a full range wide, looks one square out — so
/// nobody within range can be missed — and then measures the real distance, so nobody past it is told.</para>
///
/// <para><b>What is re-checked.</b> An object that moved, and everything in the squares around a person who moved
/// (where they stand now and where they stood) — never a whole map. A change of map is noticed the frame it happens.
/// Everything else waits for the next pass, <see cref="rebuildInterval"/> seconds apart.</para>
///
/// <para><b>Cost.</b> One pass a frame over the spawned objects to notice map changes (a comparison each), and once a
/// pass: filing everyone and everything in squares (lists kept and cleared, nothing allocated once warm), then one
/// rebuild per object that moved or stands near somebody who moved, each looking at the people in nine squares.</para>
/// </summary>
public class SceneGridInterestManagement : InterestManagement
{
    [Tooltip("How far away, in metres, an object is still sent to a person.")]
    public int visRange = 60;
    [Tooltip("Seconds between passes that re-check what moved.")]
    public float rebuildInterval = 1f;
    [Tooltip("How far, in metres, something must have moved since the last pass to be re-checked.")]
    public float minMoveDistance = 1f;

    double lastRebuild;

    readonly Dictionary<NetworkIdentity, Vector3> lastPos = new Dictionary<NetworkIdentity, Vector3>();
    readonly Dictionary<NetworkIdentity, Scene> lastScene = new Dictionary<NetworkIdentity, Scene>();
    readonly HashSet<NetworkIdentity> dirty = new HashSet<NetworkIdentity>();
    readonly List<NetworkIdentity> dirtyScratch = new List<NetworkIdentity>();

    readonly Dictionary<(int scene, int x, int z), List<NetworkConnectionToClient>> people =
        new Dictionary<(int scene, int x, int z), List<NetworkConnectionToClient>>();
    readonly Dictionary<(int scene, int x, int z), List<NetworkIdentity>> things =
        new Dictionary<(int scene, int x, int z), List<NetworkIdentity>>();

    int Cell(float v) => Mathf.FloorToInt(v / Mathf.Max(1, visRange));

    [ServerCallback]
    public override void ResetState()
    {
        lastRebuild = 0d;
        lastPos.Clear();
        lastScene.Clear();
        dirty.Clear();
        people.Clear();
        things.Clear();
    }

    [ServerCallback]
    public override void OnSpawned(NetworkIdentity identity)
    {
        lastPos[identity] = identity.transform.position;
        lastScene[identity] = identity.gameObject.scene;
        dirty.Add(identity);
    }

    [ServerCallback]
    public override void OnDestroyed(NetworkIdentity identity)
    {
        lastPos.Remove(identity);
        lastScene.Remove(identity);
        dirty.Remove(identity);
    }

    public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient newObserver)
    {
        if (newObserver == null || newObserver.identity == null) return false;
        if (identity.gameObject.scene != newObserver.identity.gameObject.scene) return false;
        Vector3 d = identity.transform.position - newObserver.identity.transform.position;
        return d.sqrMagnitude < (long)visRange * visRange;
    }

    public override void OnRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnectionToClient> newObservers)
    {
        Vector3 at = identity.transform.position;
        int scene = identity.gameObject.scene.handle;
        long rangeSq = (long)visRange * visRange;
        int cx = Cell(at.x), cz = Cell(at.z);
        for (int x = cx - 1; x <= cx + 1; x++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                if (!people.TryGetValue((scene, x, z), out var here)) continue;
                for (int i = 0; i < here.Count; i++)
                {
                    var conn = here[i];
                    if (conn == null || conn.identity == null) continue;
                    if ((conn.identity.transform.position - at).sqrMagnitude < rangeSq) newObservers.Add(conn);
                }
            }
    }

    [ServerCallback]
    void LateUpdate()
    {
        // A CHANGE OF MAP IS ACTED ON AT ONCE: the people of the map left must stop seeing it, those of the new one must
        // start. A person changing map also has the things around where they stood marked, so those let them go.
        foreach (var identity in NetworkServer.spawned.Values)
        {
            if (identity == null || !lastScene.TryGetValue(identity, out var was)) continue;
            var now = identity.gameObject.scene;
            if (now == was) continue;
            lastScene[identity] = now;
            dirty.Add(identity);
            if (identity.connectionToClient != null)
            {
                if (lastPos.TryGetValue(identity, out var stood)) MarkAround(was.handle, stood);
                // And what stands around them in the map they have just entered, so they start seeing it now.
                MarkAround(now.handle, identity.transform.position);
            }
        }

        if (NetworkTime.localTime >= lastRebuild + rebuildInterval)
        {
            lastRebuild = NetworkTime.localTime;
            Refile();
        }

        if (dirty.Count == 0) return;
        dirtyScratch.Clear();
        dirtyScratch.AddRange(dirty);
        dirty.Clear();
        for (int i = 0; i < dirtyScratch.Count; i++)
            if (dirtyScratch[i] != null) NetworkServer.RebuildObservers(dirtyScratch[i], false);
    }

    // File everyone and everything in squares again, and mark what moved — and what stands near anybody who moved.
    void Refile()
    {
        foreach (var list in people.Values) list.Clear();
        foreach (var list in things.Values) list.Clear();

        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn == null || !conn.isAuthenticated || conn.identity == null) continue;
            Vector3 at = conn.identity.transform.position;
            var key = (conn.identity.gameObject.scene.handle, Cell(at.x), Cell(at.z));
            if (!people.TryGetValue(key, out var here)) people[key] = here = new List<NetworkConnectionToClient>();
            here.Add(conn);
        }

        float moveSq = minMoveDistance * minMoveDistance;
        foreach (var identity in NetworkServer.spawned.Values)
        {
            if (identity == null) continue;
            Vector3 at = identity.transform.position;
            int scene = identity.gameObject.scene.handle;
            var key = (scene, Cell(at.x), Cell(at.z));
            if (!things.TryGetValue(key, out var here)) things[key] = here = new List<NetworkIdentity>();
            here.Add(identity);

            if (lastPos.TryGetValue(identity, out var was) && (at - was).sqrMagnitude < moveSq) continue;
            dirty.Add(identity);
            // A person who moved changes what THEY see too: everything near where they stand now and where they stood.
            if (identity.connectionToClient != null)
            {
                MarkAroundAfterFiling(scene, at);
                if (lastPos.ContainsKey(identity)) MarkAroundAfterFiling(scene, was);
            }
            lastPos[identity] = at;
        }
        // Marks asked for while things were still being filed are settled now that every square is full.
        for (int i = 0; i < pendingMarks.Count; i++) MarkAround(pendingMarks[i].scene, pendingMarks[i].at);
        pendingMarks.Clear();
    }

    readonly List<(int scene, Vector3 at)> pendingMarks = new List<(int scene, Vector3 at)>();

    void MarkAroundAfterFiling(int scene, Vector3 at) => pendingMarks.Add((scene, at));

    void MarkAround(int scene, Vector3 at)
    {
        int cx = Cell(at.x), cz = Cell(at.z);
        for (int x = cx - 1; x <= cx + 1; x++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                if (!things.TryGetValue((scene, x, z), out var here)) continue;
                for (int i = 0; i < here.Count; i++) if (here[i] != null) dirty.Add(here[i]);
            }
    }
}
