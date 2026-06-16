using UnityEngine;

/// <summary>
/// A brief ring that flashes at a clicked ground point to confirm a move / attack-move
/// order (the Dota-style "click ripple"). It expands slightly and fades out over a few
/// tenths of a second, then removes itself. Built from a single procedural ring sprite
/// (shared/cached), so it needs no art assets. Spawn it with <see cref="Spawn"/>.
/// </summary>
public class ClickMarker : MonoBehaviour
{
    const float Life = 0.4f;
    const float StartScale = 0.7f;
    const float EndScale = 1.2f;

    static Sprite ringSprite;

    public static void Spawn(Vector3 worldPos, Color color)
    {
        var go = new GameObject("ClickMarker");
        go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Ring();
        sr.color = color;
        sr.sortingOrder = DepthSortRuntime.FxClickMarker;   // FX band: above all entities + overhead
        go.AddComponent<ClickMarker>().sr = sr;
    }

    SpriteRenderer sr;
    float t;

    void Update()
    {
        t += Time.deltaTime;
        float k = t / Life;
        if (k >= 1f) { Destroy(gameObject); return; }

        transform.localScale = Vector3.one * Mathf.Lerp(StartScale, EndScale, k);
        var c = sr.color;
        c.a = 1f - k;                               // fade out
        sr.color = c;
    }

    // A hollow white ring (1 world unit at scale 1). Tinted by the SpriteRenderer color.
    static Sprite Ring()
    {
        if (ringSprite != null) return ringSprite;

        const int s = 32;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        var clear = new Color(0, 0, 0, 0);
        var center = new Vector2((s - 1) / 2f, (s - 1) / 2f);
        const float outer = 15f, inner = 11f;       // ring band thickness

        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                px[y * s + x] = (d <= outer && d >= inner) ? Color.white : clear;
            }

        tex.SetPixels(px);
        tex.Apply();
        ringSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
        return ringSprite;
    }
}
