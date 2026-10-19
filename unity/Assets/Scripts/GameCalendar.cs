using System;
using UnityEngine;

/// <summary>
/// The world's CALENDAR: the real date in GMT+8 (<see cref="RealCalendar"/>), read off the <see cref="TimeOfDay"/>
/// clock's shared <see cref="TimeOfDay.DayIndex"/> (whole days since midnight, 1 January 2026, GMT+8). The season
/// is the month's, northern hemisphere: winter Dec–Feb, spring Mar–May, summer Jun–Aug, autumn Sep–Nov.
///
/// <para>Because it's a pure function of <see cref="TimeOfDay.DayIndex"/> — which every client derives the same
/// way from the server's clock — the date is identical on every machine with zero networking, and the
/// <see cref="WeatherDirector"/> can seed deterministic weather off the same day number. This component holds
/// no time of its own; it just watches for the day to roll over and fires <see cref="OnNewDay"/> /
/// <see cref="OnSeasonChange"/> so listeners (weather, HUD) react exactly once per change.</para>
///
/// <para>Lives on the persistent <c>Systems</c> layer next to <see cref="TimeOfDay"/> (added by
/// <c>SystemsSceneBuilder</c>). Per-frame cost is a single long compare; it only recomputes on a day boundary.</para>
/// </summary>
[DisallowMultipleComponent]
public class GameCalendar : MonoBehaviour
{
    public static GameCalendar Instance { get; private set; }

    public enum Season { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

    /// <summary>The shared whole-day counter from the clock (since <see cref="TimeOfDay.Epoch"/>). 0 on 1 Jan 2026.</summary>
    public long DayIndex { get; private set; }
    /// <summary>Today's date in GMT+8.</summary>
    public DateTime Date { get; private set; }
    /// <summary>Which of the four seasons it currently is.</summary>
    public Season CurrentSeason { get; private set; }

    /// <summary>HUD-friendly date, e.g. "Oct 19, 2026". Built only on a day change (not per frame).</summary>
    public string DateString { get; private set; } = "";

    /// <summary>Fired once when the day rolls over (also fires once on first run to seed listeners). Arg = new DayIndex.</summary>
    public event Action<long> OnNewDay;
    /// <summary>Fired once when the season changes (and once on first run). Arg = the new season.</summary>
    public event Action<Season> OnSeasonChange;

    bool seeded;            // false until the first Recompute runs, so the first frame fires the seed events
    Season lastSeason;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        var tod = TimeOfDay.Instance;
        if (tod == null) return;

        long day = tod.DayIndex;
        // Hot-path guard: nothing changes within a day, so only recompute when the day actually rolls over
        // (or on the very first frame, to seed the date + notify listeners).
        if (seeded && day == DayIndex) return;

        bool firstRun = !seeded;
        Recompute(day);
        seeded = true;

        OnNewDay?.Invoke(day);
        if (firstRun || CurrentSeason != lastSeason) OnSeasonChange?.Invoke(CurrentSeason);
        lastSeason = CurrentSeason;
    }

    void Recompute(long day)
    {
        DayIndex      = day;
        Date          = RealCalendar.DateOf(day);
        CurrentSeason = RealCalendar.SeasonOf(Date.Month);
        DateString    = RealCalendar.DateText(Date);
    }
}
