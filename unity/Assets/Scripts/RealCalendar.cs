using System;

/// <summary>
/// The world's clock and calendar ARE the real ones, in GMT+8: a day is 24 real hours, the date is today's date,
/// and the season is the month's (northern hemisphere). Every sum that turns the world's time into a day, a date,
/// a season or a clock reading lives here, as plain functions of numbers, so each can be checked on its own.
///
/// <para>Two ways of cutting a day are in play and must not be mixed up. The CALENDAR day runs midnight to midnight
/// (GMT+8) — <see cref="DayIndex"/> counts those, and the date, the weather and the season follow it. The SKY's
/// <c>time01</c> starts at sunrise: 0 is 6 AM, 0.25 noon, 0.5 6 PM, 0.75 midnight. <see cref="Time01"/> and
/// <see cref="DayFraction"/> convert between the two.</para>
/// </summary>
public static class RealCalendar
{
    /// <summary>The world's time zone, in hours ahead of UTC.</summary>
    public const int ZoneHours = 8;
    /// <summary>One day of the world: a real one.</summary>
    public const double DaySeconds = 86400.0;
    // Sunrise is 6 AM: the sky's time01 is the calendar day's fraction moved back by a quarter.
    const double SunriseFraction = 0.25;

    /// <summary>Day 0 of <see cref="DayIndex"/>: midnight, 1 January 2026, GMT+8.</summary>
    public static readonly DateTime EpochUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-ZoneHours);
    static readonly DateTime EpochDate = new DateTime(2026, 1, 1);

    static readonly string[] MonthShort = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    static readonly string[] MonthLong = { "January", "February", "March", "April", "May", "June", "July", "August",
                                           "September", "October", "November", "December" };

    /// <summary>Whole calendar days since <see cref="EpochUtc"/> at <paramref name="elapsedSeconds"/> past it.</summary>
    public static long DayIndex(double elapsedSeconds) => (long)Math.Floor(elapsedSeconds / DaySeconds);

    /// <summary>How far through its calendar day (from midnight) <paramref name="elapsedSeconds"/> is, in [0, 1).</summary>
    public static double DayFractionAt(double elapsedSeconds)
    {
        double into = elapsedSeconds - DayIndex(elapsedSeconds) * DaySeconds;
        return into / DaySeconds;
    }

    /// <summary>The sky's time01 (0 = sunrise) for a calendar-day fraction (0 = midnight).</summary>
    public static float Time01(double dayFraction) => (float)Wrap01(dayFraction - SunriseFraction);

    /// <summary>The calendar-day fraction (0 = midnight) for a sky time01 (0 = sunrise).</summary>
    public static double DayFraction(double time01) => Wrap01(time01 + SunriseFraction);

    /// <summary>Minutes past midnight, 0..1439, for a sky time01.</summary>
    public static int MinuteOfDay(double time01) => Math.Min(1439, (int)Math.Floor(DayFraction(time01) * 1440.0));

    /// <summary>The 12-hour clock reading for <paramref name="minuteOfDay"/>: "8:42", and whether it is PM.</summary>
    public static string ClockDigits(int minuteOfDay, out bool pm)
    {
        int h24 = minuteOfDay / 60;
        int m = minuteOfDay % 60;
        pm = h24 >= 12;
        int h12 = h24 % 12 == 0 ? 12 : h24 % 12;
        return h12 + ":" + (m < 10 ? "0" : "") + m;
    }

    /// <summary>The date of calendar day <paramref name="day"/>.</summary>
    public static DateTime DateOf(long day) => EpochDate.AddDays(day);

    /// <summary>The calendar day of <paramref name="date"/> (its time is ignored).</summary>
    public static long DayOf(DateTime date) => (long)Math.Round((date.Date - EpochDate).TotalDays);

    /// <summary>Northern-hemisphere seasons: winter Dec–Feb, spring Mar–May, summer Jun–Aug, autumn Sep–Nov.</summary>
    public static GameCalendar.Season SeasonOf(int month)
    {
        if (month == 12 || month <= 2) return GameCalendar.Season.Winter;
        if (month <= 5) return GameCalendar.Season.Spring;
        if (month <= 8) return GameCalendar.Season.Summer;
        return GameCalendar.Season.Autumn;
    }

    /// <summary>"Oct 19, 2026".</summary>
    public static string DateText(DateTime date) => MonthShort[date.Month - 1] + " " + date.Day + ", " + date.Year;

    /// <summary>"October 2026".</summary>
    public static string MonthTitle(int year, int month) => MonthLong[month - 1] + " " + year;

    /// <summary>The month before (<paramref name="step"/> −1) or after (+1) <paramref name="year"/>/<paramref name="month"/>.</summary>
    public static void StepMonth(ref int year, ref int month, int step)
    {
        int m = year * 12 + (month - 1) + step;
        year = m / 12;
        month = m % 12 + 1;
    }

    static double Wrap01(double v) => v - Math.Floor(v);
}
