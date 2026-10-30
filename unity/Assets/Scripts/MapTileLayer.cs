using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE SQUARE PICTURES OF A TILED MAP (a seamless world, <see cref="MinimapCatalog.Entry.tileSize"/> above 0), laid side
/// by side — the one layer the minimap and the Map window both draw them with. Each call says which part of the map is
/// in view and where a map point falls on the layer; a square is taken from <see cref="MinimapCatalog"/> when it comes
/// into view and given back when it leaves, so only the squares in view are held, however big the world.
/// </summary>
public sealed class MapTileLayer
{
    sealed class Tile
    {
        public RawImage image;
        public string picture;     // the name it was taken under; null = this slot is free
        public int gx, gy;
    }
    readonly List<Tile> tiles = new List<Tile>();
    readonly RectTransform root;
    string map;
    float tileSize;
    Vector2 laidOrigin, laidScale;

    /// <param name="root">The layer: each square is placed from its middle, so the layer's middle is where
    /// <c>origin</c> in <see cref="Show"/> is measured from.</param>
    public MapTileLayer(RectTransform root) { this.root = root; }

    /// <summary>Lay the squares of <paramref name="tiledMap"/> that <paramref name="inView"/> touches (map units, x east,
    /// y north). A map point p is drawn at <paramref name="origin"/> + p × <paramref name="unitsToLayer"/> on the layer.
    /// Squares stop loading once one call has spent <paramref name="loadMilliseconds"/> on them (at least one is loaded);
    /// the rest come on the next calls.</summary>
    public void Show(string tiledMap, float squareSize, Rect inView, Vector2 origin, Vector2 unitsToLayer,
                     float loadMilliseconds = float.PositiveInfinity)
    {
        if (tiledMap != map || squareSize != tileSize) { Clear(); map = tiledMap; tileSize = squareSize; }
        int gx0 = Mathf.FloorToInt(inView.xMin / tileSize), gx1 = Mathf.FloorToInt(inView.xMax / tileSize);
        int gy0 = Mathf.FloorToInt(inView.yMin / tileSize), gy1 = Mathf.FloorToInt(inView.yMax / tileSize);

        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            if (t.picture == null || (t.gx >= gx0 && t.gx <= gx1 && t.gy >= gy0 && t.gy <= gy1)) continue;
            Free(t);
        }

        bool moved = origin != laidOrigin || unitsToLayer != laidScale;
        laidOrigin = origin; laidScale = unitsToLayer;
        if (moved)
            for (int i = 0; i < tiles.Count; i++)
                if (tiles[i].picture != null) Place(tiles[i]);

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        double ticks = loadMilliseconds * System.Diagnostics.Stopwatch.Frequency / 1000.0;
        bool spent = false;
        for (int gy = gy0; gy <= gy1 && !spent; gy++)
            for (int gx = gx0; gx <= gx1 && !spent; gx++)
                if (!Holding(gx, gy))
                {
                    Place(Take(gx, gy));
                    spent = System.Diagnostics.Stopwatch.GetTimestamp() - start >= ticks;
                }
    }

    /// <summary>Give back every square held.</summary>
    public void Clear()
    {
        for (int i = 0; i < tiles.Count; i++) if (tiles[i].picture != null) Free(tiles[i]);
    }

    void Place(Tile t)
    {
        if (!t.image.enabled) return;
        var rt = t.image.rectTransform;
        rt.anchoredPosition = laidOrigin + new Vector2((t.gx + 0.5f) * tileSize * laidScale.x, (t.gy + 0.5f) * tileSize * laidScale.y);
        rt.sizeDelta = new Vector2(tileSize * laidScale.x, tileSize * laidScale.y);
    }

    bool Holding(int gx, int gy)
    {
        for (int i = 0; i < tiles.Count; i++)
            if (tiles[i].picture != null && tiles[i].gx == gx && tiles[i].gy == gy) return true;
        return false;
    }

    // A square with no picture (open sea, past the world's edge) is held all the same, with its image off, so it is
    // not asked for again every call.
    Tile Take(int gx, int gy)
    {
        Tile t = null;
        for (int i = 0; i < tiles.Count && t == null; i++) if (tiles[i].picture == null) t = tiles[i];
        if (t == null)
        {
            var go = new GameObject("Tile", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)) { hideFlags = HideFlags.DontSaveInEditor };
            go.transform.SetParent(root, false);
            var img = go.GetComponent<RawImage>();
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            t = new Tile { image = img };
            tiles.Add(t);
        }
        t.gx = gx; t.gy = gy;
        t.picture = MinimapCatalog.TilePicture(map, gx, gy);
        var tex = MinimapCatalog.AcquirePicture(t.picture);
        t.image.texture = tex;
        t.image.enabled = tex != null;
        return t;
    }

    void Free(Tile t)
    {
        if (t.image.texture != null) MinimapCatalog.ReleasePicture(t.picture);
        t.picture = null;
        t.image.texture = null;
        t.image.enabled = false;
    }
}
