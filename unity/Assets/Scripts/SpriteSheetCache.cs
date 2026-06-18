using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared, lazily-built cache of sprites sliced from sprite-sheets at runtime, keyed by
/// (texture, geometry). <see cref="Sprite.Create"/> allocates a managed Sprite object per frame and
/// is NOT free — slicing the same sheet on every spawn (slash/bolt/smoke FX) or once per mob (walk
/// frames) churns the GC and duplicates identical sprites in memory. Slicing through this cache means
/// every caller that asks for the same cut shares ONE immutable <c>Sprite[]</c>.
///
/// Returned arrays are SHARED and must be treated as read-only (never mutate the elements). The cache
/// is static so it survives scene loads; it resets on domain reload (entering Play), which is exactly
/// when the underlying textures are reloaded too.
/// </summary>
public static class SpriteSheetCache
{
    // Horizontal strip: N square frames along the BOTTOM row (y = 0). Used by slash/bolt/smoke FX and
    // the player's 4 attack poses (which are a 1-row strip).
    static readonly Dictionary<(int tex, int size, int count), Sprite[]> strips
        = new Dictionary<(int, int, int), Sprite[]>();

    // 4-direction walk columns (Down/Up/Left/Right) read from the TOP rows of each column. Used by mobs.
    static readonly Dictionary<(int tex, int cell, int frames), Sprite[][]> walk4
        = new Dictionary<(int, int, int), Sprite[][]>();

    /// <summary>N square frames of side <paramref name="frameSize"/> along the sheet's bottom row,
    /// clamped to the number of columns. Pivot centred, pixels-per-unit = frameSize. Shared/read-only.</summary>
    public static Sprite[] Strip(Texture2D sheet, int frameSize, int frameCount)
    {
        if (sheet == null || frameSize <= 0) return System.Array.Empty<Sprite>();
        int cols = Mathf.Max(1, sheet.width / frameSize);
        int n = Mathf.Clamp(frameCount, 1, cols);

        var key = (sheet.GetInstanceID(), frameSize, n);
        if (strips.TryGetValue(key, out var cached)) return cached;

        var frames = new Sprite[n];
        for (int i = 0; i < n; i++)
            frames[i] = Sprite.Create(sheet, new Rect(i * frameSize, 0, frameSize, frameSize),
                                      new Vector2(0.5f, 0.5f), frameSize);
        strips[key] = frames;
        return frames;
    }

    /// <summary>The four directional walk clips [down, up, left, right], each <paramref name="walkFrames"/>
    /// frames read down the TOP of columns 0..3 of a <paramref name="cell"/>px grid. Shared/read-only.</summary>
    public static Sprite[][] Walk4(Texture2D sheet, int cell, int walkFrames)
    {
        if (sheet == null || cell <= 0)
            return new[] { System.Array.Empty<Sprite>(), System.Array.Empty<Sprite>(),
                           System.Array.Empty<Sprite>(), System.Array.Empty<Sprite>() };

        int cols = Mathf.Max(1, sheet.width / cell);
        int rows = Mathf.Max(1, sheet.height / cell);
        int wf = Mathf.Clamp(walkFrames, 1, rows);

        var key = (sheet.GetInstanceID(), cell, wf);
        if (walk4.TryGetValue(key, out var cached)) return cached;

        Sprite[] Column(int c)
        {
            c = Mathf.Min(c, cols - 1);
            var arr = new Sprite[wf];
            for (int r = 0; r < wf; r++)
            {
                // Unity texture origin is bottom-left; row 0 is the TOP of the sheet.
                var rect = new Rect(c * cell, sheet.height - (r + 1) * cell, cell, cell);
                arr[r] = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), cell);
            }
            return arr;
        }

        var dirs = new[] { Column(0), Column(1), Column(2), Column(3) };
        walk4[key] = dirs;
        return dirs;
    }
}
