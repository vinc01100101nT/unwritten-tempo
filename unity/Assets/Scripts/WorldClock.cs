using System;
using UnityEngine;

/// <summary>
/// What time it is in the world, decided by the SERVER.
///
/// <para>The day/night cycle used to be computed from the clock on the player's own machine. That is fine when
/// there is one player and wrong the moment there are two: changing your computer's date moved your world's
/// sun while everybody else's stayed where it was. The server's clock is the one everybody shares, so it is
/// the one that decides.</para>
///
/// <para>Asked ONCE, when signing in, and never again: the answer is turned into an offset from this machine's
/// own steady timer, and the time is counted forward from there. So there is no request per frame, no drift
/// from a clock being changed mid-session, and no dependency on the network staying up.</para>
///
/// <para>Until the answer arrives the local clock stands in — a sky has to be drawn on the very first frame,
/// and a few seconds of possibly-wrong daylight before the answer lands is better than no sky at all.</para>
/// </summary>
public static class WorldClock
{
    // Seconds to ADD to this machine's UTC clock to get the server's. Zero until the server has answered,
    // which is exactly "use the local clock for now".
    static double offsetSeconds;

    /// <summary>True once the server has said what time it is.</summary>
    public static bool Synced { get; private set; }

    /// <summary>The world's current UTC time.</summary>
    public static DateTime UtcNow => DateTime.UtcNow.AddSeconds(offsetSeconds);

    /// <summary>Seconds between <paramref name="epoch"/> and now, as the world counts them.</summary>
    public static double SecondsSince(DateTime epoch) => (UtcNow - epoch).TotalSeconds;

    /// <summary>Ask the account service what time it is. Called once, at sign-in. Harmless to call again —
    /// it simply re-measures the offset.</summary>
    public static void Sync()
    {
        AccountClient.Send("GET", "/time", null, false, (ok, body, error) =>
        {
            if (!ok || string.IsNullOrEmpty(body))
            {
                // Not fatal, and not silent: the world keeps running on the local clock, which is what it did
                // before this existed. Saying so is what stops it looking like the sky is simply wrong.
                Debug.LogWarning("[unwritten] Could not read the world clock from the server (" + error +
                                 ") — running on this machine's clock until the next sign-in.");
                return;
            }

            var answer = JsonUtility.FromJson<ServerTime>(body);
            if (answer == null || answer.epochSeconds <= 0d) return;

            var serverNow = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(answer.epochSeconds);
            offsetSeconds = (serverNow - DateTime.UtcNow).TotalSeconds;
            Synced = true;
        });
    }

    [Serializable]
    class ServerTime
    {
        public double epochSeconds;
    }
}
