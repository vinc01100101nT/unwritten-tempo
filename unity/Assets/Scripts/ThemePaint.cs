using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Remembers which interface palette row (<see cref="UiKit.Row"/>) an element was painted with, so
/// <see cref="UiKit.SetDarkMode"/> can paint it again in the other set. Added only by <see cref="UiKit.Paint{T}(T, ThemeColor)"/>
/// and <see cref="UiKit.PaintReadable{T}(T, Color, ThemeColor)"/>; never by hand. It has no Update: it runs only when
/// the element is painted and when the theme switches.
/// </summary>
[DisallowMultipleComponent]
public sealed class ThemePaint : MonoBehaviour
{
    enum Kind { None, Row, Readable, ReadableRow }

    Graphic graphic;
    Kind kind;
    UiKit.Row row;
    Color meaning;
    UiKit.Row meaningRow;
    // Below 0: the row's own alpha. Otherwise the alpha the element is drawn at (a see-through window body).
    float alpha = -1f;
    // The colour this last gave the element. Anything else on it now means a screen painted it with something that is
    // no theme row (a weather wash, a rarity tint) — the theme leaves it alone from then on.
    Color applied;
    bool hasApplied;

    internal void SetRow(Graphic g, UiKit.Row r)
    {
        graphic = g;
        kind = Kind.Row;
        row = r;
        alpha = -1f;
        Apply();
    }

    internal void SetAlpha(float a)
    {
        alpha = a;
        Apply();
    }

    // A colour that means something, kept readable against the palette row behind it.
    internal void SetReadable(Graphic g, Color m, UiKit.Row background)
    {
        graphic = g;
        kind = Kind.Readable;
        meaning = m;
        row = background;
        alpha = -1f;
        Apply();
    }

    // A theme row used as a meaning colour (a rarity), kept readable against the palette row behind it.
    internal void SetReadable(Graphic g, UiKit.Row m, UiKit.Row background)
    {
        graphic = g;
        kind = Kind.ReadableRow;
        meaningRow = m;
        row = background;
        alpha = -1f;
        Apply();
    }

    internal void Forget() => kind = Kind.None;

    /// <summary>Paint the element again in the theme in use now — when the theme switches.</summary>
    internal void Repaint()
    {
        if (graphic == null) return;
        if (hasApplied && graphic.color != applied) { kind = Kind.None; return; }
        Apply();
    }

    void Apply()
    {
        if (graphic == null) graphic = GetComponent<Graphic>();
        if (graphic == null) return;
        Color c;
        if (kind == Kind.Row) c = UiKit.Current(row);
        else if (kind == Kind.Readable) c = UiKit.Readable(meaning, UiKit.Current(row));
        else if (kind == Kind.ReadableRow) c = UiKit.Readable(UiKit.Current(meaningRow), UiKit.Current(row));
        else return;
        if (alpha >= 0f) c.a = alpha;
        graphic.color = c;
        applied = graphic.color;
        hasApplied = true;
    }
}

/// <summary>A row of the interface palette — what <see cref="UiKit.PanelFill"/>, <see cref="UiKit.TextMain"/> and the
/// rest are. Painting with one through <see cref="UiKit.Paint{T}(T, ThemeColor)"/> keeps it following the theme;
/// <see cref="Value"/> is its colour in the theme in use now, for anything that only needs the colour.</summary>
public readonly struct ThemeColor
{
    public readonly UiKit.Row Row;
    public ThemeColor(UiKit.Row row) { Row = row; }
    public Color Value => UiKit.Current(Row);
    public static bool operator ==(ThemeColor a, ThemeColor b) => a.Row == b.Row;
    public static bool operator !=(ThemeColor a, ThemeColor b) => a.Row != b.Row;
    public override bool Equals(object o) => o is ThemeColor t && t.Row == Row;
    public override int GetHashCode() => (int)Row;
}
