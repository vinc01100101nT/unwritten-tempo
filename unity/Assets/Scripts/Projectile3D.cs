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
        if (body == null) { onArrive?.Invoke(); return; }   // no art → apply the effect immediately

        var go = new GameObject("Projectile3D");
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
        p.point = target != null ? target.position + Vector3.up * 0.4f : fallbackPoint;
        p.speed = Mathf.Max(0.1f, speed);
        p.arriveRadius = Mathf.Max(0.05f, arriveRadius);
        p.die = Time.time + Mathf.Max(0.05f, maxLife);
        p.onArrive = onArrive;
        p.impactSheet = impactSheet; p.impactFrameSize = impactFrameSize; p.impactFrames = impactFrames;
        p.impactFps = impactFps; p.impactScale = impactScale;
        if (p.frames.Length > 0) sr.sprite = p.frames[0];
    }

    SpriteRenderer sr; Sprite[] frames; float fps, t;
    Transform target; Vector3 point; float speed, arriveRadius, die;
    System.Action onArrive;
    Texture2D impactSheet; int impactFrameSize, impactFrames; float impactFps, impactScale;
    bool done;

    void Update()
    {
        if (done) return;

        if (frames != null && frames.Length > 0)
        {
            t += Time.deltaTime * fps;
            sr.sprite = frames[(int)t % frames.Length];
        }

        if (target != null) point = target.position + Vector3.up * 0.4f;   // home onto mid-body
        Vector3 to = point - transform.position;
        float d = to.magnitude;
        float step = speed * Time.deltaTime;

        if (d <= arriveRadius || d <= step || Time.time >= die) { Arrive(); return; }
        transform.position += to / d * step;
    }

    void Arrive()
    {
        done = true;
        if (impactSheet != null)
            SlashEffect.Spawn(impactSheet, impactFrameSize, impactFrames, impactFps, transform.position, impactScale);
        onArrive?.Invoke();
        Destroy(gameObject);
    }
}
