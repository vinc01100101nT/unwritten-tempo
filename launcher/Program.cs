using Avalonia;

namespace UnwrittenLauncher;

static class Program
{
    /// <summary>The folder the small downloaded program is kept in, when that program started this launcher.</summary>
    public static string? Beside { get; private set; }

    /// <summary>On a Mac, which of our two apps <see cref="Beside"/> is, and its build number.</summary>
    public static string? BesideId { get; private set; }
    public static string? BesideVersion { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        string? After(string flag)
        {
            var at = Array.IndexOf(args, flag);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
        var named = Array.IndexOf(args, "--beside");
        Beside = After("--beside");
        BesideId = After("--beside-id");
        BesideVersion = After("--beside-version");

        if (new Patcher(Beside).HandedToFetcher(startedByFetcher: named >= 0)) return;
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
