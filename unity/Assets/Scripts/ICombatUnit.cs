using UnityEngine;

/// <summary>
/// ANY combatant in the game — the player's <see cref="Character"/> or a mob's <see cref="Health"/> — behind ONE
/// handle. This is the type every gameplay effect targets, so a skill effect never has to ask "is this the player or a
/// mob"; it asks the unit. Extends <see cref="IDamageable"/> (which already unified the damage sink) with the rest of
/// what an effect needs: the state store, the heal sink, the HP readout, and the scene handles.
///
/// <para><b>Why this exists.</b> The applier used to carry THREE near-identical switches — one for the caster, one for
/// a mob target, one for the player target — so every effect kind had to be implemented up to three times, and any
/// kind implemented in only one of them was silently unavailable on the other side. That is how "a stat buff can only
/// ever land on the caster" became a hard limit nobody chose: it was not a rule, it was a missing switch case. With one
/// handle there is ONE switch, and every effect is available to every entity by construction.</para>
///
/// <para>Deliberately NOT a base class: <see cref="Character"/> and <see cref="Health"/> already exist with different
/// lifetimes and different owners (the player's stats are a persistent data object, a mob's HP is on its body), and
/// nothing here needs shared storage — only a shared vocabulary.</para>
/// </summary>
public interface ICombatUnit : IDamageable
{
    /// <summary>This unit's durational buffs/debuffs. Already shared by both sides — the store never cared who owned
    /// it, which is why every state works on any unit the moment an effect can reach it.</summary>
    UnitStates States { get; }

    /// <summary>The unit's BODY in the scene — what a shove moves, what a facing turns, what a VFX parents to. Not
    /// always the same object the component sits on: the player's stats live on a persistent data object while the
    /// avatar is spawned separately (see memory "player-two-gameobjects-one-store").</summary>
    Transform Body { get; }

    /// <summary>WHICH MAP this unit is standing in, by scene name.
    ///
    /// <para>Asked because a world is several maps at once on the machine that owns it, and they all share one set of
    /// coordinates: a creature in the field and a person in the town can be a metre apart by the numbers while being
    /// nowhere near each other. Every question of the form "who is near me" has to be asked WITHIN a map or it
    /// answers across the whole world at once — which is how a town filled with a field's monsters that could walk
    /// at somebody and never reach them.</para></summary>
    string Map { get; }

    /// <summary>This unit's spawn_id — its number in the map it was numbered in, for telling two of the same creature
    /// apart in the server's log. Handed out by <see cref="SpawnIds"/> on the server; null on a player's machine, and
    /// on the server until the unit is announced.</summary>
    SpawnTag Spawn { get; set; }

    /// <summary>This unit's status host — the thing that holds its ailments, its action lock and its flinch. Exposed
    /// (and CACHED on both sides) because the damage door touches it on every landed hit, and a component lookup per
    /// hit per mob is exactly the per-frame cost PERFORMANCE.md bans.</summary>
    StatusController StatusHost { get; }

    /// <summary>This unit's OWN skills, in a fixed order — what a passive moment is dispatched against, what a cast is
    /// found by, and what gives each passive the bit that keeps it from firing off its own chain. Null for a unit
    /// that carries none (a creature with no weapon kit, a watched copy), which costs one reference check at the
    /// dispatch.
    ///
    /// <para>Held per unit rather than in one table because a server holds a character per person signed in and a
    /// kit per armed creature: a shared table would run out of bits, and an untracked passive is an unbraked one.</para></summary>
    UnitSkills Skills { get; }

    /// <summary>May this passive, which is in <see cref="Skills"/>, run right now? The ownership gate the passive
    /// dispatch asks, answered by whoever knows why the skill is on the list.</summary>
    bool PassiveIsOn(SkillDef def);

    /// <summary>What this unit's cast wind-ups are multiplied by — its DEX, its ring's cast speed and any cast speed
    /// window, through the one shared formula. 1 = the authored length.</summary>
    float CastTimeMult { get; }

    /// <summary>Spend mana if there is enough; false and nothing spent when short.</summary>
    bool SpendMana(int amount);

    /// <summary>True on a machine that only WATCHES this unit — the server holds the real one, so nothing here may
    /// decide anything about it.</summary>
    bool IsWatchedCopy { get; }

    /// <summary>Current hit points — read by the effects whose strength scales off how hurt the target is.</summary>
    int CurrentHP { get; }

    /// <summary>Hit points at full health.</summary>
    int MaxHP { get; }

    /// <summary>Health this unit restores on its own each second, from its VIT — before any timed regen change. The
    /// one per-unit ticker reads it here rather than asking what KIND of unit it is, so a mob heals out of combat by
    /// exactly the rule a player does.</summary>
    float HPRegenPerSecond { get; }

    /// <summary>Current mana.</summary>
    int CurrentMP { get; }

    /// <summary>Mana at a full pool. 0 on a unit that has no pool, which is what stops its regen.</summary>
    int MaxMP { get; }

    /// <summary>Mana this unit restores on its own each second, from its INT — before any timed regen change. The
    /// mana pool's mirror of <see cref="HPRegenPerSecond"/>, and read by the same shared ticker.</summary>
    float MPRegenPerSecond { get; }

    /// <summary>Restore mana, clamped to the pool.</summary>
    void GainMana(int amount);

    /// <summary>This unit's melee reach at FULL SIZE — its weapon's range, or its unarmed reach. Deliberately NOT
    /// scaled: contact distance depends on how big BOTH bodies are, so the caller blends it against the other unit
    /// (<see cref="UnitScale.MeleeBlend"/>). Storing a pre-scaled reach on one side is what let the player and a
    /// same-sized shadow mob disagree about the identical pairing.</summary>
    float MeleeReachFullSize { get; }

    /// <summary>The same reach WITHOUT anything a skill is lending — what this unit's gear alone gives it. Asked by
    /// the one caller that is placing a body rather than judging a blow: a dash lands you at striking distance from
    /// what you are hitting, and striking distance is a fact about your weapon. Reading the lent version there sent
    /// you as far past the target as the bonus was long, and further with every charge.</summary>
    float EquippedReachFullSize { get; }

    /// <summary>How big this unit's BODY is, for melee-contact maths — the shared character size for anything built to
    /// the <see cref="CharacterSpec"/> body, its size's multiple of that for a creature (<see cref="MobSizes"/>). Reported, never folded into a
    /// distance, so any measurement between two units blends the real pair.</summary>
    float MeleeScale { get; }

    /// <summary>Restore hit points (clamped at full, ignored when dead).</summary>
    void Heal(int amount);

    /// <summary>The fraction of the damage this unit's blows deal that comes back to it as health (0.1 = a tenth).
    /// Asked of the UNIT because life steal is a property of whoever swung, not of the player — a creature wearing a
    /// life-steal window used to get nothing from it, because the one place that pays it out asked for a
    /// <see cref="Character"/> and turned every other kind of attacker away.</summary>
    float LifeStealPct { get; }

    /// <summary>Restore hit points from LIFE STEAL — the same gain as <see cref="Heal"/> but silent, so a flurry does
    /// not bury the screen in green numbers. Separate verb rather than a flag, because the two are read by different
    /// things: a heal is an event a player is meant to see, a leech is bookkeeping.</summary>
    void LeechHeal(int amount);

    /// <summary>The last unit whose blow actually cost this one health, or null if nothing has. Recorded by the one
    /// damage door for every unit alike, so "who did this to me" has a single answer that cannot be forgotten at a
    /// call site — and so a kill can pay whoever earned it rather than whoever happens to be standing there.</summary>
    ICombatUnit LastAttacker { get; }

    /// <summary>Remember who just landed a blow — or hand the record back (null included) when that blow cost nothing.
    /// Called ONLY by the damage door.</summary>
    void NoteAttacker(ICombatUnit attacker);

    // ---- the BASIC SWING: what one ordinary attack from this unit is made of -------------------------------------
    // A skill that says "this strike resolves as a basic attack" asks the CASTER these, never the player: the skill
    // states the intent, the unit supplies the swing. Both halves live here so <see cref="BasicSwing"/> can be the one
    // implementation of "land one swing" for every unit that has one — see the player/NPC shared-behaviour rule.
    // Rolling is the unit's business (its crit rule, its weapon, its spread); the ORDER those rolls happen in, and
    // what a landed swing announces, belong to the shared function and must never be re-written per unit.

    /// <summary>Accuracy this unit swings with, blindness already taken off — rolled against the victim's
    /// <see cref="Flee"/>.</summary>
    int BasicHit { get; }

    /// <summary>The element this unit's ordinary attack carries (its weapon's, or Neutral unarmed).</summary>
    Element BasicElement { get; }

    /// <summary>Roll whether one swing at <paramref name="victim"/> crits, by the one crit rule every unit shares.
    /// Separate from the damage roll because a crit cannot be evaded, so accuracy needs this answer first.</summary>
    bool RollBasicCrit(ICombatUnit victim);

    /// <summary>Roll ONE ordinary swing's damage, crit already decided — this unit's own attack range, spread and
    /// damage buffs, and nothing about whichever skill asked for it.</summary>
    int RollBasicDamage(bool crit);

    /// <summary>This unit's dodge, rolled against an attacker's <see cref="BasicHit"/>.</summary>
    int Flee { get; }

    /// <summary>The LUK that lowers an attacker's crit chance when it swings at this unit — gear included.</summary>
    int CritShieldLuk { get; }

    // ---- a SKILL this unit uses: everything the applier reads off whoever is using it ----------------------------
    // Asked of the unit, never of the player at this keyboard, so a skill in a creature's hands scales off the
    // creature exactly as one in a player's hands scales off the player.

    /// <summary>The percentage this unit's ring adds to <paramref name="stat"/> (0.32 = +32%).</summary>
    float RingBonus(StatId stat);

    /// <summary>What this unit's level in <paramref name="skillId"/>, and its weapon's trademark, add to that skill's
    /// output, on top of the ring's term.</summary>
    float SkillOutputLift(string skillId);

    /// <summary>This unit's own level — what decides how challenging a fight with it was.</summary>
    int Level { get; }

    /// <summary>The weapon type in this unit's hands, or <see cref="WeaponType.None"/> when they are empty.</summary>
    WeaponType HeldWeapon { get; }

    /// <summary>The GRADE of the weapon in this unit's hands — what caps its weapon mastery and how many of each
    /// skill's levels count. Raw with empty hands, which is the lowest there is.</summary>
    Quality HeldQuality { get; }

    /// <summary>Seconds between this unit's swings at its current attack speed.</summary>
    float SwingTime { get; }

    /// <summary>Roll ONE skill strike's base damage from this unit's own numbers: magic attack for a
    /// <see cref="DamageType.Magic"/> strike (never a crit), otherwise its physical attack, critting by the shared rule
    /// against a defender with <paramref name="targetLuk"/> LUK — always for <see cref="DamageType.Crit"/>.</summary>
    int RollSkillDamage(DamageType type, int targetLuk, out bool crit);

    // ---- movement + aggro: the same verbs on every unit ---------------------------------------------------------
    // Each side drives a different rig (the player's controller vs a mob's pathing agent), but the CAPABILITY is
    // identical and named identically, so an effect never has to know which kind of thing it is moving.

    /// <summary>Blink instantly to <paramref name="dest"/> — no travel, no animation.</summary>
    void TeleportTo(Vector3 dest);

    /// <summary>Glide from here to <paramref name="dest"/> at <paramref name="speedMult"/> × this unit's own run
    /// speed. A fixed A→B move, not gated locomotion — it ignores the usual movement gates so a dash always travels.
    /// The ease flags shape the ramp (in = start slow, out = stop gently) over <paramref name="easeSeconds"/>.</summary>
    /// <param name="faceAt">A world point to face for the WHOLE trip, instead of turning to face the way the unit is
    /// travelling. A hop that lands beside what it is about to hit must arrive already looking at it, or the strike
    /// reads as a swing at empty air — and nothing must re-turn it on landing. Null = face the travel direction.</param>
    void GlideTo(Vector3 dest, float speedMult, bool easeIn, bool easeOut, float easeSeconds, Vector3? faceAt);

    /// <summary>True while this unit is mid-relocation — travelling a fixed A→B glide rather than walking. Lets a
    /// presentation effect last exactly as long as the hop does, without anyone having to author its length or guess
    /// it. False the instant it lands.</summary>
    bool IsRelocating { get; }

    /// <summary>Shove this unit directly AWAY from <paramref name="worldFrom"/> at <paramref name="speed"/> for
    /// <paramref name="seconds"/>, stunning its own steering for <paramref name="stagger"/>s so it doesn't fight the
    /// slide. Pull is the same verb aimed from a mirrored point.</summary>
    void Shove(Vector3 worldFrom, float speed, float seconds, float stagger);

    /// <summary>Force this unit to fixate on <paramref name="source"/> for <paramref name="seconds"/> — it engages
    /// that target and cannot drop it, leash away, or flee while the lock holds.</summary>
    void ForceAggro(ICombatUnit source, float seconds);

    /// <summary>Everything that is true of ANY unit starting a new life, in one body every revive calls: nothing timed
    /// survives the death that ended the last one. Every buff, debuff, barrier and charge is dropped, the run of hits
    /// is broken, and the stagger and the footing it had earned are cleared.
    ///
    /// <para><b>Why it lives on the interface.</b> The player and a mob revive through two different functions — one
    /// refills two pools and raises a screen, the other repaints a corpse and hands the body back to the simulation —
    /// and the shared half was written into only ONE of them, so a mob kept its buffs across death for as long as
    /// nobody noticed. Two revive paths is fine; two copies of the shared rule is the bug. There is one copy now, and a
    /// third kind of unit inherits it by existing.</para></summary>
    void BeginNewLife()
    {
        var st = States;
        if (st != null) { st.Clear(); st.NoteNewLife(); }
        var host = StatusHost;
        if (host != null) host.ClearFlinch();
    }
}
