using UnityEngine;

/// <summary>WHY A BLOW COST ITS TARGET NOTHING. Written by the damage code at the moment it returns zero, and read by the
/// hit line straight after — so "nothing got through" can say which of these it was.</summary>
public enum NoDamageReason { None, Missed, AlreadyDead, Immune, Parried, Guarded, Dodged, Invincible, Soaked, Braced }

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

        // WHO DID THIS is written BEFORE the sink, because the sink is where a unit dies — and a death pays its gold,
        // its experience and its on-kill passives to whoever is recorded at that instant. Written after, a unit killed
        // by the first blow that hurt it paid nobody (play-test, 2026-09-14). A blow that then cost nothing hands the
        // record back to whoever held it, so a miss, a parry or a full absorb is still no claim on anyone.
        var hurt = !presentationOnly && !PlayerUnits.Gone(info.attacker) ? target as ICombatUnit : null;
        var priorAttacker = hurt != null ? hurt.LastAttacker : null;
        if (hurt != null) hurt.NoteAttacker(info.attacker);

        lastNoDamage = NoDamageReason.None;
        int dealt = target.ApplyDamage(info);
        if (hurt != null && dealt <= 0) hurt.NoteAttacker(priorAttacker);

        // WHAT A BLOW ACTUALLY DID, written where the answer is real. A player's own machine reads zero for every
        // blow it throws — that is by design a few lines up — so three separate play-tests read that zero and
        // reported the game broken when it was working. This line is the honest answer, and it says WHY nothing
        // happened as well as when something did: a miss, a full absorb and a parry all arrive here as zero and
        // each is a different fact. Guarded because this is the per-hit path.
        if (GameLog.On(LogCat.Hit)) GameLog.Info(LogCat.Hit, HitLine(target, info, dealt, presentationOnly));

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

    // WHY THE BLOW IN PROGRESS COST NOTHING. Reset before each blow and written by the damage code at the moment it
    // returns zero — after anything that return set off (a parry's counter-passive can land blows of its own), so the
    // reason sitting here when the hit line reads it is this blow's own.
    static NoDamageReason lastNoDamage;

    /// <summary>Return zero from a damage intake and say why. Every intake that turns a blow away returns through
    /// this, so the hit line can say which way it was turned away instead of only "nothing got through".</summary>
    public static int NoDamage(NoDamageReason why)
    {
        lastNoDamage = why;
        return 0;
    }

    // "field_15 Snake #3 hit Alpha #1 with basic attack for 12 (rolled 40) (critical) [HP 88/100]". WITH names the skill
    // whose effects are resolving right now — every skill cast, passive and flurry beat pushes its id for exactly that
    // long — and "basic attack" when none is: a swing, a bite, an arrow. ROLLED is the blow as it arrived, before the
    // target's defence, element, barrier or brace touched it. "(after it died)" marks a flurry beat or a blow already on
    // its way that reached a unit already dead: it prints the number that was shown but took no health, so a total of
    // damage dealt must leave it out. [HP] is the target's health after the blow. Built only behind the switch.
    static string HitLine(IDamageable target, in DamageInfo info, int dealt, bool afterDeath)
    {
        var victim = target as ICombatUnit;
        string map = victim != null ? SpawnIds.MapOf(victim) : SpawnIds.MapOf(info.attacker);
        string who = SpawnIds.Label(info.attacker);
        string whom = victim != null ? SpawnIds.Label(victim) : target.GetType().Name;
        string skill = SkillWords();
        if (info.missed) return string.Concat(map, " ", who, " MISSED ", whom, " with ", skill);

        return string.Concat(new[]
        {
            map, " ", who, " hit ", whom, " with ", skill, " for ", dealt.ToString(), " (rolled ", info.amount.ToString(), ")",
            info.isCrit ? " (critical)" : "",
            afterDeath ? " (after it died)" : "",
            victim != null ? HealthLeft(victim) : "",
            dealt == 0 ? " — nothing got through: " + NoDamageWords(lastNoDamage) : "",
        });
    }

    static string SkillWords() =>
        string.IsNullOrEmpty(PassiveEvents.CurrentSkillId) ? "basic attack" : PassiveEvents.CurrentSkillId;

    /// <summary>" [HP 88/100]" — a unit's health as it stands, for the end of a log line.</summary>
    public static string HealthLeft(ICombatUnit unit) =>
        string.Concat(" [HP ", unit.CurrentHP.ToString(), "/", unit.MaxHP.ToString(), "]");

    static string NoDamageWords(NoDamageReason why)
    {
        switch (why)
        {
            case NoDamageReason.Missed:      return "missed";
            case NoDamageReason.AlreadyDead: return "already dead";
            case NoDamageReason.Immune:      return "immune to that element";
            case NoDamageReason.Parried:     return "parried";
            case NoDamageReason.Guarded:     return "blocked by an off-hand shield";
            case NoDamageReason.Dodged:      return "dodged";
            case NoDamageReason.Invincible:  return "invincible";
            case NoDamageReason.Soaked:      return "soaked by a shield barrier";
            case NoDamageReason.Braced:      return "held back by a brace";
            default:                         return "reason not recorded";
        }
    }

    /// <summary>Write down that <paramref name="dead"/> just died, and who the kill is credited to — "field_15 Snake #3
    /// died — killed by Alpha #1". Called by both kinds of unit at the one moment each dies, and only on the server,
    /// which is the only machine that decides a death. "killed by nobody" means no attacker was recorded.</summary>
    public static void LogDeath(ICombatUnit dead)
    {
        if (!GameServer.IsServer || !GameLog.On(LogCat.Hit)) return;
        var killer = dead.LastAttacker;
        GameLog.Info(LogCat.Hit, string.Concat(SpawnIds.MapOf(dead), " ", SpawnIds.Label(dead), " died — killed by ",
                                               PlayerUnits.Gone(killer) ? "nobody" : SpawnIds.Label(killer)));
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
        LogHeal(unit, amount, null);
        // Health is the server's, so this is where every gain happens — and a gain nobody says out loud is a bar that
        // rises with no digits to explain it.
        GameNetwork.AnnounceHPAdded(unit, amount);
    }

    /// <summary>Write down a unit gaining health — "field_15 Alpha #1 healed 12 by skill.x [HP 60/100]". <paramref name="how"/>
    /// names a source the caller knows ("life steal"); when null, the skill resolving right now is named if there is
    /// one, and nothing is named otherwise (regeneration, a GM heal). The amount is what was given, which can be more
    /// than the bar had room for; the health after it is at the end.</summary>
    public static void LogHeal(ICombatUnit unit, int amount, string how)
    {
        if (!GameLog.On(LogCat.Heal) || PlayerUnits.Gone(unit)) return;
        string by = how ?? PassiveEvents.CurrentSkillId;
        GameLog.Info(LogCat.Heal, string.Concat(SpawnIds.MapOf(unit), " ", SpawnIds.Label(unit), " healed ", amount.ToString(),
                                                string.IsNullOrEmpty(by) ? "" : " by " + by, HealthLeft(unit)));
    }

    /// <summary>The green digits alone — what a watching machine draws when the server says a unit was healed.</summary>
    public static void ShowHPAdded(Transform body, int amount) =>
        DamagePopup.SpawnText(body, "+" + amount, HPAddedColor);

    /// <summary>Apply RAW over-time / reflected damage (poison/burn/bleed ticks, thorns reflect) — the second combat
    /// channel through this seam. Bypasses DEF/element/crit; same server-authoritative interception point as
    /// <see cref="Apply"/> so a client can't fabricate DoT ticks once networking lands. Returns damage dealt.</summary>
    /// <param name="cause">What this damage is, for its log line: an ailment's name, "reflect", "brace debt", "GM kill".</param>
    /// <param name="attacker">The unit this damage is credited to, when there is one — whoever put a damage-over-time
    /// ailment on. Recorded as the target's last attacker BEFORE the damage, exactly as a blow is at <see cref="Land"/>,
    /// because a death pays whoever is recorded at that instant. Null leaves the record as it was.</param>
    public static int ApplyTrue(IDamageable target, int amount, Color color, string cause, ICombatUnit attacker = null)
    {
        if (target == null) return 0;

        // THE SAME RULE AS AN ORDINARY HIT. A poison tick is still a change to a creature the server owns, and a
        // player's machine does not get to make it: with both halves subtracting their own ticks, the two argued
        // about the same creature's health ten times a second. The server is already burning its own copy on its
        // own clock, so nothing needs to be sent — this side simply does nothing.
        if (!GameServer.IsServer) return 0;

        // WHO IS CREDITED is written BEFORE the damage, for the reason Land gives: the damage is where a unit dies, and a
        // death pays whoever is recorded at that instant. A tick that then took nothing hands the record back.
        var credited = !PlayerUnits.Gone(attacker) ? target as ICombatUnit : null;
        var priorAttacker = credited != null ? credited.LastAttacker : null;
        if (credited != null) credited.NoteAttacker(attacker);

        int dealt = target.ApplyTrueDamage(amount, color);
        if (credited != null && dealt <= 0) credited.NoteAttacker(priorAttacker);

        // "field_15 Snake #3 took 4 from Poison by Alpha #1 [HP 40/60]" — damage that is not a blow still writes a line,
        // or a bar drops in the log with nothing to explain it.
        if (dealt > 0 && GameLog.On(LogCat.Hit) && target is ICombatUnit hurtUnit)
            GameLog.Info(LogCat.Hit, string.Concat(new[]
            {
                SpawnIds.MapOf(hurtUnit), " ", SpawnIds.Label(hurtUnit), " took ", dealt.ToString(), " from ", cause,
                credited != null ? " by " + SpawnIds.Label(attacker) : "", HealthLeft(hurtUnit),
            }));
        // Announced for the same reason an ordinary blow is: a person's own machine no longer works their health
        // out, so a burn nobody says out loud is a bar that drops with no number to explain it.
        if (dealt > 0 && target is Character person)
            GameNetwork.AnnounceBlowOnPlayer(person, new DamageInfo(dealt, Element.Neutral, isMagic: false), dealt, tick: true,
                                          tickColor: color);
        return dealt;
    }
}
