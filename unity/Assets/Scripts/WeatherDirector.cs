using UnityEngine;
using Season = GameCalendar.Season;

/// <summary>
/// The WEATHER layer on top of the <see cref="GameCalendar"/>. Once per in-game day it picks ONE weather pattern
/// for that day and points the existing overlays at it. The pick is <b>deterministic</b>: a stable hash of
/// (<see cref="worldSeed"/>, <see cref="GameCalendar.DayIndex"/>) chooses from a <b>season-gated weighted pool</b>,
/// so — exactly like the clock and calendar — every MMO client computes the SAME weather for the same day with
/// zero networking, and the season decides what's even possible (snow only in winter, thunderstorms only in summer).
///
/// <para><b>It doesn't render anything itself.</b> Each <see cref="Preset"/> maps to an authored
/// <see cref="WeatherSequence"/> (in <see cref="WeatherSequences"/>) — a timeline of keyframes that drive the
/// independent <see cref="WeatherChannels"/> (rain / cloud / snow / puddle) over time. The director just hands the
/// sequence to the channels hub; the channels ease, and every renderer (<see cref="RainOverlay"/>,
/// <see cref="CloudShadows"/>, <see cref="SnowOverlay"/>, <see cref="WetGround"/>, <see cref="SnowGround"/>,
/// <see cref="FogOverlay"/>, <see cref="RainAudio"/>) is a dumb follower that reads its channel — none follow each
/// other. So a weather change
/// can roll clouds in BEFORE the rain, and puddles can lag and linger, all authored as data — no scene re-bake.</para>
///
/// <para>Lives on the persistent <c>Systems</c> layer next to <see cref="TimeOfDay"/>/<see cref="GameCalendar"/>
/// (added by <c>SystemsSceneBuilder</c>). Per-frame cost is a single long compare (repick only on a day boundary)
/// plus one cloud-fade lerp. Uncheck <see cref="autoFromCalendar"/> and set <see cref="forcedPreset"/> to pin a
/// preset for testing.</para>
/// </summary>
[DisallowMultipleComponent]
public class WeatherDirector : MonoBehaviour
{
    public static WeatherDirector Instance { get; private set; }

    /// <summary>The weather patterns. Each maps to an authored timeline (see <see cref="WeatherSequences.For"/>) that
    /// drives the channels. Order is the pick order within a pool, but the chosen one is weight-driven, not positional.</summary>
    public enum Preset { Clear, Cloudy, LightRain, Thunderstorm, Snow, Fog }

    [Header("Determinism")]
    [Tooltip("Shared world weather seed. MUST be identical on every client (like TimeOfDay.Epoch) so everyone " +
             "derives the same daily weather. Change it only to re-roll the whole world's weather history.")]
    public int worldSeed = 20260101;

    [Header("Manual override (testing)")]
    [Tooltip("ON: weather is auto-picked each in-game day from the calendar (production). OFF: hold forcedPreset.")]
    public bool autoFromCalendar = true;
    [Tooltip("The preset to hold while autoFromCalendar is OFF — lets you force any weather to look at it.")]
    public Preset forcedPreset = Preset.Clear;

    [SerializeField] [Tooltip("Read-only live readout of the current weather.")]
    string _readout = "";

    /// <summary>The weather currently in force (the last picked/forced preset).</summary>
    public Preset Current { get; private set; } = Preset.Clear;
    /// <summary>HUD-friendly name of the current weather, e.g. "Light Rain".</summary>
    public string CurrentName => NameOf(Current);

    // ── Calendar API: deterministic weather + tags for ANY day (past or future) ───────────────────────────────────

    /// <summary>The weather for <b>any</b> day index — the exact same pure function the live director uses, so the
    /// calendar shows precisely what each day's sky will be / was, and every client agrees. Ignores
    /// <see cref="forcedPreset"/> (a forced preset is a live-sky dev override, not the world's weather record).</summary>
    public Preset PresetForDay(long day)
    {
        // Floor-mod so days before the epoch still resolve to a valid season (matches GameCalendar.Recompute).
        long doy = ((day % GameCalendar.DaysPerYear) + GameCalendar.DaysPerYear) % GameCalendar.DaysPerYear;
        var season = (Season)((int)doy / GameCalendar.DaysPerSeason);
        return Pick(season, day);
    }

    /// <summary>Short player-facing weather tag for HUD/calendar, e.g. "Sunny" / "Rainy" / "Snow".</summary>
    public static string TagOf(Preset p)
    {
        switch (p)
        {
            case Preset.Clear:        return "Sunny";
            case Preset.Cloudy:       return "Cloudy";
            case Preset.LightRain:    return "Rainy";
            case Preset.Thunderstorm: return "Storm";
            case Preset.Snow:         return "Snow";
            case Preset.Fog:          return "Fog";
            default:                  return p.ToString();
        }
    }

    /// <summary>At-a-glance accent colour for a weather tag (shared by the date HUD widget + calendar cells).</summary>
    public static Color ColorOf(Preset p)
    {
        switch (p)
        {
            case Preset.Clear:        return new Color(1.00f, 0.85f, 0.30f);   // warm sun
            case Preset.Cloudy:       return new Color(0.78f, 0.81f, 0.85f);   // pale grey
            case Preset.LightRain:    return new Color(0.45f, 0.65f, 1.00f);   // rain blue
            case Preset.Thunderstorm: return new Color(0.70f, 0.60f, 0.98f);   // storm violet
            case Preset.Snow:         return new Color(0.85f, 0.95f, 1.00f);   // ice white
            case Preset.Fog:          return new Color(0.72f, 0.74f, 0.74f);   // dull mist
            default:                  return Color.white;
        }
    }

    // The channels hub all the renderers follow — Ensured once, then reused (Ensure does a FindFirstObjectByType,
    // which we don't want to pay on every day boundary).
    WeatherChannels channels;

    long lastDay;
    bool applied;
    bool wasAuto = true;   // tracks autoFromCalendar so flipping it back ON re-picks immediately
    bool firstApply = true;   // the very first Apply is a LOAD: SETTLE into the day's weather, never play the build-up

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>The weather a GM's <c>/weather</c> holds on the server, told to every player there. Null until
    /// somebody forces one, and gone when that server restarts. While set it wins over the calendar and over the
    /// inspector override below.</summary>
    public static Preset? ForcedByServer { get; private set; }

    /// <summary>When the weather the server holds began (world-clock epoch seconds), and what it held before then (null
    /// = the calendar's weather) and since when — so every player knows how long it has rained, whenever they arrived.</summary>
    public static double ForcedSince { get; private set; }
    public static Preset? ForcedBefore { get; private set; }
    public static double ForcedBeforeSince { get; private set; }

    /// <summary>On the server: hold <paramref name="preset"/> (null = none) from now, remembering what it replaces.</summary>
    public static void TakeForced(Preset? preset)
    {
        if (preset == ForcedByServer) return;
        TakeForced(preset, WorldClock.EpochSecondsNow, ForcedByServer, ForcedSince);
    }

    /// <summary>Take the weather the server holds, with when it began and what came before it — on a server the
    /// command setting it, on a player's machine the server's word arriving.</summary>
    public static void TakeForced(Preset? preset, double since, Preset? before, double beforeSince)
    {
        ForcedByServer = preset;
        ForcedSince = since;
        ForcedBefore = before;
        ForcedBeforeSince = beforeSince;
    }

    /// <summary>The rain standing water fills from, on the shared world clock (epoch seconds), the same on every
    /// player's machine: the rain (0..1) of the weather in force and when it began, and the same for the weather before
    /// it. Read by <see cref="WetGround"/> through <see cref="WaterPooling.Rainfall"/>.</summary>
    public static float WaterRain { get; private set; }
    public static double WaterRainSince { get; private set; }
    public static float WaterRainBefore { get; private set; }
    public static double WaterRainBeforeSince { get; private set; }

    static readonly float[] rainOf = new float[System.Enum.GetValues(typeof(Preset)).Length];
    static readonly bool[] rainKnown = new bool[rainOf.Length];

    // How hard a weather rains once it has built up (its rain channel's settled value), worked out once per weather.
    static float RainOf(Preset p)
    {
        int i = (int)p;
        if (!rainKnown[i])
        {
            var seq = WeatherSequences.For(p);
            rainOf[i] = seq != null ? seq.SettledValue(WeatherChannel.Rain) : 0f;
            rainKnown[i] = true;
        }
        return rainOf[i];
    }

    long waterDay = long.MinValue;
    double waterForcedSince = double.NaN;
    Preset? waterForced;

    // Work out the rain history for the water — only when the day or the server's held weather changes.
    void UpdateWaterRain()
    {
        var tod = TimeOfDay.Instance;
        if (tod == null) return;
        long day = tod.DayIndex;
        if (day == waterDay && ForcedByServer == waterForced && ForcedSince.Equals(waterForcedSince)) return;
        waterDay = day;
        waterForced = ForcedByServer;
        waterForcedSince = ForcedSince;

        double dayLen = tod.DayLengthSeconds;
        double epoch = WorldClock.ToEpochSeconds(TimeOfDay.Epoch) - TimeOfDay.ShiftSeconds;
        if (ForcedByServer.HasValue)
        {
            WaterRain = RainOf(ForcedByServer.Value);
            WaterRainSince = ForcedSince;
            if (ForcedBefore.HasValue)
            {
                WaterRainBefore = RainOf(ForcedBefore.Value);
                WaterRainBeforeSince = ForcedBeforeSince;
            }
            else
            {
                // Before the server held one, the calendar's weather for the day the hold began.
                long then = (long)System.Math.Floor((ForcedSince - epoch) / dayLen);
                WaterRainBefore = RainOf(PresetForDay(then));
                WaterRainBeforeSince = epoch + then * dayLen;
            }
        }
        else
        {
            WaterRain = RainOf(PresetForDay(day));
            WaterRainSince = epoch + day * dayLen;
            WaterRainBefore = RainOf(PresetForDay(day - 1));
            WaterRainBeforeSince = epoch + (day - 1) * dayLen;
        }
    }

    void Update()
    {
        UpdateWaterRain();
        if (ForcedByServer.HasValue)
        {
            var held = ForcedByServer.Value;
            if (!applied || held != Current) { Apply(held); applied = true; }
            wasAuto = false;
            return;
        }

        if (autoFromCalendar)
        {
            // Just resumed auto (toggled back on after forcing) → re-pick this frame instead of
            // waiting for the next day boundary, so the calendar's real weather returns immediately.
            if (!wasAuto) applied = false;
            var cal = GameCalendar.Instance;
            // Hot-path guard: the weather only changes when the in-game DAY rolls over (same one-long-compare idiom
            // GameCalendar uses). Polling rather than subscribing also means a day-scrub repicks for free.
            if (cal != null && (!applied || cal.DayIndex != lastDay))
            {
                Apply(Pick(cal.CurrentSeason, cal.DayIndex));
                lastDay = cal.DayIndex;
                applied = true;
            }
        }
        else if (!applied || forcedPreset != Current)
        {
            Apply(forcedPreset);
            applied = true;
        }
        wasAuto = autoFromCalendar;
    }

    /// <summary>Deterministic weighted pick from the season's pool — pure function of (seed, day), so every client
    /// agrees. Walks the weighted pool with a single hashed roll in [0,1).</summary>
    Preset Pick(Season season, long day)
    {
        var pool = PoolFor(season);
        float total = 0f;
        for (int i = 0; i < pool.Length; i++) total += pool[i].weight;

        // Hash → [0,1) → scaled across the total weight. Uniform and stable across machines.
        float r = (Hash(worldSeed, day) / 4294967296f) * total;   // /(uint.MaxValue + 1)
        for (int i = 0; i < pool.Length; i++)
        {
            if (r < pool[i].weight) return pool[i].preset;
            r -= pool[i].weight;
        }
        return pool[pool.Length - 1].preset;   // float-rounding guard
    }

    /// <summary>Hand the preset's authored <see cref="WeatherSequence"/> to the channels hub. On a genuine weather
    /// CHANGE we <see cref="WeatherChannels.Play"/> it so the event builds up over time (clouds → rain → puddles → …);
    /// on the first Apply (a scene LOAD) we <see cref="WeatherChannels.Settle"/> straight to its settled values so you
    /// drop into the day's established weather instead of replaying the build-up. No overlay poking, no re-bake.</summary>
    void Apply(Preset p)
    {
        Current = p;

        // Normally the builder put WeatherChannels on the persistent Systems rig. If this scene was baked before that
        // (no hub found), host it on OUR own GameObject — which IS persistent — rather than a loose object a map
        // unload could destroy.
        if (channels == null)
        {
            channels = WeatherChannels.Instance;
            if (channels == null) channels = gameObject.AddComponent<WeatherChannels>();
        }

        // Make sure every renderer that follows the channels exists in the rig (no-op if the builder already added
        // them; cheap, only on a weather change).
        RainOverlay.Ensure();
        SnowOverlay.Ensure();
        CloudShadows.Ensure();
        SkyboxSky.Ensure();
        WetGround.Ensure();
        SnowGround.Ensure();
        RainAudio.Ensure();
        FogOverlay.Ensure();
        CloudLayerOverlay.Ensure();

        var seq = WeatherSequences.For(p);
        bool load = firstApply;
        if (load) channels.Settle(seq);   // LOAD: snap into the settled weather, no build-up
        else      channels.Play(seq);      // CHANGE: play the build-up timeline over time
        firstApply = false;

#if UNITY_EDITOR
        _readout = $"{NameOf(p)}  ·  {(load ? "settled" : "playing")} '{seq.name}'";
#endif
    }

    // ── Season-gated weighted pools (the locked design: snow = winter only, storms = summer) ─────────────────────
    struct Entry { public Preset preset; public float weight; }
    static Entry E(Preset p, float w) => new Entry { preset = p, weight = w };

    // Allocated once (static) so a day-boundary pick never produces GC.
    static readonly Entry[] Spring =
    {
        E(Preset.Clear, 4f), E(Preset.Cloudy, 3f), E(Preset.LightRain, 3f), E(Preset.Fog, 1f),
    };
    static readonly Entry[] Summer =
    {
        E(Preset.Clear, 6f), E(Preset.Cloudy, 2f), E(Preset.LightRain, 1f), E(Preset.Thunderstorm, 1f),
    };
    static readonly Entry[] Autumn =
    {
        E(Preset.Clear, 2f), E(Preset.Cloudy, 3f), E(Preset.LightRain, 3f), E(Preset.Fog, 2f),
    };
    static readonly Entry[] Winter =
    {
        E(Preset.Clear, 3f), E(Preset.Cloudy, 2f), E(Preset.Snow, 4f), E(Preset.Fog, 1f),
    };

    static Entry[] PoolFor(Season s)
    {
        switch (s)
        {
            case Season.Spring: return Spring;
            case Season.Summer: return Summer;
            case Season.Autumn: return Autumn;
            case Season.Winter: return Winter;
            default:            return Spring;
        }
    }

    // ── Deterministic hash (SplitMix64-style mix of seed + day → uint). Stable across platforms/runs, unlike
    //    object.GetHashCode, so all clients pick the same weather. ─────────────────────────────────────────────
    static uint Hash(int seed, long day)
    {
        unchecked
        {
            ulong x = (ulong)day * 0x9E3779B97F4A7C15UL + (uint)seed * 0xD1B54A32D192ED03UL;
            x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27; x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (uint)(x >> 32);
        }
    }

    static string NameOf(Preset p) => p == Preset.LightRain ? "Light Rain" : p.ToString();
}
