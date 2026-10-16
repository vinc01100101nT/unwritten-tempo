using UnityEngine;

/// <summary>
/// THE LOCATION PIN — the one picture of "the place you are being led to": on the Map window's zone view, on its Locate
/// button, and standing on the ground at the spot itself (<see cref="QuestArrows"/>). Drawn once, in white to be tinted.
/// </summary>
public static class GuidePin
{
    /// <summary>Where the pin's point is, as a share of its height from the bottom — the spot it stands on.</summary>
    public const float TipFromBottom = 4f / 64f;

    static Sprite sprite;

    /// <summary>The pin, white, its pivot at its point. Built the first time it is asked for, then kept.</summary>
    public static Sprite Sprite => sprite != null ? sprite : (sprite = Build());

    static Sprite Build()
    {
        const int size = 64, samples = 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        var head = new Vector2(32f, 42f);
        const float radius = 18f, hole = 7f, tipY = 4f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int inside = 0;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                var p = new Vector2(x + (sx + 0.5f) / samples, y + (sy + 0.5f) / samples);
                float d = Vector2.Distance(p, head);
                bool body = d <= radius
                            || (p.y >= tipY && p.y <= head.y && Mathf.Abs(p.x - head.x) <= (p.y - tipY) / (head.y - tipY) * radius * 0.82f);
                if (body && d > hole) inside++;
            }
            px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (samples * samples)));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, TipFromBottom));
    }
}
