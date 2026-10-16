using UnityEngine;

/// <summary>
/// The colours a player picks for their cape: the cloth, the trim, and — when the gradient is on — the colour the cloth
/// fades to at the hem. Packed into one short string ("RRGGBB,RRGGBB,RRGGBB,1"), which is what is stored in the emblem
/// row, sent to everyone who can see the wearer, and read back here; an empty string means "the cape's own colours".
/// Plain string work and nothing from the engine but <see cref="Color"/>, so the server checks a packed string with the
/// same code every screen reads it with.
/// </summary>
public struct CapeColours
{
    public Color cloth, trim, hem;
    public bool gradient;

    /// <summary>The longest a packed string can be: three six-digit colours, a flag and three commas.</summary>
    public const int PackedLength = 3 * 6 + 1 + 3;

    public string Pack() => string.Concat(Hex(cloth), ",", Hex(trim), ",", Hex(hem), ",", gradient ? "1" : "0");

    /// <summary>Read <paramref name="packed"/>; false (and nothing read) when it is empty or not exactly that shape.</summary>
    public static bool TryParse(string packed, out CapeColours colours)
    {
        colours = default;
        if (string.IsNullOrEmpty(packed) || packed.Length != PackedLength) return false;
        var parts = packed.Split(',');
        if (parts.Length != 4 || (parts[3] != "0" && parts[3] != "1")) return false;
        if (!FromHex(parts[0], out colours.cloth) || !FromHex(parts[1], out colours.trim) || !FromHex(parts[2], out colours.hem))
            return false;
        colours.gradient = parts[3] == "1";
        return true;
    }

    static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    static bool FromHex(string s, out Color c)
    {
        c = Color.black;
        if (s == null || s.Length != 6) return false;
        for (int i = 0; i < 6; i++)
        {
            char ch = s[i];
            bool hex = (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F') || (ch >= 'a' && ch <= 'f');
            if (!hex) return false;
        }
        return ColorUtility.TryParseHtmlString("#" + s, out c);
    }
}
