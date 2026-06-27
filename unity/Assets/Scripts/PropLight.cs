using UnityEngine;

/// <summary>
/// A reusable real-time point light for emissive props — door glows, torches, braziers, lanterns,
/// portals. Drop it on (or near) a prop and it configures a URP point <see cref="Light"/> with a warm
/// colour, range, and intensity, so nearby billboard sprites (player/mobs) brighten and take on the
/// light's colour via <c>Unwritten/BillboardLit</c>'s additional-light pass. Optional cheap flicker
/// (torch/candle) is allocation-free (one PerlinNoise read per frame, only when enabled).
///
/// Why a component and not just a raw Light: keeps lamp authoring one-click + consistent (same warm
/// preset everywhere), and gives a single place to add behaviour (flicker, day/night gating) later.
/// Additional-light shadows are OFF in the URP asset, so this never adds a shadow pass.
/// </summary>
[RequireComponent(typeof(Light))]
public class PropLight : MonoBehaviour
{
    [Header("Light")]
    [Tooltip("Lamp colour. Warm by default (candle/door glow).")]
    public Color color = new Color(1f, 0.72f, 0.36f);
    [Tooltip("How far the glow reaches, in world units.")]
    public float range = 4.5f;
    [Tooltip("Brightness. URP point lights read this directly.")]
    public float intensity = 3f;

    [Header("Flicker (torch / candle)")]
    [Tooltip("Subtly vary the intensity each frame for a living flame.")]
    public bool flicker = false;
    [Range(0f, 1f)] [Tooltip("Fraction of intensity the flicker swings (0.15 = ±15%).")]
    public float flickerAmount = 0.15f;
    [Tooltip("How fast the flicker wanders.")]
    public float flickerSpeed = 7f;

    Light lamp;
    float baseIntensity;
    float noiseSeed;

    void Awake()
    {
        lamp = GetComponent<Light>();
        Apply();
        baseIntensity = intensity;
        noiseSeed = Random.value * 100f;   // each lamp flickers out of phase
    }

    /// <summary>Push the serialized fields onto the URP Light. Called on Awake and after editor tweaks.</summary>
    public void Apply()
    {
        if (lamp == null) lamp = GetComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = color;
        lamp.range = range;
        lamp.intensity = intensity;
        lamp.shadows = LightShadows.None;   // URP additional-light shadows are disabled project-wide
        lamp.renderMode = LightRenderMode.Auto;
        baseIntensity = intensity;
    }

    void Update()
    {
        if (!flicker) return;
        // Allocation-free wander around the base intensity (PerlinNoise is cheap and smooth, unlike Random).
        float n = Mathf.PerlinNoise(noiseSeed, Time.time * flickerSpeed) * 2f - 1f;
        lamp.intensity = baseIntensity * (1f + n * flickerAmount);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying && TryGetComponent(out Light _)) Apply();
    }
#endif

    /// <summary>
    /// One-call lamp authoring for generators / scene builders: add (or reuse) a child light at the given
    /// local offset on <paramref name="parent"/>. Returns the <see cref="PropLight"/> so the caller can
    /// tweak it (e.g. enable flicker for a torch). Idempotent — re-running reuses the existing child.
    /// </summary>
    public static PropLight Attach(Transform parent, Vector3 localOffset, Color color, float range,
                                   float intensity, string childName = "PropLight")
    {
        Transform t = parent.Find(childName);
        GameObject go = t != null ? t.gameObject : new GameObject(childName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localOffset;
        var pl = go.GetComponent<PropLight>();
        if (pl == null) pl = go.AddComponent<PropLight>();
        pl.color = color;
        pl.range = range;
        pl.intensity = intensity;
        pl.Apply();
        return pl;
    }
}
