using UnityEngine;

/// <summary>
/// A THROWN ARC — how long something launched at an angle stays in the air, how high it rises, and where it is part
/// of the way along. Plain numbers in, plain numbers out, so it is pinned by the game-maths checks without standing
/// the game up. A lobbed arrow's flight time, and so the moment it lands and hits, comes from here on every machine.
///
/// <para>Real throw physics for level ground: launched at <c>θ</c> to land <c>d</c> away under gravity <c>g</c>,
/// the flight lasts <c>√(2·d·tanθ / g)</c> and peaks <c>d·tanθ / 4</c> above the straight line. A landing higher or
/// lower than the launch is blended in along the way, which is close enough for a shot that stays inside one area.</para>
/// </summary>
public static class Ballistics
{
    /// <summary>Gravity in world units a second squared — one world unit is one metre.</summary>
    public const float Gravity = 9.81f;

    // A launch flatter or steeper than this stops being a lob: flat is a straight shot, vertical never lands anywhere.
    const float MinLaunchDegrees = 5f, MaxLaunchDegrees = 85f;

    static float Tan(float launchDegrees) =>
        Mathf.Tan(Mathf.Clamp(launchDegrees, MinLaunchDegrees, MaxLaunchDegrees) * Mathf.Deg2Rad);

    /// <summary>Seconds in the air for a throw at <paramref name="launchDegrees"/> landing
    /// <paramref name="distance"/> away. Never below one frame's worth, so a point-blank lob still arrives.</summary>
    public static float FlightSeconds(float distance, float launchDegrees, float gravity) =>
        Mathf.Max(0.05f, Mathf.Sqrt(2f * Mathf.Max(0f, distance) * Tan(launchDegrees) / Mathf.Max(0.01f, gravity)));

    /// <summary>How far the arc rises above the straight line between launch and landing, at its peak.</summary>
    public static float ApexHeight(float distance, float launchDegrees) =>
        Mathf.Max(0f, distance) * Tan(launchDegrees) * 0.25f;

    /// <summary>Where the thrown thing is <paramref name="s01"/> of the way through its flight (0 = launch,
    /// 1 = landing): the straight line between the two, lifted by the parabola.</summary>
    public static Vector3 PointAt(Vector3 from, Vector3 to, float apex, float s01)
    {
        float s = Mathf.Clamp01(s01);
        Vector3 p = Vector3.Lerp(from, to, s);
        p.y += 4f * apex * s * (1f - s);
        return p;
    }
}
