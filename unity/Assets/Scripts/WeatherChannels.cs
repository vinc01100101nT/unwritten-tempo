using UnityEngine;

/// <summary>
/// The four independent weather <b>channels</b> — a single eased 0..1 value each, the source of truth every weather
/// renderer reads. This is the decoupled core of the weather system: a channel is JUST a number that eases toward a
/// target; it knows nothing about rain streaks, puddles, clouds or who renders it. The renderers
/// (<see cref="RainOverlay"/>, <see cref="CloudShadows"/>, <see cref="SnowOverlay"/>, <see cref="WetGround"/>,
/// <see cref="SnowGround"/>, <see cref="FogOverlay"/>, <see cref="RainAudio"/>) are dumb <i>followers</i> that read
/// <see cref="Shown"/> — none
/// of them follow each other any more, so puddles can lag rain, clouds can lead it, etc.
///
/// <para><b>Composition.</b> A weather "event" (a rainy day, a building storm) is a <see cref="WeatherSequence"/> — an
/// ordered list of keyframes <c>{ atTime, channel, target, ramp }</c> — that the <see cref="WeatherDirector"/> hands
/// to <see cref="Play"/>. The sequencer fires each key as its time arrives (one index pointer, zero GC), and the
/// channels ease independently. So "heavy clouds, then very light rain, then puddles, then storm" is authored as data,
/// not hard-wired. Easing happens in ONE place (here), so a sequence's ramp times are honoured exactly.</para>
///
/// <para><b>Auto-puddle.</b> So that manually raising just the rain (DevSettings, or any code) still wets the ground,
/// the <see cref="WeatherChannel.Puddle"/> channel derives its target from rain — soak-in delay, spread, after-rain
/// linger, dry-out — UNLESS a sequence explicitly drives puddles (then <see cref="autoPuddle"/> is switched off for
/// that sequence and the keyframes own the look). This is the only built-in cross-channel coupling, and it's optional.</para>
///
/// <para>Lives on the persistent Systems layer next to <see cref="WeatherDirector"/> (added by SystemsSceneBuilder).
/// Per-frame cost is four <c>MoveTowards</c> + a key-time compare — no allocation.</para>
/// </summary>
[DisallowMultipleComponent]
public class WeatherChannels : MonoBehaviour
{
    public const int Count = 6;
    public static WeatherChannels Instance { get; private set; }

    struct Chan { public float target, shown, ramp; }
    readonly Chan[] ch = new Chan[Count];

    // ── sequencer state ───────────────────────────────────────────────────────────────────────────────────────
    WeatherSequence seq;
    float seqTime;
    int   nextKey;
    bool  playing;

    /// <summary>Name of the sequence currently playing/settled (HUD/readout only).</summary>
    public string CurrentSequence => seq != null ? seq.name : "-";

    // ── auto-puddle (manual-rain convenience; a sequence with puddle keys turns this off) ─────────────────────────
    [Header("Auto-puddle (used only when no sequence is driving the Puddle channel)")]
    [Tooltip("Derive the Puddle channel from rain so manually raising the rain still wets the ground. A weather " +
             "sequence that has its own Puddle keys switches this OFF and authors the puddles itself.")]
    public bool autoPuddle = true;
    [Range(0f, 30f)] [Tooltip("Seconds AFTER rain starts before puddles begin forming (the ground soaks first).")]
    public float puddleSoakDelay = 5f;
    [Range(0.1f, 60f)] [Tooltip("Seconds for auto-puddles to spread from none to full once they start.")]
    public float puddleSpread = 10f;
    [Range(0f, 60f)] [Tooltip("Seconds AFTER rain stops before puddles begin drying — they linger first (after-rain feel).")]
    public float puddleDryDelay = 10f;
    [Range(0.1f, 60f)] [Tooltip("Seconds for auto-puddles to dry back to none once they start receding.")]
    public float puddleRecede = 10f;
    float puddleDelayTimer;
    bool  puddleWasRaining;

    /// <summary>Find the live channels hub, creating one if none exists yet (used by the director + DevSettings so
    /// weather works even on a Systems scene baked before this component joined the rig).</summary>
    public static WeatherChannels Ensure()
    {
        var w = Instance != null ? Instance : FindFirstObjectByType<WeatherChannels>();
        if (w == null) w = new GameObject("WeatherChannels") { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<WeatherChannels>();
        return w;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        for (int i = 0; i < Count; i++) ch[i] = new Chan { target = 0f, shown = 0f, ramp = 4f };
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── public channel API ────────────────────────────────────────────────────────────────────────────────────
    /// <summary>The eased, on-screen value of a channel (0..1) — what the renderers draw.</summary>
    public float Shown(WeatherChannel c) => ch[(int)c].shown;
    /// <summary>The value the channel is easing toward.</summary>
    public float Target(WeatherChannel c) => ch[(int)c].target;

    /// <summary>Aim a channel at <paramref name="target"/>, easing over <paramref name="ramp"/> seconds (the seconds
    /// for a full 0→1 sweep — same units as the overlays' old rampTime).</summary>
    public void SetTarget(WeatherChannel c, float target, float ramp)
    {
        var k = ch[(int)c];
        k.target = Mathf.Clamp01(target);
        k.ramp   = Mathf.Max(0.01f, ramp);
        ch[(int)c] = k;
    }

    /// <summary>Snap a channel instantly to a value (no easing) — used on scene load so you drop straight into the
    /// day's established weather, and by the DevSettings sliders for immediate feedback.</summary>
    public void Snap(WeatherChannel c, float value)
    {
        var k = ch[(int)c];
        k.target = k.shown = Mathf.Clamp01(value);
        ch[(int)c] = k;
    }

    // ── sequencer ─────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Play a weather sequence from t=0 — a deliberate weather CHANGE that builds up over time (clouds roll
    /// in, then rain, then puddles…). Channels not mentioned by the sequence keep their current target.</summary>
    public void Play(WeatherSequence s)
    {
        seq = s;
        seqTime = 0f;
        nextKey = 0;
        playing = s != null && s.keys != null && s.keys.Length > 0;
        // A sequence that authors puddles owns them; otherwise let the auto-puddle driver follow rain.
        autoPuddle = !(s != null && s.Controls(WeatherChannel.Puddle));
        ResetAutoPuddleEdge();
    }

    /// <summary>Snap every channel straight to a sequence's <b>settled</b> (final) values — used on scene LOAD so you
    /// don't watch the build-up play out every time you enter a rainy map; you're just already in the weather.</summary>
    public void Settle(WeatherSequence s)
    {
        seq = s;
        playing = false;
        for (int i = 0; i < Count; i++)
            Snap((WeatherChannel)i, s != null ? s.SettledValue((WeatherChannel)i) : 0f);
        autoPuddle = !(s != null && s.Controls(WeatherChannel.Puddle));
        ResetAutoPuddleEdge();
    }

    void ResetAutoPuddleEdge()
    {
        // Sync the auto-puddle edge tracker to the current rain so switching sequences doesn't re-fire a soak/dry.
        puddleWasRaining = ch[(int)WeatherChannel.Rain].target > 0.001f;
        puddleDelayTimer = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Advance the sequencer, firing every key whose time has arrived this frame (keys are sorted by `at`).
        // Guard on a valid sequence: `playing` could be flipped on with no `seq` assigned, which used to throw an
        // NRE here every frame (seq.keys). No sequence → nothing to play, so stop the sequencer.
        if (playing && seq != null && seq.keys != null)
        {
            seqTime += dt;
            while (nextKey < seq.keys.Length && seq.keys[nextKey].at <= seqTime)
            {
                var key = seq.keys[nextKey++];
                SetTarget(key.channel, key.target, key.ramp);
            }
            if (nextKey >= seq.keys.Length) playing = false;   // sequence done; channels hold their last targets
        }
        else playing = false;   // playing flagged but no valid sequence assigned — stop (avoids the per-frame NRE)

        // Auto-puddle: when no sequence owns the Puddle channel, drive it from rain (soak delay → spread,
        // after-rain linger → recede), so manual rain still wets the ground.
        if (autoPuddle) UpdateAutoPuddle(dt);

        // The single, central easing point: every channel eases toward its target. ramp = seconds for a 0→1 sweep.
        for (int i = 0; i < Count; i++)
        {
            var k = ch[i];
            k.shown = Mathf.MoveTowards(k.shown, k.target, dt / k.ramp);
            ch[i] = k;
        }
    }

    void UpdateAutoPuddle(float dt)
    {
        bool raining = ch[(int)WeatherChannel.Rain].target > 0.001f;   // off the rain TARGET so it reacts the instant rain is commanded
        if (raining != puddleWasRaining)
        {
            puddleWasRaining = raining;
            puddleDelayTimer = raining ? puddleSoakDelay : puddleDryDelay;   // hold before the next spread/recede
        }

        if (puddleDelayTimer > 0f)
        {
            puddleDelayTimer -= dt;   // inside the soak/dry delay — leave the puddle target untouched (it holds)
            return;
        }

        float target = raining ? 1f : 0f;                    // normalized; WetGround scales by its per-map coverage cap
        float ramp   = raining ? puddleSpread : puddleRecede;
        SetTarget(WeatherChannel.Puddle, target, ramp);
    }
}

/// <summary>The weather channels. Each is an independent eased 0..1 quantity; renderers follow them by name.
/// (Adding one here means bumping <see cref="WeatherChannels.Count"/> to match — the channel array is sized to it.)</summary>
public enum WeatherChannel { Rain = 0, Cloud = 1, Snow = 2, Puddle = 3, Fog = 4, Wind = 5 }

/// <summary>One step in a <see cref="WeatherSequence"/>: at <see cref="at"/> seconds into the event, aim
/// <see cref="channel"/> at <see cref="target"/>, easing over <see cref="ramp"/> seconds.</summary>
public struct WeatherKey
{
    public float at;
    public WeatherChannel channel;
    public float target;
    public float ramp;
    public WeatherKey(float at, WeatherChannel channel, float target, float ramp)
    { this.at = at; this.channel = channel; this.target = target; this.ramp = ramp; }
}

/// <summary>A reusable, composable weather "event" — an ordered timeline of <see cref="WeatherKey"/>s that drive the
/// channels over time. Keys MUST be sorted by <see cref="WeatherKey.at"/> (the sequencer fires them in order).</summary>
public class WeatherSequence
{
    public readonly string name;
    public readonly WeatherKey[] keys;

    public WeatherSequence(string name, WeatherKey[] keys) { this.name = name; this.keys = keys; }

    /// <summary>Does this sequence have at least one keyframe for <paramref name="c"/>? (If so it OWNS that channel,
    /// e.g. a rainy day that authors its own puddles switches off the auto-puddle driver.)</summary>
    public bool Controls(WeatherChannel c)
    {
        for (int i = 0; i < keys.Length; i++) if (keys[i].channel == c) return true;
        return false;
    }

    /// <summary>The value a channel ends on once the whole sequence has played (its last keyframe, or 0 if the
    /// sequence never touches it). Used to snap a freshly-loaded map straight into its settled weather.</summary>
    public float SettledValue(WeatherChannel c)
    {
        float v = 0f, bestAt = float.NegativeInfinity;
        for (int i = 0; i < keys.Length; i++)
            if (keys[i].channel == c && keys[i].at >= bestAt) { bestAt = keys[i].at; v = keys[i].target; }
        return v;
    }
}
