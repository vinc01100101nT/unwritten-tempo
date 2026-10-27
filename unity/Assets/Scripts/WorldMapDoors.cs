using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE DOORS ON THE WORLD MAP — a blue square at each end of a door and a dashed line between the two, drawn over
/// the world picture from the atlas's copy of the world layout's doors (<see cref="WorldAtlas.links"/>), so the map
/// shows a door where the layout has it and nowhere else.
///
/// <para>One of these draws MANY doors as one mesh, either every dashed line or every square of a run of doors, so
/// the map costs a handful of drawn objects however many doors the world has. The mesh is filled when the picture
/// changes size, never per frame; zooming scales it with the picture.</para>
/// </summary>
public class WorldMapDoors : MaskableGraphic
{
    public enum Part { Lines, Squares }

    // Sizes in picture units (one unit is one metre of the world layout), as the world creator draws them.
    const float SquareHalf = 5f, SquareEdge = 2f, LineWidth = 2f, LitLineWidth = 4f, Dash = 7f, Gap = 6f;
    static readonly Color32 Blue = new Color32(0x2b, 0x6c, 0xf2, 0xff), Edge = new Color32(0xcf, 0xe0, 0xff, 0xff);
    // The route being walked, lit in the "you are here" marker's yellow, its squares rimmed in white.
    static readonly Color32 Lit = new Color32(0xff, 0xeb, 0x40, 0xff), LitEdge = new Color32(0xff, 0xff, 0xff, 0xff);

    /// <summary>The most corners one of these may be asked to draw — under what one screen mesh can hold.</summary>
    public const int MostCorners = 60000;

    Part part;
    bool lit;
    // Each door as its two ends on the picture: x,y is one end and z,w the other, as a share of the picture's width
    // from its left edge and of its height from its top edge.
    List<Vector4> doors;
    int from, to;
    float unitsWide = 1f, unitsHigh = 1f;

    /// <summary>Draw <paramref name="part"/> of doors <paramref name="from"/> up to (not including) <paramref name="to"/>
    /// of <paramref name="doors"/>, on a picture <paramref name="unitsWide"/> by <paramref name="unitsHigh"/> units.</summary>
    public void Show(Part part, List<Vector4> doors, int from, int to, float unitsWide, float unitsHigh, bool lit = false)
    {
        this.part = part;
        this.lit = lit;
        this.doors = doors;
        this.from = from;
        this.to = to;
        this.unitsWide = Mathf.Max(1f, unitsWide);
        this.unitsHigh = Mathf.Max(1f, unitsHigh);
        raycastTarget = false;
        SetVerticesDirty();
    }

    /// <summary>How many corners <paramref name="part"/> of one door takes, for splitting a long list into meshes.</summary>
    public static int CornersFor(Part part, Vector4 door, float unitsWide, float unitsHigh)
    {
        if (part == Part.Squares) return 16;
        float dx = (door.z - door.x) * unitsWide, dy = (door.w - door.y) * unitsHigh;
        return 4 * (Mathf.CeilToInt(Mathf.Sqrt(dx * dx + dy * dy) / (Dash + Gap)) + 1);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (doors == null) return;
        Rect r = rectTransform.rect;
        // The picture keeps the layout's proportions, so one scale serves both directions.
        float scale = r.width / unitsWide;
        for (int i = from; i < to && i < doors.Count; i++)
        {
            var d = doors[i];
            var a = new Vector2(r.xMin + d.x * r.width, r.yMax - d.y * r.height);
            var b = new Vector2(r.xMin + d.z * r.width, r.yMax - d.w * r.height);
            if (part == Part.Lines) Dashes(vh, a, b, scale, lit ? Lit : Blue, lit ? LitLineWidth : LineWidth);
            else { Square(vh, a, scale, lit); Square(vh, b, scale, lit); }
        }
    }

    static void Dashes(VertexHelper vh, Vector2 a, Vector2 b, float scale, Color32 colour, float width)
    {
        Vector2 along = b - a;
        float length = along.magnitude;
        if (length < 0.0001f) return;
        along /= length;
        Vector2 across = new Vector2(-along.y, along.x) * (width * scale * 0.5f);
        float dash = Dash * scale, step = (Dash + Gap) * scale;
        for (float at = 0f; at < length; at += step)
        {
            Vector2 p = a + along * at, q = a + along * Mathf.Min(at + dash, length);
            Quad(vh, p - across, p + across, q + across, q - across, colour);
        }
    }

    static void Square(VertexHelper vh, Vector2 c, float scale, bool lit)
    {
        Box(vh, c, (SquareHalf + SquareEdge) * scale, lit ? LitEdge : Edge);
        Box(vh, c, SquareHalf * scale, lit ? Lit : Blue);
    }

    static void Box(VertexHelper vh, Vector2 c, float half, Color32 colour)
        => Quad(vh, new Vector2(c.x - half, c.y - half), new Vector2(c.x - half, c.y + half),
                new Vector2(c.x + half, c.y + half), new Vector2(c.x + half, c.y - half), colour);

    static void Quad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color32 colour)
    {
        int at = vh.currentVertCount;
        vh.AddVert(p0, colour, Vector4.zero);
        vh.AddVert(p1, colour, Vector4.zero);
        vh.AddVert(p2, colour, Vector4.zero);
        vh.AddVert(p3, colour, Vector4.zero);
        vh.AddTriangle(at, at + 1, at + 2);
        vh.AddTriangle(at, at + 2, at + 3);
    }
}
