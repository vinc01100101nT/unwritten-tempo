using System;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// Asks the internet what time it really is, so the server's clock can be set from it once at start
/// (<see cref="WorldClock"/>). It tries NTP time servers first, then falls back to the Date line of a web page's
/// answer. Blocking, with short timeouts — it runs once while the server starts, before any map is up, and never
/// on a player's machine.
///
/// <para>The sums that turn an answer into a correction are plain functions of numbers (<see cref="NtpUtc"/>,
/// <see cref="OffsetSeconds"/>), so each can be checked without a network.</para>
/// </summary>
public static class InternetTime
{
    static readonly string[] NtpHosts = { "time.google.com", "time.cloudflare.com", "pool.ntp.org" };
    const string WebFallback = "https://www.google.com";
    const int TimeoutMs = 2000;
    static readonly DateTime NtpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Seconds to ADD to this machine's own UTC clock to reach the real time, and where it came from.
    /// False when nothing answered.</summary>
    public static bool TryFetchOffset(out double offsetSeconds, out string source)
    {
        for (int i = 0; i < NtpHosts.Length; i++)
            if (TryNtp(NtpHosts[i], out offsetSeconds)) { source = "NTP " + NtpHosts[i]; return true; }
        if (TryWeb(WebFallback, out offsetSeconds)) { source = "the Date line from " + WebFallback; return true; }
        offsetSeconds = 0;
        source = null;
        return false;
    }

    static bool TryNtp(string host, out double offsetSeconds)
    {
        offsetSeconds = 0;
        try
        {
            var request = new byte[48];
            // Leap indicator 0, version 3, mode 3 (a client asking).
            request[0] = 0x1B;
            using (var udp = new UdpClient())
            {
                udp.Client.ReceiveTimeout = TimeoutMs;
                udp.Client.SendTimeout = TimeoutMs;
                udp.Connect(host, 123);
                DateTime sent = DateTime.UtcNow;
                udp.Send(request, request.Length);
                var from = new IPEndPoint(IPAddress.Any, 0);
                byte[] reply = udp.Receive(ref from);
                DateTime received = DateTime.UtcNow;
                if (reply == null || reply.Length < 48) return false;
                offsetSeconds = OffsetSeconds(NtpUtc(reply, 40), sent, received);
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    static bool TryWeb(string url, out double offsetSeconds)
    {
        offsetSeconds = 0;
        try
        {
            var ask = (HttpWebRequest)WebRequest.Create(url);
            ask.Method = "HEAD";
            ask.Timeout = TimeoutMs;
            DateTime sent = DateTime.UtcNow;
            using (var answer = (HttpWebResponse)ask.GetResponse())
            {
                DateTime received = DateTime.UtcNow;
                string date = answer.Headers["Date"];
                if (string.IsNullOrEmpty(date)) return false;
                DateTime said = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
                // The Date line is cut to the whole second, so the true moment is on average half a second later.
                offsetSeconds = OffsetSeconds(said.AddSeconds(0.5), sent, received);
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The UTC moment in an NTP timestamp: 32 bits of seconds since 1900, then 32 bits of fraction,
    /// big-endian, starting at <paramref name="at"/>.</summary>
    public static DateTime NtpUtc(byte[] packet, int at)
    {
        ulong seconds = ((ulong)packet[at] << 24) | ((ulong)packet[at + 1] << 16) | ((ulong)packet[at + 2] << 8) | packet[at + 3];
        ulong fraction = ((ulong)packet[at + 4] << 24) | ((ulong)packet[at + 5] << 16) | ((ulong)packet[at + 6] << 8) | packet[at + 7];
        return NtpEpoch.AddSeconds(seconds + fraction / 4294967296.0);
    }

    /// <summary>How far this machine's clock is behind the real one: the time the far end said, moved on by half the
    /// round trip, less this machine's clock when the answer arrived.</summary>
    public static double OffsetSeconds(DateTime saidUtc, DateTime sentLocal, DateTime receivedLocal)
    {
        double roundTrip = (receivedLocal - sentLocal).TotalSeconds;
        return (saidUtc.AddSeconds(roundTrip / 2.0) - receivedLocal).TotalSeconds;
    }
}
