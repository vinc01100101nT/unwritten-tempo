using System;
using UnityEngine;

/// <summary>
/// The game's single day/night clock: <b>the real time in GMT+8</b> (<see cref="RealCalendar"/>). Lives on the persistent
/// <c>Systems</c> layer so the light (<see cref="DayNightLight"/>) and the audio beds (<see cref="MusicDirector"/>
/// surroundings, <see cref="RainAudio"/>) share ONE source of time instead of each running its own. A day is 24 real
/// hours: day from 6 AM (sunrise) to 6 PM (sunset), night from 6 PM to 6 AM.
///
/// <para><b>Why real-clock-anchored:</b> in MMO play every client derives the SAME <see cref="time01"/> and
/// <see cref="DayIndex"/> from <see cref="Epoch"/> + the server's clock, so the world's time-of-day, calendar,
/// and (seeded) weather agree on every machine — and the world keeps advancing while logged out. The one part
/// that travels over the network is a GM's <c>/time</c> shift (<see cref="ShiftSeconds"/>). <see cref="GameCalendar"/> reads <see cref="DayIndex"/>; <see cref="WeatherDirector"/> seeds
/// off it.</para>
///
/// <para>It's also the in-game <b>test tuner</b>: untick <see cref="useRealClock"/> (or tick
/// <see cref="paused"/>) to DETACH from the wall clock, then drag <see cref="time01"/> to scrub, bump
/// <see cref="testSpeed"/> to fast-forward (override mode advances a synthetic clock so <see cref="DayIndex"/>
/// still ticks for testing day/season rollover), or right-click the component for the
/// "Jump ▶ Sunrise/Noon/Sunset/Midnight" shortcuts. <see cref="_readout"/> shows the live phase + clock.</para>
/// </summary>
[DisallowMultipleComponent]
public class TimeOfDay : MonoBehaviour
{
    public static TimeOfDay Instance { get; private set; }

    /// <summary>The fixed instant <see cref="DayIndex"/> counts from: midnight, 1 January 2026, GMT+8. MUST be identical
    /// on every client (it's the shared world epoch), so it's a hard-coded constant, not tunable.</summary>
    public static readonly DateTime Epoch = RealCalendar.EpochUtc;

    [Header("Real-world anchor")]
    [Tooltip("ON (production): derive the time-of-day from the real UTC clock + Epoch, so every client agrees. " +
             "OFF (dev): detach and run a synthetic clock you can scrub/fast-forward with the tuner below.")]
    public bool useRealClock = true;

    [Header("Live test tuner — detaches from the real clock")]
    [Tooltip("Pause the clock so you can scrub the slider below by hand (also detaches from real UTC).")]
    public bool paused = false;
    [Tooltip("Fast-forward multiplier — ONLY applies in override mode (real clock off or paused off-real). " +
             "Crank it to race through days/seasons while testing.")]
    public float testSpeed = 1f;
    [Range(0f, 1f)] [Tooltip("Time of day. 0 = sunrise (6 AM), 0.25 = noon, 0.5 = sunset (6 PM), 0.75 = midnight.")]
    public float time01 = 0f;
    [SerializeField] [Tooltip("Read-only live readout.")]
    string _readout = "";

    /// <summary>Seconds added to the world clock when working out the time of day — moved by a GM's <c>/time</c>
    /// on the server and told to every player there, so everyone's sky moves together. Zero until somebody moves it,
    /// and gone when that server restarts. Only the time of DAY reads it: cooldowns and respawns count from
    /// <see cref="WorldClock"/> untouched.</summary>
    public static double ShiftSeconds { get; private set; }

    /// <summary>Take the shift the server holds. On a server this is the command setting it; on a player's machine
    /// it is the server's word arriving.</summary>
    public static void TakeShift(double seconds) => ShiftSeconds = seconds;

    /// <summary>How far to move the clock, in seconds, so that <paramref name="elapsedSeconds"/> lands on
    /// <paramref name="target01"/> of the SAME day — never the next one, so the date and the weather picked for it
    /// stay put. Negative when the target is earlier in the day.</summary>
    public static double SecondsToReach(double elapsedSeconds, double dayLengthSeconds, double target01)
    {
        double intoDay = elapsedSeconds - Math.Floor(elapsedSeconds / dayLengthSeconds) * dayLengthSeconds;
        // The calendar day starts at midnight and time01 at sunrise; the fraction is kept in [0, 1) so asking for 1
        // cannot step into the next day.
        return RealCalendar.DayFraction(target01) * dayLengthSeconds - intoDay;
    }

    /// <summary>Seconds since <see cref="Epoch"/> as the time of day counts them: the world clock plus the shift.</summary>
    public static double ElapsedForTimeOfDay => WorldClock.SecondsSince(Epoch) + ShiftSeconds;

    /// <summary>Whole in-game days elapsed since <see cref="Epoch"/> — the shared, deterministic day counter the
    /// calendar and weather seed off. In real-clock mode it's <c>floor(secondsSinceEpoch / DayLengthSeconds)</c>,
    /// so it's identical on every client; in override mode a synthetic counter advances by <see cref="testSpeed"/>.</summary>
    public long DayIndex { get; private set; }

    /// <summary>Length of one full in-game day (day + night) in real seconds: a real day.</summary>
    public double DayLengthSeconds => RealCalendar.DaySeconds;

    /// <summary>Minutes past midnight on the world's clock, 0..1439 — what the HUD clock reads.</summary>
    public int MinuteOfDay => RealCalendar.MinuteOfDay(time01);

    // Synthetic seconds-since-epoch used in OVERRIDE mode (real clock off/paused) so DayIndex keeps ticking while
    // testing. Seeded from time01+DayIndex on pause so resuming continues from where you scrubbed (no jump).
    double overrideElapsed;

    /// <summary>Smooth daylight weight: 1 at noon (0.25), 0 at midnight (0.75), 0.5 at sunrise/sunset.</summary>
    public float Daylight01 => (Mathf.Cos((time01 - 0.25f) * Mathf.PI * 2f) + 1f) * 0.5f;
    /// <summary>Inverse of <see cref="Daylight01"/> — peaks (1) at midnight.</summary>
    public float Night01 => 1f - Daylight01;
    public bool IsDay => time01 < 0.5f;

    /// <summary>Day/night tint for the UNLIT screen-space weather overlays (snow/rain) so they darken to match
    /// the world at night instead of glowing white. White at full noon (no change), easing to <paramref
    /// name="nightTint"/> (a dim cool colour) at midnight. Those passes can't be lit by the sun/ambient the way
    /// world geometry is, so they multiply their colour by this instead.</summary>
    public Color WeatherTint(Color nightTint) => Color.Lerp(nightTint, Color.white, Daylight01);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        double dayLen = DayLengthSeconds;

        if (useRealClock && !paused)
        {
            // PRODUCTION: time comes from the SERVER's clock, so every client agrees. Reading the local clock
            // here meant a player could move the world's day and night by changing their computer's date, while
            // everyone else stayed where they were.
            //
            // AND IT WAITS RATHER THAN GUESSING. Until the machine running the world has said what time it is,
            // there is no answer to draw a sun from, and the local clock is not a stand-in for one — that is the
            // exact substitution this was taken away from. The sky holds where it is; a player who is not joined
            // is not in the world anyway, and the sign-in screen is over the top of it.
            if (!WorldClock.Synced) return;

            double elapsed = ElapsedForTimeOfDay;
            overrideElapsed = elapsed;                                   // keep synced so toggling to override doesn't jump
            DayIndex = RealCalendar.DayIndex(elapsed);
            time01 = RealCalendar.Time01(RealCalendar.DayFractionAt(elapsed));
        }
        else if (!paused)
        {
            // OVERRIDE (real clock off): advance a synthetic clock by testSpeed so DayIndex still rolls over for testing.
            overrideElapsed += Time.deltaTime * Mathf.Max(0f, testSpeed);
            DayIndex = RealCalendar.DayIndex(overrideElapsed);
            time01 = RealCalendar.Time01(RealCalendar.DayFractionAt(overrideElapsed));
        }
        else
        {
            // PAUSED: time01 is scrubbed by hand. Keep the synthetic clock consistent with it so an un-pause
            // continues from the scrubbed moment (within the current day) instead of snapping back.
            time01 = Mathf.Repeat(time01, 1f);
            overrideElapsed = (DayIndex + RealCalendar.DayFraction(time01)) * dayLen;
        }
#if UNITY_EDITOR
        // Inspector-only debug string — building it every frame allocates; a build has no inspector to show it.
        _readout = $"{Phase()}  ·  day {DayIndex}  ·  t={time01:0.000}  ·  {(IsDay ? "DAY" : "NIGHT")}  ·  light={Daylight01:0.00}";
#endif
    }

    string Phase()
    {
        if (time01 < 0.125f) return "Sunrise";
        if (time01 < 0.375f) return "Daytime";
        if (time01 < 0.500f) return "Sunset";
        if (time01 < 0.625f) return "Dusk";
        if (time01 < 0.875f) return "Night";
        return "Pre-dawn";
    }

    // Right-click the component header in the Inspector for these (they pause so the moment holds).
    [ContextMenu("Jump ▶ Sunrise (0.00)")]  void JumpSunrise()  => SetTime(0f);
    [ContextMenu("Jump ▶ Noon (0.25)")]     void JumpNoon()     => SetTime(0.25f);
    [ContextMenu("Jump ▶ Sunset (0.50)")]   void JumpSunset()   => SetTime(0.5f);
    [ContextMenu("Jump ▶ Midnight (0.75)")] void JumpMidnight() => SetTime(0.75f);

    void SetTime(float t) { time01 = Mathf.Repeat(t, 1f); paused = true; }

    /// <summary>Dev day-scrub: shift the clock by whole in-game days,
    /// keeping the current time-of-day. Detaches from real UTC first — the production clock recomputes
    /// <see cref="DayIndex"/> from the wall clock every frame, so it would instantly undo any offset.
    /// <see cref="GameCalendar"/>/<see cref="WeatherDirector"/> poll <see cref="DayIndex"/>, so the date +
    /// weather re-roll on the next frame. Clamps at the epoch so the day never goes negative.</summary>
    public void NudgeDays(int delta)
    {
        useRealClock = false;
        double dayLen = DayLengthSeconds;
        overrideElapsed = Math.Max(0.0, overrideElapsed + delta * dayLen);
        DayIndex = RealCalendar.DayIndex(overrideElapsed);
        time01 = RealCalendar.Time01(RealCalendar.DayFractionAt(overrideElapsed));
    }
}
