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
    Def,          // dual: its ring % scales BOTH physical Defense and MagicDefense (no separate MDef node)
    Accuracy,     // % lift on Hit
    CastSpeed,    // % that SHORTENS the cast wind-up (Character.CastTimeMult)
    Evasion,
    AttackSpeed,
    Critical,     // one node → % to BOTH crit rate and crit damage
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
    /// The closed loop, clockwise from the top. Poles sit dead-opposite (index 0 vs 5):
    /// Def (top, "Light") ↔ MAtk (bottom, "Dark"). Accuracy/CastSpeed bridge the physical→magic
    /// transition; Critical/AtkSpd close the loop back to the top — the ring-harmonized layout.
    /// </summary>
    public static readonly StatId[] Ring =
    {
        StatId.Def,
        StatId.HP,
        StatId.Atk,
        StatId.Accuracy,
        StatId.CastSpeed,
        StatId.MAtk,
        StatId.MP,
        StatId.Evasion,
        StatId.Critical,
        StatId.AttackSpeed,
    };

    public static int Count => Ring.Length;

    // ---- ranks ----------------------------------------------------------------
    /// <summary>The most ranks a node takes. The ring stops at level 99 with 99 points, so three nodes can be full.</summary>
    public const int NodeCap = 33;
    /// <summary>What one rank is worth on every node: +1% (the Defence node: +1 point of hard defence).</summary>
    public const float PercentPerRank = 0.01f;

    /// <summary>The node's value (0.10 == +10%) at <paramref name="ranks"/>: 1% a rank, stopping at <see cref="NodeCap"/>.</summary>
    public static float Percent(StatId stat, int ranks) => PercentPerRank * Mathf.Clamp(ranks, 0, NodeCap);

    /// <summary><see cref="Percent"/> as the whole number a window shows — 0.104 reads as 10.</summary>
    public static int PercentPoints(StatId stat, int ranks) => Mathf.RoundToInt(Percent(stat, ranks) * 100f);

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
            case StatId.Accuracy:    return "ACC";
            case StatId.CastSpeed:   return "CAST";
            case StatId.Evasion:     return "EVA";
            case StatId.AttackSpeed: return "ASPD";
            case StatId.Critical:    return "CRIT";
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
            case StatId.Accuracy:
            case StatId.Critical:
            case StatId.AttackSpeed:
            case StatId.Evasion:
                return new Color(0.95f, 0.55f, 0.35f);   // physical-offense arc — warm
            case StatId.MAtk:
            case StatId.CastSpeed:
            case StatId.MP:
                return new Color(0.45f, 0.6f, 0.95f);     // magic arc — blue
            default: // HP, Def
                return new Color(0.5f, 0.8f, 0.5f);       // survivability bridge — green
        }
    }
}
