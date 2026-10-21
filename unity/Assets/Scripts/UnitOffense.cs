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

    /// <summary>What a BASIC ATTACK spends on top of <see cref="SpendCharges"/>: one basic-attack boost charge, hit or
    /// miss. Called only from <see cref="BasicSwing"/>, so a skill's own strike can never cash one.</summary>
    public static int SpendBasicAttackCharges(UnitStates states, int amount)
    {
        if (states == null) return amount;
        float mult = states.SpendBasicAttackBoost();
        return mult == 1f ? amount : Mathf.RoundToInt(amount * mult);
    }

    /// <summary>Seconds from one swing to the next, given a base cadence and the unit's attack-speed multiplier
    /// (&gt;1 = faster). Shared so the player and a mob speed up by the exact same rule.
    ///
    /// <para>The multiplier is clamped to <see cref="UnitStatMath.MaxAttackSpeedMult"/> — the same ceiling the player's
    /// stat formula already applies — so a stack of haste buffs can't drive a mob's cadence to zero. The 0.05s floor
    /// keeps a heavily-slowed attacker from dividing its way into a frozen swing.</para></summary>
    /// <summary>Seconds between ordinary swings with empty hands, before attack speed is applied.</summary>
    public const float BareSwingSeconds = 0.8f;

    /// <summary>What each point of weight a hand carries adds to the time between swings.</summary>
    public const float SwingSecondsPerWeight = 0.005f;

    /// <summary>Seconds between ordinary swings, before attack speed is applied, holding a weapon of
    /// <paramref name="weapon"/> type weighing <paramref name="weight"/> (0 = empty hands) — for a person and a creature
    /// alike. A pair weapon's weight is the pair's, and each hand swings only its own half.</summary>
    public static float SwingSeconds(WeaponType weapon, int weight)
    {
        float perHand = weight > 0 ? (WeaponTypes.IsDualWield(weapon) ? weight * 0.5f : weight) : 0f;
        return BareSwingSeconds + SwingSecondsPerWeight * perHand;
    }

    /// <summary>Seconds between swings: the weapon's base swing time turned into Ragnarok's ASPD by the attack-speed
    /// multiplier, capped at 190 (0.2 s) and floored at 0 (4 s).</summary>
    public static float AttackInterval(float baseInterval, float attackSpeedMult) =>
        UnitStatMath.SwingIntervalFromAspd(UnitStatMath.Aspd(baseInterval, attackSpeedMult));

    /// <summary>The attack-speed multiplier a swing actually plays at once the ASPD cap and floor are applied — what
    /// the swing animation is sped up by, so the body stays in step with the capped interval.</summary>
    public static float EffectiveSpeedMult(float baseInterval, float attackSpeedMult) =>
        baseInterval / AttackInterval(baseInterval, attackSpeedMult);

    /// <summary>"AM I CLOSE ENOUGH TO STAND AND FIGHT?" — the single answer, held by whoever is asking.
    ///
    /// <para>It is STICKY on purpose. A plain distance test flips true and false on alternate frames against a target
    /// hovering at the edge of reach, and everything hanging off it flips with it: the feet start and stop, the step
    /// sounds machine-gun, and the body snaps between standing and walking. So it turns on a margin INSIDE reach — reach
    /// divided by the one authored factor on the shared character spec — and stays on until the target is past reach.</para>
    ///
    /// <para><b>It never stands where the blow cannot land.</b> The margin sits inside reach, never outside it. It used
    /// to sit outside — on at reach, off at reach times the factor — and a target standing in that outer band held the
    /// asker planted and swinging at nothing, forever: MEASURED 2026-09-26, a spider biting from 0.327 away against a
    /// reach of 0.325, the person neither swinging nor stepping in until something reset the question.</para>
    ///
    /// <para>Held as a value on the asker — the player's attacker and every creature's brain — so one description of
    /// "stand and fight" serves both. Before this, the person at the keyboard stopped the moment they could swing and
    /// a creature walked on past that point to an authored hold distance, so it bit you while still walking.</para></summary>
    public struct EngageLatch
    {
        Object at;      // who the answer below is about; a different target starts the question again
        bool engaged;

        /// <summary>Answer for this frame, for a body that walks in to fight: on once the gap is inside reach by the
        /// margin, off once it is past reach. Both figures are SQUARED — every caller here already has the squared gap
        /// in hand and this runs on every creature every frame, so squaring the reach costs one multiply where taking
        /// the root of the gap would cost a square root per creature. They must be measured the same way as each other
        /// and as the blow itself.</summary>
        public bool Ask(Object target, float distanceSqr, float reachSqr)
            => Between(target, distanceSqr, reachSqr / MarginSqr, reachSqr);

        /// <summary>The same answer for a creature that HOLDS a shooting distance instead of walking in. Its feet are
        /// steered by that distance and never by this, so it cannot be stranded by it; this only says whether it may
        /// shoot, and it keeps the margin OUTSIDE the distance so a target edging just past it is not let off.</summary>
        public bool AskHolding(Object target, float distanceSqr, float holdSqr)
            => Between(target, distanceSqr, holdSqr, holdSqr * MarginSqr);

        bool Between(Object target, float distanceSqr, float onWithinSqr, float offPastSqr)
        {
            if (target == null) { engaged = false; at = null; return false; }
            if (target != at) { engaged = false; at = target; }

            if (distanceSqr <= onWithinSqr) engaged = true;
            else if (distanceSqr > offPastSqr) engaged = false;
            return engaged;
        }

        // READ FROM THE ONE AUTHORED HOME, here and nowhere else, so there is exactly one place this number exists and
        // one place it is read. It used to be an authored field on the player and a number written into this file for
        // creatures — same rule, one side tunable and the other not.
        static float MarginSqr
        {
            get { float m = Mathf.Max(1f, CharacterSpec.Active.stayEngagedFactor); return m * m; }
        }
    }
}
