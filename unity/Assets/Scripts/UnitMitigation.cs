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
/// </summary>
public static class UnitMitigation
{
    static readonly Color ShieldAbsorbColor = new Color(0.5f, 0.8f, 1f);
    static readonly Color ParryColor = new Color(1f, 0.95f, 0.6f);   // its own pale-gold word — a parry must be readable in PvP
    static readonly Color BraceColor = new Color(0.6f, 0.8f, 1f);    // delayed, not avoided — a cold number for a bill not yet paid

    /// <summary>Spend a parry charge on an incoming hit, if the unit holds one: the hit is negated OUTRIGHT and a
    /// "PARRY" word pops so both sides can read it. Returns true when the hit was erased — callers must then bail
    /// BEFORE any damage number, spark, or HP change (a parried hit didn't happen; it isn't a 0-damage hit).
    ///
    /// <para>Deliberately NOT part of the <see cref="Guarded"/>/<see cref="AfterShield"/> pipeline: those two are
    /// mitigation MATH on a hit that lands, while a parry cancels the landing itself. Shared by the player and every
    /// mob so the read behaves identically on both — only the fire-site of <see cref="SkillTrigger.OnParry"/> is
    /// player-side (passives are the player's).</para></summary>
    public static bool TryParry(UnitStates states, Vector3 popAt)
    {
        if (states == null || states.ParryCharges <= 0) return false;
        states.Consume(StateId.Parry, 1);
        DamagePopup.SpawnText(popAt, "PARRY", ParryColor);
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

    /// <summary>Drain the Shield barrier before the hit reaches HP, popping a pale-cyan number for the soaked part so
    /// the absorb is visible on whoever is wearing it. Returns what still gets through. Zero-alloc (int math + the
    /// pooled <see cref="DamagePopup"/>).</summary>
    public static int AfterShield(UnitStates states, int dmg, Vector3 popAt)
    {
        if (states == null || dmg <= 0) return dmg;
        int soaked = states.DrainShield(dmg);   // drops + clears its icon when the barrier breaks
        if (soaked <= 0) return dmg;
        DamagePopup.Spawn(popAt, soaked, ShieldAbsorbColor);
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
    public static int Deferred(UnitStates states, int dmg, Vector3 popAt)
    {
        if (states == null || dmg <= 0 || !states.AddDeferredHit(dmg)) return dmg;
        DamagePopup.Spawn(popAt, dmg, BraceColor);
        return 0;
    }
}
