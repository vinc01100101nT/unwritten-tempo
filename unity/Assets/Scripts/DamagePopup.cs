using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A floating combat number that rises from a unit's head and fades out, then
/// removes itself. Call <see cref="Spawn"/> — it builds its own tiny world-space
/// canvas, so there's nothing to wire up in the scene. Used by <see cref="Health"/>
/// (monsters) and <see cref="MonsterAI"/> (the player) when damage is dealt.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class DamagePopup : MonoBehaviour
{
    [Tooltip("Seconds before it disappears.")]
    public float lifetime = 0.7f;
    [Tooltip("World units it floats upward per second.")]
    public float riseSpeed = 1.4f;

    const float BaseScale = 0.03f;   // shrinks UI pixels down into world units (art is 16 PPU)

    Text text;
    Color startColor;
    float age;

    /// <summary>Spawn a number at a world position (a head offset is added for you).</summary>
    public static void Spawn(Vector3 worldPos, int amount, Color color)
    {
        if (amount <= 0) return;
        SpawnText(worldPos, amount.ToString(), color);
    }

    /// <summary>Spawn arbitrary floating text (e.g. "+10" for heals).</summary>
    public static void SpawnText(Vector3 worldPos, string label, Color color)
    {
        if (string.IsNullOrEmpty(label)) return;

        var go = new GameObject("DamagePopup");
        // lift to the head, plus a little horizontal scatter so rapid hits don't stack exactly
        go.transform.position = worldPos + new Vector3(Random.Range(-0.15f, 0.15f), 0.5f, 0f);
        go.transform.localScale = Vector3.one * BaseScale;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DepthSortRuntime.FxPopup;   // FX band: above all entities + overhead
        ((RectTransform)go.transform).sizeDelta = new Vector2(100, 40);

        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 28;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.text = label;
        t.color = color;

        var outline = go.AddComponent<Outline>();         // dark edge so it reads on any background
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var popup = go.AddComponent<DamagePopup>();
        popup.text = t;
        popup.startColor = color;
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

        if (text != null)
        {
            var c = startColor;
            c.a = Mathf.Clamp01(1f - age / lifetime);     // fade out over its lifetime
            text.color = c;
        }

        // a quick "pop": briefly larger at spawn, then settle to the base size
        float pop = 1f + 0.4f * Mathf.Clamp01(1f - age * 6f);
        transform.localScale = Vector3.one * (BaseScale * pop);

        if (age >= lifetime) Destroy(gameObject);
    }
}
