using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CharServer;

/// <summary>
/// WHERE THIS SERVER CAN ACTUALLY BE REACHED — said only after trying it.
///
/// <para>The same rule as the other three servers (the sign-in service, the map server, tools/serve-folder.py): no
/// address is printed as reachable unless a connection to it was just made from this computer, the address
/// players are handed is tried the same way and reported in red when it does not answer, and the whole report is
/// made again when this computer's own addresses change — an adapter that comes up after the server started
/// (Tailscale, a new Wi-Fi) is said when it happens, not left looking down for the rest of the run.</para>
/// </summary>
public static class Addresses
{
    static readonly TimeSpan Recheck = TimeSpan.FromSeconds(10);
    const int ProbeMs = 1000;

    // RED THROUGH THE CONSOLE'S OWN COLOUR, never escape codes: a Windows console window prints "←[31m" literally
    // unless the program switches it into a mode .NET never asks for, and a log file would keep the codes as junk.
    // Held under one lock, because the colour belongs to the whole window and another line could land in it.
    static readonly object writing = new();

    internal static void WriteRed(string line)
    {
        lock (writing)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(line);
            Console.ResetColor();
        }
    }

    // 10.x before 127.x before 192.x — as numbers, the order the other three servers print them in.
    static readonly Comparer<string> InNumberOrder = Comparer<string>.Create((a, b) =>
        ((ReadOnlySpan<byte>)IPAddress.Parse(a).GetAddressBytes()).SequenceCompareTo(IPAddress.Parse(b).GetAddressBytes()));

    /// <summary>address -> adapter name, for every IPv4 address this computer holds right now. 169.254.x.x is left
    /// out: it is the address an adapter gives ITSELF when it has no network, so nobody can reach it there. On
    /// Windows an address that is being given up or not yet confirmed is left out too, as Node leaves it out.</summary>
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
                if (OperatingSystem.IsWindows() && one.DuplicateAddressDetectionState != DuplicateAddressDetectionState.Preferred)
                    continue;
                found[text] = adapter.Name;
            }
        }
        return found;
    }

    internal static async Task<bool> Answers(string host, int port)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(ProbeMs);
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static async Task Report(int port, string? givenSetting, string? givenUrl)
    {
        var answering = new List<(string url, string adapter)>();
        foreach (var (address, adapter) in Own())
            if (await Answers(address, port)) answering.Add(($"http://{address}:{port}", adapter));

        if (answering.Count == 0)
            WriteRed("[char] does not answer at any address of this computer.");
        else
        {
            var width = answering.Max(a => a.url.Length);
            Console.WriteLine("[char] answers at (tested from this computer):");
            foreach (var (url, adapter) in answering) Console.WriteLine($"  {url.PadRight(width)}   {adapter}");
        }

        if (givenSetting is null || string.IsNullOrWhiteSpace(givenUrl)) return;
        var ok = Uri.TryCreate(givenUrl.Contains("://") ? givenUrl : "http://" + givenUrl, UriKind.Absolute, out var uri)
                 && await Answers(uri.Host, uri.Port);
        if (ok)
            Console.WriteLine($"[char] {givenSetting} {givenUrl} answers — that is the address players are given.");
        else
            WriteRed($"[char] {givenSetting} {givenUrl} does not answer — anyone given that "
                     + "address cannot reach this server.");
    }

    static Timer? watching;

    /// <summary>Say where this server answers, now and every time this computer's addresses change. The given
    /// address is the one players' copies of the game are built to dial, when there is one.</summary>
    public static async Task ReportAndWatch(int port, string? givenSetting, string? givenUrl)
    {
        await Report(port, givenSetting, givenUrl);

        var seen = string.Join(",", Own().Keys);
        var busy = 0;
        watching = new Timer(async _ =>
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                var now = string.Join(",", Own().Keys);
                if (now == seen) return;
                seen = now;
                Console.WriteLine("[char] this computer's addresses changed:");
                await Report(port, givenSetting, givenUrl);
            }
            finally { Interlocked.Exchange(ref busy, 0); }
        }, null, Recheck, Recheck);
    }
}
