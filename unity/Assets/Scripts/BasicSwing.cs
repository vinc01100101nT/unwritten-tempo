using UnityEngine;

/// <summary>
/// ONE ordinary attack landing on one victim — the single implementation of what a basic attack IS, shared by every
/// unit that has one and by every skill that asks to resolve as one
/// (<see cref="SkillEffect.resolveAsBasicAttack"/>).
///
/// <para><b>Nothing here names the player.</b> Both sides are <see cref="ICombatUnit"/>: the attacker supplies its own
/// crit rule, accuracy, weapon element and damage roll, the victim its dodge and crit shield. A skill therefore says
/// only "swing normally" and whoever casts it swings by their own numbers — the skill never depends on who is holding
/// it, and there is no second copy of the swing to drift.</para>
///
/// <para><b>The order is the load-bearing part</b>, and it is why this exists as one function rather than a rule each
/// caller re-applies: crit → accuracy → magnitude. Crit first because a crit cannot be evaded; accuracy next and ONCE,
/// here on the attacker, with the answer travelling in the hit so the defender only presents it; the damage number
/// last, so a whiff never pays for a roll.</para>
///
/// <para>A landed swing announces <see cref="SkillTrigger.OnBasicAttackDealt"/> — that announcement is part of what a
/// basic attack IS, which is exactly why a skill strike routed through here feeds Momentum and every other on-basic
/// passive while an ordinary skill strike does not.</para>
/// </summary>
public static class BasicSwing
{
    /// <summary>Land one swing from <paramref name="attacker"/> on <paramref name="victim"/>, scaled by
    /// <paramref name="powerMult"/> (1 = an ordinary swing; a skill passes its own authored power). Returns the damage
    /// dealt, so a rising combo total can accumulate it. <paramref name="overkill"/> keeps the number and its sound
    /// registering past the victim's death, the way a multi-hit cascade must.</summary>
    public static int Land(ICombatUnit attacker, ICombatUnit victim, float powerMult = 1f, bool overkill = false,
                           bool announceIntent = false)
    {
        if (attacker == null || victim == null) return 0;

        // A swing has TWO moments — about to swing, then landed — and a passive may key off either. An ordinary attack
        // announces the first one much earlier (before its reach is even judged, so a gap-closer can move you in and the
        // same swing still lands), which is why it asks for this to stay off: it has already announced. A swing a skill
        // produces has no separate wind-up to announce from, so it raises both here, in the same order.
        if (announceIntent) PassiveEvents.Fire(SkillTrigger.OnBasicAttackStart, null, victim.Body);

        bool crit = attacker.RollBasicCrit(victim);
        int hit = attacker.BasicHit;
        bool missed = Combat.Missed(crit, hit, victim.Flee);
        // A whiff rolls no damage at all — the miss rides along and the defender only shows it.
        int power = missed ? 0
                  : Mathf.Max(1, Mathf.RoundToInt(attacker.RollBasicDamage(crit) * Mathf.Max(0f, powerMult)));
        // One swing, one charge — spent on the whiff too, which is why this sits outside the miss branch.
        power = UnitOffense.Boosted(attacker.States, power);

        int dealt = CombatAuthority.Apply(victim, new DamageInfo(power, attacker.BasicElement, attacker.BasicIsMagic,
                                                                crit, attackerHit: hit, overkill: overkill,
                                                                missed: missed));
        if (dealt > 0)
        {
            var body = victim.Body;
            if (body != null) VfxCue.Fire(crit ? VfxCueId.OnHitByCriticalAttack : VfxCueId.OnHitByNormalAttack, body);
            // A real basic attack connected — the moment Momentum, Perfect Form and every other on-basic passive wait
            // for. Fired HERE, at the one place a swing lands, so no way of swinging can forget to announce it.
            PassiveEvents.Fire(SkillTrigger.OnBasicAttackDealt, null, body);
        }
        return dealt;
    }
}
