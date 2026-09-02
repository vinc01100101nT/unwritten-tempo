using UnityEngine;

/// <summary>
/// The ONE dispatch choke point for passive skills — the single handler every trigger routes through, so a new
/// trigger's rule is applied in exactly one place and no fire-site can forget it (centralized cross-cutting concern).
///
/// <para><b>Every moment names WHOSE it is.</b> A kill, a parry, a landed swing — each belongs to one character, and
/// the passives that answer it are that character's. This used to be implicit: there was one player at one keyboard,
/// so "the player" was a global. A server holds one character per person signed in, where that assumption is not
/// merely inconvenient but wrong in both directions — it would let one person's kill fire another person's passive,
/// and it silently switched the ownership check OFF entirely, because the check asked after a local player that a
/// server does not have.</para>
///
/// <para>So a moment is fired AT a unit, and the only passives considered are the ones on that unit's own skills
/// (<see cref="ICombatUnit.SkillRunners"/>). Each matched passive then FIRES AS A FULL SKILL
/// (<see cref="SkillRunner.FireAsPassive"/>): same VFX + ticks + effects an active would play, with the aim
/// auto-resolved from the trigger (the victim's spot for aimed skills; the caster for self skills).</para>
///
/// <para>Trigger shapes: <see cref="SkillTrigger.OnSkillCastComplete"/> is parameterized by the completed skill's id
/// (only the passive keyed to that skill fires); the natural events carry no id and, when mob-facing, pass the
/// triggering unit as <paramref name="victim"/>.</para>
/// </summary>
public static class PassiveEvents
{
    // ---- NO-SELF-FEEDING BRAKE -----------------------------------------------------------------------------------
    // A passive never fires off a chain it is ALREADY part of. Every effect a passive causes carries the set of
    // passives that led to it (`chain`, one bit per skill the caster owns); a passive whose bit is already set is
    // skipped, while any OTHER passive still runs normally. Because no passive can repeat within one chain, every
    // chain is finite BY CONSTRUCTION — no depth counter to tune, and no runaway is possible.
    //
    // A passive's BIT is its position in its own caster's skill list, which is fixed for as long as that list is.
    // It used to be a slot in one process-wide table, which a server would have run out of after two people signed
    // in — and an untracked passive is an unbraked one.
    const int MaxTrackedPassives = 64;

    // What is causing the effects landing right now. Per MACHINE, not per character, and correctly so: a resolve is
    // synchronous, so only one cast is ever mid-flight here, and these say what it is.
    static ulong chain;
    static string causeSkillId = "";

    /// <summary>The chain currently in force — a passive's runner stamps this onto the effects it causes.</summary>
    public static ulong CurrentChain => chain;

    /// <summary>WHICH SKILL is landing the effects being applied right now, or "" when nothing skill-shaped is (a basic
    /// attack, a damage-over-time tick, the engine). Read by a moment that happens INSIDE a cast and wants to name its
    /// cause — a kill is the first: "a mob died" and "Blade Dance killed it" are different moments, and only the second
    /// can drive a passive that answers one particular skill.</summary>
    public static string CurrentSkillId => causeSkillId;

    /// <summary>What was in force before a push — restore it in a finally.</summary>
    public readonly struct Cause
    {
        public readonly ulong chain; public readonly string skillId;
        public Cause(ulong chain, string skillId) { this.chain = chain; this.skillId = skillId; }
    }

    /// <summary>Make <paramref name="mask"/> and <paramref name="skillId"/> the cause in force while a cast's effects
    /// resolve, returning the previous pair to restore. Paired with <see cref="Pop"/> around the moment effects are
    /// applied.</summary>
    public static Cause Push(ulong mask, string skillId)
    {
        var prev = new Cause(chain, causeSkillId);
        chain = mask; causeSkillId = skillId ?? "";
        return prev;
    }

    public static void Pop(Cause prev) { chain = prev.chain; causeSkillId = prev.skillId; }

    /// <summary>Fire a passive trigger AT <paramref name="actor"/> — the unit the moment happened to. Only that
    /// unit's own passives are considered. For <see cref="SkillTrigger.OnSkillCastComplete"/>,
    /// <paramref name="skillId"/> is the completed active's id and only the passive keyed to it runs; the natural
    /// events ignore it. <paramref name="victim"/> is the other party for outward-facing moments (null for
    /// self-only moments like a cast completing) — a passive's Single/Ground bindings land on it.</summary>
    public static void Fire(ICombatUnit actor, SkillTrigger trigger, string skillId = null, Transform victim = null)
    {
        if (PlayerUnits.Gone(actor)) return;
        var runners = actor.SkillRunners;
        if (runners == null) return;

        var owner = actor as Character;
        int n = runners.Length < MaxTrackedPassives ? runners.Length : MaxTrackedPassives;
        for (int i = 0; i < n; i++)
        {
            var r = runners[i];
            if (r == null || !r) continue;
            var def = r.def;
            if (def == null) continue;
            // WHICH authored entry answers this moment (null = this skill doesn't). A skill may carry several triggers,
            // so the moment picks the entry, and that entry — not the skill — owns the gate and the stack cost below.
            // OnSkillCastComplete is parameterized inside EntryFor: only an entry bound to THIS skill id matches.
            var entry = def.EntryFor(trigger, skillId);
            if (entry == null) continue;
            // THE OWNERSHIP GATE: a runner exists for every authored skill, but a passive only RUNS when this
            // character ticked it on in the Grimoire or their equipped weapon rolled it. Read off THEIR loadout, not
            // off whoever happens to be at a keyboard.
            if (owner != null && !owner.PassiveSkills.IsActive(def)) continue;

            // THE BRAKE: skip a passive already in this causal chain (it, or something it caused, led here). Unrelated
            // passives are untouched — "passive A kills, passive B reacts" stays legal.
            ulong bit = 1UL << i;
            if ((chain & bit) != 0UL) continue;

            // THE GATE: a trigger that requires a state doesn't run at all when unmet — no VFX, no cast.
            if (!StateGate.IsMet(entry, r.CasterStates)) continue;

            r.FireAsPassive(entry, victim, chain | bit);   // fire the whole skill; the chain it hands to its own effects includes itself
        }
    }
}
