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
        var b = VfxCueCatalog.Get(id);
        if (b == null || b.vfx == null) return;
        VfxDirector.Play(b.vfx, BuildCtx(b, actor, target), null);
    }

    /// <summary>Begin a PERSISTENT cue that TRACKS <paramref name="actor"/> (a status aura) and return the runner
    /// handle — the caller MUST call <see cref="VfxSequenceRunner.Stop"/> on it when the state ends (e.g. the
    /// ailment expires). Author the cue's sequence as an Attach/looping step so it lives until stopped. Null if the
    /// cue is unbound or actor is null.</summary>
    public static VfxSequenceRunner Begin(VfxCueId id, Transform actor)
    {
        if (actor == null) return null;
        var b = VfxCueCatalog.Get(id);
        if (b == null || b.vfx == null) return null;
        var ctx = BuildCtx(b, actor, actor);
        ctx.follow = actor;   // the aura's Attach step tracks the unit
        return VfxDirector.Play(b.vfx, ctx, null);
    }

    // Feed the two reference points; the sequence's own steps decide which one each piece spawns at.
    // Struct return → no allocation.
    static VfxContext BuildCtx(VfxCueBinding b, Transform actor, Transform target)
    {
        Vector3 a = actor.position;
        Vector3 t = target != null ? target.position : a;
        return new VfxContext
        {
            origin = a + b.offset,
            target = t + b.offset,
            follow = null,
            targetUnit = target,
            scaleMult = b.scale <= 0f ? 1f : b.scale,
        };
    }
}
