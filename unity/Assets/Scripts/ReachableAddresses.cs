using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

/// <summary>
/// WHERE PLAYERS CAN REACH THIS MAP SERVER — said only from what this computer actually holds right now.
///
/// <para>The same rule as the other three servers (the sign-in service, the character server,
/// tools/serve-folder.py): no address is printed unless it is really there, the address players are handed
/// (<c>MAP_SERVER_ADDRESS</c>) is checked and reported in red when it is not, and the report is made again when this
/// computer's addresses change — an adapter that comes up after the server started (Tailscale, a new Wi-Fi) is
/// said when it happens.</para>
///
/// <para><b>Why this one checks holding rather than connecting.</b> The other three are tried by opening a
/// connection to them. Players reach this one over UDP, which has no connection to open — so what is checked is
/// that the address is one of this computer's own. The players' door is bound to every address, so every address
/// this computer holds is one it listens on.</para>
/// </summary>
public static class ReachableAddresses
{
    const int RecheckMs = 10_000;
    static Timer watching;

    /// <summary>address -> adapter name, for every IPv4 address this computer holds right now. 169.254.x.x is left
    /// out: it is the address an adapter gives ITSELF when it has no network, so nobody can reach it there. On
    /// Windows an address that is being given up or not yet confirmed is left out too, as the other servers do.</summary>
    static SortedDictionary<string, string> Own()
    {
        var found = new SortedDictionary<string, string>(InNumberOrder);
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var one in adapter.GetIPProperties().UnicastAddresses)
            {
                if (one.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var text = one.Address.ToString();
                if (text.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                if (!Settled(one)) continue;
                found[text] = adapter.Name;
            }
        }
        return found;
    }

    static bool Settled(UnicastIPAddressInformation one)
    {
        // Only Windows reports this; everywhere else it throws, and every address it lists is in use.
        try { return one.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred; }
        catch (Exception) { return true; }
    }

    // 10.x before 127.x before 192.x — as numbers, the order the other three servers print them in.
    static readonly IComparer<string> InNumberOrder = Comparer<string>.Create((a, b) =>
    {
        var x = IPAddress.Parse(a).GetAddressBytes();
        var y = IPAddress.Parse(b).GetAddressBytes();
        for (int i = 0; i < x.Length; i++)
            if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        return 0;
    });

    static void Report(int port, string givenAddress)
    {
        var own = Own();
        if (own.Count == 0)
        {
            GameLog.Error(LogCat.Server, GameNetwork.Shout("this computer holds no address players could reach."));
        }
        else
        {
            // One log line per address: every line of the server's log carries its own date and time, and the
            // tools that read the log expect exactly that.
            int width = 0;
            foreach (var address in own.Keys) width = Math.Max(width, (address + ":" + port).Length);
            GameLog.Info(LogCat.Server, "listening for players on UDP port " + port + " at:");
            foreach (var kv in own)
                GameLog.Info(LogCat.Server, "  " + (kv.Key + ":" + port).PadRight(width) + "   " + kv.Value);
        }

        if (Holds(own, givenAddress))
            GameLog.Info(LogCat.Server, "MAP_SERVER_ADDRESS " + givenAddress + " is one of this computer's addresses "
                                        + "— that is the address players are sent to.");
        else
            GameLog.Error(LogCat.Server, GameNetwork.Shout("MAP_SERVER_ADDRESS " + givenAddress + " is not one of this "
                                        + "computer's addresses right now — players sent there cannot reach this "
                                        + "server unless that address forwards to this computer."));
    }

    static bool Holds(SortedDictionary<string, string> own, string address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        try
        {
            foreach (var resolved in Dns.GetHostAddresses(address))
                if (resolved.AddressFamily == AddressFamily.InterNetwork && own.ContainsKey(resolved.ToString()))
                    return true;
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>Server-only: say where players can reach this server, now and every time this computer's addresses
    /// change. Watched on a timer thread, never on the frame — listing the adapters asks the operating system.</summary>
    public static void ReportAndWatch(int port, string givenAddress)
    {
        Stop();
        Report(port, givenAddress);

        var seen = string.Join(",", Own().Keys);
        watching = new Timer(_ =>
        {
            try
            {
                var now = string.Join(",", Own().Keys);
                if (now == seen) return;
                seen = now;
                GameLog.Info(LogCat.Server, "this computer's addresses changed:");
                Report(port, givenAddress);
            }
            catch (Exception e)
            {
                GameLog.Warn(LogCat.Server, "could not list this computer's addresses — " + e.Message);
            }
        }, null, RecheckMs, RecheckMs);
    }

    /// <summary>Stop watching. A timer thread outlives the world it was started for — in the editor it would go on
    /// reporting after Play has ended, and a second Play would start a second one beside it.</summary>
    public static void Stop()
    {
        watching?.Dispose();
        watching = null;
    }
}
