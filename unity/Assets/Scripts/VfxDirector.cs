using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a <see cref="VfxSequence"/> — the runtime that turns the data recipe into timed calls on the existing
/// building blocks (<see cref="Vfx"/>, <see cref="Projectile3D"/>, <see cref="CameraFollow3D.ShakeMain"/>,
/// <see cref="Sfx"/>). It is the ONLY thing that "runs" a sequence; the asset itself is inert data.
///
/// <para><b>One schedule, drawn only where there is a screen.</b> The server and every watching machine run the SAME
/// runner over the same seeded dice, so ticks, chained steps and flights land on the same moments everywhere — that is
/// what the server measures damage against. Only a watching machine draws (<see cref="VfxSequenceRunner.Draws"/>): on
/// the server every effect, shake and sound is skipped, a flight is a bare moving point, and nothing else changes.</para>
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

    // ---- stop previous on replay --------------------------------------------
    // The newest copy of each opted-in step on each caster, keyed by sequence + step index + the caster's instance id.
    // Held with its generation, so an entry whose effect has already ended and been reused elsewhere is never stopped.
    struct LatestCopy { public Vfx.EffectHandle handle; public int generation; }
    static readonly Dictionary<(VfxSequence, int, int), LatestCopy> latestCopies = new Dictionary<(VfxSequence, int, int), LatestCopy>();
    static readonly List<(VfxSequence, int, int)> staleCopies = new List<(VfxSequence, int, int)>();
    const int PruneLatestCopiesAbove = 64;

    /// <summary>Remove, instantly, the copy of this step still showing on <paramref name="caster"/> from an earlier
    /// play, if there is one. Called BEFORE the new copy is spawned, so the pool hands the freed instance straight
    /// back instead of building a second one.</summary>
    internal static void StopLatestCopy(VfxSequence seq, int step, Transform caster)
    {
        if (seq == null || caster == null) return;
        var key = (seq, step, caster.GetInstanceID());
        if (!latestCopies.TryGetValue(key, out var old)) return;
        latestCopies.Remove(key);
        if (old.handle != null && old.handle.IsActive && old.handle.Generation == old.generation) old.handle.Stop(true);
    }

    /// <summary>Remember <paramref name="copy"/> as the one the next play of this step on this caster removes.</summary>
    internal static void RememberLatestCopy(VfxSequence seq, int step, Transform caster, Vfx.EffectHandle copy)
    {
        if (seq == null || caster == null || copy == null) return;
        if (latestCopies.Count >= PruneLatestCopiesAbove) PruneLatestCopies();
        latestCopies[(seq, step, caster.GetInstanceID())] = new LatestCopy { handle = copy, generation = copy.Generation };
    }

    // Casters that stopped playing (left, died for good) leave entries behind; drop every one whose effect is over.
    static void PruneLatestCopies()
    {
        staleCopies.Clear();
        foreach (var kv in latestCopies)
        {
            var c = kv.Value;
            if (c.handle == null || !c.handle.IsActive || c.handle.Generation != c.generation) staleCopies.Add(kv.Key);
        }
        for (int i = 0; i < staleCopies.Count; i++) latestCopies.Remove(staleCopies[i]);
        staleCopies.Clear();
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
    /// <summary>TRUE on a machine with a screen. The server keeps every beat of a sequence and draws none of it.</summary>
    internal static readonly bool Draws = !GameServer.IsServer;

    VfxSequence seq;
    VfxContext ctx;
    System.Action<VfxTick> onTick;
    bool[] started;                 // per-step, reused across plays (grown as needed)
    float[] endAt;                  // per-step absolute Time.time the step's visual finishes → emitEventOnEnd + an End tick (∞ = none/handled)
    bool[] endFired;                // per-step: the step's "end" (event + End tick) has already fired → Finish won't re-fire it
    // Gameplay tick schedule. tickWhen decides placement: Start fires once at Execute; End fires at endAt (or a
    // Carrier's arrival / the aura's Stop); Interval fires every tickInterval until its window closes.
    Vector3[] tickPoint;            // per-step: where the step's visual sits (the teardown/End paths read it)
    Transform[] tickFollow;         // per-step: a live mover/followed unit — while non-null, the End paths resolve at its CURRENT position
    int[] carriersLive;             // per-step: copies of this carrier still flying — the step has ended when the LAST one lands

    /// <summary>One LIVE interval schedule: a tickId firing every so often while something is active. A PlayAt/Attach
    /// step has exactly one; a CARRIER step has one PER COPY in flight, which is the whole reason these are a list
    /// rather than a per-step slot — five fanned bolts each tick along their OWN path, at their own position.</summary>
    struct IntervalTick
    {
        public int step;
        public float nextAt;        // absolute Time.time of the next fire
        public float until;         // window end (∞ = unbounded — a persistent aura, ticks until Finish)
        public Vector3 point;
        public Transform follow;    // non-null → each tick resolves at its CURRENT position
    }
    readonly List<IntervalTick> intervals = new List<IntervalTick>(4);
    // Each kept with the play it belonged to: a trail removed early by a newer one goes back to the pool and may be
    // showing someone else's effect by the time this sequence tears down.
    struct HeldEffect { public Vfx.EffectHandle handle; public int generation; }
    readonly List<HeldEffect> attached = new List<HeldEffect>();
    // THIS PLAY'S OWN DICE, rolled from the seed the cast carried, so every machine drawing this cast puts the
    // scattered shots in the same places — and therefore measures their damage from the same places.
    CombatRng.Stream dice;
    float startTime, endBy;
    int gen;                        // bumped each Begin/Finish → stale carrier callbacks are ignored

    /// <summary>WHICH PLAY THIS RUNNER IS ON. These come out of a pool, so a handle somebody kept can end up
    /// pointing at a runner that has since finished and been handed to something else entirely — and stopping it
    /// then would tear down whatever is using it now. Anything holding a runner past the frame it started it keeps
    /// this number beside it and checks the two still agree.</summary>
    internal int Generation => gen;
    bool active;
    bool hasLiveAura;               // a persistent (Begin) Attach is running → never auto-tear-down; wait for Stop()

    internal void Begin(VfxSequence s, VfxContext c, System.Action<VfxTick> cb)
    {
        seq = s; ctx = c; onTick = cb;
        // An unseeded play rolls its own run — the single-machine case, where there is nothing to agree with.
        dice = new CombatRng.Stream(ctx.seed != 0 ? ctx.seed : CombatRng.NewSeed());
        gen++;
        startTime = Time.time;
        active = true;
        hasLiveAura = false;
        attached.Clear();
        intervals.Clear();

        int n = seq.steps.Count;
        if (started == null || started.Length < n)
        { started = new bool[n]; endAt = new float[n]; endFired = new bool[n]; tickPoint = new Vector3[n]; tickFollow = new Transform[n]; carriersLive = new int[n]; }
        for (int i = 0; i < n; i++)
        { started[i] = false; endAt[i] = float.PositiveInfinity; endFired[i] = false; tickFollow[i] = null; carriersLive[i] = 0; }

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

        // Interval ticks: raise the tickId every tickInterval while each live schedule's window is open. A live
        // mover/followed unit (a flying Carrier copy, a tracked aura) makes each tick land at its CURRENT position
        // (damage along the path). Walked BACKWARDS so a closed window can be dropped in place, and so a tick that
        // starts a fresh step mid-loop (appended at the end) waits for the next frame instead of shifting this pass.
        for (int k = intervals.Count - 1; k >= 0; k--)
        {
            var it = intervals[k];
            if (it.step >= steps.Count || steps[it.step] == null) { intervals.RemoveAt(k); continue; }
            if (it.follow != null) it.point = it.follow.position;
            float iv = Mathf.Max(0.02f, steps[it.step].tickInterval);
            while (now >= it.nextAt && (float.IsInfinity(it.until) || it.nextAt <= it.until + 1e-4f))
            {
                Emit(steps[it.step].tickId, it.point, true);
                it.nextAt += iv;
            }
            if (!float.IsInfinity(it.until) && now >= it.until) { intervals.RemoveAt(k); continue; }   // window closed
            intervals[k] = it;
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
        // The jitter moves the step's TICK POINT as well as its picture (see the `point` below feeding ArmTicks), so
        // it is rolled from the cast's shared stream for exactly the reason a carrier's scatter is.
        Vector3 point = anchorPoint + st.offset + jitter;
        float scale = st.scale * (ctx.scaleMult <= 0f ? 1f : ctx.scaleMult);
        // ON A PERSON AWAY ON A TELEPORT nothing is drawn or heard (TeleportLook.Covers) — the step still keeps its
        // beats, so what it decides lands on time. A lasting aura is still started: it hides itself until they appear.
        bool covered = TeleportLook.Covers(ctx.follow);
        bool draws = Draws && !covered;

        Emit(st.emitEventOnStart, point, false);

        switch (st.kind)
        {
            case VfxStepKind.PlayAt:
            {
                // Looping PlayAt: keep the particle loop on and live for the skill's Stay Duration; otherwise a
                // one-shot that plays once for its natural length. The window end (natural or Stay) drives End ticks.
                float stayP = Stay(st);
                if (draws)
                {
                    if (st.stopPreviousOnReplay) VfxDirector.StopLatestCopy(seq, i, ctx.follow);
                    var hP = Vfx.PlayAt(st.effectId, point, Quaternion.Euler(st.rotationEuler),
                               new Vfx.VfxParams { scale = scale, speed = st.speed, tint = st.tint, tailTrim = st.trimEnd,
                                                   clipStart = st.clipStart, loop = st.looping, lifetime = st.looping ? stayP : 0f });
                    if (st.stopPreviousOnReplay) VfxDirector.RememberLatestCopy(seq, i, ctx.follow, hP);
                }
                // WHEN THIS BEAT LANDS is read off the effect TABLE, never off the instance we may or may not have
                // just built. A machine that draws nothing has to reach the same moment as one that does, and asking
                // the handle made the answer depend on having a screen.
                float endP = StepEnds(st, st.looping ? stayP : 0f, !st.looping, stayP);
                ArmTicks(i, point, endP, null);
                break;
            }

            case VfxStepKind.Attach:
                if (ctx.follow != null)
                {
                    // Anchor at the grounded ctx.origin (feet), not the raw follow pivot (visual centre), so a
                    // ground-authored aura doesn't ride up to the character's head; keep tracking its x/z.
                    // HEIGHT ONLY. ctx.origin was taken when the play began and this step can fire frames later, so
                    // a dash has already carried the body away from it; keeping that sideways gap froze every trail
                    // parallel to the real path, up to a unit off (measured 2026-09-17, Blade Dance's first beats).
                    Vector3 followOff = st.offset + jitter + new Vector3(0f, ctx.origin.y - ctx.follow.position.y, 0f);
                    // Loops when the step asks (skill, capped by Stay Duration) OR the caller holds it (a status aura
                    // via VfxCue.Begin, which lives until Stop). A non-looping Attach plays its natural length once.
                    bool loop = st.looping || ctx.persistent;
                    float stayA = Stay(st);
                    float lifeA = (st.looping && !ctx.persistent) ? stayA : 0f;   // 0 → authored (persistent = until Stop)
                    if (Draws && (ctx.persistent || !covered))
                    {
                        if (st.stopPreviousOnReplay) VfxDirector.StopLatestCopy(seq, i, ctx.follow);
                        var h = Vfx.Attach(st.effectId, ctx.follow, followOff, scale, st.tint, oneShot: !loop, clipStart: 0f, lifetime: lifeA,
                                           speed: st.speed <= 0f ? 1f : st.speed);   // 0 = unauthored / legacy step → normal rate
                        if (h != null) attached.Add(new HeldEffect { handle = h, generation = h.Generation });
                        if (st.stopPreviousOnReplay) VfxDirector.RememberLatestCopy(seq, i, ctx.follow, h);
                    }
                    float endA;
                    // The aura's latch reads whether this effect EXISTS, not whether we drew it — a server holds a
                    // persistent aura open for exactly as long as the machines watching it do.
                    if (ctx.persistent) { endA = float.PositiveInfinity; if (EffectCatalog.Has(st.effectId)) hasLiveAura = true; }
                    else { endA = StepEnds(st, lifeA, !loop, stayA);
                           if (endA + 0.5f > endBy) endBy = endA + 0.5f; }
                    ArmTicks(i, point, endA, ctx.follow);
                }
                break;

            case VfxStepKind.Carrier:
            {
                // THIS step's volley — how many copies of THIS carrier fly, and how wide. Asked per step (by tickId),
                // so a sequence holding a bolt and a meteor gives each its own answer instead of one count multiplying
                // the whole sequence and every other step in it.
                var fan = Fan(st);
                int shots = fan.count;
                float half = fan.spreadDegrees * 0.5f;
                Vector3 fromBase = st.carrierFromCaster ? ctx.origin : point;
                carriersLive[i] = shots;
                tickPoint[i] = fromBase + st.carrierFrom;

                for (int c = 0; c < shots; c++)
                {
                    // Scatter both ends independently, per copy: the impact spreads around the anchor (a shower) and
                    // the launch origin jitters too. The event/ticks still fire on arrival at the scattered landing.
                    Vector3 land = point;
                    Vector3 from = fromBase + st.carrierFrom;
                    if (st.carrierScatter > 0f)
                    {
                        float sc = st.carrierScatter;
                        land += new Vector3(dice.Range(-sc, sc), 0f, dice.Range(-sc, sc));
                        // Off caster: launch re-bases on the scattered landing (a straight-down shower). From caster:
                        // launch stays at the fixed caster, only the impact spreads.
                        Vector3 fb = st.carrierFromCaster ? fromBase : land;
                        from = fb + st.carrierFrom + new Vector3(dice.Range(-sc, sc), 0f, dice.Range(-sc, sc));
                    }
                    // The fan turns each copy's LANDING about its launch point, so every shot leaves the same muzzle
                    // and diverges from there. A fall-in carrier launches straight above its landing, so there is no
                    // horizontal line to turn and it stays a single drop however wide the cone is set.
                    if (shots > 1 && half > 0f)
                    {
                        float ang = Mathf.Lerp(-half, half, c / (float)(shots - 1));
                        land = from + Quaternion.AngleAxis(ang, Vector3.up) * (land - from);
                    }

                    int g = gen; string tag = st.emitEventOnEnd; Vector3 landing = land; int idx = i;
                    Transform mover = null;
                    Vfx.EffectHandle travel = null;
                    // Each copy's arrival re-arms the steps chained to it, so a volley of five gets five landings —
                    // five explosions, five impact sounds — instead of one. Without that, an OnEvent step is once per
                    // sequence play, and four of the five bolts would land in silence.
                    // The flight itself is a bare moving point on every machine, so it arrives on the same frame-steps
                    // everywhere; the effect riding it is a screen's alone, and comes off the moment it lands.
                    mover = st.carrierArc
                        ? Projectile3D.FlyArc(from, land, st.carrierLaunchDegrees, st.carrierDrawsArrow && draws,
                        () =>
                        {
                            if (travel != null) travel.Stop();
                            if (gen == g) { Emit(tag, landing, true, rearmChained: true); OnCarrierArrive(idx, landing, mover); }
                        })
                        : Projectile3D.Fly(from, land, st.carrierSpeed, st.carrierArriveRadius, st.carrierMaxLife,
                        () =>
                        {
                            if (travel != null) travel.Stop();
                            if (gen == g) { Emit(tag, landing, true, rearmChained: true); OnCarrierArrive(idx, landing, mover); }
                        }, st.carrierDrawsArrow && draws);
                    if (draws) travel = DrawFlight(st, mover, from, land, scale);
                    // Carrier tick placement: Start = one tick at launch; Interval = every tickInterval along the
                    // flight (tracks the live mover, capped by carrierMaxLife, closed on arrival); End = one tick on
                    // arrival. Each copy answers for itself — its own launch, its own path, its own landing.
                    if (st.HasTick)
                    {
                        if (st.tickWhen == TickTiming.Start) Emit(st.tickId, from, true);
                        else if (st.tickWhen == TickTiming.Interval)
                            intervals.Add(new IntervalTick { step = i, nextAt = Time.time,
                                                             until = Time.time + st.carrierMaxLife,
                                                             point = from, follow = mover });
                    }
                    if (c == 0) tickFollow[i] = mover;   // the step's representative for the teardown path
                }
                break;
            }

            case VfxStepKind.Shake:
                if (draws) CameraFollow3D.ShakeMain(st.shakeStrength, st.shakeDuration);
                ScheduleEnd(i, Time.time + st.shakeDuration);
                break;

            case VfxStepKind.Sound:
                if (draws && st.sound != null && !st.sound.Empty)
                    Sfx.PlayAt(st.sound, point, st.soundFadeIn, st.soundFadeOut, st.soundDuration);
                ScheduleEnd(i, Time.time);   // one-shot cue: end ≈ start
                break;
        }
    }

    // A carrier's travel effect, riding its mover and aimed down the flight path. Map DOWN → travel dir so a pure
    // vertical drop is unchanged (identity) and only an angled launch tilts. Attach copies position each frame but not
    // rotation, so setting it once on the handle holds for the straight-line flight.
    static Vfx.EffectHandle DrawFlight(VfxStep st, Transform mover, Vector3 from, Vector3 land, float scale)
    {
        if (string.IsNullOrEmpty(st.effectId)) return null;
        var travel = Vfx.Attach(st.effectId, mover, Vector3.zero, scale, tint: st.tint, clipStart: st.clipStart);
        if (st.carrierFaceTravel && travel != null)
        {
            Vector3 dir = land - from;
            if (dir.sqrMagnitude > 1e-6f)
                travel.transform.rotation = Quaternion.FromToRotation(Vector3.down, dir.normalized);
        }
        return travel;
    }

    /// <summary>The absolute moment this step's window closes — the beat an End tick lands on, and the fence an
    /// Interval tick series runs to. Read from the effect table's measured length through the SAME arithmetic the
    /// pool uses when it actually plays one, so drawing and not drawing agree by construction.
    ///
    /// <para><paramref name="fallbackStay"/> is what an unknown or unnamed effect gets — the half-second a step with
    /// no effect behind it has always had, kept exactly, because skills are authored against it today.</para></summary>
    float StepEnds(VfxStep st, float forcedLifetime, bool oneShot, float fallbackStay)
    {
        var def = EffectCatalog.Get(st.effectId);
        if (def != null)
        {
            float window = EffectTiming.PlayEndsInSeconds(def.Timing, def.lifetime, forcedLifetime, oneShot,
                                                          st.speed <= 0f ? 1f : st.speed, st.trimEnd, st.clipStart);
            if (window > EffectTiming.NeverEnds) return Time.time + window;
        }
        return Time.time + (st.looping ? fallbackStay : 0.5f);
    }

    // A random per-axis nudge (magnitude up to 0.2 units) for a step's "scatter" option.
    Vector3 ScatterJitter() =>
        new Vector3(dice.Range(-0.2f, 0.2f), dice.Range(-0.2f, 0.2f), dice.Range(-0.2f, 0.2f));

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

    // This step's projectile volley, supplied by the skill per tickId — the same per-step shape as Stay Duration.
    // No resolver, or a step with no tick to key off, means the one carrier the VFX composed.
    ProjectileFan Fan(VfxStep st) =>
        ctx.fanForTick != null && !string.IsNullOrEmpty(st.tickId) ? ctx.fanForTick(st.tickId) : ProjectileFan.Single;

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
            intervals.Add(new IntervalTick { step = i, nextAt = Time.time, until = endT, point = point, follow = follow });
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

    // ONE copy of a flying Carrier reached its landing: fire an End tick there, or close THAT copy's Interval flight.
    // Only the last copy down ends the step — with a volley in the air, the first arrival must not mark the step
    // finished or the stragglers' ends would be swallowed by teardown.
    void OnCarrierArrive(int i, Vector3 landing, Transform mover)
    {
        var st = seq.steps[i];
        if (st.HasTick)
        {
            if (st.tickWhen == TickTiming.End) Emit(st.tickId, landing, true);
            else if (st.tickWhen == TickTiming.Interval)
                for (int k = intervals.Count - 1; k >= 0; k--)
                    if (intervals[k].step == i && intervals[k].follow == mover) intervals.RemoveAt(k);
        }
        if (tickFollow[i] == mover) tickFollow[i] = null;
        if (--carriersLive[i] <= 0) endFired[i] = true;
    }

    // Relay a raised tag+point: hand it to the skill (VfxTick → damage), then start any OnEvent steps keyed to it.
    // hasPoint carries the emitter's exact world point (e.g. a Carrier's real landing / a tick point) so an OnEvent
    // step opted into snapToEventPoint can anchor there instead of the plain context anchor.
    //
    // A chained step normally runs ONCE per sequence play. rearmChained says this tag came from one copy of a fanned
    // carrier landing, so the steps hanging off it get to run again for THIS copy — the impact belongs to the shot that
    // arrived, not to the volley. (The step's own slots are shared, so a chained step that itself exposes a tick still
    // reports one end for the volley; nothing authored does that today.)
    void Emit(string tag, Vector3 point, bool hasPoint, bool rearmChained = false)
    {
        if (string.IsNullOrEmpty(tag)) return;
        onTick?.Invoke(new VfxTick(tag, point, ctx.targetUnit, ctx.effectPoint, ctx.isRepeat));
        var steps = seq.steps;
        for (int i = 0; i < steps.Count && i < started.Length; i++)   // bound: a tuner may add steps mid-play
        {
            var st = steps[i];
            if (st == null) continue;
            if (st.schedule != VfxSchedule.OnEvent || st.startEvent != tag) continue;
            if (started[i]) { if (!rearmChained) continue; endFired[i] = false; }
            Execute(i, true, point, hasPoint);
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
        {
            var held = attached[i];
            if (held.handle != null && held.handle.Generation == held.generation) held.handle.Stop();
        }
        attached.Clear();
        onTick = null;
        VfxDirector.Return(this);
    }
}
