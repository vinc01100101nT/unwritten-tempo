using UnityEngine;

/// <summary>
/// A reusable looping status indicator that floats above a unit (e.g. the spinning swirl over a stunned
/// mob). Created once per unit and reused: <see cref="Show"/> reveals it for a duration, then it hides
/// itself. FOLLOWS the unit (rather than parenting) so it keeps a constant world scale, and billboards to
/// the camera. Frames are shared via <see cref="SpriteSheetCache"/>. (Same pattern as
/// <see cref="GuardShieldFX"/>, generalized for status effects.)
/// </summary>
public class StatusFX : MonoBehaviour
{
    Transform follow;
    Vector3 offset;
    SpriteRenderer sr;
    Sprite[] frames;
    float fps, endTime, t;

    public static StatusFX Create(Transform follow, Texture2D sheet, int frameSize, int frameCount,
                                  float fps, float worldScale, float yOffset)
    {
        var go = new GameObject("StatusFX") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, worldScale);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = DepthSortRuntime.FxSlash;   // in front of entities
        go.AddComponent<Billboard>();
        var fx = go.AddComponent<StatusFX>();
        fx.follow = follow;
        fx.offset = new Vector3(0f, yOffset, 0f);
        fx.sr = sr;
        fx.fps = Mathf.Max(1f, fps);
        fx.frames = SpriteSheetCache.Strip(sheet, frameSize, frameCount);   // shared/read-only
        sr.enabled = false;
        return fx;
    }

    /// <summary>Reveal the indicator for <paramref name="seconds"/> (extends the timer if already shown).</summary>
    public void Show(float seconds)
    {
        if (frames == null || frames.Length == 0) return;
        endTime = Mathf.Max(endTime, Time.time + Mathf.Max(0f, seconds));
        if (!sr.enabled) { t = 0f; sr.enabled = true; }
    }

    void LateUpdate()
    {
        if (follow != null) transform.position = follow.position + offset;   // track the unit (after it moves)
        else { Destroy(gameObject); return; }                                // unit destroyed → clean up
        if (!sr.enabled) return;
        if (Time.time >= endTime) { sr.enabled = false; return; }
        t += Time.deltaTime * fps;
        sr.sprite = frames[(int)t % frames.Length];
    }
}
