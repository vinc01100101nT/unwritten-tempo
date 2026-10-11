using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace UnwrittenMac;

/// <summary>What both Mac programs — the small one a player downloads (this folder) and the launcher it fetches
/// (<c>launcher/</c>, which links this file) — need to do to a Mac app folder: check a path an update list names
/// inside one, swap one app folder for another, read what an app says it is. One copy, so the two cannot disagree.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public static partial class MacApps
{
    /// <summary>The bundle names our two Mac programs carry in their Info.plist. Only an app saying one of these is
    /// ever replaced.</summary>
    public const string LauncherId = "com.unwritten.launcher", FetcherId = "com.unwritten.patcher";

    /// <summary>The program inside either app, as tools/build-launcher.sh names it.</summary>
    public const string Program = "Contents/MacOS/unwritten-launcher";

    /// <summary>True when a path from an update list can only land inside the folder it is written into: relative,
    /// forward slashes, and no empty, "." or ".." part.</summary>
    public static bool IsSafePath(string path) =>
        path.Length > 0 && path[0] != '/' && !path.Contains('\\') && !path.Contains('\0')
        && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

    /// <summary>Where <paramref name="path"/> lands inside <paramref name="folder"/>. Refuses anything that would
    /// land anywhere else, whatever the update list says.</summary>
    public static string Inside(string folder, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!IsSafePath(path) || !full.StartsWith(root + "/", StringComparison.Ordinal))
            throw new InvalidDataException($"The update list names a file outside its folder: {path}");
        return full;
    }

    /// <summary>A Mac treats two names differing only in capitals as the same file, so a list naming both would
    /// write one file twice.</summary>
    public static bool HasRepeats(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return paths.Any(p => !seen.Add(p));
    }

    /// <summary>Put the folder at <paramref name="arriving"/> where <paramref name="current"/> is, in one step:
    /// the two swap places, so at no moment is there no app under the name the player opens. The old app is then
    /// at <paramref name="arriving"/>, for the caller to delete. With nothing at <paramref name="current"/>, a
    /// plain rename.</summary>
    public static void Swap(string arriving, string current)
    {
        if (!Directory.Exists(current)) { Directory.Move(arriving, current); return; }
        if (renamex_np(arriving, current, RenameSwap) != 0)
            throw new IOException($"Could not put the new app in place ({Marshal.GetLastPInvokeError()}).");
    }

    /// <summary>Undo what a swap cut short: an app missing with its old copy beside it is put back, and a half-built
    /// or swapped-out copy is removed. Both are named after the app with a dot in front, so Finder never shows them.</summary>
    public static void ClearLeftovers(string app)
    {
        string arriving = Arriving(app), leaving = Leaving(app);
        if (!Directory.Exists(app) && Directory.Exists(leaving)) Directory.Move(leaving, app);
        foreach (var dir in new[] { arriving, leaving })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public static string Arriving(string app) => Hidden(app, ".new");
    public static string Leaving(string app) => Hidden(app, ".old");
    static string Hidden(string app, string end) =>
        Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(app))!,
                     "." + Path.GetFileName(Path.TrimEndingDirectorySeparator(app)) + end);

    /// <summary>The app folder a program file sits in: the nearest folder above it ending in ".app" that holds an
    /// Info.plist. Null when it is not inside one.</summary>
    public static string? AppOf(string program)
    {
        for (var dir = Path.GetDirectoryName(program); dir != null; dir = Path.GetDirectoryName(dir))
            if (dir.EndsWith(".app", StringComparison.Ordinal) && File.Exists(Path.Combine(dir, "Contents", "Info.plist")))
                return dir;
        return null;
    }

    /// <summary>A text value from an app's Info.plist, as tools/build-launcher.sh and Unity write it (the plain-text
    /// kind). Null when it is not there or cannot be read.</summary>
    public static string? Plist(string app, string key)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(app, "Contents", "Info.plist"));
            var found = Regex.Match(text, "<key>" + Regex.Escape(key) + @"</key>\s*<string>([^<]*)</string>");
            return found.Success ? found.Groups[1].Value.Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Every launcher handed out before 2026-10-11 that this Mac knows about, wherever the player keeps it:
    /// the apps the Mac's own app index lists under the launcher's bundle name and that carry no build number — every
    /// launcher built since carries one (tools/build-launcher.sh), so a launcher kept by the small program, or one
    /// built on a developer's Mac, is never among them. Never one in the Trash, never the read-only copy the Mac may
    /// run a downloaded app from, never one inside <paramref name="installRoot"/>.</summary>
    public static List<string> OldLaunchers(string installRoot)
    {
        var found = new List<string>();
        foreach (var app in Registered(LauncherId))
        {
            var path = Path.TrimEndingDirectorySeparator(app);
            if (Within(path, installRoot) || path.Contains("/.Trash/") || path.Contains("/AppTranslocation/")
                || !path.EndsWith(".app", StringComparison.Ordinal) || found.Contains(path)) continue;
            if (Plist(path, "CFBundleIdentifier") == LauncherId && Plist(path, "CFBundleVersion") == null) found.Add(path);
        }
        return found;
    }

    /// <summary>The apps the Mac's app index (Launch Services) lists under a bundle name. It learns of an app the
    /// moment one lands in a folder Spotlight looks at — measured 2026-10-11: two launchers built minutes before were
    /// both listed, never opened.</summary>
    static unsafe List<string> Registered(string bundleId)
    {
        var apps = new List<string>();
        var bytes = System.Text.Encoding.UTF8.GetBytes(bundleId + "\0");
        nint name, list;
        fixed (byte* b = bytes) name = CFStringCreateWithCString(0, b, 0x08000100);
        if (name == 0) return apps;
        try
        {
            list = LSCopyApplicationURLsForBundleIdentifier(name, 0);
            if (list == 0) return apps;
            try
            {
                var buffer = stackalloc byte[4096];
                for (nint i = 0, n = CFArrayGetCount(list); i < n; i++)
                    if (CFURLGetFileSystemRepresentation(CFArrayGetValueAtIndex(list, i), 1, buffer, 4096) != 0)
                        apps.Add(Marshal.PtrToStringUTF8((nint)buffer)!);
            }
            finally { CFRelease(list); }
        }
        finally { CFRelease(name); }
        return apps;
    }

    public static bool Within(string folder, string root)
    {
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)),
               b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return a == b || a.StartsWith(b + "/", StringComparison.Ordinal);
    }

    /// <summary>Give a downloaded program back its permission to run, which a download arrives without.</summary>
    public static void MayRun(string file) =>
        File.SetUnixFileMode(file, File.GetUnixFileMode(file)
                                   | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

    const uint RenameSwap = 0x2;
    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [LibraryImport(CoreFoundation)] private static unsafe partial nint CFStringCreateWithCString(nint allocator, byte* text, uint encoding);
    [LibraryImport(CoreFoundation)] private static partial nint CFArrayGetCount(nint list);
    [LibraryImport(CoreFoundation)] private static partial nint CFArrayGetValueAtIndex(nint list, nint index);
    [LibraryImport(CoreFoundation)]
    private static unsafe partial byte CFURLGetFileSystemRepresentation(nint url, byte resolveAgainstBase, byte* buffer, nint length);
    [LibraryImport(CoreFoundation)] private static partial void CFRelease(nint thing);
    [LibraryImport("/System/Library/Frameworks/CoreServices.framework/CoreServices")]
    private static partial nint LSCopyApplicationURLsForBundleIdentifier(nint bundleId, nint error);

    [LibraryImport("/usr/lib/libSystem.B.dylib", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int renamex_np(string from, string to, uint flags);
}
