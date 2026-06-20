using UnityEngine;

/// <summary>
/// Makes a prop glow from within at NIGHT. Spawns (and then drives) its own child point <see cref="Light"/>
/// that fades up as the shared <see cref="TimeOfDay"/> clock crosses into night and fades back out at dawn —
/// so houses, lamps and candles light the dark town without changing the daytime look.
///
/// Built to be cheap at town scale: the light is <b>warm, shadowless and short-range</b> (URP additional-light
/// shadows are off, and a modest-range point light is a few pixels of overdraw). It also self-contains its
/// light — a prefab only needs THIS one component added (the <c>Light Up Props (night)</c> editor tool does
/// that), no hand-authored Light child required.
///
/// Tune the whole town at once, live, from the DEV panel (P ▸ Environment) via the static
/// <see cref="GlobalEnabled"/>/<see cref="GlobalIntensity"/>/<see cref="GlobalRange"/> dials.
/// </summary>
[DisallowMultipleComponent]
public class NightLight : MonoBehaviour
{
    [Tooltip("Local offset of the light from this prop's pivot (the editor tool sets this to the prop's centre).")]
    public Vector3 localOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("Warm lamp colour.")]
    public Color color = new Color(1f, 0.72f, 0.36f);
    [Tooltip("Light intensity when fully lit at midnight (before the global multiplier).")]
    public float intensity = 2.5f;
    [Tooltip("How far the glow reaches, in WORLD units (auto-compensated for the prop's scale).")]
    public float range = 6f;
    [Range(0f, 1f)] [Tooltip("Night weight at which the lamp begins to switch on (0.5 ≈ sunset, 1 = midnight).")]
    public float turnOnAt = 0.35f;
    [Range(0f, 0.5f)] [Tooltip("Flicker amount (0 = steady flame-free glow).")]
    public float flicker = 0.07f;
    [Tooltip("Flicker speed.")]
    public float flickerSpeed = 6f;

    // ── live global dials (DevSettings) — affect EVERY lamp without re-touching prefabs ──
    public static bool GlobalEnabled = true;
    public static float GlobalIntensity = 1f;
    public static float GlobalRange = 1f;

    Light lamp;
    float seed;

    void Awake()
    {
        seed = Random.value * 100f;

        // Reuse a dedicated child named "NightLight" if a previous run made one, else create it. (Named, so we
        // never accidentally grab some other Light on the prop.)
        var t = transform.Find("NightLight");
        if (t == null)
        {
            t = new GameObject("NightLight").transform;
            t.SetParent(transform, false);
        }
        lamp = t.GetComponent<Light>();
        if (lamp == null) lamp = t.gameObject.AddComponent<Light>();

        lamp.type = LightType.Point;
        lamp.shadows = LightShadows.None;            // additional-light shadows are off in URP anyway — keep it cheap
        lamp.bounceIntensity = 0f;
        lamp.transform.localPosition = localOffset;
        Apply(0f);                                   // start dark; Update lights it when night falls
    }

    void Update()
    {
        var tod = TimeOfDay.Instance;
        float night = tod != null ? tod.Night01 : 0f;   // 0 at noon → 1 at midnight

        // Smooth dusk→full ramp over a 0.2 window above the turn-on point; 0 during the day.
        float w = GlobalEnabled
            ? Mathf.Clamp01(Mathf.InverseLerp(turnOnAt, Mathf.Min(1f, turnOnAt + 0.2f), night))
            : 0f;

        Apply(w);
    }

    void Apply(float w)
    {
        if (lamp == null) return;

        bool on = w > 0.001f;
        if (lamp.enabled != on) lamp.enabled = on;
        if (!on) return;   // off all day — nothing else to do

        // Gentle candle flicker (alloc-free Perlin; per-lamp seed so they don't pulse in sync).
        float fl = flicker > 0f ? 1f + (Mathf.PerlinNoise(seed, Time.time * flickerSpeed) - 0.5f) * 2f * flicker : 1f;

        lamp.color = color;
        lamp.intensity = intensity * GlobalIntensity * w * fl;
        // Light range is multiplied by the transform's lossy scale; props sit at ~0.3, so divide it back out
        // to keep `range` meaning WORLD units regardless of how the prop was scaled when placed.
        float s = Mathf.Max(0.0001f, transform.lossyScale.x);
        lamp.range = range * GlobalRange / s;
        lamp.transform.localPosition = localOffset;
    }
}
