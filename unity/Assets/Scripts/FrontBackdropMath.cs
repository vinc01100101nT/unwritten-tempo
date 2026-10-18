using UnityEngine;

/// <summary>
/// The sums behind the front screens' moving pictures (<see cref="FrontBackdrop"/>), kept out of the behaviour so
/// each can be checked by calling it with numbers.
/// </summary>
public static class FrontBackdropMath
{
    /// <summary>The size a picture of <paramref name="aspect"/> (width ÷ height) is drawn at so it covers
    /// <paramref name="area"/> completely at its own shape, then zoomed by <paramref name="zoom"/>.</summary>
    public static Vector2 CoverSize(Vector2 area, float aspect, float zoom)
    {
        Vector2 size = area.x / area.y > aspect
            ? new Vector2(area.x, area.x / aspect)
            : new Vector2(area.y * aspect, area.y);
        return size * zoom;
    }

    /// <summary>The whole drift a picture makes in the direction <paramref name="radians"/>, as long as it can be
    /// while its edges stay off the screen: the picture overhangs the screen by <paramref name="overhang"/> on each
    /// side, it starts half the drift one way and ends half the drift the other, and it keeps
    /// <paramref name="keep"/> of the overhang spare.</summary>
    public static Vector2 Drift(float radians, Vector2 overhang, float keep)
    {
        var dir = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        float reachX = Mathf.Abs(dir.x) > 1e-4f ? overhang.x / Mathf.Abs(dir.x) : float.MaxValue;
        float reachY = Mathf.Abs(dir.y) > 1e-4f ? overhang.y / Mathf.Abs(dir.y) : float.MaxValue;
        return dir * (2f * Mathf.Min(reachX, reachY) * (1f - keep));
    }

    /// <summary>Where the picture sits <paramref name="seconds"/> into a drift that lasts
    /// <paramref name="duration"/>: from minus half the drift to plus half, then it stays.</summary>
    public static Vector2 Offset(Vector2 drift, float seconds, float duration)
        => drift * (Mathf.Clamp01(seconds / duration) - 0.5f);

    /// <summary>How far a picture fading in over <paramref name="fade"/> seconds has come, 0 to 1, after
    /// <paramref name="seconds"/>.</summary>
    public static float FadeIn(float seconds, float fade) => Mathf.Clamp01(seconds / fade);

    /// <summary>One centimetre in screen pixels, at <paramref name="dpi"/> dots per inch; a screen that does not say
    /// its density (0) counts as 96.</summary>
    public static float CentimetrePixels(float dpi) => (dpi > 0f ? dpi : 96f) / 2.54f;

    /// <summary>One centimetre as a share of a screen <paramref name="width"/> by <paramref name="height"/> pixels,
    /// across and down.</summary>
    public static Vector2 CentimetreShare(int width, int height, float dpi)
    {
        float cm = CentimetrePixels(dpi);
        return new Vector2(cm / Mathf.Max(1, width), cm / Mathf.Max(1, height));
    }
}
