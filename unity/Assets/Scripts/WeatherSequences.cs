using Preset = WeatherDirector.Preset;

/// <summary>
/// The authored weather <b>timelines</b> — one composable <see cref="WeatherSequence"/> per
/// <see cref="WeatherDirector.Preset"/>. THIS is the file you tune to change how weather rolls in: each row is
/// <c>K(atSeconds, channel, target0to1, rampSeconds)</c>, and the <see cref="WeatherChannels"/> sequencer plays them
/// in order while the channels ease independently. Read a block top-to-bottom and you read the event:
/// "heavy clouds first → very light rain → very light puddles → storm → more puddles".
///
/// <para>Rules of thumb: keys are listed in <b>time order</b> (the sequencer assumes sorted <c>at</c>). A channel
/// HOLDS its last target between keys (and between sequences), so an after-rain puddle linger is just a late Puddle→0
/// key — the puddles sit at the previous storm level until that key fires, then recede. The final value a channel
/// reaches is its "settled" state, which is what a freshly-loaded map snaps to (so you load straight into the weather,
/// no build-up replay). Determinism is preserved: every client runs the same code timeline for the same day.</para>
///
/// <para><b>These are the fallback / built-in timelines.</b> A <see cref="WeatherSequenceLibrary"/> of
/// <see cref="WeatherSequenceAsset"/>s can override any preset live from the Inspector (the "data source moves out of
/// code" layer); <see cref="For"/> prefers an authored asset and uses the <see cref="FromCode"/> timeline below only
/// when no asset covers that preset. The channel + sequencer engine is identical either way — only where the keys come
/// from changes. With no assets authored (today) every preset comes from here, exactly as before.</para>
/// </summary>
public static class WeatherSequences
{
    static WeatherKey K(float at, WeatherChannel c, float target, float ramp) => new WeatherKey(at, c, target, ramp);

    /// <summary>The timeline the director plays for a preset: an authored <see cref="WeatherSequenceAsset"/> if one
    /// exists, otherwise the built-in <see cref="FromCode"/> timeline. This is the single seam the SO layer plugs into;
    /// the engine call site (<see cref="WeatherDirector"/>) is unchanged.</summary>
    public static WeatherSequence For(Preset p)
    {
        var authored = WeatherSequenceLibrary.Resolve(p);
        return authored ?? FromCode(p);
    }

    /// <summary>The hard-coded fallback timeline for a preset (also the seed source for the "generate assets from code"
    /// tool, so authored assets start as an exact copy of these and can then be tuned).</summary>
    public static WeatherSequence FromCode(Preset p)
    {
        switch (p)
        {
            case Preset.Clear:        return Clear;
            case Preset.Cloudy:       return Cloudy;
            case Preset.LightRain:    return LightRain;
            case Preset.Thunderstorm: return Thunderstorm;
            case Preset.Snow:         return Snow;
            case Preset.Fog:          return Fog;
            default:                  return Clear;
        }
    }

    // Skies clear: rain stops fairly fast, clouds thin out slowly, and the puddles LINGER (hold ~8s) before drying —
    // the after-rain feel, now authored instead of hard-wired in WetGround.
    public static readonly WeatherSequence Clear = new WeatherSequence("Clear", new[]
    {
        K(0f,  WeatherChannel.Rain,   0.00f, 3f),
        K(0f,  WeatherChannel.Snow,   0.00f, 6f),
        K(0f,  WeatherChannel.Fog,    0.00f, 10f),   // burn off any lingering fog as the skies clear
        K(0f,  WeatherChannel.Cloud,  0.15f, 10f),
        K(8f,  WeatherChannel.Puddle, 0.00f, 12f),   // linger 8s, then dry over 12s
    });

    // Overcast, no precip. (Puddle→0 in case we arrived from rain.)
    public static readonly WeatherSequence Cloudy = new WeatherSequence("Cloudy", new[]
    {
        K(0f,  WeatherChannel.Rain,   0.00f, 4f),
        K(0f,  WeatherChannel.Snow,   0.00f, 6f),
        K(0f,  WeatherChannel.Fog,    0.00f, 10f),
        K(0f,  WeatherChannel.Cloud,  0.85f, 8f),
        K(8f,  WeatherChannel.Puddle, 0.00f, 12f),
    });

    // Light rain — the canonical build-up: heavy clouds gather FIRST, then the rain slides 0→0.45 over 10s, then
    // faint puddles begin, and the over-dark sky settles back to a steady rain overcast.
    public static readonly WeatherSequence LightRain = new WeatherSequence("Light Rain", new[]
    {
        K(0f,  WeatherChannel.Cloud,  0.92f, 6f),    // strong cloudy moment first
        K(0f,  WeatherChannel.Snow,   0.00f, 6f),
        K(0f,  WeatherChannel.Fog,    0.00f, 10f),
        K(6f,  WeatherChannel.Rain,   0.45f, 10f),   // light rain eases in 0→0.45 over 10s, after the clouds
        K(10f, WeatherChannel.Puddle, 0.60f, 14f),   // very light puddles begin forming
        K(12f, WeatherChannel.Cloud,  0.72f, 8f),    // settle the over-dark sky to steady rain overcast
    });

    // Thunderstorm — the dramatic escalation: heavy clouds → very light rain → very light puddles → storm → puddles
    // swell. (Exactly the sequence shape requested.)
    public static readonly WeatherSequence Thunderstorm = new WeatherSequence("Thunderstorm", new[]
    {
        K(0f,  WeatherChannel.Cloud,  0.95f, 6f),    // heavy clouds appear first
        K(0f,  WeatherChannel.Snow,   0.00f, 6f),
        K(0f,  WeatherChannel.Fog,    0.00f, 10f),
        K(5f,  WeatherChannel.Rain,   0.20f, 8f),    // then very light rain
        K(9f,  WeatherChannel.Puddle, 0.25f, 10f),   // then very light puddles
        K(18f, WeatherChannel.Rain,   1.00f, 12f),   // storm comes in
        K(18f, WeatherChannel.Cloud,  1.00f, 6f),    // sky goes black
        K(22f, WeatherChannel.Puddle, 1.00f, 15f),   // increase puddles
    });

    // Snow — clouds gather, flakes fall (the SnowGround blanket builds on its own slower clock). No puddles.
    public static readonly WeatherSequence Snow = new WeatherSequence("Snow", new[]
    {
        K(0f,  WeatherChannel.Rain,   0.00f, 4f),
        K(0f,  WeatherChannel.Fog,    0.00f, 10f),
        K(0f,  WeatherChannel.Cloud,  0.60f, 8f),
        K(0f,  WeatherChannel.Puddle, 0.00f, 8f),
        K(2f,  WeatherChannel.Snow,   0.70f, 6f),    // flakes start just after the clouds
    });

    // Fog — a real drifting ground haze (FogOverlay), NOT just a dark sky. The cloud sits at a soft grey (0.45, well
    // below Cloudy's 0.85 and the storm's 1.0) so an overcast lid reads behind the mist without going black, then the
    // fog rolls in and washes out the distance. This is what makes a foggy day distinct from a cloudy one.
    public static readonly WeatherSequence Fog = new WeatherSequence("Fog", new[]
    {
        K(0f,  WeatherChannel.Rain,   0.00f, 4f),
        K(0f,  WeatherChannel.Snow,   0.00f, 6f),
        K(0f,  WeatherChannel.Puddle, 0.00f, 12f),
        K(0f,  WeatherChannel.Cloud,  0.45f, 10f),   // soft grey lid — distinctly lighter than Cloudy/Storm
        K(2f,  WeatherChannel.Fog,    1.00f, 12f),   // the haze rolls in to full and limits visibility
    });
}
