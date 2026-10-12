using System;

/// <summary>
/// The arithmetic of the KILL COMBO and the experience/zeny-per-hour meter, as plain functions that take numbers and
/// return numbers — so they can be checked without standing the game up (tools/check-game-math.sh).
///
/// <para><b>The combo.</b> Every kill that pays starts or carries a run: a kill within <see cref="WindowSeconds"/> of
/// the one before adds one to it, a kill after that starts it again at one. The run multiplies what each kill pays —
/// <see cref="BonusPercentPerKill"/> more for every kill in it, up to <see cref="MostBonusPercent"/> — never a flat
/// amount, so the bonus scales with whatever a creature is worth at any level.</para>
///
/// <para><b>Not the "combo total"</b> — that is the rising yellow number a multi-hit skill draws over a creature
/// (<see cref="RepeatsDamageTotal"/>). This is a run of KILLS.</para>
///
/// <para><b>The meter.</b> What kills paid, kept in <see cref="MeterBuckets"/> one-minute buckets, so "per hour" is
/// worked out from the last ten minutes and nothing older.</para>
/// </summary>
public static class KillComboMath
{
    /// <summary>Seconds a run waits for the next kill before it is over.</summary>
    public const double WindowSeconds = 10;

    /// <summary>How much more every kill in a run pays, in percent of what the kill is worth.</summary>
    public const int BonusPercentPerKill = 1;

    /// <summary>The most a run can add, in percent — reached at the fiftieth kill.</summary>
    public const int MostBonusPercent = 50;

    /// <summary>How many buckets the meter keeps, and how long each one covers.</summary>
    public const int MeterBuckets = 10;
    public const int BucketSeconds = 60;

    /// <summary>The shortest time a rate is worked out over: a single kill a few seconds after arriving would otherwise
    /// read as a rate a thousand times what anybody earns.</summary>
    public const double ShortestMeterSeconds = 60;

    /// <summary>The run after a kill at <paramref name="now"/>: one more than <paramref name="count"/> when the last kill,
    /// at <paramref name="lastKillAt"/>, was no more than <see cref="WindowSeconds"/> ago — exactly ten still counts —
    /// otherwise a fresh run of one. Moments are the world clock's seconds.</summary>
    public static int Step(int count, double lastKillAt, double now)
        => count > 0 && now - lastKillAt <= WindowSeconds && count < int.MaxValue ? count + 1 : 1;

    /// <summary>The moment a run that last killed at <paramref name="lastKillAt"/> is over.</summary>
    public static double EndsAt(double lastKillAt) => lastKillAt + WindowSeconds;

    /// <summary>How much more a kill pays at run <paramref name="count"/>, in whole percent.</summary>
    public static int BonusPercent(int count)
        => count <= 0 ? 0 : count >= MostBonusPercent / BonusPercentPerKill ? MostBonusPercent : count * BonusPercentPerKill;

    /// <summary>What a kill's pay is multiplied by at run <paramref name="count"/>: 1.24 at 24.</summary>
    public static double Multiplier(int count) => 1.0 + BonusPercent(count) / 100.0;

    /// <summary>What <paramref name="amount"/> pays at run <paramref name="count"/> — rounded to the nearest whole, halves
    /// up, by the same rule a world event's rate is (<see cref="WorldEventMath.Multiplied"/>).</summary>
    public static int Paid(int amount, int count) => WorldEventMath.Multiplied(amount, Multiplier(count));

    /// <summary>Which one-minute bucket the moment <paramref name="epochSeconds"/> falls in.</summary>
    public static long BucketOf(double epochSeconds) => (long)Math.Floor(epochSeconds / BucketSeconds);

    /// <summary>Empty every bucket the clock has moved past since <paramref name="lastBucket"/>, so the ring holds only the
    /// last <see cref="MeterBuckets"/> minutes. Returns the bucket now current. A clock that went backwards empties
    /// nothing and keeps the later bucket.</summary>
    public static long Roll(long[] buckets, long lastBucket, long nowBucket)
    {
        if (nowBucket <= lastBucket) return lastBucket;
        if (nowBucket - lastBucket >= buckets.Length)
        {
            Array.Clear(buckets, 0, buckets.Length);
            return nowBucket;
        }
        for (long b = lastBucket + 1; b <= nowBucket; b++) buckets[(int)(b % buckets.Length)] = 0;
        return nowBucket;
    }

    /// <summary>Put <paramref name="amount"/> into the bucket for <paramref name="nowBucket"/>.</summary>
    public static void Add(long[] buckets, long nowBucket, long amount)
    {
        if (amount <= 0) return;
        int i = (int)(nowBucket % buckets.Length);
        buckets[i] = buckets[i] > long.MaxValue - amount ? long.MaxValue : buckets[i] + amount;
    }

    /// <summary>Everything the buckets hold.</summary>
    public static long Sum(long[] buckets)
    {
        long total = 0;
        for (int i = 0; i < buckets.Length; i++) total = total > long.MaxValue - buckets[i] ? long.MaxValue : total + buckets[i];
        return total;
    }

    /// <summary>How many seconds the buckets cover at <paramref name="now"/>: the current minute so far and the nine full
    /// ones before it — or less, when the meter only started at <paramref name="meterStart"/>.</summary>
    public static double SecondsCovered(double meterStart, double now)
    {
        double ring = (MeterBuckets - 1) * (double)BucketSeconds + (now - BucketOf(now) * (double)BucketSeconds);
        return Math.Max(0, Math.Min(now - meterStart, ring));
    }

    /// <summary><paramref name="gained"/> over <paramref name="secondsCovered"/>, as a rate per hour — never over less
    /// than <see cref="ShortestMeterSeconds"/>, rounded to the nearest whole, halves up.</summary>
    public static long PerHour(long gained, double secondsCovered)
    {
        if (gained <= 0) return 0;
        double exact = Math.Round(gained * 3600.0 / Math.Max(ShortestMeterSeconds, secondsCovered), MidpointRounding.AwayFromZero);
        return exact >= long.MaxValue ? long.MaxValue : (long)exact;
    }
}
