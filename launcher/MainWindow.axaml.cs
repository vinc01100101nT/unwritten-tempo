using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace UnwrittenLauncher;

public partial class MainWindow : Window
{
    readonly Patcher patcher = new(Program.Beside, Program.BesideId, Program.BesideVersion);
    readonly CancellationTokenSource closing = new();
    bool failed;
    CancellationTokenSource? updateStop;
    Task update = Task.CompletedTask;

    public MainWindow()
    {
        InitializeComponent();

        var shape = LetteringShape.Load(App.Colour("PanelFill").Color, App.Colour("WindowEdge").Color);
        Lettering.Source = shape.Lettering;
        LetteringRim.Source = shape.Rim;
        LetteringEdge.Source = shape.Edge;

        // The window has no title bar, so these three do its jobs: moving the window, minimising it, closing it.
        Shape.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !NewsBox.IsPointerOver) BeginMoveDrag(e);
        };
        MinimiseButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Close();

        StartButton.Click += (_, _) => OnStart();
        ExitButton.Click += (_, _) => Close();
        UninstallButton.Click += (_, _) => AskUninstall(true);
        UninstallCancelButton.Click += (_, _) => AskUninstall(false);
        UninstallYesButton.Click += (_, _) => OnUninstall();
        Closing += (_, _) => closing.Cancel();
        Opened += async (_, _) => await (update = RunAsync());
    }

    async Task RunAsync()
    {
        failed = false;
        StartButton.IsEnabled = false;
        StartButton.Content = "Start";

        _ = ShowNewsAndVersionAsync();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        updateStop = stop;
        Exception? error = null;
        try
        {
            await Task.Run(() => patcher.UpdateAsync(
                (text, fraction) => Dispatcher.UIThread.Post(() => { StatusLabel.Text = text; Bar.Value = fraction; },
                                                             DispatcherPriority.Normal),
                stop.Token));
        }
        catch (Exception e) { error = e; }
        finally { updateStop = null; }

        // EVERY PROGRESS LINE IS QUEUED FOR THE WINDOW, so the result waits in the same queue behind the last of them.
        // Without this a failure's message could be drawn first and the queued "Checking your files…" over it —
        // MEASURED 2026-10-11: a player saw that line beside the Retry button, with no reason given.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Normal);

        if (error is OperationCanceledException) return;
        if (error == null) { StartButton.IsEnabled = true; return; }

        failed = true;
        StatusLabel.Text = error is HttpRequestException
            ? "Could not reach the update server. Check your connection and press Retry."
            : error.Message;
        StartButton.Content = "Retry";
        StartButton.IsEnabled = true;
    }

    void AskUninstall(bool asking)
    {
        Buttons.IsVisible = !asking;
        UninstallQuestion.IsVisible = asking;
    }

    async void OnUninstall()
    {
        AskUninstall(false);
        StartButton.IsEnabled = false;
        UninstallButton.IsEnabled = false;

        // A download still running holds the very files being removed, so it is stopped and waited for first.
        updateStop?.Cancel();
        await update;

        try
        {
            await Task.Run(patcher.Uninstall);
            Close();
        }
        catch (Exception e)
        {
            failed = true;
            StatusLabel.Text = "Could not remove the game: " + e.Message;
            Bar.Value = 0;
            StartButton.Content = "Retry";
            StartButton.IsEnabled = true;
            UninstallButton.IsEnabled = true;
        }
    }

    async Task ShowNewsAndVersionAsync()
    {
        // THE VERSION OF THE FILES THIS COMPUTER GETS, not the shared number: a publish of one platform moves the
        // shared number for both, and showing it made a computer still being served the old files look up to date.
        // When this computer's files are behind the newest version, say so.
        try
        {
            var mine = await patcher.ReadPlatformVersionAsync();
            var newest = await patcher.ReadVersionAsync();
            VersionLabel.Text = mine == null ? "Version unknown — this computer's update predates version numbers"
                              : mine == newest ? "Version " + mine
                              : "Version " + mine + " — the newest, " + newest + ", is not out for this computer yet";
        }
        catch (Exception) { VersionLabel.Text = ""; }

        NewsList.Children.Clear();
        try
        {
            foreach (var item in await patcher.ReadNewsAsync())
                NewsList.Children.Add(NewsEntry(item));
        }
        catch (Exception) { /* no news is not a reason to stop anybody playing */ }
    }

    static Control NewsEntry(Patcher.NewsItem item)
    {
        var entry = new StackPanel { Spacing = 3 };
        entry.Children.Add(new TextBlock
        {
            Text = item.date, FontSize = 11, Foreground = App.Colour("TextDim"),
        });
        entry.Children.Add(new TextBlock
        {
            Text = item.title, FontSize = 14, FontWeight = FontWeight.Bold,
            Foreground = App.Colour("TextMain"), TextWrapping = TextWrapping.Wrap,
        });
        entry.Children.Add(new TextBlock
        {
            Text = item.text, FontSize = 12, Foreground = App.Colour("TextDim"),
            TextWrapping = TextWrapping.Wrap,
        });
        return entry;
    }

    async void OnStart()
    {
        if (failed) { await (update = RunAsync()); return; }
        try
        {
            patcher.StartGame();
            Close();
        }
        catch (Exception e) { StatusLabel.Text = "Could not start the game: " + e.Message; }
    }
}
