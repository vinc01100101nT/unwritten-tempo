using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a <see cref="VfxSequence"/> — the runtime that turns the data recipe into timed calls on the existing
/// building blocks (<see cref="Vfx"/>, <see cref="Projectile3D"/>, <see cref="CameraFollow3D.ShakeMain"/>,
/// <see cref="Sfx"/>). It is the ONLY thing that "runs" a sequence; the asset itself is inert data.
///
/// <para>The event relay is the crux of the code/data split: a step can RAISE a named tag (a carrier on arrival,
/// or any step on start). When one fires, the director (1) invokes the skill's <c>onEvent(tag)</c> callback —
/// where the skill runs damage — and (2) starts any <see cref="VfxSchedule.OnEvent"/> steps keyed to that tag.
/// The asset never touches gameplay; it only labels beats.</para>
///
/// PERFORMANCE: runners are POOLED (like <see cref="Vfx"/> / <see cref="DamagePopup"/>) and driven by their own
/// Update — no per-play coroutine or <c>WaitForSeconds</c>. Sequences are cooldown-gated (cast-frequency, not
/// per-frame/per-hit), so the only remaining allocation is a tiny per-carrier arrival closure, which is off the
/// steady-combat hot path.
/// </summary>
public static class VfxDirector
{
    /// <summary>Play <paramref name="seq"/> at <paramref name="ctx"/>. <paramref name="onEvent"/> (optional) is
    /// the skill's hook: it fires with each raised event tag (e.g. "impact") so the skill can land damage on the
    /// exact beat. Returns the runner handle (rarely needed; for an attached/looping sequence call
    /// <see cref="VfxSequenceRunner.Stop"/>). No-op / null if the sequence is empty.</summary>
    public static VfxSequenceRunner Play(VfxSequence seq, VfxContext ctx, System.Action<string> onEvent = null)
    {
        if (seq == null || seq.steps == null || seq.steps.Count == 0) return null;
        var r = Rent();
        r.Begin(seq, ctx, onEvent);
        return r;
    }

    // ---- pool ---------------------------------------------------------------
    static Transform host;
    static readonly Stack<VfxSequenceRunner> pool = new Stack<VfxSequenceRunner>();

    static void EnsureHost()
    {
        if (host != null) return;
        pool.Clear();   // a scene unload destroyed the old host + its pooled children; drop stale refs
        host = new GameObject("VfxDirector").transform;
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
    System.Action<string> onEvent;
    bool[] started;                 // per-step, reused across plays (grown as needed)
    readonly List<Vfx.EffectHandle> attached = new List<Vfx.EffectHandle>();
    float startTime, endBy;
    int gen;                        // bumped each Begin/Finish → stale carrier callbacks are ignored
    bool active;

    internal void Begin(VfxSequence s, VfxContext c, System.Action<string> cb)
    {
        seq = s; ctx = c; onEvent = cb;
        gen++;
        startTime = Time.time;
        active = true;
        attached.Clear();

        int n = seq.steps.Count;
        if (started == null || started.Length < n) started = new bool[n];
        for (int i = 0; i < n; i++) started[i] = false;

        // Upper bound on when the last step can possibly fire: the latest timed delay, plus a carrier's full
        // flight time (an OnEvent step may be chained to an arrival), plus a pad. Then Finish tears down.
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

        var steps = seq.steps;
        for (int i = 0; i < steps.Count && i < started.Length; i++)   // bound: a tuner may add steps mid-play
        {
            var st = steps[i];
            if (st == null || started[i]) continue;
            if (st.schedule == VfxSchedule.AtTime && t >= st.delay) Execute(i);
        }

        if (t >= (endBy - startTime)) Finish();
    }

    // Fire a step's action. Carriers raise their event on ARRIVAL (async); everything else raises on start.
    void Execute(int i)
    {
        started[i] = true;
        var st = seq.steps[i];
        Vector3 point = (st.anchor == VfxAnchor.Origin ? ctx.origin : ctx.target) + st.offset;
        float scale = st.scale * (ctx.scaleMult <= 0f ? 1f : ctx.scaleMult);

        switch (st.kind)
        {
            case VfxStepKind.PlayAt:
                Vfx.PlayAt(st.effectId, point, Quaternion.Euler(st.rotationEuler),
                           new Vfx.VfxParams { scale = scale, speed = st.speed, tint = st.tint });
                Emit(st.emitEvent);
                break;

            case VfxStepKind.Attach:
                if (ctx.follow != null)
                {
                    var h = Vfx.Attach(st.effectId, ctx.follow, st.offset, scale);
                    if (h != null) attached.Add(h);
                }
                Emit(st.emitEvent);
                break;

            case VfxStepKind.Carrier:
                Vector3 from = point + st.carrierFrom;
                if (st.carrierScatter > 0f)
                    from += new Vector3(Random.Range(-st.carrierScatter, st.carrierScatter), 0f,
                                        Random.Range(-st.carrierScatter, st.carrierScatter));
                int g = gen; string tag = st.emitEvent;
                Projectile3D.SpawnVfx(st.effectId, null, scale, from, null, point,
                    st.carrierSpeed, st.carrierArriveRadius, st.carrierMaxLife,
                    () => { if (gen == g) Emit(tag); });   // arrival → raise the event on THIS play only
                break;

            case VfxStepKind.Shake:
                CameraFollow3D.ShakeMain(st.shakeStrength, st.shakeDuration);
                Emit(st.emitEvent);
                break;

            case VfxStepKind.Sound:
                if (st.sound != null && !st.sound.Empty) Sfx.PlayAt(st.sound, point);
                Emit(st.emitEvent);
                break;
        }
    }

    // Relay a raised event: hand it to the skill (damage), then start any OnEvent steps keyed to it.
    void Emit(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        onEvent?.Invoke(tag);
        var steps = seq.steps;
        for (int i = 0; i < steps.Count && i < started.Length; i++)   // bound: a tuner may add steps mid-play
        {
            var st = steps[i];
            if (st == null || started[i]) continue;
            if (st.schedule == VfxSchedule.OnEvent && st.startEvent == tag) Execute(i);
        }
    }

    void Finish()
    {
        if (!active) return;
        active = false;
        gen++;   // fence any carrier still in flight from this play
        for (int i = 0; i < attached.Count; i++)
            if (attached[i] != null) attached[i].Stop();
        attached.Clear();
        onEvent = null;
        VfxDirector.Return(this);
    }
}
