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

    /// <summary>
    /// Pre-renewal damage after DEF — the one place the split lives, shared by both directions
    /// (player→mob in <see cref="Health"/>, mob→player in <see cref="Character"/>) so they never drift.
    /// <b>Hard DEF</b> (<paramref name="hardDefPercent"/>, 0–100) is a PERCENT reduction applied first;
    /// <b>soft DEF</b> (<paramref name="softDef"/>) then subtracts FLAT:
    /// <c>dmg = atk × (1 − hard/100) − soft</c>.
    /// A <paramref name="crit"/> hit <b>ignores BOTH</b> (RO: criticals bypass DEF and VIT-DEF; they also
    /// bypass FLEE, which the caller handles by skipping the accuracy roll). Returns the pre-element value
    /// (the caller applies the element modifier and the final <c>max(1, …)</c>); can be negative if soft DEF
    /// exceeds the post-hard damage, which the caller's clamp turns into the 1-damage floor.
    /// </summary>
    public static float AfterDefense(float atk, int hardDefPercent, int softDef, bool crit)
    {
        if (crit) return atk;   // critical: no DEF at all
        float afterHard = atk * (1f - Mathf.Clamp(hardDefPercent, 0, 100) / 100f);
        return afterHard - softDef;
    }

    // Magic can't be dodged, but an evasive (high-FLEE) defender takes GLANCING magic — a PARTIAL reduction, never a
    // miss (design "Combat Ontology": magic is dodge-RESISTANT, not undodgeable). This is the Arcane→Finesse edge of
    // the RPS triangle. The rule is final; these two numbers are PLACEHOLDERS — tune in balance, don't rewrite logic.
    public const float MagicGlancePerFlee = 0.004f;     // damage fraction shaved per point of defender FLEE
    public const float MagicGlanceMaxReduction = 0.5f;  // cap — a spell always keeps at least half its bite

    /// <summary>Damage multiplier a spell keeps against a defender with <paramref name="defenderFlee"/> FLEE:
    /// 1 at zero evasion, falling to <c>1 − <see cref="MagicGlanceMaxReduction"/></c>. Never a full miss.</summary>
    public static float MagicGlanceMult(int defenderFlee)
        => 1f - Mathf.Clamp(defenderFlee * MagicGlancePerFlee, 0f, MagicGlanceMaxReduction);
}
