using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ten generic passive stats. These are <b>stable identities</b> — their enum
/// order is NOT the ring order (the ring order lives in <see cref="SkillRing.Ring"/>),
/// so we can re-tune the ring layout without renumbering anything that persists.
///
/// (For JS folks: think of this as a <c>const enum</c> of stat keys; the ring below is
/// a separate ordered array, the way you'd keep an object's keys stable but reorder a
/// display list.)
/// </summary>
public enum StatId
{
    Atk,
    MAtk,
    HP,
    MP,
    Def,
    MDef,
    Evasion,
    AttackSpeed,
    CritChance,
    CritDamage,
}

/// <summary>
/// The Trio-Ring data table (the canonical design doc:
/// "Skill System — Trio-Ring Passive Draft"). Pure static data + math, no Unity
/// objects — so it is trivially unit-testable and the draft logic stays content-free.
///
/// The whole "game design" of the MVP skill system is <b>the ordering of <see cref="Ring"/></b>
/// plus the soft-cap curve. Everything else is mechanical.
/// </summary>
public static class SkillRing
{
    /// <summary>
    /// The closed loop, clockwise. A stat's Trio = itself + its two array neighbors
    /// (wrapping at the ends, because it's a ring, not a line). One graceful transition
    /// (Def→Atk) and one deliberate seam (Evasion→MAtk) where the physical-offense arc
    /// meets the magic pole — see doc §5.
    /// </summary>
    public static readonly StatId[] Ring =
    {
        StatId.Atk,
        StatId.AttackSpeed,
        StatId.CritChance,
        StatId.CritDamage,
        StatId.Evasion,
        StatId.MAtk,
        StatId.MDef,
        StatId.MP,
        StatId.HP,
        StatId.Def,
    };

    public static int Count => Ring.Length;

    // ---- soft-cap diminishing returns ----------------------------------------
    // passive%(stat) = Cap * R / (R + K)   — never exceeds Cap, halves at R == K.
    // Global for the MVP (doc §7). CritChance gets a tighter ceiling because a
    // crit rate near 100% would trivialise combat — the one per-stat exception the
    // doc explicitly calls out.
    public const float DefaultCap = 0.80f;   // asymptotic ceiling (+80%)
    public const float DefaultK   = 40f;     // ranks to reach half the cap

    public static float CapFor(StatId stat)
        => stat == StatId.CritChance ? 0.50f : DefaultCap;

    public static float KFor(StatId stat) => DefaultK;

    /// <summary>The passive multiplier (e.g. 0.32 == +32%) granted by <paramref name="ranks"/>
    /// invested in <paramref name="stat"/>. This is the soft-cap curve; it asymptotes to the cap.</summary>
    public static float Percent(StatId stat, int ranks)
    {
        if (ranks <= 0) return 0f;
        float cap = CapFor(stat), k = KFor(stat);
        return cap * ranks / (ranks + k);
    }

    // ---- ring topology --------------------------------------------------------

    static readonly Dictionary<StatId, int> indexOf = BuildIndex();

    static Dictionary<StatId, int> BuildIndex()
    {
        var map = new Dictionary<StatId, int>(Ring.Length);
        for (int i = 0; i < Ring.Length; i++) map[Ring[i]] = i;
        return map;
    }

    public static int IndexOf(StatId stat) => indexOf[stat];

    /// <summary>The neighbor one step counter-clockwise (the "left" card).</summary>
    public static StatId LeftOf(StatId stat)
    {
        int i = indexOf[stat];
        return Ring[(i - 1 + Ring.Length) % Ring.Length];
    }

    /// <summary>The neighbor one step clockwise (the "right" card).</summary>
    public static StatId RightOf(StatId stat)
    {
        int i = indexOf[stat];
        return Ring[(i + 1) % Ring.Length];
    }

    /// <summary>The leader's Trio = [left, leader, right] — the sliding window of width 3.</summary>
    public static bool InTrio(StatId leader, StatId stat)
        => stat == leader || stat == LeftOf(leader) || stat == RightOf(leader);

    // ---- presentation ---------------------------------------------------------

    public static string ShortName(StatId stat)
    {
        switch (stat)
        {
            case StatId.Atk:         return "ATK";
            case StatId.MAtk:        return "M.ATK";
            case StatId.HP:          return "HP";
            case StatId.MP:          return "MP";
            case StatId.Def:         return "DEF";
            case StatId.MDef:        return "M.DEF";
            case StatId.Evasion:     return "EVA";
            case StatId.AttackSpeed: return "ASPD";
            case StatId.CritChance:  return "CRIT";
            case StatId.CritDamage:  return "C.DMG";
            default:                 return stat.ToString();
        }
    }

    /// <summary>The colour family for a stat's ring region — physical (warm) vs magic
    /// (blue) vs survivability (green) — used to tint the draft cards so the player can
    /// *see* where on the ring they're walking.</summary>
    public static Color TintFor(StatId stat)
    {
        switch (stat)
        {
            case StatId.Atk:
            case StatId.AttackSpeed:
            case StatId.CritChance:
            case StatId.CritDamage:
            case StatId.Evasion:
                return new Color(0.95f, 0.55f, 0.35f);   // physical-offense arc — warm
            case StatId.MAtk:
            case StatId.MDef:
            case StatId.MP:
                return new Color(0.45f, 0.6f, 0.95f);     // magic arc — blue
            default: // HP, Def
                return new Color(0.5f, 0.8f, 0.5f);       // survivability bridge — green
        }
    }
}
