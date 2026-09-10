using UnityEngine;

/// <summary>
/// The five item-rarity tiers (locked in the GPT⇄Claude HUD relay). Rarity drives exactly two
/// things and nothing else: one VISUAL channel — the slot BORDER color, where enhancement (+N)
/// and card pips have their own channels and never tint it — and one GAMEPLAY channel, the output
/// lift a weapon of this rarity puts on the skills it granted (<see cref="Rarities.OutputLift"/>).
/// Legendary is GOLD (not Diablo orange) to fit the "unwritten" tone + the +N enhancement language.
/// </summary>
public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

/// <summary>Everything one rarity tier means: its presentation (border / name-text / soft glow color)
/// and its gameplay weight (<see cref="OutputLift"/>). The single source of truth for both — uGUI slots,
/// tooltips, and the inventory grid all read from <see cref="Of"/>, and the skill applier reads the lift
/// from here, so neither channel can be re-typed somewhere else and drift.</summary>
public static class Rarities
{
    public readonly struct Style
    {
        public readonly string Label;
        /// <summary>Border + tooltip-header color = the sole rarity signal.</summary>
        public readonly Color Border;
        /// <summary>Item-name text color (tooltips / selected-item panel).</summary>
        public readonly Color Text;
        /// <summary>Soft outer glow; alpha 0 = no glow (Common). Legendary is screenshot-worthy.</summary>
        public readonly Color Glow;

        public Style(string label, Color border, Color text, Color glow)
        { Label = label; Border = border; Text = text; Glow = glow; }
    }

    static Color Rgb(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

    // Hexes carried over verbatim from the locked prototype (src/hud/items.ts RARITY map).
    static readonly Style[] Table =
    {
        new Style("Common",    Rgb(75, 85, 99),    Rgb(203, 210, 224), Rgb(0, 0, 0, 0f)),
        new Style("Uncommon",  Rgb(70, 209, 127),  Rgb(168, 240, 197), Rgb(70, 209, 127, 0.28f)),
        new Style("Rare",      Rgb(79, 140, 255),  Rgb(188, 212, 255), Rgb(79, 140, 255, 0.30f)),
        new Style("Epic",      Rgb(168, 85, 247),  Rgb(230, 198, 255), Rgb(168, 85, 247, 0.34f)),
        // Name = warm ivory (#F1E7D0) so it doesn't merge with the GOLD rarity LABEL/border (the label
        // carries the gold, the name carries the prestige — GPT⇄Claude HUD relay, 2026-06-25).
        new Style("Legendary", Rgb(245, 185, 66),  Rgb(241, 231, 208), Rgb(245, 185, 66, 0.40f)),
    };

    public static Style Of(Rarity r) => Table[(int)r];
    public static Color Border(Rarity r) => Table[(int)r].Border;
    public static Color Text(Rarity r) => Table[(int)r].Text;
    public static Color Glow(Rarity r) => Table[(int)r].Glow;
    public static string Label(Rarity r) => Table[(int)r].Label;

    // The rarity ladder, as fractions. It ACCELERATES on purpose — each tier is a bigger step up than the last, so
    // a Legendary reads as a real prize rather than the fifth notch on a straight line.
    static readonly float[] Lifts = { 0f, 0.03f, 0.09f, 0.18f, 0.30f };

    /// <summary>How much a weapon of this rarity lifts the OUTPUT of the skills it granted: +0% Common, +3% Uncommon,
    /// +9% Rare, +18% Epic, +30% Legendary.
    ///
    /// <para>It is ADDED to the ring's affinity term, never multiplied onto the finished number. That is the whole
    /// reason it lives as a plain fraction here: added, a Legendary is worth the same +30 POINTS to a new character
    /// and to a maxed one; multiplied, it would pay out more the more ring a player had already built, so the same
    /// drop would be worth far more to a veteran than to the person who needs it.</para>
    ///
    /// <para>Only a skill's OUTPUT takes it — damage, execute power, a heal's HP, a shield's pool, an ailment's damage
    /// per tick, how far a sub-stat buff or debuff moves, and what one outgoing-damage charge is worth. Never a
    /// duration (a Legendary must not hold a longer stun), never a proc chance (it is clamped to 100%, so lifting it
    /// saturates and then does nothing), never a hit or cleanse count (whole numbers: dead until they jump a whole
    /// extra unit), and never a shove's speed, which is movement rather than power.</para>
    ///
    /// <para>It does NOT require the skill to author any ring weights. The ring's term is exactly 1 when an effect
    /// declares none, and this is ADDED to it — so a weightless skill on a Legendary is still lifted the full 30%.</para></summary>
    public static float OutputLift(Rarity r) => Lifts[(int)r];
}
