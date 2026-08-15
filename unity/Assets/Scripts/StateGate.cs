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
    /// <summary>True when <paramref name="def"/> may run right now. A skill with no requirement always passes.
    /// Gates the WHOLE skill — callers must bail before playing any VFX or paying any cost.
    ///
    /// <para><b>Requiring and spending are SEPARATE questions.</b> A minimum of 0 means "no gate": the skill fires
    /// with an empty pile and simply cashes whatever is there. That's Deathblow — a Sword that never rolled Momentum
    /// still swings it, just without the payoff — and it's why this can't fold back into the on/off switch.</para></summary>
    public static bool IsMet(SkillTriggerEntry entry, UnitStates states)
    {
        if (entry == null || !entry.requiresState) return true;
        if (entry.requireMinStacks <= 0) return true;            // reads the pile, but doesn't demand it
        if (states == null) return false;
        return StackCount(entry, states) >= entry.requireMinStacks;
    }

    /// <summary>How many stacks of the required SKILL's pile the caster holds RIGHT NOW — the snapshot a cast takes
    /// before it spends anything, so its payoff and its cost read the same pile (the locked compute-before-consume
    /// order). 0 when the skill reads no pile.</summary>
    public static int StackCount(SkillTriggerEntry entry, UnitStates states) =>
        entry != null && entry.requiresState && states != null ? states.StacksFrom(entry.requireSkillId) : 0;

    /// <summary>The total VALUE the required pile's stacks carry, read under the SAME compute-before-consume rule as
    /// <see cref="StackCount"/> — the snapshot a cast takes before it spends anything. 0 when the skill reads no pile.
    /// WHICH of the two a skill cashes is its authored <see cref="StackPayoff"/>, not a second gate: the gate always
    /// answers both, and the payoff decides which answer is worth anything.</summary>
    public static float CarriedTotal(SkillTriggerEntry entry, UnitStates states) =>
        entry != null && entry.requiresState && states != null ? states.CarriedFrom(entry.requireSkillId) : 0f;

    /// <summary>Spend what this use costs, AFTER the skill's effects have computed. No-op when the skill has no
    /// requirement or spends nothing (a pure "while X" condition). Returns how many stacks were actually spent.</summary>
    public static int Consume(SkillTriggerEntry entry, UnitStates states)
    {
        if (entry == null || !entry.requiresState || states == null) return 0;
        if (!entry.consumeAllStacks && entry.consumeStacks <= 0) return 0;
        return states.ConsumeFrom(entry.requireSkillId, entry.consumeStacks, entry.consumeAllStacks);
    }
}
