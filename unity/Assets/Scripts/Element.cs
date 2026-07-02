using UnityEngine;

/// <summary>
/// RO-style attack/armor PROPERTIES. Every attack carries an <see cref="Element"/> and every defender
/// has an armor element; <see cref="Elements.Modifier"/> looks up how effective one is against the other
/// (a multiplier on the final damage). This is the layer that makes "Fire melts the ice mob but fizzles
/// on the water one" real — the single defining RO combat system that the Physical/Magic split alone
/// couldn't express.
///
/// Simplified to a single element LEVEL for the MVP (real RO has 4 levels per property with a bigger
/// chart); the SYSTEM is here and the table below is easy to re-tune. Neutral-vs-Neutral is always 1.0,
/// so any attack/defender left at the default <see cref="Element.Neutral"/> behaves exactly as before.
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
    Shadow,
    Ghost,
    Undead,
}

public static class Elements
{
    public const int Count = 10;

    // Damage multiplier table: T[attack, defense]. Approximates the RO level-1 armor chart, simplified
    // into clean 0 / 0.25 / 0.5 / 1.0 / 1.5 / 1.75 / 2.0 steps. Rows = the ATTACK element, columns = the
    // DEFENDER's armor element (order matches the Element enum). Tune freely — the gameplay is the chart.
    //                            Neu   Wat   Ear   Fir   Win   Poi   Hol   Sha   Gho   Und
    static readonly float[,] T = {
        /* Neutral */          { 1.00f,1.00f,1.00f,1.00f,1.00f,1.00f,1.00f,1.00f,0.25f,1.00f },
        /* Water   */          { 1.00f,0.50f,1.00f,1.50f,0.50f,1.00f,1.00f,1.00f,1.00f,1.00f },
        /* Earth   */          { 1.00f,1.00f,0.50f,0.50f,1.50f,1.00f,1.00f,1.00f,1.00f,1.00f },
        /* Fire    */          { 1.00f,0.50f,1.50f,0.50f,1.00f,1.25f,1.00f,1.00f,1.00f,1.75f },
        /* Wind    */          { 1.00f,1.50f,0.50f,1.00f,0.50f,1.00f,1.00f,1.00f,1.00f,1.00f },
        /* Poison  */          { 1.00f,1.00f,1.00f,1.00f,1.00f,0.50f,0.50f,1.00f,1.00f,0.00f },
        /* Holy    */          { 1.00f,1.00f,1.00f,1.00f,1.00f,1.25f,0.25f,1.50f,1.00f,2.00f },
        /* Shadow  */          { 1.00f,1.00f,1.00f,1.00f,1.00f,1.00f,1.50f,0.25f,1.00f,0.25f },
        /* Ghost   */          { 1.00f,1.00f,1.00f,1.00f,1.00f,1.00f,1.00f,1.00f,1.50f,1.00f },
        /* Undead  */          { 1.00f,1.00f,1.00f,1.00f,1.00f,1.00f,1.50f,1.00f,1.00f,0.25f },
    };

    /// <summary>How effective <paramref name="attack"/> is against an <paramref name="defense"/>-element
    /// defender: 1.0 = normal, &gt;1 = super-effective, &lt;1 = resisted, 0 = immune.</summary>
    public static float Modifier(Element attack, Element defense) => T[(int)attack, (int)defense];

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
            case Element.Shadow: return new Color(0.55f, 0.40f, 0.70f);
            case Element.Ghost:  return new Color(0.70f, 0.95f, 0.95f);
            case Element.Undead: return new Color(0.55f, 0.70f, 0.45f);
            default:             return Color.white;   // Neutral
        }
    }

    public static string Name(Element e) => e.ToString();
}
