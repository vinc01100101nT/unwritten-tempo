using System;
using UnityEngine;

/// <summary>
/// The in-game CALENDAR layer that sits on top of the <see cref="TimeOfDay"/> clock. It turns the clock's
/// shared <see cref="TimeOfDay.DayIndex"/> (whole in-game days since the world <see cref="TimeOfDay.Epoch"/>)
/// into a date: a <b>4-season, 12-day year</b> (Spring → Summer → Autumn → Winter, 48 in-game days per year).
/// At the locked 2-hour day that's ≈24h real per season, ≈4 real days per year.
///
/// <para>Because it's a pure function of <see cref="TimeOfDay.DayIndex"/> — which every client derives the same
/// way from real UTC — the date is identical on every machine with zero networking, and the
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

    // --- Calendar shape (the locked design: 4 seasons × 12 days = a 48-day year) ---
    public const int DaysPerSeason = 12;
    public const int SeasonCount   = 4;
    public const int DaysPerYear   = DaysPerSeason * SeasonCount;   // 48

    public enum Season { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

    /// <summary>The shared whole-day counter from the clock (since <see cref="TimeOfDay.Epoch"/>). 0 on day one.</summary>
    public long DayIndex { get; private set; }
    /// <summary>Day within the current year, 0..47.</summary>
    public int DayOfYear { get; private set; }
    /// <summary>Day within the current season, 0..11.</summary>
    public int DayOfSeason { get; private set; }
    /// <summary>Which of the four seasons it currently is.</summary>
    public Season CurrentSeason { get; private set; }
    /// <summary>Years elapsed since the epoch (0-based).</summary>
    public long Year { get; private set; }

    /// <summary>HUD-friendly date, e.g. "Spring · Day 4". Built only on a day change (not per frame).</summary>
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
        // Floor-mod so negative day indices (clock scrubbed before the epoch) still wrap cleanly.
        long doy      = ((day % DaysPerYear) + DaysPerYear) % DaysPerYear;
        DayOfYear     = (int)doy;
        DayOfSeason   = DayOfYear % DaysPerSeason;
        CurrentSeason = (Season)(DayOfYear / DaysPerSeason);
        Year          = (long)Math.Floor((double)day / DaysPerYear);
        // 1-based day number reads more naturally to players than the 0-based index.
        DateString    = $"{CurrentSeason} · Day {DayOfSeason + 1}";
    }
}
