using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The creation hexagon: the six primary stats on six corners, each pulled between 1 and 9 by dragging its corner
/// (or clicking its name to add one). Raising a stat lowers the one opposite it, so every pair keeps its total —
/// the rule and the pairs are <see cref="UnitStatMath"/>'s, never restated here. The chosen spread is the player's
/// CHOICE, sent with the creation and checked by the server; nothing here works out what it is worth.
///
/// Drawn as one mesh (grid, spokes, the filled shape and its outline) rebuilt only when a value changes.
/// </summary>
public class StatHexagon : MaskableGraphic, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    // Around the ring, clockwise from the top. Each corner's opposite is three places on, which puts every stat
    // across from its CreationOpposite — STR/INT, AGI/LUK, VIT/DEX.
    static readonly PrimaryStat[] Ring = { PrimaryStat.STR, PrimaryStat.AGI, PrimaryStat.VIT, PrimaryStat.INT, PrimaryStat.LUK, PrimaryStat.DEX };

    const float Radius = 80f, LabelRadius = 104f, KnobSize = 12f, LineWidth = 1f, OutlineWidth = 2f, GrabRadius = 18f;

    public event Action OnChanged;

    readonly int[] values = UnitStatMath.EvenCreationSpread();
    readonly Image[] knobs = new Image[6];
    readonly TMP_Text[] numbers = new TMP_Text[6];
    int dragging = -1;

    /// <summary>The spread, by <see cref="PrimaryStat"/> index — what the creation request carries.</summary>
    public int[] Spread => (int[])values.Clone();

    public static StatHexagon Build(Transform parent)
    {
        var go = new GameObject("StatHexagon", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var hex = go.AddComponent<StatHexagon>();
        hex.color = Color.white;
        ((RectTransform)go.transform).sizeDelta = new Vector2(LabelRadius * 2 + 40, LabelRadius * 2 + 30);

        for (int i = 0; i < 6; i++)
        {
            var stat = Ring[i];
            var knob = UiKit.Round(UiKit.Img("Knob_" + stat, go.transform, UiKit.Accent), UiKit.WindowRadius);
            knob.raycastTarget = false;
            knob.rectTransform.sizeDelta = new Vector2(KnobSize, KnobSize);
            hex.knobs[i] = knob;

            var label = UiKit.MakeText("Name_" + stat, go.transform, stat.ToString(), 12, FontStyle.Normal, TextAnchor.LowerCenter, null, false);
            label.rectTransform.sizeDelta = new Vector2(40, 16);
            label.rectTransform.anchoredPosition = Corner(i, LabelRadius) + new Vector2(0, 7);
            var btn = label.gameObject.AddComponent<Button>();
            btn.targetGraphic = label;
            int idx = i;
            btn.onClick.AddListener(() => hex.SetValue(idx, hex.values[(int)Ring[idx]] + 1));

            var num = UiKit.MakeText("Value_" + stat, go.transform, "", 12, FontStyle.Bold, TextAnchor.UpperCenter, null, false);
            UiKit.Paint(num, UiKit.TitleText);
            num.raycastTarget = false;
            num.rectTransform.sizeDelta = new Vector2(40, 16);
            num.rectTransform.anchoredPosition = Corner(i, LabelRadius) - new Vector2(0, 8);
            hex.numbers[i] = num;
        }
        hex.Redraw();
        return hex;
    }

    // Where corner i sits at a given distance from the centre, top first, clockwise.
    static Vector2 Corner(int i, float distance)
    {
        float a = Mathf.PI / 2f - i * Mathf.PI / 3f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * distance;
    }

    static Vector2 AtValue(int i, int value) => Corner(i, Radius * value / UnitStatMath.CreationMax);

    // Set the stat on corner i, and its opposite to what keeps the pair's total.
    void SetValue(int corner, int value)
    {
        int stat = (int)Ring[corner];
        value = Mathf.Clamp(value, UnitStatMath.CreationMin, UnitStatMath.CreationMax);
        if (values[stat] == value) return;
        values[stat] = value;
        values[UnitStatMath.CreationOpposite(stat)] = UnitStatMath.CreationPairTotal - value;
        Redraw();
        OnChanged?.Invoke();
    }

    void Redraw()
    {
        for (int i = 0; i < 6; i++)
        {
            int v = values[(int)Ring[i]];
            knobs[i].rectTransform.anchoredPosition = AtValue(i, v);
            numbers[i].text = v.ToString();
        }
        SetVerticesDirty();
    }

    // The grid and the shape are drawn in theme colours baked into the mesh, so a theme switch redraws it.
    protected override void OnEnable() { base.OnEnable(); UiKit.ThemeChanged += SetVerticesDirty; }
    protected override void OnDisable() { UiKit.ThemeChanged -= SetVerticesDirty; base.OnDisable(); }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        // Drawn about the box's CENTRE, where the knobs and labels (centre-anchored children) already sit — not
        // about the pivot, which whoever places the hexagon is free to move.
        Vector2 o = rectTransform.rect.center;
        // The grid: rings at a third, two thirds and the full reach, and a spoke to every corner.
        for (int r = 3; r <= UnitStatMath.CreationMax; r += 3)
            for (int i = 0; i < 6; i++)
                Line(vh, o + Corner(i, Radius * r / 9f), o + Corner((i + 1) % 6, Radius * r / 9f), LineWidth, UiKit.Rule.Value);
        for (int i = 0; i < 6; i++) Line(vh, o, o + Corner(i, Radius), LineWidth, UiKit.Rule.Value);

        // The chosen shape, filled, then outlined.
        int centre = vh.currentVertCount;
        vh.AddVert(o, UiKit.Hover.Value, Vector2.zero);
        for (int i = 0; i < 6; i++) vh.AddVert(o + AtValue(i, values[(int)Ring[i]]), UiKit.Hover.Value, Vector2.zero);
        for (int i = 0; i < 6; i++) vh.AddTriangle(centre, centre + 1 + i, centre + 1 + (i + 1) % 6);
        for (int i = 0; i < 6; i++)
            Line(vh, o + AtValue(i, values[(int)Ring[i]]), o + AtValue((i + 1) % 6, values[(int)Ring[(i + 1) % 6]]), OutlineWidth, UiKit.Accent.Value);
    }

    static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
    {
        var n = (b - a).normalized;
        var side = new Vector2(-n.y, n.x) * (width * 0.5f);
        int start = vh.currentVertCount;
        vh.AddVert(a - side, c, Vector2.zero);
        vh.AddVert(a + side, c, Vector2.zero);
        vh.AddVert(b + side, c, Vector2.zero);
        vh.AddVert(b - side, c, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    public void OnPointerDown(PointerEventData e)
    {
        dragging = -1;
        if (!ToLocal(e, out var p)) return;
        float best = GrabRadius * GrabRadius;
        for (int i = 0; i < 6; i++)
        {
            float d = (AtValue(i, values[(int)Ring[i]]) - p).sqrMagnitude;
            if (d < best) { best = d; dragging = i; }
        }
    }

    public void OnDrag(PointerEventData e)
    {
        if (dragging < 0 || !ToLocal(e, out var p)) return;
        // How far along its own spoke the pointer is, in stat steps.
        float along = Vector2.Dot(p, Corner(dragging, 1f));
        SetValue(dragging, Mathf.RoundToInt(along / Radius * UnitStatMath.CreationMax));
    }

    public void OnPointerUp(PointerEventData e) => dragging = -1;

    // The pointer, measured from the box's centre — the same origin the corners are drawn from.
    bool ToLocal(PointerEventData e, out Vector2 p)
    {
        bool hit = RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out p);
        p -= rectTransform.rect.center;
        return hit;
    }
}
