using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using UnwrittenMac;

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
///   windows/patcher.json         this launcher itself, for the small program a Windows player downloads
///                                (launcher-updater/), which fetches it, keeps it up to date and starts it
///   mac/patcher-arm64.json       this launcher's files, one list per kind of Mac chip, for the small program a
///   mac/patcher-x64.json         Mac player downloads (launcher-updater-mac/), which does the same on a Mac
/// </code>
/// <para>Contents are stored by fingerprint rather than by path so a publish never overwrites a file somebody is
/// halfway through downloading, and a file that did not change between versions is never uploaded twice.</para>
/// </summary>
public sealed class Patcher
{
    public sealed record NewsItem(string date, string title, string text);
    public sealed record ManifestFile(string path, long size, string sha256, bool exec);
    /// <summary><paramref name="version"/> is the version these files are — null in a list published before it was
    /// written there.
    /// <para><paramref name="play"/> is the game's own program. A list that names one is handing the FIRST launcher
    /// over: that launcher runs <paramref name="start"/> and nothing else, cannot update itself, and kept the game
    /// beside itself — so <paramref name="start"/> is given to it as the small program players download today
    /// (<c>launcher-updater/</c>), which fetches this launcher. This launcher starts <paramref name="play"/>, and
    /// <paramref name="start"/> only where the list names no <paramref name="play"/>.</para>
    /// <para>A MAC list handing launchers over names the small program as BOTH <paramref name="start"/> and
    /// <paramref name="play"/>, and the game as <paramref name="game"/>, because two kinds of Mac launcher are in
    /// players' hands: the first runs <paramref name="start"/> and the one handed out on 2026-10-11 runs
    /// <paramref name="play"/>. This launcher runs <paramref name="game"/> before either. <paramref name="fetcher"/>
    /// is the build number of the small program in that list, so a newer one already in the player's hands is never
    /// replaced by it.</para></summary>
    public sealed record Manifest(string start, List<ManifestFile> files, string? version = null, string? play = null,
                                  string? game = null, long? fetcher = null);

    /// <summary>What this machine had after its last finished update, so an unchanged file is recognised by its size
    /// and date instead of being read end to end on every launch. Kept in the folder that holds the game folder, never
    /// inside the game folder, because everything inside it that the manifest does not list is deleted.</summary>
    sealed record Known(long size, DateTime written, string sha256);

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public string PatchUrl { get; }
    public string Platform { get; }
    public string InstallRoot { get; }
    public string GameDir => GameDirIn(InstallRoot);
    string KnownFile => KnownFileIn(InstallRoot);

    /// <summary>The name the small program players download goes by — in the folder they keep it in, and among the
    /// game's files, where the update server lists a copy of it.</summary>
    const string FetcherName = "unwritten-launcher.exe";

    /// <summary>The folder a Mac list keeps its copy of the small program in, among the game's files. Never the game:
    /// starting it from here would only start this launcher again.</summary>
    const string MacFetcherFolder = "unwritten-patcher.app";

    /// <summary>ON WINDOWS, the folder the player keeps the small downloaded program in, which that program names when
    /// it starts this launcher (<c>--beside</c>). The first launcher kept the game there too, so it is where an older
    /// copy of the game is looked for. Null when nobody named one, on a Mac, and when it is this launcher's own
    /// folder — nothing of the player's is in there.</summary>
    readonly string? playersFolder;

    /// <summary>Where the small downloaded program keeps this launcher on Windows.</summary>
    string LauncherFolder => Path.Combine(InstallRoot, "patcher");
    bool KeptByFetcher => OperatingSystem.IsWindows() && SameFolder(AppContext.BaseDirectory, LauncherFolder);

    /// <summary>ON A MAC, the small program the player opened, which names itself when it starts this launcher, with
    /// its build number. Null when none was named, or when it is not one this launcher may replace: anything but our
    /// own apps, anything in the player's unwritten folder, and this launcher itself are never touched.</summary>
    readonly (string path, string id, long? version)? playersApp;

    /// <summary>True when the small program started this launcher; false when the player opened it directly.</summary>
    readonly bool startedByFetcher;

    /// <summary>Held on a Mac for as long as the launcher runs, so the small program never replaces its files while it
    /// is open and two launchers never update the same game at once.</summary>
    FileStream? held;

    string MacApp => Path.Combine(LauncherFolder, "unwritten.app");
    bool KeptByMacFetcher => OperatingSystem.IsMacOS()
        && SameFolder(AppContext.BaseDirectory, Path.Combine(MacApp, "Contents", "MacOS"));

    static string GameDirIn(string root) => Path.Combine(root, "game");
    static string KnownFileIn(string root) => Path.Combine(root, "installed.json");

    Manifest? manifest;

    public Patcher(string? beside = null, string? besideId = null, string? besideVersion = null)
    {
        Platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";

        // THE GAME LIVES IN THE PLAYER'S OWN FOLDER, never beside the launcher: a launcher unzipped into Program
        // Files, or a Mac app moved into Applications, sits where a program without administrator rights may not
        // write. MEASURED 2026-10-11: a Windows launcher in Program Files stopped at "Access to the path … is denied".
        InstallRoot = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "Library", "Application Support", "unwritten")
            : OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "unwritten")
                : AppContext.BaseDirectory;
        PatchUrl = (Setting("PATCH_URL", InstallRoot) ?? "").TrimEnd('/');
        startedByFetcher = beside != null;

        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(beside) && !Within(beside, InstallRoot))
            playersFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(beside));

        if (OperatingSystem.IsMacOS() && !string.IsNullOrWhiteSpace(beside) && besideId is MacApps.LauncherId or MacApps.FetcherId)
        {
            var app = Path.TrimEndingDirectorySeparator(Path.GetFullPath(beside));
            var own = Environment.ProcessPath is { } self ? MacApps.AppOf(self) : null;
            if (app.EndsWith(".app", StringComparison.Ordinal) && !Within(app, InstallRoot) && (own == null || !SameFolder(app, own)))
                playersApp = (app, besideId, long.TryParse(besideVersion, out var v) ? v : null);
        }
    }

    static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                      Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    static bool Within(string folder, string root) =>
        SameFolder(folder, root)
        || Path.GetFullPath(folder).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
                                               StringComparison.OrdinalIgnoreCase);

    /// <summary>ONLY THE SMALL DOWNLOADED PROGRAM REPLACES THIS LAUNCHER — a running program cannot replace itself.
    /// So when this launcher is opened straight from the folder that program keeps it in (a taskbar pin, a shortcut),
    /// it starts the copy of that program held among the game's files and steps aside; that program brings this
    /// launcher up to date and opens it again. True when it did, and this launcher should close without a window.</summary>
    public bool HandedToFetcher(bool startedByFetcher)
    {
        if (!KeptByFetcher || startedByFetcher) return false;
        var fetcher = Path.Combine(GameDir, FetcherName);
        if (!File.Exists(fetcher)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(fetcher) { UseShellExecute = false, WorkingDirectory = LauncherFolder });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>Where the update server is. The environment first, so it can be pointed somewhere else for a test
    /// without touching a file — and on a Mac it is how the small program hands over the address that just answered
    /// it; then <c>launcher.txt</c>, in the same <c>NAME=value</c> shape the game's own settings file uses — beside the
    /// program on Windows; on a Mac the one the small program keeps in the player's folder (the address the server
    /// last gave), then the one in the app's <c>Contents/Resources</c>, the one place inside a Mac app where a file that
    /// is not a program is covered by the app's signature.</summary>
    static string? Setting(string name, string installRoot)
    {
        var env = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

        var file = Path.Combine(AppContext.BaseDirectory, "launcher.txt");
        if (!File.Exists(file) && OperatingSystem.IsMacOS()) file = Path.Combine(installRoot, "patcher", "launcher.txt");
        if (!File.Exists(file))
            file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "launcher.txt"));
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

    /// <summary>The version of the files THIS computer downloads — from its own platform's list, never the shared
    /// version.txt, which moves even when only the other platform was published. Null when the list predates it.</summary>
    public async Task<string?> ReadPlatformVersionAsync()
    {
        var listed = JsonSerializer.Deserialize<Manifest>(
            await Http.GetStringAsync($"{PatchUrl}/{Platform}/manifest.json"), Json);
        return listed?.version;
    }

    public async Task<List<NewsItem>> ReadNewsAsync() =>
        JsonSerializer.Deserialize<List<NewsItem>>(await Http.GetStringAsync($"{PatchUrl}/news.json"), Json) ?? [];

    /// <summary>Bring the game folder to exactly what the update server lists. <paramref name="progress"/> is told
    /// what is happening in words, and how far through the download it is from 0 to 1.</summary>
    public async Task UpdateAsync(Action<string, double> progress, CancellationToken cancel)
    {
        if (Platform == "linux") throw new InvalidOperationException("This launcher runs on Windows and Mac only.");
        if (PatchUrl.Length == 0)
            throw new InvalidOperationException("This launcher was built without an update address (PATCH_URL).");

        if (OperatingSystem.IsMacOS())
        {
            Directory.CreateDirectory(LauncherFolder);
            try { held ??= new FileStream(Path.Combine(LauncherFolder, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("The launcher is already open."); }
        }

        progress("Checking for updates…", 0);
        if (OperatingSystem.IsMacOS() && KeptByMacFetcher && !startedByFetcher) await StopIfBehindAsync(cancel);
        manifest = JsonSerializer.Deserialize<Manifest>(
                       await Http.GetStringAsync($"{PatchUrl}/{Platform}/manifest.json", cancel), Json)
                   ?? throw new InvalidDataException("The update list could not be read.");

        progress("Checking your files…", 0);
        Directory.CreateDirectory(GameDir);
        // IN THIS ORDER. While the first launcher is still what the player opens, it downloads the whole game again
        // the moment its game folder is empty — so the game is moved out from beside it only once the small program
        // has taken its place.
        if (await KeepPlayersProgramCurrentAsync(cancel)) MoveFormerCopy(progress, cancel);
        progress("Checking your files…", 0);
        var known = ReadKnown(KnownFile);
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
        if (OperatingSystem.IsMacOS()) KeepPlayersMacAppCurrent();
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

    /// <summary>True when <paramref name="folder"/> holds a game the first launcher downloaded: its own record of a
    /// finished update, naming at least one file that really is in the game folder beside it. A folder that merely
    /// happens to be called "game" is never touched.</summary>
    bool HoldsFormerCopy(string folder)
    {
        var game = GameDirIn(folder);
        return Directory.Exists(game)
            && ReadKnown(KnownFileIn(folder)).Keys.Any(
                   path => File.Exists(Path.Combine(game, path.Replace('/', Path.DirectorySeparatorChar))));
    }

    static async Task<bool> IsAsync(string path, ManifestFile listed, CancellationToken cancel) =>
        new FileInfo(path).Length == listed.size && await HashAsync(path, cancel) == listed.sha256;

    /// <summary>The program in the player's folder is kept equal to the one the update server lists — the only way a
    /// copy already on a player's computer is ever brought up to date, since it never writes into its own folder. The
    /// FIRST launcher sitting under that name is replaced the same way, which is what moves its players over. So is
    /// the update address beside it. Returns false when the program there is not the listed one and could not be made
    /// so — a folder this launcher may not write to is left exactly as it is.</summary>
    async Task<bool> KeepPlayersProgramCurrentAsync(CancellationToken cancel)
    {
        if (playersFolder == null || manifest!.play == null) return true;
        var theirs = Path.Combine(playersFolder, FetcherName);
        var listed = manifest.files.FirstOrDefault(f => f.path == manifest.start);
        if (listed == null || !File.Exists(theirs) || SameFolder(playersFolder, AppContext.BaseDirectory)) return true;

        string arriving = theirs + ".new", leaving = theirs + ".old";
        try
        {
            if (!await IsAsync(theirs, listed, cancel))
            {
                // The copy the first launcher has just downloaded, the one among this launcher's game files, or a
                // fresh one — whichever is first found to be the listed program, byte for byte.
                var mine = LocalPath(listed.path);
                var beside = Path.Combine(GameDirIn(playersFolder), listed.path.Replace('/', Path.DirectorySeparatorChar));
                var copy = File.Exists(beside) && await IsAsync(beside, listed, cancel) ? beside : mine;
                if (copy == mine && !(File.Exists(mine) && await IsAsync(mine, listed, cancel)))
                    await DownloadAsync(listed, cancel, _ => { });

                // WINDOWS WILL NOT OVERWRITE A PROGRAM THAT IS STILL RUNNING, BUT IT WILL RENAME ONE — and whichever
                // program started this launcher may not have finished closing. So the old one is renamed aside and
                // the new one renamed into place; a half-written program is never left under the name players open.
                File.Copy(copy, arriving, overwrite: true);
                File.Delete(leaving);
                File.Move(theirs, leaving);
                File.Move(arriving, theirs);
            }

            var address = Path.Combine(playersFolder, "launcher.txt");
            var wanted = $"# Where this launcher downloads updates from.\nPATCH_URL={PatchUrl}\n";
            if (File.Exists(address) && File.ReadAllText(address) != wanted) File.WriteAllText(address, wanted);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }

        try { File.Delete(leaving); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return true;
    }

    /// <summary>ONLY THE SMALL PROGRAM REPLACES THIS LAUNCHER ON A MAC, and a Mac launcher opened directly (from the
    /// Dock) cannot hand back to it. So when one is opened directly and the update server lists a different launcher,
    /// it says so and goes no further, rather than updating the game with an old launcher. When the list cannot be
    /// read, it carries on: the update that follows says what is wrong.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    async Task StopIfBehindAsync(CancellationToken cancel)
    {
        var chip = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                   == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        List<ManifestFile>? listed;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"{PatchUrl}/mac/patcher-{chip}.json", cancel));
            listed = doc.RootElement.GetProperty("files").Deserialize<List<ManifestFile>>(Json);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return;
        }
        if (listed == null || listed.Any(f => !MacApps.IsSafePath(f.path))) return;

        // The small program's record of the files it installed, in the shape of this launcher's record of the game.
        var known = ReadKnown(Path.Combine(LauncherFolder, "checked.json"));
        foreach (var f in listed)
            if (!await IsCurrentAtAsync(MacApps.Inside(MacApp, f.path), f, known, cancel))
                throw new InvalidOperationException(
                    $"A newer launcher is out. Open the unwritten app you downloaded, or download it again from {PatchUrl}.");
    }

    /// <summary>ON A MAC, every app a player opens to play is made the small program the game list carries: each
    /// launcher handed out before 2026-10-11 that the Mac knows about, which is what moves those players over — their
    /// Start runs the copy among the game's files, so it is that copy which got this far — and the small program the
    /// player opened, when it is older than the listed one. Run after the game's files are current, so the copy among
    /// them is whole. Each app folder swaps in one step; one this launcher may not write to is left exactly as it is,
    /// and nothing about it stops the update.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    void KeepPlayersMacAppCurrent()
    {
        if (manifest!.game == null || manifest.fetcher is not { } listedVersion) return;
        var prefix = MacFetcherFolder + "/";
        var fetcher = manifest.files.Where(f => f.path.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        if (fetcher.Count == 0 || MacApps.HasRepeats(fetcher.Select(f => f.path))) return;
        foreach (var f in fetcher)
            if (!IsAsync(LocalPath(f.path), f, CancellationToken.None).GetAwaiter().GetResult()) return;

        var replace = new List<string>();
        if (playersApp is { } players && !(players.id == MacApps.FetcherId && players.version >= listedVersion))
            replace.Add(players.path);
        var own = Environment.ProcessPath is { } self ? MacApps.AppOf(self) : null;
        foreach (var old in MacApps.OldLaunchers(InstallRoot))
            if (!replace.Contains(old) && (own == null || !SameFolder(old, own))) replace.Add(old);

        foreach (var app in replace)
        {
            try
            {
                MacApps.ClearLeftovers(app);
                var arriving = MacApps.Arriving(app);
                foreach (var f in fetcher)
                {
                    var to = MacApps.Inside(arriving, f.path[prefix.Length..]);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(LocalPath(f.path), to);
                    if (f.exec) MacApps.MayRun(to);
                }
                MacApps.Swap(arriving, app);
                if (Directory.Exists(arriving)) Directory.Delete(arriving, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                try { MacApps.ClearLeftovers(app); }
                catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    async Task<bool> IsCurrentAtAsync(string local, ManifestFile f, Dictionary<string, Known> known, CancellationToken cancel)
    {
        var info = new FileInfo(local);
        if (!info.Exists || info.Length != f.size) return false;
        if (known.TryGetValue(f.path, out var k) && k.size == info.Length && k.written == info.LastWriteTimeUtc)
            return k.sha256 == f.sha256;
        return await HashAsync(local, cancel) == f.sha256;
    }

    /// <summary>A game the first launcher downloaded beside itself is moved into the player's own folder file by file,
    /// so nobody downloads it a second time. A file that cannot be moved is left where it is and downloaded like any
    /// missing one, and the move is tried again the next time. Nothing is moved while the game is running from there,
    /// or when another drive has no room for it.</summary>
    void MoveFormerCopy(Action<string, double> progress, CancellationToken cancel)
    {
        if (playersFolder == null || !HoldsFormerCopy(playersFolder)) return;
        var formerGame = GameDirIn(playersFolder);
        string Destination(string file) => Path.Combine(GameDir, Path.GetRelativePath(formerGame, file));

        var plays = Path.Combine(formerGame, (manifest!.play ?? manifest.start).Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(plays))
        {
            try { using var alone = new FileStream(plays, FileMode.Open, FileAccess.Read, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("The game is still open. Close it and press Retry."); }
        }

        var files = Directory.EnumerateFiles(formerGame, "*", SearchOption.AllDirectories).ToList();
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(GameDir))!);
        if (!string.Equals(drive.Name, Path.GetPathRoot(Path.GetFullPath(formerGame)), StringComparison.OrdinalIgnoreCase))
        {
            // Onto another drive is a copy, not a rename, and the game is most of a gigabyte.
            long needed = files.Where(f => !File.Exists(Destination(f))).Sum(f => new FileInfo(f).Length);
            if (needed > drive.AvailableFreeSpace)
                throw new InvalidOperationException(
                    $"Drive {drive.Name} needs {needed / 1_000_000:N0} MB free to move the game onto it and has " +
                    $"{drive.AvailableFreeSpace / 1_000_000:N0} MB. Free some space and press Retry.");
        }

        int done = 0, left = 0;
        foreach (var file in files)
        {
            cancel.ThrowIfCancellationRequested();
            progress("Moving your game…", (double)done++ / files.Count);
            try
            {
                var to = Destination(file);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (File.Exists(to)) File.Delete(file);
                else File.Move(file, to);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { left++; }
        }

        // ONLY EMPTY FOLDERS ARE REMOVED, deepest first — never a folder and whatever is still in it.
        foreach (var dir in Directory.EnumerateDirectories(formerGame, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length).Append(formerGame).ToList())
        {
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        // The record goes last and only once every file has: while it is there, the move is tried again.
        if (left > 0) return;
        try
        {
            var formerKnown = KnownFileIn(playersFolder);
            if (File.Exists(KnownFile)) File.Delete(formerKnown);
            else File.Move(formerKnown, KnownFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Take the game off this computer: the game folder and the record of what was downloaded, in the
    /// player's own folder and wherever the first launcher left a copy. This launcher goes too — on Windows once it has
    /// closed, on a Mac at once. The small program the player downloaded stays where they put it — opening it installs everything again.</summary>
    public void Uninstall()
    {
        var roots = new List<string> { InstallRoot };
        if (playersFolder != null && HoldsFormerCopy(playersFolder)) roots.Add(playersFolder);
        foreach (var root in roots)
        {
            if (Directory.Exists(GameDirIn(root))) Directory.Delete(GameDirIn(root), recursive: true);
            if (File.Exists(KnownFileIn(root))) File.Delete(KnownFileIn(root));
        }
        manifest = null;

        // ON A MAC THIS LAUNCHER GOES TOO, at once: a Mac lets a running program's files be deleted.
        if (OperatingSystem.IsMacOS() && Directory.Exists(LauncherFolder))
        {
            held?.Dispose();
            held = null;
            Directory.Delete(LauncherFolder, recursive: true);
        }

        if (KeptByFetcher) RemoveSelfOnceClosed();
        else if (!SameFolder(AppContext.BaseDirectory, InstallRoot) && Directory.Exists(InstallRoot)
                 && !Directory.EnumerateFileSystemEntries(InstallRoot).Any())
            Directory.Delete(InstallRoot);
    }

    /// <summary>A running program cannot delete its own file, so Windows' own command line is left to do it: it tries
    /// once a second for half a minute until this launcher's folder is gone, then removes the folder above it if that
    /// is empty. Started from the temporary folder, because a folder a running program sits in cannot be removed.</summary>
    void RemoveSelfOnceClosed()
    {
        var remove = $"(for /l %i in (1,1,30) do @if exist \"{LauncherFolder}\" (rmdir /s /q \"{LauncherFolder}\" 2>nul & " +
                     $"if exist \"{LauncherFolder}\" ping -n 2 127.0.0.1 >nul)) & rmdir \"{InstallRoot}\" 2>nul";
        Process.Start(new ProcessStartInfo("cmd.exe", $"/d /s /c \"{remove}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath(),
        });
    }

    /// <summary>Start the game and hand over. Only after a finished update — the game is never started half-patched.</summary>
    public void StartGame()
    {
        if (manifest == null) throw new InvalidOperationException("Update first.");
        var game = manifest.game ?? manifest.play ?? manifest.start;
        if (game.StartsWith(MacFetcherFolder + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("The update list names the patcher as the game. Press Retry later.");
        var program = LocalPath(game);
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

    static Dictionary<string, Known> ReadKnown(string file)
    {
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, Known>>(File.ReadAllText(file), Json) ?? []
                : [];
        }
        catch (Exception e) when (e is JsonException or IOException) { return []; }
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
