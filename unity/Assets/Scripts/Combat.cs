using UnityEngine;

/// <summary>
/// RO-style physical accuracy — the one place the HIT-vs-FLEE roll lives, shared by both directions
/// (player→monster in <see cref="Health"/>, monster→player in <see cref="Character"/>).
///
/// A physical hit lands with chance <c>(80 + attackerHIT − defenderFLEE)%</c> (Ragnarok's), clamped to a
/// [<see cref="MinHitChance"/>, <see cref="MaxHitChance"/>] window. So:
/// • <b>HIT</b> (accuracy, from DEX + level) and <b>FLEE</b> (evasion, from AGI + level) trade off 1-for-1;
/// • even hopeless accuracy still lands 5% of the time, so FLEE alone never makes anyone untouchable;
/// • investing the relevant stat visibly shifts the rate.
/// Magic ignores all of this (MATK vs MDEF only — spells never "miss").
/// </summary>
public static class Combat
{
    public const float MinHitChance = 0.05f;   // even hopeless accuracy still connects 5% of the time
    public const float MaxHitChance = 1f;
    // a chance to hit may reach 100%: it breaks nothing, so it needs no lower ceiling

    /// <summary>Probability [0..1] that an attacker with <paramref name="attackerHit"/> HIT lands on a
    /// defender with <paramref name="defenderFlee"/> FLEE.</summary>
    public static float HitChance01(int attackerHit, int defenderFlee)
        => Mathf.Clamp((HitBase + (long)attackerHit - defenderFlee) / 100f, MinHitChance, MaxHitChance);

    /// <summary>Ragnarok's: chance to hit = HIT + 80 − FLEE, in percent.</summary>
    public const int HitBase = 80;

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

    /// <summary>An ordinary attack's hit chance in points BEFORE it is held inside 5–100: HitBase + HIT − FLEE, with the
    /// defender's FLEE cut for every creature past the second on it — the same FLEE <see cref="Missed(bool,int,int,int,int)"/>
    /// rolls against. Above 100 is accuracy the roll can no longer use.</summary>
    public static int RawHitPoints(int attackerHit, int defenderFlee, int attackersOnDefender)
        => (int)(HitBase + (long)attackerHit - UnitStatMath.CrowdedFlee(defenderFlee, attackersOnDefender));

    /// <summary>The whole of an ordinary attack's whiff, Ragnarok's way: the defender's perfect dodge first (LUK × 0.1%,
    /// and it stops even a crit), then the HIT-vs-FLEE roll with the defender's FLEE cut for every creature past the
    /// second on it (<see cref="UnitStatMath.CrowdedFlee"/>).</summary>
    public static bool Missed(bool crit, int attackerHit, int defenderFlee, int defenderLuk, int attackersOnDefender)
    {
        if (attackerHit >= 0 && defenderLuk > 0 && CombatRng.Chance(defenderLuk * UnitStatMath.PerfectDodgePerLuk)) return true;
        return Missed(crit, attackerHit, UnitStatMath.CrowdedFlee(defenderFlee, attackersOnDefender));
    }

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

    /// <summary>The most a blow can lose to hard defence: 900 tenths = 90%. Every source of hard defence together stops here.</summary>
    public const int MaxHardDefenseTenths = 900;

    /// <summary>
    /// Damage after defence — Ragnarok's two-part defence, the one place it lives for every direction (player→mob in
    /// <see cref="Health"/>, mob→player in <see cref="Character"/>). Hard defence takes a PERCENTAGE off first
    /// (<paramref name="hardTenths"/> in tenths of a percent, held inside [0, <see cref="MaxHardDefenseTenths"/>]), then
    /// soft defence subtracts FLAT: <c>atk × (1000 − hard) ÷ 1000 − soft</c>. A <paramref name="crit"/> skips both.
    /// Returns the pre-element value; the caller applies the element modifier and the final <c>max(1, …)</c>.
    /// </summary>
    public static float Mitigated(float atk, int hardTenths, int soft, bool crit)
    {
        if (crit) return atk;
        int hard = Mathf.Clamp(hardTenths, 0, MaxHardDefenseTenths);
        return atk * (1000 - hard) / 1000f - soft;
    }
}
