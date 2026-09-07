using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONE choke point for playing a visual in response to a gameplay EVENT (a "verb" or a status beat) — the
/// status/verb analog of what <see cref="SkillRunner"/> is for skills. Gameplay code that heals, hits, dies, or
/// applies an ailment calls <see cref="Fire"/> (a one-shot puff) or <see cref="Begin"/> (a persistent aura it
/// holds and later stops); it never touches <see cref="VfxDirector"/> or <see cref="Vfx"/> directly. That keeps
/// the event→look wiring in a single table (<see cref="VfxCueLibrary"/>, edited in the Vfx Cue Composer) instead
/// of scattered spawn calls across the combat/status code — so re-skinning "what a heal looks like" is a
/// one-asset edit, and a new global rule (mute cues while X) lands in one place.
///
/// Unlike a skill, a cue carries NO gameplay: the event already happened, so <see cref="VfxDirector.Play"/> is
/// called with a null onTick. An unbound cue is a silent no-op (never throws).
///
/// <para><b>WHO GETS TO SAY A CUE HAPPENED — one rule, no exceptions.</b> A cue is a picture of an event, and it
/// belongs to whichever machine DECIDED that event. In a shared world that is always the server: it owns every
/// creature and holds every person's character, so it is the one that knows a blow landed, a poison started, a
/// heal went in or somebody died. It therefore ANNOUNCES every cue it fires to each machine watching the unit it
/// played on, and draws nothing itself (it has no screen).
///
/// <para>A machine that has JOINED somebody else's world does the opposite: it fires nothing of its own. Every one
/// of its units is a copy of one the server owns, so a cue worked out here would be a second opinion about an
/// event this machine never decided — which is exactly how the same puff used to play twice, or play on one screen
/// and no other. It draws only what it is told (see <see cref="PlayFromServer"/>), including cues on its own body.
/// That is the same bargain the game already makes for your own spell effects, which also wait for the server's
/// word rather than being drawn the instant you press.</para>
///
/// <para>A single machine with no world attached — nobody joined, no server — decides everything itself, so it
/// both fires and draws exactly as it always has. Nothing about that case changes.</para>
///
/// PERFORMANCE: called per-hit / per-DoT-tick, not per-frame. Alloc-free — the binding lookup is an array index
/// (<see cref="VfxCueCatalog"/>), the context is a struct built on the stack, and the null onTick means no
/// capturing closure. The underlying <see cref="VfxDirector"/> / <see cref="Vfx"/> pools are reused.
/// </summary>
public static class VfxCue
{
    /// <summary>Play a cue's one-shot look. <paramref name="actor"/> is the sequence's Origin; the optional
    /// <paramref name="target"/> is its Target (falls back to the actor when null). WHERE each piece spawns is the
    /// sequence's own per-step "Place at" choice — the cue only supplies the two points. Fire-and-forget: nothing to
    /// hold. No-op if the cue is unbound or actor is null.</summary>
    public static void Fire(VfxCueId id, Transform actor, Transform target = null)
    {
        if (actor == null) return;
        // NOT MINE TO SAY — see the rule on the class. The server's own word for this event is already on its way.
        if (GameNetwork.TheWorldIsElsewhere) return;

        var b = VfxCueCatalog.Get(id);
        if (b == null || b.vfx == null) return;

        // …AND EVERY MACHINE WATCHING THIS UNIT IS TOLD. Silent off a server, so a single machine is untouched.
        GameNetwork.AnnounceCue(actor, target, id, 0);

        if (NobodyToShowItTo) return;
        Draw(b, actor, target);
    }

    /// <summary>TRUE where there is nobody to watch an effect — a headless server. It decides the whole of combat
    /// now, so every blow it resolves would otherwise build and tick a visual effect for an empty room. Asked at
    /// this door because every cue in the game passes through it.</summary>
    static bool NobodyToShowItTo => GameServer.IsServer;

    /// <summary>Begin a PERSISTENT cue that TRACKS <paramref name="actor"/> (a status aura) and return the runner
    /// handle — the caller MUST call <see cref="VfxSequenceRunner.Stop"/> on it when the state ends (e.g. the
    /// ailment expires). Author the cue's sequence as an Attach/looping step so it lives until stopped. Null if the
    /// cue is unbound or actor is null.
    ///
    /// <para>On a server the runner is still built even though nothing is drawn, and that is the point: it is what
    /// knows the aura is still running, and stopping it is what tells every watching machine to take the aura off
    /// the body it is riding. Without it an ailment's glow would start on every screen and never end.</para></summary>
    public static VfxSequenceRunner Begin(VfxCueId id, Transform actor)
    {
        if (actor == null) return null;
        if (GameNetwork.TheWorldIsElsewhere) return null;

        var b = VfxCueCatalog.Get(id);
        if (b == null || b.vfx == null) return null;

        var ctx = BuildCtx(b, actor, actor);
        ctx.follow = actor;      // the aura's Attach step tracks the unit
        ctx.persistent = true;   // caller holds it alive and Stops it when the state ends
        var runner = VfxDirector.Play(b.vfx, ctx, null);

        // NAMED, so the ending can be said as plainly as the beginning. A one-shot needs no name — it is over by
        // the time the message lands — but an aura has to be taken OFF a body later, and a watching machine can
        // only do that if both sides call it the same thing.
        int key = NextAuraKey();
        uint on = GameNetwork.AnnounceCue(actor, actor, id, key);
        // AND THE RUNNER CARRIES ITS OWN ENDING. Whoever holds this aura stops the runner and knows nothing about
        // any of this, so the runner is what says so — one door for "this aura is over", however it came to be over.
        if (on != 0u && runner != null) runner.AnnounceStopAs(on, key);
        return runner;
    }

    // Aura names, handed out by the machine that owns the world. Starts at 1 because 0 means "a one-shot, there is
    // nothing to stop". Wraps back to 1 rather than to 0 — a session would have to start four billion auras.
    static int nextAuraKey = 1;

    static int NextAuraKey()
    {
        if (nextAuraKey == int.MaxValue) nextAuraKey = 1;
        return nextAuraKey++;
    }

    // ---- the watching side: draw what the server said happened -------------------------------------------

    // Auras running on THIS machine that the server named, so the "it ended" that arrives later can find the right
    // one. Keyed by the unit AND the aura's name together: two people can be poisoned at once, and each one's glow
    // has to come off its own body. Event-driven (an ailment starting or ending), never per frame.
    static readonly Dictionary<long, ToldAura> toldAuras = new Dictionary<long, ToldAura>(16);

    /// <summary>An aura running here because the world said so, and WHICH PLAY of that runner it is. The runner
    /// comes out of a pool: if it finishes on its own before the "it is over" arrives, it is handed straight to the
    /// next effect that needs one, and stopping it then would tear down something else entirely. The number says
    /// whether the runner is still on the play we started.</summary>
    struct ToldAura { public VfxSequenceRunner runner; public int generation; }

    static long AuraSlot(uint onNetId, int key) => ((long)onNetId << 32) | (uint)key;

    /// <summary>Play a cue the SERVER said happened, on the body THIS machine draws for that unit. The only door a
    /// joined client draws a cue through — it never decides one for itself, so nothing here announces anything and
    /// there is no way for a cue to echo back and forth.</summary>
    public static void PlayFromServer(VfxCueId id, Transform actor, Transform target, int auraKey, uint onNetId)
    {
        if (actor == null) return;
        var b = VfxCueCatalog.Get(id);
        if (b == null || b.vfx == null) return;

        if (auraKey == 0) { Draw(b, actor, target); return; }

        // A LASTING ONE. A refreshed ailment gets a NEW name and the world stops the old one first, in that order,
        // so there is nothing to clear away here — the ending of the previous glow has already arrived.
        var ctx = BuildCtx(b, actor, actor);
        ctx.follow = actor;
        ctx.persistent = true;
        var runner = VfxDirector.Play(b.vfx, ctx, null);
        if (runner != null)
            toldAuras[AuraSlot(onNetId, auraKey)] = new ToldAura { runner = runner, generation = runner.Generation };
    }

    /// <summary>Take off an aura the server has said is over.</summary>
    public static void StopFromServer(uint onNetId, int auraKey)
    {
        long slot = AuraSlot(onNetId, auraKey);
        if (!toldAuras.TryGetValue(slot, out var held)) return;
        toldAuras.Remove(slot);
        StopIfStillOurs(held);
    }

    // Stop a runner only while it is still on the play we started it for — see ToldAura.
    static void StopIfStillOurs(ToldAura held)
    {
        if (held.runner != null && held.runner.Generation == held.generation) held.runner.Stop();
    }

    /// <summary>Every aura this machine was told about, taken off at once — for a body that has gone away, or a
    /// world this machine has left. An aura is held by name rather than by the body it rides, so without this a
    /// person who walked out of view mid-poison would leave their glow behind them forever.</summary>
    public static void ForgetAurasOn(uint onNetId)
    {
        if (toldAuras.Count == 0) return;
        forgetting.Clear();
        foreach (var pair in toldAuras)
            if ((uint)(pair.Key >> 32) == onNetId) forgetting.Add(pair.Key);
        for (int i = 0; i < forgetting.Count; i++)
        {
            if (toldAuras.TryGetValue(forgetting[i], out var held)) StopIfStillOurs(held);
            toldAuras.Remove(forgetting[i]);
        }
    }

    // Reused so taking a body's auras off allocates nothing — it happens whenever anybody walks out of view.
    static readonly List<long> forgetting = new List<long>(8);

    // ---- drawing ------------------------------------------------------------------------------------------

    static void Draw(VfxCueBinding b, Transform actor, Transform target)
    {
        var ctx = BuildCtx(b, actor, target);
        ctx.follow = actor;   // let one-shot cues use Attach steps too (they auto-stop when the sequence ends)
        VfxDirector.Play(b.vfx, ctx, null);
    }

    // Feed the two reference points; the sequence's own steps decide which one each piece spawns at. The Composer
    // authors from the ground, so we raycast the floor under each point and anchor there (matches the ruler standing
    // on the floor). Robust to the unit's pivot — units are feet-pivoted now, but this still finds true ground either
    // way. Struct return → no allocation.
    static VfxContext BuildCtx(VfxCueBinding b, Transform actor, Transform target)
    {
        Vector3 a = actor.position; a.y = GroundY(a);
        Vector3 t = target != null ? target.position : actor.position; t.y = GroundY(t);
        return new VfxContext
        {
            origin = a + b.offset,
            target = t + b.offset,
            effectPoint = t + b.offset,   // a cue has no aim stage; the target spot is the only point it knows
            follow = null,
            targetUnit = target,
            scaleMult = b.scale <= 0f ? 1f : b.scale,
            groundY = a.y,
        };
    }

    static int groundMask = -1;   // cached Ground-layer mask (-1 unresolved, 0 = no Ground layer)

    // World Y of the floor under a point. Single non-alloc raycast (cues are event-driven, not per-frame). Falls
    // back to the point's own Y if there's no Ground layer / no hit, so an off-floor actor isn't yanked down.
    static float GroundY(Vector3 p)
    {
        if (groundMask == -1) { int l = LayerMask.NameToLayer("Ground"); groundMask = l >= 0 ? 1 << l : 0; }
        if (groundMask != 0 && Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 12f, groundMask))
            return hit.point.y;
        return p.y;
    }
}
