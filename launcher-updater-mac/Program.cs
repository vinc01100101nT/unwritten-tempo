// A Mac program and nothing else, so every Mac-only call in it is allowed.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("macos")]

namespace UnwrittenLauncherUpdater;

static class Program
{
    static int Main()
    {
        var fetcher = new MacFetcher(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "unwritten"));
        var players = fetcher.FindPlayersApp();

        // With UNWRITTEN_PATCHER_TEXT set, the lines go to the terminal instead of a window, so the whole program
        // can be checked from a terminal with nothing put on the screen.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UNWRITTEN_PATCHER_TEXT")))
        {
            try
            {
                var (program, address) = fetcher.FetchAsync(Console.WriteLine, CancellationToken.None).GetAwaiter().GetResult();
                fetcher.Start(program, address, players);
                return 0;
            }
            catch (Exception e) { Console.WriteLine(e is FetchFailed ? e.Message : "Something went wrong: " + e.Message); return 1; }
        }

        MacWindow.Open("unwritten", "Checking for patcher update");
        _ = Task.Run(async () =>
        {
            try
            {
                var (program, address) = await fetcher.FetchAsync(MacWindow.Say, CancellationToken.None);
                fetcher.Start(program, address, players);
                MacWindow.Close();
            }
            // The line stays up until the player closes the window: there is nothing here to press.
            catch (FetchFailed e) { MacWindow.Say(e.Message); }
            catch (Exception e) { MacWindow.Say("Something went wrong: " + e.Message); }
        });
        MacWindow.Run();
        return 0;
    }
}
