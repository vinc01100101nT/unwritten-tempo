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
    // Normalized height of the transparent foot-padding at the BOTTOM of a mob cell — i.e. how far up the
    // cell the character's feet actually sit. The Walk4 pivot is placed here so a mob's rendered feet land
    // ON its (ground-seated) transform, with the padding hanging below to be alpha-clipped by the ground.
    // SINGLE OWNER of this value: Build25DSpike.SpriteFoot (editor/build side) reads it from here.
    public const float MobFootPivotY = 0.1f;

    // Horizontal strip: N square frames along the BOTTOM row (y = 0). Used by slash/bolt/smoke FX and
    // the player's 4 attack poses (which are a 1-row strip).
    static readonly Dictionary<(int tex, int size, int count), Sprite[]> strips
        = new Dictionary<(int, int, int), Sprite[]>();

    // 4-direction walk columns (Down/Up/Left/Right) read from the TOP rows of each column. Used by mobs.
    static readonly Dictionary<(int tex, int cell, int frames), Sprite[][]> walk4
        = new Dictionary<(int, int, int), Sprite[][]>();

    // Arbitrary horizontal clips read left→right from a grid cell. Used by the row-major NinjaGreen
    // "CharacterAnimated" sheets (one direction per ROW), vs walk4's column-major mob sheets.
    static readonly Dictionary<(int tex, int cell, int row, int col, int frames, int ppu), Sprite[]> grids
        = new Dictionary<(int, int, int, int, int, int), Sprite[]>();

    // Single horizontal strip of NON-square cells (cellW × cellH) read left→right along the BOTTOM row.
    // Strip/Grid/Walk4 above all assume SQUARE cells; tall directional strips (one PNG per direction) need
    // their own cut. Used by the baked 8-direction billboard (BakedBillboard8).
    static readonly Dictionary<(int tex, int w, int h, int frames, int ppu), Sprite[]> rows
        = new Dictionary<(int, int, int, int, int), Sprite[]>();

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
                // Foot-line pivot: the sprite renders UP from the transform, which sits on the ground
                // (feet-pivot invariant — see UnitAnchor). Mobs are the only Walk4 consumer.
                arr[r] = Sprite.Create(sheet, rect, new Vector2(0.5f, MobFootPivotY), cell);
            }
            return arr;
        }

        var dirs = new[] { Column(0), Column(1), Column(2), Column(3) };
        walk4[key] = dirs;
        return dirs;
    }

    /// <summary>One horizontal clip: <paramref name="frames"/> cells of side <paramref name="cell"/>,
    /// read left→right starting at grid (<paramref name="row"/>, <paramref name="col"/>). Row 0 is the
    /// TOP of the sheet (Unity's texture origin is bottom-left). Pivot centred; pixels-per-unit =
    /// <paramref name="ppu"/> — pass the cell size for a 1-unit sprite, or a SMALLER ppu to render the
    /// art larger (NinjaGreen's 32px frames slice at ppu 16 so the ~16px character is ~1 unit tall).
    /// Shared/read-only. The slicer behind the row-major NinjaGreen player animations.</summary>
    public static Sprite[] Grid(Texture2D sheet, int cell, int row, int col, int frames, int ppu)
    {
        if (sheet == null || cell <= 0 || frames <= 0) return System.Array.Empty<Sprite>();
        if (ppu <= 0) ppu = cell;
        int cols = Mathf.Max(1, sheet.width / cell);
        int n = Mathf.Clamp(frames, 1, Mathf.Max(1, cols - col));

        var key = (sheet.GetInstanceID(), cell, row, col, n, ppu);
        if (grids.TryGetValue(key, out var cached)) return cached;

        var arr = new Sprite[n];
        for (int i = 0; i < n; i++)
        {
            var rect = new Rect((col + i) * cell, sheet.height - (row + 1) * cell, cell, cell);
            arr[i] = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), ppu);
        }
        grids[key] = arr;
        return arr;
    }

    /// <summary><paramref name="frames"/> cells of <paramref name="cellW"/>×<paramref name="cellH"/> read
    /// left→right along the sheet's BOTTOM row (a single-row strip), clamped to the column count. Pivot is
    /// centred horizontally and on the CELL bottom-eighth so the character's feet sit near the transform
    /// regardless of cell height; pixels-per-unit = <paramref name="ppu"/>. Shared/read-only. This is the
    /// non-square cousin of <see cref="Strip"/> for tall directional strips (used by BakedBillboard8).</summary>
    public static Sprite[] Row(Texture2D sheet, int cellW, int cellH, int frames, int ppu)
    {
        if (sheet == null || cellW <= 0 || cellH <= 0) return System.Array.Empty<Sprite>();
        if (ppu <= 0) ppu = cellH;
        int cols = Mathf.Max(1, sheet.width / cellW);
        int n = Mathf.Clamp(frames, 1, cols);

        var key = (sheet.GetInstanceID(), cellW, cellH, n, ppu);
        if (rows.TryGetValue(key, out var cached)) return cached;

        // Centre pivot — grounding is handled by the controller lifting the transform to its measured
        // foot offset, so a plain centre pivot keeps every action's frames consistent.
        var frames2 = new Sprite[n];
        for (int i = 0; i < n; i++)
            frames2[i] = Sprite.Create(sheet, new Rect(i * cellW, 0, cellW, cellH),
                                       new Vector2(0.5f, 0.5f), ppu);
        rows[key] = frames2;
        return frames2;
    }
}
