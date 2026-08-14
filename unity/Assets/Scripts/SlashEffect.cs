using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A short-lived slash animation played at a world position when an attack lands.
/// Slices its frames straight off a horizontal sprite-sheet texture at runtime
/// (no asset slicing needed), plays them once, then removes itself. Spawned by
/// <see cref="Player3DAttacker"/>; the sheet is assigned in the editor.
/// </summary>
public class SlashEffect : MonoBehaviour
{
    // POOLED. An impact spark fires on every projectile landing, so create-and-destroy here was a GameObject plus
    // three components built and thrown away per hit — exactly what PERFORMANCE.md's checklist means by "anything that
    // spawns repeatedly is pooled". Spent sparks go dormant and are reused instead.
    static readonly Stack<SlashEffect> pool = new Stack<SlashEffect>(16);

    public static void Spawn(Texture2D sheet, int frameSize, int frameCount, float fps, Vector3 worldPos, float scale)
    {
        if (sheet == null || frameSize <= 0) return;

        SlashEffect fx = null;
        // A scene unload destroys pooled objects while the static pool still holds them — drop those on the way out.
        while (pool.Count > 0 && fx == null) fx = pool.Pop();
        if (fx == null)
        {
            var go = new GameObject("SlashEffect") { hideFlags = HideFlags.DontSaveInEditor };
            var r = go.AddComponent<SpriteRenderer>();
            r.sortingOrder = DepthSortRuntime.FxSlash;   // FX band: above all entities + overhead
            go.AddComponent<Billboard>();                // face the camera in 2.5D
            fx = go.AddComponent<SlashEffect>();
            fx.sr = r;
        }

        fx.transform.position = worldPos;
        fx.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        fx.Init(sheet, frameSize, frameCount, fps);
    }

    SpriteRenderer sr;
    Sprite[] frames;
    float fps;
    float t;

    void Init(Texture2D sheet, int frameSize, int frameCount, float fps)
    {
        this.fps = Mathf.Max(1f, fps);
        t = 0f;
        // Shared slice cache: every slash/bolt/smoke off the same sheet reuses ONE Sprite[] instead of
        // re-slicing (allocating) the sheet on every spawn — this runs on every hit.
        frames = SpriteSheetCache.Strip(sheet, frameSize, frameCount);
        if (frames.Length == 0) { Recycle(); return; }
        sr.sprite = frames[0];
        gameObject.SetActive(true);
    }

    void Update()
    {
        t += Time.deltaTime * fps;
        int frame = Mathf.FloorToInt(t);
        if (frame >= frames.Length) { Recycle(); return; }
        sr.sprite = frames[frame];
    }

    // Go dormant and wait to be reused. Clearing the sprite drops the only reference this object holds, so a pooled
    // spark can't pin a texture the rest of the game has finished with.
    void Recycle()
    {
        frames = null;
        sr.sprite = null;
        gameObject.SetActive(false);
        pool.Push(this);
    }
}
