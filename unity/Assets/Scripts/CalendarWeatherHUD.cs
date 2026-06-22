using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 4 HUD widget: shows the in-game date + today's weather, e.g. "Spring · Day 4 / Light Rain".
/// Attach to the "HUD_Calendar" Text built by <see cref="UIShellBuilder"/>; it reads the two persistent
/// systems on the <c>Systems</c> layer — <see cref="GameCalendar.DateString"/> and
/// <see cref="WeatherDirector.CurrentName"/>.
///
/// <para><b>Perf:</b> like the rest of the HUD this is essentially event-rate. We can't subscribe to a
/// single event because the date changes on a day rollover (<see cref="GameCalendar"/>) while the weather
/// can ALSO change mid-day when DevSettings forces a preset (Phase 5). So we poll with two cheap compares
/// (a <c>long</c> day index + an <c>enum</c> preset) and only rebuild the text — the one allocating step,
/// incl. the enum's <c>ToString</c> — when one of them actually changes (≈ once per in-game day at runtime).
/// No string work, no <c>GetComponent</c>, no <c>Find*</c> on the steady-state frame.</para>
/// </summary>
[RequireComponent(typeof(Text))]
public class CalendarWeatherHUD : MonoBehaviour
{
    Text label;
    bool seeded;
    long lastDay;
    WeatherDirector.Preset lastWeather;

    void Awake() => label = GetComponent<Text>();

    void Update()
    {
        var cal = GameCalendar.Instance;
        if (cal == null) return;
        // Wait until the calendar has actually computed a date (its first Update produces DateString).
        // Without this the widget could latch onto the empty pre-seed value and never refresh.
        if (cal.DateString.Length == 0) return;

        var wx = WeatherDirector.Instance;
        var weather = wx != null ? wx.Current : WeatherDirector.Preset.Clear;

        // Hot-path guard: nothing to redraw unless the day rolled over or the weather changed.
        if (seeded && cal.DayIndex == lastDay && weather == lastWeather) return;

        lastDay = cal.DayIndex;
        lastWeather = weather;
        seeded = true;

        // Only runs on a real change, so the alloc here (string join) is not a hot path. Same "Sunny/Rainy/Snow"
        // tag vocabulary the full calendar panel uses, so the glanceable widget and the panel always agree.
        label.text = cal.DateString + "\n" + WeatherDirector.TagOf(weather);
        label.color = SeasonTint(cal.CurrentSeason);
    }

    // A gentle per-season tint so the date reads at a glance (spring green … winter ice-blue).
    static Color SeasonTint(GameCalendar.Season s)
    {
        switch (s)
        {
            case GameCalendar.Season.Spring: return new Color(0.70f, 1.00f, 0.70f);
            case GameCalendar.Season.Summer: return new Color(1.00f, 0.95f, 0.50f);
            case GameCalendar.Season.Autumn: return new Color(1.00f, 0.75f, 0.45f);
            case GameCalendar.Season.Winter: return new Color(0.70f, 0.90f, 1.00f);
            default:                         return Color.white;
        }
    }
}
