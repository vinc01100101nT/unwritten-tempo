using UnityEngine;

/// <summary>
/// The ONE place a skill's "requires a state" condition is checked and spent — the combo gate. Both entry points route
/// through here (a pressed active in <see cref="SkillRunner"/>, a passive in <see cref="PassiveEvents"/>), so the rule
/// lives in exactly one place and no path can forget it.
///
/// <para><b>Compute BEFORE consume — the locked rule.</b> Every computation reads the LIVE store; a snapshot would be a
/// second copy of the value, which is exactly what single-source-of-truth forbids. So the order is always:
/// <see cref="IsMet"/> → the skill's effects run (reading the store) → <see cref="Consume"/>. That's why Deathblow can
/// see all of a Momentum pile while computing its damage, and spends it the instant that computation finishes.</para>
///
/// <para><b>Spent on FIRE, never refunded — the locked rule for every charged state.</b> A charge is paid the moment
/// the use it gated actually happens, and nothing that comes after can give it back: whatever the skill went on to do
/// — a swing that got cancelled, a dash that hit a wall, a target that died mid-cast — the charge is gone. A refund or
/// a deferred "pay only if it worked" path would make cancelling FREE, so cancel-spam becomes a way to re-trigger a
/// charged effect for nothing, and the store would need to remember owed charges (a second copy of the count). This is
/// why <see cref="Consume"/> has no inverse: don't add one. Same rule outside this gate — Parry's charges are spent by
/// the hit they erase (<see cref="UnitMitigation"/>), at the moment of use, with no refund.</para>
/// </summary>
public static class StateGate
{
    /// <summary>The granter id that means "nobody granted this — the fight itself counts it": the unit's unbroken run
    /// of hits on one body (<see cref="UnitStates.ConsecutiveHits"/>). It is stored where a granting skill's id is
    /// stored, and deliberately starts with a character no skill id can contain, so it can never collide with a real
    /// skill and an UNAUTHORED gate (empty id) stays distinguishable from this one.
    ///
    /// <para>There is no "which pile" half for it: nothing grants it, there is only ever one of it, and it is never
    /// spent — a run is ended by the fight (a switched target, a hit that dealt nothing), never by a skill cashing
    /// it.</para></summary>
    public const string RunOfHitsId = "@run-of-hits";

    /// <summary>True when this gate reads the automatic run of hits rather than some skill's pile.</summary>
    public static bool ReadsRunOfHits(SkillTriggerEntry entry) =>
        entry != null && entry.requiresState && entry.requireSkillId == RunOfHitsId;

    /// <summary>The only moments where the run of hits is a LIVE number: a hit of yours just landed, and the count
    /// already includes it (the damage door counts before the sink that fires these).
    ///
    /// <para>Every other moment reads something that is not the run: a press or a finished cast reads a standing
    /// number nothing just changed; the start of a swing reads one hit behind, on a swing that may go on to miss; a
    /// KILL reads a run against a body that is now dead; and taking damage, parrying or dropping to low HP are not
    /// about your attacking at all. Offering it there would be a knob that reads a real number meaning nothing.</para></summary>
    public static bool MomentHasLiveRunOfHits(SkillTrigger trigger) =>
        trigger == SkillTrigger.OnBasicAttackDealt || trigger == SkillTrigger.OnDamageDealt;

    /// <summary>True when <paramref name="def"/> may run right now. A skill with no requirement always passes.
    /// Gates the WHOLE skill — callers must bail before playing any VFX or paying any cost.
    ///
    /// <para><b>Requiring and spending are SEPARATE questions.</b> A minimum of 0 means "no gate": the skill fires
    /// with an empty pile and simply cashes whatever is there. That's Deathblow — a Sword that never rolled Momentum
    /// still swings it, just without the payoff — and it's why this can't fold back into the on/off switch.</para></summary>
    public static bool IsMet(SkillTriggerEntry entry, UnitStates states)
    {
        if (entry == null || !entry.requiresState) return true;
        ReportIfUnauthored(entry);
        if (entry.requireMinStacks <= 0) return true;            // reads the pile, but doesn't demand it
        if (states == null) return false;
        return StackCount(entry, states) >= entry.requireMinStacks;
    }

    // Gates already reported, so a skill pressed every few seconds says it once and not once per press. Keyed by the
    // granter id, which is what the author has to go fix.
    static readonly System.Collections.Generic.HashSet<string> reported = new System.Collections.Generic.HashSet<string>();

    /// <summary>SAY SO when a gate reads a pile nobody ever named. A gate whose pile was never authored reads the
    /// first state in the enum, which no author ever means, so it silently counts nothing and spends nothing — the
    /// exact failure that survived a day of play because it looked like a skill that simply "wasn't scaling yet."
    /// Cost is one set lookup on a path that already walks a state list; nothing is logged twice.</summary>
    static void ReportIfUnauthored(SkillTriggerEntry entry)
    {
        if (ReadsRunOfHits(entry)) return;   // it names no pile because there is none to name — that is the whole point
        if (entry.requireStateIdSet || !reported.Add(entry.requireSkillId ?? "")) return;
        Debug.LogError($"[StateGate] A skill reads \"{entry.requireSkillId}\"'s stacks but never names WHICH pile, so " +
                       "it counts nothing and spends nothing. Open it in the Skill Composer, pick the pile, and save.");
    }

    /// <summary>How many stacks of the required SKILL's pile the caster holds RIGHT NOW — the snapshot a cast takes
    /// before it spends anything, so its payoff and its cost read the same pile (the locked compute-before-consume
    /// order). 0 when the skill reads no pile.</summary>
    public static int StackCount(SkillTriggerEntry entry, UnitStates states) =>
        entry == null || !entry.requiresState || states == null ? 0
        : ReadsRunOfHits(entry) ? states.ConsecutiveHits
        : states.StacksFrom(entry.requireSkillId, entry.requireStateId);

    /// <summary>The total VALUE the required pile's stacks carry, read under the SAME compute-before-consume rule as
    /// <see cref="StackCount"/> — the snapshot a cast takes before it spends anything. 0 when the skill reads no pile.
    /// WHICH of the two is cashed is each EFFECT's own <see cref="SkillEffect.stackValueSource"/>, not a second gate:
    /// the gate always answers both, and every effect decides which answer is worth anything to it.
    ///
    /// <para>The run of hits answers 0 here, deliberately: its stacks are a TALLY, not an amount. Reading a value off
    /// it would be inventing one, so an effect paid by the run must read the COUNT.</para></summary>
    public static float CarriedTotal(SkillTriggerEntry entry, UnitStates states) =>
        entry == null || !entry.requiresState || states == null || ReadsRunOfHits(entry) ? 0f
        : states.CarriedFrom(entry.requireSkillId, entry.requireStateId);

    /// <summary>Spend what this use costs, AFTER the skill's effects have computed. No-op when the skill has no
    /// requirement or spends nothing (a pure "while X" condition). Returns how many stacks were actually spent.</summary>
    public static int Consume(SkillTriggerEntry entry, UnitStates states)
    {
        if (entry == null || !entry.requiresState || states == null) return 0;
        // A run of hits is ended by the fight, never by a skill cashing it — so there is nothing here to spend, and the
        // composer doesn't offer the knob. Answered here too, so a hand-edited asset can't spend what isn't spendable.
        if (ReadsRunOfHits(entry)) return 0;
        if (!entry.consumeAllStacks && entry.consumeStacks <= 0) return 0;
        return states.ConsumeFrom(entry.requireSkillId, entry.requireStateId,
                                  entry.consumeStacks, entry.consumeAllStacks);
    }
}
