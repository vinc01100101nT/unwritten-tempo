using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// THE ARROWS ON THE GROUND that lead to the followed quest's step — a line of them from the player along a walkable
/// route to the townsperson, or, when the step is on another map, to the door the server says to take next. A spot the
/// player picked on the map window (<see cref="MapGuide"/>) is led to the same way, ahead of the quest, until reached.
///
/// <para><b>What the server decides and what this machine does.</b> WHERE the step happens, and which door leads
/// there, are the server's answers (<see cref="QuestStore.Target"/>). The ROUTE across the ground is worked out here,
/// with the same route-finder this machine already walks by (<see cref="Pathfinder3D"/>) — it decides nothing and
/// changes no number, and a server routing for every player on every re-route would not hold at full player counts
/// (decided by the user 2026-10-04, as part of movement's exception).</para>
///
/// <para><b>Cheap by design.</b> A fixed pool of flat arrows, made once; a new route at most twice a second and only
/// after the player has moved, or at once when the place it leads to changed; ground heights read only when a route
/// is made, never per frame. Gone while talking, near the target, and with nothing followed.</para>
///
/// <para><b>One route, three drawings.</b> The ground shows the first stretch of it; the minimap and the Map window's
/// zone view draw the whole of it from <see cref="Route"/>, which is empty whenever the ground shows nothing.</para>
/// </summary>
public class QuestArrows : MonoBehaviour
{
    const int Pool = 24;
    const float Spacing = 1.6f, FirstAhead = 1.4f, ArrowSize = 0.6f, LiftAboveGround = 0.06f;
    const float CloseEnough = 3.2f, RerouteEvery = 0.5f, RerouteAfterMoving = 1.2f, RouteRadius = 0.35f;

    readonly SpriteRenderer[] arrows = new SpriteRenderer[Pool];
    static readonly List<Vector2> route = new List<Vector2>(64);

    /// <summary>The whole route the arrows follow, on the ground of the map the player stands in (X and Z), starting
    /// where the player stood when it was made and ending at the goal. Empty when nothing is shown.</summary>
    public static IReadOnlyList<Vector2> Route => route;

    /// <summary>Goes up every time <see cref="Route"/> changes, emptied included — a drawing redraws when it moved.</summary>
    public static int RouteVersion { get; private set; }

    /// <summary>Where the route starts — the player's spot when it was made.</summary>
    public static Vector2 RouteStart { get; private set; }

    /// <summary>How long the route is on this map, in metres along it — to the place itself, or to the door on the way
    /// there (<see cref="ThroughADoor"/>). Measured once per route, never per frame.</summary>
    public static float RouteMetres { get; private set; }

    /// <summary>The map the place being led to is on, by scene name. Null when nothing is shown.</summary>
    public static string DestinationMap { get; private set; }

    /// <summary>True when the place is on another map, so the route here ends at the door on the way.</summary>
    public static bool ThroughADoor { get; private set; }

    /// <summary>THE ROUTE AS MAP ARROWS — one every <see cref="MapMarkSpacing"/> metres along the whole of it, for a
    /// map seen from straight above with north up: x and y are the ground's X and Z, z is the turn (degrees) that
    /// points an upward arrow along the route. Worked out once per route, never per frame. Empty when nothing is shown.</summary>
    public static IReadOnlyList<Vector3> Marks
    {
        get
        {
            if (marksVersion != RouteVersion) LayMarks();
            return marks;
        }
    }

    /// <summary>The gap between two arrows on a map, in metres on the ground.</summary>
    public const float MapMarkSpacing = 2.5f;

    static readonly List<Vector3> marks = new List<Vector3>(128);
    static int marksVersion = -1;

    static void LayMarks()
    {
        marksVersion = RouteVersion;
        marks.Clear();
        if (route.Count == 0) return;
        Vector2 prev = RouteStart;
        float offset = MapMarkSpacing * 0.5f;
        for (int r = 0; r < route.Count; r++)
        {
            Vector2 next = route[r], seg = next - prev;
            float len = seg.magnitude;
            if (len < 0.001f) { prev = next; continue; }
            Vector2 dir = seg / len;
            float turn = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            float along = offset;
            for (; along <= len; along += MapMarkSpacing)
            {
                Vector2 at = prev + dir * along;
                marks.Add(new Vector3(at.x, at.y, turn));
            }
            offset = along - len;
            prev = next;
        }
    }
    Transform host;
    // THE PIN standing on the ground where the route ends on this map, turned to the camera while it is up.
    SpriteRenderer pin;
    const float PinHeight = 1.2f, PinGroundSearch = 80f;
    int shownCount;
    float nextReroute;
    Vector3 routedFrom, routedGoal;
    int routedMapVersion = -1;
    bool routeShown;
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

        var pinGo = new GameObject("DestinationPin") { hideFlags = HideFlags.DontSaveInEditor };
        pinGo.transform.SetParent(host, false);
        pin = pinGo.AddComponent<SpriteRenderer>();
        pin.sprite = GuidePin.Sprite;
        pin.color = UiKit.Accent.Value;
        pinGo.transform.localScale = Vector3.one * PinHeight / pin.sprite.bounds.size.y;
        pinGo.SetActive(false);

        if (GetComponent<RouteDestinationHud>() == null) gameObject.AddComponent<RouteDestinationHud>();
    }

    void OnDestroy()
    {
        if (host != null) Destroy(host.gameObject);
        route.Clear();
        RouteVersion++;
    }

    void LateUpdate()
    {
        if (host == null) return;
        if (!Goal(out var goal)) { Hide(); return; }
        var me = player.position;
        var flat = goal - me; flat.y = 0f;
        if (flat.sqrMagnitude <= CloseEnough * CloseEnough) { Hide(); return; }
        if (pin.gameObject.activeSelf) pin.transform.rotation = Quaternion.LookRotation(CameraRig.ForwardXZ);

        // A NEW PLACE TO LEAD TO is routed at once; a new quest list that leads to the same place is not, so a burst of
        // kills — one list each — never runs a route search each.
        bool targetMoved = !routeShown || routedMapVersion != MapManager.MapVersion
                           || (goal - routedGoal).sqrMagnitude > 0.01f;
        bool walked = (me - routedFrom).sqrMagnitude > RerouteAfterMoving * RerouteAfterMoving;
        if (!targetMoved && (!walked || Time.unscaledTime < nextReroute)) return;
        routedGoal = goal;
        routedMapVersion = MapManager.MapVersion;
        routedFrom = me;
        nextReroute = Time.unscaledTime + RerouteEvery;
        Lay(me, goal);
    }

    // Nothing to show: the arrows go, and so does the route the maps draw.
    void Hide()
    {
        Show(0);
        if (pin != null && pin.gameObject.activeSelf) pin.gameObject.SetActive(false);
        if (!routeShown) return;
        routeShown = false;
        DestinationMap = null;
        route.Clear();
        RouteVersion++;
    }

    // Where the arrows lead right now, in this machine's world — or false for nothing to show. A spot picked on the map
    // window comes first; the followed quest's step otherwise.
    bool Goal(out Vector3 goal)
    {
        goal = default;
        if (PlayerInteractor.Talking) return false;
        if (player == null) { var sc = StatusController.Player; player = sc != null ? sc.transform : null; }
        if (player == null) return false;
        string here = MapManager.CurrentMap;

        if (MapGuide.Active)
        {
            DestinationMap = MapGuide.Map;
            ThroughADoor = MapGuide.Map != here;
            if (MapGuide.Map == here)
            {
                // Held at one height, so walking up a slope is never read as the goal moving (which re-routes at once).
                var spot = MapGuide.Spot;
                goal = MapManager.InWorld(new Vector3(spot.x, 0f, spot.y), here);
                var flat = goal - player.position; flat.y = 0f;
                if (flat.sqrMagnitude > CloseEnough * CloseEnough) return true;
                // ARRIVED: the spot is done with, and the arrows go back to the quest from the next frame.
                MapGuide.Clear();
                return false;
            }
            string next = WorldRoutes.NextMap(here, MapGuide.Map, out _);
            return !string.IsNullOrEmpty(next) && DoorInto(next, out goal);
        }

        var t = QuestStore.Target;
        if (!t.any || string.IsNullOrEmpty(t.map)) return false;
        DestinationMap = t.map;
        ThroughADoor = t.map != here;
        if (t.map == here) { goal = MapManager.InWorld(t.at, here); return true; }
        if (string.IsNullOrEmpty(t.nextMap)) return false;
        return DoorInto(t.nextMap, out goal);
    }

    // ANOTHER MAP: the door into the next map on the way, chosen from the doors standing in this one — the nearest when
    // several lead there.
    bool DoorInto(string nextMap, out Vector3 goal)
    {
        goal = default;
        float best = float.MaxValue;
        bool any = false;
        foreach (var p in Portal3D.Active)
        {
            if (p == null || p.targetScene != nextMap) continue;
            float d = (p.transform.position - player.position).sqrMagnitude;
            if (d < best) { best = d; goal = p.transform.position; any = true; }
        }
        return any;
    }

    void Lay(Vector3 from, Vector3 goal)
    {
        // THE ROUTE: around walls and props. A search that runs out before the goal gives the way to the nearest point it
        // reached, and the next re-route, a few steps on, finds more of it; only when no step of a route is found at all
        // does it point straight at the goal, so the arrows still face the right way.
        route.Clear();
        if (!Pathfinder3D.FindPath(new Vector2(from.x, from.z), new Vector2(goal.x, goal.z), RouteRadius, route)
            || route.Count == 0)
            route.Add(new Vector2(goal.x, goal.z));
        RouteStart = new Vector2(from.x, from.z);
        routeShown = true;
        RouteVersion++;

        float metres = 0f;
        Vector2 walkedFrom = RouteStart;
        // A length is a sum of real distances, worked out once per route (at most twice a second), never per frame.
        for (int r = 0; r < route.Count; r++) { metres += (route[r] - walkedFrom).magnitude; walkedFrom = route[r]; }   // perf-ok: a length, not a comparison
        RouteMetres = metres;

        // The pin stands on the ground where the route ends. A spot picked on the map has no height of its own, so the
        // ground is looked for well above and below the player's.
        var pinAt = new Vector3(goal.x, from.y, goal.z);
        if (GroundSnap.TryGroundY(pinAt, GroundSnap.GroundMask, PinGroundSearch, 2f * PinGroundSearch, out float groundY)) pinAt.y = groundY;
        pin.transform.SetPositionAndRotation(pinAt, Quaternion.LookRotation(CameraRig.ForwardXZ));
        if (!pin.gameObject.activeSelf) pin.gameObject.SetActive(true);

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
