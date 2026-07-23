using UnityEngine;

/// <summary>
/// One immutable damage RESULT — the shape a server-authoritative build broadcasts and every client applies
/// verbatim. Bundles the already-resolved amount + element + flags so <see cref="IDamageable.ApplyDamage"/> is a
/// single entry no matter who takes the hit. It carries the FULL context the existing sinks need so routing through
/// it never changes behavior: <see cref="attackerHit"/> drives the RO accuracy roll (≥0 rolls HIT vs FLEE, −1 always
/// lands), <see cref="overkill"/> keeps a multi-hit flurry's numbers climbing past a mob's death, and
/// <see cref="source"/> is the attacking mob a player-side reflect bounces back at.
/// </summary>
public readonly struct DamageInfo
{
    public readonly int amount;
    public readonly Element element;
    public readonly bool isMagic;
    public readonly bool isCrit;
    public readonly int attackerHit;      // ≥0 → roll HIT vs the defender's FLEE; −1 → always-lands (skill strike)
    public readonly bool overkill;        // mob multi-hit: register the number/sound even past death (RO cascade)
    public readonly MonsterAI3D source;   // the attacking mob, for player-side damage-reflect (null otherwise)

    public DamageInfo(int amount, Element element, bool isMagic = false, bool isCrit = false,
                      int attackerHit = -1, bool overkill = false, MonsterAI3D source = null)
    {
        this.amount = amount;
        this.element = element;
        this.isMagic = isMagic;
        this.isCrit = isCrit;
        this.attackerHit = attackerHit;
        this.overkill = overkill;
        this.source = source;
    }
}

/// <summary>
/// Anything that can be hurt — a mob's <see cref="Health"/> or the player's <see cref="Character"/>. Gives the
/// network layer ONE type to target and apply an authoritative <see cref="DamageInfo"/> to, instead of switching on
/// two concrete sink classes. Purely additive: the existing per-class TakeDamage/TakeMagicDamage overloads are
/// untouched; <see cref="ApplyDamage"/> just routes into them. Applied through the <see cref="CombatAuthority"/> seam.
/// </summary>
public interface IDamageable
{
    /// <summary>Already at zero HP — the sink decides whether to skip (normal) or still register (overkill).</summary>
    bool IsDead { get; }

    /// <summary>World point to anchor damage numbers / impact FX at (the mob body, or the player avatar).</summary>
    Vector3 DamagePosition { get; }

    /// <summary>Apply a resolved hit, routing to this entity's existing damage sink. Returns damage dealt.</summary>
    int ApplyDamage(in DamageInfo info);

    /// <summary>Apply RAW over-time / reflected damage that bypasses DEF, element, and crit (poison/burn/bleed ticks,
    /// thorns reflect). A distinct channel from <see cref="ApplyDamage"/> — same seam owner, different rules. Returns
    /// damage dealt.</summary>
    int ApplyTrueDamage(int amount, Color color);
}
