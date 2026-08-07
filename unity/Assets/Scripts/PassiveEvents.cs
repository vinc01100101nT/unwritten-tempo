using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONE dispatch choke point for passive skills — the single handler every trigger routes through, so a new
/// trigger's rule is applied in exactly one place and no fire-site can forget it (centralized cross-cutting concern).
///
/// <para>Every <see cref="SkillRunner"/> registers here while it lives on the player (so a registered runner ⇔ an
/// owned skill — no separate owned-set to hand-sync); it registers unconditionally because <c>def</c> may be set
/// after OnEnable. A combat moment fires ONE call (<see cref="Fire"/>), which dispatches only to the passives whose
/// <see cref="SkillDef.trigger"/> matches — a registered active (trigger = <see cref="SkillTrigger.OnCast"/>) is never
/// matched, since <see cref="Fire"/> is only ever called with a passive trigger. Each matched passive then FIRES AS A
/// FULL SKILL (<see cref="SkillRunner.FireAsPassive"/>): same VFX + ticks + effects an active would play, with the aim
/// auto-resolved from the trigger (the victim's spot for aimed skills; the caster for self skills).</para>
///
/// <para>Trigger shapes: <see cref="SkillTrigger.OnSkillCastComplete"/> is parameterized by the completed skill's id
/// (only the passive keyed to that skill fires); the natural events (<see cref="SkillTrigger.OnBasicAttackLanded"/>,
/// <see cref="SkillTrigger.OnKill"/>, <see cref="SkillTrigger.OnLowHP"/>, <see cref="SkillTrigger.OnHit"/>,
/// <see cref="SkillTrigger.OnParry"/>) carry no id and, when mob-facing, pass the triggering mob as
/// <paramref name="victim"/>. Fire-sites for the natural events are wired as each consuming passive is built.</para>
/// </summary>
public static class PassiveEvents
{
    // Small (a player's rolled passives — a handful). No per-call alloc; iterated on discrete combat moments, never per-frame.
    static readonly List<SkillRunner> passives = new List<SkillRunner>(16);

    // ---- NO-SELF-FEEDING BRAKE -----------------------------------------------------------------------------------
    // A passive never fires off a chain it is ALREADY part of. Every effect a passive causes carries the set of passives
    // that led to it (`chain`, one bit per registered passive); a passive whose bit is already set is skipped, while any
    // OTHER passive still runs normally. Because no passive can repeat within one chain, every chain is finite BY
    // CONSTRUCTION (at most one link per owned passive) — no depth counter to tune, and no runaway is possible.
    //
    // This REPLACES the old `dispatching` bool, which only caught SYNCHRONOUS re-entry and missed the real hazard: a
    // passive's damage lands frames later (deferred VFX ticks), long after that flag had cleared.
    //
    // Zero-alloc: the chain is one ulong passed by value, and the "who am I" bit is an index assigned at registration.
    const int MaxTrackedPassives = 64;   // one bit each in the chain mask
    static ulong chain;                  // the passives already in the currently-resolving causal chain

    /// <summary>The chain currently in force — a passive's runner stamps this onto the effects it causes.</summary>
    public static ulong CurrentChain => chain;

    /// <summary>Make <paramref name="mask"/> the active chain while a passive's effects resolve, returning the previous
    /// one to restore. Paired with <see cref="PopChain"/> around the moment effects are applied.</summary>
    public static ulong PushChain(ulong mask) { var prev = chain; chain = mask; return prev; }
    public static void PopChain(ulong prev) { chain = prev; }

    /// <summary>A passive runner announces itself while it's alive on the player (called from its OnEnable).</summary>
    public static void Register(SkillRunner r) { if (r != null && !passives.Contains(r)) passives.Add(r); }

    /// <summary>Drop a passive runner when it's torn down / unequipped (called from its OnDisable). Safe if absent.</summary>
    public static void Unregister(SkillRunner r) { passives.Remove(r); }

    /// <summary>Fire a passive trigger. For <see cref="SkillTrigger.OnSkillCastComplete"/>, <paramref name="skillId"/>
    /// is the completed active's id and only the passive keyed to it runs; the natural events ignore it.
    /// <paramref name="victim"/> is the triggering mob for mob-facing moments (null for self-only moments like a cast
    /// completing) — a passive's Single/Ground bindings land on it.</summary>
    public static void Fire(SkillTrigger trigger, string skillId = null, Transform victim = null)
    {
        // Reverse walk so a passive that tears down mid-dispatch (rare) can't cause a neighbour to be skipped, and so
        // a stale (destroyed) entry can be compacted out in the same pass.
        for (int i = passives.Count - 1; i >= 0; i--)
        {
            var r = passives[i];
            if (r == null) { passives.RemoveAt(i); continue; }
            var def = r.def;
            if (def == null || def.trigger != trigger) continue;
            // OnSkillCastComplete is parameterized: only the passive bound to THIS skill fires (empty id never matches a real cast).
            if (trigger == SkillTrigger.OnSkillCastComplete && def.triggerSkillId != skillId) continue;

            // THE BRAKE: skip a passive already in this causal chain (it, or something it caused, led here). Unrelated
            // passives are untouched — "passive A kills, passive B reacts" stays legal.
            ulong bit = i < MaxTrackedPassives ? 1UL << i : 0UL;
            if (bit != 0UL && (chain & bit) != 0UL) continue;

            // THE GATE: a skill that requires a state doesn't run at all when unmet — no VFX, no cast.
            if (!StateGate.IsMet(def, r.CasterStates)) continue;

            r.FireAsPassive(victim, chain | bit);   // fire the whole skill; the chain it hands to its own effects includes itself
        }
    }
}
