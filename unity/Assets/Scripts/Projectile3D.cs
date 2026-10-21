using UnityEngine;

/// <summary>
/// A reusable traveling billboard projectile for the 2.5D world. It flies from a spawn point toward a
/// target Transform (homing, so it tracks a moving unit) — or a fixed fallback point if there's no target
/// (or the target dies) — then fires an on-arrive callback (where the caller applies damage), optionally
/// spawns an impact FX, and removes itself. Frames are sliced through <see cref="SpriteSheetCache"/> and it
/// billboards like the other FX. Powers the Flam mob's fireball and the Mage's Fireball/Meteor.
///
/// Created per shot like <see cref="SlashEffect"/> (projectiles are short-lived and far less frequent than
/// per-hit slashes). The on-arrive callback should re-check validity (target may have died mid-flight).
/// </summary>
public class Projectile3D : MonoBehaviour
{
    /// <summary>Launch a projectile. <paramref name="target"/> (homing) wins; else it flies to
    /// <paramref name="fallbackPoint"/>. On arrival (within <paramref name="arriveRadius"/> or after
    /// <paramref name="maxLife"/>s) it fires <paramref name="onArrive"/> and, if given, an impact FX.</summary>
    public static void Spawn(Texture2D body, int frameSize, int frameCount, float fps, float scale,
                             Vector3 from, Transform target, Vector3 fallbackPoint, float speed,
                             float arriveRadius, float maxLife, System.Action onArrive,
                             Texture2D impactSheet = null, int impactFrameSize = 0, int impactFrames = 0,
                             float impactFps = 20f, float impactScale = 1f)
    {
        // no art → apply the effect immediately
        if (body == null) { onArrive?.Invoke(); return; }

        var p = Create(body, frameSize, frameCount, fps, scale, from, target, fallbackPoint, speed, arriveRadius, maxLife);
        p.onArrive = onArrive;
        p.impactSheet = impactSheet; p.impactFrameSize = impactFrameSize; p.impactFrames = impactFrames;
        p.impactFps = impactFps; p.impactScale = impactScale;
    }

    /// <summary>The hit state a zero-alloc on-arrive handler needs, carried BY VALUE on the projectile
    /// instead of captured in a per-shot closure (PERFORMANCE.md: no `new` per combat event).
    /// <c>at</c> is the arrival position (for popups/FX); <c>homing</c> is the tracked target transform.</summary>
    public struct HitPayload
    {
        public Component source;
        public Health target;
        public Transform homing;
        public int amount;
        public Element element;
        public Vector3 at;
        // The rest of a physical blow rolled at launch, for a handler that lands it on arrival.
        public bool isCrit, missed;
        public int attackerHit;
    }

    /// <summary>As <see cref="Spawn"/> but zero-alloc per shot: the hit's state rides in a
    /// <see cref="HitPayload"/> handed back to <paramref name="onArrive"/> — pass a CACHED
    /// (static readonly) handler so nothing is allocated per launch.</summary>
    public static void Spawn(Texture2D body, int frameSize, int frameCount, float fps, float scale,
                             Vector3 from, Transform target, Vector3 fallbackPoint, float speed,
                             float arriveRadius, float maxLife, System.Action<HitPayload> onArrive,
                             Component source, Health hitTarget, int amount, Element element,
                             bool isCrit = false, bool missed = false, int attackerHit = -1)
    {
        var payload = new HitPayload
        {
            source = source, target = hitTarget, homing = target,
            amount = amount, element = element, at = fallbackPoint,
            isCrit = isCrit, missed = missed, attackerHit = attackerHit
        };
        // no art → the blow lands instantly, same as the closure overload's null-body path
        if (body == null) { onArrive?.Invoke(payload); return; }

        var p = Create(body, frameSize, frameCount, fps, scale, from, target, fallbackPoint, speed, arriveRadius, maxLife);
        p.onArriveWith = onArrive;
        p.payload = payload;
    }

    // POOLED. Every ranged mob shot and every player arrow used to build a GameObject with three components and throw
    // it away on arrival; in a fight with archers or casters that is constant churn. Spent projectiles go dormant and
    // are reused (PERFORMANCE.md names projectiles explicitly in its "must be pooled" checklist).
    static readonly System.Collections.Generic.Stack<Projectile3D> pool =
        new System.Collections.Generic.Stack<Projectile3D>(16);

    // Take a dormant projectile, or build one. A scene unload destroys pooled objects while this static pool still
    // holds them, so skip any that came back dead.
    static Projectile3D Rent()
    {
        while (pool.Count > 0)
        {
            var reused = pool.Pop();
            if (reused != null) return reused;
        }
        var go = new GameObject("Projectile3D") { hideFlags = HideFlags.DontSaveInEditor };
        var sr = go.AddComponent<SpriteRenderer>();
        // FX band: in front of entities
        sr.sortingOrder = DepthSortRuntime.FxSlash;
        go.AddComponent<Billboard>();
        var made = go.AddComponent<Projectile3D>();
        made.sr = sr;
        return made;
    }

    static Projectile3D Create(Texture2D body, int frameSize, int frameCount, float fps, float scale,
                               Vector3 from, Transform target, Vector3 fallbackPoint, float speed,
                               float arriveRadius, float maxLife)
    {
        var p = Rent();
        p.transform.position = from;
        p.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        // Every field reset explicitly: a reused projectile must not inherit the previous shot's callback, payload,
        // impact FX or animation phase.
        p.frames = body != null ? SpriteSheetCache.Strip(body, frameSize, frameCount) : null;   // shared/read-only
        p.fps = Mathf.Max(1f, fps);
        p.t = 0f;
        p.target = target;
        p.point = target != null ? UnitAnchor.Center(target) : fallbackPoint;   // home onto mid-body (feet-pivoted)
        p.speed = Mathf.Max(0.1f, speed);
        p.arriveRadius = Mathf.Max(0.05f, arriveRadius);
        p.die = Time.time + Mathf.Max(0.05f, maxLife);
        p.onArrive = null;
        p.onArriveWith = null;
        p.payload = default;
        p.impactSheet = null; p.impactFrameSize = 0; p.impactFrames = 0; p.impactFps = 20f; p.impactScale = 1f;
        p.done = false;
        p.sr.enabled = true;
        p.sr.sprite = p.frames != null && p.frames.Length > 0 ? p.frames[0] : null;
        p.gameObject.SetActive(true);
        return p;
    }

    /// <summary>A bare moving point flying from <paramref name="from"/> to <paramref name="to"/>: on arrival (within
    /// <paramref name="arriveRadius"/>, or after <paramref name="maxLife"/> seconds) it fires <paramref name="onArrive"/>
    /// and removes itself. It draws nothing — a sequence's flight on the server, and the thing a watching screen hangs
    /// the flight's effect on. Same motion as every other projectile.</summary>
    public static Transform Fly(Vector3 from, Vector3 to, float speed, float arriveRadius, float maxLife, System.Action onArrive,
                                bool drawArrow = false)
    {
        // NOT pooled, deliberately: this hands its live transform back to the caller, which keeps reading the position
        // each frame — recycling it underneath that caller would silently re-aim someone else's effect. It also has no
        // SpriteRenderer, so it isn't a valid object for the sprite pool to hand out. It is destroyed on arrival, and
        // these are far rarer than per-shot sprite projectiles.
        var go = new GameObject("Projectile3D(Flight)") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.position = from;

        var p = go.AddComponent<Projectile3D>();
        p.pooled = false;
        p.target = null;
        p.point = to;
        p.speed = Mathf.Max(0.1f, speed);
        p.arriveRadius = Mathf.Max(0.05f, arriveRadius);
        p.die = Time.time + Mathf.Max(0.05f, maxLife);
        p.onArrive = onArrive;
        if (drawArrow) p.arrow = ArrowOn(go);
        return go.transform;   // the live mover — a Carrier with spread ticks reads its position each frame
    }

    /// <summary>A bare mover THROWN in an arc (<see cref="Ballistics"/>): launched at <paramref name="launchDegrees"/>,
    /// it falls onto <paramref name="to"/> under gravity, so the flight lasts as long as real throw physics says for that
    /// distance, and <paramref name="onArrive"/> runs on the frame it lands. <paramref name="drawArrow"/> draws the
    /// arrow streak along its flight. Same lifetime rules as <see cref="Fly"/>.</summary>
    public static Transform FlyArc(Vector3 from, Vector3 to, float launchDegrees, bool drawArrow, System.Action onArrive)
    {
        var go = new GameObject("Projectile3D(Arc)") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.position = from;
        var p = go.AddComponent<Projectile3D>();
        p.pooled = false;
        p.target = null;
        p.point = to;
        Vector3 flat = to - from; flat.y = 0f;
        float d = flat.magnitude;
        p.arc = true;
        p.arcFrom = from;
        p.arcSeconds = Ballistics.FlightSeconds(d, launchDegrees, Ballistics.Gravity);
        p.arcApex = Ballistics.ApexHeight(d, launchDegrees);
        p.arcElapsed = 0f;
        p.die = Time.time + p.arcSeconds + 1f;
        p.onArrive = onArrive;
        if (drawArrow) p.arrow = ArrowOn(go);
        return go.transform;
    }

    // The arrow a bow skill's shot draws — the same model every bow arrow has (Arrow.Model), riding the mover and
    // pointed along its flight each frame. Nothing is drawn if the model is missing.
    static Transform ArrowOn(GameObject go)
    {
        var prefab = Arrow.Model;
        if (prefab == null) return null;
        var a = Instantiate(prefab, go.transform, false);
        a.hideFlags = HideFlags.DontSaveInEditor;
        return a.transform;
    }

    bool arc; Vector3 arcFrom; float arcSeconds, arcApex, arcElapsed;
    Transform arrow;

    SpriteRenderer sr; Sprite[] frames; float fps, t;
    Transform target; Vector3 point; float speed, arriveRadius, die;
    System.Action onArrive;
    // typed twin of onArrive: the payload rides here so callers can pass a cached handler (no closure)
    System.Action<HitPayload> onArriveWith;
    HitPayload payload;
    Texture2D impactSheet; int impactFrameSize, impactFrames; float impactFps, impactScale;
    bool done;
    bool pooled = true;        // false only for Fly, which hands its transform to a live caller

    void Update()
    {
        if (done) return;

        if (frames != null && frames.Length > 0)
        {
            t += Time.deltaTime * fps;
            sr.sprite = frames[(int)t % frames.Length];
        }

        if (arc)
        {
            arcElapsed += Time.deltaTime;
            float s = arcSeconds > 0f ? arcElapsed / arcSeconds : 1f;
            Vector3 was = transform.position;
            Vector3 now = Ballistics.PointAt(arcFrom, point, arcApex, s);
            transform.position = now;
            if (arrow != null)
            {
                Vector3 dir = now - was;
                if (dir.sqrMagnitude > 1e-8f) transform.rotation = Quaternion.LookRotation(dir.normalized);
            }
            if (s >= 1f || Time.time >= die) Arrive();
            return;
        }

        // home onto mid-body (feet-pivoted)
        if (target != null) point = UnitAnchor.Center(target);
        Vector3 to = point - transform.position;
        float d = to.magnitude;
        float step = speed * Time.deltaTime;

        if (d <= arriveRadius || d <= step || Time.time >= die) { Arrive(); return; }
        transform.position += to / d * step;
        if (arrow != null) transform.rotation = Quaternion.LookRotation(to / d);
    }

    void Arrive()
    {
        done = true;
        if (impactSheet != null)
            SlashEffect.Spawn(impactSheet, impactFrameSize, impactFrames, impactFps, transform.position, impactScale);
        onArrive?.Invoke();
        if (onArriveWith != null)
        {
            payload.at = transform.position;
            onArriveWith(payload);
        }

        if (!pooled) { Destroy(gameObject); return; }

        // Go dormant and wait to be reused. Drop every reference the shot was holding FIRST — the arrival callbacks
        // above have already run, and a dormant projectile must not keep a target, a payload or a sprite sheet alive.
        target = null;
        onArrive = null;
        onArriveWith = null;
        payload = default;
        frames = null;
        impactSheet = null;
        if (sr != null) sr.sprite = null;
        gameObject.SetActive(false);
        pool.Push(this);
    }
}
