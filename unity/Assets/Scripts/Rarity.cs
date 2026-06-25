using UnityEngine;

/// <summary>
/// The five item-rarity tiers (locked in the GPT⇄Claude HUD relay). Rarity is ONE
/// visual channel — the slot BORDER color — and nothing else: enhancement (+N) and
/// card pips have their own channels and never tint the rarity border. Legendary is
/// GOLD (not Diablo orange) to fit the "unwritten" tone + the +N enhancement language.
/// </summary>
public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

/// <summary>Per-rarity presentation (border / name-text / soft glow color). The single
/// source of truth for rarity coloring — uGUI slots, tooltips, and the inventory grid
/// all read from <see cref="Of"/> so the channel stays consistent everywhere.</summary>
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
        new Style("Legendary", Rgb(245, 185, 66),  Rgb(255, 226, 166), Rgb(245, 185, 66, 0.40f)),
    };

    public static Style Of(Rarity r) => Table[(int)r];
    public static Color Border(Rarity r) => Table[(int)r].Border;
    public static Color Text(Rarity r) => Table[(int)r].Text;
    public static Color Glow(Rarity r) => Table[(int)r].Glow;
    public static string Label(Rarity r) => Table[(int)r].Label;
}
