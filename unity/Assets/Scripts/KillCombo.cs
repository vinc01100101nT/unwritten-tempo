/// <summary>
/// SERVER: one character's KILL COMBO and its experience/zeny-per-hour meter, held on the server's character the way
/// the ring is. The arithmetic is <see cref="KillComboMath"/>; this only remembers.
///
/// <para><b>No timer.</b> A run that has gone quiet is not ended by anything ticking: the next kill finds it more than
/// <see cref="KillComboMath.WindowSeconds"/> old and starts again at one, and the player's machine stops drawing it at
/// the end moment it was sent. Nothing here runs between kills.</para>
///
/// <para><b>Not saved.</b> It lives as long as the server's character does — through this map, and gone with any door
/// or disconnect, each of which makes a new one.</para>
///
/// <para>About 200 bytes per person playing.</para>
/// </summary>
public sealed class KillCombo
{
    /// <summary>THE clock every run and meter is measured on: this server process's steady clock, which never jumps
    /// back. The world clock can (an operator or a time sync moving it), and a run measured on it would carry across a
    /// gap of any length. Only the end moment sent to the player is turned into the world's time, at the moment it is
    /// sent (<see cref="SecondsLeft"/>).</summary>
    public static double Now => UnityEngine.Time.realtimeSinceStartupAsDouble;

    /// <summary>The run as it stands after the last kill — not yet ended even when its window has passed, because the
    /// next kill is what decides that.</summary>
    public int Count { get; private set; }

    /// <summary>The moment of the last kill that counted, on <see cref="Now"/>'s clock.</summary>
    public double LastKillAt { get; private set; }

    /// <summary>The rates last sent to the player, so a rate that has fallen is sent once and an unchanged one never.</summary>
    public long SentExperiencePerHour, SentZenyPerHour;

    /// <summary>Seconds until the run ends at <paramref name="now"/>, never below zero.</summary>
    public double SecondsLeft(double now) => System.Math.Max(0.0, KillComboMath.EndsAt(LastKillAt) - now);

    double meterStart = double.NaN;
    long lastBucket;
    readonly long[] experience = new long[KillComboMath.MeterBuckets];
    readonly long[] zeny = new long[KillComboMath.MeterBuckets];

    // ONE DEATH, ONE STEP. A creature that turns back asks for the run before its killer is paid, in the same frame, and
    // both must be told the same number.
    int steppedForVictim;
    int steppedOnFrame = -1;

    /// <summary>The meter starts counting now, if it has not already — on arrival, so time spent before the first kill
    /// is part of "per hour".</summary>
    public void StartMeter(double now)
    {
        if (!double.IsNaN(meterStart)) return;
        meterStart = now;
        lastBucket = KillComboMath.BucketOf(now);
    }

    /// <summary>A kill of the unit with instance id <paramref name="victimId"/> counts, on frame <paramref name="frame"/>:
    /// the run steps once and its count is returned. Asked again for the same death, it returns the same count without
    /// stepping.</summary>
    public int CountKill(int victimId, int frame, double now)
    {
        if (victimId == steppedForVictim && frame == steppedOnFrame) return Count;
        StartMeter(now);
        Count = KillComboMath.Step(Count, LastKillAt, now);
        LastKillAt = now;
        steppedForVictim = victimId;
        steppedOnFrame = frame;
        return Count;
    }

    /// <summary>A kill paid <paramref name="experiencePaid"/> experience and <paramref name="zenyPaid"/> zeny at
    /// <paramref name="now"/>: both go into the meter.</summary>
    public void Earned(double now, long experiencePaid, long zenyPaid)
    {
        StartMeter(now);
        RollTo(now);
        KillComboMath.Add(experience, lastBucket, experiencePaid);
        KillComboMath.Add(zeny, lastBucket, zenyPaid);
    }

    /// <summary>Experience per hour over the last ten minutes, at <paramref name="now"/>.</summary>
    public long ExperiencePerHour(double now) => RateOf(experience, now);

    /// <summary>Zeny per hour over the last ten minutes, at <paramref name="now"/>.</summary>
    public long ZenyPerHour(double now) => RateOf(zeny, now);

    // Both rings empty the same minutes, from the same last bucket.
    void RollTo(double now)
    {
        long nowBucket = KillComboMath.BucketOf(now);
        KillComboMath.Roll(zeny, lastBucket, nowBucket);
        lastBucket = KillComboMath.Roll(experience, lastBucket, nowBucket);
    }

    long RateOf(long[] buckets, double now)
    {
        if (double.IsNaN(meterStart)) return 0;
        RollTo(now);
        return KillComboMath.PerHour(KillComboMath.Sum(buckets), KillComboMath.SecondsCovered(meterStart, now));
    }
}
