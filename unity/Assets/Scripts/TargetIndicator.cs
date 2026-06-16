using UnityEngine;

/// <summary>
/// A small gold down-arrow that bobs over the player's current attack target.
/// Built entirely in code (procedural sprite) and created on demand by
/// <see cref="PlayerAttacker"/>; call <see cref="Follow"/> with the target's
/// transform to move it, or null to hide it.
/// </summary>
public class TargetIndicator : MonoBehaviour
{
    [Tooltip("Height above the target's origin.")]
    public float headOffset = 0.85f;
    public float bobAmount = 0.1f;
    public float bobSpeed = 5f;

    Transform target;
    SpriteRenderer sr;

    public static TargetIndicator Create()
    {
        var go = new GameObject("TargetIndicator");
        var ind = go.AddComponent<TargetIndicator>();
        ind.sr = go.AddComponent<SpriteRenderer>();
        ind.sr.sprite = BuildArrow();
        ind.sr.sortingOrder = DepthSortRuntime.FxTarget;   // FX band: above all entities + overhead
        go.transform.localScale = Vector3.one * 0.6f;
        go.SetActive(false);
        return ind;
    }

    public void Follow(Transform t)
    {
        target = t;
        gameObject.SetActive(t != null);
    }

    void LateUpdate()
    {
        if (target == null) { if (gameObject.activeSelf) gameObject.SetActive(false); return; }
        float y = headOffset + Mathf.Sin(Time.time * bobSpeed) * bobAmount;
        transform.position = target.position + Vector3.up * y;
    }

    // A filled gold triangle pointing DOWN, with a 1px dark outline for contrast.
    static Sprite BuildArrow()
    {
        const int s = 16;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        var clear = new Color(0, 0, 0, 0);
        var gold  = new Color(1f, 0.85f, 0.2f, 1f);
        var dark  = new Color(0.15f, 0.1f, 0f, 1f);

        var px = new Color[s * s];
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        // y=0 is the bottom row in Unity textures → apex at the bottom = points down.
        int cx = s / 2;
        for (int y = 0; y < s; y++)
        {
            float t = y / (float)(s - 1);            // 0 at bottom apex, 1 at top base
            int half = Mathf.RoundToInt(t * (cx - 1));
            for (int x = cx - half; x <= cx + half; x++)
                px[y * s + x] = gold;
        }

        // cheap outline: any clear pixel touching a gold pixel becomes dark
        var outlined = (Color[])px.Clone();
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                if (px[y * s + x].a > 0f) continue;
                bool nearGold =
                    (x > 0     && px[y * s + x - 1].a > 0f) ||
                    (x < s - 1 && px[y * s + x + 1].a > 0f) ||
                    (y > 0     && px[(y - 1) * s + x].a > 0f) ||
                    (y < s - 1 && px[(y + 1) * s + x].a > 0f);
                if (nearGold) outlined[y * s + x] = dark;
            }

        tex.SetPixels(outlined);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }
}
