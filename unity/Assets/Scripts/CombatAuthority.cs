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

        // ON A PLAYER'S MACHINE, NOBODY'S HP CHANGES HERE — EVER. The world lives on the server, so a hit is a
        // REQUEST, not an act: it is sent, the server decides what really happened, and the answer comes back to
        // every player at once. Returning 0 is not a failure — it is this machine saying "I did not decide that".
        if (GameNetwork.SendHitToServer(target, info)) return 0;

        // The server is the only thing that reaches this line. There is no longer an offline game to fall through
        // to: a player who is not joined is not in the world, so there is nothing for them to hit and nothing of
        // theirs to be hit. A blow that gets here on a player's machine is one aimed at something the server has
        // never heard of, and the honest answer is that it does not land.
        if (!GameServer.IsServer)
        {
            WarnUnownedTarget(target);
            return 0;
        }

        return Land(target, info);
    }

    // Said ONCE per kind of target, not once per blow: whatever is producing these is producing them at combat
    // speed, and a line each would bury the one that matters. This exists because the alternative — dropping the
    // blow in silence — is how "why does nothing take damage" costs somebody an afternoon.
    static readonly System.Collections.Generic.HashSet<string> warnedAbout = new System.Collections.Generic.HashSet<string>();

    static void WarnUnownedTarget(IDamageable target)
    {
        string kind = target.GetType().Name;
        if (!warnedAbout.Add(kind)) return;
        Debug.LogWarning("[net] a blow aimed at a '" + kind + "' was dropped: this machine is not the authority "
                         + "and the server does not know that thing. Nothing local decides damage any more. If "
                         + "this is a creature hitting a player, that path is still waiting on per-connection "
                         + "fighters — see the migration plan.");
    }

    /// <summary>Apply a blow the SERVER already decided, on a machine that is only watching. Skips the forwarding
    /// step — this hit has already been to the server and come back, and sending it again would loop forever — and
    /// otherwise runs the identical door, so the number, the flash, the impact and the stagger are the ones the
    /// offline game has always produced rather than a second set written for the networked case.</summary>
    public static int ApplyFromServer(IDamageable target, in DamageInfo info)
    {
        if (target == null) return 0;
        return Land(target, info);
    }

    // The door itself, once. Everything above decides WHETHER this machine is the one that gets to open it.
    static int Land(IDamageable target, in DamageInfo info)
    {
        // The attacker's RUN of hits is counted here, before the sink, for the same reason the flinch is decided after
        // it: this is the one door every real hit passes through. Counting before means the number is already right
        // when the sink fires the moment a skill reads it — and switching targets restarts the run at this very hit.
        // Gone, not merely null: a blow can outlive whoever threw it — a projectile in flight, a delayed tick, a
        // player who disconnected — and a destroyed object behind an interface reference passes a null check.
        var attackerStates = !PlayerUnits.Gone(info.attacker) ? info.attacker.States : null;
        if (attackerStates != null) attackerStates.NoteAttackHit(target as ICombatUnit);

        int dealt = target.ApplyDamage(info);
        // WHO DID THIS, recorded once, here, for every unit alike — a kill pays whoever landed it, and this is the
        // only place that knows. A blow that cost nothing (a miss, a parry, a full absorb) is not a claim on anyone.
        if (dealt > 0 && !PlayerUnits.Gone(info.attacker) && target is ICombatUnit hurt) hurt.NoteAttacker(info.attacker);
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

        // A BLOW ON A PERSON HAS TO BE SAID OUT LOUD. The server has no screen, and the machine of the person it
        // happened to no longer works its own health out — so a hit the server keeps to itself is a hit that
        // never appears anywhere. Said HERE, at the one door, so a bite, a poison tick and whatever is written
        // next are all announced by one line that nobody has to remember to add.
        if (target is Character person) GameNetwork.AnnounceBlowOnPlayer(person, info, dealt);
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

        // THE SAME RULE AS AN ORDINARY HIT. A poison tick is still a change to a creature the server owns, and a
        // player's machine does not get to make it: with both halves subtracting their own ticks, the two argued
        // about the same creature's health ten times a second. The server is already burning its own copy on its
        // own clock, so nothing needs to be sent — this side simply does nothing.
        if (!GameServer.IsServer) return 0;

        int dealt = target.ApplyTrueDamage(amount, color);
        // Announced for the same reason an ordinary blow is: a person's own machine no longer works their health
        // out, so a burn nobody says out loud is a bar that drops with no number to explain it.
        if (dealt > 0 && target is Character person)
            GameNetwork.AnnounceBlowOnPlayer(person, new DamageInfo(dealt, Element.Neutral, isMagic: false), dealt);
        return dealt;
    }
}
