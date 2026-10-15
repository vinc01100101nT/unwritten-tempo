using UnityEngine;

/// <summary>
/// HOW A WEAPON THAT SLIPS OUT OF A DYING BODY'S HAND FALLS — thrown a little away from the body, spinning, bouncing
/// two or three times lower each time with its spin knocked a new way on every bounce, then tipping over flat onto its
/// widest side. Only a look: the weapon stays in the bag, and nothing here is a game number. Numbers in, numbers out,
/// so it runs without the game standing up.
///
/// <para>Its randomness is its own small generator fed a seed, so the same seed falls the same way. It is cosmetic, so
/// each screen seeds it itself and two screens may see slightly different falls — the scope <see cref="CombatRng"/>
/// sets for cosmetic randomness.</para>
/// </summary>
public static class WeaponTumble
{
    /// <summary>Pull downward, in world units a second squared — a little over real, so a small weapon does not
    /// float on a body this size.</summary>
    public const float Gravity = 14f;
    /// <summary>Share of the downward speed a bounce turns back into upward speed.</summary>
    public const float Bounce = 0.38f;
    /// <summary>Share of the sideways speed kept through a bounce.</summary>
    public const float SlideKept = 0.55f;
    /// <summary>Share of the spin kept through a bounce.</summary>
    public const float SpinKept = 0.5f;
    /// <summary>How far each bounce knocks the spin's direction off its old one (0 = never, 1 = as far as a whole new
    /// direction).</summary>
    public const float SpinKnock = 0.8f;
    /// <summary>The most bounces before it lies down, whatever its speed.</summary>
    public const int MaxBounces = 3;
    /// <summary>A bounce slower upward than this, in world units a second, is the last one.</summary>
    public const float LastBounceBelow = 0.6f;
    /// <summary>How long, in seconds, it takes to tip over flat after its last bounce.</summary>
    public const float LieDownSeconds = 0.3f;

    /// <summary>One falling weapon. <see cref="position"/> and <see cref="rotation"/> are its pivot in the world.</summary>
    public struct State
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 spinAxis;
        public float spinDegreesPerSecond;
        public int bounces;
        /// <summary>0 while still in the air; then 0→1 as it tips over flat after its last bounce.</summary>
        public float lying;
        public bool resting;
        public Quaternion lieFrom, restRotation;
        /// <summary>Its shape: a box in its own space, and its scale in the world.</summary>
        public Bounds box;
        public Vector3 scale;
        public uint seed;
    }

    /// <summary>Start a fall from where the weapon is now, thrown toward <paramref name="away"/> (flat; any length).</summary>
    public static State Begin(Vector3 position, Quaternion rotation, Bounds box, Vector3 scale, Vector3 away, uint seed)
    {
        var s = new State { position = position, rotation = rotation, box = box, scale = scale, seed = seed == 0 ? 1u : seed };
        away.y = 0f;
        if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
        away = Quaternion.AngleAxis(Between(ref s.seed, -40f, 40f), Vector3.up) * away.normalized;
        s.velocity = away * Between(ref s.seed, 0.6f, 1.3f) + Vector3.up * Between(ref s.seed, 1.2f, 2.2f);
        s.spinAxis = AnyDirection(ref s.seed);
        s.spinDegreesPerSecond = Between(ref s.seed, 420f, 900f);
        return s;
    }

    /// <summary>Move the fall on by <paramref name="dt"/> seconds over ground at height <paramref name="groundY"/>.
    /// False once it is lying still, so the caller can stop asking.</summary>
    public static bool Step(ref State s, float dt, float groundY)
    {
        if (s.resting) return false;

        if (s.lying > 0f)
        {
            s.lying = Mathf.Min(1f, s.lying + dt / LieDownSeconds);
            float k = s.lying * s.lying * (3f - 2f * s.lying);
            s.rotation = Quaternion.Slerp(s.lieFrom, s.restRotation, k);
            s.position += new Vector3(s.velocity.x, 0f, s.velocity.z) * (dt * (1f - k));
            s.position.y = groundY - Lowest(s.rotation, s.box, s.scale);
            if (s.lying >= 1f) s.resting = true;
            return true;
        }

        s.velocity.y -= Gravity * dt;
        s.position += s.velocity * dt;
        s.rotation = Quaternion.AngleAxis(s.spinDegreesPerSecond * dt, s.spinAxis) * s.rotation;

        float low = s.position.y + Lowest(s.rotation, s.box, s.scale);
        if (low >= groundY || s.velocity.y >= 0f) return true;

        s.position.y += groundY - low;
        s.bounces++;
        float up = -s.velocity.y * Bounce;
        s.velocity.x *= SlideKept;
        s.velocity.z *= SlideKept;
        s.spinDegreesPerSecond *= SpinKept;
        s.spinAxis = s.spinAxis + AnyDirection(ref s.seed) * SpinKnock;
        s.spinAxis = s.spinAxis.sqrMagnitude > 1e-6f ? s.spinAxis.normalized : Vector3.up;

        if (up < LastBounceBelow || s.bounces >= MaxBounces)
        {
            s.velocity.y = 0f;
            s.lieFrom = s.rotation;
            s.restRotation = Flat(s.rotation, s.box, s.scale);
            s.lying = 1e-4f;
        }
        else s.velocity.y = up;
        return true;
    }

    /// <summary>How far above its pivot the lowest corner of its box is when turned by <paramref name="rotation"/>
    /// (negative when the box reaches below the pivot).</summary>
    public static float Lowest(Quaternion rotation, Bounds box, Vector3 scale)
    {
        float low = float.MaxValue;
        var c = box.center;
        var e = box.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x),
                                     c.y + ((i & 2) == 0 ? -e.y : e.y),
                                     c.z + ((i & 4) == 0 ? -e.z : e.z));
            float y = (rotation * Vector3.Scale(scale, corner)).y;
            if (y < low) low = y;
        }
        return low;
    }

    /// <summary>The nearest turn of <paramref name="rotation"/> that lays the weapon on its widest side: its thinnest
    /// direction pointing straight up (or straight down, whichever is nearer).</summary>
    public static Quaternion Flat(Quaternion rotation, Bounds box, Vector3 scale)
    {
        var size = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        var thin = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
        var worldThin = rotation * thin;
        if (worldThin.y < 0f) worldThin = -worldThin;
        return Quaternion.FromToRotation(worldThin, Vector3.up) * rotation;
    }

    // A small generator of its own (xorshift), so a fall is fixed by its seed and shares no state with anything.
    static float Next01(ref uint seed)
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        return (seed & 0xFFFFFF) / 16777216f;
    }

    static float Between(ref uint seed, float lo, float hi) => lo + (hi - lo) * Next01(ref seed);

    static Vector3 AnyDirection(ref uint seed)
    {
        var v = new Vector3(Between(ref seed, -1f, 1f), Between(ref seed, -1f, 1f), Between(ref seed, -1f, 1f));
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.up;
    }
}
