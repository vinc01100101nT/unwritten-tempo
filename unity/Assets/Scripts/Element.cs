using UnityEngine;

/// <summary>
/// RO-style attack/armor PROPERTIES. Every attack carries an <see cref="Element"/> and every defender
/// has an armor element; <see cref="Elements.Modifier"/> looks up how effective one is against the other
/// (a multiplier on the final damage). This is the layer that makes "Fire melts the ice mob but fizzles
/// on the water one" real — the single defining RO combat system that the Physical/Magic split alone
/// couldn't express.
///
/// A DEFENDING element has a LEVEL, 1 to 4, and the level amplifies what it resists and what it is weak to —
/// Ragnarok's four pre-renewal tables, copied cell for cell. An ATTACK has no level. A negative value means the
/// hit HEALS the defender instead of hurting it (<see cref="Elements.ElementHeal"/>). Neutral against Neutral is
/// always 1.0, so an attack and a defender left at the default <see cref="Element.Neutral"/> are unaffected.
/// </summary>
public enum Element
{
    Neutral,
    Water,
    Earth,
    Fire,
    Wind,
    Poison,
    Holy,
    Dark,
    Ghost,
    Undead,
}

public static class Elements
{
    public const int Count = 10;

    /// <summary>The highest level a defending element can have.</summary>
    public const int MaxLevel = 4;

    /// <summary>The level an armour converter defends at. Decided 2026-09-23: converters are level 1 only — levels 2 to
    /// 4 belong to creatures' own nature.</summary>
    public const int ConverterLevel = 1;

    // THE CHART, in whole percent: Percent[level − 1, attack, defense]. Ragnarok's pre-renewal element tables
    // (rAthena db/pre-re/attr_fix.yml, sha256 f785222b…a7a6), copied cell for cell.
    // Rows = the ATTACK element, columns = the DEFENDING element, both in Element enum order. Whole percent so no
    // hand-typed decimal can drift from the source; a value below zero heals the defender.
    static readonly int[,,] Percent =
    {
        {
            // Level 1
            //             Neu   Wat   Ear   Fir   Win   Poi   Hol   Drk   Gho   Und
            /* Neu */ {  100,  100,  100,  100,  100,  100,  100,  100,   25,  100 },
            /* Wat */ {  100,   25,  100,  150,   50,  100,   75,  100,  100,  100 },
            /* Ear */ {  100,  100,  100,   50,  150,  100,   75,  100,  100,  100 },
            /* Fir */ {  100,   50,  150,   25,  100,  100,   75,  100,  100,  125 },
            /* Win */ {  100,  175,   50,  100,   25,  100,   75,  100,  100,  100 },
            /* Poi */ {  100,  100,  125,  125,  125,    0,   75,   50,  100,  -25 },
            /* Hol */ {  100,  100,  100,  100,  100,  100,    0,  125,  100,  150 },
            /* Drk */ {  100,  100,  100,  100,  100,   50,  125,    0,  100,  -25 },
            /* Gho */ {   25,  100,  100,  100,  100,  100,   75,   75,  125,  100 },
            /* Und */ {  100,  100,  100,  100,  100,   50,  100,    0,  100,    0 },
        },
        {
            // Level 2
            //             Neu   Wat   Ear   Fir   Win   Poi   Hol   Drk   Gho   Und
            /* Neu */ {  100,  100,  100,  100,  100,  100,  100,  100,   25,  100 },
            /* Wat */ {  100,    0,  100,  175,   25,  100,   50,   75,  100,  100 },
            /* Ear */ {  100,  100,   50,   25,  175,  100,   50,   75,  100,  100 },
            /* Fir */ {  100,   25,  175,    0,  100,  100,   50,   75,  100,  150 },
            /* Win */ {  100,  175,   25,  100,    0,  100,   50,   75,  100,  100 },
            /* Poi */ {  100,   75,  125,  125,  125,    0,   50,   25,   75,  -50 },
            /* Hol */ {  100,  100,  100,  100,  100,  100,  -25,  150,  100,  175 },
            /* Drk */ {  100,  100,  100,  100,  100,   25,  150,  -25,  100,  -50 },
            /* Gho */ {    0,   75,   75,   75,   75,   75,   50,   50,  150,  125 },
            /* Und */ {  100,   75,   75,   75,   75,   25,  125,    0,  100,    0 },
        },
        {
            // Level 3
            //             Neu   Wat   Ear   Fir   Win   Poi   Hol   Drk   Gho   Und
            /* Neu */ {  100,  100,  100,  100,  100,  100,  100,  100,    0,  100 },
            /* Wat */ {  100,  -25,  100,  200,    0,  100,   25,   50,  100,  125 },
            /* Ear */ {  100,  100,    0,    0,  200,  100,   25,   50,  100,   75 },
            /* Fir */ {  100,    0,  200,  -25,  100,  100,   25,   50,  100,  175 },
            /* Win */ {  100,  200,    0,  100,  -25,  100,   25,   50,  100,  100 },
            /* Poi */ {  100,   50,  100,  100,  100,    0,   25,    0,   50,  -75 },
            /* Hol */ {  100,  100,  100,  100,  100,  125,  -50,  175,  100,  200 },
            /* Drk */ {  100,  100,  100,  100,  100,    0,  175,  -50,  100,  -75 },
            /* Gho */ {    0,   50,   50,   50,   50,   50,   25,   25,  175,  150 },
            /* Und */ {  100,   50,   50,   50,   50,    0,  150,    0,  100,    0 },
        },
        {
            // Level 4
            //             Neu   Wat   Ear   Fir   Win   Poi   Hol   Drk   Gho   Und
            /* Neu */ {  100,  100,  100,  100,  100,  100,  100,  100,    0,  100 },
            /* Wat */ {  100,  -50,  100,  200,    0,   75,    0,   25,  100,  150 },
            /* Ear */ {  100,  100,  -25,    0,  200,   75,    0,   25,  100,   50 },
            /* Fir */ {  100,    0,  200,  -50,  100,   75,    0,   25,  100,  200 },
            /* Win */ {  100,  200,    0,  100,  -50,   75,    0,   25,  100,  100 },
            /* Poi */ {  100,   25,   75,   75,   75,    0,    0,  -25,   25, -100 },
            /* Hol */ {  100,   75,   75,   75,   75,  125, -100,  200,  100,  200 },
            /* Drk */ {  100,   75,   75,   75,   75,  -25,  200, -100,  100, -100 },
            /* Gho */ {    0,   25,   25,   25,   25,   25,    0,    0,  200,  175 },
            /* Und */ {  100,   25,   25,   25,   25,  -25,  175,    0,  100,    0 },
        },
    };

    /// <summary>How effective <paramref name="attack"/> is against a defender of <paramref name="defense"/> at
    /// <paramref name="defenseLevel"/> (1–4; anything outside is held to that range): 1.0 = normal, &gt;1 = weak to it,
    /// &lt;1 = resisted, 0 = immune, below 0 = the hit heals the defender.</summary>
    public static float Modifier(Element attack, Element defense, int defenseLevel) =>
        Percent[Mathf.Clamp(defenseLevel, 1, MaxLevel) - 1, (int)attack, (int)defense] / 100f;

    /// <summary>The chart's answer after a worn ward (a garment's Emberward, Tideward, Stoneward or Galeward) of
    /// <paramref name="ward"/> — a fraction — against the attack's element. A ward only ever cuts DAMAGE: an immune
    /// or a healing answer (0 or below) is left exactly as it is.</summary>
    public static float Warded(float modifier, float ward) =>
        modifier > 0f ? modifier * (1f - Mathf.Clamp01(ward)) : modifier;

    /// <summary>The health a defender GAINS from a hit its element turns into a heal (<paramref name="modifier"/> below
    /// zero): what the hit would have dealt after defence, times how far below zero the chart goes. Held to at least 1,
    /// the same floor a hit that lands has; defence bigger than the attack counts as nothing through, so it gives 1.</summary>
    public static int ElementHeal(float afterDefense, float modifier) =>
        Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0f, afterDefense) * -modifier));

    /// <summary>True if the matchup is notably strong (pops a "Weak!" cue over the target).</summary>
    public static bool IsStrong(float mod) => mod > 1.05f;
    /// <summary>True if the matchup is resisted or immune (pops a "Resist"/"Immune" cue).</summary>
    public static bool IsWeak(float mod) => mod < 0.95f;

    /// <summary>Opacity for the floating DAMAGE NUMBER, used INSTEAD of a word cue to read the matchup:
    /// strong/normal hits are full opaque, a resisted hit is dimmed to 70%, an immune (0×) hit to 30%.</summary>
    public static float DamageAlpha(float mod)
    {
        if (mod <= 0f) return 0.30f;   // immune
        if (IsWeak(mod)) return 0.70f; // resisted
        return 1.0f;                   // normal / super-effective
    }

    public static Color Tint(Element e)
    {
        switch (e)
        {
            case Element.Water:  return new Color(0.40f, 0.70f, 1.00f);
            case Element.Earth:  return new Color(0.70f, 0.55f, 0.35f);
            case Element.Fire:   return new Color(1.00f, 0.50f, 0.25f);
            case Element.Wind:   return new Color(0.55f, 0.95f, 0.70f);
            case Element.Poison: return new Color(0.65f, 0.45f, 0.85f);
            case Element.Holy:   return new Color(1.00f, 0.92f, 0.55f);
            case Element.Dark: return new Color(0.55f, 0.40f, 0.70f);
            case Element.Ghost:  return new Color(0.70f, 0.95f, 0.95f);
            case Element.Undead: return new Color(0.55f, 0.70f, 0.45f);
            default:             return new Color(0.62f, 0.62f, 0.66f);   // Neutral: a plain stone grey
        }
    }

    public static string Name(Element e) => e.ToString();

    /// <summary>The element's short face, drawn on a tile of its <see cref="Tint"/> the way a skill's two letters sit
    /// on its weapon's colour (<see cref="SkillSymbol"/>).</summary>
    public static string Symbol(Element e)
    {
        switch (e)
        {
            case Element.Water:  return "Wr";
            case Element.Earth:  return "Ea";
            case Element.Fire:   return "Fr";
            case Element.Wind:   return "Wd";
            case Element.Poison: return "Ps";
            case Element.Holy:   return "Ho";
            case Element.Dark:   return "Dk";
            case Element.Ghost:  return "Gh";
            case Element.Undead: return "Ud";
            default:             return "Ne";   // Neutral
        }
    }
}
