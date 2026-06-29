using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a prop glow from within at NIGHT. Spawns (and then drives) its own child point <see cref="Light"/>
/// that fades up as the shared <see cref="TimeOfDay"/> clock crosses into night and fades back out at dawn —
/// so houses, lamps and candles light the dark town without changing the daytime look.
///
/// Built to be cheap at town scale: the light is <b>warm and short-range</b> (a modest-range point light is a
/// few pixels of overdraw). It also self-contains its light — a prefab only needs THIS one component added
/// (the <c>Light Up Props (night)</c> editor tool does that), no hand-authored Light child required.
///
/// <para><b>Shadows.</b> Point-light shadows are expensive (a cubemap per caster), so we never let every town
/// lamp cast: only the <see cref="MaxShadowCasters"/> lamps nearest the player cast (re-selected on a cheap
/// timer). That keeps us inside URP's additional-shadow budget while still grounding the player with a real
/// cast shadow from whatever lamps they're standing next to. <see cref="BillboardShadow"/> reads
/// <see cref="TryGetShadowLamp"/> to aim the sprite's shadow proxy at the nearest casting lamp.</para>
///
/// Tune the whole town at once, live, from the DEV panel (P ▸ Environment) via the static
/// <see cref="GlobalEnabled"/>/<see cref="GlobalIntensity"/>/<see cref="GlobalRange"/>/<see cref="GlobalShadows"/> dials.
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
    /// <summary>Master switch for the nearest-lamp cast shadow (DevSettings ▸ Shadows). Off → no lamp casts.</summary>
    public static bool GlobalShadows = true;
    /// <summary>Only lamps within this distance of the player are eligible to be a shadow caster.</summary>
    public static float MaxShadowDist = 12f;
    /// <summary>Darkness of the cast lamp shadow (0 = invisible, 1 = black). DevSettings ▸ Shadows.</summary>
    public static float GlobalShadowStrength = 1f;
    /// <summary>How many lamps may cast a real point-light shadow at once. Each is a cubemap (6 face renders),
    /// so this is the cost dial. PC-only target → 4 is comfortable. Clamped to <see cref="HardCap"/>.</summary>
    public static int MaxShadowCasters = 4;

    // ── nearest-lamp shadow selection ──
    // Only the N closest LIT lamps to the player cast a shadow at a time — point-light shadows are a cubemap
    // each, so we cap at MaxShadowCasters to respect URP's additional-shadow budget. Re-picked on a timer.
    const int HardCap = 8;                                             // safety ceiling for the selection buffers
    static readonly List<NightLight> All = new List<NightLight>();
    static readonly NightLight[] bestNl = new NightLight[HardCap];     // reused scratch: top-N nearest to player, ascending
    static readonly float[] bestDist = new float[HardCap];            // their squared distances to the player
    static readonly List<NightLight> casting = new List<NightLight>(); // lamps currently flagged to cast
    static NightLight current;          // the NEAREST caster (BillboardShadow proxy aims at this one)
    static float nextSelect;            // unscaled time of the next nearest re-evaluation
    bool castShadow;                    // set by SelectNearest; read in Apply

    Light lamp;
    float seed;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable()
    {
        All.Remove(this);
        if (current == this) current = null;
    }

    /// <summary>For <see cref="BillboardShadow"/>: the world position of the lamp currently casting a shadow,
    /// so sprite shadow proxies can turn to face it. False when no lamp is casting (day, or shadows off).</summary>
    public static bool TryGetShadowLamp(out Vector3 pos)
    {
        if (current != null && current.lamp != null && current.lamp.enabled && current.castShadow)
        { pos = current.lamp.transform.position; return true; }
        pos = default;
        return false;
    }

    // The MOVING player transform. NOT Character.Instance — that's a stationary logical object at the origin, so
    // ranking lamps against it froze the selection (the same lamps "won" no matter where you walked). The avatar
    // that actually moves is Player3DController; resolve it once and cache (re-resolves only if it's gone/respawned).
    static Transform playerTf;
    static Transform PlayerTransform()
    {
        if (playerTf != null) return playerTf;
        var p3d = FindFirstObjectByType<Player3DController>();
        if (p3d != null) playerTf = p3d.transform;
        return playerTf;
    }

    // Pick the N closest LIT lamps to the player and make only those cast. Cheap: runs at most a few times a
    // second off ONE shared timer regardless of how many lamps call it. No allocations (reused scratch buffers).
    static void SelectNearest()
    {
        if (Time.unscaledTime < nextSelect) return;
        nextSelect = Time.unscaledTime + 0.3f;

        int n = Mathf.Clamp(MaxShadowCasters, 0, HardCap);
        int found = 0;                                 // how many of the top-N slots are filled
        var playerTf = PlayerTransform();
        if (n > 0 && GlobalShadows && playerTf != null)
        {
            Vector3 p = playerTf.position;
            float maxSq = MaxShadowDist * MaxShadowDist;
            for (int i = 0; i < All.Count; i++)
            {
                var nl = All[i];
                if (nl == null || nl.lamp == null || !nl.lamp.enabled) continue;   // only lamps that are lit
                // Measure from the ACTUAL lamp position, not the prop root — on a big (scaled) building the root
                // can be far from the lamp, wrongly excluding it from the nearest-caster pick.
                // Simple rule: rank by straight distance from the PLAYER to the lamp. The nearest lamp always casts.
                float score = (nl.lamp.transform.position - p).sqrMagnitude;
                if (score >= maxSq) continue;                                        // outside eligibility radius
                if (found == n && score >= bestDist[found - 1]) continue;           // worse than our current worst
                // Insertion-sort into the ascending top-N buffer (n ≤ HardCap, so this stays tiny).
                int at = found < n ? found : n - 1;
                while (at > 0 && bestDist[at - 1] > score)
                {
                    bestDist[at] = bestDist[at - 1];
                    bestNl[at] = bestNl[at - 1];
                    at--;
                }
                bestDist[at] = score;
                bestNl[at] = nl;
                if (found < n) found++;
            }
        }

        // Clear last frame's casters, then flag this frame's set. (Flag churn is fine at a 0.3s cadence.)
        for (int i = 0; i < casting.Count; i++)
            if (casting[i] != null) casting[i].castShadow = false;
        casting.Clear();
        for (int i = 0; i < found; i++)
        {
            bestNl[i].castShadow = true;
            casting.Add(bestNl[i]);
            bestNl[i] = null;                          // release scratch ref so we don't pin a destroyed lamp
        }
        current = found > 0 ? casting[0] : null;       // nearest caster, for the BillboardShadow proxy aim
    }

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
        lamp.shadows = LightShadows.None;            // off by default; SelectNearest promotes the closest lamp
        lamp.bounceIntensity = 0f;
        PlaceLamp();
        Apply(0f);                                   // start dark; Update lights it when night falls
    }

    // Seat the lamp at its offset AND neutralize the prop's scale so the Light transform is WORLD-UNIT (lossyScale
    // 1). A point light under a scaled parent renders its lighting fine but its SHADOW matrices get distorted/clipped
    // (Unity's "Light has scale in its transform" gotcha) — so a lamp on a scale-4 building lit the ground but cast
    // no shadow. Counter-scaling makes shadows correct and lets `range` be true world units regardless of prop scale.
    void PlaceLamp()
    {
        if (lamp == null) return;
        var lt = lamp.transform;
        lt.localPosition = localOffset;
        var ls = transform.lossyScale;
        lt.localScale = new Vector3(1f / Mathf.Max(1e-4f, ls.x),
                                    1f / Mathf.Max(1e-4f, ls.y),
                                    1f / Mathf.Max(1e-4f, ls.z));
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
        SelectNearest();   // shared timer inside → only re-picks the casting lamp a few times a second
    }

    void Apply(float w)
    {
        if (lamp == null) return;

        bool on = w > 0.001f;
        if (lamp.enabled != on) lamp.enabled = on;
        if (!on) { if (lamp.shadows != LightShadows.None) lamp.shadows = LightShadows.None; return; }   // off all day

        // Only the player-nearest lamp casts (SelectNearest sets castShadow). Hard shadows — cheaper than soft
        // and the silhouette is a hard pixel-art cutout anyway.
        LightShadows want = (castShadow && GlobalShadows) ? LightShadows.Hard : LightShadows.None;
        if (lamp.shadows != want) lamp.shadows = want;
        if (want != LightShadows.None && lamp.shadowStrength != GlobalShadowStrength)
            lamp.shadowStrength = GlobalShadowStrength;

        // Gentle candle flicker (alloc-free Perlin; per-lamp seed so they don't pulse in sync).
        float fl = flicker > 0f ? 1f + (Mathf.PerlinNoise(seed, Time.time * flickerSpeed) - 0.5f) * 2f * flicker : 1f;

        lamp.color = color;
        lamp.intensity = intensity * GlobalIntensity * w * fl;
        // Lamp transform is world-unit (PlaceLamp counter-scales the prop), so range is plain world units.
        lamp.range = range * GlobalRange;
    }
}
