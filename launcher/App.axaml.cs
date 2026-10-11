using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;

namespace UnwrittenLauncher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LoadPalette();
    }

    /// <summary>THE GAME'S OWN COLOURS, each under the game's own name for it, read from the file
    /// <c>tools/ui-tokens.py</c> writes from the game's code and <c>tools/build-launcher.sh</c> refreshes before every
    /// build — so the launcher takes the game's look and cannot keep an old one.</summary>
    void LoadPalette()
    {
        using var file = AssetLoader.Open(new Uri("avares://unwritten-launcher/Assets/ui-tokens.json"));
        using var tokens = JsonDocument.Parse(file);
        foreach (var colour in tokens.RootElement.GetProperty("palette").EnumerateObject())
            Resources[colour.Name] = new SolidColorBrush(Color.Parse(colour.Value.GetProperty("hex").GetString()!),
                                                         colour.Value.GetProperty("alpha").GetDouble());

        // The game's title bar runs from one palette colour at the top to another at the bottom.
        Resources["TitleBar"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colour("TitleBarTop").Color, 0),
                new GradientStop(Colour("TitleBarBottom").Color, 1),
            },
        };
    }

    public static SolidColorBrush Colour(string name) => (SolidColorBrush)Current!.Resources[name]!;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
