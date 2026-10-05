using System.Collections.Generic;

/// <summary>WHERE A WALK CYCLE'S FEET LAND, AND HOW FAST THE CLIP WALKS — worked out from one ankle's path through the
/// cycle. Plain functions over plain numbers, so the rule can be checked by calling it with a recorded path.
///
/// <para>Both read the same two numbers per frame: how far the ankle is in front of the body's centre, and how high it
/// is. A foot swings forward through the air, lands in front of the body, then slides back under it while it carries
/// the weight — so it LANDS on the frame right after its furthest-forward point, and while it is on the ground it slides
/// back at exactly the speed the clip walks.</para></summary>
public static class StrideMath
{
    /// <summary>Add every frame <paramref name="forward"/> lands on, as <paramref name="right"/>'s plants, to
    /// <paramref name="into"/>. The clip loops, so frame 0 follows the last frame. A furthest-forward point higher than
    /// <paramref name="plantTolerance"/> above the foot's lowest point is a swing that never touched down, and is
    /// skipped.</summary>
    public static void FindPlants(float[] forward, float[] height, bool right, float plantTolerance, List<FootPlant> into)
    {
        int n = forward.Length;
        if (n < 3 || height.Length != n) return;
        float low = Lowest(height);
        for (int f = 0; f < n; f++)
        {
            float peak = forward[(f - 1 + n) % n];
            if (peak > forward[(f - 2 + n) % n] && peak >= forward[f] && height[f] <= low + plantTolerance)
                into.Add(new FootPlant { frame = f, right = right });
        }
    }

    /// <summary>How fast the foot that lands on <paramref name="plantFrame"/> slides back while it stays on the ground —
    /// the clip's walking speed, in the path's own units per second. "On the ground" is every frame from the landing
    /// that stays within <paramref name="contactTolerance"/> of the foot's lowest point. 0 when the foot is never down
    /// for two frames in a row.</summary>
    public static float ContactSpeed(float[] forward, float[] height, int plantFrame, float contactTolerance, float fps)
    {
        int n = forward.Length;
        if (n < 2 || height.Length != n || plantFrame < 0 || plantFrame >= n) return 0f;
        float low = Lowest(height);
        int steps = 0;
        while (steps < n - 1 && height[(plantFrame + steps + 1) % n] <= low + contactTolerance) steps++;
        if (steps == 0) return 0f;
        return (forward[plantFrame] - forward[(plantFrame + steps) % n]) / steps * fps;
    }

    /// <summary>The playback rate that keeps a planted foot still: how fast the body travels over the ground, divided by
    /// how fast its clip walks at normal speed. 1 when the clip's speed is unknown, so a body never freezes.</summary>
    public static float LockedPlayback(float groundSpeed, float clipSpeed)
        => clipSpeed > 1e-5f ? groundSpeed / clipSpeed : 1f;

    static float Lowest(float[] values)
    {
        float low = float.MaxValue;
        for (int i = 0; i < values.Length; i++) if (values[i] < low) low = values[i];
        return low;
    }
}
