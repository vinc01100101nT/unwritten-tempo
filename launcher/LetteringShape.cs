using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace UnwrittenLauncher;

/// <summary>The three pictures that give the window its shape: the game's lettering, a rim that follows the
/// lettering's outline, and a slightly wider copy of that rim that shows as a line round it. The rim is worked out
/// from the lettering's own see-through edge each time the launcher starts, so a new lettering file needs no second
/// picture drawn to match it.</summary>
static class LetteringShape
{
    /// <summary>Room left round the lettering for its rim, in the lettering picture's own pixels.</summary>
    const int Room = 22;

    public sealed record Pictures(Bitmap Lettering, Bitmap Rim, Bitmap Edge);

    public static Pictures Load(Color rim, Color edge)
    {
        using var file = AssetLoader.Open(new Uri("avares://unwritten-launcher/Assets/unwritten-lettering.png"));
        using var lettering = SKBitmap.Decode(file);
        return new(Draw(lettering, null, 0), Draw(lettering, rim, 6.8f), Draw(lettering, edge, 8.9f));
    }

    /// <summary>The lettering itself, or — given a colour — its outline grown by <paramref name="spread"/> and filled
    /// with that colour. All three come out the same size, so they sit exactly on top of one another.</summary>
    static Bitmap Draw(SKBitmap lettering, Color? colour, float spread)
    {
        var size = new SKImageInfo(lettering.Width + 2 * Room, lettering.Height + 2 * Room,
                                   SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(size);
        using var paint = new SKPaint();
        if (colour is { } c)
        {
            // A blur spreads the lettering outward and fades it; the table then makes every pixel the blur reached
            // more than faintly fully solid, in the one colour. The result is the lettering, evenly fattened.
            using var blur = SKImageFilter.CreateBlur(spread, spread);
            using var solid = SKColorFilter.CreateColorMatrix(
            [
                0, 0, 0, 0, c.R / 255f,
                0, 0, 0, 0, c.G / 255f,
                0, 0, 0, 0, c.B / 255f,
                0, 0, 0, 14, -1,
            ]);
            paint.ImageFilter = SKImageFilter.CreateColorFilter(solid, blur);
        }
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawBitmap(lettering, Room, Room, paint);

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        png.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
