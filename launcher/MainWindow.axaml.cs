using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace UnwrittenLauncher;

public partial class MainWindow : Window
{
    readonly Patcher patcher = new();
    readonly CancellationTokenSource closing = new();
    bool failed;

    public MainWindow()
    {
        InitializeComponent();
        StartButton.Click += (_, _) => OnStart();
        ExitButton.Click += (_, _) => Close();
        Closing += (_, _) => closing.Cancel();
        Opened += async (_, _) => await RunAsync();
    }

    async Task RunAsync()
    {
        failed = false;
        StartButton.IsEnabled = false;
        StartButton.Content = "Start";

        _ = ShowNewsAndVersionAsync();

        try
        {
            await Task.Run(() => patcher.UpdateAsync(
                (text, fraction) => Dispatcher.UIThread.Post(() => { StatusLabel.Text = text; Bar.Value = fraction; }),
                closing.Token));
            StartButton.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            failed = true;
            StatusLabel.Text = e is HttpRequestException
                ? "Could not reach the update server. Check your connection and press Retry."
                : e.Message;
            StartButton.Content = "Retry";
            StartButton.IsEnabled = true;
        }
    }

    async Task ShowNewsAndVersionAsync()
    {
        try { VersionLabel.Text = "Version " + await patcher.ReadVersionAsync(); }
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
            Text = item.date, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#a79e88")),
        });
        entry.Children.Add(new TextBlock
        {
            Text = item.title, FontSize = 14, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#e7e3d5")), TextWrapping = TextWrapping.Wrap,
        });
        entry.Children.Add(new TextBlock
        {
            Text = item.text, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#a79e88")),
            TextWrapping = TextWrapping.Wrap,
        });
        return entry;
    }

    async void OnStart()
    {
        if (failed) { await RunAsync(); return; }
        try
        {
            patcher.StartGame();
            Close();
        }
        catch (Exception e) { StatusLabel.Text = "Could not start the game: " + e.Message; }
    }
}
