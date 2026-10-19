using UnityEngine;
using TMPro;

/// <summary>
/// The top-right clock plate under the minimap: the world's time, large, in the seven-segment face
/// (<see cref="UiKit.ClockFont"/>), with AM/PM beside it; under it the date ("Oct 19, 2026") and today's weather
/// ("Sunny"). All three are the world's — the real time and date in GMT+8, off the server's clock
/// (<see cref="TimeOfDay"/>, <see cref="GameCalendar"/>, <see cref="WeatherDirector"/>).
/// Attach to the "HUD_Calendar" text built by <see cref="UIShellBuilder"/>, inside the "HUD_CalendarPanel" plate.
///
/// <para>The clock's two texts are built here when the game starts, never saved: the seven-segment face is made in
/// memory, and a saved text with no saved font rewrites itself in the editor (tools/check-scene-stays-clean.py).
/// The plate and the saved text are placed here too, so a plate saved before the clock existed lays out the same.</para>
///
/// <para><b>Perf:</b> polled with three cheap compares (the minute, the day index, the weather preset); the text —
/// the one allocating step — is only rewritten when one of them actually changes, about once a real minute.</para>
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class CalendarWeatherHUD : MonoBehaviour
{
    /// <summary>The plate's size at the HUD's reference resolution — UIShellBuilder saves it with this.</summary>
    public static readonly Vector2 PlateSize = new Vector2(150, 82);

    TMP_Text label, clock, half;
    bool seeded;
    int lastMinute = -1;
    long lastDay;
    WeatherDirector.Preset lastWeather;

    void Awake() => label = GetComponent<TMP_Text>();

    void Start()
    {
        var plate = transform.parent as RectTransform;
        if (plate == null) return;
        plate.sizeDelta = PlateSize;

        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = new Vector2(1, 0); lrt.pivot = new Vector2(0.5f, 0);
        lrt.sizeDelta = new Vector2(-8, 36); lrt.anchoredPosition = new Vector2(0, 6);

        clock = UiKit.MakeText("HUD_Clock", plate, "", 26, FontStyle.Normal, TextAnchor.MiddleRight, UiKit.ClockFont, false);
        clock.raycastTarget = false;
        var crt = clock.rectTransform;
        crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
        crt.offsetMin = new Vector2(8, -40); crt.offsetMax = new Vector2(-40, -6);

        half = UiKit.MakeText("HUD_ClockHalf", plate, "", 14, FontStyle.Bold, TextAnchor.LowerLeft, null, false);
        half.raycastTarget = false;
        UiKit.Paint(half, UiKit.TextDim);
        var hrt = half.rectTransform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(1, 1);
        hrt.sizeDelta = new Vector2(32, 30); hrt.anchoredPosition = new Vector2(-6, -8);
    }

    void Update()
    {
        var cal = GameCalendar.Instance;
        var tod = TimeOfDay.Instance;
        if (cal == null || tod == null) return;
        // Wait until the calendar has actually computed a date (its first Update produces DateString).
        // Without this the widget could latch onto the empty pre-seed value and never refresh.
        if (cal.DateString.Length == 0) return;

        var wx = WeatherDirector.Instance;
        var weather = wx != null ? wx.Current : WeatherDirector.Preset.Clear;
        int minute = tod.MinuteOfDay;

        if (seeded && minute == lastMinute && cal.DayIndex == lastDay && weather == lastWeather) return;

        if (clock != null && minute != lastMinute)
        {
            clock.text = RealCalendar.ClockDigits(minute, out bool pm);
            half.text = pm ? "PM" : "AM";
        }
        lastMinute = minute;

        if (!seeded || cal.DayIndex != lastDay || weather != lastWeather)
        {
            // Same "Sunny/Rainy/Snow" tag vocabulary the full calendar panel uses, so the glanceable widget and the
            // panel always agree.
            label.text = cal.DateString + "\n" + WeatherDirector.TagOf(weather);
            UiKit.PaintReadable(label, SeasonTint(cal.CurrentSeason));
        }
        lastDay = cal.DayIndex;
        lastWeather = weather;
        seeded = true;
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
