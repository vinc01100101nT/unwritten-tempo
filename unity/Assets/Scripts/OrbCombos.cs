using System.Collections.Generic;

/// <summary>
/// WHICH STAFF COMBINATION a unit's orbs make, and whether it may cast it — the one place Release asks.
///
/// <para>A combination is never learned with points. It may be cast when a Staff is in hand, the unit's three held
/// orbs are exactly its combination, and — for a pure combination (an ultimate) — that orb has reached
/// <see cref="OrbMath.UltimateOrbLevel"/>. Read by the server before a cast and by the player's own screen to choose
/// which icon the Release button shows; neither works out a number.</para>
/// </summary>
public static class OrbCombos
{
    // Every combination by its count key, gathered the first time it is asked and forgotten when the catalog is.
    static Dictionary<int, SkillDef> byKey;

    /// <summary>Forget the combinations, so a skill authored since is seen. The skill catalog calls this on reload.</summary>
    public static void Forget() => byKey = null;

    static void EnsureLoaded()
    {
        if (byKey != null) return;
        byKey = new Dictionary<int, SkillDef>();
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var def = all[i];
            if (def == null || !def.IsOrbCombo) continue;
            int key = OrbMath.KeyOf(def.orbCombo);
            if (key >= 0 && !byKey.ContainsKey(key)) byKey.Add(key, def);
        }
    }

    /// <summary>The combination <paramref name="unit"/>'s held orbs make, or null when they hold fewer than three.</summary>
    public static SkillDef ComboFor(ICombatUnit unit)
    {
        var states = unit != null ? unit.States : null;
        if (states == null) return null;
        states.OrbCounts(out int f, out int e, out int w, out int a);
        if (f + e + w + a != OrbMath.Slots) return null;
        EnsureLoaded();
        return byKey.TryGetValue(OrbMath.Key(f, e, w, a), out var def) ? def : null;
    }

    /// <summary>The orb a pure combination is made of, or None for a mixed one.</summary>
    public static OrbElement PureOrb(SkillDef combo) =>
        combo != null && OrbMath.IsPure(combo.orbCombo) ? OrbMath.FromLetter(combo.orbCombo[0]) : OrbElement.None;

    /// <summary>True when <paramref name="unit"/> has opened <paramref name="combo"/>: every mixed combination is open
    /// from the start; a pure one opens at its orb's level 20.</summary>
    public static bool Opened(ICombatUnit unit, SkillDef combo)
    {
        if (combo == null || !combo.IsOrbCombo) return false;
        var pure = PureOrb(combo);
        return pure == OrbElement.None || SkillEffectApplier.OrbLevel(unit, pure) >= OrbMath.UltimateOrbLevel;
    }

    /// <summary>May <paramref name="unit"/>, holding <paramref name="held"/>, cast <paramref name="combo"/> right now?</summary>
    public static bool MayCast(ICombatUnit unit, SkillDef combo, WeaponType held) =>
        combo != null && combo.IsOrbCombo && held == WeaponType.Staff
        && ReferenceEquals(ComboFor(unit), combo) && Opened(unit, combo);

    /// <summary>Why <paramref name="combo"/> cannot be cast, in one short sentence, or null when it can.</summary>
    public static string WhyNot(ICombatUnit unit, SkillDef combo, WeaponType held)
    {
        if (combo == null || !combo.IsOrbCombo) return null;
        string name = string.IsNullOrEmpty(combo.displayName) ? combo.id : combo.displayName;
        if (held != WeaponType.Staff) return name + " needs a Staff in hand.";
        if (!ReferenceEquals(ComboFor(unit), combo)) return "Your orbs do not make " + name + ".";
        if (!Opened(unit, combo))
        {
            var orb = SkillCatalog.Get(OrbMath.SkillIdOf(PureOrb(combo)));
            return "Not learned: " + name + " (" + (orb != null ? orb.displayName : "its orb") + " level "
                   + OrbMath.UltimateOrbLevel + ")";
        }
        return null;
    }

    /// <summary>Every combination's id — the runners a unit holding the Release needs.</summary>
    public static void CollectIds(HashSet<string> into)
    {
        if (into == null) return;
        EnsureLoaded();
        foreach (var kv in byKey) into.Add(kv.Value.id);
    }
}
