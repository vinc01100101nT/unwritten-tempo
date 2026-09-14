/// <summary>
/// Global weather quality gate. The HEAVY wet-ground + splash effects only run at <see cref="Tier.High"/>,
/// so they NEVER raise the minimum spec — Low/Medium ship the cheap rain overlay only. A graphics-quality
/// setting drives <see cref="Quality"/>. Default Medium = heavy off.
/// </summary>
public static class WeatherFX
{
    public enum Tier { Low = 0, Medium = 1, High = 2 }

    /// <summary>Current weather visual tier. Heavy effects check <see cref="Heavy"/>. Defaults to High on
    /// PC/editor (the launch target) but stays Medium on the WebGL demo so the heavy passes never ship there.</summary>
    public static Tier Quality =
#if UNITY_WEBGL && !UNITY_EDITOR
        Tier.Medium;
#else
        Tier.High;
#endif

    /// <summary>True when heavy effects (wet ground, splashes) are allowed to render.</summary>
    public static bool Heavy => Quality >= Tier.High;
}
