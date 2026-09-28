using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace UnwrittenLauncher;

/// <summary>
/// Everything the launcher DOES, with no window in it: find out what the update server holds, compare it with the
/// game files on this machine, fetch only what differs, and start the game.
///
/// <para><b>The update server is a plain folder of files</b>, written by <c>tools/publish-update.py</c> and served
/// as-is:</para>
/// <code>
///   version.txt                  the version every copy must match (the character server reads the same file)
///   news.json                    the news box
///   &lt;platform&gt;/manifest.json     every game file: its path, size, fingerprint, and whether it is a program
///   &lt;platform&gt;/files/&lt;sha256&gt;    each file's contents, stored under its fingerprint
/// </code>
/// <para>Contents are stored by fingerprint rather than by path so a publish never overwrites a file somebody is
/// halfway through downloading, and a file that did not change between versions is never uploaded twice.</para>
/// </summary>
public sealed class Patcher
{
    public sealed record NewsItem(string date, string title, string text);
    public sealed record ManifestFile(string path, long size, string sha256, bool exec);
    public sealed record Manifest(string start, List<ManifestFile> files);

    /// <summary>What this machine had after its last finished update, so an unchanged file is recognised by its size
    /// and date instead of being read end to end on every launch. Kept beside the game folder, never inside it,
    /// because everything inside it that the manifest does not list is deleted.</summary>
    sealed record Known(long size, DateTime written, string sha256);

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public string PatchUrl { get; }
    public string Platform { get; }
    public string InstallRoot { get; }
    public string GameDir => Path.Combine(InstallRoot, "game");
    string KnownFile => Path.Combine(InstallRoot, "installed.json");

    Manifest? manifest;

    public Patcher()
    {
        PatchUrl = Setting("PATCH_URL") ?? "http://localhost:8090";
        PatchUrl = PatchUrl.TrimEnd('/');
        Platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";

        // WINDOWS keeps the game beside the launcher, where the player unzipped it — the way Ragnarok Online's
        // folder works. A MAC app is moved into Applications, which is no place to write, so the game lives in the
        // player's own Application Support folder instead.
        InstallRoot = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "Library", "Application Support", "unwritten")
            : AppContext.BaseDirectory;
    }

    /// <summary>Where the update server is. The environment first, so it can be pointed somewhere else for a test
    /// without touching a file; then <c>launcher.txt</c> beside the program, in the same <c>NAME=value</c> shape
    /// the game's own settings file uses.</summary>
    static string? Setting(string name)
    {
        var env = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

        var file = Path.Combine(AppContext.BaseDirectory, "launcher.txt");
        if (!File.Exists(file)) return null;
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var split = line.IndexOf('=');
            if (split > 0 && line[..split].Trim() == name) return line[(split + 1)..].Trim();
        }
        return null;
    }

    public async Task<string> ReadVersionAsync() =>
        (await Http.GetStringAsync($"{PatchUrl}/version.txt")).Trim();

    public async Task<List<NewsItem>> ReadNewsAsync() =>
        JsonSerializer.Deserialize<List<NewsItem>>(await Http.GetStringAsync($"{PatchUrl}/news.json"), Json) ?? [];

    /// <summary>Bring the game folder to exactly what the update server lists. <paramref name="progress"/> is told
    /// what is happening in words, and how far through the download it is from 0 to 1.</summary>
    public async Task UpdateAsync(Action<string, double> progress, CancellationToken cancel)
    {
        if (Platform == "linux") throw new InvalidOperationException("This launcher runs on Windows and Mac only.");

        progress("Checking for updates…", 0);
        manifest = JsonSerializer.Deserialize<Manifest>(
                       await Http.GetStringAsync($"{PatchUrl}/{Platform}/manifest.json", cancel), Json)
                   ?? throw new InvalidDataException("The update list could not be read.");

        progress("Checking your files…", 0);
        Directory.CreateDirectory(GameDir);
        var known = ReadKnown();
        var needed = new List<ManifestFile>();
        foreach (var f in manifest.files)
        {
            cancel.ThrowIfCancellationRequested();
            if (!await IsCurrentAsync(f, known, cancel)) needed.Add(f);
        }

        long total = needed.Sum(f => f.size), done = 0;
        for (int i = 0; i < needed.Count; i++)
        {
            var f = needed[i];
            progress($"Downloading {i + 1} of {needed.Count} — {Path.GetFileName(f.path)}", total == 0 ? 1 : (double)done / total);
            await DownloadAsync(f, cancel, got =>
                progress($"Downloading {i + 1} of {needed.Count} — {Path.GetFileName(f.path)}", total == 0 ? 1 : (double)(done + got) / total));
            done += f.size;
        }

        RemoveUnlisted();
        WriteKnown();
        progress("Up to date. Press Start.", 1);
    }

    async Task<bool> IsCurrentAsync(ManifestFile f, Dictionary<string, Known> known, CancellationToken cancel)
    {
        var local = LocalPath(f.path);
        var info = new FileInfo(local);
        if (!info.Exists || info.Length != f.size) return false;
        if (known.TryGetValue(f.path, out var k) && k.size == info.Length && k.written == info.LastWriteTimeUtc)
            return k.sha256 == f.sha256;
        return await HashAsync(local, cancel) == f.sha256;
    }

    async Task DownloadAsync(ManifestFile f, CancellationToken cancel, Action<long> got)
    {
        var local = LocalPath(f.path);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        var part = local + ".part";

        using (var response = await Http.GetAsync($"{PatchUrl}/{Platform}/files/{f.sha256}",
                                                  HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            await using var from = await response.Content.ReadAsStreamAsync(cancel);
            await using var to = File.Create(part);
            var buffer = new byte[1 << 16];
            long sum = 0;
            int n;
            while ((n = await from.ReadAsync(buffer, cancel)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, n), cancel);
                sum += n;
                got(sum);
            }
        }

        // CHECKED BEFORE IT REPLACES ANYTHING, so a cut connection or a wrong file leaves the old copy in place
        // rather than a broken one the game would try to load.
        if (await HashAsync(part, cancel) != f.sha256)
        {
            File.Delete(part);
            throw new InvalidDataException($"{f.path} arrived damaged. Press Retry.");
        }
        File.Move(part, local, overwrite: true);

        // A program downloaded over the internet arrives without permission to run on a Mac; the manifest says
        // which files are programs so they can be given it back.
        if (f.exec && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(local, File.GetUnixFileMode(local)
                                        | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    /// <summary>A file the new version no longer has must go, or the game may load it as though it still belonged.
    /// Only inside the game folder, which holds nothing the launcher did not put there.</summary>
    void RemoveUnlisted()
    {
        var listed = manifest!.files.Select(f => Path.GetFullPath(LocalPath(f.path))).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(GameDir, "*", SearchOption.AllDirectories))
            if (!listed.Contains(Path.GetFullPath(file))) File.Delete(file);

        foreach (var dir in Directory.EnumerateDirectories(GameDir, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length))
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    /// <summary>Start the game and hand over. Only after a finished update — the game is never started half-patched.</summary>
    public void StartGame()
    {
        if (manifest == null) throw new InvalidOperationException("Update first.");
        var program = LocalPath(manifest.start);
        Process.Start(new ProcessStartInfo(program) { WorkingDirectory = Path.GetDirectoryName(program)!, UseShellExecute = false });
    }

    string LocalPath(string manifestPath)
    {
        // A path from the update server must stay inside the game folder, whatever it says.
        var full = Path.GetFullPath(Path.Combine(GameDir, manifestPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(GameDir) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"The update list names a file outside the game folder: {manifestPath}");
        return full;
    }

    static async Task<string> HashAsync(string path, CancellationToken cancel)
    {
        await using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, cancel));
    }

    Dictionary<string, Known> ReadKnown()
    {
        try
        {
            return File.Exists(KnownFile)
                ? JsonSerializer.Deserialize<Dictionary<string, Known>>(File.ReadAllText(KnownFile), Json) ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }

    void WriteKnown()
    {
        var known = new Dictionary<string, Known>();
        foreach (var f in manifest!.files)
        {
            var info = new FileInfo(LocalPath(f.path));
            known[f.path] = new Known(info.Length, info.LastWriteTimeUtc, f.sha256);
        }
        File.WriteAllText(KnownFile, JsonSerializer.Serialize(known));
    }
}
