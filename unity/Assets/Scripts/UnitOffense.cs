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

    /// <summary>The LAST thing that happens to an attack on the attacker's side: spend one charge from EACH outgoing
    /// charge pile this unit holds — Boost Outgoing Damage and Reduce Outgoing Damage — and multiply the attack by what
    /// those charges are worth. Returns the amount unchanged when it holds none, so every outgoing site can call it
    /// without asking first. Holding both is not a special case: the attack pays both multipliers.
    ///
    /// <para><b>Call it ONCE per attack, INCLUDING an attack that missed</b> (pass its 0) — the charge pays for making
    /// the attack, not for landing it, so a whiff spends one exactly as a hit does. Calling it twice for one attack
    /// would charge the unit twice, which is why it belongs at the site that BUILDS the hit and nowhere downstream.</para>
    ///
    /// <para>Deliberately BEFORE the target's armour, not after: armour subtracts a flat amount, so multiplying first
    /// means that subtraction is paid once out of a bigger number, and the boost is worth MORE the tankier the target.
    /// That is the design, and it also keeps the charge on the attacker, where the pile lives, instead of shipping the
    /// attacker's state across to the defender.</para>
    ///
    /// <para>Damage-over-time never reaches here — ailment ticks are true damage straight to HP — so it neither gains
    /// the boost nor eats a charge of either kind, with nothing to special-case.</para></summary>
    public static int SpendCharges(UnitStates states, int amount)
    {
        if (states == null) return amount;
        // BOTH piles are asked every attack, never one-or-the-other: an early return on the boost would let a unit
        // carrying both keep its penalty charges forever.
        float mult = states.SpendOutgoingBoost() * states.SpendOutgoingPenalty();
        return mult == 1f ? amount : Mathf.RoundToInt(amount * mult);
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

    /// <summary>"AM I CLOSE ENOUGH TO STAND AND FIGHT?" — the single answer, held by whoever is asking.
    ///
    /// <para>It is STICKY on purpose. A plain distance test flips true and false on alternate frames against a target
    /// hovering at the edge of reach, and everything hanging off it flips with it: the feet start and stop, the step
    /// sounds machine-gun, and the body snaps between standing and walking. So it turns on at reach and stays on
    /// until the target has drifted, by the one authored factor on the shared character spec, past its reach.</para>
    ///
    /// <para><b>It never extends reach.</b> Landing a blow still asks the strict distance; this only answers whether
    /// to stand still, which is why the extra margin costs nothing.</para>
    ///
    /// <para>Held as a value on the asker — the player's attacker and every creature's brain — so one description of
    /// "stand and fight" serves both. Before this, the person at the keyboard stopped the moment they could swing and
    /// a creature walked on past that point to an authored hold distance, so it bit you while still walking.</para></summary>
    public struct EngageLatch
    {
        Object at;      // who the answer below is about; a different target starts the question again
        bool engaged;

        /// <summary>Answer for this frame. Both figures are SQUARED — every caller here already has the squared gap
        /// in hand and this runs on every creature every frame, so squaring the reach costs one multiply where taking
        /// the root of the gap would cost a square root per creature. They must be measured the same way as each other
        /// and as the blow itself.</summary>
        public bool Ask(Object target, float distanceSqr, float reachSqr)
        {
            if (target == null) { engaged = false; at = null; return false; }
            if (target != at) { engaged = false; at = target; }

            if (distanceSqr <= reachSqr) engaged = true;
            else
            {
                // READ FROM THE ONE AUTHORED HOME, here and nowhere else, so there is exactly one place this number
                // exists and one place it is read. It used to be an authored field on the player and a number written
                // into this file for creatures — same rule, one side tunable and the other not.
                float stay = Mathf.Max(1f, CharacterSpec.Active.stayEngagedFactor);
                if (distanceSqr > reachSqr * stay * stay) engaged = false;
            }
            return engaged;
        }
    }
}
