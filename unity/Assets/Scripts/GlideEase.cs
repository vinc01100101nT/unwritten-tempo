using UnityEngine;

/// <summary>
/// How fast a skill glide (a Dash, or a shove routed through one) travels at each point of its trip, as a plain
/// function of numbers. The body that performs a glide — a person's legs or a creature's pathing agent — and every
/// screen that only WATCHES it ask this one function, so the hop drawn for somebody else is the hop they actually took.
/// </summary>
public static class GlideEase
{
    /// <summary>A GLIDE THAT HITS SOMETHING STOPS WHERE IT IS. True when one frame's move closed less than half of the
    /// <paramref name="step"/> it tried — a wall, a creature, any collision in the way — so the body ends the glide on
    /// the spot instead of pushing against it (or sliding round it) until its time runs out. A strike riding the dash
    /// then fails, because the body never reached where it was sent (<see cref="DashLandings"/>). The person's legs and
    /// a creature's pathing agent both ask this.</summary>
    public static bool Blocked(float leftBefore, float leftAfter, float step)
        => step > 1e-4f && leftBefore - leftAfter < step * 0.5f;

    /// <summary>The glide speed with <paramref name="remaining"/> of <paramref name="total"/> distance left. Each ramp
    /// spans <paramref name="easeSeconds"/> of travel at full speed, clamped to the trip, so a dash shorter than the
    /// window eases the whole way; a floor of a fifth keeps an eased glide from stalling short of its end. No easing =
    /// constant <paramref name="speed"/>.</summary>
    public static float Speed(float speed, float remaining, float total, bool easeIn, bool easeOut, float easeSeconds)
    {
        if (!easeIn && !easeOut) return speed;
        const float floor = 0.2f;
        float d = Mathf.Clamp(easeSeconds * speed, 1e-4f, total);
        float f = 1f;
        if (easeIn)  f = Mathf.Min(f, Mathf.Lerp(floor, 1f, Mathf.SmoothStep(0f, 1f, (total - remaining) / d)));
        if (easeOut) f = Mathf.Min(f, Mathf.Lerp(floor, 1f, Mathf.SmoothStep(0f, 1f, remaining / d)));
        return speed * f;
    }

    /// <summary>How long a glide of <paramref name="total"/> metres takes at <paramref name="speed"/>, shaped by the same
    /// ease as <see cref="Speed"/> — what a strike that lands on arrival waits for. Summed over 32 short stretches, once
    /// per dash, never per frame. 0 when there is no trip.</summary>
    public static float Seconds(float speed, float total, bool easeIn, bool easeOut, float easeSeconds)
    {
        if (speed <= 0f || total <= 0f) return 0f;
        if (!easeIn && !easeOut) return total / speed;
        const int Stretches = 32;
        float stretch = total / Stretches, seconds = 0f;
        for (int i = 0; i < Stretches; i++)
        {
            float remaining = total - (i + 0.5f) * stretch;
            seconds += stretch / Mathf.Max(1e-4f, Speed(speed, remaining, total, easeIn, easeOut, easeSeconds));
        }
        return seconds;
    }

    /// <summary>A creature's knockback speed one frame later: it starts at the shove's speed and slows evenly to a
    /// stop over the shove's length. The creature's agent and every screen watching it step it the same way.</summary>
    public static Vector3 KnockbackSlowed(Vector3 velocity, float slowPerSecond, float deltaTime)
        => Vector3.MoveTowards(velocity, Vector3.zero, slowPerSecond * deltaTime);
}
