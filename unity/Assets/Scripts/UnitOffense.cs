using UnityEngine;

/// <summary>
/// The OUTGOING half of a unit's combat math — the mirror of <see cref="UnitMitigation"/> (which owns the incoming
/// half). One implementation, called by the player (<see cref="Character"/>, <see cref="Player3DAttacker"/>) and by
/// every mob (<see cref="MonsterAI3D"/>), because a buff is a property of the UNIT, not of who is controlling it.
///
/// <para>Why this exists: a mob's damage used to be its raw <c>attack</c> field and its cadence its raw
/// <c>touchInterval</c>, so every damage buff and attack-speed buff was silently ignored on mobs — a "cut this
/// monster's damage" debuff did nothing at all, with no error to catch it. That is the same shape of bug
/// <see cref="UnitMitigation.Landed"/> was created to kill on the incoming side. Route new outgoing damage paths
/// through here and they inherit buffs for free; don't reintroduce a hand-written multiply.</para>
/// </summary>
public static class UnitOffense
{
    /// <summary>Turn a RAW damage roll into what the unit actually deals, scaled by every live damage buff it holds.
    /// Takes a float so the buff is applied BEFORE rounding rather than on an already-rounded number.
    ///
    /// <para>Deliberately NOT floored at 1: an attacker whose damage has been debuffed to nothing should land nothing,
    /// and the defence math downstream owns the "a hit that connects always registers" floor
    /// (<see cref="UnitMitigation.Landed"/>). Flooring here too would double up.</para></summary>
    public static int Dealt(UnitStates states, float rawRoll)
    {
        float buff = states != null ? states.DamageBuffMult : 1f;
        return Mathf.RoundToInt(rawRoll * buff);
    }

    /// <summary>Seconds from one swing to the next, given a base cadence and the unit's attack-speed multiplier
    /// (&gt;1 = faster). Shared so the player and a mob speed up by the exact same rule.
    ///
    /// <para>The multiplier is clamped to <see cref="UnitStatMath.MaxAttackSpeedMult"/> — the same ceiling the player's
    /// stat formula already applies — so a stack of haste buffs can't drive a mob's cadence to zero. The 0.05s floor
    /// keeps a heavily-slowed attacker from dividing its way into a frozen swing.</para></summary>
    public static float AttackInterval(float baseInterval, float attackSpeedMult)
    {
        float m = Mathf.Clamp(attackSpeedMult, 0.01f, UnitStatMath.MaxAttackSpeedMult);
        return Mathf.Max(0.05f, baseInterval / m);
    }
}
