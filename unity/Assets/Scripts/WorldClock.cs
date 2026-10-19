using System;

/// <summary>
/// What time it is in the world. THE one answer, and it is the clock of the machine running the SERVER, set from
/// the internet's real time once when the server starts.
///
/// <para><b>How the server's clock is set.</b> As a server starts, before any map is up, it asks the internet
/// what time it really is (<see cref="InternetTime"/>) and keeps how far its own machine's clock is off. From then
/// on its time is its machine's steady clock plus that correction, so a machine whose clock runs fast or slow
/// does not move the world. If nothing answers, the server says so in its log and runs on its machine's clock
/// as it is. A player's machine is TOLD the server's time at the moment the server hands it a body, and counts
/// forward from there against its own steady clock. A machine that has not been told does not guess:
/// <see cref="Synced"/> stays false, and nothing that has to outlive the moment may be stamped until it is true.</para>
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
/// <para><b>Two server machines agree because both ask the internet.</b> Each sets itself from the real time on
/// its own; one whose question went unanswered runs on its machine's clock and can disagree by however far that
/// clock is off — its log says so when it starts.</para>
/// </summary>
public static class WorldClock
{
    static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Seconds to ADD to this machine's own UTC clock to reach the server's. Unused on a server.
    static double offsetSeconds;
    // Seconds to ADD to a server machine's own UTC clock to reach the real time, asked of the internet at start.
    static double serverCorrectionSeconds;

    /// <summary>True when this machine knows the world's time. Always true on a server, which owns it.</summary>
    public static bool Synced { get; private set; } = GameServer.IsServer;

    /// <summary>The world's current UTC time.</summary>
    public static DateTime UtcNow =>
        DateTime.UtcNow.AddSeconds(GameServer.IsServer ? serverCorrectionSeconds : offsetSeconds);

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

    // Once, as a server starts and before any map is up — so every moment it ever stamps is on the corrected clock.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void SetServerClockFromInternet()
    {
        if (!GameServer.IsServer) return;
        if (InternetTime.TryFetchOffset(out double correction, out string source))
        {
            serverCorrectionSeconds = correction;
            GameLog.Info(LogCat.Server, $"World clock set from {source}: this machine's clock was {correction:+0.000;-0.000}s off.");
        }
        else
            GameLog.Warn(LogCat.Server, "World clock: no internet time server answered — running on this machine's own clock.");
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
