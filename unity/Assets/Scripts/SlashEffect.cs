using UnityEngine;

/// <summary>
/// A short-lived slash animation played at a world position when an attack lands.
/// Slices its frames straight off a horizontal sprite-sheet texture at runtime
/// (no asset slicing needed), plays them once, then removes itself. Spawned by
/// <see cref="PlayerAttacker"/>; the sheet is assigned in the editor.
/// </summary>
public class SlashEffect : MonoBehaviour
{
    public static void Spawn(Texture2D sheet, int frameSize, int frameCount, float fps, Vector3 worldPos, float scale)
    {
        if (sheet == null || frameSize <= 0) return;
        var go = new GameObject("SlashEffect");
        go.transform.position = worldPos;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = DepthSortRuntime.FxSlash;   // FX band: above all entities + overhead
        go.AddComponent<SlashEffect>().Init(sheet, frameSize, frameCount, fps, sr);
    }

    SpriteRenderer sr;
    Sprite[] frames;
    float fps;
    float t;

    void Init(Texture2D sheet, int frameSize, int frameCount, float fps, SpriteRenderer renderer)
    {
        sr = renderer;
        this.fps = Mathf.Max(1f, fps);
        int cols = Mathf.Max(1, sheet.width / frameSize);
        int n = Mathf.Clamp(frameCount, 1, cols);
        frames = new Sprite[n];
        for (int i = 0; i < n; i++)
            frames[i] = Sprite.Create(sheet, new Rect(i * frameSize, 0, frameSize, frameSize),
                                      new Vector2(0.5f, 0.5f), frameSize);
        sr.sprite = frames[0];
    }

    void Update()
    {
        t += Time.deltaTime * fps;
        int frame = Mathf.FloorToInt(t);
        if (frame >= frames.Length) { Destroy(gameObject); return; }
        sr.sprite = frames[frame];
    }
}
