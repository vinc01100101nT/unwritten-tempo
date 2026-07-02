using UnityEngine;

/// <summary>
/// A looping shield bubble shown around the player while a guard/defensive stance is up.
/// Created once per player and reused: <see cref="Show"/> reveals it for a duration, then it hides
/// itself. It FOLLOWS the player transform (rather than parenting) so it doesn't inherit the player's
/// billboard/idle-breathe scale — its own world scale stays constant. Frames are sliced through
/// <see cref="SpriteSheetCache"/> (shared, zero re-slice) like the other FX.
/// </summary>
public class GuardShieldFX : MonoBehaviour
{
    Transform follow;
    Vector3 offset;
    SpriteRenderer sr;
    Sprite[] frames;
    float fps, endTime, t;

    /// <summary>Spawn a shield that tracks <paramref name="follow"/>. Starts hidden; call <see cref="Show"/>.</summary>
    public static GuardShieldFX Create(Transform follow, Texture2D sheet, int frameSize, int frameCount,
                                       float fps, float worldScale, float yOffset)
    {
        var go = new GameObject("GuardShield");
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, worldScale);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = DepthSortRuntime.FxSlash;   // FX band: in front of entities
        go.AddComponent<Billboard>();                 // face the camera in 2.5D
        var fx = go.AddComponent<GuardShieldFX>();
        fx.follow = follow;
        fx.offset = new Vector3(0f, yOffset, 0f);
        fx.sr = sr;
        fx.fps = Mathf.Max(1f, fps);
        fx.frames = SpriteSheetCache.Strip(sheet, frameSize, frameCount);   // shared/read-only
        sr.enabled = false;
        return fx;
    }

    /// <summary>Reveal the shield for <paramref name="seconds"/> (refreshes the timer if already up).</summary>
    public void Show(float seconds)
    {
        if (frames == null || frames.Length == 0) return;
        endTime = Time.time + Mathf.Max(0f, seconds);
        if (!sr.enabled) { t = 0f; sr.enabled = true; }
    }

    void LateUpdate()
    {
        if (follow != null) transform.position = follow.position + offset;   // track the player (after it moves)
        if (!sr.enabled) return;
        if (Time.time >= endTime) { sr.enabled = false; return; }
        t += Time.deltaTime * fps;
        sr.sprite = frames[(int)t % frames.Length];   // loop while the stance is up
    }
}
