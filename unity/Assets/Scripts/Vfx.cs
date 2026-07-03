using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one entry point for playing a particle effect from the <see cref="EffectLibrary"/> — the visual
/// sibling of <see cref="Sfx"/>. Gameplay code calls <see cref="PlayAt(string,Vector3)"/> (a one-shot burst
/// at a world point — hits, explosions, slashes, magic circles) or <see cref="Attach"/> (a looping effect
/// that tracks a unit — auras, shields) and gets back a handle it can <see cref="EffectHandle.Stop"/>.
///
/// PERFORMANCE: instances are POOLED per prefab (like <see cref="DamagePopup"/>), never
/// create-and-destroy — combat spawns hits/slashes constantly. After warm-up a steady fight allocates
/// nothing: a spawn pops a free instance (or instantiates only on a pool miss), plays it, and returns it to
/// the free-list when the particle system finishes. Component lookups are cached ONCE per instance, never
/// per spawn or per frame.
/// </summary>
public static class Vfx
{
    /// <summary>Per-spawn overrides for <see cref="Vfx.PlayAt(string,Vector3,VfxParams)"/>. Any field left at
    /// its zero-default is treated as "unset" and falls back to the prefab's authored value — so
    /// <c>new VfxParams { scale = 1.6f }</c> means 1.6× size, normal speed, no tint.</summary>
    public struct VfxParams
    {
        public float scale;   // ≤0 → authored scale (×1)
        public float speed;   // ≤0 → authored playback speed (×1)
        public Color tint;    // (0,0,0,0) → no tint; otherwise multiplies startColor
        public float tailTrim; // >0 → return this many seconds EARLY (cut trailing dead time)
        public float clipStart; // >0 → fast-forward this many seconds INTO the clip on spawn (skip the intro)

        public static VfxParams Scaled(float scale) => new VfxParams { scale = scale };
    }

    // One pooled instance's runtime state. Added via AddComponent (never wired in the editor), so it needs
    // no matching filename. Caches its particle systems once, then restarts + auto-returns on each reuse.
    public class EffectHandle : MonoBehaviour
    {
        ParticleSystem[] systems;
        AudioSource[] audioSources;
        float[] baseSpeed;                       // authored simulationSpeed per system — the reset baseline
        ParticleSystem.MinMaxGradient[] baseColor; // authored startColor per system — the tint baseline
        bool[] isRoot;                           // system has no ParticleSystem ancestor in this instance → drives children
        bool[] baseLoop;                         // authored loop flag per system — restored for attached auras, forced off for one-shots
        int poolKey;
        bool looping;          // any child system loops → won't auto-return; the caller must Stop() it
        float oneShotDuration; // longest system's (duration + max start-lifetime), for the auto-return timer
        float fadeTail;        // longest system's particle lifetime — the fade window after emission stops
        float returnAt;        // Time.time to auto-return at; 0 = never (attached loop, until Stop)
        bool stopping;         // in the fade-out tail after Stop() → next returnAt hits Return, not another Stop
        Transform follow;      // tracked target for Attach (auras/shields); null for a fixed-point one-shot
        Vector3 offset;

        const float MaxOneShot = 30f;   // safety cap so a mis-read duration can't leak an instance forever

        // Cache components + measure loop/duration + the authored speed/color baselines. Runs ONCE the first
        // time this instance is built (the arrays persist across reuses), so no GetComponentsInChildren on the
        // hot spawn path — and every Begin resets speed/colour FROM these baselines, so a crit's params never
        // leak into the next reuse of the same pooled instance.
        internal void Prepare(int key)
        {
            poolKey = key;
            systems = GetComponentsInChildren<ParticleSystem>(true);
            audioSources = GetComponentsInChildren<AudioSource>(true);
            baseSpeed = new float[systems.Length];
            baseColor = new ParticleSystem.MinMaxGradient[systems.Length];
            isRoot = new bool[systems.Length];
            baseLoop = new bool[systems.Length];
            looping = false;
            oneShotDuration = 0f;
            fadeTail = 0f;
            for (int i = 0; i < systems.Length; i++)
            {
                var m = systems[i].main;
                // Force Hierarchy scaling so a caller's scale multiplier on the ROOT flows down to EVERY child
                // system. Many vendor prefabs (Hovl fire) author child systems as Local, which ignores the parent
                // scale — so scale/crit/AoE sizing silently did nothing. Set once here on the pooled instance.
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                baseSpeed[i] = m.simulationSpeed;
                baseColor[i] = m.startColor;
                // A system is a root if no ancestor transform carries another of our systems — only roots get the
                // clip-start Simulate (with children), so children aren't fast-forwarded twice.
                bool root = true;
                for (Transform pt = systems[i].transform.parent; pt != null; pt = pt.parent)
                {
                    var ap = pt.GetComponent<ParticleSystem>();
                    if (ap != null && System.Array.IndexOf(systems, ap) >= 0) { root = false; break; }
                }
                isRoot[i] = root;
                baseLoop[i] = m.loop;
                if (m.loop) looping = true;
                float speed = Mathf.Max(0.01f, m.simulationSpeed);
                float life = m.duration + m.startLifetime.constantMax;
                oneShotDuration = Mathf.Max(oneShotDuration, life / speed);
                fadeTail = Mathf.Max(fadeTail, m.startLifetime.constantMax / speed);
            }
            oneShotDuration = Mathf.Min(oneShotDuration + 0.15f, MaxOneShot);
            fadeTail = Mathf.Min(fadeTail + 0.15f, MaxOneShot);
        }

        // (Re)activate at a world pose with a caller scale/speed/tint. followTarget != null → track it each
        // frame. speedMult scales playback (and the auto-return timer); tint multiplies startColor (white = no-op).
        // oneShot = fire-and-forget (PlayAt): ALWAYS time-bounded, even if a sub-emitter loops — the loop is just
        // texture, not a request to live forever. Only an ATTACHED loop (Attach, oneShot=false) waits for Stop().
        internal void Begin(Vector3 pos, Quaternion rot, float authoredScale, float callerScale, float speedMult,
                            Color tint, float lifetimeOverride, bool oneShot, Transform followTarget, Vector3 followOffset,
                            float tailTrim = 0f, float clipStart = 0f)
        {
            follow = followTarget;
            offset = followOffset;
            stopping = false;
            transform.SetPositionAndRotation(pos, rot);
            transform.localScale = Vector3.one * Mathf.Max(0.001f, authoredScale * callerScale);
            gameObject.SetActive(true);

            bool tinted = tint != Color.white;
            float speed = Mathf.Max(0.01f, speedMult);
            for (int i = 0; i < systems.Length; i++)
            {
                var m = systems[i].main;
                m.simulationSpeed = baseSpeed[i] * speed;             // reset from baseline, then apply
                // A one-shot plays exactly ONE clip — no restart at the loop boundary (mirrors the Vfx Composer
                // preview, which forces loop off for PlayAt). Attached auras keep their authored looping.
                m.loop = oneShot ? false : baseLoop[i];
                m.startColor = tinted ? Multiply(baseColor[i], tint) : baseColor[i];
                systems[i].Clear(true);
                systems[i].Play(true);
            }
            // Clip-start: fast-forward each root (with its children) into the clip, then Play() to resume live from
            // that state — so the effect appears already mid-animation instead of from frame 0. Runs after the whole
            // Clear/Play loop so a child isn't wiped by a later root's Clear(true).
            if (clipStart > 0f)
                for (int i = 0; i < systems.Length; i++)
                    if (isRoot[i]) { systems[i].Simulate(clipStart, true, false); systems[i].Play(); }

            if (audioSources != null)
                for (int i = 0; i < audioSources.Length; i++)
                    if (audioSources[i].playOnAwake && audioSources[i].clip != null) audioSources[i].Play();

            if (lifetimeOverride > 0f) returnAt = Time.time + Mathf.Max(0.05f, lifetimeOverride - tailTrim - clipStart);
            else if (looping && !oneShot) returnAt = 0f;              // attached loop: lives until Stop()
            // One-shot window = the Composer's WYSIWYG length (naturalLife/speed − trimEnd). A looping sub-emitter
            // takes the SAME window (matches the preview), then Stop() fades the tail for one particle lifetime.
            else returnAt = Time.time + Mathf.Max(0.05f, oneShotDuration / speed - tailTrim - clipStart);
        }

        // Tint an authored startColor by multiplying. Handles the common solid / two-colour modes; an authored
        // gradient is left as-is (multiplying a gradient in-place isn't worth the per-spawn cost here).
        static ParticleSystem.MinMaxGradient Multiply(ParticleSystem.MinMaxGradient g, Color tint)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(g.color * tint);
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(g.colorMin * tint, g.colorMax * tint);
                default:
                    return g;
            }
        }

        /// <summary>True while this instance is playing (rented from the pool). Dev tuner uses it to know whether
        /// the handle it's holding is still alive or has already auto-returned.</summary>
        public bool IsActive => gameObject.activeSelf;

        /// <summary>Absolute <see cref="Time.time"/> this one-shot will auto-return at (i.e. when its visual is
        /// done). 0 = never (an attached loop that lives until <see cref="Stop"/>). Lets a sequence schedule an
        /// "on end" event at the real finish time instead of guessing.</summary>
        public float ReturnAt => returnAt;

        /// <summary>Stop a looping/attached effect: stop emitting, let live particles fade, then return to the
        /// pool. Safe to call more than once; a no-op once already returned.</summary>
        public void Stop()
        {
            if (!gameObject.activeSelf || stopping) return;
            for (int i = 0; i < systems.Length; i++)
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            // Keep tracking `follow` through the fade-out tail so the ease-out stays glued to a MOVING caster
            // (cleared on Return once the tail is done). Nulling it here left the tail behind at the Stop position.
            stopping = true;
            returnAt = Time.time + fadeTail;   // let live particles fade for one particle lifetime, then Return picks it up
        }

        void LateUpdate()
        {
            if (follow != null)
            {
                if (follow.gameObject == null) { Stop(); return; }   // target destroyed → tear down
                transform.position = follow.position + offset;
            }
            if (returnAt <= 0f || Time.time < returnAt) return;
            // A one-shot with a looping sub-emitter: stop emitting + fade the tail, THEN return (avoids a hard
            // cut). Everything else returns immediately.
            if (looping && !stopping) Stop();
            else Return();
        }

        void Return()
        {
            gameObject.SetActive(false);
            follow = null;
            GetPool(poolKey).Push(this);
        }
    }

    // ---- pool ---------------------------------------------------------------
    static Transform host;                                                   // parent for all pooled instances
    static readonly Dictionary<int, Stack<EffectHandle>> pools = new Dictionary<int, Stack<EffectHandle>>();

    // The shared parent. Recreated lazily if a scene unload destroyed it (pooled children die with it, so the
    // pools are cleared then to drop the stale refs). Mirrors DamagePopup.EnsureHost.
    static void EnsureHost()
    {
        if (host != null) return;
        pools.Clear();
        host = new GameObject("VfxSystem").transform;
        host.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    static Stack<EffectHandle> GetPool(int key)
    {
        if (!pools.TryGetValue(key, out var s)) { s = new Stack<EffectHandle>(); pools[key] = s; }
        return s;
    }

    // Pop a free instance for this def, or instantiate one (pool miss only).
    static EffectHandle Rent(EffectDef def)
    {
        EnsureHost();
        int key = def.prefab.GetInstanceID();
        var pool = GetPool(key);
        if (pool.Count > 0) return pool.Pop();

        var go = Object.Instantiate(def.prefab, host);
        var h = go.GetComponent<EffectHandle>();
        if (h == null) h = go.AddComponent<EffectHandle>();
        h.Prepare(key);
        return h;
    }

    /// <summary>Play a one-shot effect at a world position (prefab's own orientation). Returns null if the id
    /// is unknown or the library is missing.</summary>
    public static EffectHandle PlayAt(string id, Vector3 pos) => PlayAt(id, pos, Quaternion.identity, 1f);

    /// <summary>Play a one-shot effect at a world position + rotation, scaled by <paramref name="scaleMult"/>×
    /// the effect's authored scale.</summary>
    public static EffectHandle PlayAt(string id, Vector3 pos, Quaternion rot, float scaleMult = 1f)
    {
        var def = EffectCatalog.Get(id);
        if (def == null) return null;
        var h = Rent(def);
        h.Begin(pos + def.offset, rot, def.scale, scaleMult, 1f, Color.white, def.lifetime, true, null, Vector3.zero);
        return h;
    }

    /// <summary>Play a one-shot effect with per-spawn overrides — scale (crit-punch, AoE sizing), playback
    /// speed (slow-mo / snappy), and colour tint (element recolour). Reuse-safe: each spawn resets these from
    /// the prefab's authored values, so one call's params never bleed into the next. Unset fields fall back to
    /// the authored defaults (see <see cref="VfxParams"/>).</summary>
    public static EffectHandle PlayAt(string id, Vector3 pos, VfxParams p) => PlayAt(id, pos, Quaternion.identity, p);

    /// <summary>As <see cref="PlayAt(string,Vector3,VfxParams)"/> but with an explicit rotation.</summary>
    public static EffectHandle PlayAt(string id, Vector3 pos, Quaternion rot, VfxParams p)
    {
        var def = EffectCatalog.Get(id);
        if (def == null) return null;
        var h = Rent(def);
        float scale = p.scale <= 0f ? 1f : p.scale;
        float speed = p.speed <= 0f ? 1f : p.speed;
        Color tint = p.tint == default ? Color.white : p.tint;   // unset (0,0,0,0) → no tint
        h.Begin(pos + def.offset, rot, def.scale, scale, speed, tint, def.lifetime, true, null, Vector3.zero, p.tailTrim, p.clipStart);
        return h;
    }

    /// <summary>Attach an effect that TRACKS a unit (auras, shields, channels). Follows <paramref name="target"/>
    /// each frame without parenting (so it never inherits a billboard's breathe/scale). Looping effects live
    /// until you call <see cref="EffectHandle.Stop"/> on the returned handle; a fixed-lifetime effect returns
    /// itself. Null if the id is unknown.</summary>
    public static EffectHandle Attach(string id, Transform target, Vector3 offset = default, float scaleMult = 1f,
                                      Color tint = default, bool oneShot = false, float clipStart = 0f)
    {
        var def = EffectCatalog.Get(id);
        if (def == null || target == null) return null;
        var h = Rent(def);
        Vector3 total = offset + def.offset;
        Color t = tint == default ? Color.white : tint;   // unset (0,0,0,0) → no tint
        h.Begin(target.position + total, Quaternion.identity, def.scale, scaleMult, 1f, t, def.lifetime, oneShot, target, total, 0f, clipStart);
        return h;
    }

    /// <summary>Pre-instantiate <paramref name="count"/> instances of an effect into the pool so the first
    /// combat use doesn't hitch. Call from a load/scene-setup step, never per frame.</summary>
    public static void Prewarm(string id, int count)
    {
        var def = EffectCatalog.Get(id);
        if (def == null) return;
        EnsureHost();
        int key = def.prefab.GetInstanceID();
        var pool = GetPool(key);
        for (int i = 0; i < count; i++)
        {
            var go = Object.Instantiate(def.prefab, host);
            var h = go.GetComponent<EffectHandle>();
            if (h == null) h = go.AddComponent<EffectHandle>();
            h.Prepare(key);
            go.SetActive(false);
            pool.Push(h);
        }
    }
}
