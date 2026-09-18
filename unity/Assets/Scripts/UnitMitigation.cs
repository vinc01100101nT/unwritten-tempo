using UnityEngine;

/// <summary>
/// The mitigation every unit shares between "damage has been rolled and reduced by defence" and "HP actually drops":
/// the Guard damage-cut, then the Shield absorb pool. ONE implementation, called by both the player
/// (<see cref="Character"/>) and every mob (<see cref="Health"/>) — the states already live on both sides in the
/// unified <see cref="UnitStates"/> store, and only the READ was player-only before this.
///
/// <para>Order matters and is fixed here so it can't drift between the two callers: <b>Guard cuts a percentage of the
/// incoming hit, then Shield soaks a flat pool out of what's left.</b> Both are HIT-only — damage-over-time bypasses
/// them (see the true-damage paths), which is why neither is applied inside the roll itself.</para>
///
/// <para><b>Nothing here draws.</b> Each of these DECIDES and reports what it did on an <see cref="Outcome"/>; the
/// machine that has somebody sitting in front of it draws that through <see cref="Show"/>. See the note on
/// <see cref="Outcome"/> for what went wrong while the two were one thing.</para>
/// </summary>
public static class UnitMitigation
{
    static readonly Color ShieldAbsorbColor = new Color(0.5f, 0.8f, 1f);
    static readonly Color ParryColor = new Color(1f, 0.95f, 0.6f);   // its own pale-gold word — a parry must be readable in PvP
    static readonly Color BraceColor = new Color(0.6f, 0.8f, 1f);    // delayed, not avoided — a cold number for a bill not yet paid
    static readonly Color GuardColor = new Color(0.78f, 0.85f, 1f);  // an off-hand shield turned the blow aside

    /// <summary>WHAT THE MITIGATIONS DID TO ONE BLOW — how much a barrier ate, how much a brace held back, and
    /// whether the blow was erased outright by a parry or an off-hand block.
    ///
    /// <para><b>Why deciding and drawing had to come apart.</b> Each of these used to pop its own word or number the
    /// instant it decided. That was right while one machine did both. It stopped being right the moment the SERVER
    /// became the machine that decides: a server suppresses every floating number it makes, so all four were being
    /// drawn into nothing, and the person they happened to was told only the figure that survived — which for a
    /// fully braced, fully soaked, parried or blocked hit is zero, and zero draws nothing at all. So a brace held a
    /// blow and the person saw no sign of it whatsoever.</para>
    ///
    /// <para>The deciding half now RECORDS this and the machine with a screen DRAWS it, through
    /// <see cref="Show"/> — one description of what happened, travelling to wherever there is somebody to show it
    /// to.</para></summary>
    public struct Outcome
    {
        /// <summary>Damage a Shield barrier ate before it could reach HP.</summary>
        public int soaked;

        /// <summary>Damage a Brace held back — postponed, not avoided; it comes due when the window ends.</summary>
        public int banked;

        /// <summary>A parry charge erased the blow outright.</summary>
        public bool parried;

        /// <summary>An off-hand shield turned the blow aside.</summary>
        public bool blocked;

        /// <summary>TRUE when nothing at all was mitigated, so a caller can skip the whole business for the
        /// ordinary blow that most blows are.</summary>
        public bool Nothing => soaked <= 0 && banked <= 0 && !parried && !blocked;
    }

    /// <summary>Draw what the mitigations did, over <paramref name="unit"/>'s head, in the same words and colours the
    /// game has always used for them. Called on the machine that has a screen — which is no longer the machine that
    /// decided any of it. Silent when nothing was mitigated, and silent on a server, where the popups suppress
    /// themselves.</summary>
    public static void Show(in Outcome o, Transform unit)
    {
        if (o.parried) DamagePopup.SpawnText(unit, "PARRY", ParryColor);
        if (o.blocked) DamagePopup.SpawnText(unit, "Guard", GuardColor);
        if (o.soaked > 0) DamagePopup.Spawn(unit, o.soaked, ShieldAbsorbColor);
        if (o.banked > 0) DamagePopup.Spawn(unit, o.banked, BraceColor);
    }

    /// <summary>Spend a parry charge on an incoming hit, if the unit holds one: the hit is negated OUTRIGHT. Returns
    /// true when the hit was erased — callers must then bail BEFORE any damage number, spark, or HP change (a parried
    /// hit didn't happen; it isn't a 0-damage hit), and record it on their <see cref="Outcome"/> so the word reaches
    /// a screen.
    ///
    /// <para>Deliberately NOT part of the <see cref="Guarded"/>/<see cref="AfterShield"/> pipeline: those two are
    /// mitigation MATH on a hit that lands, while a parry cancels the landing itself.</para>
    ///
    /// <para><b>THE PARRY MOMENT IS RAISED HERE</b>, at the one place a charge is ever spent, and it belongs to
    /// whoever spent it — so a creature holding a counter-passive answers its own parry exactly as a person does.
    /// It used to be raised only inside the player's damage door, which meant a creature's parry announced nothing
    /// to anybody. <paramref name="attacker"/> travels as the moment's other party, so a counter can aim back.</para></summary>
    public static bool TryParry(ICombatUnit unit, ICombatUnit attacker = null)
    {
        var states = unit != null ? unit.States : null;
        if (states == null || states.ParryCharges <= 0) return false;
        states.Consume(StateId.Parry, 1);
        PassiveEvents.Fire(unit, SkillTrigger.OnParry, null,
                           !PlayerUnits.Gone(attacker) ? attacker.Body : null);
        return true;
    }

    /// <summary>Turn a RAW post-defence figure into the damage that actually LANDS: scale it by the target's damage
    /// reduction — ONE signed number covering both protection and exposure — then round to a whole point, floored at 1
    /// so a hit that connects always registers. Takes a float on purpose, so the scaling happens BEFORE rounding
    /// rather than on an already-rounded number.
    ///
    /// <para>Protection and exposure used to be two multipliers applied in sequence (a Guard cut and a Vulnerability
    /// amplify). One number means 30% of each is exactly neutral, which is what a player reading their own screen
    /// expects, instead of the 9% net the two-multiplier order happened to produce.</para>
    ///
    /// <para><b>THE entry point for every damage path, player and mob alike.</b> Vulnerability used to be written by
    /// hand into each intake formula — six copies of one term — and a path that forgot it silently ignored the debuff
    /// with no error to catch it. That is exactly how the player ended up immune: <see cref="Character"/>'s formulas
    /// were written alongside <see cref="Health"/>'s and never picked the term up. Route new damage paths through here
    /// and they inherit it for free; don't reintroduce a hand-written multiply.</para></summary>
    public static int Landed(UnitStates states, float rawAfterDefence)
    {
        float mult = states != null ? states.IncomingMult : 1f;
        return Mathf.Max(1, Mathf.RoundToInt(rawAfterDefence * mult));
    }

    /// <summary>Drain the Shield barrier before the hit reaches HP. Returns what still gets through, and hands back
    /// how much was eaten so the caller can have it drawn wherever there is a screen. Zero-alloc (int math).</summary>
    public static int AfterShield(UnitStates states, int dmg, out int soaked)
    {
        soaked = 0;
        if (states == null || dmg <= 0) return dmg;
        soaked = states.DrainShield(dmg);   // drops + clears its icon when the barrier breaks
        if (soaked <= 0) return dmg;
        return dmg - soaked;
    }

    /// <summary>LAST step before HP: if a Brace CHARGE is armed, it is spent to hold this hit back — the damage is
    /// banked instead of dealt (returns 0) and the whole debt comes due when the window ends. One charge per hit,
    /// exactly like <see cref="TryParry"/>; the difference is only that a parry ERASES the hit while a brace
    /// POSTPONES it. Out of charges, the next hit lands normally.
    ///
    /// <para>Runs after every other mitigation on purpose — what gets banked is what would really have cost HP, so a
    /// Guard cut, a Vulnerability amplify or a Shield absorb each shrink the bill exactly as they shrink the hit.
    /// Returns <paramref name="dmg"/> untouched when nothing is armed.</para></summary>
    public static int Deferred(UnitStates states, int dmg, out int banked)
    {
        banked = 0;
        if (states == null || dmg <= 0 || !states.AddDeferredHit(dmg)) return dmg;
        banked = dmg;
        return 0;
    }
}
