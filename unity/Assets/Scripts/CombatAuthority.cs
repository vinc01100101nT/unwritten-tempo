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

        // ON A CONNECTED CLIENT, NOBODY'S HP CHANGES HERE. The world lives on the server, so a hit is a
        // REQUEST, not an act: it is sent, the server decides what really happened, and the answer comes back
        // to every player at once. Returning 0 is not a failure — it is this machine saying "I did not decide
        // that". Everything a landed hit sets off (flinch, on-hit effects, a broken run of hits) then happens
        // exactly once, on the server, instead of once per watching client.
        //
        // Offline, none of this is true and the whole method runs as it always has.
        if (GameNetwork.SendHitToServer(target, info)) return 0;
        // The attacker's RUN of hits is counted here, before the sink, for the same reason the flinch is decided after
        // it: this is the one door every real hit passes through. Counting before means the number is already right
        // when the sink fires the moment a skill reads it — and switching targets restarts the run at this very hit.
        var attackerStates = info.attacker != null ? info.attacker.States : null;
        if (attackerStates != null) attackerStates.NoteAttackHit(target as ICombatUnit);

        int dealt = target.ApplyDamage(info);
        // FLINCH is decided here because this is the one door every real hit passes through — a player swing, a skill
        // tick and a mob's bite alike. It keys off what was actually DEALT, so a miss, a parry, a full shield absorb
        // and a braced hit all cost no HP and stagger nobody, with no case written for any of them.
        if (dealt > 0 && target is ICombatUnit unit)
        {
            var status = unit.StatusHost;
            if (status != null) status.TookHit(dealt);
        }
        // The same DEALT number read from the ATTACKER's side: a hit that cost the victim nothing ends their run. This
        // is the ONLY place that fact exists — a miss, a parry and a fully absorbed hit fire no trigger for anything to
        // hear. Overkill beats are excluded: a flurry's extra numbers past a mob's death are presentation, and reading
        // them as failed hits would break a run the attacker never actually dropped.
        if (attackerStates != null && dealt <= 0 && !info.overkill) attackerStates.BreakConsecutiveHits();
        return dealt;
    }

    /// <summary>Green digits over a unit that just gained HP.</summary>
    static readonly Color HPAddedColor = new Color(0.45f, 1f, 0.45f);

    /// <summary>Fired whenever a unit's HP goes UP, by ANY route — a heal skill, a potion, regen, a life-steal
    /// trickle, anything written later. Deliberately says only WHO and HOW MUCH: a listener that had to know which
    /// source it came from would need a new case for every future source, which is the exact thing this replaces.</summary>
    public static event System.Action<ICombatUnit, int> OnHPAdded;

    /// <summary>Announce that <paramref name="unit"/> just gained <paramref name="amount"/> HP. The ONE door every
    /// gain passes through, so the green digits and the cue are written once and cannot be forgotten by whoever adds
    /// the next source. The caller has already applied the HP — this only announces it.
    ///
    /// <para>The digits show the FULL intended amount even when the unit was already at full: the gain still landed,
    /// and hiding it would make a heal at full health look like it failed.</para></summary>
    public static void NoteHPAdded(ICombatUnit unit, int amount)
    {
        if (unit == null || amount <= 0) return;
        var body = unit.Body;
        if (body != null)
        {
            DamagePopup.SpawnText(UnitAnchor.Overhead(body), "+" + amount, HPAddedColor);
            VfxCue.Fire(VfxCueId.OnHPAdded, body);
        }
        OnHPAdded?.Invoke(unit, amount);
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
