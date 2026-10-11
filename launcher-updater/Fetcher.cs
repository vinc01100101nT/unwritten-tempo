using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace UnwrittenLauncherUpdater;

/// <summary>A failure the player is told about in the window, in these words.</summary>
public sealed class FetchFailed(string message) : Exception(message);

/// <summary>
/// Everything this program DOES, with no window in it: make sure the launcher on this computer is the one the update
/// server holds, and say where it is so it can be started.
///
/// <para><b>Two programs, on purpose.</b> This one is what a player downloads and opens. It never writes into its own
/// folder — so it works from a folder a program may not write to, such as Program Files — and it never replaces
/// itself. The launcher it fetches lives in the player's own folder, where it can always be replaced, and that one
/// updates and starts the game. <b>Keep this program small and plain</b>: a copy already on a player's computer is
/// only ever replaced by the launcher, and only where the launcher is allowed to write.</para>
///
/// <para>The update server lists the launcher in <c>windows/patcher.json</c> — its size, its fingerprint, and the
/// update address players should be using — and stores its contents under that fingerprint in
/// <c>windows/files/</c>, beside the game's files (<c>tools/publish-update.py</c>).</para>
/// </summary>
public sealed class Fetcher
{
    /// <summary>The name this program and the launcher both go by, each in its own folder. The download keeps the
    /// name the first launcher shipped under, so unzipping it over an older folder replaces that launcher.</summary>
    public const string ProgramName = "unwritten-launcher.exe";

    static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(20);

    readonly HttpClient http;
    readonly string own;

    /// <summary>The folder this program treats as the player's: where the update address is read from, and where an
    /// older copy of the game may sit. Its own folder — except when THE FIRST LAUNCHER started it out of that
    /// launcher's game folder. That launcher kept the game beside itself and cannot update itself, so the update
    /// server hands it this program as "the game"; the player's folder is then the one above.</summary>
    public string Home { get; }

    public string LauncherFolder { get; }
    public string Launcher => Path.Combine(LauncherFolder, ProgramName);

    /// <summary>The launcher's size, date and fingerprint when it was last checked, so an untouched 100 MB program is
    /// not read end to end every time this is opened.</summary>
    string CheckedFile => Path.Combine(LauncherFolder, "checked.txt");

    public Fetcher(string ownFolder, string installRoot, HttpClient? http = null)
    {
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        own = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownFolder));
        installRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        LauncherFolder = Path.Combine(installRoot, "patcher");

        // The first launcher's folder holds its own program, its address file, the record it wrote after an update,
        // and the game folder this program was started from. All four, or this is not that folder. Never the folder
        // the game lives in today, which holds a copy of this program too.
        var above = Path.GetDirectoryName(own);
        var startedByFirstLauncher = above != null
            && !string.Equals(above, installRoot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(own), "game", StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(above, "installed.json"))
            && File.Exists(Path.Combine(above, "launcher.txt"))
            && File.Exists(Path.Combine(above, ProgramName));
        Home = startedByFirstLauncher ? above! : own;
    }

    /// <summary>Bring the launcher on this computer to the one the update server holds, and return it. A launcher
    /// already here is returned as it is whenever a newer one cannot be had — it can say for itself what is wrong.</summary>
    public async Task<string> FetchAsync(Action<string> say, CancellationToken cancel)
    {
        var address = (Setting("PATCH_URL") ?? "").TrimEnd('/');
        if (address.Length == 0)
            throw new FetchFailed("This launcher has no update address. Download it again from the website.");

        say("Checking for patcher update");
        var have = File.Exists(Launcher);
        Listed listed;
        try
        {
            using var answer = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            answer.CancelAfter(AnswerWithin);
            listed = Listed.Read(await http.GetStringAsync($"{address}/windows/patcher.json", answer.Token));
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException
                                    or KeyNotFoundException or InvalidOperationException)
        {
            cancel.ThrowIfCancellationRequested();
            if (!have) throw new FetchFailed("Could not reach the update server. Check your connection and open this again.");
            return Launcher;
        }

        if (!(have && await IsCurrentAsync(listed, cancel)))
        {
            var doing = have ? "Updating the patcher" : "Downloading the patcher";
            say(doing);
            Directory.CreateDirectory(LauncherFolder);
            var part = Launcher + ".part";
            try
            {
                await DownloadAsync($"{address}/windows/files/{listed.Sha256}", part, listed.Size,
                                    percent => say($"{doing} {percent}%"), cancel);
                if (await FingerprintAsync(part, cancel) != listed.Sha256 || new FileInfo(part).Length != listed.Size)
                {
                    File.Delete(part);
                    throw new FetchFailed("The patcher arrived damaged. Open this again.");
                }
            }
            catch (Exception e) when (e is HttpRequestException or IOException or FetchFailed)
            {
                if (have) return Launcher;
                throw e as FetchFailed ?? new FetchFailed("The download stopped. Check your connection and open this again.");
            }

            try { File.Move(part, Launcher, overwrite: true); }
            catch (Exception e) when (have && e is IOException or UnauthorizedAccessException)
            {
                // Windows will not replace a program that is running, so the launcher is open already. It is
                // replaced the next time this is opened with it closed.
                File.Delete(part);
                throw new FetchFailed("The patcher is already open. Close it to let it update, then open this again.");
            }
            Remember(listed);
        }

        // The launcher reads the update address from a file beside itself, as it always has. The server's own word
        // for the address wins, so players are moved to a new one without downloading anything again.
        File.WriteAllText(Path.Combine(LauncherFolder, "launcher.txt"),
                          $"# Where this launcher downloads updates from.\nPATCH_URL={listed.Address ?? address}\n");
        return Launcher;
    }

    /// <summary>Start the launcher and hand over, telling it which folder the player keeps this program in.</summary>
    public void Start(string launcher)
    {
        // Its own folder as the working folder, never this program's: a folder a running program sits in cannot be
        // removed, and the launcher may need to remove the one this was started from.
        var start = new ProcessStartInfo(launcher) { UseShellExecute = false, WorkingDirectory = LauncherFolder };
        start.ArgumentList.Add("--beside");
        start.ArgumentList.Add(Home);
        Process.Start(start);
    }

    /// <summary>Where the update server is: the environment first, so a test can point it elsewhere without touching
    /// a file; then <c>launcher.txt</c> in the player's folder, the same shape the launcher itself reads; then the
    /// launcher's own copy, for the copy of this program that sits among the game's files.</summary>
    string? Setting(string name)
    {
        var env = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

        foreach (var folder in new[] { Home, LauncherFolder })
        {
            var file = Path.Combine(folder, "launcher.txt");
            if (!File.Exists(file)) continue;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var split = line.IndexOf('=');
                if (split > 0 && line[..split].Trim() == name && line[(split + 1)..].Trim().Length > 0)
                    return line[(split + 1)..].Trim();
            }
        }
        return null;
    }

    sealed record Listed(long Size, string Sha256, string? Address)
    {
        public static Listed Read(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var sha = doc.RootElement.GetProperty("sha256").GetString() ?? "";
            // The fingerprint becomes part of an address and of nothing else; anything but a fingerprint is refused.
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) throw new JsonException("not a fingerprint");
            var address = doc.RootElement.TryGetProperty("address", out var a) ? a.GetString()?.Trim().TrimEnd('/') : null;
            if (address != null && !(address.StartsWith("https://") || address.StartsWith("http://"))) address = null;
            return new Listed(doc.RootElement.GetProperty("size").GetInt64(), sha.ToLowerInvariant(), address);
        }
    }

    async Task<bool> IsCurrentAsync(Listed listed, CancellationToken cancel)
    {
        var file = new FileInfo(Launcher);
        if (file.Length != listed.Size) return false;
        try
        {
            var seen = File.ReadAllLines(CheckedFile);
            if (seen.Length == 3 && seen[0] == file.Length.ToString() && seen[1] == file.LastWriteTimeUtc.Ticks.ToString())
                return seen[2] == listed.Sha256;
        }
        catch (IOException) { }

        if (await FingerprintAsync(Launcher, cancel) != listed.Sha256) return false;
        Remember(listed);
        return true;
    }

    void Remember(Listed listed)
    {
        var file = new FileInfo(Launcher);
        try { File.WriteAllLines(CheckedFile, [file.Length.ToString(), file.LastWriteTimeUtc.Ticks.ToString(), listed.Sha256]); }
        catch (IOException) { }
    }

    static async Task<string> FingerprintAsync(string path, CancellationToken cancel)
    {
        await using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, cancel));
    }

    async Task DownloadAsync(string from, string to, long size, Action<int> percent, CancellationToken cancel)
    {
        using var response = await http.GetAsync(from, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        await using var file = File.Create(to);
        var buffer = new byte[1 << 16];
        long got = 0;
        int n, said = -1;
        while ((n = await source.ReadAsync(buffer, cancel)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), cancel);
            got += n;
            var now = size <= 0 ? 0 : (int)Math.Min(100, got * 100 / size);
            if (now != said) percent(said = now);
        }
    }
}
