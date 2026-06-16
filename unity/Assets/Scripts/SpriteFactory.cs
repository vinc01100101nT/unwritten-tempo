using UnityEngine;

/// <summary>
/// Generates simple solid-colour sprites at runtime so the hello-world needs
/// zero imported art. Each sprite is 1x1 world unit (pixelsPerUnit == size).
/// </summary>
public static class SpriteFactory
{
    public static Sprite Solid(Color color, int size = 16)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), // pivot = centre
            size                      // pixelsPerUnit → 1 sprite == 1 world unit
        );
    }
}
