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

    /// <summary>Apply the Guard stance's cut to a post-defence figure. Floors at 1 so a guarded hit still registers
    /// as a hit rather than silently vanishing.</summary>
    public static int Guarded(UnitStates states, int dmg)
    {
        if (states == null || dmg <= 0) return dmg;
        float cut = states.GuardCut;
        return cut > 0f ? Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - cut))) : dmg;
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
}
