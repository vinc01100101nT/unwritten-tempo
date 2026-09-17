using UnityEngine;

/// <summary>
/// THE ARROW A BOW LOOSES — one look, one speed, for every unit in the game that draws one. A person and a creature
/// holding a bow fire the identical thing, because there is only one of it here to fire.
///
/// <para><b>It is a picture and nothing else.</b> Nothing here rolls, decides or lands anything: the machine that
/// owns the world works out when the shot arrives (distance ÷ <see cref="Speed"/>) and lands the blow on its own
/// clock, then tells every machine that can see the shooter to draw this. So the arrow you watch and the damage you
/// take come from one decision, and a watcher's own frame rate can never move a blow.</para>
///
/// <para>It is drawn as a line rather than a picture because the game has no arrow art yet. When there is some, this
/// is the one place that changes and every bow in the game changes with it.</para>
/// </summary>
public static class Arrow
{
    /// <summary>How fast an arrow travels, in world units per second. One number for every bow — a person's and a
    /// creature's used to be two separate authored fields (14 and 9), so the same shot crossed the same gap at two
    /// different speeds depending on who fired it. 14 is the person's old value, kept because it is the one the game
    /// has been played at.</summary>
    public const float Speed = 14f;

    // How close counts as arrived, and how long a shot may chase before it gives up — the same numbers the sprite
    // projectiles have always flown with.
    const float ArriveRadius = 0.35f;
    const float MaxLife = 3f;

    // A pale shaft that fades out behind its own head, so the line reads as travelling rather than as a stick.
    static readonly Color Tint = new Color(1f, 0.94f, 0.72f, 0.95f);
    const float Width = 0.045f;
    const float Length = 0.7f;

    /// <summary>How long a shot from <paramref name="from"/> to <paramref name="to"/> is in the air. Asked by the
    /// machine that owns the world, to decide WHEN the blow lands; a plain function of two points and one speed, so
    /// it can be checked without standing the game up.</summary>
    public static float FlightSeconds(Vector3 from, Vector3 to)
        => Vector3.Distance(from, to) / Speed;

    /// <summary>Draw one arrow flying from <paramref name="from"/> to <paramref name="target"/> — or to
    /// <paramref name="fallbackPoint"/> when there is no body to home onto. It lands on nobody and takes nothing;
    /// it reaches the target and disappears.</summary>
    public static void Show(Vector3 from, Transform target, Vector3 fallbackPoint)
        => Projectile3D.SpawnLine(from, target, fallbackPoint, Speed, ArriveRadius, MaxLife, Tint, Width, Length);
}
