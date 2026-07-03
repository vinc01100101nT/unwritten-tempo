using System;
using UnityEngine;

/// <summary>
/// The game's single day/night clock, <b>anchored to real-world UTC</b>. Lives on the persistent
/// <c>Systems</c> layer so the light (<see cref="DayNightLight"/>) and the audio beds (<see cref="MusicDirector"/>
/// surroundings, <see cref="RainAudio"/>) share ONE source of time instead of each running its own. The cycle is split evenly:
/// <see cref="halfCycleSeconds"/> of day (incl. sunrise) then the same of night (incl. sunset) — default
/// 3600s each = a 1-hour day + a 1-hour night = a <b>2-hour full cycle (12× real)</b>.
///
/// <para><b>Why real-UTC-anchored:</b> in MMO play every client derives the SAME <see cref="time01"/> and
/// <see cref="DayIndex"/> from <see cref="Epoch"/> + the system clock, so the world's time-of-day, calendar,
/// and (seeded) weather agree on every machine with zero networking — and the world keeps advancing while
/// logged out. <see cref="GameCalendar"/> reads <see cref="DayIndex"/>; <see cref="WeatherDirector"/> seeds
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

    /// <summary>Game "year zero" — the fixed UTC instant <see cref="DayIndex"/>/<see cref="time01"/> count from.
    /// MUST be identical on every client (it's the shared world epoch), so it's a hard-coded constant, not tunable.</summary>
    public static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Header("Cycle")]
    [Tooltip("Real seconds for ONE HALF of the cycle. 3600 = a 1-hour day + a 1-hour night = a 2-hour full " +
             "cycle (12× real). The full day length (this × 2) also defines how long an in-game DAY lasts.")]
    public float halfCycleSeconds = 3600f;

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
    [Range(0f, 1f)] [Tooltip("Time of day. 0 = sunrise, 0.25 = noon, 0.5 = sunset, 0.75 = midnight.")]
    public float time01 = 0f;
    [SerializeField] [Tooltip("Read-only live readout.")]
    string _readout = "";

    /// <summary>Whole in-game days elapsed since <see cref="Epoch"/> — the shared, deterministic day counter the
    /// calendar and weather seed off. In real-clock mode it's <c>floor(secondsSinceEpoch / DayLengthSeconds)</c>,
    /// so it's identical on every client; in override mode a synthetic counter advances by <see cref="testSpeed"/>.</summary>
    public long DayIndex { get; private set; }

    /// <summary>Length of one full in-game day (day + night) in real seconds — <see cref="halfCycleSeconds"/> × 2.</summary>
    public double DayLengthSeconds => Math.Max(1.0, halfCycleSeconds * 2.0);

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
            // PRODUCTION: time is a pure function of the real UTC clock + the shared Epoch, so every client agrees.
            double elapsed = (DateTime.UtcNow - Epoch).TotalSeconds;
            overrideElapsed = elapsed;                                   // keep synced so toggling to override doesn't jump
            DayIndex = (long)Math.Floor(elapsed / dayLen);
            time01 = (float)((elapsed - DayIndex * dayLen) / dayLen);    // mod into [0,1)
        }
        else if (!paused)
        {
            // OVERRIDE (real clock off): advance a synthetic clock by testSpeed so DayIndex still rolls over for testing.
            overrideElapsed += Time.deltaTime * Mathf.Max(0f, testSpeed);
            DayIndex = (long)Math.Floor(overrideElapsed / dayLen);
            time01 = (float)((overrideElapsed - DayIndex * dayLen) / dayLen);
        }
        else
        {
            // PAUSED: time01 is scrubbed by hand. Keep the synthetic clock consistent with it so an un-pause
            // continues from the scrubbed moment (within the current day) instead of snapping back.
            time01 = Mathf.Repeat(time01, 1f);
            overrideElapsed = DayIndex * dayLen + time01 * dayLen;
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

    /// <summary>Dev day-scrub (DevSettings "Calendar &amp; Weather"): shift the clock by whole in-game days,
    /// keeping the current time-of-day. Detaches from real UTC first — the production clock recomputes
    /// <see cref="DayIndex"/> from the wall clock every frame, so it would instantly undo any offset.
    /// <see cref="GameCalendar"/>/<see cref="WeatherDirector"/> poll <see cref="DayIndex"/>, so the date +
    /// weather re-roll on the next frame. Clamps at the epoch so the day never goes negative.</summary>
    public void NudgeDays(int delta)
    {
        useRealClock = false;
        double dayLen = DayLengthSeconds;
        overrideElapsed = Math.Max(0.0, overrideElapsed + delta * dayLen);
        DayIndex = (long)Math.Floor(overrideElapsed / dayLen);
        time01 = (float)((overrideElapsed - DayIndex * dayLen) / dayLen);
    }
}
