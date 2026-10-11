using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnwrittenMac;

namespace UnwrittenLauncherUpdater;

/// <summary>A failure the player is told about in the window, in these words.</summary>
public sealed class FetchFailed(string message) : Exception(message);

/// <summary>The app the player opens to play, which the launcher replaces with the newest copy of this program when
/// it is older: <paramref name="Path"/> is where it really is (never the read-only copy a Mac may run a downloaded
/// app from), <paramref name="Id"/> which of our apps it is, <paramref name="Version"/> its build number.</summary>
public sealed record PlayersApp(string Path, string Id, string? Version);

/// <summary>
/// Everything the Mac program a player downloads DOES, with no window in it: make sure the launcher in the player's
/// own folder is the one the update server holds, and say where it is so it can be started. The Mac twin of
/// <c>launcher-updater/Fetcher.cs</c>, kept apart from it so a Mac change can never alter the Windows program, whose
/// copies on players' computers are replaced every time its fingerprint changes.
///
/// <para><b>A Mac launcher is a folder of files</b> (an app), so the update server lists it file by file in
/// <c>mac/patcher-arm64.json</c> and <c>mac/patcher-x64.json</c>, one per kind of Mac chip, with the update address
/// players should be using; the contents sit in <c>mac/files/</c> under their fingerprints, beside the game's files
/// (<c>tools/publish-update.py</c>). Files copied byte for byte, with their permission to run given back, keep the
/// app's signature whole.</para>
///
/// <para>Like the Windows program, this never writes inside or beside itself — a Mac may run a downloaded app from a
/// read-only copy — and never replaces itself; the launcher does that.</para>
/// </summary>
public sealed class MacFetcher
{
    static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(20);

    readonly HttpClient http;
    readonly string? ownApp;

    public string InstallRoot { get; }
    public string LauncherFolder => Path.Combine(InstallRoot, "patcher");
    public string App => Path.Combine(LauncherFolder, "unwritten.app");
    public string Program => Path.Combine(App, MacApps.Program);

    /// <summary>Each file of the launcher as it was when last checked, so an untouched 100 MB program is not read end
    /// to end every time this is opened. The launcher reads it too, to tell whether it is behind.</summary>
    string CheckedFile => Path.Combine(LauncherFolder, "checked.json");
    string AddressFile => Path.Combine(LauncherFolder, "launcher.txt");
    string LockFile => Path.Combine(LauncherFolder, ".lock");
    static string Chip => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    public MacFetcher(string installRoot, HttpClient? http = null)
    {
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        InstallRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        ownApp = Environment.ProcessPath is { } self ? MacApps.AppOf(self) : null;
    }

    /// <summary>Which app the player opens: this program's own app. Null when it is the copy listed among the game's
    /// files — an older launcher started that one, and the launcher finds those itself (<c>MacApps.OldLaunchers</c>);
    /// the program that started this one has always closed before it could be asked, measured 2026-10-11.</summary>
    public PlayersApp? FindPlayersApp()
    {
        if (ownApp == null || MacApps.Within(ownApp, InstallRoot)) return null;
        var id = MacApps.Plist(ownApp, "CFBundleIdentifier");
        if (id != MacApps.FetcherId) return null;
        var version = MacApps.Plist(ownApp, "CFBundleVersion");
        return WhereItReallyIs(ownApp) is { } path ? new PlayersApp(path, id, version) : null;
    }

    /// <summary>Bring the launcher in the player's folder to the one the update server holds, and return it with the
    /// address that answered. A launcher already here is returned as it is whenever a newer one cannot be had — it
    /// can say for itself what is wrong.</summary>
    public async Task<(string program, string? address)> FetchAsync(Action<string> say, CancellationToken cancel)
    {
        var addresses = Addresses();
        if (addresses.Count == 0)
            throw new FetchFailed("This launcher has no update address. Download it again from the website.");

        say("Checking for patcher update");
        Directory.CreateDirectory(LauncherFolder);
        using var held = Lock()
            ?? throw new FetchFailed("The patcher is already open. Close it to let it update, then open this again.");
        MacApps.ClearLeftovers(App);

        // THE ADDRESS THE SERVER LAST GAVE FIRST, THEN THE ONE THIS APP WAS BUILT WITH: an address that has stopped
        // answering must never strand a player, and a fresh download carries the newest one.
        Listed? listed = null;
        string? used = null;
        foreach (var address in addresses)
        {
            try
            {
                using var answer = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                answer.CancelAfter(AnswerWithin);
                listed = Listed.Read(await http.GetStringAsync($"{address}/mac/patcher-{Chip}.json", answer.Token));
                used = address;
                break;
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException
                                        or KeyNotFoundException or InvalidOperationException or InvalidDataException)
            {
                cancel.ThrowIfCancellationRequested();
            }
        }

        if (listed == null || used == null)
        {
            if (IsWhole()) return (Program, null);
            throw new FetchFailed("Could not reach the update server. Check your connection and open this again.");
        }

        var have = IsWhole();
        try
        {
            if (!(have && await IsCurrentAsync(listed, cancel))) await InstallAsync(listed, used, have, say, cancel);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException
                                    or InvalidDataException or FetchFailed)
        {
            MacApps.ClearLeftovers(App);
            if (IsWhole()) return (Program, used);
            throw e as FetchFailed ?? new FetchFailed("The download stopped. Check your connection and open this again.");
        }

        // Written only once an answer came from it. The server's own word for the address wins, so players are moved
        // to a new one without downloading anything again.
        var current = listed.Address ?? used;
        File.WriteAllText(AddressFile, $"# Where this launcher downloads updates from.\nPATCH_URL={current}\n");
        return (Program, current);
    }

    /// <summary>Start the launcher and hand over: which app the player opens (empty when none can be trusted), and the
    /// address that answered, which the launcher reads before any file.</summary>
    public void Start(string program, string? address, PlayersApp? players)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, WorkingDirectory = LauncherFolder };
        start.ArgumentList.Add("--beside");
        start.ArgumentList.Add(players?.Path ?? "");
        if (players != null)
        {
            start.ArgumentList.Add("--beside-id");
            start.ArgumentList.Add(players.Id);
            start.ArgumentList.Add("--beside-version");
            start.ArgumentList.Add(players.Version ?? "");
        }
        if (address != null) start.Environment["PATCH_URL"] = address;
        Process.Start(start);
    }

    /// <summary>The launcher holds this for as long as it is open, and this program while it replaces the launcher's
    /// files, so neither works on files the other is using. Null when it is held.</summary>
    FileStream? Lock()
    {
        try { return new FileStream(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    /// <summary>Where the update server is: the environment first, so a test can point it elsewhere; then the address
    /// the server last gave, kept in the player's folder; then the one sealed inside this app when it was built.</summary>
    List<string> Addresses()
    {
        var env = Environment.GetEnvironmentVariable("PATCH_URL");
        if (!string.IsNullOrWhiteSpace(env)) return [env.Trim().TrimEnd('/')];
        var found = new List<string>();
        foreach (var file in new[] { AddressFile, ownApp == null ? null : Path.Combine(ownApp, "Contents", "Resources", "launcher.txt") })
            if (file != null && Setting(file, "PATCH_URL") is { } address && !found.Contains(address)) found.Add(address);
        return found;
    }

    static string? Setting(string file, string name)
    {
        try
        {
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var split = line.IndexOf('=');
                var value = split > 0 && line[..split].Trim() == name ? line[(split + 1)..].Trim().TrimEnd('/') : "";
                if (value.StartsWith("https://") || value.StartsWith("http://")) return value;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>The launcher's files as the update server lists them.</summary>
    sealed record Listed(List<Listed.Entry> Files, string? Address)
    {
        public sealed record Entry(string Path, long Size, string Sha256, bool Exec);

        public static Listed Read(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var files = new List<Entry>();
            foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
            {
                var path = f.GetProperty("path").GetString() ?? "";
                var sha = (f.GetProperty("sha256").GetString() ?? "").ToLowerInvariant();
                var size = f.GetProperty("size").GetInt64();
                // A path becomes a place on the player's disk and a fingerprint part of an address; anything that
                // could reach further than the launcher's own folder is refused.
                if (!MacApps.IsSafePath(path) || sha.Length != 64 || !sha.All(Uri.IsHexDigit) || size < 0)
                    throw new InvalidDataException("not a launcher file: " + path);
                files.Add(new Entry(path, size, sha, f.TryGetProperty("exec", out var x) && x.GetBoolean()));
            }
            if (MacApps.HasRepeats(files.Select(f => f.Path)) || files.All(f => f.Path != MacApps.Program))
                throw new InvalidDataException("not a launcher");
            var address = doc.RootElement.TryGetProperty("address", out var a) ? a.GetString()?.Trim().TrimEnd('/') : null;
            if (address != null && !(address.StartsWith("https://") || address.StartsWith("http://"))) address = null;
            return new Listed(files, address);
        }
    }

    sealed record Checked(long Size, DateTime Written, string Sha256);

    Dictionary<string, Checked> ReadChecked()
    {
        var known = new Dictionary<string, Checked>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(CheckedFile));
            foreach (var p in doc.RootElement.EnumerateObject())
                known[p.Name] = new Checked(p.Value.GetProperty("size").GetInt64(), p.Value.GetProperty("written").GetDateTime(),
                                            p.Value.GetProperty("sha256").GetString() ?? "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                                    or KeyNotFoundException or InvalidOperationException or FormatException) { }
        return known;
    }

    /// <summary>Written in the shape the launcher's own record of the game uses, so the launcher reads it the same way.</summary>
    void WriteChecked(Listed listed)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            foreach (var f in listed.Files)
            {
                var info = new FileInfo(MacApps.Inside(App, f.Path));
                json.WriteStartObject(f.Path);
                json.WriteNumber("size", info.Length);
                json.WriteString("written", info.LastWriteTimeUtc);
                json.WriteString("sha256", f.Sha256);
                json.WriteEndObject();
            }
            json.WriteEndObject();
        }
        File.WriteAllBytes(CheckedFile, stream.ToArray());
    }

    /// <summary>True when the launcher here can be started: its program is there, and every file last checked is
    /// still there at the size it had.</summary>
    bool IsWhole() =>
        File.Exists(Program)
        && ReadChecked().All(k => MacApps.IsSafePath(k.Key) && new FileInfo(MacApps.Inside(App, k.Key)) is { Exists: true } f
                                  && f.Length == k.Value.Size);

    async Task<bool> IsFileCurrentAsync(Listed.Entry f, Dictionary<string, Checked> known, CancellationToken cancel)
    {
        var info = new FileInfo(MacApps.Inside(App, f.Path));
        if (!info.Exists || info.Length != f.Size) return false;
        if (known.TryGetValue(f.Path, out var k) && k.Size == info.Length && k.Written == info.LastWriteTimeUtc)
            return k.Sha256 == f.Sha256;
        return await FingerprintAsync(info.FullName, cancel) == f.Sha256;
    }

    /// <summary>Exactly the listed files, each the listed one. A file the list does not name would break the app's
    /// signature, so it makes the launcher not current too.</summary>
    async Task<bool> IsCurrentAsync(Listed listed, CancellationToken cancel)
    {
        var known = ReadChecked();
        var onDisk = Directory.EnumerateFiles(App, "*", SearchOption.AllDirectories)
                              .Select(p => Path.GetRelativePath(App, p)).ToHashSet();
        if (!onDisk.SetEquals(listed.Files.Select(f => f.Path))) return false;
        foreach (var f in listed.Files)
            if (!await IsFileCurrentAsync(f, known, cancel)) return false;
        WriteChecked(listed);
        return true;
    }

    /// <summary>Build the new launcher beside the old one — each unchanged file copied across, each other one
    /// downloaded and checked — then swap the two in one step, so the player is never left without one.</summary>
    async Task InstallAsync(Listed listed, string address, bool have, Action<string> say, CancellationToken cancel)
    {
        var known = ReadChecked();
        var needed = new HashSet<Listed.Entry>();
        foreach (var f in listed.Files)
            if (!(have && await IsFileCurrentAsync(f, known, cancel))) needed.Add(f);

        var doing = have ? "Updating the patcher" : "Downloading the patcher";
        say(doing);
        long total = needed.Sum(f => f.Size), done = 0;
        int said = -1;
        var arriving = MacApps.Arriving(App);
        Directory.CreateDirectory(arriving);
        foreach (var f in listed.Files)
        {
            var to = MacApps.Inside(arriving, f.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (needed.Contains(f))
            {
                await DownloadAsync($"{address}/mac/files/{f.Sha256}", to, got =>
                {
                    var now = total <= 0 ? 100 : (int)Math.Min(100, (done + got) * 100 / total);
                    if (now != said) say($"{doing} {said = now}%");
                }, cancel);
                done += f.Size;
                if (new FileInfo(to).Length != f.Size || await FingerprintAsync(to, cancel) != f.Sha256)
                    throw new FetchFailed("The patcher arrived damaged. Open this again.");
            }
            else File.Copy(MacApps.Inside(App, f.Path), to);
            if (f.Exec) MacApps.MayRun(to);
        }

        MacApps.Swap(arriving, App);
        if (Directory.Exists(arriving)) Directory.Delete(arriving, recursive: true);
        WriteChecked(listed);
    }

    static async Task<string> FingerprintAsync(string path, CancellationToken cancel)
    {
        await using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, cancel));
    }

    async Task DownloadAsync(string from, string to, Action<long> got, CancellationToken cancel)
    {
        using var response = await http.GetAsync(from, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        await using var file = File.Create(to);
        var buffer = new byte[1 << 16];
        long sum = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, cancel)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), cancel);
            got(sum += n);
        }
    }

    /// <summary>A Mac runs an app downloaded from the internet from a hidden read-only copy until the player moves it
    /// ("App Translocation"), so the path a running app sees may not be the one the player opens. The real one is
    /// asked of the system; null when the system cannot say.</summary>
    static unsafe string? WhereItReallyIs(string app)
    {
        var bytes = Encoding.UTF8.GetBytes(app);
        nint url;
        fixed (byte* b = bytes) url = CFURLCreateFromFileSystemRepresentation(0, b, bytes.Length, 1);
        if (url == 0) return null;
        try
        {
            byte translocated = 0;
            if (SecTranslocateIsTranslocatedURL(url, &translocated, 0) == 0 || translocated == 0) return app;
            var original = SecTranslocateCreateOriginalPathForURL(url, 0);
            if (original == 0) return null;
            try
            {
                var buffer = stackalloc byte[4096];
                return CFURLGetFileSystemRepresentation(original, 1, buffer, 4096) != 0
                    ? Marshal.PtrToStringUTF8((nint)buffer) : null;
            }
            finally { CFRelease(original); }
        }
        finally { CFRelease(url); }
    }

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    static extern unsafe nint CFURLCreateFromFileSystemRepresentation(nint allocator, byte* path, nint length, byte isDirectory);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    static extern unsafe byte CFURLGetFileSystemRepresentation(nint url, byte resolveAgainstBase, byte* buffer, nint length);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern void CFRelease(nint thing);
    // Not in Apple's published headers, but exported by the Security framework since macOS 10.12 and the only way to
    // ask where a translocated app really is.
    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    static extern unsafe byte SecTranslocateIsTranslocatedURL(nint url, byte* isTranslocated, nint error);
    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    static extern nint SecTranslocateCreateOriginalPathForURL(nint url, nint error);
}
