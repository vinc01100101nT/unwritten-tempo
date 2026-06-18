using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A combat test dummy: a huge-HP, immobile target that resets to full HP on a timer so you can
/// measure damage / DPS / build output without it ever dying. Pair it with an inert
/// <see cref="MonsterAI3D"/> (so it's targetable by click-attack and the caster) and a big
/// <see cref="Health"/>. Spawned by <c>Tools ▸ unwritten ▸ Spawn Training Dummy</c>.
/// Shows a floating "current / max" HP readout above its head.
/// </summary>
[RequireComponent(typeof(Health))]
public class TrainingDummy : MonoBehaviour
{
    [Tooltip("Seconds between full-HP resets.")]
    public float regenInterval = 5f;
    [Tooltip("Height (world units) of the HP readout above the dummy.")]
    public float labelHeight = 1.3f;

    Health health;
    Text label;
    float timer;
    int lastShownHP = int.MinValue;   // only rebuild the label string when HP actually changes

    void Awake()
    {
        health = GetComponent<Health>();
        BuildLabel();
    }

    void Update()
    {
        if (health == null) return;

        timer += Time.deltaTime;
        if (timer >= regenInterval)
        {
            timer = 0f;
            health.Revive();   // resets Current to maxHealth (and clears the hit-flash tint)
        }

        // Rebuild the readout only when HP changed — a per-frame string + canvas rebuild for a static
        // number is pure waste (this was the last steady combat allocator the profiler still showed).
        if (label != null && health.Current != lastShownHP)
        {
            lastShownHP = health.Current;
            label.text = $"TEST DUMMY\n{health.Current:N0} / {health.maxHealth:N0}";
        }
    }

    // A world-space HP readout above the head. Counter-scales for the dummy's billboard scale so the
    // text is a readable size regardless of how the dummy is scaled; Billboard keeps it facing the camera.
    void BuildLabel()
    {
        var go = new GameObject("DummyHP", typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, labelHeight, 0f);
        float worldScale = Mathf.Max(0.0001f, transform.lossyScale.x);
        go.transform.localScale = Vector3.one * (0.03f / worldScale);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DepthSortRuntime.FxPopup;
        ((RectTransform)go.transform).sizeDelta = new Vector2(260, 56);

        label = go.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 22;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.color = new Color(1f, 0.95f, 0.6f);

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        go.AddComponent<Billboard>();   // face the camera in 2.5D
    }
}
