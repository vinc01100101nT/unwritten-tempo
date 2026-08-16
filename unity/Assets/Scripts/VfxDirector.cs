using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a <see cref="VfxSequence"/> — the runtime that turns the data recipe into timed calls on the existing
/// building blocks (<see cref="Vfx"/>, <see cref="Projectile3D"/>, <see cref="CameraFollow3D.ShakeMain"/>,
/// <see cref="Sfx"/>). It is the ONLY thing that "runs" a sequence; the asset itself is inert data.
///
/// <para>The tick relay is the crux of the code/data split: a step can expose a gameplay <see cref="VfxStep.tickId"/>
/// that fires per its <see cref="VfxStep.tickWhen"/> rule (Start / End / every <see cref="VfxStep.tickInterval"/>), at the step's
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
        host = new GameObject("VfxDirector") { hideFlags = HideFlags.DontSaveInEditor }.transform;
    }

    static VfxSequenceRunner Rent()
    {
        EnsureHost();
        if (pool.Count > 0) return pool.Pop();
        var go = new GameObject("VfxSequenceRunner") { hideFlags = HideFlags.DontSaveInEditor };
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
    float[] endAt;                  // per-step absolute Time.time the step's visual finishes → emitEventOnEnd + an End tick (∞ = none/handled)
    bool[] endFired;                // per-step: the step's "end" (event + End tick) has already fired → Finish won't re-fire it
    // Gameplay tick schedule, per-step (reused/grown). tickWhen decides placement: Start fires once at Execute; End
    // fires at endAt (or a Carrier's arrival / the aura's Stop); Interval fires every tickInterval until tickUntil.
    float[] tickAt;                 // Interval: next fire time (∞ = interval inactive for this step)
    float[] tickUntil;              // Interval: window end (∞ = unbounded — a persistent aura, ticks until Finish)
    Vector3[] tickPoint;
    Transform[] tickFollow;         // a live mover/followed unit — while non-null, ticks resolve at its CURRENT position
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
        if (started == null || started.Length < n)
        { started = new bool[n]; endAt = new float[n]; endFired = new bool[n]; tickAt = new float[n]; tickUntil = new float[n]; tickPoint = new Vector3[n]; tickFollow = new Transform[n]; }
        for (int i = 0; i < n; i++)
        { started[i] = false; endAt[i] = float.PositiveInfinity; endFired[i] = false; tickAt[i] = float.PositiveInfinity; tickUntil[i] = float.PositiveInfinity; tickFollow[i] = null; }

        // Upper bound on when the last step can possibly fire: the latest timed delay, plus a carrier's full
        // flight time (an OnEvent step may be chained to an arrival), plus a pad. Then Finish tears down. Tick
        // schedules extend this further when they start (see ArmTicks).
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

        // Interval ticks: raise the tickId every tickInterval while the step's window is open. A live mover/followed
        // unit (a flying Carrier, a tracked aura) makes each tick land at its CURRENT position (damage along the path).
        for (int i = 0; i < steps.Count && i < tickAt.Length; i++)
        {
            if (float.IsInfinity(tickAt[i])) continue;   // interval not active for this step
            if (tickFollow[i] != null) tickPoint[i] = tickFollow[i].position;
            float iv = Mathf.Max(0.02f, steps[i].tickInterval);
            while (now >= tickAt[i] && (float.IsInfinity(tickUntil[i]) || tickAt[i] <= tickUntil[i] + 1e-4f))
            {
                Emit(steps[i].tickId, tickPoint[i], true);
                tickAt[i] += iv;
            }
            if (!float.IsInfinity(tickUntil[i]) && now >= tickUntil[i]) tickAt[i] = float.PositiveInfinity;   // window closed
        }

        // Window end: when a step's visual finishes, raise emitEventOnEnd AND (for End timing) its one gameplay tick.
        for (int i = 0; i < steps.Count && i < endAt.Length; i++)
        {
            if (float.IsInfinity(endAt[i]) || now < endAt[i]) continue;
            endAt[i] = float.PositiveInfinity;
            EmitStepEnd(i, tickFollow[i] != null ? tickFollow[i].position : tickPoint[i]);
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
        // Origin samples the caster's LIVE position at the moment THIS step fires (ctx.follow), so a DELAYED step
        // during a moving cast (dash, knockback) stamps where the caster is NOW — a fresh snapshot, not a follow.
        // Falls back to the cast-time ctx.origin for point contexts that carry no follow transform.
        Vector3 originPoint = ctx.follow != null ? ctx.follow.position : ctx.origin;
        Vector3 anchorPoint = (fromEvent && hasEventPoint && st.snapToEventPoint)
            ? eventPoint
            : (st.anchor == VfxAnchor.Origin ? originPoint : ctx.target);
        // Optional random spawn jitter (PlayAt/Attach) so repeated plays don't stack in the exact same spot.
        Vector3 jitter = (st.scatter && (st.kind == VfxStepKind.PlayAt || st.kind == VfxStepKind.Attach)) ? ScatterJitter() : Vector3.zero;
        Vector3 point = anchorPoint + st.offset + jitter;
        float scale = st.scale * (ctx.scaleMult <= 0f ? 1f : ctx.scaleMult);

        Emit(st.emitEventOnStart, point, false);

        switch (st.kind)
        {
            case VfxStepKind.PlayAt:
            {
                // Looping PlayAt: keep the particle loop on and live for the skill's Stay Duration; otherwise a
                // one-shot that plays once for its natural length. The window end (natural or Stay) drives End ticks.
                float stayP = Stay(st);
                var ph = Vfx.PlayAt(st.effectId, point, Quaternion.Euler(st.rotationEuler),
                           new Vfx.VfxParams { scale = scale, speed = st.speed, tint = st.tint, tailTrim = st.trimEnd,
                                               clipStart = st.clipStart, loop = st.looping, lifetime = st.looping ? stayP : 0f });
                float endP = (ph != null && ph.ReturnAt > 0f) ? ph.ReturnAt : Time.time + (st.looping ? stayP : 0.5f);
                ArmTicks(i, point, endP, null);
                break;
            }

            case VfxStepKind.Attach:
                if (ctx.follow != null)
                {
                    // Anchor at the grounded ctx.origin (feet), not the raw follow pivot (visual centre), so a
                    // ground-authored aura doesn't ride up to the character's head; keep tracking its x/z.
                    Vector3 followOff = st.offset + jitter + (ctx.origin - ctx.follow.position);
                    // Loops when the step asks (skill, capped by Stay Duration) OR the caller holds it (a status aura
                    // via VfxCue.Begin, which lives until Stop). A non-looping Attach plays its natural length once.
                    bool loop = st.looping || ctx.persistent;
                    float stayA = Stay(st);
                    float lifeA = (st.looping && !ctx.persistent) ? stayA : 0f;   // 0 → authored (persistent = until Stop)
                    var h = Vfx.Attach(st.effectId, ctx.follow, followOff, scale, st.tint, oneShot: !loop, clipStart: 0f, lifetime: lifeA,
                                       speed: st.speed <= 0f ? 1f : st.speed);   // 0 = unauthored / legacy step → normal rate
                    float endA;
                    if (ctx.persistent) { endA = float.PositiveInfinity; if (h != null) hasLiveAura = true; }
                    else { endA = (h != null && h.ReturnAt > 0f) ? h.ReturnAt : Time.time + (st.looping ? stayA : 0.5f);
                           if (endA + 0.5f > endBy) endBy = endA + 0.5f; }
                    if (h != null) attached.Add(h);
                    ArmTicks(i, point, endA, ctx.follow);
                }
                break;

            case VfxStepKind.Carrier:
            {
                // Scatter both ends independently: the impact spreads around the anchor (a shower) and the launch
                // origin jitters too. The event/ticks still fire on arrival at the scattered landing.
                Vector3 land = point;
                Vector3 fromBase = st.carrierFromCaster ? ctx.origin : point;
                Vector3 from = fromBase + st.carrierFrom;
                if (st.carrierScatter > 0f)
                {
                    float sc = st.carrierScatter;
                    land += new Vector3(Random.Range(-sc, sc), 0f, Random.Range(-sc, sc));
                    // Off caster: launch re-bases on the scattered landing (a straight-down shower). From caster:
                    // launch stays at the fixed caster, only the impact spreads.
                    Vector3 fb = st.carrierFromCaster ? fromBase : land;
                    from = fb + st.carrierFrom + new Vector3(Random.Range(-sc, sc), 0f, Random.Range(-sc, sc));
                }
                int g = gen; string tag = st.emitEventOnEnd; Vector3 landing = land; int idx = i;
                var mover = Projectile3D.SpawnVfx(st.effectId, scale, from, null, land,
                    st.carrierSpeed, st.carrierArriveRadius, st.carrierMaxLife,
                    () => { if (gen == g) { Emit(tag, landing, true); OnCarrierArrive(idx, landing); } },
                    st.carrierFaceTravel, st.clipStart, st.tint);
                // Carrier tick placement: Start = one tick at launch; Interval = every tickInterval along the flight
                // (tracks the live mover, capped by carrierMaxLife, closed on arrival); End = one tick on arrival.
                if (st.HasTick)
                {
                    tickPoint[i] = from;
                    if (st.tickWhen == TickTiming.Start) Emit(st.tickId, from, true);
                    else if (st.tickWhen == TickTiming.Interval)
                    { tickFollow[i] = mover; tickAt[i] = Time.time; tickUntil[i] = Time.time + st.carrierMaxLife; }
                }
                break;
            }

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

    // Arm the step's "end" (visual finish) at absolute time t: drives emitEventOnEnd AND an End-timing tick. Always
    // records the time (even pure-visual) so teardown outlives it; EmitStepEnd decides what actually fires.
    void ScheduleEnd(int i, float t)
    {
        endAt[i] = t;
        if (t + 1f > endBy) endBy = t + 1f;
    }

    // The default Stay Duration for a looping PlayAt/Attach when the skill doesn't set one.
    const float DefaultStay = 3f;
    // Per-step stay: the skill supplies it by the step's tickId (a looping step with no binding falls back to default).
    float Stay(VfxStep st)
    {
        float s = ctx.stayForTick != null && !string.IsNullOrEmpty(st.tickId) ? ctx.stayForTick(st.tickId) : 0f;
        return s > 0f ? s : DefaultStay;
    }

    // Set up a PlayAt/Attach step's ticks + end, given its active window [now, endT] (endT = ∞ for a persistent aura).
    // Start fires one tick now; Interval fires every tickInterval until endT; End fires from EmitStepEnd at endT.
    void ArmTicks(int i, Vector3 point, float endT, Transform follow)
    {
        var st = seq.steps[i];
        tickPoint[i] = point;
        tickFollow[i] = follow;
        if (!float.IsInfinity(endT)) ScheduleEnd(i, endT);
        if (!st.HasTick) return;
        if (st.tickWhen == TickTiming.Start) Emit(st.tickId, point, true);
        else if (st.tickWhen == TickTiming.Interval)
        {
            tickAt[i] = Time.time;
            tickUntil[i] = endT;
            if (!float.IsInfinity(endT) && endT + 1f > endBy) endBy = endT + 1f;
        }
    }

    // Raise a step's end (emitEventOnEnd + an End-timing tick) exactly once, at `point`.
    void EmitStepEnd(int i, Vector3 point)
    {
        if (endFired[i]) return;
        endFired[i] = true;
        var st = seq.steps[i];
        Emit(st.emitEventOnEnd, point, false);
        if (st.HasTick && st.tickWhen == TickTiming.End) Emit(st.tickId, point, true);
    }

    // A flying Carrier reached its landing: fire an End tick there, or close an Interval flight.
    void OnCarrierArrive(int i, Vector3 landing)
    {
        var st = seq.steps[i];
        if (st.HasTick)
        {
            if (st.tickWhen == TickTiming.End) Emit(st.tickId, landing, true);
            else if (st.tickWhen == TickTiming.Interval) { tickAt[i] = float.PositiveInfinity; tickFollow[i] = null; }
        }
        endFired[i] = true;
    }

    // Relay a raised tag+point: hand it to the skill (VfxTick → damage), then start any OnEvent steps keyed to it.
    // hasPoint carries the emitter's exact world point (e.g. a Carrier's real landing / a tick point) so an OnEvent
    // step opted into snapToEventPoint can anchor there instead of the plain context anchor.
    void Emit(string tag, Vector3 point, bool hasPoint)
    {
        if (string.IsNullOrEmpty(tag)) return;
        onTick?.Invoke(new VfxTick(tag, point, ctx.targetUnit, ctx.effectPoint, ctx.isRepeat));
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

        // Any step whose end hasn't fired yet (a persistent aura, or a step cut short by teardown) "ends" now —
        // raise its on-end tag AND an End-timing tick to the skill (damage hook). Direct callback only (we're tearing
        // down, so we don't start fresh OnEvent steps here).
        var steps = seq.steps;
        for (int i = 0; i < steps.Count && i < started.Length; i++)
        {
            if (!started[i] || endFired[i] || steps[i] == null) continue;
            var st = steps[i];
            Vector3 p = tickFollow[i] != null ? tickFollow[i].position : (ctx.follow != null ? ctx.follow.position : ctx.target);
            if (!string.IsNullOrEmpty(st.emitEventOnEnd)) onTick?.Invoke(new VfxTick(st.emitEventOnEnd, p, ctx.targetUnit, ctx.effectPoint, ctx.isRepeat));
            if (st.HasTick && st.tickWhen == TickTiming.End) onTick?.Invoke(new VfxTick(st.tickId, p, ctx.targetUnit, ctx.effectPoint, ctx.isRepeat));
            endFired[i] = true;
        }

        gen++;   // fence any carrier still in flight from this play
        for (int i = 0; i < attached.Count; i++)
            if (attached[i] != null) attached[i].Stop();
        attached.Clear();
        onTick = null;
        VfxDirector.Return(this);
    }
}
