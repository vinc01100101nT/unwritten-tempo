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
        public bool loop;      // true → keep the particle loop ON and live for `lifetime` (a looping PlayAt); false → one-shot
        public float lifetime; // >0 → force this exact on-screen lifetime (a looping PlayAt's Stay Duration); ≤0 → authored/natural

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
        EntityId poolKey;
        // What this effect's own particles say about its length — the same three numbers the effect table carries
        // for a machine that never builds one of these (see EffectTiming).
        EffectTiming.Measured measured;
        // Its renderers, held for hiding it while the person it rides is away on a teleport (TeleportLook.Covers).
        Renderer[] renderers;
        bool covered;
        float returnAt;        // Time.time to auto-return at; 0 = never (attached loop, until Stop)
        bool stopping;         // in the fade-out tail after Stop() → next returnAt hits Return, not another Stop
        Transform follow;      // tracked target for Attach (auras/shields); null for a fixed-point one-shot
        Vector3 offset;
        int generation;

        /// <summary>WHICH PLAY THIS INSTANCE IS ON. Instances are pooled, so a reference kept past its play can end
        /// up pointing at an instance now showing somebody else's effect. Whoever keeps one keeps this number beside
        /// it, and stops it only while the two still agree.</summary>
        internal int Generation => generation;


        // Cache components + measure loop/duration + the authored speed/color baselines. Runs ONCE the first
        // time this instance is built (the arrays persist across reuses), so no GetComponentsInChildren on the
        // hot spawn path — and every Begin resets speed/colour FROM these baselines, so a crit's params never
        // leak into the next reuse of the same pooled instance.
        internal void Prepare(EntityId key)
        {
            poolKey = key;
            systems = GetComponentsInChildren<ParticleSystem>(true);
            audioSources = GetComponentsInChildren<AudioSource>(true);
            // The x-ray silhouette bits are RESERVED for the player. Vendor VFX prefabs ship with
            // renderingLayerMask "Everything" (all bits set), which spuriously matches the silhouette
            // feature's filter and paints the effect flat blue when it passes behind scenery. Strip those
            // two bits here — the one place every pooled effect is born — so no combat VFX can be x-rayed.
            const uint reserved = OccludedSilhouetteTarget.Bit | OccludedSilhouetteTarget.CoverBit;
            renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].renderingLayerMask &= ~reserved;
            baseSpeed = new float[systems.Length];
            baseColor = new ParticleSystem.MinMaxGradient[systems.Length];
            isRoot = new bool[systems.Length];
            baseLoop = new bool[systems.Length];
            // HOW LONG THIS PLAYS is no longer worked out here — it is one formula, shared with the author-time
            // baker that writes the same numbers into the effect table for a machine that never builds an instance
            // (see EffectTiming). Measured from the instance we are holding, so a machine with the real thing still
            // reads the real thing; the bake exists for the one that hasn't.
            measured = EffectTiming.Of(systems);
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
            }
        }

        // (Re)activate at a world pose with a caller scale/speed/tint. followTarget != null → track it each
        // frame. speedMult scales playback (and the auto-return timer); tint multiplies startColor (white = no-op).
        // oneShot = fire-and-forget (PlayAt): ALWAYS time-bounded, even if a sub-emitter loops — the loop is just
        // texture, not a request to live forever. Only an ATTACHED loop (Attach, oneShot=false) waits for Stop().
        internal void Begin(Vector3 pos, Quaternion rot, float authoredScale, float callerScale, float speedMult,
                            Color tint, float lifetimeOverride, bool oneShot, Transform followTarget, Vector3 followOffset,
                            float tailTrim = 0f, float clipStart = 0f)
        {
            generation++;
            if (covered) Cover(false);
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

            // WHEN THIS PLAY ENDS is one arithmetic, shared with whatever else has to know the moment without
            // holding an instance (see EffectTiming.PlayEndsInSeconds). A forced window wins; otherwise a one-shot
            // runs the Composer's WYSIWYG length and an attached loop never ends until Stop().
            float window = EffectTiming.PlayEndsInSeconds(measured, 0f, lifetimeOverride, oneShot, speed, tailTrim, clipStart);
            returnAt = window > EffectTiming.NeverEnds ? Time.time + window : 0f;
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
        public void Stop() => Stop(false);

        /// <summary>Stop this effect. <paramref name="immediate"/> = clear live particles and return to the pool THIS
        /// frame (no fade tail) — for a targeting reticle that must vanish the instant aim ends, not linger. Default
        /// (false) = the normal soft stop: stop emitting, let particles fade, then return.</summary>
        public void Stop(bool immediate)
        {
            if (!gameObject.activeSelf) return;
            if (immediate)
            {
                for (int i = 0; i < systems.Length; i++)
                    systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Return();
                return;
            }
            if (stopping) return;
            for (int i = 0; i < systems.Length; i++)
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            // Keep tracking `follow` through the fade-out tail so the ease-out stays glued to a MOVING caster
            // (cleared on Return once the tail is done). Nulling it here left the tail behind at the Stop position.
            stopping = true;
            returnAt = Time.time + measured.fadeSeconds;
            // let live particles fade for one particle lifetime, then Return picks it up
        }

        void LateUpdate()
        {
            if (follow != null)
            {
                if (follow.gameObject == null) { Stop(); return; }   // target destroyed → tear down
                transform.position = follow.position + offset;
                // HIDDEN AND SILENT while the person it rides is away on a teleport — an aura, a regeneration glow,
                // anything held on them — and back the frame they appear.
                bool hide = TeleportLook.Covers(follow);
                if (hide != covered) Cover(hide);
            }
            if (returnAt <= 0f || Time.time < returnAt) return;
            // A one-shot with a looping sub-emitter: stop emitting + fade the tail, THEN return (avoids a hard
            // cut). Everything else returns immediately.
            if (measured.loops && !stopping) Stop();
            else Return();
        }

        // Switch this effect's drawing and its sound off or back on. Only when it changes, never per frame.
        void Cover(bool on)
        {
            covered = on;
            if (renderers != null)
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].forceRenderingOff = on;
            if (audioSources != null)
                for (int i = 0; i < audioSources.Length; i++)
                    if (audioSources[i] != null) audioSources[i].mute = on;
        }

        void Return()
        {
            if (covered) Cover(false);
            gameObject.SetActive(false);
            follow = null;
            GetPool(poolKey).Push(this);
        }
    }

    // ---- pool ---------------------------------------------------------------
    static Transform host;                                                   // parent for all pooled instances
    static readonly Dictionary<EntityId, Stack<EffectHandle>> pools = new Dictionary<EntityId, Stack<EffectHandle>>();

    // The shared parent. Recreated lazily if a scene unload destroyed it (pooled children die with it, so the
    // pools are cleared then to drop the stale refs). Mirrors DamagePopup.EnsureHost.
    static void EnsureHost()
    {
        if (host != null) return;
        pools.Clear();
        // DontSaveInEditor: this is a RUNTIME-ONLY pool host — it must NEVER be serialized into a scene.
        // Without this, instantiating the pool in edit mode (e.g. a Prewarm run via MCP while Systems.unity
        // is open) baked the host + all its clones into the scene, spawning phantom effects on every load.
        host = new GameObject("VfxSystem") { hideFlags = HideFlags.DontSaveInEditor }.transform;
        host.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    // The one place a pooled instance is born (Rent pool-miss + Prewarm). Tags it DontSaveInEditor so a pool
    // object can never be baked into a saved scene, then caches its components once via Prepare.
    static EffectHandle NewInstance(EffectDef def, EntityId key)
    {
        var go = Object.Instantiate(def.prefab, host);
        go.hideFlags = HideFlags.DontSaveInEditor;
        var h = go.GetComponent<EffectHandle>();
        if (h == null) h = go.AddComponent<EffectHandle>();
        h.Prepare(key);
        return h;
    }

    static Stack<EffectHandle> GetPool(EntityId key)
    {
        if (!pools.TryGetValue(key, out var s)) { s = new Stack<EffectHandle>(); pools[key] = s; }
        return s;
    }

    // Pop a free instance for this def, or instantiate one (pool miss only).
    static EffectHandle Rent(EffectDef def)
    {
        EnsureHost();
        EntityId key = def.prefab.GetEntityId();
        var pool = GetPool(key);
        if (pool.Count > 0) return pool.Pop();
        return NewInstance(def, key);
    }

    /// <summary>TRUE on the server, which draws nothing and is never meant to reach this door: every effect is drawn by
    /// the screens from the event the server announced, and reaching here writes an error to the server's log
    /// (<see cref="GameServer.CaughtDrawing"/>). The twin of <c>Sfx.Deaf</c>.
    ///
    /// <para>Nothing about TIMING hangs on this. A skill's beats come from the effect table's measured lengths
    /// (see <see cref="EffectTiming"/>), never from whether an instance was built — so a machine that draws
    /// nothing still runs every skill on the same moments as the machines watching it.</para></summary>
    static bool Blind => GameServer.CaughtDrawing("Vfx");

    /// <summary>Play a one-shot effect at a world position (prefab's own orientation). Returns null if the id
    /// is unknown or the library is missing.</summary>
    public static EffectHandle PlayAt(string id, Vector3 pos) => PlayAt(id, pos, Quaternion.identity, 1f);

    /// <summary>Play a one-shot effect at a world position + rotation, scaled by <paramref name="scaleMult"/>×
    /// the effect's authored scale.</summary>
    public static EffectHandle PlayAt(string id, Vector3 pos, Quaternion rot, float scaleMult = 1f)
    {
        if (Blind) return null;
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
        if (Blind) return null;
        var def = EffectCatalog.Get(id);
        if (def == null) return null;
        var h = Rent(def);
        float scale = p.scale <= 0f ? 1f : p.scale;
        float speed = p.speed <= 0f ? 1f : p.speed;
        Color tint = p.tint == default ? Color.white : p.tint;   // unset (0,0,0,0) → no tint
        // loop=true keeps the particle loop on and lives for `lifetime` (a looping PlayAt capped by the skill's Stay
        // Duration); the default (loop=false) is the classic one-shot that plays once for its authored length.
        float life = p.lifetime > 0f ? p.lifetime : def.lifetime;
        h.Begin(pos + def.offset, rot, def.scale, scale, speed, tint, life, !p.loop, null, Vector3.zero, p.tailTrim, p.clipStart);
        return h;
    }

    /// <summary>Attach an effect that TRACKS a unit (auras, shields, channels). Follows <paramref name="target"/>
    /// each frame without parenting (so it never inherits a billboard's breathe/scale). Looping effects live
    /// until you call <see cref="EffectHandle.Stop"/> on the returned handle; a fixed-lifetime effect returns
    /// itself. Null if the id is unknown.</summary>
    /// <remarks><paramref name="speed"/> scales the effect's PLAYBACK rate (same knob PlayAt exposes) — a slow
    /// smoulder or a frantic flicker. It never changes how LONG the aura lives: <paramref name="lifetime"/> is real
    /// seconds, applied separately.</remarks>
    public static EffectHandle Attach(string id, Transform target, Vector3 offset = default, float scaleMult = 1f,
                                      Color tint = default, bool oneShot = false, float clipStart = 0f, float lifetime = 0f,
                                      float speed = 1f)
    {
        if (Blind) return null;
        var def = EffectCatalog.Get(id);
        if (def == null || target == null) return null;
        var h = Rent(def);
        Vector3 total = offset + def.offset;
        Color t = tint == default ? Color.white : tint;   // unset (0,0,0,0) → no tint
        // lifetime > 0 → a looping aura capped at that many seconds (a skill's Stay Duration); 0 → the authored
        // lifetime (which for a looping effect means "until Stop()", the persistent-aura path).
        float life = lifetime > 0f ? lifetime : def.lifetime;
        h.Begin(target.position + total, Quaternion.identity, def.scale, scaleMult, speed, t, life, oneShot, target, total, 0f, clipStart);
        return h;
    }

    /// <summary>Attach a GROUND-CIRCLE telegraph sized so its visible ring lands exactly on <paramref name="worldRadius"/>
    /// (the skill's real AoE radius). Uses the prefab's hand-tuned <see cref="EffectDef.groundRingRadius"/> (the ring's
    /// size at authored scale) to back-compute the caller scale — so the SHARED <c>def.scale</c> (which release
    /// VfxSequences also read) is never touched. This is the ONE telegraph sizing path; the aim reticle and the
    /// wind-up circle both use it, so they can't drift from each other OR from the actual AoE. Loops until Stop().</summary>
    public static EffectHandle AttachGroundCircle(string id, Transform target, Vector3 offset, float worldRadius, Color tint)
    {
        var def = EffectCatalog.Get(id);
        if (def == null || target == null) return null;
        float ring = def.groundRingRadius > 0f ? def.groundRingRadius : 1f;   // 0 = uncalibrated → treat scale as 1:1
        return Attach(id, target, offset, worldRadius / ring, tint);
    }

    /// <summary>Pre-instantiate <paramref name="count"/> instances of an effect into the pool so the first
    /// combat use doesn't hitch. Call from a load/scene-setup step, never per frame.</summary>
    public static void Prewarm(string id, int count)
    {
        var def = EffectCatalog.Get(id);
        if (def == null) return;
        EnsureHost();
        EntityId key = def.prefab.GetEntityId();
        var pool = GetPool(key);
        for (int i = 0; i < count; i++)
        {
            var h = NewInstance(def, key);
            h.gameObject.SetActive(false);
            pool.Push(h);
        }
    }
}
