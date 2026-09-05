using System;

/// <summary>
/// What time it is in the world. THE one answer, and it is the clock of the machine running the SERVER.
///
/// <para><b>There is no second source and no fallback.</b> A server reads its own machine's clock directly — it
/// IS the world's clock, so there is nothing to ask and nothing to correct. A player's machine is TOLD that time
/// at the moment the server hands it a body, and counts forward from there against its own steady clock. A
/// machine that has not been told does not guess: <see cref="Synced"/> stays false, and nothing that has to
/// outlive the moment may be stamped until it is true.</para>
///
/// <para><b>Why every lasting timer is anchored here.</b> Unity's <c>Time.time</c> counts from the instant THAT
/// process started, so it is a different number in every process and begins again at zero on every restart. A
/// cooldown, a respawn or a day counted from it means nothing the moment it is saved, its map is put to sleep,
/// or its player moves to another server. An absolute moment from this clock survives all three.</para>
///
/// <para><b>What must NOT be read from here every frame.</b> A date is more expensive than a float compare, so
/// a timer that is CHECKED per frame keeps a <c>Time.time</c> mirror for the checking and uses the moment here
/// only as the value it is derived from and saved as. Timing that lives and dies inside one action — a wind-up,
/// an animation blend, a cast bar — never comes here at all; it outlives nothing, so it needs nothing.</para>
///
/// <para><b>The price of having no fallback, paid deliberately.</b> Nothing reconciles two server machines. If
/// the world is ever split across more than one, their clocks have to be kept in step by the machines
/// themselves, or a moment stamped by one is wrong when read by the other.</para>
/// </summary>
public static class WorldClock
{
    static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Seconds to ADD to this machine's own UTC clock to reach the server's. Always zero on a server, which is
    // the literal statement that a server's own clock IS the world's.
    static double offsetSeconds;

    /// <summary>True when this machine knows the world's time. Always true on a server, which owns it.</summary>
    public static bool Synced { get; private set; } = GameServer.IsServer;

    /// <summary>The world's current UTC time.</summary>
    public static DateTime UtcNow =>
        GameServer.IsServer ? DateTime.UtcNow : DateTime.UtcNow.AddSeconds(offsetSeconds);

    /// <summary>Seconds between <paramref name="epoch"/> and now, as the world counts them.</summary>
    public static double SecondsSince(DateTime epoch) => (UtcNow - epoch).TotalSeconds;

    /// <summary>The world's time as a plain number, which is the only shape it travels in.</summary>
    public static double EpochSecondsNow => (UtcNow - UnixEpoch).TotalSeconds;

    /// <summary>A moment off the wire, or out of a save, turned back into a moment.</summary>
    public static DateTime FromEpochSeconds(double seconds) => UnixEpoch.AddSeconds(seconds);

    /// <summary>A moment turned into the plain number a save or a message carries.</summary>
    public static double ToEpochSeconds(DateTime moment) => (moment - UnixEpoch).TotalSeconds;

    /// <summary>How long until <paramref name="moment"/>, never negative. Zero means it has already come.</summary>
    public static float SecondsUntil(DateTime moment)
    {
        double left = (moment - UtcNow).TotalSeconds;
        return left <= 0d ? 0f : (float)left;
    }

    /// <summary>The server saying what time it is, as it hands this machine a body. A server ignores it: it is
    /// the source, and a source that accepted a correction would no longer be one.</summary>
    public static void TakeFromServer(double serverEpochSeconds)
    {
        if (GameServer.IsServer || serverEpochSeconds <= 0d) return;

        offsetSeconds = (FromEpochSeconds(serverEpochSeconds) - DateTime.UtcNow).TotalSeconds;
        Synced = true;
    }
}
