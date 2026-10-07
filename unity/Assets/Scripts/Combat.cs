using UnityEngine;

/// <summary>
/// RO-style physical accuracy — the one place the HIT-vs-FLEE roll lives, shared by both directions
/// (player→monster in <see cref="Health"/>, monster→player in <see cref="Character"/>).
///
/// A physical hit lands with chance <c>(100 + attackerHIT − defenderFLEE)%</c>, clamped to a
/// [<see cref="MinHitChance"/>, <see cref="MaxHitChance"/>] window. So:
/// • <b>HIT</b> (accuracy, from DEX + level) and <b>FLEE</b> (evasion, from AGI + level) trade off 1-for-1;
/// • there's always a sliver of both — even a maxed attacker whiffs ~5%, and you can't fully dodge by FLEE alone;
/// • investing the relevant stat visibly shifts the rate.
/// Magic ignores all of this (MATK vs MDEF only — spells never "miss").
/// </summary>
public static class Combat
{
    public const float MinHitChance = 0.05f;   // even hopeless accuracy still connects 5% of the time
    public const float MaxHitChance = 0.95f;   // …and even a sitting duck dodges 5% (so MISS always shows)

    /// <summary>Probability [0..1] that an attacker with <paramref name="attackerHit"/> HIT lands on a
    /// defender with <paramref name="defenderFlee"/> FLEE.</summary>
    public static float HitChance01(int attackerHit, int defenderFlee)
        => Mathf.Clamp((100 + attackerHit - defenderFlee) / 100f, MinHitChance, MaxHitChance);

    /// <summary>Roll it: true = the hit lands, false = it's a MISS.</summary>
    public static bool Lands(int attackerHit, int defenderFlee)
        => CombatRng.Chance(HitChance01(attackerHit, defenderFlee));

    /// <summary>Resolve whether an attack WHIFFS — the ONE place the whole accuracy condition lives, for every
    /// direction (player→mob and mob→player). A crit can't be evaded, and <paramref name="attackerHit"/> −1 means an
    /// always-lands skill strike, so both skip the roll.
    ///
    /// <para><b>This is a RANDOM roll, so it must happen EXACTLY ONCE per attack — on the ATTACKER, before the damage
    /// magnitude is rolled.</b> Its answer then travels to the defender in <see cref="DamageInfo.missed"/> rather than
    /// being re-rolled there: a second roll would be a second coin-flip and could disagree with the first. Rolling it
    /// up front is also what lets a whiff skip the damage roll entirely, and what lets a one-shot "next hit" buff be
    /// spent only when the strike actually connects.</para></summary>
    public static bool Missed(bool crit, int attackerHit, int defenderFlee)
        => !crit && attackerHit >= 0 && !Lands(attackerHit, defenderFlee);

    /// <summary>Whether one physical blow is stopped outright, at <paramref name="blockChance"/> — the unit's block-chance
    /// sub-stat, which only a skill grants. Rolled once. The one roll for every unit, so a creature blocks exactly as
    /// often as a player with the same chance does.</summary>
    public static bool Blocked(float blockChance)
    {
        float chance = BlockChance(blockChance);
        return chance > 0f && CombatRng.Chance(chance);
    }

    /// <summary>A block chance held inside [0, 95%] — a wall that never lets a hit through would be a wall nobody could
    /// fight.</summary>
    public static float BlockChance(float chance) => Mathf.Clamp(chance, 0f, 0.95f);

    /// <summary>
    /// Damage after defence — the one place it lives, shared by both directions (player→mob in
    /// <see cref="Health"/>, mob→player in <see cref="Character"/>) so they never drift. Defence
    /// (<paramref name="defense"/>, worked out from VIT, level and what is worn) subtracts FLAT:
    /// <c>dmg = atk − defense</c>. A <paramref name="crit"/> hit <b>ignores it</b> (criticals bypass defence;
    /// they also bypass FLEE, which the caller handles by skipping the accuracy roll). A percentage cut is not
    /// this function's business: worn words and timed states carry it as the damage-reduction sub-stat, which
    /// <see cref="UnitMitigation.Landed"/> applies. Returns the pre-element value (the caller applies the element
    /// modifier and the final <c>max(1, …)</c>); can be negative if defence exceeds the attack, which the caller's
    /// clamp turns into the 1-damage floor.
    /// </summary>
    public static float AfterDefense(float atk, int defense, bool crit)
    {
        if (crit) return atk;   // critical: no DEF at all
        return atk - defense;
    }
}
