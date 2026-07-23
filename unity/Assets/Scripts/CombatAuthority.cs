using UnityEngine;

/// <summary>
/// The ONE place a resolved combat hit is APPLIED. Every damage source — a player swing (<see cref="Player3DAttacker"/>),
/// a skill tick (<see cref="SkillEffectApplier"/>), and a mob attack (<see cref="MonsterAI3D"/>) — routes its damage
/// through <see cref="Apply"/> instead of calling a sink directly. Centralized like <c>Hotkeys</c> (input) and
/// <c>CombatRng</c> (dice): a single choke point a rule can be added to ONCE and can't be forgotten at a call site.
///
/// <para><b>Today</b> it applies locally and synchronously — zero behavior change, still singleplayer. <b>Once
/// server-authoritative</b> this is the seam the network layer swaps: a client's <see cref="Apply"/> becomes "send the
/// intent to the server → the server validates it, rolls the outcome via <see cref="CombatRng"/>, and broadcasts an
/// authoritative <see cref="DamageInfo"/> → every client applies it here." The <see cref="IDamageable"/> target +
/// <see cref="DamageInfo"/> result are exactly the payload that crosses the wire, so no call site changes when it does.</para>
/// </summary>
public static class CombatAuthority
{
    /// <summary>Apply a resolved hit to <paramref name="target"/>. Returns damage dealt. No IsDead gate here — the
    /// sinks own that (a normal hit skips a dead target; an overkill beat still registers past death, RO-style).</summary>
    public static int Apply(IDamageable target, in DamageInfo info)
    {
        if (target == null) return 0;
        return target.ApplyDamage(info);
    }

    /// <summary>Apply RAW over-time / reflected damage (poison/burn/bleed ticks, thorns reflect) — the second combat
    /// channel through this seam. Bypasses DEF/element/crit; same server-authoritative interception point as
    /// <see cref="Apply"/> so a client can't fabricate DoT ticks once networking lands. Returns damage dealt.</summary>
    public static int ApplyTrue(IDamageable target, int amount, Color color)
    {
        if (target == null) return 0;
        return target.ApplyTrueDamage(amount, color);
    }
}
