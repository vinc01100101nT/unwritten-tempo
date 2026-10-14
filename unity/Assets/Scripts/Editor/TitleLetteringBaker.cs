using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes the one picture the front screens draw at their top (<see cref="TitleLettering"/>): the launcher's lettering
/// file, on the two rims the launcher grows round it at its start — the lettering's see-through edge blurred, then
/// every pixel the blur reached more than faintly made solid in one colour. Run once through the live editor after the
/// lettering file changes: <c>TitleLetteringBaker.Bake()</c>. Nothing calls it on its own.
/// </summary>
public static class TitleLetteringBaker
{
    // The launcher's own file — read from where the launcher reads it, so there is one lettering.
    const string Source = "/../../wiki/assets/unwritten-lettering.png";
    const string Output = "Assets/3_Data/Resources/" + TitleLettering.PicturePath + ".png";

    // The launcher's numbers: room round the lettering for its rims, and how far each rim spreads.
    const int Room = 22;
    const float RimSpread = 6.8f, EdgeSpread = 8.9f;

    // editor-write-ok: only ever run by hand through the live editor, after the lettering file changes
    public static string Bake()
    {
        var art = new Texture2D(2, 2);
        art.LoadImage(File.ReadAllBytes(Application.dataPath + Source));
        var src = art.GetPixels32();
        int aw = art.width, ah = art.height;
        Object.DestroyImmediate(art);

        // Rounded up to a multiple of four, which block compression needs; the spare rows go at the bottom.
        int w = aw + 2 * Room, h = ah + 2 * Room;
        int w4 = (w + 3) / 4 * 4, h4 = (h + 3) / 4 * 4;
        int dx = Room + (w4 - w) / 2, dy = Room + (h4 - h);

        var alpha = new float[w4 * h4];
        for (int y = 0; y < ah; y++)
        for (int x = 0; x < aw; x++)
            alpha[(y + dy) * w4 + x + dx] = src[y * aw + x].a / 255f;

        var edge = Blur(alpha, w4, h4, EdgeSpread);
        var rim = Blur(alpha, w4, h4, RimSpread);
        var px = new Color[w4 * h4];
        for (int i = 0; i < px.Length; i++)
        {
            var c = Solid(UiKit.WindowEdge, edge[i]);
            c = Over(Solid(UiKit.PanelFill, rim[i]), c);
            int x = i % w4 - dx, y = i / w4 - dy;
            if (x >= 0 && x < aw && y >= 0 && y < ah) c = Over(src[y * aw + x], c);
            px[i] = c;
        }

        var tex = new Texture2D(w4, h4, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        File.WriteAllBytes(Output, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(Output);
        return "baked " + w4 + "x" + h4;
    }

    static Color Solid(Color colour, float blurred)
    {
        colour.a = Mathf.Clamp01(14f * blurred - 1f);
        return colour;
    }

    // One colour laid over another, both with their own see-through amount.
    static Color Over(Color top, Color under)
    {
        float a = top.a + under.a * (1f - top.a);
        if (a <= 0f) return new Color(0f, 0f, 0f, 0f);
        var rgb = ((Vector4)top * top.a + (Vector4)under * under.a * (1f - top.a)) / a;
        return new Color(rgb.x, rgb.y, rgb.z, a);
    }

    // A Gaussian blur of standard deviation sigma, across then down — the launcher's blur.
    static float[] Blur(float[] src, int w, int h, float sigma)
    {
        int r = Mathf.CeilToInt(sigma * 3f);
        var k = new float[2 * r + 1];
        float sum = 0f;
        for (int i = -r; i <= r; i++) sum += k[i + r] = Mathf.Exp(-i * i / (2f * sigma * sigma));
        for (int i = 0; i < k.Length; i++) k[i] /= sum;

        var across = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = 0f;
            for (int i = -r; i <= r; i++)
            {
                int xx = x + i;
                if (xx >= 0 && xx < w) v += src[y * w + xx] * k[i + r];
            }
            across[y * w + x] = v;
        }

        var down = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = 0f;
            for (int i = -r; i <= r; i++)
            {
                int yy = y + i;
                if (yy >= 0 && yy < h) v += across[yy * w + x] * k[i + r];
            }
            down[y * w + x] = v;
        }
        return down;
    }
}
