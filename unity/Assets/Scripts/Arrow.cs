using UnityEngine;

/// <summary>
/// WHAT A STREAK IN THE AIR LOOKS LIKE — an arrow, or a bouncing skill with no look of its own. One look for every
/// unit in the game that draws one, because there is only one of it here.
///
/// <para><b>It is a picture and nothing else.</b> When a shot leaves and arrives, and what it lands, is the flight
/// store's (<see cref="Flights"/>) on the machine that owns the world; every machine that can see the shooter draws
/// the leg it is told (<see cref="FlightPictures"/>), and that drawing reads these numbers.</para>
///
/// <para>It is drawn as a line rather than a picture because the game has no arrow art yet. When there is some, this
/// is the one place that changes and every bow in the game changes with it.</para>
/// </summary>
public static class Arrow
{
    // A pale shaft that fades out behind its own head, so the line reads as travelling rather than as a stick.
    public static readonly Color Tint = new Color(1f, 0.94f, 0.72f, 0.95f);
    public const float Width = 0.045f;
    public const float Length = 0.7f;

    /// <summary>THE ARROW A BOW SHOOTS — a real model, lying along its own forward (+Z), tip first, sized to the
    /// world's bodies. Every bow arrow in the air draws this one, its ordinary shots and its skills alike. Loaded once,
    /// the first time a bow fires on this machine; null if the model is missing, and the line is drawn instead.</summary>
    public static GameObject Model
    {
        get
        {
            if (!modelLooked) { modelLooked = true; model = Resources.Load<GameObject>("Projectiles/BowArrow"); }
            return model;
        }
    }
    static GameObject model;
    static bool modelLooked;
}
