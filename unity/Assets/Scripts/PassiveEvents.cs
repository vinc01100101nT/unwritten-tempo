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
/// (only the passive keyed to that skill fires); the natural events (<see cref="SkillTrigger.OnBasicAttackStart"/>,
/// <see cref="SkillTrigger.OnBasicAttackDealt"/>,
/// <see cref="SkillTrigger.OnKill"/>, <see cref="SkillTrigger.OnLowHP"/>, <see cref="SkillTrigger.OnDamageDealt"/>,
/// <see cref="SkillTrigger.OnParry"/>) carry no id and, when mob-facing, pass the triggering mob as
/// <paramref name="victim"/>. Fire-sites for the natural events are wired as each consuming passive is built.</para>
/// </summary>
public static class PassiveEvents
{
    // Small (a player's rolled passives — a handful). No per-call alloc; iterated on discrete combat moments, never per-frame.
    // FIXED SLOTS, never compacted: a runner's slot index IS its identity bit in the chain mask, so that bit must not
    // move for as long as it is registered. This used to be a List walked by index, which meant unregistering ANY
    // passive shifted every later one down a slot — and because a chain is stamped now but consumed frames later (a
    // flurry's tail hits, a deferred VFX tick), a skill enable/disable in between silently remapped the bits. Two real
    // failures: the passive that caused the chain stopped recognising itself and re-fired off its own damage (the exact
    // runaway this brake exists to stop), and an unrelated passive inherited the vacated bit and was silently muted.
    static readonly SkillRunner[] slots = new SkillRunner[MaxTrackedPassives];

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

    /// <summary>A passive runner announces itself while it's alive on the player (called from its OnEnable). It keeps
    /// the SAME slot — and so the same chain bit — until it unregisters.</summary>
    public static void Register(SkillRunner r)
    {
        if (r == null) return;
        int free = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == r) return;                       // already registered — keep its existing slot
            if (free < 0 && slots[i] == null) free = i;
        }
        // No free slot: this runner simply isn't tracked (bit 0). It still fires; it just isn't braked — the same
        // degradation the old code had past 64, and it can't corrupt anyone else's bit.
        if (free >= 0) slots[free] = r;
    }

    /// <summary>Drop a passive runner when it's torn down / unequipped (called from its OnDisable). Safe if absent.
    /// Frees the slot WITHOUT shifting anyone else's, so every other passive keeps its identity.</summary>
    public static void Unregister(SkillRunner r)
    {
        if (r == null) return;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] == r) { slots[i] = null; return; }
    }

    /// <summary>Fire a passive trigger. For <see cref="SkillTrigger.OnSkillCastComplete"/>, <paramref name="skillId"/>
    /// is the completed active's id and only the passive keyed to it runs; the natural events ignore it.
    /// <paramref name="victim"/> is the triggering mob for mob-facing moments (null for self-only moments like a cast
    /// completing) — a passive's Single/Ground bindings land on it.</summary>
    public static void Fire(SkillTrigger trigger, string skillId = null, Transform victim = null)
    {
        // Walk the fixed slots. Nothing is compacted, so a passive that tears down mid-dispatch just leaves a hole and
        // every other slot — and therefore every other chain bit — stays exactly where it was.
        for (int i = 0; i < slots.Length; i++)
        {
            var r = slots[i];
            if (r == null) continue;
            if (!r) { slots[i] = null; continue; }   // destroyed runner: clear its slot, don't shift the rest
            var def = r.def;
            if (def == null) continue;
            // WHICH authored entry answers this moment (null = this skill doesn't). A skill may carry several triggers,
            // so the moment picks the entry, and that entry — not the skill — owns the gate and the stack cost below.
            // OnSkillCastComplete is parameterized inside EntryFor: only an entry bound to THIS skill id matches.
            var entry = def.EntryFor(trigger, skillId);
            if (entry == null) continue;
            // THE OWNERSHIP GATE: a runner exists for every authored skill, but a passive only RUNS when the player
            // ticked it on in the Grimoire or the equipped weapon rolled it. One rule, read from its one owner.
            // A skill with an OnCast trigger passes freely here (an active is gated by the hotbar) — including one
            // whose EXTRA trigger fired, since in practice that trigger is gated on a partner skill's stacks anyway.
            if (Character.Instance != null && !Character.Instance.PassiveSkills.IsActive(def)) continue;

            // THE BRAKE: skip a passive already in this causal chain (it, or something it caused, led here). Unrelated
            // passives are untouched — "passive A kills, passive B reacts" stays legal.
            ulong bit = 1UL << i;   // stable for as long as this runner holds slot i
            if ((chain & bit) != 0UL) continue;

            // THE GATE: a trigger that requires a state doesn't run at all when unmet — no VFX, no cast.
            if (!StateGate.IsMet(entry, r.CasterStates)) continue;

            r.FireAsPassive(entry, victim, chain | bit);   // fire the whole skill; the chain it hands to its own effects includes itself
        }
    }
}
