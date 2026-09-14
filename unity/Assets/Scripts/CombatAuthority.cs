using UnityEngine;

/// <summary>
/// The ONE place a resolved combat hit is APPLIED. Every damage source — a player swing (<see cref="Player3DAttacker"/>),
/// a skill tick (<see cref="SkillEffectApplier"/>), and a mob attack (<see cref="MonsterAI3D"/>) — routes its damage
/// through <see cref="Apply"/> instead of calling a sink directly. Centralized like <c>Hotkeys</c> (input) and
/// <c>CombatRng</c> (dice): a single choke point a rule can be added to ONCE and can't be forgotten at a call site.
///
/// <para><b>Only the server lands anything here.</b> A player's machine asks the server to swing or to cast, the server
/// rolls and lands the blow through this door, and every screen is told what was drawn — a creature's blow by the
/// creature itself (<c>Health.Show</c>), a person's by <see cref="GameNetwork.AnnounceBlowOnPlayer"/>.</para>
/// </summary>
public static class CombatAuthority
{
    /// <summary>Apply a resolved hit to <paramref name="target"/>. Returns damage dealt. No IsDead gate here — the
    /// sinks own that (a normal hit skips a dead target; an overkill beat still registers past death, RO-style).</summary>
    public static int Apply(IDamageable target, in DamageInfo info)
    {
        if (target == null) return 0;

        // ON A PLAYER'S MACHINE, NOTHING LANDS HERE — EVER — AND NOTHING IS SENT FROM HERE EITHER. A blow reaching this
        // door has already been rolled, and a player's machine rolls nothing: it asks the server to swing
        // (GameNetwork.AskToSwingAt) or to cast, and the server lands the blow and tells every screen what it drew.
        // Returning 0 is this machine saying "I did not decide that".
        if (!GameServer.IsServer)
        {
            WarnNotTheAuthority(target);
            return 0;
        }

        return Land(target, info);
    }

    // Said ONCE per kind of target, not once per blow: whatever is producing these is producing them at combat
    // speed, and a line each would bury the one that matters. This exists because the alternative — dropping the
    // blow in silence — is how "why does nothing take damage" costs somebody an afternoon.
    static readonly System.Collections.Generic.HashSet<string> warnedAbout = new System.Collections.Generic.HashSet<string>();

    static void WarnNotTheAuthority(IDamageable target)
    {
        string kind = target.GetType().Name;
        if (!warnedAbout.Add(kind)) return;
        GameLog.Warn(LogCat.Net, "a blow aimed at a '" + kind + "' was dropped: only the server lands blows, and "
                         + "something on this player's machine rolled one anyway. Whatever produced it is working "
                         + "out damage where it must not.");
    }

    // The door itself, once. Everything above decides WHETHER this machine is the one that gets to open it.
    static int Land(IDamageable target, in DamageInfo info)
    {
        // The attacker's RUN of hits is counted here, before the sink, for the same reason the flinch is decided after
        // it: this is the one door every real hit passes through. Counting before means the number is already right
        // when the sink fires the moment a skill reads it — and switching targets restarts the run at this very hit.
        // Gone, not merely null: a blow can outlive whoever threw it — a projectile in flight, a delayed tick, a
        // player who disconnected — and a destroyed object behind an interface reference passes a null check.
        // A blow on a target that is ALREADY dead — a flurry beat, a swing or an arrow that was on its way — is
        // presentation only: the sink draws its number and nothing else, so it teaches nobody anything: no run of hits,
        // no credit for the kill, no flinch.
        bool presentationOnly = target.IsDead;
        var attackerStates = !presentationOnly && !PlayerUnits.Gone(info.attacker) ? info.attacker.States : null;
        if (attackerStates != null) attackerStates.NoteAttackHit(target as ICombatUnit);

        int dealt = target.ApplyDamage(info);

        // WHAT A BLOW ACTUALLY DID, written where the answer is real. A player's own machine reads zero for every
        // blow it throws — that is by design a few lines up — so three separate play-tests read that zero and
        // reported the game broken when it was working. This line is the honest answer, and it says WHY nothing
        // happened as well as when something did: a miss, a full absorb and a parry all arrive here as zero and
        // each is a different fact. Guarded because this is the per-hit path.
        if (GameLog.On(LogCat.Hit))
            GameLog.Info(LogCat.Hit, string.Concat(
                info.attacker != null && !PlayerUnits.Gone(info.attacker) ? info.attacker.Body != null ? info.attacker.Body.name : "somebody" : "somebody",
                info.missed ? " MISSED " : " hit ",
                (target as ICombatUnit)?.Body != null ? (target as ICombatUnit).Body.name : target.GetType().Name,
                " for ", dealt.ToString(),
                info.isCrit ? " (critical)" : "",
                dealt == 0 && !info.missed ? " — nothing got through" : ""));

        // WHO DID THIS, recorded once, here, for every unit alike — a kill pays whoever landed it, and this is the
        // only place that knows. A blow that cost nothing (a miss, a parry, a full absorb) is not a claim on anyone.
        if (!presentationOnly && dealt > 0 && !PlayerUnits.Gone(info.attacker) && target is ICombatUnit hurt) hurt.NoteAttacker(info.attacker);
        // FLINCH is decided here because this is the one door every real hit passes through — a player swing, a skill
        // tick and a mob's bite alike. It keys off what was actually DEALT, so a miss, a parry, a full shield absorb
        // and a braced hit all cost no HP and stagger nobody, with no case written for any of them.
        if (!presentationOnly && dealt > 0 && target is ICombatUnit unit)
        {
            var status = unit.StatusHost;
            if (status != null) status.TookHit(dealt);
        }
        // The same DEALT number read from the ATTACKER's side: a hit that cost the victim nothing ends their run. This
        // is the ONLY place that fact exists — a miss, a parry and a fully absorbed hit fire no trigger for anything to
        // hear. Overkill beats are excluded: a flurry's extra numbers past a mob's death are presentation, and reading
        // them as failed hits would break a run the attacker never actually dropped.
        if (attackerStates != null && dealt <= 0 && !info.overkill && !presentationOnly) attackerStates.BreakConsecutiveHits();

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
            ShowHPAdded(body, amount);
            VfxCue.Fire(VfxCueId.OnHPAdded, body);
        }
        OnHPAdded?.Invoke(unit, amount);
        // Health is the server's, so this is where every gain happens — and a gain nobody says out loud is a bar that
        // rises with no digits to explain it.
        GameNetwork.AnnounceHPAdded(unit, amount);
    }

    /// <summary>The green digits alone — what a watching machine draws when the server says a unit was healed.</summary>
    public static void ShowHPAdded(Transform body, int amount) =>
        DamagePopup.SpawnText(UnitAnchor.Overhead(body), "+" + amount, HPAddedColor);

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
            GameNetwork.AnnounceBlowOnPlayer(person, new DamageInfo(dealt, Element.Neutral, isMagic: false), dealt, tick: true);
        return dealt;
    }
}
