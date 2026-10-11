using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A circle drawn as a line of a set thickness, about the centre of its own box and as wide as that box.
/// One mesh, rebuilt only when its box changes.
/// </summary>
public class CircleOutline : MaskableGraphic
{
    const int Segments = 72;
    float thickness = 2f;

    public static CircleOutline Build(Transform parent, float diameter, float thickness, Color color)
    {
        var go = new GameObject("CircleOutline", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var circle = go.AddComponent<CircleOutline>();
        circle.thickness = thickness;
        circle.color = color;
        circle.raycastTarget = false;
        ((RectTransform)go.transform).sizeDelta = new Vector2(diameter, diameter);
        return circle;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var box = rectTransform.rect;
        Vector2 centre = box.center;
        float outer = Mathf.Min(box.width, box.height) * 0.5f, inner = outer - thickness;
        for (int i = 0; i < Segments; i++)
        {
            float a = i * 2f * Mathf.PI / Segments;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            vh.AddVert(centre + dir * inner, color, Vector2.zero);
            vh.AddVert(centre + dir * outer, color, Vector2.zero);
        }
        for (int i = 0; i < Segments; i++)
        {
            int here = i * 2, next = (i + 1) % Segments * 2;
            vh.AddTriangle(here, here + 1, next + 1);
            vh.AddTriangle(here, next + 1, next);
        }
    }
}
