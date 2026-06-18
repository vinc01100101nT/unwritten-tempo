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
        => Random.value < HitChance01(attackerHit, defenderFlee);
}
