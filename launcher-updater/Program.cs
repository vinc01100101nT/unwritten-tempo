namespace UnwrittenLauncherUpdater;

static class Program
{
    [STAThread]
    static void Main()
    {
        var fetcher = new Fetcher(
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "unwritten"));

        Window.Open("unwritten", "Checking for patcher update");
        _ = Task.Run(async () =>
        {
            try
            {
                fetcher.Start(await fetcher.FetchAsync(Window.Say, CancellationToken.None));
                Window.Close();
            }
            // The line stays up until the player closes the window: there is nothing here to press.
            catch (FetchFailed e) { Window.Say(e.Message); }
            catch (Exception e) { Window.Say("Something went wrong: " + e.Message); }
        });
        Window.Run();
    }
}
