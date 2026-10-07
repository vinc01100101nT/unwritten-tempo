using System;
using System.Globalization;

/// <summary>
/// The arithmetic and the wording of a world event (see <see cref="WorldEvents"/>), as plain functions that take
/// numbers and return numbers or text — so they can be checked without standing the game up.
/// </summary>
public static class WorldEventMath
{
    /// <summary>An amount of experience under a multiplier: rounded to the nearest whole point, halves up, and never
    /// past the largest number the game counts experience in.</summary>
    public static int Multiplied(int experience, double multiplier)
    {
        if (experience <= 0 || !(multiplier > 0)) return 0;
        double exact = Math.Round(experience * multiplier, MidpointRounding.AwayFromZero);
        return exact >= int.MaxValue ? int.MaxValue : (int)exact;
    }

    /// <summary>A multiplier as a player reads it: "×1.5", "×2".</summary>
    public static string MultiplierText(double multiplier)
        => "×" + multiplier.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A length of time as a player reads it, its two largest units: "2 h 5 min", "45 min 30 s", "30 s",
    /// "3 d 4 h".</summary>
    public static string DurationText(double seconds)
    {
        long s = (long)Math.Max(0, Math.Round(seconds));
        long d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60, sec = s % 60;
        if (d > 0) return h > 0 ? d + " d " + h + " h" : d + " d";
        if (h > 0) return m > 0 ? h + " h " + m + " min" : h + " h";
        if (m > 0) return sec > 0 ? m + " min " + sec + " s" : m + " min";
        return sec + " s";
    }

    /// <summary>A duration a GM typed — a number and one unit, s, m, h or d ("90s", "30m", "2h", "1.5d") — in whole
    /// seconds. False for anything else, or for less than one second.</summary>
    public static bool TryParseDuration(string typed, out long seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(typed) || typed.Length < 2) return false;
        double unit;
        switch (char.ToLowerInvariant(typed[typed.Length - 1]))
        {
            case 's': unit = 1; break;
            case 'm': unit = 60; break;
            case 'h': unit = 3600; break;
            case 'd': unit = 86400; break;
            default: return false;
        }
        if (!double.TryParse(typed.Substring(0, typed.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture,
                             out var count) || !(count > 0)) return false;
        double total = Math.Round(count * unit);
        if (total < 1 || total > int.MaxValue) return false;
        seconds = (long)total;
        return true;
    }
}
