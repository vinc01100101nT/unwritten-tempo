using UnityEngine;

/// <summary>
/// The game's single accelerated day/night clock. Lives on the persistent <c>Systems</c> layer so the
/// light (<see cref="DayNightLight"/>) and ambience (<see cref="DayNightAmbience"/>) share ONE source of
/// time instead of each running its own. The cycle is split evenly: <see cref="halfCycleSeconds"/> of day
/// (incl. sunrise) then the same of night (incl. sunset) — default 3600s each = a 1-hour day + 1-hour night.
///
/// It's also the in-game <b>test tuner</b>: in Play mode, tick <see cref="paused"/> and drag
/// <see cref="time01"/> to scrub, bump <see cref="testSpeed"/> to fast-forward, or right-click the
/// component for the "Jump ▶ Sunrise/Noon/Sunset/Midnight" context-menu shortcuts. <see cref="_readout"/>
/// shows the live phase + clock in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public class TimeOfDay : MonoBehaviour
{
    public static TimeOfDay Instance { get; private set; }

    [Header("Cycle (accelerated)")]
    [Tooltip("Real seconds for ONE HALF of the cycle. 3600 = a 1-hour day + a 1-hour night (full cycle = 2×).")]
    public float halfCycleSeconds = 3600f;

    [Header("Live test tuner — works in Play mode")]
    [Tooltip("Pause the clock so you can scrub the slider below by hand.")]
    public bool paused = false;
    [Tooltip("Fast-forward multiplier for testing (1 = real cadence).")]
    public float testSpeed = 1f;
    [Range(0f, 1f)] [Tooltip("Time of day. 0 = sunrise, 0.25 = noon, 0.5 = sunset, 0.75 = midnight.")]
    public float time01 = 0f;
    [SerializeField] [Tooltip("Read-only live readout.")]
    string _readout = "";

    /// <summary>Smooth daylight weight: 1 at noon (0.25), 0 at midnight (0.75), 0.5 at sunrise/sunset.</summary>
    public float Daylight01 => (Mathf.Cos((time01 - 0.25f) * Mathf.PI * 2f) + 1f) * 0.5f;
    /// <summary>Inverse of <see cref="Daylight01"/> — peaks (1) at midnight.</summary>
    public float Night01 => 1f - Daylight01;
    public bool IsDay => time01 < 0.5f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        float full = Mathf.Max(0.01f, halfCycleSeconds * 2f);   // day + night
        if (!paused)
            time01 = Mathf.Repeat(time01 + Time.deltaTime * Mathf.Max(0f, testSpeed) / full, 1f);
#if UNITY_EDITOR
        // Inspector-only debug string — building it every frame allocates; a build has no inspector to show it.
        _readout = $"{Phase()}  ·  t={time01:0.000}  ·  {(IsDay ? "DAY" : "NIGHT")}  ·  light={Daylight01:0.00}";
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
}
