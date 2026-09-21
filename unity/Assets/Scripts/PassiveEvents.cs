using UnityEngine;

/// <summary>
/// The ONE dispatch choke point for passive skills — the single handler every trigger routes through, so a new
/// trigger's rule is applied in exactly one place and no fire-site can forget it (centralized cross-cutting concern).
///
/// <para><b>Every moment names WHOSE it is.</b> A kill, a parry, a landed swing — each belongs to one character, and
/// the passives that answer it are that character's. This used to be implicit: there was one player at one keyboard,
/// so "the player" was a global. A server holds one character per person signed in, where that assumption is not
/// merely inconvenient but wrong in both directions — it would let one person's kill fire another person's passive,
/// and it silently switched the ownership check OFF entirely, because the check asked after a local player that a
/// server does not have.</para>
///
/// <para>So a moment is fired AT a unit, and the only passives considered are the ones on that unit's own skills
/// (<see cref="ICombatUnit.Skills"/>). Each matched passive then FIRES AS A FULL SKILL
/// (<see cref="SkillRunner.FireAsPassive"/>): same VFX + ticks + effects an active would play, with the aim
/// auto-resolved from the trigger (the victim's spot for aimed skills; the caster for self skills).</para>
///
/// <para>Trigger shapes: <see cref="SkillTrigger.OnSkillCastComplete"/> is parameterized by the completed skill's id
/// (only the passive keyed to that skill fires); the natural events carry no id and, when mob-facing, pass the
/// triggering unit as <paramref name="victim"/>.</para>
/// </summary>
public static class PassiveEvents
{
    // ---- NO-SELF-FEEDING BRAKE -----------------------------------------------------------------------------------
    // A passive never fires off a chain it is ALREADY part of. Every effect a passive causes carries the set of
    // passives that led to it (`chain`, one bit per skill the caster owns); a passive whose bit is already set is
    // skipped, while any OTHER passive still runs normally. Because no passive can repeat within one chain, every
    // chain is finite BY CONSTRUCTION — no depth counter to tune, and no runaway is possible.
    //
    // A passive's BIT is its position in its own caster's skill list, which is fixed for as long as that list is.
    // It used to be a slot in one process-wide table, which a server would have run out of after two people signed
    // in — and an untracked passive is an unbraked one.
    const int MaxTrackedPassives = 64;

    // What is causing the effects landing right now. Per MACHINE, not per character, and correctly so: a resolve is
    // synchronous, so only one cast is ever mid-flight here, and these say what it is.
    static ulong chain;
    static string causeSkillId = "";

    /// <summary>The chain currently in force — a passive's runner stamps this onto the effects it causes.</summary>
    public static ulong CurrentChain => chain;

    /// <summary>WHICH SKILL is landing the effects being applied right now, or "" when nothing skill-shaped is (a basic
    /// attack, a damage-over-time tick, the engine). Read by a moment that happens INSIDE a cast and wants to name its
    /// cause — a kill is the first: "a mob died" and "Blade Dance killed it" are different moments, and only the second
    /// can drive a passive that answers one particular skill.</summary>
    public static string CurrentSkillId => causeSkillId;

    /// <summary>What was in force before a push — restore it in a finally.</summary>
    public readonly struct Cause
    {
        public readonly ulong chain; public readonly string skillId;
        public Cause(ulong chain, string skillId) { this.chain = chain; this.skillId = skillId; }
    }

    /// <summary>Make <paramref name="mask"/> and <paramref name="skillId"/> the cause in force while a cast's effects
    /// resolve, returning the previous pair to restore. Paired with <see cref="Pop"/> around the moment effects are
    /// applied.</summary>
    public static Cause Push(ulong mask, string skillId)
    {
        var prev = new Cause(chain, causeSkillId);
        chain = mask; causeSkillId = skillId ?? "";
        return prev;
    }

    public static void Pop(Cause prev) { chain = prev.chain; causeSkillId = prev.skillId; }

    // A PASSIVE THAT DID NOT FIRE, AND WHY — "field_15 Alpha #1 skipped skill.sword.encore on OnKill — not switched on".
    // "It never fired" used to be provable only by a missing line, which cannot tell a passive that was turned away
    // from one that was never asked. Only the reasons a player could act on are written: a skill that simply does not
    // answer this moment is not a skip. Behind the switch, because moments like a landed hit fire per blow.
    static void Skipped(ICombatUnit actor, SkillDef def, SkillTrigger trigger, string why)
    {
        if (!GameLog.On(LogCat.Passive)) return;
        GameLog.Info(LogCat.Passive, string.Concat(new[]
        {
            SpawnIds.MapOf(actor), " ", SpawnIds.Label(actor), " skipped ", def.id, " on ", trigger.ToString(), " — ", why,
        }));
    }

    static void SkippedOutOfRange(ICombatUnit actor, SkillDef def, SkillTrigger trigger, Vector3 gap)
    {
        if (!GameLog.On(LogCat.Passive)) return;
        Skipped(actor, def, trigger, string.Concat("target ", gap.magnitude.ToString("0.##"), " units away, cast range ",
                                                   def.castRange.ToString("0.##")));
    }

    /// <summary>Fire a passive trigger AT <paramref name="actor"/> — the unit the moment happened to. Only that
    /// unit's own passives are considered. For <see cref="SkillTrigger.OnSkillCastComplete"/>,
    /// <paramref name="skillId"/> is the completed active's id and only the passive keyed to it runs; the natural
    /// events ignore it. <paramref name="victim"/> is the other party for outward-facing moments (null for
    /// self-only moments like a cast completing) — a passive's Single/Ground bindings land on it.</summary>
    public static void Fire(ICombatUnit actor, SkillTrigger trigger, string skillId = null, Transform victim = null)
    {
        if (PlayerUnits.Gone(actor)) return;
        // A WATCHED COPY ANSWERS NOTHING. The unit it stands for belongs to the server, which is where its passives
        // already ran; running them here as well would be this machine deciding a second time — a second cast, a
        // second buff, a second blow — off its own copy of numbers it was merely told.
        if (actor.IsWatchedCopy) return;
        var skills = actor.Skills;
        if (skills == null) return;

        int n = skills.Count < MaxTrackedPassives ? skills.Count : MaxTrackedPassives;

        // ONLY THE SKILLS THAT ANSWER THIS MOMENT. Their positions were worked out once, when this unit's skill list
        // was bound, so a blow no longer asks every skill in the catalog whether it cares — it asks the few that
        // authored this trigger.
        var slots = skills.SlotsFor(trigger);
        for (int s = 0; s < slots.Length; s++)
        {
            int i = slots[s];
            if (i < 0 || i >= n) continue;
            // THE AUTHORED SKILL, not its component. Every question below is answered off the asset, so a skill that
            // turns out not to answer this moment — or that this person has not switched on — never costs a component.
            var def = skills.DefAt(i);
            if (def == null) continue;
            // WHICH authored entry answers this moment (null = this skill doesn't). A skill may carry several triggers,
            // so the moment picks the entry, and that entry — not the skill — owns the gate and the stack cost below.
            // OnSkillCastComplete is parameterized inside EntryFor: only an entry bound to THIS skill id matches.
            var entry = def.EntryFor(trigger, skillId);
            if (entry == null) continue;
            // THE OWNERSHIP GATE: a passive only RUNS when its unit says it is on — a person ticked it on and is
            // holding the weapon it belongs to; a creature's list is what its own mastery opened, so all of it is
            // on. Asked of THIS unit, not of whoever happens to be at a keyboard.
            if (!actor.PassiveIsOn(def)) { Skipped(actor, def, trigger, "not switched on"); continue; }

            // THE SAME REACH ITS PRESSED FORM HAS. A skill whose manual cast makes you PICK a target can only pick
            // one inside its cast range — the picker is built with that number. Fired by a moment instead of by a
            // press, it had no range at all, so the same skill reached as far as whatever moment produced it: an
            // auto-fired Lunge answered a target picked up at fourteen units when pressing Lunge cannot reach past
            // eight. One skill, two reaches, and the longer one is the one nobody authored.
            //
            // Only skills that ask for a PICK are bound by this, because they are the only ones whose pressed form
            // is range-limited at all. A skill that lands on whoever the moment names — every passive that answers a
            // hit you just dealt or took — is untouched, and so is any moment that names nobody.
            if (victim != null && def.NeedsUnitPick && def.castRange > 0f)
            {
                var from = actor.Body;
                if (from != null)
                {
                    Vector3 gap = victim.position - from.position; gap.y = 0f;
                    if (gap.sqrMagnitude > def.castRange * def.castRange) { SkippedOutOfRange(actor, def, trigger, gap); continue; }
                }
            }

            // THE BRAKE: skip a passive already in this causal chain (it, or something it caused, led here). Unrelated
            // passives are untouched — "passive A kills, passive B reacts" stays legal.
            ulong bit = 1UL << i;
            if ((chain & bit) != 0UL) { Skipped(actor, def, trigger, "already part of this chain"); continue; }

            // THE GATE: a trigger that requires a state doesn't run at all when unmet — no VFX, no cast. Asked of the
            // caster's own store rather than through a component, so an unmet gate also costs nothing to build.
            if (!StateGate.IsMet(entry, actor.States))
            {
                Skipped(actor, def, trigger, "its required state is not met");
                continue;
            }

            // ONLY NOW is the component built, if this is the first time this person has fired this passive.
            var r = skills.RunnerAt(i);
            if (r == null || !r) continue;

            // A PASSIVE ACTUALLY FIRING — the moment a play-test could not see. Every line above this one is a
            // reason NOT to fire, so reaching here is the event worth writing down. Behind the switch because
            // this is the per-hit path: with the category off it costs one bit test and builds no string.
            if (GameLog.On(LogCat.Passive))
            {
                // HOW FAR AWAY IT WAS, said with the name. A trigger-fired skill has no range limit of its own, so
                // "it fired" answers nothing on its own — the question is always whether it fired from where the
                // player thought it would. Only built when the category is on, and only when there is a body and a
                // victim to measure between.
                string how = "";
                if (victim != null)
                {
                    // perf-ok: only with the passive category on, and only when a passive actually fires
                    var victimUnit = victim.GetComponent<ICombatUnit>();
                    string victimName = victimUnit != null ? SpawnIds.Label(victimUnit) : victim.name;
                    var from = actor.Body;
                    if (from != null)
                    {
                        Vector3 d = victim.position - from.position; d.y = 0f;
                        how = string.Concat(" at ", victimName, ", ", d.magnitude.ToString("0.##"), " units away");
                    }
                    else how = string.Concat(" at ", victimName);
                }
                GameLog.Info(LogCat.Passive, string.Concat(
                    SpawnIds.MapOf(actor), " ", SpawnIds.Label(actor), " fired ", def.id, " on ", trigger.ToString(), how));
            }

            r.FireAsPassive(entry, victim, chain | bit);   // fire the whole skill; the chain it hands to its own effects includes itself
        }
    }

    /// <summary>The fraction of full health at or under which a unit counts as badly hurt — the falling edge
    /// <see cref="SkillTrigger.OnLowHP"/> answers. ONE owner, because both damage doors ask it and a copy on either
    /// side would let a person and a creature disagree about what "nearly dead" means.</summary>
    public const float LowHpFrac = 0.3f;

    /// <summary>THE MOMENTS ONE LANDED HIT RAISES, for any unit alike — "damage landed on me", and "that hit dropped
    /// me under <see cref="LowHpFrac"/> of my health and left me alive". Called by BOTH damage doors, straight after
    /// the health has come off, with <paramref name="hpBefore"/> read immediately before it.
    ///
    /// <para><b>Why it is one function.</b> Both moments used to be raised inside the PLAYER's damage door and
    /// nowhere else, so a creature holding Last Stand or Composure never fired them at all — the rule that creatures
    /// get everything players get, broken by two lines living in the wrong file. Written once here, a third kind of
    /// unit inherits them by calling this.</para>
    ///
    /// <para>Nothing is raised for a hit that cost no health: a miss, a parry, a full barrier soak and a braced hit
    /// all reach the door with zero, and each of those is a different moment, or none. Damage over time does not come
    /// through here on either side.</para></summary>
    public static void HitLanded(ICombatUnit victim, int hpBefore, ICombatUnit attacker)
    {
        if (victim == null) return;
        var from = !PlayerUnits.Gone(attacker) ? attacker.Body : null;
        Fire(victim, SkillTrigger.OnDamageReceived, null, from);

        // FALLING EDGE ONLY: above the line before this hit, at or under it now, and still alive. Worked out from the
        // two readings rather than remembered, so it re-arms after any heal with no flag to reset.
        int thr = Mathf.CeilToInt(victim.MaxHP * LowHpFrac);
        if (hpBefore > thr && victim.CurrentHP > 0 && victim.CurrentHP <= thr)
            Fire(victim, SkillTrigger.OnLowHP);
    }
}
