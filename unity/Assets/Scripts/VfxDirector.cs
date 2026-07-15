using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a <see cref="VfxSequence"/> — the runtime that turns the data recipe into timed calls on the existing
/// building blocks (<see cref="Vfx"/>, <see cref="Projectile3D"/>, <see cref="CameraFollow3D.ShakeMain"/>,
/// <see cref="Sfx"/>). It is the ONLY thing that "runs" a sequence; the asset itself is inert data.
///
/// <para>The tick relay is the crux of the code/data split: a step can expose a gameplay <see cref="VfxStep.tickId"/>
/// that fires <see cref="VfxStep.tickCount"/> times, <see cref="VfxStep.tickInterval"/> apart, at the step's
/// gameplay moment (a Carrier's arrival, or any step's start). Each tick (and each visual emit-event) is relayed to
/// the skill's <c>onTick(VfxTick)</c> callback — where the skill runs damage — carrying WHERE it happened. The
/// asset never touches gameplay; it only labels beats. Repeat-over-time is owned HERE (the tick schedule), so the
/// skill just applies one burst per tick.</para>
///
/// PERFORMANCE: runners are POOLED (like <see cref="Vfx"/> / <see cref="DamagePopup"/>) and driven by their own
/// Update — no per-play coroutine or <c>WaitForSeconds</c>. Sequences are cooldown-gated (cast-frequency, not
/// per-frame/per-hit), so the only remaining allocation is a tiny per-carrier arrival closure, off the steady hot path.
/// </summary>
public static class VfxDirector
{
    /// <summary>Play <paramref name="seq"/> at <paramref name="ctx"/>. <paramref name="onTick"/> (optional) is
    /// the skill's hook: it fires with each raised tick / event (as a <see cref="VfxTick"/>) so the skill can land
    /// damage on the exact beat, at the exact point. Returns the runner handle (rarely needed; for an attached/
    /// looping sequence call <see cref="VfxSequenceRunner.Stop"/>). No-op / null if the sequence is empty.</summary>
    public static VfxSequenceRunner Play(VfxSequence seq, VfxContext ctx, System.Action<VfxTick> onTick = null)
    {
        if (seq == null || seq.steps == null || seq.steps.Count == 0) return null;
        var r = Rent();
        r.Begin(seq, ctx, onTick);
        return r;
    }

    // ---- pool ---------------------------------------------------------------
    static Transform host;
    static readonly Stack<VfxSequenceRunner> pool = new Stack<VfxSequenceRunner>();

    static void EnsureHost()
    {
        if (host != null) return;
        pool.Clear();   // a scene unload destroyed the old host + its pooled children; drop stale refs
        host = new GameObject("VfxDirector") { hideFlags = HideFlags.DontSaveInEditor }.transform;   // runtime-only pool host — never serialize into a scene
    }

    static VfxSequenceRunner Rent()
    {
        EnsureHost();
        if (pool.Count > 0) return pool.Pop();
        var go = new GameObject("VfxSequenceRunner");
        go.transform.SetParent(host, false);
        return go.AddComponent<VfxSequenceRunner>();
    }

    internal static void Return(VfxSequenceRunner r)
    {
        if (host == null) { Object.Destroy(r.gameObject); return; }
        r.gameObject.SetActive(false);
        pool.Push(r);
    }
}

/// <summary>One pooled, self-updating playback of a <see cref="VfxSequence"/>. Added via AddComponent (never
/// wired in the editor). Reused across plays; a generation counter fences a late carrier arrival from bleeding
/// into the next play of the same reused runner.</summary>
public class VfxSequenceRunner : MonoBehaviour
{
    VfxSequence seq;
    VfxContext ctx;
    System.Action<VfxTick> onTick;
    bool[] started;                 // per-step, reused across plays (grown as needed)
    float[] endAt;                  // per-step absolute Time.time to raise emitEventOnEnd (∞ = none/handled)
    // Gameplay tick schedule, per-step (reused/grown). A step raises its tickId tickCount times, tickInterval apart,
    // starting when the step becomes gameplay-active (a Carrier's arrival, or another step's start).
    int[] tickLeft;
    float[] tickAt;
    Vector3[] tickPoint;
    readonly List<Vfx.EffectHandle> attached = new List<Vfx.EffectHandle>();
    float startTime, endBy;
    int gen;                        // bumped each Begin/Finish → stale carrier callbacks are ignored
    bool active;
    bool hasLiveAura;               // a persistent (Begin) Attach is running → never auto-tear-down; wait for Stop()

    internal void Begin(VfxSequence s, VfxContext c, System.Action<VfxTick> cb)
    {
        seq = s; ctx = c; onTick = cb;
        gen++;
        startTime = Time.time;
        active = true;
        hasLiveAura = false;
        attached.Clear();

        int n = seq.steps.Count;
        if (started == null || started.Length < n) { started = new bool[n]; endAt = new float[n]; tickLeft = new int[n]; tickAt = new float[n]; tickPoint = new Vector3[n]; }
        for (int i = 0; i < n; i++) { started[i] = false; endAt[i] = float.PositiveInfinity; tickLeft[i] = 0; }

        // Upper bound on when the last step can possibly fire: the latest timed delay, plus a carrier's full
        // flight time (an OnEvent step may be chained to an arrival), plus a pad. Then Finish tears down. Tick
        // schedules extend this further when they start (see ScheduleTicks).
        float maxDelay = 0f, maxCarrier = 0f;
        for (int i = 0; i < n; i++)
        {
            var st = seq.steps[i];
            if (st == null) continue;
            if (st.schedule == VfxSchedule.AtTime) maxDelay = Mathf.Max(maxDelay, st.delay);
            if (st.kind == VfxStepKind.Carrier) maxCarrier = Mathf.Max(maxCarrier, st.carrierMaxLife);
        }
        endBy = startTime + maxDelay + maxCarrier + 1f;

        gameObject.SetActive(true);
    }

    /// <summary>Stop an attached/looping sequence early (fades its Attach effects) and return to the pool.</summary>
    public void Stop() { Finish(); }

    void Update()
    {
        if (!active) return;
        float t = Time.time - startTime;
        float now = Time.time;

        var steps = seq.steps;
        for (int i = 0; i < steps.Count && i < started.Length; i++)   // bound: a tuner may add steps mid-play
        {
            var st = steps[i];
            if (st == null || started[i]) continue;
            if (st.schedule == VfxSchedule.AtTime && t >= st.delay) Execute(i);
        }

        // Drain any due gameplay ticks (raise the tickId, tickCount times, tickInterval apart). interval 0 = all now.
        for (int i = 0; i < steps.Count && i < tickLeft.Length; i++)
        {
            if (tickLeft[i] <= 0) continue;
            float iv = steps[i].tickInterval;
            while (tickLeft[i] > 0 && (iv <= 0f || now >= tickAt[i]))
            {
                Emit(steps[i].tickId, tickPoint[i], true);
                tickLeft[i]--;
                if (iv > 0f) tickAt[i] += iv;
            }
        }

        // Fire any pending "on end" events whose effect has now finished (PlayAt lifetime, Shake duration, …).
        for (int i = 0; i < steps.Count && i < endAt.Length; i++)
        {
            if (float.IsInfinity(endAt[i]) || now < endAt[i]) continue;
            endAt[i] = float.PositiveInfinity;
            Emit(steps[i].emitEventOnEnd, ctx.target, false);
        }

        if (!hasLiveAura && t >= (endBy - startTime)) Finish();
    }

    // Fire a step's action. emitEventOnStart raises the instant the step begins; emitEventOnEnd raises when the
    // visual FINISHES — a Carrier on arrival (async), a PlayAt/Shake when its scheduled end time is reached (see
    // Update), an Attach when the sequence stops (see Finish). A step's gameplay tick starts here (non-carrier) or
    // on arrival (carrier).
    void Execute(int i, bool fromEvent = false, Vector3 eventPoint = default, bool hasEventPoint = false)
    {
        started[i] = true;
        var st = seq.steps[i];
        // An OnEvent step can snap to the exact point the emitter reported (e.g. a Carrier's scattered landing)
        // instead of re-resolving the plain anchor. Opt-in per step via snapToEventPoint.
        Vector3 anchorPoint = (fromEvent && hasEventPoint && st.snapToEventPoint)
            ? eventPoint
            : (st.anchor == VfxAnchor.Origin ? ctx.origin : ctx.target);
        // Optional random spawn jitter (PlayAt/Attach) so repeated plays don't stack in the exact same spot.
        Vector3 jitter = (st.scatter && (st.kind == VfxStepKind.PlayAt || st.kind == VfxStepKind.Attach)) ? ScatterJitter() : Vector3.zero;
        Vector3 point = anchorPoint + st.offset + jitter;
        float scale = st.scale * (ctx.scaleMult <= 0f ? 1f : ctx.scaleMult);

        Emit(st.emitEventOnStart, point, false);

        switch (st.kind)
        {
            case VfxStepKind.PlayAt:
                var ph = Vfx.PlayAt(st.effectId, point, Quaternion.Euler(st.rotationEuler),
                           new Vfx.VfxParams { scale = scale, speed = st.speed, tint = st.tint, tailTrim = st.trimEnd, clipStart = st.clipStart });
                float endT = (ph != null && ph.ReturnAt > 0f) ? ph.ReturnAt : Time.time + 0.5f;
                ScheduleEnd(i, endT);
                ScheduleTicks(i, point);   // non-carrier: gameplay ticks start at spawn
                break;

            case VfxStepKind.Attach:
                if (ctx.follow != null)
                {
                    // Persistent (Begin) → loop until the caller Stops it. One-shot (Fire) → play the effect's
                    // authored lifetime once, and extend teardown so the sequence doesn't cut the aura short.
                    // Anchor at the grounded ctx.origin (feet), not the raw follow pivot (visual centre), and keep
                    // tracking the unit's x/z — otherwise a ground-authored aura rides up to the character's head.
                    Vector3 followOff = st.offset + jitter + (ctx.origin - ctx.follow.position);
                    var h = Vfx.Attach(st.effectId, ctx.follow, followOff, scale, st.tint, oneShot: !ctx.persistent);
                    if (h != null)
                    {
                        attached.Add(h);
                        if (ctx.persistent) hasLiveAura = true;
                        else if (h.ReturnAt + 0.5f > endBy) endBy = h.ReturnAt + 0.5f;
                    }
                    ScheduleTicks(i, point);   // ticks fire at the followed unit's current point (resolved each raise below)
                }
                break;

            case VfxStepKind.Carrier:
                // Scatter both ends independently: the impact spreads around the anchor (a shower) and the launch
                // origin jitters too. The event/ticks still fire on arrival at the scattered landing.
                Vector3 land = point;
                Vector3 from = point + st.carrierFrom;
                if (st.carrierScatter > 0f)
                {
                    float sc = st.carrierScatter;
                    land += new Vector3(Random.Range(-sc, sc), 0f, Random.Range(-sc, sc));
                    from = land + st.carrierFrom + new Vector3(Random.Range(-sc, sc), 0f, Random.Range(-sc, sc));
                }
                int g = gen; string tag = st.emitEventOnEnd; Vector3 landing = land; int idx = i;
                Projectile3D.SpawnVfx(st.effectId, scale, from, null, land,
                    st.carrierSpeed, st.carrierArriveRadius, st.carrierMaxLife,
                    () => { if (gen == g) { Emit(tag, landing, true); ScheduleTicks(idx, landing); } },   // arrival == end → raise + tick on THIS play only, at the real landing
                    st.carrierFaceTravel, st.clipStart);
                break;

            case VfxStepKind.Shake:
                CameraFollow3D.ShakeMain(st.shakeStrength, st.shakeDuration);
                ScheduleEnd(i, Time.time + st.shakeDuration);
                break;

            case VfxStepKind.Sound:
                if (st.sound != null && !st.sound.Empty)
                    Sfx.PlayAt(st.sound, point, st.soundFadeIn, st.soundFadeOut, st.soundDuration);
                ScheduleEnd(i, Time.time);   // one-shot cue: end ≈ start
                break;
        }
    }

    // A random per-axis nudge (magnitude up to 0.2 units) for a step's "scatter" option.
    static Vector3 ScatterJitter() =>
        new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.2f, 0.2f), Random.Range(-0.2f, 0.2f));

    // Arm an "on end" event for step i at absolute time t (no-op if the step has no on-end tag). Extends the
    // teardown deadline so a late end-event (and anything it chains) still fires before Finish.
    void ScheduleEnd(int i, float t)
    {
        if (string.IsNullOrEmpty(seq.steps[i].emitEventOnEnd)) return;
        endAt[i] = t;
        if (t + 1f > endBy) endBy = t + 1f;
    }

    // Arm step i's gameplay ticks: tickCount raises of tickId, tickInterval apart, starting now at `point`. No-op
    // for a pure-visual step. Extends the teardown deadline to outlive the last tick.
    void ScheduleTicks(int i, Vector3 point)
    {
        var st = seq.steps[i];
        if (!st.HasTick) return;
        tickLeft[i] = st.tickCount;
        tickAt[i] = Time.time;
        tickPoint[i] = point;
        float last = Time.time + Mathf.Max(0f, st.tickInterval) * Mathf.Max(0, st.tickCount - 1);
        if (last + 1f > endBy) endBy = last + 1f;
    }

    // Relay a raised tag+point: hand it to the skill (VfxTick → damage), then start any OnEvent steps keyed to it.
    // hasPoint carries the emitter's exact world point (e.g. a Carrier's real landing / a tick point) so an OnEvent
    // step opted into snapToEventPoint can anchor there instead of the plain context anchor.
    void Emit(string tag, Vector3 point, bool hasPoint)
    {
        if (string.IsNullOrEmpty(tag)) return;
        onTick?.Invoke(new VfxTick(tag, point));
        var steps = seq.steps;
        for (int i = 0; i < steps.Count && i < started.Length; i++)   // bound: a tuner may add steps mid-play
        {
            var st = steps[i];
            if (st == null || started[i]) continue;
            if (st.schedule == VfxSchedule.OnEvent && st.startEvent == tag) Execute(i, true, point, hasPoint);
        }
    }

    void Finish()
    {
        if (!active) return;
        active = false;

        // Attach steps "end" when the sequence stops — raise their on-end tag to the skill (damage hook) before
        // teardown. Direct callback only (we're tearing down, so we don't start fresh OnEvent steps here).
        var steps = seq.steps;
        for (int i = 0; i < steps.Count && i < started.Length; i++)
            if (started[i] && steps[i] != null && steps[i].kind == VfxStepKind.Attach
                && !string.IsNullOrEmpty(steps[i].emitEventOnEnd))
                onTick?.Invoke(new VfxTick(steps[i].emitEventOnEnd, ctx.follow != null ? ctx.follow.position : ctx.target));

        gen++;   // fence any carrier still in flight from this play
        for (int i = 0; i < attached.Count; i++)
            if (attached[i] != null) attached[i].Stop();
        attached.Clear();
        onTick = null;
        VfxDirector.Return(this);
    }
}
