using Avalonia;

namespace UnwrittenLauncher;

static class Program
{
    /// <summary>The folder the small downloaded program is kept in, when that program started this launcher.</summary>
    public static string? Beside { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        var named = Array.IndexOf(args, "--beside");
        if (named >= 0 && named + 1 < args.Length) Beside = args[named + 1];

        if (new Patcher(Beside).HandedToFetcher(startedByFetcher: named >= 0)) return;
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
