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
/// </summary>
public static class StateGate
{
    /// <summary>True when <paramref name="def"/> may run right now. A skill with no requirement always passes.
    /// Gates the WHOLE skill — callers must bail before playing any VFX or paying any cost.</summary>
    public static bool IsMet(SkillDef def, UnitStates states)
    {
        if (def == null || !def.requiresState) return true;
        if (states == null) return false;
        return states.StackCount(def.requireState) >= Mathf.Max(1, def.requireMinStacks);
    }

    /// <summary>Spend what this use costs, AFTER the skill's effects have computed. No-op when the skill has no
    /// requirement or spends nothing (a pure "while X" condition). Returns how many stacks were actually spent.</summary>
    public static int Consume(SkillDef def, UnitStates states)
    {
        if (def == null || !def.requiresState || states == null) return 0;
        if (!def.consumeAllStacks && def.consumeStacks <= 0) return 0;
        return states.Consume(def.requireState, def.consumeStacks, def.consumeAllStacks);
    }
}
