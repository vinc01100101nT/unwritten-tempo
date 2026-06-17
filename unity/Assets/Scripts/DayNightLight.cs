using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drives the directional Sun's color, intensity, sky angle, and the scene's ambient fill from the shared
/// <see cref="TimeOfDay"/> clock — so the world visibly goes from warm bright noon to dim blue midnight and
/// back. Lives on the Systems Sun (added by <c>SystemsSceneBuilder</c>). All values lerp by
/// <see cref="TimeOfDay.Daylight01"/>, so it crossfades smoothly with the day/night ambience.
/// </summary>
[RequireComponent(typeof(Light))]
public class DayNightLight : MonoBehaviour
{
    [Header("Sun color + brightness")]
    public Color dayColor = new Color(1f, 0.96f, 0.90f);     // warm midday
    public Color nightColor = new Color(0.50f, 0.60f, 0.95f); // cool moonlight
    public float dayIntensity = 1.1f;
    [Tooltip("Kept low but non-zero so night is dark, not pitch-black.")]
    public float nightIntensity = 0.18f;

    [Header("Ambient fill (whole-scene)")]
    public bool driveAmbient = true;
    public Color dayAmbient = new Color(0.55f, 0.56f, 0.60f);
    public Color nightAmbient = new Color(0.10f, 0.12f, 0.22f);

    [Header("Sun angle")]
    public bool rotateSun = true;
    [Tooltip("Pitch above the horizon at noon.")]    public float noonPitch = 68f;
    [Tooltip("Pitch at sunrise/sunset (near horizon).")] public float horizonPitch = 6f;
    public float yaw = -30f;

    Light sun;

    void Awake()
    {
        sun = GetComponent<Light>();
        if (driveAmbient) RenderSettings.ambientMode = AmbientMode.Flat;   // so ambientLight actually applies
    }

    void Update()
    {
        var tod = TimeOfDay.Instance;
        float day = tod != null ? tod.Daylight01 : 1f;

        sun.color = Color.Lerp(nightColor, dayColor, day);
        sun.intensity = Mathf.Lerp(nightIntensity, dayIntensity, day);

        if (driveAmbient)
            RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient, day);

        if (rotateSun)
            sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(horizonPitch, noonPitch, day), yaw, 0f);
    }
}
