using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// THE ARROWS ON THE GROUND that lead to the followed quest's step — a line of them from the player along a walkable
/// route to the townsperson, or, when the step is on another map, to the door the server says to take next.
///
/// <para><b>What the server decides and what this machine does.</b> WHERE the step happens, and which door leads
/// there, are the server's answers (<see cref="QuestStore.Target"/>). The ROUTE across the ground is worked out here,
/// with the same route-finder this machine already walks by (<see cref="Pathfinder3D"/>) — it decides nothing and
/// changes no number, and a server routing for every player on every re-route would not hold at full player counts
/// (decided by the user 2026-10-04, as part of movement's exception).</para>
///
/// <para><b>Cheap by design.</b> A fixed pool of flat arrows, made once; a new route at most twice a second and only
/// after the player has moved or the target changed; ground heights read only when a route is made, never per frame.
/// Gone while talking, near the target, and with nothing followed.</para>
/// </summary>
public class QuestArrows : MonoBehaviour
{
    const int Pool = 24;
    const float Spacing = 1.6f, FirstAhead = 1.4f, ArrowSize = 0.9f, LiftAboveGround = 0.06f;
    const float CloseEnough = 3.2f, RerouteEvery = 0.5f, RerouteAfterMoving = 1.2f, RouteRadius = 0.35f;

    readonly SpriteRenderer[] arrows = new SpriteRenderer[Pool];
    readonly List<Vector2> route = new List<Vector2>(64);
    Transform host;
    int shownCount;
    float nextReroute;
    Vector3 routedFrom;
    int routedVersion = -1, routedMapVersion = -1;
    Transform player;

    void Start()
    {
        if (GameServer.IsServer && !GameNetwork.TheWorldIsElsewhere) { enabled = false; return; }
        var sprite = Resources.Load<Sprite>("Guide/GroundArrow");
        var go = new GameObject("QuestArrows") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        host = go.transform;
        for (int i = 0; i < Pool; i++)
        {
            var a = new GameObject("Arrow") { hideFlags = HideFlags.DontSaveInEditor };
            a.transform.SetParent(host, false);
            var sr = a.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            a.transform.localScale = Vector3.one * ArrowSize * (sprite != null ? sprite.pixelsPerUnit / sprite.rect.width : 1f);
            a.SetActive(false);
            arrows[i] = sr;
        }
    }

    void OnDestroy() { if (host != null) Destroy(host.gameObject); }

    void LateUpdate()
    {
        if (host == null) return;
        if (!Goal(out var goal)) { Show(0); return; }
        var me = player.position;
        var flat = goal - me; flat.y = 0f;
        if (flat.sqrMagnitude <= CloseEnough * CloseEnough) { Show(0); return; }

        bool targetMoved = routedVersion != QuestStore.Version || routedMapVersion != MapManager.MapVersion;
        bool walked = (me - routedFrom).sqrMagnitude > RerouteAfterMoving * RerouteAfterMoving;
        if (!targetMoved && (!walked || Time.unscaledTime < nextReroute)) return;
        routedVersion = QuestStore.Version;
        routedMapVersion = MapManager.MapVersion;
        routedFrom = me;
        nextReroute = Time.unscaledTime + RerouteEvery;
        Lay(me, goal);
    }

    // Where the arrows lead right now, in this machine's world — or false for nothing to show.
    bool Goal(out Vector3 goal)
    {
        goal = default;
        var t = QuestStore.Target;
        if (!t.any || string.IsNullOrEmpty(t.map) || PlayerInteractor.Talking) return false;
        if (player == null) { var sc = StatusController.Player; player = sc != null ? sc.transform : null; }
        if (player == null) return false;
        string here = MapManager.CurrentMap;
        if (t.map == here) { goal = MapManager.InWorld(t.at, here); return true; }
        if (string.IsNullOrEmpty(t.nextMap)) return false;
        // ANOTHER MAP: the door into the next map on the way, chosen from the doors standing in this one.
        float best = float.MaxValue;
        bool any = false;
        foreach (var p in Portal3D.Active)
        {
            if (p == null || p.targetScene != t.nextMap) continue;
            float d = (p.transform.position - player.position).sqrMagnitude;
            if (d < best) { best = d; goal = p.transform.position; any = true; }
        }
        return any;
    }

    void Lay(Vector3 from, Vector3 goal)
    {
        // THE ROUTE: around walls and props when one is found; straight at the goal when the search gives up (a far
        // goal runs out of search before it is reached) — the arrows still point the right way, and the next re-route,
        // a few steps on, finds more of the way.
        route.Clear();
        if (!Pathfinder3D.FindPath(new Vector2(from.x, from.z), new Vector2(goal.x, goal.z), RouteRadius, route)
            || route.Count == 0)
            route.Add(new Vector2(goal.x, goal.z));

        int placed = 0;
        Vector2 prev = new Vector2(from.x, from.z);
        float offset = FirstAhead;   // how far into the next stretch of the route the next arrow stands
        for (int r = 0; r < route.Count && placed < Pool; r++)
        {
            Vector2 next = route[r], seg = next - prev;
            float len = seg.magnitude;
            if (len < 0.001f) { prev = next; continue; }
            Vector2 dir = seg / len;
            float along = offset;
            while (along <= len && placed < Pool)
            {
                Vector2 at = prev + dir * along;
                var pos = new Vector3(at.x, from.y, at.y);
                if (GroundSnap.TryGroundY(pos, GroundSnap.GroundMask, out float y)) pos.y = y;
                pos.y += LiftAboveGround;
                var tr = arrows[placed].transform;
                tr.SetPositionAndRotation(pos, Quaternion.Euler(90f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 0f));
                placed++;
                along += Spacing;
            }
            offset = along - len;
            prev = next;
        }
        Show(placed);
    }

    void Show(int count)
    {
        if (count == shownCount) return;
        for (int i = 0; i < Pool; i++)
        {
            bool on = i < count;
            if (arrows[i] != null && arrows[i].gameObject.activeSelf != on) arrows[i].gameObject.SetActive(on);
        }
        shownCount = count;
    }
}
