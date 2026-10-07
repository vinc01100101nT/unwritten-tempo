using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// THE PICTURE OF EVERYTHING IN THE AIR, on a watching machine — the drawing half of <see cref="Flights"/>. Each flight
/// the server announces is ONE drawn object for its whole life: its first leg leaves the thrower's hand, every later
/// leg moves the same object on from wherever it is, and the flight home ends it in the hand.
///
/// <para><b>It decides nothing.</b> Every leg arrives with the exact moments it leaves and arrives on the shared
/// clock, and the picture is placed by that alone: how far along the leg it is, between where it started and where
/// its target is drawn now. So the blade you watch reaches its target at the instant the number appears, on every
/// machine, whatever the frame rate.</para>
///
/// <para><b>The hand is empty while a blade is out.</b> A thrown weapon hides the one in the thrower's hand
/// (<see cref="WeaponSocket.HideForFlight"/>) and gives it back however the flight ends — home, out of legs, its
/// object destroyed with the map — so a hand can never stay empty.</para>
/// </summary>
public static class FlightPictures
{
    static readonly Dictionary<ulong, FlightPicture> live = new Dictionary<ulong, FlightPicture>();

    static ulong KeyOf(uint thrower, ushort blade) => ((ulong)thrower << 16) | blade;

    /// <summary>Draw one leg of flight <paramref name="blade"/> of <paramref name="throwerNetId"/>.</summary>
    public static void Leg(uint throwerNetId, ushort blade, Transform thrower, Transform to, Vector3 toPoint,
                           double departs, double arrives, Flights.Look look, string effectId, bool home)
    {
        ulong key = KeyOf(throwerNetId, blade);
        if (!live.TryGetValue(key, out var picture) || picture == null)
        {
            // A flight's FIRST sight on this machine. Its flight home with nothing drawn yet (it came into view mid-chain)
            // has nothing to bring back.
            if (home) return;
            picture = FlightPicture.Rent();
            picture.Begin(key, thrower, look, effectId);
            live[key] = picture;
        }
        picture.SetLeg(to, toPoint, departs, arrives, home);
    }

    internal static void Forget(ulong key, FlightPicture picture)
    {
        if (live.TryGetValue(key, out var held) && held == picture) live.Remove(key);
    }
}

/// <summary>One drawn flight — see <see cref="FlightPictures"/>. Pooled: a blade, an arrow and a bounce are thrown many
/// times a second in a fight, so a spent one goes dormant and is used again.</summary>
public class FlightPicture : MonoBehaviour
{
    // How long a leg that has arrived waits for the next one before the flight is taken as over. Longer than any gap
    // the server leaves between an arrival and the next leg's message, short enough that a chain that ended without a
    // flight home does not leave a blade hanging in the air.
    const double GraceSeconds = 0.6;

    // How fast a flying weapon spins, in degrees a second. A drawing constant: the server knows nothing of it.
    const float SpinDegreesPerSecond = 1440f;

    static readonly Stack<FlightPicture> pool = new Stack<FlightPicture>(16);
    static readonly Dictionary<GameObject, Stack<GameObject>> modelPool = new Dictionary<GameObject, Stack<GameObject>>();

    ulong key;
    Flights.Look look;
    WeaponSocket socket;        // the thrower's hand, emptied while this flies; null when nothing was taken from it
    GameObject modelPrefab, model;
    LineRenderer line;
    Vfx.EffectHandle effect;

    Transform target;
    Vector3 start, fallback;
    double departs, arrives;
    bool home, live;

    internal static FlightPicture Rent()
    {
        while (pool.Count > 0)
        {
            var reused = pool.Pop();
            if (reused != null) return reused;
        }
        var go = new GameObject("FlightPicture") { hideFlags = HideFlags.DontSaveInEditor };
        var made = go.AddComponent<FlightPicture>();
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        // always-included, URP-friendly — the same one the aim reticle draws with
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.sortingOrder = DepthSortRuntime.FxSlash;
        lr.startColor = Arrow.Tint;
        lr.endColor = new Color(Arrow.Tint.r, Arrow.Tint.g, Arrow.Tint.b, 0f);
        lr.widthMultiplier = Arrow.Width;
        lr.enabled = false;
        made.line = lr;
        return made;
    }

    internal void Begin(ulong flightKey, Transform thrower, Flights.Look how, string effectId)
    {
        key = flightKey;
        look = how;
        socket = null;
        model = null; modelPrefab = null; effect = null;
        gameObject.SetActive(true);
        live = true;

        var hand = WeaponSocket.Of(thrower);
        Vector3 from = thrower != null ? UnitAnchor.Center(thrower) : Vector3.zero;

        if (look == Flights.Look.HeldWeapon && hand != null && hand.ShownModel != null)
        {
            modelPrefab = hand.ShownModel;
            model = RentModel(modelPrefab);
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.zero;
            // LAID FLAT and spun about the vertical, like a thrown blade — a weapon model stands along its own long
            // axis, and spinning it about that axis would barely show.
            model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // THE SIZE IT IS DRAWN AT IN THE HAND, copied exactly — never re-worked out from the prefab.
            var mounted = hand.Mounted;
            model.transform.localScale = mounted != null ? mounted.lossyScale : Vector3.one;
            if (mounted != null) from = mounted.position;
            socket = hand;
            socket.HideForFlight();
        }
        else if (look == Flights.Look.Effect && !string.IsNullOrEmpty(effectId))
            effect = Vfx.Attach(effectId, transform, Vector3.zero, 1f);
        else
            look = Flights.Look.Streak;

        transform.position = from;
        transform.rotation = Quaternion.identity;
        start = from;
        line.enabled = false;
    }

    internal void SetLeg(Transform to, Vector3 toPoint, double leaves, double lands, bool goingHome)
    {
        start = transform.position;
        target = to;
        fallback = toPoint;
        departs = leaves;
        arrives = lands > leaves ? lands : leaves + 0.001;
        home = goingHome;
        ShowVisuals(true);
    }

    void Update()
    {
        if (!live) return;
        double now = NetworkTime.time;
        double t01 = (now - departs) / (arrives - departs);
        float t = (float)(t01 < 0 ? 0 : t01 > 1 ? 1 : t01);

        Vector3 end = target != null ? UnitAnchor.Center(target) : fallback;
        Vector3 pos = Vector3.Lerp(start, end, t);
        Vector3 travel = end - transform.position;
        transform.position = pos;

        if (model != null) transform.Rotate(0f, SpinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
        if (look == Flights.Look.Streak)
        {
            Vector3 dir = travel.sqrMagnitude > 1e-6f ? travel.normalized : transform.forward;
            line.SetPosition(0, pos);
            line.SetPosition(1, pos - dir * Arrow.Length);
        }

        if (t01 < 1) return;
        // ARRIVED. Home: the flight is over and the weapon is back in the hand. Anywhere else: the blow lands now, and
        // the flight either jumps on (a new leg arrives in a moment) or has ended — out of sight until one of the two.
        if (home || now > arrives + GraceSeconds) { Finish(); return; }
        ShowVisuals(false);
    }

    void ShowVisuals(bool on)
    {
        if (model != null) model.SetActive(on);
        if (line != null) line.enabled = on && look == Flights.Look.Streak;
    }

    void Finish()
    {
        if (!live) return;
        live = false;
        GiveBack();
        line.enabled = false;
        target = null;
        gameObject.SetActive(false);
        pool.Push(this);
    }

    // Hand the weapon back, return the model, stop the effect — whatever ended this flight.
    void GiveBack()
    {
        FlightPictures.Forget(key, this);
        if (socket != null) { socket.ShowAfterFlight(); socket = null; }
        if (model != null && modelPrefab != null)
        {
            model.SetActive(false);
            model.transform.SetParent(null, false);
            if (!modelPool.TryGetValue(modelPrefab, out var stack)) modelPool[modelPrefab] = stack = new Stack<GameObject>(4);
            stack.Push(model);
        }
        model = null; modelPrefab = null;
        if (effect != null) { effect.Stop(); effect = null; }
    }

    // Destroyed with the map it was drawn in: whatever it took from a hand is given back all the same.
    void OnDestroy()
    {
        if (!live) return;
        live = false;
        FlightPictures.Forget(key, this);
        if (socket != null) socket.ShowAfterFlight();
        socket = null;
    }

    static GameObject RentModel(GameObject prefab)
    {
        if (modelPool.TryGetValue(prefab, out var stack))
            while (stack.Count > 0)
            {
                var reused = stack.Pop();
                if (reused != null) { reused.SetActive(true); return reused; }
            }
        var made = Instantiate(prefab);
        made.hideFlags = HideFlags.DontSaveInEditor;
        made.name = prefab.name;
        // A flying copy is a picture: nothing on it may collide or think.
        foreach (var c in made.GetComponentsInChildren<Collider>(true)) c.enabled = false;   // perf-ok: once per model built
        return made;
    }
}
