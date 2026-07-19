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
    }

    /// <summary>As <see cref="Spawn"/> but zero-alloc per shot: the hit's state rides in a
    /// <see cref="HitPayload"/> handed back to <paramref name="onArrive"/> — pass a CACHED
    /// (static readonly) handler so nothing is allocated per launch.</summary>
    public static void Spawn(Texture2D body, int frameSize, int frameCount, float fps, float scale,
                             Vector3 from, Transform target, Vector3 fallbackPoint, float speed,
                             float arriveRadius, float maxLife, System.Action<HitPayload> onArrive,
                             Component source, Health hitTarget, int amount, Element element)
    {
        var payload = new HitPayload
        {
            source = source, target = hitTarget, homing = target,
            amount = amount, element = element, at = fallbackPoint
        };
        // no art → the blow lands instantly, same as the closure overload's null-body path
        if (body == null) { onArrive?.Invoke(payload); return; }

        var p = Create(body, frameSize, frameCount, fps, scale, from, target, fallbackPoint, speed, arriveRadius, maxLife);
        p.onArriveWith = onArrive;
        p.payload = payload;
    }

    static Projectile3D Create(Texture2D body, int frameSize, int frameCount, float fps, float scale,
                               Vector3 from, Transform target, Vector3 fallbackPoint, float speed,
                               float arriveRadius, float maxLife)
    {
        var go = new GameObject("Projectile3D") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.position = from;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = DepthSortRuntime.FxSlash;   // FX band: in front of entities
        go.AddComponent<Billboard>();

        var p = go.AddComponent<Projectile3D>();
        p.sr = sr;
        p.frames = SpriteSheetCache.Strip(body, frameSize, frameCount);   // shared/read-only
        p.fps = Mathf.Max(1f, fps);
        p.target = target;
        p.point = target != null ? UnitAnchor.Center(target) : fallbackPoint;   // home onto mid-body (feet-pivoted)
        p.speed = Mathf.Max(0.1f, speed);
        p.arriveRadius = Mathf.Max(0.05f, arriveRadius);
        p.die = Time.time + Mathf.Max(0.05f, maxLife);
        if (p.frames.Length > 0) sr.sprite = p.frames[0];
        return p;
    }

    /// <summary>As <see cref="Spawn"/> but the projectile IS a particle effect from the <see cref="EffectLibrary"/>
    /// instead of a billboard sprite: <paramref name="travelId"/> is attached to the moving object and tracks it,
    /// and on arrival <paramref name="impactId"/> (if any) plays at the hit point. Same homing/motion/onArrive.</summary>
    public static void SpawnVfx(string travelId, float scale,
                                Vector3 from, Transform target, Vector3 fallbackPoint, float speed,
                                float arriveRadius, float maxLife, System.Action onArrive, bool faceTravel = false,
                                float travelClipStart = 0f, Color travelTint = default)
    {
        var go = new GameObject("Projectile3D(Vfx)") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.position = from;

        var p = go.AddComponent<Projectile3D>();
        p.target = target;
        p.point = target != null ? UnitAnchor.Center(target) : fallbackPoint;   // home onto mid-body (feet-pivoted)
        p.speed = Mathf.Max(0.1f, speed);
        p.arriveRadius = Mathf.Max(0.05f, arriveRadius);
        p.die = Time.time + Mathf.Max(0.05f, maxLife);
        p.onArrive = onArrive;
        if (!string.IsNullOrEmpty(travelId)) p.travel = Vfx.Attach(travelId, go.transform, Vector3.zero, scale, tint: travelTint, clipStart: travelClipStart);
        // Aim the effect down its flight path. Map DOWN → travel dir so a pure vertical drop is unchanged (identity)
        // and only an angled launch tilts. Attach copies position each frame but not rotation, so setting it once on
        // the handle holds for the straight-line flight.
        if (faceTravel && p.travel != null)
        {
            Vector3 dir = p.point - from;
            if (dir.sqrMagnitude > 1e-6f)
                p.travel.transform.rotation = Quaternion.FromToRotation(Vector3.down, dir.normalized);
        }
    }

    SpriteRenderer sr; Sprite[] frames; float fps, t;
    Transform target; Vector3 point; float speed, arriveRadius, die;
    System.Action onArrive;
    // typed twin of onArrive: the payload rides here so callers can pass a cached handler (no closure)
    System.Action<HitPayload> onArriveWith;
    HitPayload payload;
    Texture2D impactSheet; int impactFrameSize, impactFrames; float impactFps, impactScale;
    Vfx.EffectHandle travel;   // Hovl travel mode (SpawnVfx)
    bool done;

    void Update()
    {
        if (done) return;

        if (frames != null && frames.Length > 0)
        {
            t += Time.deltaTime * fps;
            sr.sprite = frames[(int)t % frames.Length];
        }

        if (target != null) point = UnitAnchor.Center(target);   // home onto mid-body (feet-pivoted)
        Vector3 to = point - transform.position;
        float d = to.magnitude;
        float step = speed * Time.deltaTime;

        if (d <= arriveRadius || d <= step || Time.time >= die) { Arrive(); return; }
        transform.position += to / d * step;
    }

    void Arrive()
    {
        done = true;
        if (travel != null) travel.Stop();                         // stop + fade the attached travel effect
        if (impactSheet != null)
            SlashEffect.Spawn(impactSheet, impactFrameSize, impactFrames, impactFps, transform.position, impactScale);
        onArrive?.Invoke();
        if (onArriveWith != null)
        {
            payload.at = transform.position;
            onArriveWith(payload);
        }
        Destroy(gameObject);
    }
}
